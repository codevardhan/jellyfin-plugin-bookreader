using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JellyfinBookReader.Dto;

/// <summary>
/// Response for GET /api/BookReader/books/{id}/client-data.
/// The server stores and returns Data verbatim — it never parses or inspects the JSON.
/// The client owns the schema inside Data (quotes, stats, bookmarks, etc.).
/// </summary>
public class ClientDataDto
{
    /// <summary>
    /// Opaque JSON blob. The server treats this as a raw string.
    /// Clients are responsible for serializing / deserializing their own schema.
    /// </summary>
    [JsonPropertyName("data")]
    public string Data { get; set; } = "{}";

    /// <summary>
    /// Server-assigned timestamp of the last successful write.
    /// Clients should send this back on the next PUT for conflict detection.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Request body for PUT /api/BookReader/books/{id}/client-data.
/// </summary>
public class ClientDataUpdateDto
{
    /// <summary>
    /// The full client data blob. Must be valid JSON but the server does not
    /// validate the internal structure — that is the client's responsibility.
    /// </summary>
    [JsonPropertyName("data")]
    public string Data { get; set; } = "{}";

    /// <summary>
    /// Optional. The updatedAt value from the last GET or successful PUT.
    /// If provided and older than the server's current value, the server
    /// returns 409 Conflict with the server's current state so the client
    /// can merge locally and retry.
    /// Omit on the very first push for a book (no prior server state).
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}

//  Batch variants 

/// <summary>
/// Request body for PUT /api/BookReader/client-data/batch.
/// Mirrors the batch progress endpoint — intended for offline sync catch-up.
/// </summary>
public class BatchClientDataRequest
{
    [JsonPropertyName("updates")]
    public List<BatchClientDataItem> Updates { get; set; } = new();
}

public class BatchClientDataItem
{
    [JsonPropertyName("bookId")]
    public Guid BookId { get; set; }

    [JsonPropertyName("data")]
    public string Data { get; set; } = "{}";

    /// <summary>
    /// Optional — same conflict detection semantics as the single-book endpoint.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Response for PUT /api/BookReader/client-data/batch.
/// </summary>
public class BatchClientDataResponse
{
    [JsonPropertyName("results")]
    public List<BatchClientDataResult> Results { get; set; } = new();
}

public class BatchClientDataResult
{
    [JsonPropertyName("bookId")]
    public Guid BookId { get; set; }

    /// <summary>"updated" | "conflict" | "error"</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Populated only when status == "conflict".
    /// Contains the server's current blob so the client can merge and retry.
    /// </summary>
    [JsonPropertyName("serverData")]
    public ClientDataDto? ServerData { get; set; }
}