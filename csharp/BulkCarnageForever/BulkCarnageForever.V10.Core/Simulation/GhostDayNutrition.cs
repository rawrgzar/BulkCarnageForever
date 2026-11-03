using BulkCarnageForever.V10.Core.Model;

namespace BulkCarnageForever.V10.Core.Simulation;

/// <summary>Known totals plus coverage. An empty or incomplete group is not a known zero.</summary>
public sealed record GhostNutritionSummary(NutritionTotals Actual, NutritionTotals Simulated,
    int ActualEntries, int SimulatedEntries, int UnknownActualEntries, int UnknownSimulatedEntries,
    int MissingSimulations, int StaleSimulations)
{
    public bool ActualComplete => ActualEntries > 0 && UnknownActualEntries == 0;
    public bool SimulatedComplete => ActualEntries > 0 && SimulatedEntries == ActualEntries &&
        UnknownSimulatedEntries == 0 && MissingSimulations == 0 && StaleSimulations == 0;
}

public sealed record GhostMealNutrition(MealType MealType, GhostNutritionSummary Totals);
public sealed record GhostDayNutrition(DateOnly Date, GhostNutritionSummary Totals, IReadOnlyList<GhostMealNutrition> Meals);
