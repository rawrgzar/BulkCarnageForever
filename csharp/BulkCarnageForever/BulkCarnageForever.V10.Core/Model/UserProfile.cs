namespace BulkCarnageForever.V10.Core.Model
{
    public class UserProfile
    {
        public long Id { get; set; }

        public string DisplayName { get; set; } = string.Empty;

        public DateOnly? DateOfBirth { get; set; }
        public double? HeightCm { get; set; }

        // Optional input for the TDEE formula coefficient.
        public bool? IsMale { get; set; }
        public double ActivityFactor { get; set; } = 1.2;
    }
}
