using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JellyfinBookReader.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyfinBookReader.Tasks;

public class StaleSessionTask : IScheduledTask
{
    private readonly SessionService _sessionService;
    private readonly ILogger<StaleSessionTask> _logger;

    public StaleSessionTask(SessionService sessionService, ILogger<StaleSessionTask> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    public string Name => "Close Stale Reading Sessions";

    public string Key => "BookReaderStaleSessionCleanup";

    public string Description =>
        "Automatically closes reading sessions that haven't received a heartbeat within the configured timeout.";

    public string Category => "Book Reader";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var timeout = Plugin.Instance?.Configuration?.StaleSessionTimeoutMinutes ?? 30;

        _logger.LogDebug("Running stale session cleanup with {Timeout} minute timeout.", timeout);

        var closed = _sessionService.CloseStaleSessionsGlobal(timeout);

        _logger.LogInformation("Stale session cleanup complete. Closed {Count} sessions.", closed);
        progress.Report(100);

        return Task.CompletedTask;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromMinutes(15).Ticks,
            }
        };
    }
}