using System.ComponentModel.DataAnnotations;

namespace BulkCarnageForever.V10.Core.Model
{
    public class UserPreferences
    {
        // One preferences record per local profile.
        [Key]
        public long UserProfileId { get; set; }

        public string Theme { get; set; } = "CarnagePro";
        public bool MatureArtworkEnabled { get; set; }
        public string BackgroundPreset { get; set; } = "Theme";

        public string WeightUnit { get; set; } = "kg";
        public string HeightUnit { get; set; } = "cm";

        public MealType LastMeal { get; set; } = MealType.Breakfast;
    }
}
