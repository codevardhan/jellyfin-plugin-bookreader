using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace JellyfinBookReader.Services;

public class CoverService
{
    private readonly ILogger<CoverService> _logger;

    public CoverService(ILogger<CoverService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Try to get a cover image. Returns (stream, contentType) or (null, null).
    /// Fallback chain: Jellyfin image cache → epub extraction.
    /// </summary>
    public async Task<(Stream? Stream, string? ContentType)> GetCoverAsync(BaseItem item)
    {
        // 1. Try Jellyfin's own image cache
        var imageInfo = item.GetImageInfo(ImageType.Primary, 0);
        if (imageInfo != null && File.Exists(imageInfo.Path))
        {
            var ext = Path.GetExtension(imageInfo.Path).ToLowerInvariant();
            var ct = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };

            var stream = File.OpenRead(imageInfo.Path);
            return (stream, ct);
        }

        // 2. Try extracting from epub
        if (item.Path != null && item.Path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
        {
            return await ExtractEpubCoverAsync(item.Path).ConfigureAwait(false);
        }

        return (null, null);
    }

    private async Task<(Stream? Stream, string? ContentType)> ExtractEpubCoverAsync(string epubPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(epubPath);

            // Find the OPF file via container.xml
            var containerEntry = zip.GetEntry("META-INF/container.xml");
            if (containerEntry == null) return (null, null);

            string opfPath;
            using (var containerStream = containerEntry.Open())
            {
                var containerDoc = await XDocument.LoadAsync(containerStream, LoadOptions.None, default).ConfigureAwait(false);
                XNamespace ns = "urn:oasis:names:tc:opendocument:xmlns:container";
                opfPath = containerDoc.Descendants(ns + "rootfile")
                    .FirstOrDefault()?.Attribute("full-path")?.Value ?? string.Empty;
            }

            if (string.IsNullOrEmpty(opfPath)) return (null, null);

            // Parse the OPF to find the cover image
            var opfEntry = zip.GetEntry(opfPath);
            if (opfEntry == null) return (null, null);

            string? coverHref = null;
            var opfDir = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? string.Empty;

            using (var opfStream = opfEntry.Open())
            {
                var opfDoc = await XDocument.LoadAsync(opfStream, LoadOptions.None, default).ConfigureAwait(false);
                XNamespace opfNs = "http://www.idpf.org/2007/opf";

                // Strategy 1: <meta name="cover" content="cover-image-id"/>
                var coverMeta = opfDoc.Descendants(opfNs + "meta")
                    .FirstOrDefault(m => m.Attribute("name")?.Value == "cover");
                if (coverMeta != null)
                {
                    var coverId = coverMeta.Attribute("content")?.Value;
                    coverHref = opfDoc.Descendants(opfNs + "item")
                        .FirstOrDefault(i => i.Attribute("id")?.Value == coverId)
                        ?.Attribute("href")?.Value;
                }

                // Strategy 2: item with properties="cover-image"
                coverHref ??= opfDoc.Descendants(opfNs + "item")
                    .FirstOrDefault(i => i.Attribute("properties")?.Value?.Contains("cover-image") == true)
                    ?.Attribute("href")?.Value;

                // Strategy 3: item with id containing "cover" and an image media type
                coverHref ??= opfDoc.Descendants(opfNs + "item")
                    .FirstOrDefault(i =>
                        (i.Attribute("id")?.Value?.Contains("cover", StringComparison.OrdinalIgnoreCase) == true) &&
                        (i.Attribute("media-type")?.Value?.StartsWith("image/") == true))
                    ?.Attribute("href")?.Value;
            }

            if (string.IsNullOrEmpty(coverHref)) return (null, null);

            // Resolve relative path
            var coverPath = string.IsNullOrEmpty(opfDir) ? coverHref : $"{opfDir}/{coverHref}";
            coverPath = coverPath.Replace('\\', '/');

            var coverEntry = zip.GetEntry(coverPath);
            if (coverEntry == null) return (null, null);

            // Copy to a MemoryStream so we can close the zip
            var ms = new MemoryStream();
            using (var coverStream = coverEntry.Open())
            {
                await coverStream.CopyToAsync(ms).ConfigureAwait(false);
            }
            ms.Position = 0;

            var contentType = Path.GetExtension(coverHref).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };

            return (ms, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract cover from epub: {Path}", epubPath);
            return (null, null);
        }
    }
}