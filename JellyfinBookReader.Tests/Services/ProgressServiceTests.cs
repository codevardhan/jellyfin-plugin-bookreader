using System;
using System.Collections.Generic;
using JellyfinBookReader.Data;
using JellyfinBookReader.Dto;
using JellyfinBookReader.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyfinBookReader.Tests.Services;

public class ProgressServiceTests : IDisposable
{
    private readonly TestDbFixture _fixture;
    private readonly ProgressService _service;

    public ProgressServiceTests()
    {
        _fixture = new TestDbFixture();
        var repo = new ProgressRepository(_fixture.DbContext, NullLogger<ProgressRepository>.Instance);
        _service = new ProgressService(repo, NullLogger<ProgressService>.Instance);
    }

    [Fact]
    public void UpdateProgress_ClampsPercentage_ToValidRange()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        var update = new ProgressUpdateDto { Percentage = 150.0 };
        var (status, _) = _service.UpdateProgress(userId, bookId, update);

        Assert.Equal("updated", status);
        var progress = _service.GetProgress(userId, bookId);
        Assert.NotNull(progress);
        Assert.Equal(100.0, progress.Percentage);
        Assert.True(progress.IsFinished);
    }

    [Fact]
    public void UpdateProgress_ClampsNegativePercentage()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        var update = new ProgressUpdateDto { Percentage = -10.0 };
        _service.UpdateProgress(userId, bookId, update);

        var progress = _service.GetProgress(userId, bookId);
        Assert.NotNull(progress);
        Assert.Equal(0.0, progress.Percentage);
    }

    [Fact]
    public void UpdateProgress_AutoSetsIsFinished_At100Percent()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        var update = new ProgressUpdateDto
        {
            Percentage = 100.0,
            IsFinished = false,
        };
        _service.UpdateProgress(userId, bookId, update);

        var progress = _service.GetProgress(userId, bookId);
        Assert.True(progress!.IsFinished);
    }

    [Fact]
    public void UpdateProgress_DoesNotAutoSetIsFinished_Below100()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        var update = new ProgressUpdateDto
        {
            Percentage = 99.9,
            IsFinished = false,
        };
        _service.UpdateProgress(userId, bookId, update);

        var progress = _service.GetProgress(userId, bookId);
        Assert.False(progress!.IsFinished);
    }

    [Fact]
    public void UpdateProgress_PreservesClientIsFinished_WhenExplicitlyTrue()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        var update = new ProgressUpdateDto
        {
            Percentage = 95.0,
            IsFinished = true,
        };
        _service.UpdateProgress(userId, bookId, update);

        var progress = _service.GetProgress(userId, bookId);
        Assert.True(progress!.IsFinished);
    }

    [Fact]
    public void ClearProgress_ReturnsTrue_WhenProgressExists()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        _service.UpdateProgress(userId, bookId, new ProgressUpdateDto { Percentage = 50.0 });
        Assert.True(_service.ClearProgress(userId, bookId));
    }

    [Fact]
    public void ClearProgress_ReturnsFalse_WhenNoProgress()
    {
        Assert.False(_service.ClearProgress(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void GetAllProgress_ReturnsEmpty_WhenNone()
    {
        var result = _service.GetAllProgress(Guid.NewGuid());
        Assert.Empty(result);
    }

    [Fact]
    public void GetProgress_ReturnsNull_WhenNoProgress()
    {
        var result = _service.GetProgress(Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(result);
    }

    //  Batch Update 

    [Fact]
    public void BatchUpdate_ProcessesMultipleUpdates()
    {
        var userId = Guid.NewGuid();
        var book1 = Guid.NewGuid();
        var book2 = Guid.NewGuid();

        var request = new BatchProgressRequest
        {
            Updates = new List<BatchProgressItem>
            {
                new() { BookId = book1, Percentage = 25.0 },
                new() { BookId = book2, Percentage = 75.0, IsFinished = false },
            }
        };

        var response = _service.BatchUpdate(userId, request);

        Assert.Equal(2, response.Results.Count);
        Assert.All(response.Results, r => Assert.Equal("updated", r.Status));

        Assert.Equal(25.0, _service.GetProgress(userId, book1)!.Percentage);
        Assert.Equal(75.0, _service.GetProgress(userId, book2)!.Percentage);
    }

    [Fact]
    public void BatchUpdate_ClampsPercentages()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        var request = new BatchProgressRequest
        {
            Updates = new List<BatchProgressItem>
            {
                new() { BookId = bookId, Percentage = 200.0 },
            }
        };

        _service.BatchUpdate(userId, request);

        var progress = _service.GetProgress(userId, bookId);
        Assert.Equal(100.0, progress!.Percentage);
        Assert.True(progress.IsFinished);
    }

    [Fact]
    public void BatchUpdate_ClampsNegativePercentages()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        var request = new BatchProgressRequest
        {
            Updates = new List<BatchProgressItem>
            {
                new() { BookId = bookId, Percentage = -50.0 },
            }
        };

        _service.BatchUpdate(userId, request);

        var progress = _service.GetProgress(userId, bookId);
        Assert.Equal(0.0, progress!.Percentage);
    }

    [Fact]
    public void BatchUpdate_AutoSetsIsFinished_At100()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        var request = new BatchProgressRequest
        {
            Updates = new List<BatchProgressItem>
            {
                new() { BookId = bookId, Percentage = 100.0, IsFinished = false },
            }
        };

        _service.BatchUpdate(userId, request);

        var progress = _service.GetProgress(userId, bookId);
        Assert.True(progress!.IsFinished);
    }

    [Fact]
    public void BatchUpdate_HandlesConflicts()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        // Set initial progress
        _service.UpdateProgress(userId, bookId, new ProgressUpdateDto { Percentage = 50.0 });

        // Try batch update with stale timestamp
        var request = new BatchProgressRequest
        {
            Updates = new List<BatchProgressItem>
            {
                new()
                {
                    BookId = bookId,
                    Percentage = 60.0,
                    LastReadAt = DateTime.UtcNow.AddHours(-1),
                },
            }
        };

        var response = _service.BatchUpdate(userId, request);

        Assert.Single(response.Results);
        Assert.Equal("conflict", response.Results[0].Status);
        Assert.NotNull(response.Results[0].ServerProgress);
        Assert.Equal(50.0, response.Results[0].ServerProgress!.Percentage);
    }

    [Fact]
    public void BatchUpdate_MapsAllTierFields()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        var request = new BatchProgressRequest
        {
            Updates = new List<BatchProgressItem>
            {
                new()
                {
                    BookId = bookId,
                    Percentage = 33.3,
                    CurrentPage = 66,
                    TotalPages = 200,
                    ChapterIndex = 3,
                    ChapterTitle = "Chapter 4",
                    PageInChapter = 8,
                    TotalPagesInChapter = 20,
                    Position = "cfi-string",
                },
            }
        };

        _service.BatchUpdate(userId, request);

        var progress = _service.GetProgress(userId, bookId);
        Assert.NotNull(progress);
        Assert.Equal(33.3, progress.Percentage);
        Assert.Equal(66, progress.CurrentPage);
        Assert.Equal(200, progress.TotalPages);
        Assert.Equal(3, progress.ChapterIndex);
        Assert.Equal("Chapter 4", progress.ChapterTitle);
        Assert.Equal(8, progress.PageInChapter);
        Assert.Equal(20, progress.TotalPagesInChapter);
        Assert.Equal("cfi-string", progress.Position);
    }

    [Fact]
    public void BatchUpdate_EmptyUpdates_ReturnsEmptyResults()
    {
        var userId = Guid.NewGuid();
        var request = new BatchProgressRequest { Updates = new List<BatchProgressItem>() };

        // Note: The controller rejects empty batches, but the service handles it gracefully
        var response = _service.BatchUpdate(userId, request);
        Assert.Empty(response.Results);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}