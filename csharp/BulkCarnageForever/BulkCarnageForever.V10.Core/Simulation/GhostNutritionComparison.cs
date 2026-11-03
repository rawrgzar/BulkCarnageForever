using BulkCarnageForever.V10.Core.Model;

namespace BulkCarnageForever.V10.Core.Simulation;

/// <summary>Read-only aggregation of recorded meals and saved scenarios; never generates history.</summary>
public static class GhostNutritionComparison
{
    public static List<GhostDayNutrition> Build(long userProfileId, Guid ghostProfileId, DateOnly from, DateOnly through,
        IEnumerable<MealEntry> actual, IReadOnlyDictionary<string, FoodItem> catalog, IEnumerable<GhostMealSimulation> simulations)
    {
        if (userProfileId <= 0) throw new ArgumentException("A positive user profile ID is required.", nameof(userProfileId));
        if (ghostProfileId == Guid.Empty) throw new ArgumentException("A Ghost identifier is required.", nameof(ghostProfileId));
        if (from > through) throw new ArgumentException("The end date must be on or after the start date.", nameof(through));
        ArgumentNullException.ThrowIfNull(actual); ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(simulations);
        var meals = actual.Where(x => x.UserProfileId == userProfileId).OrderBy(x => x.Id).ToDictionary(x => x.Id);
        // Defensive source matching also prevents an orphan snapshot from contributing food.
        var saved = simulations.Where(x => x.UserProfileId == userProfileId && x.GhostProfileId == ghostProfileId && meals.ContainsKey(x.MealEntryId))
            .OrderBy(x => x.MealEntryId).ToDictionary(x => x.MealEntryId);
        var days = new List<GhostDayNutrition>();
        for (int number = from.DayNumber; number <= through.DayNumber; number++)
        {
            var day = DateOnly.FromDayNumber(number);
            days.Add(new GhostDayNutrition(day, Summarize(day, null), Enum.GetValues<MealType>()
                .Select(type => new GhostMealNutrition(type, Summarize(day, type))).ToArray()));
        }
        return days;

        GhostNutritionSummary Summarize(DateOnly day, MealType? type)
        {
            bool Match(DateOnly date, MealType mealType) => date == day && (!type.HasValue || type.Value == mealType);
            var recorded = meals.Values.Where(x => Match(x.Date, x.MealType)).ToList();
            var snapshots = saved.Values.Where(x => Match(x.Date, x.MealType)).ToList();
            var actualTotal = new NutritionTotals(0, 0, 0, 0);
            var ghostTotal = new NutritionTotals(0, 0, 0, 0);
            int actualUnknown = 0, ghostUnknown = 0;
            foreach (var meal in recorded)
            {
                if (!catalog.TryGetValue(meal.FoodName, out var food) || food.RecipeName != meal.FoodName || !Enum.IsDefined(meal.MealType) ||
                    !Positive(meal.AmountEaten) || !Positive(food.MeasurementServings)) { actualUnknown++; continue; }
                var basis = new NutritionTotals(food.CaloriesPerServing, food.Protein, food.Carbs, food.Fats);
                double servings = meal.AmountEaten / food.MeasurementServings;
                if (!Valid(basis) || !Nonnegative(servings) || !TryAdd(ref actualTotal, new NutritionTotals(
                    basis.Calories * servings, basis.Protein * servings, basis.Carbs * servings, basis.Fats * servings))) actualUnknown++;
            }
            foreach (var snapshot in snapshots)
            {
                if (!Enum.IsDefined(snapshot.MealType) || !Positive(snapshot.SourceAmount) || !Positive(snapshot.MeasurementServings) ||
                    !Nonnegative(snapshot.AppliedMultiplier) || !Nonnegative(snapshot.SimulatedAmount) || !Nonnegative(snapshot.SimulatedServings) ||
                    (snapshot.OverrideServings.HasValue && !Nonnegative(snapshot.OverrideServings.Value)) ||
                    !Valid(new NutritionTotals(snapshot.CaloriesPerServing, snapshot.ProteinPerServing, snapshot.CarbsPerServing, snapshot.FatsPerServing)) ||
                    !TryAdd(ref ghostTotal, new NutritionTotals(snapshot.Calories, snapshot.Protein, snapshot.Carbs, snapshot.Fats))) ghostUnknown++;
            }
            int missing = recorded.Count(x => !saved.ContainsKey(x.Id));
            int stale = saved.Values.Count(snapshot =>
            {
                var meal = meals[snapshot.MealEntryId];
                return (Match(snapshot.Date, snapshot.MealType) || Match(meal.Date, meal.MealType)) &&
                    (snapshot.SourceAmount != meal.AmountEaten || snapshot.Date != meal.Date || snapshot.MealType != meal.MealType || snapshot.FoodName != meal.FoodName);
            });
            return new GhostNutritionSummary(actualTotal, ghostTotal, recorded.Count, snapshots.Count, actualUnknown, ghostUnknown, missing, stale);
        }
    }

    private static bool TryAdd(ref NutritionTotals total, NutritionTotals amount)
    {
        if (!Valid(amount)) return false;
        var combined = new NutritionTotals(total.Calories + amount.Calories, total.Protein + amount.Protein, total.Carbs + amount.Carbs, total.Fats + amount.Fats);
        if (!Valid(combined)) return false;
        total = combined; return true;
    }
    private static bool Valid(NutritionTotals totals) => Nonnegative(totals.Calories) && Nonnegative(totals.Protein) && Nonnegative(totals.Carbs) && Nonnegative(totals.Fats);
    private static bool Nonnegative(double value) => double.IsFinite(value) && value >= 0;
    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
}
