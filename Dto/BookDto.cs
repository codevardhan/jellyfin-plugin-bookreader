using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JellyfinBookReader.Dto;

public class BookDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("sortTitle")]
    public string SortTitle { get; set; } = string.Empty;

    [JsonPropertyName("authors")]
    public List<string> Authors { get; set; } = new();

    [JsonPropertyName("genres")]
    public List<string> Genres { get; set; } = new();

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }

    [JsonPropertyName("publishedYear")]
    public int? PublishedYear { get; set; }

    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    [JsonPropertyName("coverUrl")]
    public string CoverUrl { get; set; } = string.Empty;

    [JsonPropertyName("dateAdded")]
    public DateTime DateAdded { get; set; }

    [JsonPropertyName("progress")]
    public ProgressDto? Progress { get; set; }
}