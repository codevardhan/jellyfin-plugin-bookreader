using System;
using System.Text.Json.Serialization;

namespace JellyfinBookReader.Dto;

/// <summary>
/// Returned by <c>GET /api/BookReader/books/{id}/manifest</c>.
/// Clients should fetch this before requesting pages to know the total page count
/// and whether streaming is supported for this book's format.
/// </summary>
public class BookManifestDto
{
    [JsonPropertyName("bookId")]
    public Guid BookId { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;

    [JsonPropertyName("isStreamable")]
    public bool IsStreamable { get; set; }
}
