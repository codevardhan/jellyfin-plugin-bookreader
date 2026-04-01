using System;
using System.Collections.Generic;
using System.Linq;
using JellyfinBookReader.Data;
using JellyfinBookReader.Dto;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace JellyfinBookReader.Services;

public class SessionService
{
    private readonly SessionRepository _repo;
    private readonly ProgressRepository _progressRepo;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<SessionService> _logger;

    public SessionService(
        SessionRepository repo,
        ProgressRepository progressRepo,
        ILibraryManager libraryManager,
        ILogger<SessionService> logger)
    {
        _repo = repo;
        _progressRepo = progressRepo;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public StartSessionResponse StartSession(Guid userId, Guid bookId)
    {
        var (sessionId, startedAt) = _repo.StartSession(userId, bookId);
        return new StartSessionResponse
        {
            SessionId = sessionId,
            StartedAt = startedAt,
        };
    }

    public bool Heartbeat(string sessionId) =>
        _repo.Heartbeat(sessionId);

    public bool EndSession(EndSessionRequest request) =>
        _repo.EndSession(request.SessionId, request.PagesRead, request.PercentageAdvanced);

    public int CloseStaleSessionsGlobal(int timeoutMinutes) =>
        _repo.CloseStaleSessionsGlobal(timeoutMinutes);

    public ReadingStatsDto GetStats(Guid userId)
    {
        var sessions = _repo.GetSessionsForUser(userId);
        var progressMap = _progressRepo.GetAllForUser(userId);
        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);

        // Total stats
        long totalTime = sessions.Sum(s => (long)s.DurationSeconds);
        int totalSessions = sessions.Count;
        int totalFinished = progressMap.Values.Count(p => p.IsFinished);

        // Last 30 days
        var recentSessions = sessions.Where(s => s.StartedAt >= thirtyDaysAgo).ToList();
        long recentTime = recentSessions.Sum(s => (long)s.DurationSeconds);
        int recentCount = recentSessions.Count;
        // Books finished in last 30 days: progress with IsFinished and LastReadAt in range
        int recentFinished = progressMap.Values
            .Count(p => p.IsFinished && p.LastReadAt >= thirtyDaysAgo);

        // Daily average: total time / number of distinct days with sessions
        var distinctDays = sessions
            .Select(s => s.StartedAt.Date)
            .Distinct()
            .Count();
        long dailyAvg = distinctDays > 0 ? totalTime / distinctDays : 0;

        // Streaks (based on calendar days UTC)
        var (currentStreak, longestStreak) = ComputeStreaks(sessions, now);

        // Per-book stats
        var perBook = sessions
            .GroupBy(s => s.BookId)
            .Select(g =>
            {
                var bookId = g.Key;
                var title = _libraryManager.GetItemById(bookId)?.Name ?? "Unknown";

                return new PerBookStats
                {
                    BookId = bookId.ToString(),
                    Title = title,
                    TotalTimeSeconds = g.Sum(s => (long)s.DurationSeconds),
                    SessionsCount = g.Count(),
                };
            })
            .OrderByDescending(b => b.TotalTimeSeconds)
            .ToList();

        return new ReadingStatsDto
        {
            UserId = userId.ToString(),
            TotalReadingTimeSeconds = totalTime,
            TotalSessions = totalSessions,
            TotalBooksFinished = totalFinished,
            CurrentStreak = currentStreak,
            LongestStreak = longestStreak,
            DailyAverageSeconds = dailyAvg,
            Last30Days = new Last30DaysStats
            {
                ReadingTimeSeconds = recentTime,
                SessionsCount = recentCount,
                BooksFinished = recentFinished,
            },
            PerBook = perBook,
        };
    }

    /// <summary>
    /// Compute current and longest reading streaks.
    /// A day counts if the user had at least one session on that calendar day (UTC).
    /// </summary>
    private static (int Current, int Longest) ComputeStreaks(List<SessionRow> sessions, DateTime now)
    {
        if (sessions.Count == 0) return (0, 0);

        var readingDays = sessions
            .Select(s => s.StartedAt.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();

        if (readingDays.Count == 0) return (0, 0);

        // Current streak: count consecutive days going back from today/yesterday
        int currentStreak = 0;
        var checkDate = now.Date;

        // If user didn't read today, check if they read yesterday (streak not broken yet)
        if (readingDays[0] < checkDate)
        {
            if (readingDays[0] < checkDate.AddDays(-1))
            {
                // Last session was 2+ days ago — streak is broken
                currentStreak = 0;
            }
            else
            {
                checkDate = checkDate.AddDays(-1);
            }
        }

        foreach (var day in readingDays)
        {
            if (day == checkDate)
            {
                currentStreak++;
                checkDate = checkDate.AddDays(-1);
            }
            else if (day < checkDate)
            {
                break;
            }
        }

        // Longest streak: scan all days chronologically
        var ascending = readingDays.OrderBy(d => d).ToList();
        int longestStreak = 1;
        int streak = 1;

        for (int i = 1; i < ascending.Count; i++)
        {
            if (ascending[i] == ascending[i - 1].AddDays(1))
            {
                streak++;
                longestStreak = Math.Max(longestStreak, streak);
            }
            else
            {
                streak = 1;
            }
        }

        longestStreak = Math.Max(longestStreak, currentStreak);

        return (currentStreak, longestStreak);
    }
}