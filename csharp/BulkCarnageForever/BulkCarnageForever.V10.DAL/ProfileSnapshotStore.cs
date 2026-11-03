using BulkCarnageForever.V10.Core.Export;
using Microsoft.EntityFrameworkCore;

namespace BulkCarnageForever.V10.DAL;

/// <summary>Reads a consistent, owner-scoped export without using mutable WPF caches.</summary>
public sealed class ProfileSnapshotStore(Func<AppDbContext> database)
{
    private readonly Func<AppDbContext> _database = database ?? throw new ArgumentNullException(nameof(database));

    public async Task<ProfileSnapshotData> ReadAsync(long userProfileId, CancellationToken ct = default)
    {
        if (userProfileId <= 0) throw new ArgumentException("An existing user profile is required.", nameof(userProfileId));
        await using var db = _database(); await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var profile = await db.UserProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userProfileId, ct)
            ?? throw new ArgumentException("An existing user profile is required.", nameof(userProfileId));
        var preferences = await db.UserPreferences.AsNoTracking().SingleOrDefaultAsync(x => x.UserProfileId == userProfileId, ct);
        var goals = await db.NutritionGoals.AsNoTracking().Where(x => x.UserProfileId == userProfileId).OrderBy(x => x.EffectiveFrom).ThenBy(x => x.Id).ToListAsync(ct);
        var meals = await db.MealEntries.AsNoTracking().Where(x => x.UserProfileId == userProfileId).OrderBy(x => x.Id).ToListAsync(ct);
        var weights = await db.WeightEntries.AsNoTracking().Where(x => x.UserProfileId == userProfileId).OrderBy(x => x.Date).ToListAsync(ct);
        var favorites = await db.FoodFavorites.AsNoTracking().Where(x => x.UserProfileId == userProfileId).OrderBy(x => x.FoodName).ToListAsync(ct);
        var ghosts = await db.GhostProfiles.AsNoTracking().Where(x => x.UserProfileId == userProfileId).OrderBy(x => x.Id).ToListAsync(ct);
        var mealIds = meals.Select(x => x.Id).ToList(); var ghostIds = ghosts.Select(x => x.Id).ToList();
        var simulations = await db.GhostMealSimulations.AsNoTracking().Where(x => x.UserProfileId == userProfileId &&
            mealIds.Contains(x.MealEntryId) && ghostIds.Contains(x.GhostProfileId)).OrderBy(x => x.Id).ToListAsync(ct);
        var names = meals.Select(x => x.FoodName).Concat(simulations.Select(x => x.FoodName)).Concat(favorites.Select(x => x.FoodName))
            .Distinct(StringComparer.Ordinal).ToList();
        var foods = await db.FoodItems.AsNoTracking().Where(x => names.Contains(x.RecipeName)).OrderBy(x => x.RecipeName).ToListAsync(ct);
        var snapshot = new ProfileSnapshotData(DateTimeOffset.UtcNow, profile, preferences, goals, foods, meals, weights, favorites, ghosts, simulations);
        await transaction.CommitAsync(ct);
        return snapshot;
    }
}
