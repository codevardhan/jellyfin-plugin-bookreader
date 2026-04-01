using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace JellyfinBookReader.Data;

public class SessionRepository
{
    private readonly BookReaderDbContext _db;
    private readonly ILogger<SessionRepository> _logger;

    public SessionRepository(BookReaderDbContext db, ILogger<SessionRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Start a new session. Auto-closes any existing open session for the same user+book.
    /// Returns the new session ID and start time.
    /// </summary>
    public (string SessionId, DateTime StartedAt) StartSession(Guid userId, Guid bookId)
    {
        using var conn = _db.GetConnection();
        using var tx = conn.BeginTransaction();

        try
        {
            // Close any existing open session for this user+book
            CloseOpenSessions(conn, tx, userId, bookId);

            var sessionId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;
            var nowStr = now.ToString("o");

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO ReadingSessions
                    (Id, UserId, BookId, StartedAt, LastHeartbeatAt, IsOpen)
                VALUES
                    (@id, @userId, @bookId, @now, @now, 1)";

            cmd.Parameters.AddWithValue("@id", sessionId);
            cmd.Parameters.AddWithValue("@userId", userId.ToString());
            cmd.Parameters.AddWithValue("@bookId", bookId.ToString());
            cmd.Parameters.AddWithValue("@now", nowStr);

            cmd.ExecuteNonQuery();
            tx.Commit();

            return (sessionId, now);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Update the heartbeat timestamp for an open session.
    /// Returns false if session not found or already closed.
    /// </summary>
    public bool Heartbeat(string sessionId)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            UPDATE ReadingSessions
            SET LastHeartbeatAt = @now
            WHERE Id = @id AND IsOpen = 1";

        cmd.Parameters.AddWithValue("@id", sessionId);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));

        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// End a session. Sets EndedAt, computes duration, records pages/percentage.
    /// Returns false if session not found or already closed.
    /// </summary>
    public bool EndSession(string sessionId, int? pagesRead, double? percentageAdvanced)
    {
        using var conn = _db.GetConnection();
        using var tx = conn.BeginTransaction();

        try
        {
            // Get the session's StartedAt
            using var getCmd = conn.CreateCommand();
            getCmd.Transaction = tx;
            getCmd.CommandText = "SELECT StartedAt FROM ReadingSessions WHERE Id = @id AND IsOpen = 1";
            getCmd.Parameters.AddWithValue("@id", sessionId);

            var startedAtStr = getCmd.ExecuteScalar() as string;
            if (startedAtStr == null)
            {
                tx.Rollback();
                return false;
            }

            var startedAt = DateTime.Parse(startedAtStr, null, System.Globalization.DateTimeStyles.RoundtripKind);
            var now = DateTime.UtcNow;
            var duration = (int)(now - startedAt).TotalSeconds;

            using var updateCmd = conn.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandText = @"
                UPDATE ReadingSessions
                SET EndedAt            = @endedAt,
                    DurationSeconds    = @duration,
                    PagesRead          = @pages,
                    PercentageAdvanced = @pctAdv,
                    IsOpen             = 0
                WHERE Id = @id AND IsOpen = 1";

            updateCmd.Parameters.AddWithValue("@id", sessionId);
            updateCmd.Parameters.AddWithValue("@endedAt", now.ToString("o"));
            updateCmd.Parameters.AddWithValue("@duration", duration);
            updateCmd.Parameters.AddWithValue("@pages", (object?)pagesRead ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("@pctAdv", (object?)percentageAdvanced ?? DBNull.Value);

            var rows = updateCmd.ExecuteNonQuery();
            tx.Commit();

            return rows > 0;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Close all stale sessions (open sessions with no heartbeat within the timeout).
    /// Returns the number of sessions closed.
    /// </summary>
    public int CloseStaleSessionsGlobal(int timeoutMinutes)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();

        var cutoff = DateTime.UtcNow.AddMinutes(-timeoutMinutes).ToString("o");

        cmd.CommandText = @"
            UPDATE ReadingSessions
            SET EndedAt         = LastHeartbeatAt,
                DurationSeconds = CAST(
                    (julianday(LastHeartbeatAt) - julianday(StartedAt)) * 86400 AS INTEGER
                ),
                IsOpen          = 0
            WHERE IsOpen = 1 AND LastHeartbeatAt < @cutoff";

        cmd.Parameters.AddWithValue("@cutoff", cutoff);

        var closed = cmd.ExecuteNonQuery();
        if (closed > 0)
        {
            _logger.LogInformation("Auto-closed {Count} stale reading sessions.", closed);
        }

        return closed;
    }

    /// <summary>
    /// Get all closed sessions for a user, ordered by StartedAt desc.
    /// </summary>
    public List<SessionRow> GetSessionsForUser(Guid userId)
    {
        var sessions = new List<SessionRow>();

        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            SELECT Id, BookId, StartedAt, EndedAt, DurationSeconds,
                   PagesRead, PercentageAdvanced
            FROM ReadingSessions
            WHERE UserId = @userId AND IsOpen = 0 AND DurationSeconds > 0
            ORDER BY StartedAt DESC";

        cmd.Parameters.AddWithValue("@userId", userId.ToString());

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            sessions.Add(new SessionRow
            {
                Id = reader.GetString(0),
                BookId = Guid.Parse(reader.GetString(1)),
                StartedAt = DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
                EndedAt = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
                DurationSeconds = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                PagesRead = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                PercentageAdvanced = reader.IsDBNull(6) ? null : reader.GetDouble(6),
            });
        }

        return sessions;
    }

    /// <summary>
    /// Close any open sessions for a specific user+book.
    /// </summary>
    private void CloseOpenSessions(SqliteConnection conn, SqliteTransaction tx, Guid userId, Guid bookId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;

        cmd.CommandText = @"
            UPDATE ReadingSessions
            SET EndedAt         = LastHeartbeatAt,
                DurationSeconds = CAST(
                    (julianday(LastHeartbeatAt) - julianday(StartedAt)) * 86400 AS INTEGER
                ),
                IsOpen          = 0
            WHERE UserId = @userId AND BookId = @bookId AND IsOpen = 1";

        cmd.Parameters.AddWithValue("@userId", userId.ToString());
        cmd.Parameters.AddWithValue("@bookId", bookId.ToString());

        cmd.ExecuteNonQuery();
    }
}

public class SessionRow
{
    public string Id { get; set; } = string.Empty;
    public Guid BookId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int DurationSeconds { get; set; }
    public int? PagesRead { get; set; }
    public double? PercentageAdvanced { get; set; }
}