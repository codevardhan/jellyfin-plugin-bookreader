using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JellyfinBookReader.Dto;

//  Requests 

public class StartSessionRequest
{
    [JsonPropertyName("bookId")]
    public Guid BookId { get; set; }
}

public class HeartbeatRequest
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;
}

public class EndSessionRequest
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("pagesRead")]
    public int? PagesRead { get; set; }

    [JsonPropertyName("percentageAdvanced")]
    public double? PercentageAdvanced { get; set; }
}

//  Responses 

public class StartSessionResponse
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }
}

public class ReadingStatsDto
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("totalReadingTimeSeconds")]
    public long TotalReadingTimeSeconds { get; set; }

    [JsonPropertyName("totalSessions")]
    public int TotalSessions { get; set; }

    [JsonPropertyName("totalBooksFinished")]
    public int TotalBooksFinished { get; set; }

    [JsonPropertyName("currentStreak")]
    public int CurrentStreak { get; set; }

    [JsonPropertyName("longestStreak")]
    public int LongestStreak { get; set; }

    [JsonPropertyName("dailyAverageSeconds")]
    public long DailyAverageSeconds { get; set; }

    [JsonPropertyName("last30Days")]
    public Last30DaysStats Last30Days { get; set; } = new();

    [JsonPropertyName("perBook")]
    public List<PerBookStats> PerBook { get; set; } = new();
}

public class Last30DaysStats
{
    [JsonPropertyName("readingTimeSeconds")]
    public long ReadingTimeSeconds { get; set; }

    [JsonPropertyName("sessionsCount")]
    public int SessionsCount { get; set; }

    [JsonPropertyName("booksFinished")]
    public int BooksFinished { get; set; }
}

public class PerBookStats
{
    [JsonPropertyName("bookId")]
    public string BookId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("totalTimeSeconds")]
    public long TotalTimeSeconds { get; set; }

    [JsonPropertyName("sessionsCount")]
    public int SessionsCount { get; set; }
}