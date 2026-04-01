using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JellyfinBookReader.Dto;

public class CollectionStatsDto
{
    [JsonPropertyName("totalBooks")]
    public int TotalBooks { get; set; }

    [JsonPropertyName("totalAuthors")]
    public int TotalAuthors { get; set; }

    [JsonPropertyName("formatBreakdown")]
    public Dictionary<string, int> FormatBreakdown { get; set; } = new();

    [JsonPropertyName("totalFileSize")]
    public long TotalFileSize { get; set; }

    [JsonPropertyName("recentlyAdded")]
    public List<BookDto> RecentlyAdded { get; set; } = new();
}