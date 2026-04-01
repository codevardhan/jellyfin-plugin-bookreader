using System;
using System.Threading;
using JellyfinBookReader.Data;
using JellyfinBookReader.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyfinBookReader.Tests.Data;

public class ProgressRepositoryTests : IDisposable
{
    private readonly TestDbFixture _fixture;
    private readonly ProgressRepository _repo;

    private static readonly Guid User1 = Guid.NewGuid();
    private static readonly Guid User2 = Guid.NewGuid();
    private static readonly Guid Book1 = Guid.NewGuid();
    private static readonly Guid Book2 = Guid.NewGuid();

    public ProgressRepositoryTests()
    {
        _fixture = new TestDbFixture();
        _repo = new ProgressRepository(_fixture.DbContext, NullLogger<ProgressRepository>.Instance);
    }

    [Fact]
    public void Get_ReturnsNull_WhenNoProgressExists()
    {
        var result = _repo.Get(User1, Book1);
        Assert.Null(result);
    }

    [Fact]
    public void Upsert_CreatesNewProgress_AllFields()
    {
        var update = new ProgressUpdateDto
        {
            Percentage = 25.5,
            CurrentPage = 50,
            TotalPages = 200,
            ChapterIndex = 2,
            ChapterTitle = "Chapter 3",
            PageInChapter = 12,
            TotalPagesInChapter = 30,
            Position = "epubcfi(/6/14)",
            IsFinished = false,
        };

        var (status, _) = _repo.Upsert(User1, Book1, update);
        Assert.Equal("updated", status);

        var progress = _repo.Get(User1, Book1);
        Assert.NotNull(progress);
        Assert.Equal(25.5, progress.Percentage);
        Assert.Equal(50, progress.CurrentPage);
        Assert.Equal(200, progress.TotalPages);
        Assert.Equal(2, progress.ChapterIndex);
        Assert.Equal("Chapter 3", progress.ChapterTitle);
        Assert.Equal(12, progress.PageInChapter);
        Assert.Equal(30, progress.TotalPagesInChapter);
        Assert.Equal("epubcfi(/6/14)", progress.Position);
        Assert.False(progress.IsFinished);
    }

    [Fact]
    public void Upsert_UpdatesExistingProgress()
    {
        var initial = new ProgressUpdateDto { Percentage = 10.0 };
        _repo.Upsert(User1, Book1, initial);

        var updated = new ProgressUpdateDto
        {
            Percentage = 75.0,
            CurrentPage = 150,
            TotalPages = 200,
            IsFinished = false,
        };
        var (status, _) = _repo.Upsert(User1, Book1, updated);

        Assert.Equal("updated", status);
        var progress = _repo.Get(User1, Book1);
        Assert.NotNull(progress);
        Assert.Equal(75.0, progress.Percentage);
        Assert.Equal(150, progress.CurrentPage);
    }

    [Fact]
    public void Upsert_DetectsConflict_WhenClientIsOlderThanServer()
    {
        _repo.Upsert(User1, Book1, new ProgressUpdateDto { Percentage = 50.0 });

        Thread.Sleep(50);

        var staleUpdate = new ProgressUpdateDto
        {
            Percentage = 60.0,
            LastReadAt = DateTime.UtcNow.AddHours(-1),
        };

        var (status, serverProgress) = _repo.Upsert(User1, Book1, staleUpdate);

        Assert.Equal("conflict", status);
        Assert.NotNull(serverProgress);
        Assert.Equal(50.0, serverProgress.Percentage);
    }

    [Fact]
    public void Upsert_NoConflict_WhenClientIsNewerThanServer()
    {
        _repo.Upsert(User1, Book1, new ProgressUpdateDto { Percentage = 50.0 });

        var freshUpdate = new ProgressUpdateDto
        {
            Percentage = 80.0,
            LastReadAt = DateTime.UtcNow.AddHours(1),
        };

        var (status, _) = _repo.Upsert(User1, Book1, freshUpdate);
        Assert.Equal("updated", status);

        var progress = _repo.Get(User1, Book1);
        Assert.Equal(80.0, progress!.Percentage);
    }

    [Fact]
    public void Upsert_NoConflict_WhenNoLastReadAtProvided()
    {
        _repo.Upsert(User1, Book1, new ProgressUpdateDto { Percentage = 50.0 });

        var (status, _) = _repo.Upsert(User1, Book1, new ProgressUpdateDto { Percentage = 90.0 });
        Assert.Equal("updated", status);
    }

    [Fact]
    public void GetAllForUser_ReturnsAllBooksForUser()
    {
        _repo.Upsert(User1, Book1, new ProgressUpdateDto { Percentage = 25.0 });
        _repo.Upsert(User1, Book2, new ProgressUpdateDto { Percentage = 50.0 });
        _repo.Upsert(User2, Book1, new ProgressUpdateDto { Percentage = 75.0 });

        var user1Progress = _repo.GetAllForUser(User1);
        Assert.Equal(2, user1Progress.Count);
        Assert.True(user1Progress.ContainsKey(Book1));
        Assert.True(user1Progress.ContainsKey(Book2));
        Assert.Equal(25.0, user1Progress[Book1].Percentage);
        Assert.Equal(50.0, user1Progress[Book2].Percentage);

        var user2Progress = _repo.GetAllForUser(User2);
        Assert.Single(user2Progress);
    }

    [Fact]
    public void GetAllForUser_ReturnsEmpty_WhenNoProgress()
    {
        var result = _repo.GetAllForUser(Guid.NewGuid());
        Assert.Empty(result);
    }

    [Fact]
    public void Delete_RemovesProgress()
    {
        _repo.Upsert(User1, Book1, new ProgressUpdateDto { Percentage = 50.0 });

        var deleted = _repo.Delete(User1, Book1);
        Assert.True(deleted);

        var progress = _repo.Get(User1, Book1);
        Assert.Null(progress);
    }

    [Fact]
    public void Delete_ReturnsFalse_WhenNoProgressExists()
    {
        var deleted = _repo.Delete(User1, Guid.NewGuid());
        Assert.False(deleted);
    }

    [Fact]
    public void Progress_IsolatedPerUser()
    {
        _repo.Upsert(User1, Book1, new ProgressUpdateDto { Percentage = 30.0 });
        _repo.Upsert(User2, Book1, new ProgressUpdateDto { Percentage = 70.0 });

        Assert.Equal(30.0, _repo.Get(User1, Book1)!.Percentage);
        Assert.Equal(70.0, _repo.Get(User2, Book1)!.Percentage);

        _repo.Delete(User1, Book1);
        Assert.Null(_repo.Get(User1, Book1));
        Assert.NotNull(_repo.Get(User2, Book1));
    }

    [Fact]
    public void Upsert_HandlesNullOptionalFields()
    {
        var update = new ProgressUpdateDto
        {
            Percentage = 10.0,
            CurrentPage = null,
            TotalPages = null,
            ChapterIndex = null,
            ChapterTitle = null,
            PageInChapter = null,
            TotalPagesInChapter = null,
            Position = null,
            IsFinished = false,
        };

        _repo.Upsert(User1, Book1, update);
        var progress = _repo.Get(User1, Book1);

        Assert.NotNull(progress);
        Assert.Equal(10.0, progress.Percentage);
        Assert.Null(progress.CurrentPage);
        Assert.Null(progress.TotalPages);
        Assert.Null(progress.ChapterIndex);
        Assert.Null(progress.ChapterTitle);
        Assert.Null(progress.PageInChapter);
        Assert.Null(progress.TotalPagesInChapter);
        Assert.Null(progress.Position);
    }

    [Fact]
    public void Upsert_SetsLastReadAt_Automatically()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        _repo.Upsert(User1, Book1, new ProgressUpdateDto { Percentage = 50.0 });
        var after = DateTime.UtcNow.AddSeconds(1);

        var progress = _repo.Get(User1, Book1);
        Assert.NotNull(progress);
        Assert.InRange(progress.LastReadAt, before, after);
    }

    [Fact]
    public void Upsert_OverwritesAllFields_OnUpdate()
    {
        // First insert with all chapter info
        _repo.Upsert(User1, Book1, new ProgressUpdateDto
        {
            Percentage = 25.0,
            ChapterIndex = 2,
            ChapterTitle = "Chapter 3",
            PageInChapter = 5,
            TotalPagesInChapter = 20,
            Position = "epubcfi(/6/14)",
        });

        // Second insert clears chapter info (e.g. switching formats)
        _repo.Upsert(User1, Book1, new ProgressUpdateDto
        {
            Percentage = 50.0,
            ChapterIndex = null,
            ChapterTitle = null,
            PageInChapter = null,
            TotalPagesInChapter = null,
            Position = null,
        });

        var progress = _repo.Get(User1, Book1);
        Assert.NotNull(progress);
        Assert.Equal(50.0, progress.Percentage);
        Assert.Null(progress.ChapterIndex);
        Assert.Null(progress.ChapterTitle);
        Assert.Null(progress.PageInChapter);
        Assert.Null(progress.TotalPagesInChapter);
        Assert.Null(progress.Position);
    }

    [Fact]
    public void Upsert_HandlesZeroPercentage()
    {
        _repo.Upsert(User1, Book1, new ProgressUpdateDto { Percentage = 0.0 });

        var progress = _repo.Get(User1, Book1);
        Assert.NotNull(progress);
        Assert.Equal(0.0, progress.Percentage);
        Assert.False(progress.IsFinished);
    }

    [Fact]
    public void Upsert_HandlesExactly100Percent()
    {
        _repo.Upsert(User1, Book1, new ProgressUpdateDto
        {
            Percentage = 100.0,
            IsFinished = true,
        });

        var progress = _repo.Get(User1, Book1);
        Assert.NotNull(progress);
        Assert.Equal(100.0, progress.Percentage);
        Assert.True(progress.IsFinished);
    }

    [Fact]
    public void Upsert_ConflictDetection_ExactSameTimestamp_NoConflict()
    {
        // First insert
        _repo.Upsert(User1, Book1, new ProgressUpdateDto { Percentage = 50.0 });

        // Read back to get exact server timestamp
        var current = _repo.Get(User1, Book1);
        Assert.NotNull(current);

        // Update with exact same timestamp — should NOT conflict (not strictly older)
        var update = new ProgressUpdateDto
        {
            Percentage = 60.0,
            LastReadAt = current.LastReadAt, // exact same = not older
        };

        var (status, _) = _repo.Upsert(User1, Book1, update);
        // Equal timestamps are not "older than", so no conflict
        Assert.Equal("updated", status);
    }

    [Fact]
    public void GetAllForUser_CorrectlyMapsAllFields()
    {
        var update = new ProgressUpdateDto
        {
            Percentage = 42.5,
            CurrentPage = 85,
            TotalPages = 200,
            ChapterIndex = 4,
            ChapterTitle = "The Beginning",
            PageInChapter = 10,
            TotalPagesInChapter = 25,
            Position = "epubcfi(/6/28)",
            IsFinished = false,
        };

        _repo.Upsert(User1, Book1, update);

        var allProgress = _repo.GetAllForUser(User1);
        Assert.Single(allProgress);

        var progress = allProgress[Book1];
        Assert.Equal(42.5, progress.Percentage);
        Assert.Equal(85, progress.CurrentPage);
        Assert.Equal(200, progress.TotalPages);
        Assert.Equal(4, progress.ChapterIndex);
        Assert.Equal("The Beginning", progress.ChapterTitle);
        Assert.Equal(10, progress.PageInChapter);
        Assert.Equal(25, progress.TotalPagesInChapter);
        Assert.Equal("epubcfi(/6/28)", progress.Position);
    }

    [Fact]
    public void Delete_DoesNotAffectOtherBooks()
    {
        _repo.Upsert(User1, Book1, new ProgressUpdateDto { Percentage = 25.0 });
        _repo.Upsert(User1, Book2, new ProgressUpdateDto { Percentage = 75.0 });

        _repo.Delete(User1, Book1);

        Assert.Null(_repo.Get(User1, Book1));
        Assert.NotNull(_repo.Get(User1, Book2));
        Assert.Equal(75.0, _repo.Get(User1, Book2)!.Percentage);
    }

    [Fact]
    public void Upsert_HandlesUnicodeChapterTitle()
    {
        _repo.Upsert(User1, Book1, new ProgressUpdateDto
        {
            Percentage = 10.0,
            ChapterTitle = "第一章：开始 — ñ ü ö ☕ 🎉",
        });

        var progress = _repo.Get(User1, Book1);
        Assert.NotNull(progress);
        Assert.Equal("第一章：开始 — ñ ü ö ☕ 🎉", progress.ChapterTitle);
    }

    [Fact]
    public void Upsert_HandlesLongPosition_String()
    {
        // EPUB CFI strings can be quite long
        var longPosition = "epubcfi(/6/14[chap05ref]!/4[body01]/10[para05]/2/1:3)" +
                          new string('x', 500);

        _repo.Upsert(User1, Book1, new ProgressUpdateDto
        {
            Percentage = 30.0,
            Position = longPosition,
        });

        var progress = _repo.Get(User1, Book1);
        Assert.NotNull(progress);
        Assert.Equal(longPosition, progress.Position);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}