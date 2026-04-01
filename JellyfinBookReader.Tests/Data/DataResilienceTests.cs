using System;
using System.Globalization;
using JellyfinBookReader.Data;
using JellyfinBookReader.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyfinBookReader.Tests.Data;

/// <summary>
/// Tests for data resilience — corrupt DB values, boundary conditions, etc.
/// </summary>
public class DataResilienceTests : IDisposable
{
    private readonly TestDbFixture _fixture;
    private readonly ProgressRepository _repo;

    public DataResilienceTests()
    {
        _fixture = new TestDbFixture();
        _repo = new ProgressRepository(_fixture.DbContext, NullLogger<ProgressRepository>.Instance);
    }

    [Fact]
    public void MapRow_HandlesCorruptLastReadAt_Gracefully()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        // Insert progress with valid data first
        _repo.Upsert(userId, bookId, new ProgressUpdateDto { Percentage = 50.0 });

        // Corrupt the LastReadAt value via raw SQL
        using var conn = _fixture.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE ReadingProgress 
            SET LastReadAt = 'not-a-date'
            WHERE UserId = @userId AND BookId = @bookId";
        cmd.Parameters.AddWithValue("@userId", userId.ToString());
        cmd.Parameters.AddWithValue("@bookId", bookId.ToString());
        cmd.ExecuteNonQuery();

        // Should not throw — should fallback gracefully
        var progress = _repo.Get(userId, bookId);
        Assert.NotNull(progress);
        Assert.Equal(50.0, progress.Percentage);
        // LastReadAt should be a valid DateTime (fallback to UtcNow)
        Assert.True(progress.LastReadAt > DateTime.MinValue);
    }

    [Fact]
    public void MapRow_HandlesEmptyLastReadAt_Gracefully()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        // Insert with valid data
        _repo.Upsert(userId, bookId, new ProgressUpdateDto { Percentage = 25.0 });

        // Set LastReadAt to empty string via raw SQL
        // (column is NOT NULL, so we test the TryParse fallback with unparseable data)
        using var conn = _fixture.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE ReadingProgress
            SET LastReadAt = ''
            WHERE UserId = @userId AND BookId = @bookId";
        cmd.Parameters.AddWithValue("@userId", userId.ToString());
        cmd.Parameters.AddWithValue("@bookId", bookId.ToString());
        cmd.ExecuteNonQuery();

        // Should not throw — falls back to DateTime.UtcNow
        var progress = _repo.Get(userId, bookId);
        Assert.NotNull(progress);
        Assert.True(progress.LastReadAt > DateTime.MinValue);
    }

    [Fact]
    public void Upsert_HandlesEmptyStringPosition()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        _repo.Upsert(userId, bookId, new ProgressUpdateDto
        {
            Percentage = 10.0,
            Position = "",
        });

        var progress = _repo.Get(userId, bookId);
        Assert.NotNull(progress);
        Assert.Equal("", progress.Position);
    }

    [Fact]
    public void Upsert_HandlesEmptyStringChapterTitle()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        _repo.Upsert(userId, bookId, new ProgressUpdateDto
        {
            Percentage = 10.0,
            ChapterTitle = "",
        });

        var progress = _repo.Get(userId, bookId);
        Assert.NotNull(progress);
        Assert.Equal("", progress.ChapterTitle);
    }

    [Fact]
    public void Upsert_HandlesMaxIntPages()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        _repo.Upsert(userId, bookId, new ProgressUpdateDto
        {
            Percentage = 50.0,
            CurrentPage = int.MaxValue,
            TotalPages = int.MaxValue,
        });

        var progress = _repo.Get(userId, bookId);
        Assert.NotNull(progress);
        Assert.Equal(int.MaxValue, progress.CurrentPage);
        Assert.Equal(int.MaxValue, progress.TotalPages);
    }

    [Fact]
    public void Upsert_HandlesZeroPages()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        _repo.Upsert(userId, bookId, new ProgressUpdateDto
        {
            Percentage = 0.0,
            CurrentPage = 0,
            TotalPages = 0,
            ChapterIndex = 0,
            PageInChapter = 0,
            TotalPagesInChapter = 0,
        });

        var progress = _repo.Get(userId, bookId);
        Assert.NotNull(progress);
        Assert.Equal(0, progress.CurrentPage);
        Assert.Equal(0, progress.TotalPages);
        Assert.Equal(0, progress.ChapterIndex);
    }

    [Fact]
    public void GetAllForUser_HandlesLargeNumberOfBooks()
    {
        var userId = Guid.NewGuid();

        // Insert 50 books worth of progress
        for (int i = 0; i < 50; i++)
        {
            _repo.Upsert(userId, Guid.NewGuid(), new ProgressUpdateDto
            {
                Percentage = i * 2.0,
            });
        }

        var all = _repo.GetAllForUser(userId);
        Assert.Equal(50, all.Count);
    }

    [Fact]
    public void ConflictDetection_HandlesRoundTripDatePrecision()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        // Insert progress
        _repo.Upsert(userId, bookId, new ProgressUpdateDto { Percentage = 50.0 });

        // Read back the exact server timestamp
        var serverProgress = _repo.Get(userId, bookId);
        Assert.NotNull(serverProgress);

        // A client sending back a date 1 millisecond *older* should conflict
        var slightlyOlder = serverProgress.LastReadAt.AddMilliseconds(-1);
        var (status, _) = _repo.Upsert(userId, bookId, new ProgressUpdateDto
        {
            Percentage = 60.0,
            LastReadAt = slightlyOlder,
        });

        Assert.Equal("conflict", status);
    }

    [Fact]
    public void Upsert_RapidSuccessiveUpdates_AllSucceed()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        // Simulate rapid page turns
        for (int i = 1; i <= 20; i++)
        {
            var (status, _) = _repo.Upsert(userId, bookId, new ProgressUpdateDto
            {
                Percentage = i * 5.0,
                CurrentPage = i,
                TotalPages = 20,
            });
            Assert.Equal("updated", status);
        }

        var progress = _repo.Get(userId, bookId);
        Assert.NotNull(progress);
        Assert.Equal(100.0, progress.Percentage);
        Assert.Equal(20, progress.CurrentPage);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}