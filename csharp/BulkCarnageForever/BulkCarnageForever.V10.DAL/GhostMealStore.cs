using BulkCarnageForever.V10.Core.Model;
using BulkCarnageForever.V10.Core.Simulation;
using Microsoft.EntityFrameworkCore;

namespace BulkCarnageForever.V10.DAL;

/// <summary>
/// Owns a short-lived context per operation. New meals and their initial scenarios
/// commit together; failed calculations or writes leave neither partially saved.
/// </summary>
public sealed class GhostMealStore(Func<AppDbContext> database)
{
    private readonly Func<AppDbContext> _database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<MealEntry> LogAsync(long userProfileId, MealEntry draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Id != 0 || userProfileId <= 0 || draft.UserProfileId != userProfileId)
            throw new ArgumentException("A new meal belonging to the selected user is required.", nameof(draft));
        // Capture the input before awaiting. The caller's diary object is never mutated.
        var meal = new MealEntry
        {
            UserProfileId = userProfileId, Date = draft.Date, MealType = draft.MealType,
            FoodName = draft.FoodName, AmountEaten = draft.AmountEaten
        };
        if (!Enum.IsDefined(meal.MealType) || string.IsNullOrWhiteSpace(meal.FoodName) ||
            !double.IsFinite(meal.AmountEaten) || meal.AmountEaten <= 0)
            throw new ArgumentException("A valid meal type, logged food, and finite positive quantity are required.", nameof(draft));

        await using var db = _database();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await new GhostProfileStore(db).EnsureDefaultAsync(userProfileId, ct);
        var food = await db.FoodItems.AsNoTracking().SingleOrDefaultAsync(x => x.RecipeName == meal.FoodName, ct)
            ?? throw new ArgumentException("The logged food is not in the local catalog.", nameof(draft));
        var ghosts = await db.GhostProfiles.AsNoTracking().Where(x => x.UserProfileId == userProfileId).ToListAsync(ct);

        db.MealEntries.Add(meal);
        await db.SaveChangesAsync(ct);
        foreach (var ghost in ghosts)
        {
            var calculation = GhostCalculator.Calculate(ghost, meal, food);
            db.GhostMealSimulations.Add(ApplySnapshot(new GhostMealSimulation(), calculation, food));
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return meal;
    }

    public async Task<bool> RemoveAsync(long userProfileId, long mealEntryId, CancellationToken ct = default)
    {
        await using var db = _database();
        await RequireOwnerAsync(db, userProfileId, ct);
        // The owner-qualified meal foreign key cascades to its simulations in the same
        // SQLite statement, including when a meal is removed directly through EF.
        return await db.MealEntries.Where(x => x.UserProfileId == userProfileId && x.Id == mealEntryId)
            .ExecuteDeleteAsync(ct) > 0;
    }

    /// <summary>
    /// Returns saved snapshots in their saved date range, with source corrections flagged.
    /// This read never regenerates historical values or creates missing scenarios.
    /// </summary>
    public async Task<List<GhostSimulationComparison>> ListAsync(long userProfileId, Guid ghostProfileId,
        DateOnly? from = null, DateOnly? through = null, CancellationToken ct = default)
    {
        if (from.HasValue && through.HasValue && from > through)
            throw new ArgumentException("The end date must be on or after the start date.", nameof(through));
        await using var db = _database();
        await RequireOwnerAsync(db, userProfileId, ct);
        if (!await db.GhostProfiles.AnyAsync(x => x.Id == ghostProfileId && x.UserProfileId == userProfileId, ct))
            throw new KeyNotFoundException("The Ghost was not found for this user.");
        var snapshots = db.GhostMealSimulations.AsNoTracking()
            .Where(x => x.UserProfileId == userProfileId && x.GhostProfileId == ghostProfileId);
        if (from.HasValue) snapshots = snapshots.Where(x => x.Date >= from.Value);
        if (through.HasValue) snapshots = snapshots.Where(x => x.Date <= through.Value);

        return await (from snapshot in snapshots
                      join meal in db.MealEntries.AsNoTracking().Where(x => x.UserProfileId == userProfileId)
                          on snapshot.MealEntryId equals meal.Id
                      orderby snapshot.Date, snapshot.MealEntryId
                      select new GhostSimulationComparison(snapshot,
                          snapshot.SourceAmount != meal.AmountEaten || snapshot.Date != meal.Date ||
                          snapshot.MealType != meal.MealType || snapshot.FoodName != meal.FoodName))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Overrides one saved scenario using its captured food basis. Null resets to its
    /// saved source quantity and applied multiplier, without reading newer weekly/catalog settings.
    /// Source-correction status is unaffected until an explicit recalculation.
    /// </summary>
    public async Task SetServingOverrideAsync(long userProfileId, long simulationId, double? servings,
        CancellationToken ct = default)
    {
        await using var db = _database();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await RequireOwnerAsync(db, userProfileId, ct);
        var snapshot = await db.GhostMealSimulations.SingleOrDefaultAsync(
            x => x.Id == simulationId && x.UserProfileId == userProfileId, ct)
            ?? throw new KeyNotFoundException("The saved scenario was not found for this user.");
        var savedGhost = new GhostProfile
        {
            Id = snapshot.GhostProfileId, UserProfileId = snapshot.UserProfileId,
            MondayMultiplier = snapshot.AppliedMultiplier, TuesdayMultiplier = snapshot.AppliedMultiplier,
            WednesdayMultiplier = snapshot.AppliedMultiplier, ThursdayMultiplier = snapshot.AppliedMultiplier,
            FridayMultiplier = snapshot.AppliedMultiplier, SaturdayMultiplier = snapshot.AppliedMultiplier,
            SundayMultiplier = snapshot.AppliedMultiplier
        };
        var savedMeal = new MealEntry
        {
            Id = snapshot.MealEntryId, UserProfileId = snapshot.UserProfileId, Date = snapshot.Date,
            MealType = snapshot.MealType, FoodName = snapshot.FoodName, AmountEaten = snapshot.SourceAmount
        };
        var savedFood = new FoodItem
        {
            RecipeName = snapshot.FoodName, MeasurementServings = snapshot.MeasurementServings,
            MeasurementType = snapshot.MeasurementType, CaloriesPerServing = snapshot.CaloriesPerServing,
            Protein = snapshot.ProteinPerServing, Carbs = snapshot.CarbsPerServing, Fats = snapshot.FatsPerServing
        };
        var calculation = GhostCalculator.Calculate(savedGhost, savedMeal, savedFood, servings);
        ApplyPortion(snapshot, calculation);
        snapshot.OverrideServings = servings;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Explicitly refreshes one Ghost from current logged meals, weekly ratios and catalog data.
    /// The inclusive range matches either a meal's current date or that Ghost's saved date,
    /// so date corrections can be refreshed from either side. Missing scenarios are created
    /// only for matched, existing meals. Returns the number of meals processed.
    /// </summary>
    /// <param name="preserveOverrides">Keep absolute overridden serving counts by default, even if the source food changed.</param>
    public async Task<int> RecalculateAsync(long userProfileId, Guid ghostProfileId, DateOnly from, DateOnly through,
        bool preserveOverrides = true, CancellationToken ct = default)
    {
        if (from > through) throw new ArgumentException("The end date must be on or after the start date.", nameof(through));
        await using var db = _database();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await RequireOwnerAsync(db, userProfileId, ct);
        var ghost = await db.GhostProfiles.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == ghostProfileId && x.UserProfileId == userProfileId, ct)
            ?? throw new KeyNotFoundException("The Ghost was not found for this user.");
        var meals = await db.MealEntries.AsNoTracking().Where(meal => meal.UserProfileId == userProfileId &&
            ((meal.Date >= from && meal.Date <= through) || db.GhostMealSimulations.Any(snapshot =>
                snapshot.UserProfileId == userProfileId && snapshot.GhostProfileId == ghostProfileId &&
                snapshot.MealEntryId == meal.Id && snapshot.Date >= from && snapshot.Date <= through)))
            .OrderBy(x => x.Date).ThenBy(x => x.Id).ToListAsync(ct);
        var mealIds = meals.Select(x => x.Id).ToList();
        var existing = await db.GhostMealSimulations.Where(x => x.UserProfileId == userProfileId &&
            x.GhostProfileId == ghostProfileId && mealIds.Contains(x.MealEntryId))
            .ToDictionaryAsync(x => x.MealEntryId, ct);
        var foods = new Dictionary<string, FoodItem>(StringComparer.Ordinal);
        var recalculatedAt = DateTimeOffset.UtcNow;
        foreach (var meal in meals)
        {
            if (!foods.TryGetValue(meal.FoodName, out var food))
            {
                food = await db.FoodItems.AsNoTracking().SingleOrDefaultAsync(x => x.RecipeName == meal.FoodName, ct)
                    ?? throw new ArgumentException("A selected meal's food is not in the local catalog.");
                foods.Add(meal.FoodName, food);
            }
            existing.TryGetValue(meal.Id, out var snapshot);
            double? servings = preserveOverrides ? snapshot?.OverrideServings : null;
            var calculation = GhostCalculator.Calculate(ghost, meal, food, servings);
            if (snapshot is null)
            {
                snapshot = new GhostMealSimulation();
                db.GhostMealSimulations.Add(snapshot);
            }
            ApplySnapshot(snapshot, calculation, food);
            snapshot.OverrideServings = servings;
            snapshot.RecalculatedAt = recalculatedAt;
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return meals.Count;
    }

    private static async Task RequireOwnerAsync(AppDbContext db, long userProfileId, CancellationToken ct)
    {
        if (userProfileId <= 0 || !await db.UserProfiles.AnyAsync(x => x.Id == userProfileId, ct))
            throw new ArgumentException("An existing user profile is required.", nameof(userProfileId));
    }

    /// <summary>
    /// Reads recorded nutrition and saved Ghost nutrition in one consistent transaction.
    /// Source/snapshot context outside the range is included only to detect moved dates.
    /// No scenarios are generated, recalculated, or saved by this read.
    /// </summary>
    public async Task<List<GhostDayNutrition>> CompareAsync(long userProfileId, Guid ghostProfileId,
        DateOnly from, DateOnly through, CancellationToken ct = default)
    {
        if (from > through) throw new ArgumentException("The end date must be on or after the start date.", nameof(through));
        await using var db = _database();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await RequireOwnerAsync(db, userProfileId, ct);
        if (!await db.GhostProfiles.AnyAsync(x => x.Id == ghostProfileId && x.UserProfileId == userProfileId, ct))
            throw new KeyNotFoundException("The Ghost was not found for this user.");
        var saved = await db.GhostMealSimulations.AsNoTracking().Where(snapshot => snapshot.UserProfileId == userProfileId &&
            snapshot.GhostProfileId == ghostProfileId && ((snapshot.Date >= from && snapshot.Date <= through) ||
                db.MealEntries.Any(meal => meal.Id == snapshot.MealEntryId && meal.UserProfileId == userProfileId && meal.Date >= from && meal.Date <= through)))
            .ToListAsync(ct);
        var ids = saved.Select(x => x.MealEntryId).ToList();
        var meals = await db.MealEntries.AsNoTracking().Where(x => x.UserProfileId == userProfileId &&
            ((x.Date >= from && x.Date <= through) || ids.Contains(x.Id))).ToListAsync(ct);
        var names = meals.Select(x => x.FoodName).Distinct().ToList();
        var catalog = await db.FoodItems.AsNoTracking().Where(x => names.Contains(x.RecipeName)).ToDictionaryAsync(x => x.RecipeName, ct);
        var result = GhostNutritionComparison.Build(userProfileId, ghostProfileId, from, through, meals, catalog, saved);
        await transaction.CommitAsync(ct);
        return result;
    }

    private static GhostMealSimulation ApplySnapshot(GhostMealSimulation snapshot, GhostCalculation result, FoodItem food)
    {
        snapshot.GhostProfileId = result.GhostProfileId;
        snapshot.MealEntryId = result.MealEntryId;
        snapshot.UserProfileId = result.UserProfileId;
        snapshot.Date = result.Date;
        snapshot.MealType = result.MealType;
        snapshot.FoodName = result.FoodName;
        snapshot.SourceAmount = result.ActualAmount;
        snapshot.AppliedMultiplier = result.AppliedMultiplier;
        snapshot.MeasurementServings = food.MeasurementServings;
        snapshot.MeasurementType = food.MeasurementType;
        snapshot.CaloriesPerServing = food.CaloriesPerServing;
        snapshot.ProteinPerServing = food.Protein;
        snapshot.CarbsPerServing = food.Carbs;
        snapshot.FatsPerServing = food.Fats;
        ApplyPortion(snapshot, result);
        return snapshot;
    }

    private static void ApplyPortion(GhostMealSimulation snapshot, GhostCalculation result)
    {
        snapshot.SimulatedAmount = result.SimulatedAmount;
        snapshot.SimulatedServings = result.SimulatedServings;
        snapshot.Calories = result.Nutrition.Calories;
        snapshot.Protein = result.Nutrition.Protein;
        snapshot.Carbs = result.Nutrition.Carbs;
        snapshot.Fats = result.Nutrition.Fats;
    }
}
