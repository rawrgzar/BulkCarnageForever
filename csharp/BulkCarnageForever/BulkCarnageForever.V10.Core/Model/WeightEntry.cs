namespace BulkCarnageForever.V10.Core.Model
{
    public class WeightEntry
    {
        public long Id { get; set; }
        public long UserProfileId { get; set; }

        public DateOnly Date { get; set; }
            = DateOnly.FromDateTime(DateTime.Today);

        // Store a consistent unit, regardless of the display preference.
        public double WeightKg { get; set; }
    }
}
