using BulkCarnageForever.V10.Core.Model;

namespace BulkCarnageForever.V10.Core.Simulation;

/// <summary>Deterministic portion scaling; does not read, save, or mutate diary records.</summary>
public static class GhostCalculator
{
    /// <param name="overrideServings">An optional absolute serving count; null uses the weekday ratio.</param>
    public static GhostCalculation Calculate(GhostProfile ghost, MealEntry meal, FoodItem food, double? overrideServings = null)
    {
        ArgumentNullException.ThrowIfNull(ghost);
        ArgumentNullException.ThrowIfNull(meal);
        ArgumentNullException.ThrowIfNull(food);

        if (ghost.UserProfileId <= 0 || ghost.UserProfileId != meal.UserProfileId)
            throw new ArgumentException("The Ghost and meal must share a positive user profile ID.", nameof(meal));
        if (string.IsNullOrWhiteSpace(meal.FoodName) ||
            !string.Equals(meal.FoodName, food.RecipeName, StringComparison.Ordinal))
            throw new ArgumentException("The food must match the meal's logged catalog key.", nameof(food));

        RequirePositive(meal.AmountEaten, nameof(meal.AmountEaten));
        RequirePositive(food.MeasurementServings, nameof(food.MeasurementServings));
        RequireNonnegative(food.CaloriesPerServing, nameof(food.CaloriesPerServing));
        RequireNonnegative(food.Protein, nameof(food.Protein));
        RequireNonnegative(food.Carbs, nameof(food.Carbs));
        RequireNonnegative(food.Fats, nameof(food.Fats));

        double multiplier = ghost.GetMultiplier(meal.Date.DayOfWeek);
        RequireNonnegative(multiplier, nameof(multiplier));
        double amount = meal.AmountEaten * multiplier;
        RequireNonnegative(amount, nameof(amount));
        double servings = amount / food.MeasurementServings;
        RequireNonnegative(servings, nameof(servings));
        if (overrideServings.HasValue)
        {
            RequireNonnegative(overrideServings.Value, nameof(overrideServings));
            servings = overrideServings.Value;
            amount = servings * food.MeasurementServings;
            RequireNonnegative(amount, nameof(amount));
        }
        var nutrition = new NutritionTotals(
            Scale(food.CaloriesPerServing, servings),
            Scale(food.Protein, servings),
            Scale(food.Carbs, servings),
            Scale(food.Fats, servings));

        return new GhostCalculation(ghost.Id, meal.Id, meal.UserProfileId, meal.Date,
            meal.MealType, meal.FoodName, multiplier, meal.AmountEaten, amount, servings, nutrition);
    }

    private static double Scale(double perServing, double servings)
    {
        double value = perServing * servings;
        RequireNonnegative(value, nameof(value));
        return value;
    }

    private static void RequirePositive(double value, string parameter)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(parameter, "A finite value greater than zero is required.");
    }

    private static void RequireNonnegative(double value, string parameter)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(parameter, "A finite, nonnegative value is required.");
    }
}
