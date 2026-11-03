using BulkCarnageForever.V10.Core.Model;

namespace BulkCarnageForever.V10.Core.Export;

/// <summary>Detached persisted records for one local user; no derived history is generated.</summary>
public sealed record ProfileSnapshotData(DateTimeOffset ExportedAt, UserProfile Profile, UserPreferences? Preferences,
    IReadOnlyList<NutritionGoal> Goals, IReadOnlyList<FoodItem> Foods, IReadOnlyList<MealEntry> MealEntries,
    IReadOnlyList<WeightEntry> WeightEntries, IReadOnlyList<FoodFavorite> FoodFavorites,
    IReadOnlyList<GhostProfile> GhostProfiles, IReadOnlyList<GhostMealSimulation> GhostMealSimulations);
