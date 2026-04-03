using System;
using System.IO;

namespace JellyfinBookReader.Services;

/// <summary>
/// Disk-backed page cache — used for books at or above <c>LargeBookThresholdMb</c>.
///
/// Each page is written to <c>%TEMP%/jellyfin-bookreader/{bookId}/{page:D5}.ext</c>.
/// The file extension encodes the content type so no separate metadata file is needed.
/// Reads use <see cref="File.ReadAllBytes"/> (synchronous) which is acceptable since
/// the warm-up path is always async and controller reads are infrequent per request.
///
/// Eviction deletes the entire book directory recursively.
/// </summary>
public sealed class DiskPageCacheStore : IPageCacheStore
{
    private readonly string _dir;
    private bool _disposed;

    private static readonly string[] KnownExtensions = { ".jpg", ".png", ".webp", ".gif", ".xhtml" };

    public DiskPageCacheStore(Guid bookId)
    {
        _dir = Path.Combine(
            Path.GetTempPath(), "jellyfin-bookreader", bookId.ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    // Zero-padded so directory listings sort correctly (00000.jpg, 00001.jpg, ...).
    private string PagePath(int page, string ext) =>
        Path.Combine(_dir, $"{page:D5}{ext}");

    private static string ExtForMime(string mime) => mime switch
    {
        "image/png"             => ".png",
        "image/webp"            => ".webp",
        "image/gif"             => ".gif",
        "application/xhtml+xml" => ".xhtml",
        _                       => ".jpg",
    };

    private static string MimeForExt(string ext) => ext switch
    {
        ".png"   => "image/png",
        ".webp"  => "image/webp",
        ".gif"   => "image/gif",
        ".xhtml" => "application/xhtml+xml",
        _        => "image/jpeg",
    };

    public void Set(int page, byte[] data, string contentType)
    {
        var path = PagePath(page, ExtForMime(contentType));
        if (!File.Exists(path))
            File.WriteAllBytes(path, data);
    }

    public bool TryGet(int page, out byte[]? data, out string? contentType)
    {
        foreach (var ext in KnownExtensions)
        {
            var path = PagePath(page, ext);
            if (!File.Exists(path)) continue;

            data = File.ReadAllBytes(path);
            contentType = MimeForExt(ext);
            return true;
        }

        (data, contentType) = (null, null);
        return false;
    }

    public bool HasPage(int page)
    {
        foreach (var ext in KnownExtensions)
        {
            if (File.Exists(PagePath(page, ext))) return true;
        }
        return false;
    }

    public void Evict()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Evict();
    }
}
