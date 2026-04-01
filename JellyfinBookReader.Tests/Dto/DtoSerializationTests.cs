using System;
using System.Collections.Generic;
using System.Text.Json;
using JellyfinBookReader.Dto;
using Xunit;

namespace JellyfinBookReader.Tests.Dto;

/// <summary>
/// Verify that DTOs serialize/deserialize correctly with the expected JSON property names.
/// This catches accidental renames that would break API clients.
/// </summary>
public class DtoSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null, // rely on JsonPropertyName attributes
    };

    //  BookDto 

    [Fact]
    public void BookDto_SerializesWithExpectedPropertyNames()
    {
        var dto = new BookDto
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Title = "Test Book",
            SortTitle = "test book",
            Authors = new List<string> { "Author One" },
            Genres = new List<string> { "Fiction" },
            Description = "A test book.",
            Publisher = "TestPub",
            PublishedYear = 2024,
            Format = "epub",
            FileSize = 1024000,
            CoverUrl = "/api/BookReader/books/11111111/cover",
            DateAdded = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc),
        };

        var json = JsonSerializer.Serialize(dto, Options);

        Assert.Contains("\"id\":", json);
        Assert.Contains("\"title\":", json);
        Assert.Contains("\"sortTitle\":", json);
        Assert.Contains("\"authors\":", json);
        Assert.Contains("\"genres\":", json);
        Assert.Contains("\"description\":", json);
        Assert.Contains("\"publisher\":", json);
        Assert.Contains("\"publishedYear\":", json);
        Assert.Contains("\"format\":", json);
        Assert.Contains("\"fileSize\":", json);
        Assert.Contains("\"coverUrl\":", json);
        Assert.Contains("\"dateAdded\":", json);
        Assert.Contains("\"progress\":", json);
    }

    [Fact]
    public void BookDto_RoundTrips()
    {
        var dto = new BookDto
        {
            Id = Guid.NewGuid(),
            Title = "Round Trip Book",
            Authors = new List<string> { "Author A", "Author B" },
            Genres = new List<string> { "Sci-Fi", "Fantasy" },
            Format = "epub",
            FileSize = 2048000,
        };

        var json = JsonSerializer.Serialize(dto, Options);
        var deserialized = JsonSerializer.Deserialize<BookDto>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(dto.Id, deserialized.Id);
        Assert.Equal(dto.Title, deserialized.Title);
        Assert.Equal(2, deserialized.Authors.Count);
        Assert.Equal(2, deserialized.Genres.Count);
        Assert.Equal("epub", deserialized.Format);
        Assert.Equal(2048000, deserialized.FileSize);
    }

    //  ProgressDto 

    [Fact]
    public void ProgressDto_SerializesAllTiers()
    {
        var dto = new ProgressDto
        {
            // Tier 1
            Percentage = 42.5,
            IsFinished = false,
            LastReadAt = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc),
            Position = "epubcfi(/6/28)",
            // Tier 2
            CurrentPage = 85,
            TotalPages = 200,
            // Tier 3
            ChapterIndex = 4,
            ChapterTitle = "Chapter 5",
            PageInChapter = 10,
            TotalPagesInChapter = 25,
        };

        var json = JsonSerializer.Serialize(dto, Options);

        // Tier 1
        Assert.Contains("\"percentage\":", json);
        Assert.Contains("\"isFinished\":", json);
        Assert.Contains("\"lastReadAt\":", json);
        Assert.Contains("\"position\":", json);
        // Tier 2
        Assert.Contains("\"currentPage\":", json);
        Assert.Contains("\"totalPages\":", json);
        // Tier 3
        Assert.Contains("\"chapterIndex\":", json);
        Assert.Contains("\"chapterTitle\":", json);
        Assert.Contains("\"pageInChapter\":", json);
        Assert.Contains("\"totalPagesInChapter\":", json);
    }

    [Fact]
    public void ProgressDto_RoundTrips()
    {
        var dto = new ProgressDto
        {
            Percentage = 42.5,
            CurrentPage = 85,
            TotalPages = 200,
            ChapterIndex = 4,
            ChapterTitle = "Chapter 5",
            PageInChapter = 10,
            TotalPagesInChapter = 25,
            Position = "epubcfi(/6/28)",
            LastReadAt = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc),
            IsFinished = false,
        };

        var json = JsonSerializer.Serialize(dto, Options);
        var deserialized = JsonSerializer.Deserialize<ProgressDto>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Equal(42.5, deserialized.Percentage);
        Assert.Equal(85, deserialized.CurrentPage);
        Assert.Equal(200, deserialized.TotalPages);
        Assert.Equal(4, deserialized.ChapterIndex);
        Assert.Equal("Chapter 5", deserialized.ChapterTitle);
        Assert.Equal(10, deserialized.PageInChapter);
        Assert.Equal(25, deserialized.TotalPagesInChapter);
        Assert.Equal("epubcfi(/6/28)", deserialized.Position);
        Assert.False(deserialized.IsFinished);
    }

    [Fact]
    public void ProgressDto_NullOptionalFields_SerializeAsNull()
    {
        var dto = new ProgressDto
        {
            Percentage = 10.0,
            IsFinished = false,
            LastReadAt = DateTime.UtcNow,
            // All optional fields left null
        };

        var json = JsonSerializer.Serialize(dto, Options);
        var deserialized = JsonSerializer.Deserialize<ProgressDto>(json, Options);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.CurrentPage);
        Assert.Null(deserialized.TotalPages);
        Assert.Null(deserialized.ChapterIndex);
        Assert.Null(deserialized.ChapterTitle);
        Assert.Null(deserialized.PageInChapter);
        Assert.Null(deserialized.TotalPagesInChapter);
        Assert.Null(deserialized.Position);
    }

    //  ProgressUpdateDto 

    [Fact]
    public void ProgressUpdateDto_DeserializesFromClientJson()
    {
        var clientJson = """
        {
            "percentage": 55.5,
            "currentPage": 110,
            "totalPages": 200,
            "chapterIndex": 6,
            "chapterTitle": "Chapter 7",
            "pageInChapter": 15,
            "totalPagesInChapter": 30,
            "position": "epubcfi(/6/42)",
            "isFinished": false,
            "lastReadAt": "2024-06-15T12:00:00Z"
        }
        """;

        var dto = JsonSerializer.Deserialize<ProgressUpdateDto>(clientJson, Options);
        Assert.NotNull(dto);
        Assert.Equal(55.5, dto.Percentage);
        Assert.Equal(110, dto.CurrentPage);
        Assert.Equal(200, dto.TotalPages);
        Assert.Equal(6, dto.ChapterIndex);
        Assert.Equal("Chapter 7", dto.ChapterTitle);
        Assert.Equal(15, dto.PageInChapter);
        Assert.Equal(30, dto.TotalPagesInChapter);
        Assert.Equal("epubcfi(/6/42)", dto.Position);
        Assert.False(dto.IsFinished);
        Assert.NotNull(dto.LastReadAt);
    }

    [Fact]
    public void ProgressUpdateDto_DeserializesMinimalPayload()
    {
        // Clients should be able to send just percentage
        var clientJson = """{ "percentage": 42.0 }""";

        var dto = JsonSerializer.Deserialize<ProgressUpdateDto>(clientJson, Options);
        Assert.NotNull(dto);
        Assert.Equal(42.0, dto.Percentage);
        Assert.Null(dto.CurrentPage);
        Assert.Null(dto.ChapterIndex);
        Assert.Null(dto.Position);
        Assert.Null(dto.LastReadAt);
        Assert.False(dto.IsFinished);
    }

    //  BatchProgress 

    [Fact]
    public void BatchProgressRequest_DeserializesCorrectly()
    {
        var json = """
        {
            "updates": [
                { "bookId": "11111111-1111-1111-1111-111111111111", "percentage": 50.0 },
                { "bookId": "22222222-2222-2222-2222-222222222222", "percentage": 100.0, "isFinished": true }
            ]
        }
        """;

        var dto = JsonSerializer.Deserialize<BatchProgressRequest>(json, Options);
        Assert.NotNull(dto);
        Assert.Equal(2, dto.Updates.Count);
        Assert.Equal(50.0, dto.Updates[0].Percentage);
        Assert.True(dto.Updates[1].IsFinished);
    }

    [Fact]
    public void BatchProgressResponse_SerializesCorrectly()
    {
        var response = new BatchProgressResponse
        {
            Results = new List<BatchProgressResult>
            {
                new() { BookId = Guid.NewGuid(), Status = "updated" },
                new()
                {
                    BookId = Guid.NewGuid(),
                    Status = "conflict",
                    ServerProgress = new ProgressDto { Percentage = 50.0, LastReadAt = DateTime.UtcNow },
                },
            },
        };

        var json = JsonSerializer.Serialize(response, Options);
        Assert.Contains("\"results\":", json);
        Assert.Contains("\"status\":\"updated\"", json);
        Assert.Contains("\"status\":\"conflict\"", json);
        Assert.Contains("\"serverProgress\":", json);
    }

    [Fact]
    public void BatchProgressItem_DeserializesAllTiers()
    {
        var json = """
        {
            "bookId": "11111111-1111-1111-1111-111111111111",
            "percentage": 33.3,
            "currentPage": 66,
            "totalPages": 200,
            "chapterIndex": 3,
            "chapterTitle": "Chapter 4",
            "pageInChapter": 8,
            "totalPagesInChapter": 20,
            "position": "cfi-string",
            "isFinished": false,
            "lastReadAt": "2024-06-15T12:00:00Z"
        }
        """;

        var dto = JsonSerializer.Deserialize<BatchProgressItem>(json, Options);
        Assert.NotNull(dto);
        Assert.Equal(33.3, dto.Percentage);
        Assert.Equal(3, dto.ChapterIndex);
        Assert.Equal("Chapter 4", dto.ChapterTitle);
        Assert.Equal(8, dto.PageInChapter);
        Assert.Equal(20, dto.TotalPagesInChapter);
        Assert.Equal("cfi-string", dto.Position);
    }

    //  Session DTOs 

    [Fact]
    public void SessionDtos_SerializeCorrectly()
    {
        var startReq = new StartSessionRequest
        {
            BookId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        };

        var json = JsonSerializer.Serialize(startReq, Options);
        Assert.Contains("\"bookId\":", json);

        var startResp = new StartSessionResponse
        {
            SessionId = "abc-123",
            StartedAt = DateTime.UtcNow,
        };
        var respJson = JsonSerializer.Serialize(startResp, Options);
        Assert.Contains("\"sessionId\":", respJson);
        Assert.Contains("\"startedAt\":", respJson);
    }

    [Fact]
    public void HeartbeatRequest_DeserializesCorrectly()
    {
        var json = """{ "sessionId": "session-abc-123" }""";
        var dto = JsonSerializer.Deserialize<HeartbeatRequest>(json, Options);
        Assert.NotNull(dto);
        Assert.Equal("session-abc-123", dto.SessionId);
    }

    [Fact]
    public void EndSessionRequest_DeserializesAllFields()
    {
        var json = """
        {
            "sessionId": "session-abc-123",
            "pagesRead": 15,
            "percentageAdvanced": 7.5
        }
        """;

        var dto = JsonSerializer.Deserialize<EndSessionRequest>(json, Options);
        Assert.NotNull(dto);
        Assert.Equal("session-abc-123", dto.SessionId);
        Assert.Equal(15, dto.PagesRead);
        Assert.Equal(7.5, dto.PercentageAdvanced);
    }

    [Fact]
    public void EndSessionRequest_DeserializesWithoutOptionalFields()
    {
        var json = """{ "sessionId": "session-abc-123" }""";
        var dto = JsonSerializer.Deserialize<EndSessionRequest>(json, Options);
        Assert.NotNull(dto);
        Assert.Null(dto.PagesRead);
        Assert.Null(dto.PercentageAdvanced);
    }

    //  ReadingStatsDto 

    [Fact]
    public void ReadingStatsDto_SerializesAllFields()
    {
        var dto = new ReadingStatsDto
        {
            UserId = "user-1",
            TotalReadingTimeSeconds = 3600,
            TotalSessions = 10,
            TotalBooksFinished = 2,
            CurrentStreak = 5,
            LongestStreak = 12,
            DailyAverageSeconds = 1800,
            Last30Days = new Last30DaysStats
            {
                ReadingTimeSeconds = 1200,
                SessionsCount = 4,
                BooksFinished = 1,
            },
            PerBook = new List<PerBookStats>
            {
                new() { BookId = "book-1", Title = "Test", TotalTimeSeconds = 600, SessionsCount = 3 },
            },
        };

        var json = JsonSerializer.Serialize(dto, Options);

        Assert.Contains("\"totalReadingTimeSeconds\":", json);
        Assert.Contains("\"currentStreak\":", json);
        Assert.Contains("\"longestStreak\":", json);
        Assert.Contains("\"dailyAverageSeconds\":", json);
        Assert.Contains("\"last30Days\":", json);
        Assert.Contains("\"perBook\":", json);
    }

    //  CollectionStatsDto 

    [Fact]
    public void CollectionStatsDto_SerializesCorrectly()
    {
        var dto = new CollectionStatsDto
        {
            TotalBooks = 150,
            TotalAuthors = 45,
            FormatBreakdown = new Dictionary<string, int>
            {
                { "epub", 100 },
                { "pdf", 50 },
            },
            TotalFileSize = 1024 * 1024 * 500L,
        };

        var json = JsonSerializer.Serialize(dto, Options);
        Assert.Contains("\"totalBooks\":150", json);
        Assert.Contains("\"totalAuthors\":45", json);
        Assert.Contains("\"formatBreakdown\":", json);
        Assert.Contains("\"totalFileSize\":", json);
    }

    //  PagedResult 

    [Fact]
    public void PagedResult_SerializesCorrectly()
    {
        var dto = new PagedResult<BookDto>
        {
            Items = new List<BookDto> { new() { Title = "Test" } },
            TotalCount = 100,
            Limit = 20,
            Offset = 0,
        };

        var json = JsonSerializer.Serialize(dto, Options);
        Assert.Contains("\"items\":", json);
        Assert.Contains("\"totalCount\":100", json);
        Assert.Contains("\"limit\":20", json);
        Assert.Contains("\"offset\":0", json);
    }

    [Fact]
    public void PagedResult_EmptyItems_SerializesAsEmptyArray()
    {
        var dto = new PagedResult<BookDto>
        {
            Items = new List<BookDto>(),
            TotalCount = 0,
            Limit = 20,
            Offset = 0,
        };

        var json = JsonSerializer.Serialize(dto, Options);
        Assert.Contains("\"items\":[]", json);
        Assert.Contains("\"totalCount\":0", json);
    }

    //  BookQueryParams defaults 

    [Fact]
    public void BookQueryParams_HasCorrectDefaults()
    {
        var q = new BookQueryParams();

        Assert.Null(q.Search);
        Assert.Null(q.Author);
        Assert.Null(q.Genre);
        Assert.Null(q.Format);
        Assert.Null(q.Status);
        Assert.Null(q.LibraryId);
        Assert.Equal("title", q.Sort);
        Assert.Equal("asc", q.SortOrder);
        Assert.Null(q.Limit);
        Assert.Equal(0, q.Offset);
    }
}