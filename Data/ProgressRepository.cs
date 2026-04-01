using System;
using System.Collections.Generic;
using System.Globalization;
using JellyfinBookReader.Dto;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace JellyfinBookReader.Data;

public class ProgressRepository
{
    private readonly BookReaderDbContext _db;
    private readonly ILogger<ProgressRepository> _logger;

    public ProgressRepository(BookReaderDbContext db, ILogger<ProgressRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Get progress for a specific user + book. Returns null if none exists.
    /// </summary>
    public ProgressDto? Get(Guid userId, Guid bookId)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            SELECT Percentage, CurrentPage, TotalPages,
                   ChapterIndex, ChapterTitle, PageInChapter, TotalPagesInChapter,
                   Position, LastReadAt, IsFinished
            FROM ReadingProgress
            WHERE UserId = @userId AND BookId = @bookId";

        cmd.Parameters.AddWithValue("@userId", userId.ToString());
        cmd.Parameters.AddWithValue("@bookId", bookId.ToString());

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return MapRow(reader);
    }

    /// <summary>
    /// Get progress for all books for a given user. Keyed by BookId.
    /// </summary>
    public Dictionary<Guid, ProgressDto> GetAllForUser(Guid userId)
    {
        var result = new Dictionary<Guid, ProgressDto>();

        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            SELECT BookId, Percentage, CurrentPage, TotalPages,
                   ChapterIndex, ChapterTitle, PageInChapter, TotalPagesInChapter,
                   Position, LastReadAt, IsFinished
            FROM ReadingProgress
            WHERE UserId = @userId";

        cmd.Parameters.AddWithValue("@userId", userId.ToString());

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var bookId = Guid.Parse(reader.GetString(0));
            result[bookId] = MapRow(reader, offset: 1);
        }

        return result;
    }

    /// <summary>
    /// Create or update progress. Returns "updated" or "conflict".
    /// If clientLastReadAt is provided and is older than what's stored, returns "conflict".
    /// </summary>
    public (string Status, ProgressDto? ServerProgress) Upsert(
        Guid userId,
        Guid bookId,
        ProgressUpdateDto update)
    {
        using var conn = _db.GetConnection();
        using var tx = conn.BeginTransaction();

        try
        {
            // Check for conflict if client sent a lastReadAt
            if (update.LastReadAt.HasValue)
            {
                using var checkCmd = conn.CreateCommand();
                checkCmd.Transaction = tx;
                checkCmd.CommandText = @"
                    SELECT LastReadAt FROM ReadingProgress
                    WHERE UserId = @userId AND BookId = @bookId";
                checkCmd.Parameters.AddWithValue("@userId", userId.ToString());
                checkCmd.Parameters.AddWithValue("@bookId", bookId.ToString());

                var existingStr = checkCmd.ExecuteScalar() as string;
                if (existingStr != null &&
                    DateTime.TryParse(existingStr, null, DateTimeStyles.RoundtripKind, out var existingDt) &&
                    update.LastReadAt.Value < existingDt)
                {
                    tx.Rollback();
                    var serverProgress = Get(userId, bookId);
                    return ("conflict", serverProgress);
                }
            }

            var now = DateTime.UtcNow.ToString("o");

            using var upsertCmd = conn.CreateCommand();
            upsertCmd.Transaction = tx;
            upsertCmd.CommandText = @"
                INSERT INTO ReadingProgress
                    (UserId, BookId, Percentage, CurrentPage, TotalPages,
                     ChapterIndex, ChapterTitle, PageInChapter, TotalPagesInChapter,
                     Position, LastReadAt, IsFinished)
                VALUES
                    (@userId, @bookId, @pct, @page, @total,
                     @chIdx, @chTitle, @pgInCh, @totalPgInCh,
                     @pos, @lastRead, @finished)
                ON CONFLICT(UserId, BookId) DO UPDATE SET
                    Percentage          = @pct,
                    CurrentPage         = @page,
                    TotalPages          = @total,
                    ChapterIndex        = @chIdx,
                    ChapterTitle        = @chTitle,
                    PageInChapter       = @pgInCh,
                    TotalPagesInChapter = @totalPgInCh,
                    Position            = @pos,
                    LastReadAt          = @lastRead,
                    IsFinished          = @finished";

            upsertCmd.Parameters.AddWithValue("@userId", userId.ToString());
            upsertCmd.Parameters.AddWithValue("@bookId", bookId.ToString());
            upsertCmd.Parameters.AddWithValue("@pct", update.Percentage);
            upsertCmd.Parameters.AddWithValue("@page", (object?)update.CurrentPage ?? DBNull.Value);
            upsertCmd.Parameters.AddWithValue("@total", (object?)update.TotalPages ?? DBNull.Value);
            upsertCmd.Parameters.AddWithValue("@chIdx", (object?)update.ChapterIndex ?? DBNull.Value);
            upsertCmd.Parameters.AddWithValue("@chTitle", (object?)update.ChapterTitle ?? DBNull.Value);
            upsertCmd.Parameters.AddWithValue("@pgInCh", (object?)update.PageInChapter ?? DBNull.Value);
            upsertCmd.Parameters.AddWithValue("@totalPgInCh", (object?)update.TotalPagesInChapter ?? DBNull.Value);
            upsertCmd.Parameters.AddWithValue("@pos", (object?)update.Position ?? DBNull.Value);
            upsertCmd.Parameters.AddWithValue("@lastRead", now);
            upsertCmd.Parameters.AddWithValue("@finished", update.IsFinished ? 1 : 0);

            upsertCmd.ExecuteNonQuery();
            tx.Commit();

            return ("updated", null);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Delete progress for a user + book.
    /// </summary>
    public bool Delete(Guid userId, Guid bookId)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            DELETE FROM ReadingProgress
            WHERE UserId = @userId AND BookId = @bookId";

        cmd.Parameters.AddWithValue("@userId", userId.ToString());
        cmd.Parameters.AddWithValue("@bookId", bookId.ToString());

        return cmd.ExecuteNonQuery() > 0;
    }

    // Column order: Percentage, CurrentPage, TotalPages,
    //               ChapterIndex, ChapterTitle, PageInChapter, TotalPagesInChapter,
    //               Position, LastReadAt, IsFinished
    private static ProgressDto MapRow(SqliteDataReader reader, int offset = 0)
    {
        var lastReadAtStr = reader.IsDBNull(offset + 8) ? null : reader.GetString(offset + 8);
        DateTime lastReadAt;
        if (lastReadAtStr == null ||
            !DateTime.TryParse(lastReadAtStr, null, DateTimeStyles.RoundtripKind, out lastReadAt))
        {
            lastReadAt = DateTime.UtcNow; // fallback for corrupt/missing dates
        }

        return new ProgressDto
        {
            Percentage          = reader.IsDBNull(offset + 0) ? 0.0 : reader.GetDouble(offset + 0),
            CurrentPage         = reader.IsDBNull(offset + 1) ? null : reader.GetInt32(offset + 1),
            TotalPages          = reader.IsDBNull(offset + 2) ? null : reader.GetInt32(offset + 2),
            ChapterIndex        = reader.IsDBNull(offset + 3) ? null : reader.GetInt32(offset + 3),
            ChapterTitle        = reader.IsDBNull(offset + 4) ? null : reader.GetString(offset + 4),
            PageInChapter       = reader.IsDBNull(offset + 5) ? null : reader.GetInt32(offset + 5),
            TotalPagesInChapter = reader.IsDBNull(offset + 6) ? null : reader.GetInt32(offset + 6),
            Position            = reader.IsDBNull(offset + 7) ? null : reader.GetString(offset + 7),
            LastReadAt          = lastReadAt,
            IsFinished          = reader.IsDBNull(offset + 9) ? false : reader.GetInt32(offset + 9) != 0,
        };
    }
}