using System;
using System.Collections.Generic;
using System.Globalization;
using JellyfinBookReader.Dto;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace JellyfinBookReader.Data;

public class ClientDataRepository
{
    private readonly BookReaderDbContext _db;
    private readonly ILogger<ClientDataRepository> _logger;

    public ClientDataRepository(BookReaderDbContext db, ILogger<ClientDataRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Get client data for a specific user + book. Returns null if none exists.
    /// </summary>
    public ClientDataDto? Get(Guid userId, Guid bookId)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            SELECT Data, UpdatedAt
            FROM BookClientData
            WHERE UserId = @userId AND BookId = @bookId";

        cmd.Parameters.AddWithValue("@userId", userId.ToString());
        cmd.Parameters.AddWithValue("@bookId", bookId.ToString());

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return MapRow(reader);
    }

    /// <summary>
    /// Get client data blobs for all books for a given user. Keyed by BookId.
    /// </summary>
    public Dictionary<Guid, ClientDataDto> GetAllForUser(Guid userId)
    {
        var result = new Dictionary<Guid, ClientDataDto>();

        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            SELECT BookId, Data, UpdatedAt
            FROM BookClientData
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
    /// Create or update the client data blob.
    /// Returns "updated" on success, or "conflict" if the client's updatedAt is
    /// older than the server's — indicating the client must fetch, merge, and retry.
    /// On conflict, serverData contains the current server state.
    /// </summary>
    public (string Status, ClientDataDto? ServerData) Upsert(
        Guid userId,
        Guid bookId,
        ClientDataUpdateDto update)
    {
        using var conn = _db.GetConnection();
        using var tx = conn.BeginTransaction();

        try
        {
            // Conflict check: if the client sent an updatedAt and it's stale, reject.
            if (update.UpdatedAt.HasValue)
            {
                using var checkCmd = conn.CreateCommand();
                checkCmd.Transaction = tx;
                checkCmd.CommandText = @"
                    SELECT UpdatedAt FROM BookClientData
                    WHERE UserId = @userId AND BookId = @bookId";
                checkCmd.Parameters.AddWithValue("@userId", userId.ToString());
                checkCmd.Parameters.AddWithValue("@bookId", bookId.ToString());

                var existingStr = checkCmd.ExecuteScalar() as string;
                if (existingStr != null &&
                    DateTime.TryParse(existingStr, null, DateTimeStyles.RoundtripKind, out var existingDt) &&
                    update.UpdatedAt.Value < existingDt)
                {
                    tx.Rollback();
                    var serverData = Get(userId, bookId);
                    return ("conflict", serverData);
                }
            }

            var now = DateTime.UtcNow.ToString("o");

            using var upsertCmd = conn.CreateCommand();
            upsertCmd.Transaction = tx;
            upsertCmd.CommandText = @"
                INSERT INTO BookClientData (UserId, BookId, Data, UpdatedAt)
                VALUES (@userId, @bookId, @data, @updatedAt)
                ON CONFLICT(UserId, BookId) DO UPDATE SET
                    Data      = @data,
                    UpdatedAt = @updatedAt";

            upsertCmd.Parameters.AddWithValue("@userId", userId.ToString());
            upsertCmd.Parameters.AddWithValue("@bookId", bookId.ToString());
            upsertCmd.Parameters.AddWithValue("@data", update.Data);
            upsertCmd.Parameters.AddWithValue("@updatedAt", now);

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
    /// Delete client data for a user + book. Returns true if a row was removed.
    /// </summary>
    public bool Delete(Guid userId, Guid bookId)
    {
        using var conn = _db.GetConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            DELETE FROM BookClientData
            WHERE UserId = @userId AND BookId = @bookId";

        cmd.Parameters.AddWithValue("@userId", userId.ToString());
        cmd.Parameters.AddWithValue("@bookId", bookId.ToString());

        return cmd.ExecuteNonQuery() > 0;
    }

    // Column order (when offset == 0): Data, UpdatedAt
    // Column order (when offset == 1): BookId, Data, UpdatedAt  — used by GetAllForUser
    private static ClientDataDto MapRow(SqliteDataReader reader, int offset = 0)
    {
        var updatedAtStr = reader.IsDBNull(offset + 1) ? null : reader.GetString(offset + 1);
        DateTime updatedAt;
        if (updatedAtStr == null ||
            !DateTime.TryParse(updatedAtStr, null, DateTimeStyles.RoundtripKind, out updatedAt))
        {
            updatedAt = DateTime.UtcNow;
        }

        return new ClientDataDto
        {
            Data = reader.IsDBNull(offset + 0) ? "{}" : reader.GetString(offset + 0),
            UpdatedAt = updatedAt,
        };
    }
}