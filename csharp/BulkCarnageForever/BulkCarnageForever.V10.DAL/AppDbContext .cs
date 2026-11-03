using Microsoft.EntityFrameworkCore.Storage;
﻿using BulkCarnageForever.V10.Core.Model;
using BulkCarnageForever.V10.DAL.Catalog;
using Microsoft.EntityFrameworkCore;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace BulkCarnageForever.V10.DAL
{
    public class AppDbContext : DbContext
    {
        // Increment when the database schema changes.
        private const int SchemaVersion = 8;

        private static readonly string DatabasePath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "BulkCarnageForever",
            "carnage.db");

        public DbSet<FoodItem> FoodItems => Set<FoodItem>();
        public DbSet<MealEntry> MealEntries => Set<MealEntry>();

        public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
        public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();
        public DbSet<NutritionGoal> NutritionGoals => Set<NutritionGoal>();
        public DbSet<WeightEntry> WeightEntries => Set<WeightEntry>();
        public DbSet<FoodFavorite> FoodFavorites => Set<FoodFavorite>();
        public DbSet<GhostProfile> GhostProfiles => Set<GhostProfile>();
        public DbSet<GhostMealSimulation> GhostMealSimulations => Set<GhostMealSimulation>();

        protected override void OnConfiguring(
            DbContextOptionsBuilder options)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(DatabasePath)!);

            options
                .UseSqlite($"Data Source={DatabasePath}")
                .UseAsyncSeeding(async (db, created, ct) =>
                {
                    // Automatically import the food catalog.
                    if (!await EntityFrameworkQueryableExtensions
                        .AnyAsync(db.Set<FoodItem>(), ct))
                    {
                        await FoodImporter.ImportAsync(db, ct);
                    }
                });
        }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<UserProfile>().Property(x => x.DisplayName).IsRequired().HasMaxLength(80);
            modelBuilder.Entity<UserPreferences>().HasOne<UserProfile>().WithOne().HasForeignKey<UserPreferences>(x => x.UserProfileId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<NutritionGoal>().HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserProfileId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<NutritionGoal>().HasIndex(x => new { x.UserProfileId, x.EffectiveFrom }).IsUnique();
            modelBuilder.Entity<WeightEntry>().HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserProfileId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<WeightEntry>().HasIndex(x => new { x.UserProfileId, x.Date }).IsUnique();
            modelBuilder.Entity<FoodFavorite>().HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserProfileId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<FoodFavorite>().HasIndex(x => new { x.UserProfileId, x.FoodName }).IsUnique();

            modelBuilder.Entity<GhostProfile>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).IsRequired().HasMaxLength(80);
                entity.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserProfileId).OnDelete(DeleteBehavior.Cascade);
                // Multiple additional Ghosts are allowed, but only one default per owner.
                entity.HasIndex(x => x.UserProfileId, "IX_GhostProfiles_DefaultOwner")
                    .IsUnique().HasFilter("\"IsDefault\" = 1");
                entity.HasIndex(x => x.UserProfileId, "IX_GhostProfiles_Owner");
                // Explicit unique indexes also upgrade existing SQLite tables without
                // rebuilding them; composite simulation FKs use these owner-qualified keys.
                entity.HasIndex(x => new { x.Id, x.UserProfileId }).IsUnique();
            });

            modelBuilder.Entity<GhostMealSimulation>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.FoodName).IsRequired();
                entity.Property(x => x.MealType).HasConversion<string>();
                entity.HasIndex(x => new { x.GhostProfileId, x.MealEntryId }).IsUnique();
                entity.HasIndex(x => new { x.UserProfileId, x.GhostProfileId, x.Date });
                entity.HasOne<GhostProfile>().WithMany()
                    .HasForeignKey(x => new { x.GhostProfileId, x.UserProfileId })
                    .HasPrincipalKey(x => new { x.Id, x.UserProfileId }).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<MealEntry>().WithMany()
                    .HasForeignKey(x => new { x.MealEntryId, x.UserProfileId })
                    .HasPrincipalKey(x => new { x.Id, x.UserProfileId }).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<MealEntry>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.HasIndex(x => new { x.Id, x.UserProfileId }).IsUnique();

                entity.Property(x => x.MealType)
                    .HasConversion<string>();

                entity.Property(x => x.FoodName)
                    .IsRequired();

                entity.HasIndex(x => new
                {
                    x.UserProfileId,
                    x.Date,
                    x.MealType
                });
            });
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            // Additive upgrades preserve profiles and journals. Never delete an existing database.
            var connection = Database.GetDbConnection();
            await connection.OpenAsync(ct);
            try
            {
                await using var version = connection.CreateCommand(); version.CommandText = "PRAGMA user_version;";
                int currentVersion = Convert.ToInt32(await version.ExecuteScalarAsync(ct));
                if (currentVersion > SchemaVersion) throw new InvalidOperationException($"Database version {currentVersion} is newer than supported version {SchemaVersion}.");
                await using var transaction = await Database.BeginTransactionAsync(ct);
                async Task AddColumn(string table, string column, string definition)
                {
                    await using var inspect = connection.CreateCommand();
                    inspect.Transaction = transaction.GetDbTransaction();
                    inspect.CommandText = $"PRAGMA table_info(\"{table}\");";
                    bool tableExists = false, columnExists = false;
                    await using (var reader = await inspect.ExecuteReaderAsync(ct))
                        while (await reader.ReadAsync(ct)) { tableExists = true; if (reader.GetString(1) == column) columnExists = true; }
                    if (tableExists && !columnExists)
                    {
                        await using var alter = connection.CreateCommand(); alter.Transaction = transaction.GetDbTransaction();
                        // Identifiers and definitions are private, fixed upgrade steps, never user input.
                        alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};";
                        await alter.ExecuteNonQueryAsync(ct);
                    }
                }
                await AddColumn("MealEntries", "UserProfileId", "INTEGER NOT NULL DEFAULT 0");
                await AddColumn("UserPreferences", "MatureArtworkEnabled", "INTEGER NOT NULL DEFAULT 0");
                await AddColumn("UserPreferences", "BackgroundPreset", "TEXT NOT NULL DEFAULT 'Theme'");
                await AddColumn("GhostMealSimulations", "OverrideServings", "REAL NULL");
                await AddColumn("GhostMealSimulations", "RecalculatedAt", "TEXT NULL");
                string create = Database.GenerateCreateScript()
                    .Replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ")
                    .Replace("CREATE UNIQUE INDEX ", "CREATE UNIQUE INDEX IF NOT EXISTS ")
                    .Replace("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ");
                await Database.ExecuteSqlRawAsync(create, ct);
                if (currentVersion < 5)
                    await Database.ExecuteSqlRawAsync("UPDATE UserPreferences SET Theme = CASE WHEN Theme IN ('Neon', 'NeonNights', 'Neon Nights') THEN 'NeonNights' ELSE 'CarnagePro' END;", ct);
                if (!await FoodItems.AnyAsync(ct)) await FoodImporter.ImportAsync(this, ct);
                var ownersWithoutDefaults = await UserProfiles
                    .Where(user => !GhostProfiles.Any(ghost => ghost.UserProfileId == user.Id && ghost.IsDefault))
                    .Select(user => user.Id).ToListAsync(ct);
                var ghosts = new GhostProfileStore(this);
                foreach (long ownerId in ownersWithoutDefaults)
                    await ghosts.EnsureDefaultAsync(ownerId, ct);
                await Database.ExecuteSqlRawAsync($"PRAGMA user_version = {SchemaVersion};", ct);
                await transaction.CommitAsync(ct);
            }
            finally { await connection.CloseAsync(); }
        }
    }
}
