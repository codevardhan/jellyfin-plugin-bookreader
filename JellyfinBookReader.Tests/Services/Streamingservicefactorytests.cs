using System.Collections.Generic;
using JellyfinBookReader.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyfinBookReader.Tests.Services;

public class StreamingServiceFactoryTests
{
    private static StreamingServiceFactory BuildFactory() => new(new IBookStreamingService[]
    {
        new CbzStreamingService(NullLogger<CbzStreamingService>.Instance),
        new CbrStreamingService(NullLogger<CbrStreamingService>.Instance),
        new EpubStreamingService(NullLogger<EpubStreamingService>.Instance),
    });

    //  GetService 

    [Theory]
    [InlineData("comic.cbz", typeof(CbzStreamingService))]
    [InlineData("comic.CBZ", typeof(CbzStreamingService))]
    [InlineData("comic.cbr", typeof(CbrStreamingService))]
    [InlineData("comic.CBR", typeof(CbrStreamingService))]
    [InlineData("book.epub", typeof(EpubStreamingService))]
    [InlineData("book.EPUB", typeof(EpubStreamingService))]
    public void GetService_ReturnsCorrectImplementation(string path, System.Type expectedType)
    {
        var factory = BuildFactory();
        var service = factory.GetService(path);

        Assert.NotNull(service);
        Assert.IsType(expectedType, service);
    }

    [Theory]
    [InlineData("book.pdf")]
    [InlineData("book.mobi")]
    [InlineData("book.azw3")]
    [InlineData("book.fb2")]
    [InlineData("book.txt")]
    [InlineData("book.zip")]
    [InlineData("book.rar")]
    [InlineData("")]
    [InlineData("nodotextension")]
    public void GetService_ReturnsNullForUnsupportedFormats(string path)
    {
        var factory = BuildFactory();
        Assert.Null(factory.GetService(path));
    }

    //  IsStreamable 

    [Theory]
    [InlineData("comic.cbz", true)]
    [InlineData("comic.cbr", true)]
    [InlineData("book.epub", true)]
    [InlineData("book.pdf", false)]
    [InlineData("book.mobi", false)]
    [InlineData("", false)]
    public void IsStreamable_MatchesGetServiceNotNull(string path, bool expected)
    {
        var factory = BuildFactory();
        Assert.Equal(expected, factory.IsStreamable(path));
    }

    //  Priority (first registered wins) 

    [Fact]
    public void GetService_ReturnsFirstRegisteredForSameExtension()
    {
        // Register two services that both claim .cbz — first one wins.
        var first = new CbzStreamingService(NullLogger<CbzStreamingService>.Instance);
        var second = new CbzStreamingService(NullLogger<CbzStreamingService>.Instance);
        var factory = new StreamingServiceFactory(new IBookStreamingService[] { first, second });

        var result = factory.GetService("comic.cbz");

        Assert.Same(first, result);
    }

    //  Empty factory 

    [Fact]
    public void GetService_ReturnsNullForEmptyFactory()
    {
        var factory = new StreamingServiceFactory(new List<IBookStreamingService>());
        Assert.Null(factory.GetService("comic.cbz"));
    }

    [Fact]
    public void IsStreamable_ReturnsFalseForEmptyFactory()
    {
        var factory = new StreamingServiceFactory(new List<IBookStreamingService>());
        Assert.False(factory.IsStreamable("comic.cbz"));
    }
}