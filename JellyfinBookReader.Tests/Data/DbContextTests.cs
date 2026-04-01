using System;
using System.Collections.Generic;
using System.IO;
using JellyfinBookReader.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyfinBookReader.Tests.Data;

public class DbContextTests : IDisposable
{
    private readonly TestDbFixture _fixture;

    public DbContextTests()
    {
        _fixture = new TestDbFixture();
    }

    [Fact]
    public void GetConnection_ReturnsOpenConnection()
    {
        using var conn = _fixture.DbContext.GetConnection();
        Assert.Equal(System.Data.ConnectionState.Open, conn.State);
    }

    [Fact]
    public void GetConnection_EnablesWalMode()
    {
        using var conn = _fixture.DbContext.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var mode = cmd.ExecuteScalar()?.ToString();
        Assert.Equal("wal", mode);
    }

    [Fact]
    public void Schema_ReadingProgressTable_HasExpectedColumns()
    {
        using var conn = _fixture.GetConnection();
        var columns = GetTableColumns(conn, "ReadingProgress");

        Assert.Contains("UserId", columns);
        Assert.Contains("BookId", columns);
        Assert.Contains("Percentage", columns);
        Assert.Contains("CurrentPage", columns);
        Assert.Contains("TotalPages", columns);
        Assert.Contains("ChapterIndex", columns);
        Assert.Contains("ChapterTitle", columns);
        Assert.Contains("PageInChapter", columns);
        Assert.Contains("TotalPagesInChapter", columns);
        Assert.Contains("Position", columns);
        Assert.Contains("LastReadAt", columns);
        Assert.Contains("IsFinished", columns);
    }

    [Fact]
    public void Schema_ReadingSessionsTable_HasExpectedColumns()
    {
        using var conn = _fixture.GetConnection();
        var columns = GetTableColumns(conn, "ReadingSessions");

        Assert.Contains("Id", columns);
        Assert.Contains("UserId", columns);
        Assert.Contains("BookId", columns);
        Assert.Contains("StartedAt", columns);
        Assert.Contains("EndedAt", columns);
        Assert.Contains("LastHeartbeatAt", columns);
        Assert.Contains("DurationSeconds", columns);
        Assert.Contains("PagesRead", columns);
        Assert.Contains("PercentageAdvanced", columns);
        Assert.Contains("IsOpen", columns);
    }

    [Fact]
    public void Schema_HasExpectedIndexes()
    {
        using var conn = _fixture.GetConnection();
        var indexes = GetIndexNames(conn);

        Assert.Contains("idx_sessions_user", indexes);
        Assert.Contains("idx_sessions_open", indexes);
        Assert.Contains("idx_progress_user", indexes);
    }

    [Fact]
    public void Schema_Version_IsSetToCurrentVersion()
    {
        using var conn = _fixture.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(cmd.ExecuteScalar());

        // Current schema version is 2
        Assert.Equal(2, version);
    }

    [Fact]
    public void Schema_ReadingProgress_HasCompositePrimaryKey()
    {
        using var conn = _fixture.GetConnection();
        // Try inserting duplicate UserId+BookId — should fail
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ReadingProgress (UserId, BookId, Percentage, LastReadAt, IsFinished)
            VALUES ('user1', 'book1', 50.0, '2024-01-01T00:00:00Z', 0);";
        cmd.ExecuteNonQuery();

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = @"
            INSERT INTO ReadingProgress (UserId, BookId, Percentage, LastReadAt, IsFinished)
            VALUES ('user1', 'book1', 75.0, '2024-01-02T00:00:00Z', 0);";

        Assert.Throws<SqliteException>(() => cmd2.ExecuteNonQuery());
    }

    [Fact]
    public void MultipleDbContexts_SameDir_ShareDatabase()
    {
        // The fixture already created a DB. Create another context pointing at the same dir.
        // It should see the same tables and data.
        var tempDir = Path.GetTempPath();
        var sharedDir = Path.Combine(tempDir, $"jf_shared_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sharedDir);

        try
        {
            var appPaths = new TestAppPaths(sharedDir);
            var ctx1 = new BookReaderDbContext(appPaths, NullLogger<BookReaderDbContext>.Instance);
            var ctx2 = new BookReaderDbContext(appPaths, NullLogger<BookReaderDbContext>.Instance);

            // Write via ctx1
            using (var conn = ctx1.GetConnection())
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO ReadingProgress (UserId, BookId, Percentage, LastReadAt, IsFinished)
                    VALUES ('user1', 'book1', 50.0, '2024-01-01T00:00:00Z', 0);";
                cmd.ExecuteNonQuery();
            }

            // Read via ctx2
            using (var conn = ctx2.GetConnection())
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM ReadingProgress;";
                var count = Convert.ToInt32(cmd.ExecuteScalar());
                Assert.Equal(1, count);
            }
        }
        finally
        {
            try { Directory.Delete(sharedDir, true); } catch { }
        }
    }

    [Fact]
    public void GetConnection_MultipleCallsReturnIndependentConnections()
    {
        using var conn1 = _fixture.DbContext.GetConnection();
        using var conn2 = _fixture.DbContext.GetConnection();

        // Both should be open
        Assert.Equal(System.Data.ConnectionState.Open, conn1.State);
        Assert.Equal(System.Data.ConnectionState.Open, conn2.State);

        // Closing one shouldn't affect the other
        conn1.Close();
        Assert.Equal(System.Data.ConnectionState.Open, conn2.State);
    }

    //  Helpers 

    private static HashSet<string> GetTableColumns(SqliteConnection conn, string tableName)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1)); // column name
        }
        return columns;
    }

    private static HashSet<string> GetIndexNames(SqliteConnection conn)
    {
        var indexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index';";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            indexes.Add(reader.GetString(0));
        }
        return indexes;
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}