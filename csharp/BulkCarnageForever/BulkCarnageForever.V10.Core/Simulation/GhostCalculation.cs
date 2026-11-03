using BulkCarnageForever.V10.Core.Model;

namespace BulkCarnageForever.V10.Core.Simulation;

/// <summary>
/// Immutable result of one calculation. Amounts use the source food's native units;
/// SimulatedServings uses its MeasurementServings conversion. No source objects are retained.
/// Persistence and explicit historical recalculation are handled separately.
/// </summary>
public sealed record GhostCalculation(
    Guid GhostProfileId,
    long MealEntryId,
    long UserProfileId,
    DateOnly Date,
    MealType MealType,
    string FoodName,
    double AppliedMultiplier,
    double ActualAmount,
    double SimulatedAmount,
    double SimulatedServings,
    NutritionTotals Nutrition);
