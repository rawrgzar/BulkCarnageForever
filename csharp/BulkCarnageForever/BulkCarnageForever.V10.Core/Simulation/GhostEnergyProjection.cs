using BulkCarnageForever.V10.Core.Model;

namespace BulkCarnageForever.V10.Core.Simulation;

public sealed record GhostEnergyBalance(double? ActualCalories, double? GhostCalories, double? EstimatedMaintenanceCalories,
    double? ActualBalance, double? GhostBalance, double? GhostMinusActual);
public sealed record GhostProjectedEnergyDay(DateOnly Date, DateOnly ReferenceDate, GhostEnergyBalance Energy);
public sealed record GhostProjectionTotals(double? ActualCalories, double? GhostCalories, double? ActualBalance,
    double? GhostBalance, double? GhostMinusActual, int KnownActualDays, int KnownGhostDays, int KnownPairDays);
public sealed record GhostProjectionResult(DateOnly ReferenceWeekStart, DateOnly StartsOn, int HorizonDays,
    double? EstimatedMaintenanceCalories, IReadOnlyList<GhostProjectedEnergyDay> Days, GhostProjectionTotals Totals)
{
    public string Assumption => "Repeats the explicitly supplied reference week's complete calorie totals by weekday; estimated maintenance is held constant when supplied.";
}

/// <summary>
/// Energy-only foundation for a future projection view. The caller explicitly supplies
/// a reference week and estimated maintenance. Incomplete days remain unknown.
/// No physiological or body-weight prediction is performed.
/// </summary>
public static class GhostEnergyProjection
{
    public static double? MaintenanceFor(long userProfileId, DateOnly date, IEnumerable<NutritionGoal> goals)
    {
        if (userProfileId <= 0) throw new ArgumentException("A positive user profile ID is required.", nameof(userProfileId));
        ArgumentNullException.ThrowIfNull(goals);
        var goal = goals.Where(x => x.UserProfileId == userProfileId && x.EffectiveFrom <= date)
            .OrderBy(x => x.EffectiveFrom).ThenBy(x => x.Id).LastOrDefault();
        // Existing goals use zero when no maintenance estimate has been saved.
        if (goal is null || goal.Tdee == 0) return null;
        ValidateMaintenance(goal.Tdee);
        return goal.Tdee;
    }

    public static GhostEnergyBalance CompareObserved(GhostDayNutrition day, double? estimatedMaintenanceCalories)
    {
        ArgumentNullException.ThrowIfNull(day); ArgumentNullException.ThrowIfNull(day.Totals);
        ValidateMaintenance(estimatedMaintenanceCalories);
        double? actual = day.Totals.ActualComplete ? Calories(day.Totals.Actual.Calories) : null;
        double? ghost = day.Totals.SimulatedComplete ? Calories(day.Totals.Simulated.Calories) : null;
        return new GhostEnergyBalance(actual, ghost, estimatedMaintenanceCalories,
            actual.HasValue && estimatedMaintenanceCalories.HasValue ? actual.Value - estimatedMaintenanceCalories.Value : null,
            ghost.HasValue && estimatedMaintenanceCalories.HasValue ? ghost.Value - estimatedMaintenanceCalories.Value : null,
            actual.HasValue && ghost.HasValue ? ghost.Value - actual.Value : null);
    }

    public static GhostProjectionResult Project(IReadOnlyList<GhostDayNutrition> referenceWeek, DateOnly startsOn,
        int horizonDays, double? estimatedMaintenanceCalories)
    {
        ArgumentNullException.ThrowIfNull(referenceWeek);
        if (horizonDays is not (5 or 30 or 90)) throw new ArgumentOutOfRangeException(nameof(horizonDays), "Choose 5, 30, or 90 days.");
        if (horizonDays - 1 > DateOnly.MaxValue.DayNumber - startsOn.DayNumber)
            throw new ArgumentOutOfRangeException(nameof(startsOn), "The projection extends beyond the supported calendar.");
        ValidateMaintenance(estimatedMaintenanceCalories);
        if (referenceWeek.Count != 7 || referenceWeek.Any(x => x is null))
            throw new ArgumentException("Supply one complete Monday–Sunday reference week, including unknown days.", nameof(referenceWeek));
        var week = referenceWeek.OrderBy(x => x.Date).ToArray();
        if (week[0].Date.DayOfWeek != DayOfWeek.Monday || week.Where((x, index) => x.Date.DayNumber != week[0].Date.DayNumber + index).Any())
            throw new ArgumentException("Reference dates must form one consecutive Monday–Sunday week.", nameof(referenceWeek));
        var energy = week.Select(x => CompareObserved(x, estimatedMaintenanceCalories)).ToArray();
        var days = new List<GhostProjectedEnergyDay>(horizonDays);
        for (int i = 0; i < horizonDays; i++)
        {
            var date = startsOn.AddDays(i); int index = ((int)date.DayOfWeek + 6) % 7;
            days.Add(new GhostProjectedEnergyDay(date, week[index].Date, energy[index]));
        }
        var totals = new GhostProjectionTotals(
            SumComplete(days.Select(x => x.Energy.ActualCalories)), SumComplete(days.Select(x => x.Energy.GhostCalories)),
            SumComplete(days.Select(x => x.Energy.ActualBalance)), SumComplete(days.Select(x => x.Energy.GhostBalance)),
            SumComplete(days.Select(x => x.Energy.GhostMinusActual)), days.Count(x => x.Energy.ActualCalories.HasValue),
            days.Count(x => x.Energy.GhostCalories.HasValue), days.Count(x => x.Energy.GhostMinusActual.HasValue));
        return new GhostProjectionResult(week[0].Date, startsOn, horizonDays, estimatedMaintenanceCalories, days, totals);
    }

    private static void ValidateMaintenance(double? value)
    {
        if (value.HasValue && (!double.IsFinite(value.Value) || value.Value <= 0))
            throw new ArgumentOutOfRangeException(nameof(value), "Estimated maintenance must be finite and positive, or unknown.");
    }
    private static double Calories(double value)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Complete calorie totals must be finite and nonnegative.");
        return value;
    }
    private static double? SumComplete(IEnumerable<double?> values)
    {
        var all = values.ToArray(); if (all.Any(x => !x.HasValue)) return null;
        double total = 0;
        foreach (var value in all)
        {
            total += value!.Value;
            if (!double.IsFinite(total)) throw new ArgumentOutOfRangeException(nameof(values), "The projection exceeds the supported numeric range.");
        }
        return total;
    }
}
