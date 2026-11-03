namespace BulkCarnageForever.V10.Core.Model;

/// <summary>A user-owned portion scenario, separate from the recorded diary.</summary>
public class GhostProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long UserProfileId { get; set; }
    public string Name { get; set; } = "Ghost";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsDefault { get; set; }
    public string? VisualIdentity { get; set; }

    // Ratios, not percentages: 1 = 100%, 0.8 = 80%. Zero is allowed.
    // Validate edited schedules before saving; the calculator validates its selected ratio.
    public double MondayMultiplier { get; set; } = 1;
    public double TuesdayMultiplier { get; set; } = 1;
    public double WednesdayMultiplier { get; set; } = 1;
    public double ThursdayMultiplier { get; set; } = 1;
    public double FridayMultiplier { get; set; } = 1;
    public double SaturdayMultiplier { get; set; } = 1;
    public double SundayMultiplier { get; set; } = 1;

    public double GetMultiplier(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => MondayMultiplier,
        DayOfWeek.Tuesday => TuesdayMultiplier,
        DayOfWeek.Wednesday => WednesdayMultiplier,
        DayOfWeek.Thursday => ThursdayMultiplier,
        DayOfWeek.Friday => FridayMultiplier,
        DayOfWeek.Saturday => SaturdayMultiplier,
        DayOfWeek.Sunday => SundayMultiplier,
        _ => throw new ArgumentOutOfRangeException(nameof(day))
    };
}
