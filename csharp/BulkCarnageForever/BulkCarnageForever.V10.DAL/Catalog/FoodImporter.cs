using BulkCarnageForever.V10.Core.Model;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace BulkCarnageForever.V10.DAL.Catalog
{
    public static class FoodImporter
    {
        public static async Task<int> ImportAsync(DbContext db, CancellationToken ct = default)
        {
            using var stream = typeof(FoodImporter)
                .Assembly
                .GetManifestResourceStream("FoodCatalog.json")
                ?? throw new FileNotFoundException(
                    "FoodCatalog.json not found.");

            var foods = await JsonSerializer.DeserializeAsync<List<FoodItem>>(
                stream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                ct) ?? [];

            if (foods.Any(x => string.IsNullOrWhiteSpace(x.RecipeName)))
                throw new InvalidDataException("A food has no RecipeName.");

            var duplicates = foods
                .GroupBy(x => x.RecipeName, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicates.Count > 0)
                throw new InvalidDataException(
                    $"Duplicate recipes: {string.Join(", ", duplicates.Select(g => g.Key))}");

            await db.Set<FoodItem>().AddRangeAsync(foods, ct);
            await db.SaveChangesAsync(ct);

            return foods.Count;
        }
    }
}
