using System;
using System.Collections.Generic;
using System.Reflection;
using JellyfinBookReader.Data;
using JellyfinBookReader.Services;
using Xunit;

namespace JellyfinBookReader.Tests.Services;

/// <summary>
/// Tests for the streak computation logic in SessionService.
/// Uses reflection to call the private ComputeStreaks method directly.
/// </summary>
public class StreakComputationTests
{
    private static (int Current, int Longest) ComputeStreaks(List<SessionRow> sessions, DateTime now)
    {
        var method = typeof(SessionService).GetMethod(
            "ComputeStreaks",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var result = method!.Invoke(null, new object[] { sessions, now });
        var tuple = ((int Current, int Longest))result!;
        return tuple;
    }

    private static SessionRow MakeSession(DateTime startedAt, int durationSeconds = 1800)
    {
        return new SessionRow
        {
            Id = Guid.NewGuid().ToString(),
            BookId = Guid.NewGuid(),
            StartedAt = startedAt,
            EndedAt = startedAt.AddSeconds(durationSeconds),
            DurationSeconds = durationSeconds,
        };
    }

    [Fact]
    public void EmptySessions_ReturnsZeroStreaks()
    {
        var (current, longest) = ComputeStreaks(new List<SessionRow>(), DateTime.UtcNow);
        Assert.Equal(0, current);
        Assert.Equal(0, longest);
    }

    [Fact]
    public void SingleSessionToday_ReturnsStreakOfOne()
    {
        var now = DateTime.UtcNow;
        var sessions = new List<SessionRow>
        {
            MakeSession(now.Date.AddHours(10)),
        };

        var (current, longest) = ComputeStreaks(sessions, now);
        Assert.Equal(1, current);
        Assert.Equal(1, longest);
    }

    [Fact]
    public void ConsecutiveDays_CountsStreak()
    {
        var now = DateTime.UtcNow;
        var sessions = new List<SessionRow>
        {
            MakeSession(now.Date.AddHours(10)),              // today
            MakeSession(now.Date.AddDays(-1).AddHours(10)),  // yesterday
            MakeSession(now.Date.AddDays(-2).AddHours(10)),  // 2 days ago
        };

        var (current, longest) = ComputeStreaks(sessions, now);
        Assert.Equal(3, current);
        Assert.Equal(3, longest);
    }

    [Fact]
    public void GapInStreak_BreaksCurrentButKeepsLongest()
    {
        var now = DateTime.UtcNow;
        var sessions = new List<SessionRow>
        {
            MakeSession(now.Date.AddHours(10)),              // today
            // gap: yesterday missing
            MakeSession(now.Date.AddDays(-2).AddHours(10)),  // 2 days ago
            MakeSession(now.Date.AddDays(-3).AddHours(10)),  // 3 days ago
            MakeSession(now.Date.AddDays(-4).AddHours(10)),  // 4 days ago
        };

        var (current, longest) = ComputeStreaks(sessions, now);
        Assert.Equal(1, current);  // only today
        Assert.Equal(3, longest);  // 3 consecutive days (2-3-4 days ago)
    }

    [Fact]
    public void YesterdayOnly_StreakNotBrokenYet()
    {
        var now = DateTime.UtcNow;
        var sessions = new List<SessionRow>
        {
            MakeSession(now.Date.AddDays(-1).AddHours(10)),  // yesterday only
        };

        var (current, longest) = ComputeStreaks(sessions, now);
        Assert.Equal(1, current);  // yesterday counts, streak not broken yet
        Assert.Equal(1, longest);
    }

    [Fact]
    public void TwoDaysAgo_StreakIsBroken()
    {
        var now = DateTime.UtcNow;
        var sessions = new List<SessionRow>
        {
            MakeSession(now.Date.AddDays(-2).AddHours(10)),  // 2 days ago
        };

        var (current, longest) = ComputeStreaks(sessions, now);
        Assert.Equal(0, current);  // streak broken
        Assert.Equal(1, longest);  // but still had a 1-day streak historically
    }

    [Fact]
    public void MultipleSessions_SameDay_CountAsOne()
    {
        var now = DateTime.UtcNow;
        var sessions = new List<SessionRow>
        {
            MakeSession(now.Date.AddHours(8)),
            MakeSession(now.Date.AddHours(12)),
            MakeSession(now.Date.AddHours(20)),
        };

        var (current, longest) = ComputeStreaks(sessions, now);
        Assert.Equal(1, current);
        Assert.Equal(1, longest);
    }

    [Fact]
    public void LongStreak_TrackedCorrectly()
    {
        var now = DateTime.UtcNow;
        var sessions = new List<SessionRow>();

        // 30-day streak ending today
        for (int i = 0; i < 30; i++)
        {
            sessions.Add(MakeSession(now.Date.AddDays(-i).AddHours(10)));
        }

        var (current, longest) = ComputeStreaks(sessions, now);
        Assert.Equal(30, current);
        Assert.Equal(30, longest);
    }

    [Fact]
    public void HistoricStreak_LongerThanCurrent()
    {
        var now = DateTime.UtcNow;
        var sessions = new List<SessionRow>();

        // Current streak: 2 days (today + yesterday)
        sessions.Add(MakeSession(now.Date.AddHours(10)));
        sessions.Add(MakeSession(now.Date.AddDays(-1).AddHours(10)));

        // Gap of 1 day

        // Historical streak: 5 days (3-7 days ago)
        for (int i = 3; i <= 7; i++)
        {
            sessions.Add(MakeSession(now.Date.AddDays(-i).AddHours(10)));
        }

        var (current, longest) = ComputeStreaks(sessions, now);
        Assert.Equal(2, current);
        Assert.Equal(5, longest);
    }

    [Fact]
    public void MultipleSessions_AcrossMidnight_SameDay()
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        var sessions = new List<SessionRow>
        {
            MakeSession(today.AddHours(0).AddMinutes(1)),  // just after midnight
            MakeSession(today.AddHours(23).AddMinutes(59)), // just before midnight
        };

        var (current, longest) = ComputeStreaks(sessions, now);
        Assert.Equal(1, current);
        Assert.Equal(1, longest);
    }

    [Fact]
    public void Streak_WithVeryOldHistory_DoesNotBreak()
    {
        var now = DateTime.UtcNow;
        var sessions = new List<SessionRow>
        {
            MakeSession(now.Date.AddHours(10)),  // today
            MakeSession(now.Date.AddDays(-365).AddHours(10)), // 1 year ago
        };

        var (current, longest) = ComputeStreaks(sessions, now);
        Assert.Equal(1, current);
        Assert.Equal(1, longest);
    }

    [Fact]
    public void Streak_AllSessionsSameDay_LongAgo()
    {
        var now = DateTime.UtcNow;
        var oldDay = now.Date.AddDays(-30);

        var sessions = new List<SessionRow>
        {
            MakeSession(oldDay.AddHours(8)),
            MakeSession(oldDay.AddHours(12)),
            MakeSession(oldDay.AddHours(20)),
        };

        var (current, longest) = ComputeStreaks(sessions, now);
        Assert.Equal(0, current);  // 30 days ago, streak broken
        Assert.Equal(1, longest);  // but had 1 day of reading
    }
}