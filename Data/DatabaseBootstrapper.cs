using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FlexPlan.Data;

public static class DatabaseBootstrapper
{
    /// <summary>
    /// Applies any pending EF Core migrations. A database created by an older FlexPlan
    /// version (before migrations existed) can't be upgraded in place, so it is renamed
    /// to a .backup file and a fresh database is created.
    /// </summary>
    public static void PrepareAndMigrate(AppDbContext db, ILogger logger)
    {
        // Hosted PostgreSQL: create the tables on first start.
        // (The migrations in /Migrations are written for the local SQLite database.)
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            db.Database.EnsureCreated();
            return;
        }

        BackupLegacyDatabase(db, logger);
        db.Database.Migrate();
    }

    private static void BackupLegacyDatabase(AppDbContext db, ILogger logger)
    {
        if (db.Database.GetDbConnection() is not SqliteConnection conn) return;

        var file = conn.DataSource;
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return;

        bool TableExists(string name)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$n";
            cmd.Parameters.AddWithValue("$n", name);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        conn.Open();
        var legacy = TableExists("Users") && !TableExists("__EFMigrationsHistory");
        conn.Close();
        if (!legacy) return;

        SqliteConnection.ClearAllPools();
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = file + suffix;
            if (File.Exists(path)) File.Move(path, $"{file}.backup-{stamp}{suffix}");
        }
        logger.LogWarning("Found a database from an older FlexPlan version. It was saved as {File}.backup-{Stamp} and a new one will be created.", file, stamp);
    }
}
