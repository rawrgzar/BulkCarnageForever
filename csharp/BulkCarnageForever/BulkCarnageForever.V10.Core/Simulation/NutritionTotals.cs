namespace BulkCarnageForever.V10.Core.Simulation;

/// <summary>Calories in kcal and macronutrients in grams, without display rounding.</summary>
public sealed record NutritionTotals(double Calories, double Protein, double Carbs, double Fats);
