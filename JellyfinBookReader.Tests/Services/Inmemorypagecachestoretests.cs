using System;
using JellyfinBookReader.Services;
using Xunit;

namespace JellyfinBookReader.Tests.Services;

public class InMemoryPageCacheStoreTests
{
    private static InMemoryPageCacheStore MakeStore() => new();

    //  HasPage 

    [Fact]
    public void HasPage_ReturnsFalseForUncachedPage()
    {
        var store = MakeStore();
        Assert.False(store.HasPage(0));
    }

    [Fact]
    public void HasPage_ReturnsTrueAfterSet()
    {
        var store = MakeStore();
        store.Set(3, new byte[] { 0x01 }, "image/jpeg");
        Assert.True(store.HasPage(3));
    }

    [Fact]
    public void HasPage_ReturnsFalseForDifferentPage()
    {
        var store = MakeStore();
        store.Set(0, new byte[] { 0x01 }, "image/jpeg");
        Assert.False(store.HasPage(1));
    }

    //  TryGet 

    [Fact]
    public void TryGet_ReturnsFalseAndNullsForMiss()
    {
        var store = MakeStore();
        var hit = store.TryGet(5, out var data, out var ct);

        Assert.False(hit);
        Assert.Null(data);
        Assert.Null(ct);
    }

    [Fact]
    public void TryGet_ReturnsTrueAndDataForHit()
    {
        var store = MakeStore();
        var expected = new byte[] { 0xAA, 0xBB, 0xCC };
        store.Set(7, expected, "image/png");

        var hit = store.TryGet(7, out var data, out var ct);

        Assert.True(hit);
        Assert.Equal(expected, data);
        Assert.Equal("image/png", ct);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    [InlineData("image/gif")]
    [InlineData("application/xhtml+xml")]
    public void TryGet_PreservesContentType(string contentType)
    {
        var store = MakeStore();
        store.Set(0, new byte[] { 0x01 }, contentType);

        store.TryGet(0, out _, out var ct);
        Assert.Equal(contentType, ct);
    }

    //  Idempotent Set (TryAdd semantics) 

    [Fact]
    public void Set_SecondCallForSamePage_PreservesFirstData()
    {
        var store = MakeStore();
        var first  = new byte[] { 0x01 };
        var second = new byte[] { 0x02 };

        store.Set(0, first, "image/jpeg");
        store.Set(0, second, "image/jpeg"); // should be a no-op

        store.TryGet(0, out var data, out _);
        Assert.Equal(first, data);
    }

    //  Evict 

    [Fact]
    public void Evict_ClearsAllPages()
    {
        var store = MakeStore();
        store.Set(0, new byte[] { 0x01 }, "image/jpeg");
        store.Set(1, new byte[] { 0x02 }, "image/jpeg");
        store.Set(2, new byte[] { 0x03 }, "image/jpeg");

        store.Evict();

        Assert.False(store.HasPage(0));
        Assert.False(store.HasPage(1));
        Assert.False(store.HasPage(2));
    }

    [Fact]
    public void Evict_AllowsReuseAfterClear()
    {
        var store = MakeStore();
        store.Set(0, new byte[] { 0x01 }, "image/jpeg");
        store.Evict();

        store.Set(0, new byte[] { 0x02 }, "image/jpeg");

        store.TryGet(0, out var data, out _);
        Assert.Equal(new byte[] { 0x02 }, data);
    }

    //  Dispose 

    [Fact]
    public void Dispose_ClearsPages()
    {
        var store = MakeStore();
        store.Set(0, new byte[] { 0x01 }, "image/jpeg");

        store.Dispose();

        Assert.False(store.HasPage(0));
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var store = MakeStore();
        var ex = Record.Exception(() =>
        {
            store.Dispose();
            store.Dispose();
        });
        Assert.Null(ex);
    }
}