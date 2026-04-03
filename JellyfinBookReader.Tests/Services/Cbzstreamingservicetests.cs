using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JellyfinBookReader.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyfinBookReader.Tests.Services;

/// <summary>
/// Tests for CbzStreamingService.
/// Real ZIP archives are created in-memory and written to a temp file so the
/// service can open them with ZipFile.OpenRead (which requires a seekable stream).
/// </summary>
public class CbzStreamingServiceTests : IDisposable
{
    private readonly CbzStreamingService _service;
    private readonly string _tempDir;

    public CbzStreamingServiceTests()
    {
        _service = new CbzStreamingService(NullLogger<CbzStreamingService>.Instance);
        _tempDir = Path.Combine(Path.GetTempPath(), $"cbz_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    //  CanStream 

    [Theory]
    [InlineData("comic.cbz", true)]
    [InlineData("comic.CBZ", true)]
    [InlineData("comic.Cbz", true)]
    [InlineData("comic.cbr", false)]
    [InlineData("comic.epub", false)]
    [InlineData("comic.pdf", false)]
    [InlineData("comic.zip", false)]
    [InlineData("", false)]
    public void CanStream_MatchesOnlyCbzExtension(string path, bool expected)
    {
        Assert.Equal(expected, _service.CanStream(path));
    }

    //  GetPageCountAsync 

    [Fact]
    public async Task GetPageCountAsync_ReturnsNumberOfImageFiles()
    {
        var path = CreateCbz(("001.jpg", new byte[] { 0xFF, 0xD8, 0xFF }), // JPEG magic
                             ("002.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
                             ("003.webp", new byte[] { 0x52, 0x49, 0x46, 0x46 }));

        Assert.Equal(3, await _service.GetPageCountAsync(path));
    }

    [Fact]
    public async Task GetPageCountAsync_ExcludesNonImageFiles()
    {
        var path = CreateCbz(("page.jpg", new byte[] { 0xFF, 0xD8 }),
                             ("notes.txt", Encoding.UTF8.GetBytes("text")),
                             ("meta.xml", Encoding.UTF8.GetBytes("<xml/>")));

        Assert.Equal(1, await _service.GetPageCountAsync(path));
    }

    [Fact]
    public async Task GetPageCountAsync_ReturnsZeroForEmptyArchive()
    {
        var path = CreateCbz();
        Assert.Equal(0, await _service.GetPageCountAsync(path));
    }

    [Fact]
    public async Task GetPageCountAsync_ExcludesDirectoryEntries()
    {
        var path = WriteCbzWithDirectory();
        Assert.Equal(1, await _service.GetPageCountAsync(path));
    }

    //  GetPageAsync 

    [Fact]
    public async Task GetPageAsync_ReturnsCorrectBytesForPage()
    {
        var page0Bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xAA };
        var page1Bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var path = CreateCbz(("001.jpg", page0Bytes), ("002.png", page1Bytes));

        var (stream, _) = await _service.GetPageAsync(path, 0);

        Assert.NotNull(stream);
        var data = ReadAll(stream!);
        Assert.Equal(page0Bytes, data);
    }

    [Fact]
    public async Task GetPageAsync_PagesServedInAlphabeticalOrder()
    {
        // Entries added out of order — should be sorted by full path.
        var aBytes = new byte[] { 0x01 };
        var bBytes = new byte[] { 0x02 };
        var path = CreateCbz(("b_page.jpg", bBytes), ("a_page.jpg", aBytes));

        var (streamA, _) = await _service.GetPageAsync(path, 0);
        var (streamB, _) = await _service.GetPageAsync(path, 1);

        Assert.Equal(aBytes, ReadAll(streamA!)); // a_page.jpg is page 0
        Assert.Equal(bBytes, ReadAll(streamB!)); // b_page.jpg is page 1
    }

    [Theory]
    [InlineData("page.jpg",  "image/jpeg")]
    [InlineData("page.jpeg", "image/jpeg")]
    [InlineData("page.png",  "image/png")]
    [InlineData("page.webp", "image/webp")]
    [InlineData("page.gif",  "image/gif")]
    [InlineData("page.JPG",  "image/jpeg")]
    public async Task GetPageAsync_ReturnsCorrectContentType(string fileName, string expectedMime)
    {
        var path = CreateCbz((fileName, new byte[] { 0x01, 0x02 }));
        var (stream, contentType) = await _service.GetPageAsync(path, 0);

        Assert.NotNull(stream);
        Assert.Equal(expectedMime, contentType);
        stream!.Dispose();
    }

    [Fact]
    public async Task GetPageAsync_ReturnsNullForNegativeIndex()
    {
        var path = CreateCbz(("001.jpg", new byte[] { 0x01 }));
        var (stream, contentType) = await _service.GetPageAsync(path, -1);

        Assert.Null(stream);
        Assert.Null(contentType);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsNullForIndexEqualToCount()
    {
        var path = CreateCbz(("001.jpg", new byte[] { 0x01 }));
        var (stream, contentType) = await _service.GetPageAsync(path, 1);

        Assert.Null(stream);
        Assert.Null(contentType);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsNullForIndexBeyondCount()
    {
        var path = CreateCbz(("001.jpg", new byte[] { 0x01 }));
        var (stream, contentType) = await _service.GetPageAsync(path, 99);

        Assert.Null(stream);
        Assert.Null(contentType);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsNullForNonexistentFile()
    {
        var (stream, contentType) = await _service.GetPageAsync(
            Path.Combine(_tempDir, "does_not_exist.cbz"), 0);

        Assert.Null(stream);
        Assert.Null(contentType);
    }

    [Fact]
    public async Task GetPageAsync_RespectsCancellation()
    {
        var path = CreateCbz(("001.jpg", new byte[] { 0x01 }));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.GetPageAsync(path, 0, cts.Token));
    }

    [Fact]
    public async Task GetPageAsync_ReturnedStreamIsReadable()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0x01, 0x02 };
        var path = CreateCbz(("001.jpg", bytes));

        var (stream, _) = await _service.GetPageAsync(path, 0);
        Assert.NotNull(stream);
        Assert.True(stream!.CanRead);
        Assert.Equal(0, stream.Position);
        stream.Dispose();
    }

    //  Helpers 

    private string CreateCbz(params (string Name, byte[] Data)[] entries)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.cbz");
        using var fs = File.OpenWrite(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, data) in entries)
        {
            var entry = zip.CreateEntry(name);
            using var es = entry.Open();
            es.Write(data, 0, data.Length);
        }
        return path;
    }

    /// Creates a CBZ with a directory entry and one image inside it.
    private string WriteCbzWithDirectory()
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.cbz");
        using var fs = File.OpenWrite(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        zip.CreateEntry("pages/");          // directory entry
        var e = zip.CreateEntry("pages/001.jpg");
        using var es = e.Open();
        es.Write(new byte[] { 0xFF, 0xD8 }, 0, 2);
        return path;
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}