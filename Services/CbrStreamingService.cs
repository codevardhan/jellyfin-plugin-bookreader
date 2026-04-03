using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SharpCompress.Readers;

namespace JellyfinBookReader.Services;


/// <summary>
/// Streams pages from CBR (Comic Book RAR) files.
/// Requires NuGet: SharpCompress
///
/// Uses ReaderFactory (the streaming/sequential API) rather than the Archive API,
/// which has had breaking changes across SharpCompress versions.
///
/// The sorted image key list is cached per file path so sequential warm-up
/// (pages 0, 1, 2 ...) only scans the archive once rather than once per page.
/// </summary>
public class CbrStreamingService : IBookStreamingService
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    // Keyed by file path — avoids re-scanning the archive for every warm-up page.
    private readonly ConcurrentDictionary<string, List<string>> _keyCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<CbrStreamingService> _logger;

    public CbrStreamingService(ILogger<CbrStreamingService> logger)
    {
        _logger = logger;
    }

    public bool CanStream(string filePath) =>
        Path.GetExtension(filePath).Equals(".cbr", StringComparison.OrdinalIgnoreCase);

    public Task<int> GetPageCountAsync(string filePath, CancellationToken ct = default) =>
        Task.FromResult(GetSortedImageKeys(filePath).Count);

    public async Task<(Stream? Stream, string? ContentType)> GetPageAsync(
        string filePath, int pageIndex, CancellationToken ct = default)
    {
        try
        {
            var keys = GetSortedImageKeys(filePath);

            if (pageIndex < 0 || pageIndex >= keys.Count)
                return (null, null);

            var targetKey = keys[pageIndex];

            // Sequential scan — stop as soon as we find the target entry.
            using var fileStream = File.OpenRead(filePath);
            using var reader = ReaderFactory.Open(fileStream);

            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory) continue;

                if (!string.Equals(reader.Entry.Key, targetKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                var ms = new MemoryStream();
                using (var entryStream = reader.OpenEntryStream())
                    await entryStream.CopyToAsync(ms, ct).ConfigureAwait(false);

                ms.Position = 0;
                return (ms, MimeForExtension(Path.GetExtension(targetKey)));
            }

            return (null, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CBR extraction failed: {Path} page {Page}", filePath, pageIndex);
            return (null, null);
        }
    }

    /// <summary>
    /// Scans the archive once and returns a sorted list of image entry keys.
    /// Result is cached so repeated calls for the same file are O(1).
    /// </summary>
    private List<string> GetSortedImageKeys(string filePath) =>
        _keyCache.GetOrAdd(filePath, path =>
        {
            var keys = new List<string>();
            using var stream = File.OpenRead(path);
            using var reader = ReaderFactory.Open(stream);

            while (reader.MoveToNextEntry())
            {
                if (!reader.Entry.IsDirectory &&
                    ImageExtensions.Contains(Path.GetExtension(reader.Entry.Key ?? string.Empty)))
                {
                    keys.Add(reader.Entry.Key!);
                }
            }

            keys.Sort(StringComparer.OrdinalIgnoreCase);
            return keys;
        });

    private static string MimeForExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/jpeg",
    };
}