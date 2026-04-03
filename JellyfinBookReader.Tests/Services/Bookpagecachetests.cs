using System;
using System.IO;
using JellyfinBookReader.Services;
using Xunit;

namespace JellyfinBookReader.Tests.Services;

/// <summary>
/// Tests for BookPageCache — the coordinator that owns per-book IPageCacheStore instances.
///
/// Plugin.Instance is null in tests, so ThresholdBytes falls back to 50 MB.
/// A small temp file is always below that threshold, so GetOrCreateStore always
/// returns an InMemoryPageCacheStore in this test suite.
/// </summary>
public class BookPageCacheTests : IDisposable
{
    private readonly BookPageCache _cache;
    private readonly string _tempFile;

    public BookPageCacheTests()
    {
        _cache = new BookPageCache();

        // A tiny file well below the 50 MB threshold → InMemoryPageCacheStore.
        _tempFile = Path.GetTempFileName();
        File.WriteAllBytes(_tempFile, new byte[] { 0x01, 0x02, 0x03 });
    }

    public void Dispose()
    {
        try { File.Delete(_tempFile); } catch { }
        GC.SuppressFinalize(this);
    }

    //  GetOrCreateStore 

    [Fact]
    public void GetOrCreateStore_ReturnsSameInstanceForSameBookId()
    {
        var bookId = Guid.NewGuid();
        var s1 = _cache.GetOrCreateStore(bookId, _tempFile);
        var s2 = _cache.GetOrCreateStore(bookId, _tempFile);

        Assert.Same(s1, s2);
    }

    [Fact]
    public void GetOrCreateStore_ReturnsDifferentInstancesForDifferentBookIds()
    {
        var s1 = _cache.GetOrCreateStore(Guid.NewGuid(), _tempFile);
        var s2 = _cache.GetOrCreateStore(Guid.NewGuid(), _tempFile);

        Assert.NotSame(s1, s2);
    }

    [Fact]
    public void GetOrCreateStore_CreatesInMemoryStoreForSmallFile()
    {
        var store = _cache.GetOrCreateStore(Guid.NewGuid(), _tempFile);
        Assert.IsType<InMemoryPageCacheStore>(store);
    }

    //  TryGet 

    [Fact]
    public void TryGet_ReturnsFalseWhenNoStoreExistsForBook()
    {
        var hit = _cache.TryGet(Guid.NewGuid(), 0, out var data, out var ct);

        Assert.False(hit);
        Assert.Null(data);
        Assert.Null(ct);
    }

    //  Set + TryGet round-trip 

    [Fact]
    public void Set_ThenTryGet_ReturnsStoredData()
    {
        var bookId = Guid.NewGuid();
        var expected = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        _cache.Set(bookId, _tempFile, 0, expected, "image/jpeg");
        var hit = _cache.TryGet(bookId, 0, out var data, out var ct);

        Assert.True(hit);
        Assert.Equal(expected, data);
        Assert.Equal("image/jpeg", ct);
    }

    [Fact]
    public void Set_DoesNotLeakAcrossBooks()
    {
        var bookA = Guid.NewGuid();
        var bookB = Guid.NewGuid();

        _cache.Set(bookA, _tempFile, 0, new byte[] { 0x01 }, "image/jpeg");

        var hit = _cache.TryGet(bookB, 0, out _, out _);
        Assert.False(hit);
    }

    [Fact]
    public void Set_MultiplePages_AllRetrievable()
    {
        var bookId = Guid.NewGuid();

        for (var i = 0; i < 10; i++)
            _cache.Set(bookId, _tempFile, i, new byte[] { (byte)i }, "image/jpeg");

        for (var i = 0; i < 10; i++)
        {
            var hit = _cache.TryGet(bookId, i, out var data, out _);
            Assert.True(hit);
            Assert.Equal(new byte[] { (byte)i }, data);
        }
    }

    //  HasPage 

    [Fact]
    public void HasPage_ReturnsFalseWhenNoStoreExists()
    {
        Assert.False(_cache.HasPage(Guid.NewGuid(), 0));
    }

    [Fact]
    public void HasPage_ReturnsTrueAfterSet()
    {
        var bookId = Guid.NewGuid();
        _cache.Set(bookId, _tempFile, 4, new byte[] { 0x01 }, "image/jpeg");

        Assert.True(_cache.HasPage(bookId, 4));
    }

    [Fact]
    public void HasPage_ReturnsFalseForDifferentPageInSameBook()
    {
        var bookId = Guid.NewGuid();
        _cache.Set(bookId, _tempFile, 0, new byte[] { 0x01 }, "image/jpeg");

        Assert.False(_cache.HasPage(bookId, 1));
    }

    //  Evict 

    [Fact]
    public void Evict_RemovesStoreForBook()
    {
        var bookId = Guid.NewGuid();
        _cache.Set(bookId, _tempFile, 0, new byte[] { 0x01 }, "image/jpeg");

        _cache.Evict(bookId);

        // After evict the store is gone — TryGet should return false.
        var hit = _cache.TryGet(bookId, 0, out _, out _);
        Assert.False(hit);
    }

    [Fact]
    public void Evict_DoesNotAffectOtherBooks()
    {
        var bookA = Guid.NewGuid();
        var bookB = Guid.NewGuid();
        _cache.Set(bookA, _tempFile, 0, new byte[] { 0x01 }, "image/jpeg");
        _cache.Set(bookB, _tempFile, 0, new byte[] { 0x02 }, "image/jpeg");

        _cache.Evict(bookA);

        Assert.True(_cache.HasPage(bookB, 0));
    }

    [Fact]
    public void Evict_OnNonexistentBook_DoesNotThrow()
    {
        var ex = Record.Exception(() => _cache.Evict(Guid.NewGuid()));
        Assert.Null(ex);
    }

    [Fact]
    public void Evict_AllowsStoreToBeRecreated()
    {
        var bookId = Guid.NewGuid();
        _cache.Set(bookId, _tempFile, 0, new byte[] { 0x01 }, "image/jpeg");

        _cache.Evict(bookId);
        _cache.Set(bookId, _tempFile, 0, new byte[] { 0x02 }, "image/jpeg");

        _cache.TryGet(bookId, 0, out var data, out _);
        Assert.Equal(new byte[] { 0x02 }, data);
    }
}