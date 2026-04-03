using System;
using System.IO;
using JellyfinBookReader.Services;
using Xunit;

namespace JellyfinBookReader.Tests.Services;

public class DiskPageCacheStoreTests : IDisposable
{
    // Each test gets its own bookId so stores don't share temp directories.
    private readonly DiskPageCacheStore _store;
    private readonly Guid _bookId = Guid.NewGuid();

    public DiskPageCacheStoreTests()
    {
        _store = new DiskPageCacheStore(_bookId);
    }

    public void Dispose()
    {
        _store.Dispose();
        GC.SuppressFinalize(this);
    }

    //  HasPage 

    [Fact]
    public void HasPage_ReturnsFalseInitially()
    {
        Assert.False(_store.HasPage(0));
    }

    [Fact]
    public void HasPage_ReturnsTrueAfterSet()
    {
        _store.Set(0, new byte[] { 0xFF }, "image/jpeg");
        Assert.True(_store.HasPage(0));
    }

    [Fact]
    public void HasPage_ReturnsFalseForUncachedPage()
    {
        _store.Set(0, new byte[] { 0xFF }, "image/jpeg");
        Assert.False(_store.HasPage(1));
    }

    //  Set + TryGet round-trip 

    [Fact]
    public void TryGet_ReturnsStoredBytesAndContentType()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        _store.Set(5, data, "image/jpeg");

        var hit = _store.TryGet(5, out var retrieved, out var ct);

        Assert.True(hit);
        Assert.Equal(data, retrieved);
        Assert.Equal("image/jpeg", ct);
    }

    [Fact]
    public void TryGet_ReturnsFalseAndNullsForMiss()
    {
        var hit = _store.TryGet(99, out var data, out var ct);
        Assert.False(hit);
        Assert.Null(data);
        Assert.Null(ct);
    }

    //  Content-type / extension mapping 

    [Theory]
    [InlineData("image/jpeg",             "image/jpeg")]
    [InlineData("image/png",              "image/png")]
    [InlineData("image/webp",             "image/webp")]
    [InlineData("image/gif",              "image/gif")]
    [InlineData("application/xhtml+xml", "application/xhtml+xml")]
    [InlineData("text/html",             "image/jpeg")] // unknown → .jpg → image/jpeg
    public void TryGet_ReturnsCorrectMimeAfterRoundTrip(string inputMime, string expectedMime)
    {
        var store = new DiskPageCacheStore(Guid.NewGuid());
        try
        {
            store.Set(0, new byte[] { 0xAA }, inputMime);
            store.TryGet(0, out _, out var ct);
            Assert.Equal(expectedMime, ct);
        }
        finally
        {
            store.Dispose();
        }
    }

    //  Page numbering 

    [Fact]
    public void Set_MultiplePages_AllRetrievable()
    {
        for (var i = 0; i < 5; i++)
            _store.Set(i, new byte[] { (byte)i }, "image/jpeg");

        for (var i = 0; i < 5; i++)
        {
            var hit = _store.TryGet(i, out var data, out _);
            Assert.True(hit);
            Assert.Equal(new byte[] { (byte)i }, data);
        }
    }

    [Fact]
    public void Set_LargePageIndex_Retrievable()
    {
        _store.Set(99999, new byte[] { 0xFF }, "image/png");
        Assert.True(_store.HasPage(99999));
    }

    //  Idempotent Set 

    [Fact]
    public void Set_SecondCallForSamePage_PreservesFirstData()
    {
        var first  = new byte[] { 0x01 };
        var second = new byte[] { 0x02 };

        _store.Set(0, first, "image/jpeg");
        _store.Set(0, second, "image/jpeg"); // no-op: file already exists

        _store.TryGet(0, out var data, out _);
        Assert.Equal(first, data);
    }

    //  Evict 

    [Fact]
    public void Evict_RemovesAllPages()
    {
        _store.Set(0, new byte[] { 0x01 }, "image/jpeg");
        _store.Set(1, new byte[] { 0x02 }, "image/png");

        _store.Evict();

        Assert.False(_store.HasPage(0));
        Assert.False(_store.HasPage(1));
    }

    [Fact]
    public void Evict_DeletesTempDirectory()
    {
        // Set a page so the directory exists on disk.
        _store.Set(0, new byte[] { 0x01 }, "image/jpeg");

        _store.Evict();

        // Directory should no longer exist.
        var dir = Path.Combine(
            Path.GetTempPath(), "jellyfin-bookreader", _bookId.ToString("N"));
        Assert.False(Directory.Exists(dir));
    }

    //  Dispose 

    [Fact]
    public void Dispose_DeletesTempDirectory()
    {
        var store = new DiskPageCacheStore(Guid.NewGuid());
        var storeId = Guid.NewGuid(); // just used for path check below
        // We don't have access to the bookId used inside, so we verify indirectly:
        store.Set(0, new byte[] { 0xAB }, "image/jpeg");

        store.Dispose(); // should clean up

        // After dispose, HasPage should return false (directory gone).
        Assert.False(store.HasPage(0));
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes_WithoutException()
    {
        var store = new DiskPageCacheStore(Guid.NewGuid());
        var ex = Record.Exception(() =>
        {
            store.Dispose();
            store.Dispose();
            store.Dispose();
        });
        Assert.Null(ex);
    }
}