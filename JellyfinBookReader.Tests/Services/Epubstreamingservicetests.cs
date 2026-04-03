using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using JellyfinBookReader.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyfinBookReader.Tests.Services;

/// <summary>
/// Tests for EpubStreamingService.
/// EPUBs are ZIPs with a container.xml → OPF structure.
/// Minimal valid EPUBs are created from scratch for each test scenario.
/// </summary>
public class EpubStreamingServiceTests : IDisposable
{
    private readonly EpubStreamingService _service;
    private readonly string _tempDir;

    public EpubStreamingServiceTests()
    {
        _service = new EpubStreamingService(NullLogger<EpubStreamingService>.Instance);
        _tempDir = Path.Combine(Path.GetTempPath(), $"epub_stream_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    //  CanStream 

    [Theory]
    [InlineData("book.epub", true)]
    [InlineData("book.EPUB", true)]
    [InlineData("book.Epub", true)]
    [InlineData("book.cbz", false)]
    [InlineData("book.cbr", false)]
    [InlineData("book.pdf", false)]
    [InlineData("book.mobi", false)]
    [InlineData("", false)]
    public void CanStream_MatchesOnlyEpubExtension(string path, bool expected)
    {
        Assert.Equal(expected, _service.CanStream(path));
    }

    //  GetPageCountAsync 

    [Fact]
    public async Task GetPageCountAsync_ReturnsTwoForTwoChapterEpub()
    {
        var path = CreateEpub(
            ("ch1.xhtml", "<html><body><p>Chapter 1</p></body></html>"),
            ("ch2.xhtml", "<html><body><p>Chapter 2</p></body></html>"));

        Assert.Equal(2, await _service.GetPageCountAsync(path));
    }

    [Fact]
    public async Task GetPageCountAsync_ReturnsZeroForMissingContainerXml()
    {
        // A ZIP that's not a valid EPUB structure returns 0.
        var path = CreateBareZip(("notanepub.txt", "hello"));
        Assert.Equal(0, await _service.GetPageCountAsync(path));
    }

    [Fact]
    public async Task GetPageCountAsync_ReturnsZeroForEmptySpine()
    {
        var path = CreateEpubWithEmptySpine();
        Assert.Equal(0, await _service.GetPageCountAsync(path));
    }

    [Fact]
    public async Task GetPageCountAsync_ThrowsForNonexistentFile()
    {
        // EpubStreamingService.BuildSpineAsync does not catch FileNotFoundException —
        // unlike CBZ/CBR, it propagates. The manifest controller handles this via
        // ASP.NET middleware. Document the actual behaviour here.
        await Assert.ThrowsAnyAsync<IOException>(
            () => _service.GetPageCountAsync(Path.Combine(_tempDir, "missing.epub")));
    }

    //  GetPageAsync 

    [Fact]
    public async Task GetPageAsync_ReturnsChapterContentForPage0()
    {
        const string ch1Content = "<html><body><p>Chapter 1 text</p></body></html>";
        var path = CreateEpub(
            ("ch1.xhtml", ch1Content),
            ("ch2.xhtml", "<html><body><p>Ch2</p></body></html>"));

        var (stream, contentType) = await _service.GetPageAsync(path, 0);

        Assert.NotNull(stream);
        var text = Encoding.UTF8.GetString(ReadAll(stream!));
        Assert.Contains("Chapter 1 text", text);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsCorrectChapterForPage1()
    {
        const string ch2Content = "<html><body><p>Chapter 2 unique text</p></body></html>";
        var path = CreateEpub(
            ("ch1.xhtml", "<html><body><p>Ch1</p></body></html>"),
            ("ch2.xhtml", ch2Content));

        var (stream, contentType) = await _service.GetPageAsync(path, 1);

        Assert.NotNull(stream);
        var text = Encoding.UTF8.GetString(ReadAll(stream!));
        Assert.Contains("Chapter 2 unique text", text);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsXhtmlContentType()
    {
        var path = CreateEpub(("ch1.xhtml", "<html/>"));
        var (stream, contentType) = await _service.GetPageAsync(path, 0);

        Assert.NotNull(stream);
        Assert.Equal("application/xhtml+xml", contentType);
        stream!.Dispose();
    }

    [Fact]
    public async Task GetPageAsync_ReturnsNullForNegativeIndex()
    {
        var path = CreateEpub(("ch1.xhtml", "<html/>"));
        var (stream, contentType) = await _service.GetPageAsync(path, -1);

        Assert.Null(stream);
        Assert.Null(contentType);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsNullForIndexEqualToCount()
    {
        var path = CreateEpub(("ch1.xhtml", "<html/>"));
        var (stream, contentType) = await _service.GetPageAsync(path, 1);

        Assert.Null(stream);
        Assert.Null(contentType);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsNullForMissingFile()
    {
        var (stream, contentType) = await _service.GetPageAsync(
            Path.Combine(_tempDir, "missing.epub"), 0);

        Assert.Null(stream);
        Assert.Null(contentType);
    }

    [Fact]
    public async Task GetPageAsync_ReturnedStreamIsAtPositionZero()
    {
        var path = CreateEpub(("ch1.xhtml", "<html><body>content</body></html>"));
        var (stream, _) = await _service.GetPageAsync(path, 0);

        Assert.NotNull(stream);
        Assert.Equal(0, stream!.Position);
        stream.Dispose();
    }

    //  Helpers 

    /// Creates a minimal valid EPUB ZIP with the given chapters in spine order.
    private string CreateEpub(params (string Name, string Content)[] chapters)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.epub");

        // Build OPF manifest items and spine itemrefs
        var manifestItems = new StringBuilder();
        var spineItems = new StringBuilder();
        for (int i = 0; i < chapters.Length; i++)
        {
            manifestItems.AppendLine(
                $"<item id=\"ch{i}\" href=\"{chapters[i].Name}\" media-type=\"application/xhtml+xml\"/>");
            spineItems.AppendLine($"<itemref idref=\"ch{i}\"/>");
        }

        using var fs = File.OpenWrite(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        WriteEntry(zip, "META-INF/container.xml",
            """<?xml version="1.0"?><container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="content.opf" media-type="application/oebps-package+xml"/></rootfiles></container>""");

        WriteEntry(zip, "content.opf",
            $"""<?xml version="1.0" encoding="UTF-8"?><package xmlns="http://www.idpf.org/2007/opf" version="3.0"><metadata/><manifest>{manifestItems}</manifest><spine>{spineItems}</spine></package>""");

        foreach (var (name, content) in chapters)
            WriteEntry(zip, name, content);

        return path;
    }

    private string CreateEpubWithEmptySpine()
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.epub");
        using var fs = File.OpenWrite(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        WriteEntry(zip, "META-INF/container.xml",
            """<?xml version="1.0"?><container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="content.opf" media-type="application/oebps-package+xml"/></rootfiles></container>""");

        WriteEntry(zip, "content.opf",
            """<?xml version="1.0" encoding="UTF-8"?><package xmlns="http://www.idpf.org/2007/opf" version="3.0"><metadata/><manifest/><spine/></package>""");

        return path;
    }

    private string CreateBareZip(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.epub");
        using var fs = File.OpenWrite(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
            WriteEntry(zip, name, content);
        return path;
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var s = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}