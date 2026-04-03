using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyfinBookReader.Services;

/// <summary>
/// Streams pages from CBZ (Comic Book ZIP) files.
/// CBZ archives contain image files sorted alphabetically — no manifest required.
/// ZipArchive supports random entry access, so page N is O(1) to locate.
/// </summary>
public class CbzStreamingService : IBookStreamingService
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    private readonly ILogger<CbzStreamingService> _logger;

    public CbzStreamingService(ILogger<CbzStreamingService> logger)
    {
        _logger = logger;
    }

    public bool CanStream(string filePath) =>
        Path.GetExtension(filePath).Equals(".cbz", StringComparison.OrdinalIgnoreCase);

    public Task<int> GetPageCountAsync(string filePath, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(filePath);
        return Task.FromResult(GetSortedImageEntries(zip).Count);
    }

    public async Task<(Stream? Stream, string? ContentType)> GetPageAsync(
        string filePath, int pageIndex, CancellationToken ct = default)
    {
        try
        {
            using var zip = ZipFile.OpenRead(filePath);
            var entries = GetSortedImageEntries(zip);

            if (pageIndex < 0 || pageIndex >= entries.Count)
                return (null, null);

            var entry = entries[pageIndex];
            var ms = new MemoryStream((int)entry.Length);

            using (var entryStream = entry.Open())
                await entryStream.CopyToAsync(ms, ct).ConfigureAwait(false);

            ms.Position = 0;
            return (ms, MimeForExtension(Path.GetExtension(entry.Name)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CBZ extraction failed: {Path} page {Page}", filePath, pageIndex);
            return (null, null);
        }
    }

    // Entries sorted by full path — the CBZ convention (001.jpg, 002.jpg, ...).
    private static List<ZipArchiveEntry> GetSortedImageEntries(ZipArchive zip) =>
        zip.Entries
           .Where(e => ImageExtensions.Contains(Path.GetExtension(e.Name)))
           .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
           .ToList();

    private static string MimeForExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".png"  => "image/png",
        ".webp" => "image/webp",
        ".gif"  => "image/gif",
        _       => "image/jpeg",
    };
}
