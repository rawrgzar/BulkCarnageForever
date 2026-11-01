
using BulkCarnageForever.V10.DAL;

await using var db = new AppDbContext();

await db.Database.EnsureCreatedAsync();

// Only retrieve grouping information
var foods = db.FoodItems
    .Select(x => new
    {
        x.GroupName,
        x.BrandType
    })
    .ToList();

// Group by food category
var groups = foods
    .GroupBy(x => x.GroupName ?? "Uncategorized")
    .OrderBy(x => x.Key);

Console.WriteLine($"Total Foods: {foods.Count:N0}");
Console.WriteLine($"Total Groups: {groups.Count()}");
Console.WriteLine(new string('=', 60));

foreach (var group in groups)
{
    Console.WriteLine(
        $"\n{group.Key.ToUpper()} ({group.Count()} foods)");

    var brands = group
        .GroupBy(x => x.BrandType ?? "Generic")
        .OrderBy(x => x.Key);

    foreach (var brand in brands)
    {
        Console.WriteLine(
            $"  {brand.Key,-35} {brand.Count(),5} items");
    }
}
