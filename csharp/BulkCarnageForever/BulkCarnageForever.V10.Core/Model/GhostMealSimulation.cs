namespace BulkCarnageForever.V10.Core.Model;

/// <summary>
/// Saved scenario for one logged meal. Quantities use the captured food's native units.
/// Settings/catalog changes never rewrite these values implicitly.
/// </summary>
public class GhostMealSimulation
{
    public long Id { get; set; }
    public Guid GhostProfileId { get; set; }
    public long MealEntryId { get; set; }
    public long UserProfileId { get; set; }
    public DateOnly Date { get; set; }
    public MealType MealType { get; set; }
    public string FoodName { get; set; } = string.Empty;
    public double SourceAmount { get; set; }
    public double AppliedMultiplier { get; set; }
    public double SimulatedAmount { get; set; }
    public double SimulatedServings { get; set; }
    // Null uses the saved source quantity and applied multiplier. Zero is a valid override.
    public double? OverrideServings { get; set; }

    // Capture the calculation basis so future explicit recalculation/overrides can
    // distinguish saved nutrition from a later version of the food catalog.
    public double MeasurementServings { get; set; }
    public string? MeasurementType { get; set; }
    public double CaloriesPerServing { get; set; }
    public double ProteinPerServing { get; set; }
    public double CarbsPerServing { get; set; }
    public double FatsPerServing { get; set; }
    public double Calories { get; set; }
    public double Protein { get; set; }
    public double Carbs { get; set; }
    public double Fats { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RecalculatedAt { get; set; }
}
