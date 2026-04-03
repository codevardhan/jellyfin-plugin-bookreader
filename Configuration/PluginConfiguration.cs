using MediaBrowser.Model.Plugins;

namespace JellyfinBookReader.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    //  Existing settings 

    /// <summary>Minutes before an open reading session is considered stale and auto-closed.</summary>
    public int StaleSessionTimeoutMinutes { get; set; } = 30;

    /// <summary>Maximum number of books returned per page in list endpoints.</summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>Default number of books returned per page.</summary>
    public int DefaultPageSize { get; set; } = 20;

    //  Streaming / cache settings 

    /// <summary>
    /// Books whose file size is at or above this threshold use the disk-backed
    /// page cache (<see cref="Services.DiskPageCacheStore"/>) instead of the
    /// in-memory store (<see cref="Services.InMemoryPageCacheStore"/>).
    ///
    /// Default: 50 MB. A 300-page scanned comic at ~3 MB/page = 900 MB in memory,
    /// so anything above a modest graphic novel should go to disk.
    /// </summary>
    public int LargeBookThresholdMb { get; set; } = 50;

    /// <summary>
    /// Number of pages to pre-extract into cache when a reading session starts.
    /// These pages are fetched by the background warm-up worker before the user
    /// reaches them, so the first N page requests are served entirely from cache.
    ///
    /// Default: 10. Increase for CBR files (sequential RAR access) where mid-book
    /// cold reads are expensive.
    /// </summary>
    public int WarmUpInitialPages { get; set; } = 10;

    /// <summary>
    /// Number of pages to queue for prefetch after a cache miss.
    /// When the page endpoint serves a miss it publishes a new warm-up request
    /// covering pages [missedPage+1 .. missedPage+PrefetchWindow] so the user
    /// never hits two cold reads in a row during normal forward reading.
    ///
    /// Default: 5.
    /// </summary>
    public int WarmUpPrefetchWindow { get; set; } = 5;

    /// <summary>
    /// Maximum number of warm-up requests that can be queued before the oldest
    /// ones are dropped. Prevents memory growth when many sessions start at once.
    ///
    /// Default: 20.
    /// </summary>
    public int WarmUpChannelCapacity { get; set; } = 20;
}
