namespace BulkCarnageForever.V10.Core.Model
{
    public class NutritionGoal
    {
        public long Id { get; set; }
        public long UserProfileId { get; set; }

        // The latest goal on or before a journal date applies to that date.
        public DateOnly EffectiveFrom { get; set; }
            = DateOnly.FromDateTime(DateTime.Today);

        public GoalType Goal { get; set; } = GoalType.Custom;
        public TdeeMethod TdeeMethod { get; set; } = TdeeMethod.Manual;

        public double Tdee { get; set; }
        public double CalorieAdjustment { get; set; }
        public double Calories { get; set; }

        // Daily macro targets in grams.
        public double Protein { get; set; }
        public double Carbs { get; set; }
        public double Fats { get; set; }

        // Inputs used for this estimate; leave null for a manual TDEE.
        public int? AgeYears { get; set; }
        public double? WeightKg { get; set; }
        public double? HeightCm { get; set; }
        public bool? IsMale { get; set; }
        public double? ActivityFactor { get; set; }
    }
}
