using System;
using System.Text.Json.Serialization;

namespace JellyfinBookReader.Dto;

/// <summary>
/// Request body for PUT /api/BookReader/books/{id}/progress.
/// Same field set as ProgressDto, plus optional lastReadAt for conflict detection.
/// </summary>
public class ProgressUpdateDto
{
    //  Tier 1: Universal 

    [JsonPropertyName("percentage")]
    public double Percentage { get; set; }

    [JsonPropertyName("isFinished")]
    public bool IsFinished { get; set; }

    [JsonPropertyName("position")]
    public string? Position { get; set; }

    //  Tier 2: Page position 

    [JsonPropertyName("currentPage")]
    public int? CurrentPage { get; set; }

    [JsonPropertyName("totalPages")]
    public int? TotalPages { get; set; }

    //  Tier 3: Chapter info (EPUB / FB2) 

    [JsonPropertyName("chapterIndex")]
    public int? ChapterIndex { get; set; }

    [JsonPropertyName("chapterTitle")]
    public string? ChapterTitle { get; set; }

    [JsonPropertyName("pageInChapter")]
    public int? PageInChapter { get; set; }

    [JsonPropertyName("totalPagesInChapter")]
    public int? TotalPagesInChapter { get; set; }

    //  Sync 

    /// <summary>
    /// Optional. Used for offline sync conflict detection.
    /// If provided and older than server's value, the update is rejected as a conflict.
    /// </summary>
    [JsonPropertyName("lastReadAt")]
    public DateTime? LastReadAt { get; set; }
}