using System;
using System.Collections.Concurrent;
using System.IO;
using JellyfinBookReader.Configuration;
using Microsoft.Extensions.Logging;

namespace JellyfinBookReader.Services;

/// <summary>
/// Coordinator that owns the per-book <see cref="IPageCacheStore"/> registry.
///
/// On first access for a book it reads <c>FileInfo.Length</c> and compares it against
/// <c>PluginConfiguration.LargeBookThresholdMb</c> to decide which backing store to
/// create — <see cref="InMemoryPageCacheStore"/> for small books,
/// <see cref="DiskPageCacheStore"/> for large ones.
///
/// If the disk store cannot be created (e.g. stale /tmp directory owned by another
/// user after a service restart), it logs a warning and falls back to in-memory so
/// page requests never result in a 500 error.
///
/// The store selection is permanent for the lifetime of the session: a book that was
/// small enough for memory when first opened will not migrate to disk mid-session.
/// </summary>
public class BookPageCache
{
    private readonly ConcurrentDictionary<Guid, IPageCacheStore> _stores = new();
    private readonly ILogger<BookPageCache> _logger;

    public BookPageCache(ILogger<BookPageCache> logger)
    {
        _logger = logger;
    }

    private long ThresholdBytes
    {
        get
        {
            var mb = Plugin.Instance?.Configuration?.LargeBookThresholdMb ?? 50;
            return (long)mb * 1024L * 1024L;
        }
    }

    /// <summary>
    /// Returns the existing store for the book, creating one if this is the first access.
    /// <paramref name="filePath"/> is required only when creating — ignored on cache hits.
    /// </summary>
    public IPageCacheStore GetOrCreateStore(Guid bookId, string filePath) =>
        _stores.GetOrAdd(bookId, _ => CreateStore(bookId, filePath));

    private IPageCacheStore CreateStore(Guid bookId, string filePath)
    {
        var fileSize = new FileInfo(filePath).Length;
        if (fileSize < ThresholdBytes)
            return new InMemoryPageCacheStore();

        // Large book — try disk cache. Fall back to in-memory if the temp
        // directory is inaccessible (e.g. owned by another user from a
        // previous run with different permissions).
        try
        {
            return new DiskPageCacheStore(bookId);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex,
                "Cannot create disk cache store for book {BookId} " +
                "— falling back to in-memory. " +
                "Run: sudo rm -rf /tmp/jellyfin-bookreader/ to clear stale directories.",
                bookId);
            return new InMemoryPageCacheStore();
        }
    }

    //  Convenience pass-throughs used by controller + background service 

    public void Set(Guid bookId, string filePath, int page, byte[] data, string contentType) =>
        GetOrCreateStore(bookId, filePath).Set(page, data, contentType);

    public bool TryGet(Guid bookId, int page, out byte[]? data, out string? contentType)
    {
        if (_stores.TryGetValue(bookId, out var store))
            return store.TryGet(page, out data, out contentType);

        (data, contentType) = (null, null);
        return false;
    }

    public bool HasPage(Guid bookId, int page) =>
        _stores.TryGetValue(bookId, out var store) && store.HasPage(page);

    /// <summary>
    /// Removes and disposes the store for this book.
    /// For <see cref="DiskPageCacheStore"/> this deletes the temp directory.
    /// </summary>
    public void Evict(Guid bookId)
    {
        if (_stores.TryRemove(bookId, out var store))
            store.Dispose();
    }
}