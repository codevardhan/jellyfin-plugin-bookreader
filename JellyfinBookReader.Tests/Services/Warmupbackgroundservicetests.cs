using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using JellyfinBookReader.Dto;
using JellyfinBookReader.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyfinBookReader.Tests.Services;

/// <summary>
/// Tests for WarmUpBackgroundService.
///
/// The service reads WarmUpRequests from a bounded channel and pre-populates
/// BookPageCache. Tests use a real CBZ archive (easy to create) as the book file,
/// a real StreamingServiceFactory, and a real BookPageCache with InMemoryStores.
/// </summary>
public class WarmUpBackgroundServiceTests : IDisposable
{
    private readonly string _tempDir;

    public WarmUpBackgroundServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"warmup_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    //  Helpers 

    private (WarmUpBackgroundService Service, Channel<WarmUpRequest> Channel, BookPageCache Cache)
        BuildService()
    {
        var channel = Channel.CreateBounded<WarmUpRequest>(
            new BoundedChannelOptions(50) { FullMode = BoundedChannelFullMode.DropOldest });

        var factory = new StreamingServiceFactory(new IBookStreamingService[]
        {
            new CbzStreamingService(NullLogger<CbzStreamingService>.Instance),
        });

        var cache = new BookPageCache(NullLogger<BookPageCache>.Instance);
        var service = new WarmUpBackgroundService(
            channel, factory, cache, NullLogger<WarmUpBackgroundService>.Instance);

        return (service, channel, cache);
    }

    private string CreateCbz(int pageCount)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.cbz");
        using var fs = File.OpenWrite(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        for (var i = 0; i < pageCount; i++)
        {
            var entry = zip.CreateEntry($"{i:D4}.jpg");
            using var s = entry.Open();
            s.Write(new byte[] { 0xFF, 0xD8, (byte)i }, 0, 3);
        }
        return path;
    }

    private static async Task DrainAsync(
        WarmUpBackgroundService service,
        Channel<WarmUpRequest> channel,
        TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        using var cts = new CancellationTokenSource(timeout.Value);
        var startTask = service.StartAsync(cts.Token);
        channel.Writer.TryComplete(); // signal no more items
        await startTask;
        try { await service.StopAsync(CancellationToken.None); } catch { }
    }

    //  Core warm-up 

    [Fact]
    public async Task WarmUp_PopulatesCacheForRequestedPages()
    {
        var (svc, ch, cache) = BuildService();
        var bookId = Guid.NewGuid();
        var path = CreateCbz(pageCount: 5);

        ch.Writer.TryWrite(new WarmUpRequest(bookId, path, StartPage: 0, PageCount: 3));

        await DrainAsync(svc, ch);

        Assert.True(cache.HasPage(bookId, 0));
        Assert.True(cache.HasPage(bookId, 1));
        Assert.True(cache.HasPage(bookId, 2));
        Assert.False(cache.HasPage(bookId, 3)); // not requested
        Assert.False(cache.HasPage(bookId, 4)); // not requested
    }

    [Fact]
    public async Task WarmUp_CachedPagesContainCorrectData()
    {
        var (svc, ch, cache) = BuildService();
        var bookId = Guid.NewGuid();
        var path = CreateCbz(pageCount: 2);

        ch.Writer.TryWrite(new WarmUpRequest(bookId, path, StartPage: 0, PageCount: 2));
        await DrainAsync(svc, ch);

        var hit = cache.TryGet(bookId, 0, out var data, out var ct);
        Assert.True(hit);
        Assert.NotNull(data);
        Assert.True(data!.Length > 0);
        Assert.Equal("image/jpeg", ct);
    }

    [Fact]
    public async Task WarmUp_ProcessesMultipleRequestsInOrder()
    {
        var (svc, ch, cache) = BuildService();
        var book1 = Guid.NewGuid();
        var book2 = Guid.NewGuid();
        var path1 = CreateCbz(pageCount: 2);
        var path2 = CreateCbz(pageCount: 3);

        ch.Writer.TryWrite(new WarmUpRequest(book1, path1, 0, 2));
        ch.Writer.TryWrite(new WarmUpRequest(book2, path2, 0, 3));

        await DrainAsync(svc, ch);

        Assert.True(cache.HasPage(book1, 0));
        Assert.True(cache.HasPage(book1, 1));
        Assert.True(cache.HasPage(book2, 0));
        Assert.True(cache.HasPage(book2, 1));
        Assert.True(cache.HasPage(book2, 2));
    }

    //  Skips already-cached pages 

    [Fact]
    public async Task WarmUp_SkipsPagesThatAreAlreadyCached()
    {
        var (svc, ch, cache) = BuildService();
        var bookId = Guid.NewGuid();
        var path = CreateCbz(pageCount: 3);
        var prePopulated = new byte[] { 0xDE, 0xAD };

        // Pre-populate page 1 with sentinel data.
        cache.Set(bookId, path, 1, prePopulated, "image/jpeg");

        ch.Writer.TryWrite(new WarmUpRequest(bookId, path, StartPage: 0, PageCount: 3));
        await DrainAsync(svc, ch);

        // Page 1 should still have sentinel data (not overwritten).
        cache.TryGet(bookId, 1, out var data, out _);
        Assert.Equal(prePopulated, data);
    }

    //  Handles out-of-range pages 

    [Fact]
    public async Task WarmUp_StopsWhenPageIndexExceedsBookLength()
    {
        var (svc, ch, cache) = BuildService();
        var bookId = Guid.NewGuid();
        var path = CreateCbz(pageCount: 2); // only 2 pages

        // Request 10 pages — service should stop when stream returns null.
        ch.Writer.TryWrite(new WarmUpRequest(bookId, path, StartPage: 0, PageCount: 10));
        await DrainAsync(svc, ch);

        // Pages 0 and 1 were cached; pages 2..9 don't exist.
        Assert.True(cache.HasPage(bookId, 0));
        Assert.True(cache.HasPage(bookId, 1));
        Assert.False(cache.HasPage(bookId, 2));
    }

    [Fact]
    public async Task WarmUp_StartingBeyondEndOfBook_CachesNothing()
    {
        var (svc, ch, cache) = BuildService();
        var bookId = Guid.NewGuid();
        var path = CreateCbz(pageCount: 3);

        ch.Writer.TryWrite(new WarmUpRequest(bookId, path, StartPage: 5, PageCount: 3));
        await DrainAsync(svc, ch);

        Assert.False(cache.HasPage(bookId, 5));
        Assert.False(cache.HasPage(bookId, 6));
    }

    //  Unsupported format 

    [Fact]
    public async Task WarmUp_IgnoresRequestForUnsupportedFormat()
    {
        var (svc, ch, cache) = BuildService();
        var bookId = Guid.NewGuid();
        var path = Path.Combine(_tempDir, "book.pdf"); // factory has no PDF service
        File.WriteAllBytes(path, new byte[] { 0x25, 0x50, 0x44, 0x46 }); // %PDF

        ch.Writer.TryWrite(new WarmUpRequest(bookId, path, 0, 3));
        await DrainAsync(svc, ch);

        Assert.False(cache.HasPage(bookId, 0));
    }

    //  Resilience 

    [Fact]
    public async Task WarmUp_MissingFile_AbortsGracefully()
    {
        var (svc, ch, cache) = BuildService();
        var bookId = Guid.NewGuid();
        var missing = Path.Combine(_tempDir, "gone.cbz");

        // Should not throw — missing file is an exception caught inside the service.
        var ex = await Record.ExceptionAsync(async () =>
        {
            ch.Writer.TryWrite(new WarmUpRequest(bookId, missing, 0, 5));
            await DrainAsync(svc, ch);
        });

        Assert.Null(ex);
        Assert.False(cache.HasPage(bookId, 0));
    }

    [Fact]
    public async Task WarmUp_ExceptionInOnePage_DoesNotPreventOtherRequests()
    {
        var (svc, ch, cache) = BuildService();
        var goodBook = Guid.NewGuid();
        var badBook = Guid.NewGuid();
        var goodPath = CreateCbz(pageCount: 2);
        var badPath = Path.Combine(_tempDir, "corrupt.cbz");
        File.WriteAllBytes(badPath, new byte[] { 0x00 }); // invalid ZIP

        ch.Writer.TryWrite(new WarmUpRequest(badBook, badPath, 0, 2));
        ch.Writer.TryWrite(new WarmUpRequest(goodBook, goodPath, 0, 2));
        await DrainAsync(svc, ch);

        // The good book should still be fully warmed up.
        Assert.True(cache.HasPage(goodBook, 0));
        Assert.True(cache.HasPage(goodBook, 1));
    }

    //  Zero-page request 

    [Fact]
    public async Task WarmUp_ZeroPageCount_CachesNothing()
    {
        var (svc, ch, cache) = BuildService();
        var bookId = Guid.NewGuid();
        var path = CreateCbz(pageCount: 5);

        ch.Writer.TryWrite(new WarmUpRequest(bookId, path, StartPage: 0, PageCount: 0));
        await DrainAsync(svc, ch);

        Assert.False(cache.HasPage(bookId, 0));
    }
}