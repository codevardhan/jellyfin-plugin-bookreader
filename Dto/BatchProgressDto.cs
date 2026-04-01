using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JellyfinBookReader.Dto;

/// <summary>
/// Request body for PUT /api/BookReader/progress/batch
/// </summary>
public class BatchProgressRequest
{
    [JsonPropertyName("updates")]
    public List<BatchProgressItem> Updates { get; set; } = new();
}

public class BatchProgressItem
{
    [JsonPropertyName("bookId")]
    public Guid BookId { get; set; }

    //  Tier 1 

    [JsonPropertyName("percentage")]
    public double Percentage { get; set; }

    [JsonPropertyName("isFinished")]
    public bool IsFinished { get; set; }

    [JsonPropertyName("position")]
    public string? Position { get; set; }

    //  Tier 2 

    [JsonPropertyName("currentPage")]
    public int? CurrentPage { get; set; }

    [JsonPropertyName("totalPages")]
    public int? TotalPages { get; set; }

    //  Tier 3 

    [JsonPropertyName("chapterIndex")]
    public int? ChapterIndex { get; set; }

    [JsonPropertyName("chapterTitle")]
    public string? ChapterTitle { get; set; }

    [JsonPropertyName("pageInChapter")]
    public int? PageInChapter { get; set; }

    [JsonPropertyName("totalPagesInChapter")]
    public int? TotalPagesInChapter { get; set; }

    //  Sync 

    [JsonPropertyName("lastReadAt")]
    public DateTime? LastReadAt { get; set; }
}

/// <summary>
/// Response for PUT /api/BookReader/progress/batch
/// </summary>
public class BatchProgressResponse
{
    [JsonPropertyName("results")]
    public List<BatchProgressResult> Results { get; set; } = new();
}

public class BatchProgressResult
{
    [JsonPropertyName("bookId")]
    public Guid BookId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("serverProgress")]
    public ProgressDto? ServerProgress { get; set; }
}