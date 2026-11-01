using BulkCarnageForever.V10.Core.Model;
using BulkCarnageForever.V10.DAL.Catalog;
using Microsoft.EntityFrameworkCore;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace BulkCarnageForever.V10.DAL
{
    public class AppDbContext : DbContext
    {
        // Increment when the database schema changes.
        private const int SchemaVersion = 2;

        private static readonly string DatabasePath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "BulkCarnageForever",
            "carnage.db");

        public DbSet<FoodItem> FoodItems => Set<FoodItem>();
        public DbSet<MealEntry> MealEntries => Set<MealEntry>();

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

            modelBuilder.Entity<MealEntry>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.MealType)
                    .HasConversion<string>();

                entity.Property(x => x.FoodName)
                    .IsRequired();

                entity.HasIndex(x => new
                {
                    x.Date,
                    x.MealType
                });
            });
        }

        public async Task InitializeAsync(
            CancellationToken ct = default)
        {
            int currentVersion = 0;
            bool exists = File.Exists(DatabasePath);

            if (exists)
            {
                // Read SQLite's stored database version.
                var connection = Database.GetDbConnection();

                await connection.OpenAsync(ct);

                try
                {
                    await using var command =
                        connection.CreateCommand();

                    command.CommandText = "PRAGMA user_version;";

                    var result = await command.ExecuteScalarAsync(ct);

                    currentVersion = Convert.ToInt32(result);
                }
                finally
                {
                    await connection.CloseAsync();
                }

                // Prevent accidental downgrade.
                if (currentVersion > SchemaVersion)
                {
                    throw new InvalidOperationException(
                        $"Database version {currentVersion} is newer " +
                        $"than supported version {SchemaVersion}.");
                }

                // Rebuild when the schema version changes.
                if (currentVersion < SchemaVersion)
                {
                    await Database.EnsureDeletedAsync(ct);
                }
            }

            // EF creates every table defined in the model.
            // UseAsyncSeeding imports the food catalog.
            await Database.EnsureCreatedAsync(ct);

            // Save the schema version after successful creation.
            if (!exists || currentVersion < SchemaVersion)
            {
                await Database.ExecuteSqlRawAsync(
                    $"PRAGMA user_version = {SchemaVersion};",
                    ct);
            }
        }
    }
}
