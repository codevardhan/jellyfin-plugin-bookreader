using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace JellyfinBookReader.Services;

/// <summary>
/// Streams individual spine items (chapters / images) from EPUB files.
///
/// EPUBs are ZIPs. The OPF manifest lists all assets; the spine defines reading order.
/// Each "page" served here is one spine item — HTML for text EPUBs, an image for
/// image-based (comic-style) EPUBs. The client is responsible for rendering HTML.
///
/// OPF parsing reuses the same container.xml → OPF → spine walk that CoverService uses.
/// </summary>
public class EpubStreamingService : IBookStreamingService
{
    private readonly ILogger<EpubStreamingService> _logger;

    public EpubStreamingService(ILogger<EpubStreamingService> logger)
    {
        _logger = logger;
    }

    public bool CanStream(string filePath) =>
        Path.GetExtension(filePath).Equals(".epub", StringComparison.OrdinalIgnoreCase);

    public async Task<int> GetPageCountAsync(string filePath, CancellationToken ct = default)
    {
        var spine = await BuildSpineAsync(filePath, ct).ConfigureAwait(false);
        return spine.Count;
    }

    public async Task<(Stream? Stream, string? ContentType)> GetPageAsync(
        string filePath, int pageIndex, CancellationToken ct = default)
    {
        try
        {
            var spine = await BuildSpineAsync(filePath, ct).ConfigureAwait(false);

            if (pageIndex < 0 || pageIndex >= spine.Count)
                return (null, null);

            var (zipPath, contentType) = spine[pageIndex];

            using var zip = ZipFile.OpenRead(filePath);
            var entry = zip.GetEntry(zipPath);
            if (entry == null) return (null, null);

            var ms = new MemoryStream((int)entry.Length);
            using (var stream = entry.Open())
                await stream.CopyToAsync(ms, ct).ConfigureAwait(false);

            ms.Position = 0;
            return (ms, contentType);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EPUB extraction failed: {Path} page {Page}", filePath, pageIndex);
            return (null, null);
        }
    }

    /// <summary>
    /// Parses container.xml → OPF → spine and returns an ordered list of
    /// (zipEntryPath, contentType) pairs representing the reading order.
    /// </summary>
    private async Task<List<(string ZipPath, string ContentType)>> BuildSpineAsync(
        string filePath, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(filePath);

        // Step 1: locate OPF via container.xml
        var containerEntry = zip.GetEntry("META-INF/container.xml");
        if (containerEntry == null) return new();

        string opfPath;
        using (var containerStream = containerEntry.Open())
        {
            var containerDoc = await XDocument
                .LoadAsync(containerStream, LoadOptions.None, ct)
                .ConfigureAwait(false);

            XNamespace ns = "urn:oasis:names:tc:opendocument:xmlns:container";
            opfPath = containerDoc
                .Descendants(ns + "rootfile")
                .FirstOrDefault()
                ?.Attribute("full-path")?.Value ?? string.Empty;
        }

        if (string.IsNullOrEmpty(opfPath)) return new();

        var opfEntry = zip.GetEntry(opfPath);
        if (opfEntry == null) return new();

        var opfDir = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? string.Empty;

        // Step 2: parse OPF — build id→item map, then walk spine itemrefs
        XDocument opfDoc;
        using (var opfStream = opfEntry.Open())
            opfDoc = await XDocument
                .LoadAsync(opfStream, LoadOptions.None, ct)
                .ConfigureAwait(false);

        XNamespace opfNs = "http://www.idpf.org/2007/opf";

        var manifest = opfDoc.Descendants(opfNs + "item")
            .Where(i => i.Attribute("id") != null)
            .ToDictionary(
                i => i.Attribute("id")!.Value,
                i => (
                    Href:      i.Attribute("href")?.Value      ?? string.Empty,
                    MediaType: i.Attribute("media-type")?.Value ?? "application/xhtml+xml"
                )
            );

        return opfDoc.Descendants(opfNs + "itemref")
            .Select(r => r.Attribute("idref")?.Value ?? string.Empty)
            .Where(id => manifest.ContainsKey(id))
            .Select(id =>
            {
                var (href, mediaType) = manifest[id];
                var zipPath = string.IsNullOrEmpty(opfDir)
                    ? href
                    : $"{opfDir}/{href}";
                return (ZipPath: zipPath.Replace('\\', '/'), ContentType: mediaType);
            })
            .ToList();
    }
}
