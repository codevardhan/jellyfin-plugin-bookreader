using System;

namespace JellyfinBookReader.Services;

/// <summary>
/// Backing store for pre-extracted book pages.
/// Two implementations exist: <see cref="InMemoryPageCacheStore"/> for small books
/// and <see cref="DiskPageCacheStore"/> for large ones.
/// Selection is made once per book by <see cref="BookPageCache"/>.
/// </summary>
public interface IPageCacheStore : IDisposable
{
    void Set(int page, byte[] data, string contentType);

    bool TryGet(int page, out byte[]? data, out string? contentType);

    bool HasPage(int page);

    /// <summary>Clears all cached pages for this book and releases resources.</summary>
    void Evict();
}
