using System;
using System.IO;
using MediaBrowser.Common.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace JellyfinBookReader.Data;

public class BookReaderDbContext
{
    private readonly string _connectionString;
    private readonly ILogger<BookReaderDbContext> _logger;

    // Bump this when the schema changes. MigrateDatabase() handles upgrades.
    private const int CurrentSchemaVersion = 2;

    public BookReaderDbContext(IApplicationPaths appPaths, ILogger<BookReaderDbContext> logger)
    {
        _logger = logger;

        var pluginDataDir = Path.Combine(appPaths.PluginConfigurationsPath, "BookReader");
        Directory.CreateDirectory(pluginDataDir);

        var dbPath = Path.Combine(pluginDataDir, "bookreader.db");
        _connectionString = $"Data Source={dbPath}";

        InitializeDatabase();
    }

    /// <summary>
    /// Get an open connection. Caller is responsible for disposing.
    /// </summary>
    public SqliteConnection GetConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Enable WAL mode for better concurrent read/write performance
        using var walCmd = conn.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode=WAL;";
        walCmd.ExecuteNonQuery();

        return conn;
    }

    private void InitializeDatabase()
    {
        try
        {
            using var conn = GetConnection();

            // Create tables if they don't exist (fresh install)
            CreateTablesIfNeeded(conn);

            // Run migrations for existing installs
            MigrateDatabase(conn);

            _logger.LogInformation("BookReader database initialized (schema v{Version}).", CurrentSchemaVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize BookReader database.");
            throw;
        }
    }

    private void CreateTablesIfNeeded(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS ReadingProgress (
                UserId                TEXT NOT NULL,
                BookId                TEXT NOT NULL,
                Percentage            REAL NOT NULL DEFAULT 0,
                CurrentPage           INTEGER,
                TotalPages            INTEGER,
                ChapterIndex          INTEGER,
                ChapterTitle          TEXT,
                PageInChapter         INTEGER,
                TotalPagesInChapter   INTEGER,
                Position              TEXT,
                LastReadAt            TEXT NOT NULL,
                IsFinished            INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (UserId, BookId)
            );

            CREATE TABLE IF NOT EXISTS ReadingSessions (
                Id                  TEXT PRIMARY KEY,
                UserId              TEXT NOT NULL,
                BookId              TEXT NOT NULL,
                StartedAt           TEXT NOT NULL,
                EndedAt             TEXT,
                LastHeartbeatAt     TEXT NOT NULL,
                DurationSeconds     INTEGER,
                PagesRead           INTEGER,
                PercentageAdvanced  REAL,
                IsOpen              INTEGER NOT NULL DEFAULT 1
            );

            CREATE INDEX IF NOT EXISTS idx_sessions_user
                ON ReadingSessions(UserId, BookId);
            CREATE INDEX IF NOT EXISTS idx_sessions_open
                ON ReadingSessions(IsOpen, LastHeartbeatAt);
            CREATE INDEX IF NOT EXISTS idx_progress_user
                ON ReadingProgress(UserId);
        ";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Run incremental migrations based on a user_version pragma.
    /// </summary>
    private void MigrateDatabase(SqliteConnection conn)
    {
        int version;
        using (var vCmd = conn.CreateCommand())
        {
            vCmd.CommandText = "PRAGMA user_version;";
            version = Convert.ToInt32(vCmd.ExecuteScalar());
        }

        if (version < 2)
        {
            MigrateV1ToV2(conn);
        }

        // Set current version
        using var setCmd = conn.CreateCommand();
        setCmd.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
        setCmd.ExecuteNonQuery();
    }

    /// <summary>
    /// v1 → v2: Replace CurrentChapter (TEXT) with ChapterIndex (INT),
    /// ChapterTitle (TEXT), PageInChapter (INT), TotalPagesInChapter (INT).
    /// SQLite doesn't support DROP COLUMN on older versions, so we add
    /// the new columns and leave CurrentChapter as a dead column.
    /// </summary>
    private void MigrateV1ToV2(SqliteConnection conn)
    {
        _logger.LogInformation("Migrating BookReader DB from v1 to v2 (unified progress model)...");

        using var cmd = conn.CreateCommand();

        // Check which columns already exist (handles fresh installs where
        // CreateTablesIfNeeded already created the v2 schema)
        cmd.CommandText = "PRAGMA table_info(ReadingProgress);";
        var existingColumns = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                existingColumns.Add(reader.GetString(1)); // column name
            }
        }

        void AddColumnIfMissing(string name, string type)
        {
            if (existingColumns.Contains(name)) return;
            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE ReadingProgress ADD COLUMN {name} {type};";
            alter.ExecuteNonQuery();
            _logger.LogInformation("  Added column: {Column}", name);
        }

        AddColumnIfMissing("ChapterIndex", "INTEGER");
        AddColumnIfMissing("ChapterTitle", "TEXT");
        AddColumnIfMissing("PageInChapter", "INTEGER");
        AddColumnIfMissing("TotalPagesInChapter", "INTEGER");
        AddColumnIfMissing("Position", "TEXT");

        _logger.LogInformation("Migration v1→v2 complete.");
    }

    // No Dispose needed — connections are created and disposed per-operation
}