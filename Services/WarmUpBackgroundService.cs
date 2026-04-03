using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using JellyfinBookReader.Dto;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyfinBookReader.Services;

/// <summary>
/// Long-running background worker that reads <see cref="WarmUpRequest"/> items from the
/// bounded channel and pre-extracts pages into <see cref="BookPageCache"/>.
///
/// A single reader processes requests sequentially. This is intentional:
/// concurrent archive extractions on the same file would contend on disk I/O
/// without meaningful throughput gain.
///
/// The channel is bounded with <c>DropOldest</c> overflow — if requests pile up faster
/// than the worker can drain them (e.g. many sessions starting simultaneously), the
/// oldest stale requests are discarded rather than growing memory unboundedly.
/// </summary>
public class WarmUpBackgroundService : BackgroundService
{
    private readonly Channel<WarmUpRequest> _channel;
    private readonly StreamingServiceFactory _factory;
    private readonly BookPageCache _cache;
    private readonly ILogger<WarmUpBackgroundService> _logger;

    public WarmUpBackgroundService(
        Channel<WarmUpRequest> channel,
        StreamingServiceFactory factory,
        BookPageCache cache,
        ILogger<WarmUpBackgroundService> logger)
    {
        _channel = channel;
        _factory = factory;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var req in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            await WarmUpAsync(req, ct).ConfigureAwait(false);
        }
    }

    private async Task WarmUpAsync(WarmUpRequest req, CancellationToken ct)
    {
        var service = _factory.GetService(req.FilePath);
        if (service == null) return;

        var end = req.StartPage + req.PageCount;

        for (var i = req.StartPage; i < end; i++)
        {
            ct.ThrowIfCancellationRequested();

            // Skip pages already cached (e.g. from an earlier overlapping prefetch window)
            if (_cache.HasPage(req.BookId, i)) continue;

            try
            {
                var (stream, contentType) = await service
                    .GetPageAsync(req.FilePath, i, ct)
                    .ConfigureAwait(false);

                if (stream == null) break; // past end of book — stop early

                await using (stream.ConfigureAwait(false))
                {
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
                    _cache.Set(req.BookId, req.FilePath, i, ms.ToArray(), contentType!);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Warm-up cancelled for book {BookId} at page {Page}", req.BookId, i);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Warm-up failed for book {BookId} at page {Page} — aborting batch",
                    req.BookId, i);
                break;
            }
        }
    }
}
