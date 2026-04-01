using System;
using System.IO;
using JellyfinBookReader.Data;
using MediaBrowser.Common.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace JellyfinBookReader.Tests;

/// <summary>
/// Provides a real BookReaderDbContext backed by a temp-directory SQLite file.
/// Each test class gets its own isolated DB.
/// </summary>
public class TestDbFixture : IDisposable
{
    private readonly string _tempDir;
    public BookReaderDbContext DbContext { get; }

    public TestDbFixture()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"jf_bookreader_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // BookReaderDbContext uses appPaths.PluginConfigurationsPath to find/create its DB.
        var appPaths = new TestAppPaths(_tempDir);
        DbContext = new BookReaderDbContext(appPaths, NullLogger<BookReaderDbContext>.Instance);
    }

    /// <summary>
    /// Get a raw connection to the same DB (for backdoor inserts in tests).
    /// </summary>
    public SqliteConnection GetConnection() => DbContext.GetConnection();

    /// <summary>
    /// Wipe all rows between tests if sharing a fixture.
    /// </summary>
    public void ClearData()
    {
        using var conn = DbContext.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ReadingProgress; DELETE FROM ReadingSessions;";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        // BookReaderDbContext is not IDisposable — connections are per-operation.
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Minimal IApplicationPaths implementation for tests.
/// Only PluginConfigurationsPath is used by BookReaderDbContext.
/// </summary>
internal class TestAppPaths : IApplicationPaths
{
    private readonly string _root;
    public TestAppPaths(string root) => _root = root;

    public string ProgramDataPath => _root;
    public string WebPath => _root;
    public string ProgramSystemPath => _root;
    public string DataPath => _root;
    public string ImageCachePath => _root;
    public string PluginsPath => _root;
    public string PluginConfigurationsPath => _root;
    public string LogDirectoryPath => _root;
    public string ConfigurationDirectoryPath => _root;
    public string CachePath => _root;
    public string TempDirectory => _root;
    public string SystemConfigurationFilePath => Path.Combine(_root, "system.xml");
    public string VirtualDataPath => _root;
    public string TrickplayPath => _root;
    public string BackupPath => _root;

    public void MakeSanityCheckOrThrow() { }
    public void CreateAndCheckMarker(string name, string defaultValue = "", bool isDirectory = false) { }
}