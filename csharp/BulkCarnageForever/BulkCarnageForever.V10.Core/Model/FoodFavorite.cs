namespace BulkCarnageForever.V10.Core.Model
{
    public class FoodFavorite
    {
        public long Id { get; set; }
        public long UserProfileId { get; set; }

        // Matches FoodItem.RecipeName, the existing food catalog key.
        public string FoodName { get; set; } = string.Empty;
    }
}
