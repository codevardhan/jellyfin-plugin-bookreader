using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using JellyfinBookReader.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyfinBookReader.Tests.Services;

/// <summary>
/// Tests for CoverService EPUB cover extraction.
/// Uses reflection to call the private ExtractEpubCoverAsync method directly,
/// since BaseItem cannot be easily mocked in unit tests.
/// </summary>
public class CoverServiceTests : IDisposable
{
    private readonly CoverService _service;
    private readonly MethodInfo _extractMethod;
    private readonly string _tempDir;

    public CoverServiceTests()
    {
        _service = new CoverService(NullLogger<CoverService>.Instance);
        _tempDir = Path.Combine(Path.GetTempPath(), $"jf_cover_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _extractMethod = typeof(CoverService).GetMethod(
            "ExtractEpubCoverAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.NotNull(_extractMethod);
    }

    private async Task<(Stream? Stream, string? ContentType)> ExtractEpubCover(string epubPath)
    {
        var task = (Task<(Stream? Stream, string? ContentType)>)_extractMethod.Invoke(
            _service, new object[] { epubPath })!;
        return await task;
    }

    //  Strategy 1: <meta name="cover" content="id"/> 

    [Fact]
    public async Task ExtractsFromEpub_MetaNameCover()
    {
        var epubPath = CreateMinimalEpub(coverStrategy: "meta-name", imageExt: "jpg");
        var (stream, contentType) = await ExtractEpubCover(epubPath);

        Assert.NotNull(stream);
        Assert.NotNull(contentType);
        Assert.Equal("image/jpeg", contentType);
        Assert.True(stream!.Length > 0);
        stream.Dispose();
    }

    //  Strategy 2: item properties="cover-image" 

    [Fact]
    public async Task ExtractsFromEpub_PropertiesCoverImage()
    {
        var epubPath = CreateMinimalEpub(coverStrategy: "properties", imageExt: "png");
        var (stream, contentType) = await ExtractEpubCover(epubPath);

        Assert.NotNull(stream);
        Assert.Equal("image/png", contentType);
        stream!.Dispose();
    }

    //  Strategy 3: id containing "cover" + image media type 

    [Fact]
    public async Task ExtractsFromEpub_IdContainsCover()
    {
        var epubPath = CreateMinimalEpub(coverStrategy: "id-cover", imageExt: "jpg");
        var (stream, contentType) = await ExtractEpubCover(epubPath);

        Assert.NotNull(stream);
        Assert.Equal("image/jpeg", contentType);
        stream!.Dispose();
    }

    //  No cover 

    [Fact]
    public async Task ReturnsNull_WhenNoCoverInEpub()
    {
        var epubPath = CreateMinimalEpub(coverStrategy: "none");
        var (stream, contentType) = await ExtractEpubCover(epubPath);

        Assert.Null(stream);
        Assert.Null(contentType);
    }

    //  Corrupt/invalid EPUBs 

    [Fact]
    public async Task ReturnsNull_WhenEpubIsCorruptBytes()
    {
        var corruptPath = Path.Combine(_tempDir, "corrupt.epub");
        File.WriteAllBytes(corruptPath, new byte[] { 0x00, 0x01, 0x02, 0x03 });

        var (stream, contentType) = await ExtractEpubCover(corruptPath);
        Assert.Null(stream);
        Assert.Null(contentType);
    }

    [Fact]
    public async Task ReturnsNull_WhenEpubMissingContainerXml()
    {
        var epubPath = Path.Combine(_tempDir, "no_container.epub");
        using (var zip = ZipFile.Open(epubPath, ZipArchiveMode.Create))
        {
            AddEntry(zip, "mimetype", "application/epub+zip");
        }

        var (stream, contentType) = await ExtractEpubCover(epubPath);
        Assert.Null(stream);
        Assert.Null(contentType);
    }

    [Fact]
    public async Task ReturnsNull_WhenContainerXmlPointsToMissingOpf()
    {
        var epubPath = Path.Combine(_tempDir, "missing_opf.epub");
        using (var zip = ZipFile.Open(epubPath, ZipArchiveMode.Create))
        {
            AddEntry(zip, "mimetype", "application/epub+zip");
            AddEntry(zip, "META-INF/container.xml", @"<?xml version=""1.0""?>
<container version=""1.0"" xmlns=""urn:oasis:names:tc:opendocument:xmlns:container"">
  <rootfiles>
    <rootfile full-path=""OEBPS/nonexistent.opf"" media-type=""application/oebps-package+xml""/>
  </rootfiles>
</container>");
        }

        var (stream, contentType) = await ExtractEpubCover(epubPath);
        Assert.Null(stream);
        Assert.Null(contentType);
    }

    [Fact]
    public async Task ReturnsNull_WhenOpfReferencesNonexistentImage()
    {
        var epubPath = Path.Combine(_tempDir, "missing_image.epub");
        using (var zip = ZipFile.Open(epubPath, ZipArchiveMode.Create))
        {
            AddEntry(zip, "mimetype", "application/epub+zip");
            AddEntry(zip, "META-INF/container.xml", @"<?xml version=""1.0""?>
<container version=""1.0"" xmlns=""urn:oasis:names:tc:opendocument:xmlns:container"">
  <rootfiles>
    <rootfile full-path=""OEBPS/content.opf"" media-type=""application/oebps-package+xml""/>
  </rootfiles>
</container>");
            AddEntry(zip, "OEBPS/content.opf", @"<?xml version=""1.0""?>
<package xmlns=""http://www.idpf.org/2007/opf"" version=""3.0"">
  <metadata>
    <meta name=""cover"" content=""cover-img""/>
  </metadata>
  <manifest>
    <item id=""cover-img"" href=""images/nonexistent.jpg"" media-type=""image/jpeg""/>
  </manifest>
</package>");
        }

        var (stream, contentType) = await ExtractEpubCover(epubPath);
        Assert.Null(stream);
        Assert.Null(contentType);
    }

    [Fact]
    public async Task ReturnsNull_WhenContainerXmlIsEmpty()
    {
        var epubPath = Path.Combine(_tempDir, "empty_container.epub");
        using (var zip = ZipFile.Open(epubPath, ZipArchiveMode.Create))
        {
            AddEntry(zip, "mimetype", "application/epub+zip");
            AddEntry(zip, "META-INF/container.xml", @"<?xml version=""1.0""?>
<container version=""1.0"" xmlns=""urn:oasis:names:tc:opendocument:xmlns:container"">
  <rootfiles/>
</container>");
        }

        var (stream, contentType) = await ExtractEpubCover(epubPath);
        Assert.Null(stream);
        Assert.Null(contentType);
    }

    [Fact]
    public async Task ReturnsNull_WhenFileDoesNotExist()
    {
        var nonexistentPath = Path.Combine(_tempDir, "does_not_exist.epub");

        var (stream, contentType) = await ExtractEpubCover(nonexistentPath);
        Assert.Null(stream);
        Assert.Null(contentType);
    }

    //  Content type detection 

    [Theory]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("gif", "image/gif")]
    [InlineData("webp", "image/webp")]
    public async Task DetectsCorrectContentType(string imageExt, string expectedContentType)
    {
        var epubPath = CreateMinimalEpub(coverStrategy: "meta-name", imageExt: imageExt);
        var (stream, contentType) = await ExtractEpubCover(epubPath);

        Assert.NotNull(stream);
        Assert.Equal(expectedContentType, contentType);
        stream!.Dispose();
    }

    //  OPF in root directory 

    [Fact]
    public async Task ExtractsFromEpub_WhenOpfIsInRootDir()
    {
        var epubPath = Path.Combine(_tempDir, "root_opf.epub");
        using (var zip = ZipFile.Open(epubPath, ZipArchiveMode.Create))
        {
            AddEntry(zip, "mimetype", "application/epub+zip");
            AddEntry(zip, "META-INF/container.xml", @"<?xml version=""1.0""?>
<container version=""1.0"" xmlns=""urn:oasis:names:tc:opendocument:xmlns:container"">
  <rootfiles>
    <rootfile full-path=""content.opf"" media-type=""application/oebps-package+xml""/>
  </rootfiles>
</container>");
            AddEntry(zip, "content.opf", @"<?xml version=""1.0""?>
<package xmlns=""http://www.idpf.org/2007/opf"" version=""3.0"">
  <metadata>
    <meta name=""cover"" content=""cover-img""/>
  </metadata>
  <manifest>
    <item id=""cover-img"" href=""cover.jpg"" media-type=""image/jpeg""/>
  </manifest>
</package>");
            AddBinaryEntry(zip, "cover.jpg", new byte[50]);
        }

        var (stream, contentType) = await ExtractEpubCover(epubPath);
        Assert.NotNull(stream);
        Assert.Equal("image/jpeg", contentType);
        stream!.Dispose();
    }

    //  Cover image content verification 

    [Fact]
    public async Task ExtractedCover_HasCorrectContent()
    {
        var knownContent = new byte[256];
        for (int i = 0; i < 256; i++) knownContent[i] = (byte)i;

        var epubPath = Path.Combine(_tempDir, "known_content.epub");
        using (var zip = ZipFile.Open(epubPath, ZipArchiveMode.Create))
        {
            AddEntry(zip, "mimetype", "application/epub+zip");
            AddEntry(zip, "META-INF/container.xml", @"<?xml version=""1.0""?>
<container version=""1.0"" xmlns=""urn:oasis:names:tc:opendocument:xmlns:container"">
  <rootfiles>
    <rootfile full-path=""OEBPS/content.opf"" media-type=""application/oebps-package+xml""/>
  </rootfiles>
</container>");
            AddEntry(zip, "OEBPS/content.opf", @"<?xml version=""1.0""?>
<package xmlns=""http://www.idpf.org/2007/opf"" version=""3.0"">
  <metadata>
    <meta name=""cover"" content=""cover-img""/>
  </metadata>
  <manifest>
    <item id=""cover-img"" href=""images/cover.jpg"" media-type=""image/jpeg""/>
  </manifest>
</package>");
            AddBinaryEntry(zip, "OEBPS/images/cover.jpg", knownContent);
        }

        var (stream, _) = await ExtractEpubCover(epubPath);
        Assert.NotNull(stream);

        var ms = new MemoryStream();
        await stream!.CopyToAsync(ms);
        var extracted = ms.ToArray();

        Assert.Equal(knownContent.Length, extracted.Length);
        Assert.Equal(knownContent, extracted);
        stream.Dispose();
    }

    //  Helpers 

    private string CreateMinimalEpub(string coverStrategy, string imageExt = "jpg")
    {
        var epubPath = Path.Combine(_tempDir, $"test_{coverStrategy}_{imageExt}.epub");
        using var zip = ZipFile.Open(epubPath, ZipArchiveMode.Create);

        AddEntry(zip, "mimetype", "application/epub+zip");
        AddEntry(zip, "META-INF/container.xml", @"<?xml version=""1.0""?>
<container version=""1.0"" xmlns=""urn:oasis:names:tc:opendocument:xmlns:container"">
  <rootfiles>
    <rootfile full-path=""OEBPS/content.opf"" media-type=""application/oebps-package+xml""/>
  </rootfiles>
</container>");

        var imageMediaType = imageExt switch
        {
            "png" => "image/png",
            "gif" => "image/gif",
            "webp" => "image/webp",
            _ => "image/jpeg",
        };

        string opfContent = coverStrategy switch
        {
            "meta-name" => $@"<?xml version=""1.0""?>
<package xmlns=""http://www.idpf.org/2007/opf"" version=""3.0"">
  <metadata>
    <meta name=""cover"" content=""cover-img""/>
  </metadata>
  <manifest>
    <item id=""cover-img"" href=""images/cover.{imageExt}"" media-type=""{imageMediaType}""/>
  </manifest>
</package>",
            "properties" => $@"<?xml version=""1.0""?>
<package xmlns=""http://www.idpf.org/2007/opf"" version=""3.0"">
  <metadata/>
  <manifest>
    <item id=""cover"" href=""images/cover.{imageExt}"" media-type=""{imageMediaType}"" properties=""cover-image""/>
  </manifest>
</package>",
            "id-cover" => $@"<?xml version=""1.0""?>
<package xmlns=""http://www.idpf.org/2007/opf"" version=""3.0"">
  <metadata/>
  <manifest>
    <item id=""cover-image"" href=""images/cover.{imageExt}"" media-type=""{imageMediaType}""/>
  </manifest>
</package>",
            _ => @"<?xml version=""1.0""?>
<package xmlns=""http://www.idpf.org/2007/opf"" version=""3.0"">
  <metadata/>
  <manifest>
    <item id=""chapter1"" href=""chapter1.xhtml"" media-type=""application/xhtml+xml""/>
  </manifest>
</package>",
        };

        AddEntry(zip, "OEBPS/content.opf", opfContent);

        if (coverStrategy != "none")
        {
            var fakeImage = new byte[100];
            new Random(42).NextBytes(fakeImage);
            AddBinaryEntry(zip, $"OEBPS/images/cover.{imageExt}", fakeImage);
        }

        return epubPath;
    }

    private static void AddEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void AddBinaryEntry(ZipArchive zip, string path, byte[] content)
    {
        var entry = zip.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(content, 0, content.Length);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
        GC.SuppressFinalize(this);
    }
}