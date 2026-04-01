using JellyfinBookReader.Utils;
using Xunit;

namespace JellyfinBookReader.Tests.Utils;

public class MimeTypeHelperTests
{
    //  GetMimeType 

    [Theory]
    [InlineData("/books/test.epub", "application/epub+zip")]
    [InlineData("/books/test.pdf", "application/pdf")]
    [InlineData("/books/test.mobi", "application/x-mobipocket-ebook")]
    [InlineData("/books/test.azw3", "application/x-mobi8-ebook")]
    [InlineData("/books/test.azw", "application/x-mobipocket-ebook")]
    [InlineData("/books/test.cbz", "application/x-cbz")]
    [InlineData("/books/test.cbr", "application/x-cbr")]
    [InlineData("/books/test.fb2", "application/x-fictionbook+xml")]
    [InlineData("/books/test.txt", "text/plain")]
    [InlineData("/books/test.djvu", "image/vnd.djvu")]
    public void GetMimeType_ReturnsCorrectType(string path, string expected)
    {
        Assert.Equal(expected, MimeTypeHelper.GetMimeType(path));
    }

    [Theory]
    [InlineData("/books/test.EPUB", "application/epub+zip")]
    [InlineData("/books/test.PDF", "application/pdf")]
    [InlineData("/books/test.Mobi", "application/x-mobipocket-ebook")]
    public void GetMimeType_IsCaseInsensitive(string path, string expected)
    {
        Assert.Equal(expected, MimeTypeHelper.GetMimeType(path));
    }

    [Theory]
    [InlineData("/books/test.docx")]
    [InlineData("/books/test.mp3")]
    [InlineData("/books/test.unknown")]
    [InlineData("/books/noextension")]
    public void GetMimeType_ReturnsFallback_ForUnknownFormats(string path)
    {
        Assert.Equal("application/octet-stream", MimeTypeHelper.GetMimeType(path));
    }

    //  IsSupportedBookFormat 

    [Theory]
    [InlineData("/books/test.epub", true)]
    [InlineData("/books/test.pdf", true)]
    [InlineData("/books/test.mobi", true)]
    [InlineData("/books/test.azw3", true)]
    [InlineData("/books/test.azw", true)]
    [InlineData("/books/test.cbz", true)]
    [InlineData("/books/test.cbr", true)]
    [InlineData("/books/test.fb2", true)]
    [InlineData("/books/test.txt", true)]
    [InlineData("/books/test.djvu", true)]
    [InlineData("/books/test.docx", false)]
    [InlineData("/books/test.mp3", false)]
    [InlineData("/books/test", false)]
    public void IsSupportedBookFormat_ReturnsExpected(string path, bool expected)
    {
        Assert.Equal(expected, MimeTypeHelper.IsSupportedBookFormat(path));
    }

    [Fact]
    public void IsSupportedBookFormat_IsCaseInsensitive()
    {
        Assert.True(MimeTypeHelper.IsSupportedBookFormat("/books/test.EPUB"));
        Assert.True(MimeTypeHelper.IsSupportedBookFormat("/books/test.Pdf"));
    }

    //  Edge Cases 

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetMimeType_HandlesEmptyOrWhitespace(string path)
    {
        // Path.GetExtension returns "" for empty/whitespace, so fallback expected
        Assert.Equal("application/octet-stream", MimeTypeHelper.GetMimeType(path));
    }

    [Fact]
    public void IsSupportedBookFormat_EmptyString_ReturnsFalse()
    {
        Assert.False(MimeTypeHelper.IsSupportedBookFormat(""));
    }

    [Theory]
    [InlineData("/books/.epub")]
    [InlineData("/.pdf")]
    public void GetMimeType_HandlesPathsWithNoBaseName(string path)
    {
        // Files named just ".epub" are technically valid
        Assert.NotEqual("application/octet-stream", MimeTypeHelper.GetMimeType(path));
    }

    [Theory]
    [InlineData("/path/with spaces/my book.epub", "application/epub+zip")]
    [InlineData("/path/special (chars)/book [2024].pdf", "application/pdf")]
    public void GetMimeType_HandlesSpecialCharactersInPath(string path, string expected)
    {
        Assert.Equal(expected, MimeTypeHelper.GetMimeType(path));
    }

    [Theory]
    [InlineData("/books/archive.epub.bak")]
    [InlineData("/books/test.pdf.tmp")]
    public void IsSupportedBookFormat_RejectsNonBookExtensions(string path)
    {
        Assert.False(MimeTypeHelper.IsSupportedBookFormat(path));
    }
}