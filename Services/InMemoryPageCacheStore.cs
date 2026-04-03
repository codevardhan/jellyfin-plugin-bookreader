using System.Collections.Concurrent;

namespace JellyfinBookReader.Services;

/// <summary>
/// In-memory page cache — used for books below <c>LargeBookThresholdMb</c>.
/// Fast reads (no I/O), but all extracted bytes live on the managed heap.
/// Eviction simply clears the dictionary; GC reclaims memory immediately.
/// </summary>
public sealed class InMemoryPageCacheStore : IPageCacheStore
{
    private readonly ConcurrentDictionary<int, (byte[] Data, string ContentType)> _pages = new();

    public void Set(int page, byte[] data, string contentType) =>
        _pages.TryAdd(page, (data, contentType));

    public bool TryGet(int page, out byte[]? data, out string? contentType)
    {
        if (_pages.TryGetValue(page, out var entry))
        {
            (data, contentType) = entry;
            return true;
        }

        (data, contentType) = (null, null);
        return false;
    }

    public bool HasPage(int page) => _pages.ContainsKey(page);

    public void Evict() => _pages.Clear();

    public void Dispose() => Evict();
}
