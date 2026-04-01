using System;
using System.Text.Json.Serialization;

namespace JellyfinBookReader.Dto;

/// <summary>
/// Reading progress — unified model for all book formats.
///
/// Tier 1 (universal):   percentage, isFinished, lastReadAt, position
/// Tier 2 (page-based):  currentPage, totalPages
/// Tier 3 (chapter-based, EPUB/FB2 only): chapterIndex, chapterTitle, pageInChapter, totalPagesInChapter
///
/// Any client can render a meaningful progress bar from Tier 1 alone.
/// PDF / CBZ / CBR / MOBI / DJVU clients use Tiers 1+2.
/// EPUB / FB2 clients populate all three tiers.
/// </summary>
public class ProgressDto
{
    //  Tier 1: Universal 

    /// <summary>
    /// Overall reading progress, 0–100. The single source of truth for
    /// progress bars, "45% complete", sort-by-progress, etc.
    /// </summary>
    [JsonPropertyName("percentage")]
    public double Percentage { get; set; }

    [JsonPropertyName("isFinished")]
    public bool IsFinished { get; set; }

    [JsonPropertyName("lastReadAt")]
    public DateTime LastReadAt { get; set; }

    /// <summary>
    /// Opaque client-specific restore token. Clients store whatever they
    /// need to resume at the exact sub-page position (e.g. EPUB CFI, scroll
    /// offset, viewport anchor). The server stores and returns it verbatim.
    /// </summary>
    [JsonPropertyName("position")]
    public string? Position { get; set; }

    //  Tier 2: Page position (all formats) 

    /// <summary>1-indexed page number within the book.</summary>
    [JsonPropertyName("currentPage")]
    public int? CurrentPage { get; set; }

    /// <summary>Total pages in the book as determined by the client.</summary>
    [JsonPropertyName("totalPages")]
    public int? TotalPages { get; set; }

    //  Tier 3: Chapter info (EPUB / FB2 only) 

    /// <summary>0-indexed spine/chapter position.</summary>
    [JsonPropertyName("chapterIndex")]
    public int? ChapterIndex { get; set; }

    /// <summary>Human-readable chapter title from the TOC/NCX.</summary>
    [JsonPropertyName("chapterTitle")]
    public string? ChapterTitle { get; set; }

    /// <summary>1-indexed page within the current chapter.</summary>
    [JsonPropertyName("pageInChapter")]
    public int? PageInChapter { get; set; }

    /// <summary>Total pages in the current chapter.</summary>
    [JsonPropertyName("totalPagesInChapter")]
    public int? TotalPagesInChapter { get; set; }
}