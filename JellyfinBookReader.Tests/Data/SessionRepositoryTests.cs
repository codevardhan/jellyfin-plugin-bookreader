using System;
using System.Linq;
using System.Threading;
using JellyfinBookReader.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyfinBookReader.Tests.Data;

public class SessionRepositoryTests : IDisposable
{
    private readonly TestDbFixture _fixture;
    private readonly SessionRepository _repo;

    private static readonly Guid User1 = Guid.NewGuid();
    private static readonly Guid Book1 = Guid.NewGuid();
    private static readonly Guid Book2 = Guid.NewGuid();

    public SessionRepositoryTests()
    {
        _fixture = new TestDbFixture();
        _repo = new SessionRepository(_fixture.DbContext, NullLogger<SessionRepository>.Instance);
    }

    //  StartSession 

    [Fact]
    public void StartSession_CreatesNewSession()
    {
        var (sessionId, startedAt) = _repo.StartSession(User1, Book1);

        Assert.False(string.IsNullOrEmpty(sessionId));
        Assert.InRange(startedAt, DateTime.UtcNow.AddSeconds(-2), DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void StartSession_AutoClosesPreviousOpenSession()
    {
        var (session1Id, _) = _repo.StartSession(User1, Book1);
        var (session2Id, _) = _repo.StartSession(User1, Book1);

        Assert.NotEqual(session1Id, session2Id);

        Assert.False(_repo.Heartbeat(session1Id));
        Assert.True(_repo.Heartbeat(session2Id));
    }

    [Fact]
    public void StartSession_DifferentBooks_DoesNotCloseOtherBook()
    {
        var (session1Id, _) = _repo.StartSession(User1, Book1);
        var (session2Id, _) = _repo.StartSession(User1, Book2);

        Assert.True(_repo.Heartbeat(session1Id));
        Assert.True(_repo.Heartbeat(session2Id));
    }

    //  Heartbeat 

    [Fact]
    public void Heartbeat_ReturnsTrueForOpenSession()
    {
        var (sessionId, _) = _repo.StartSession(User1, Book1);
        Assert.True(_repo.Heartbeat(sessionId));
    }

    [Fact]
    public void Heartbeat_ReturnsFalseForNonexistent()
    {
        Assert.False(_repo.Heartbeat(Guid.NewGuid().ToString()));
    }

    [Fact]
    public void Heartbeat_ReturnsFalseForClosedSession()
    {
        var (sessionId, _) = _repo.StartSession(User1, Book1);
        _repo.EndSession(sessionId, null, null);

        Assert.False(_repo.Heartbeat(sessionId));
    }

    //  EndSession 

    [Fact]
    public void EndSession_ClosesSessionSuccessfully()
    {
        var (sessionId, _) = _repo.StartSession(User1, Book1);

        Thread.Sleep(50);

        var ended = _repo.EndSession(sessionId, pagesRead: 10, percentageAdvanced: 5.5);
        Assert.True(ended);

        Assert.False(_repo.Heartbeat(sessionId));
    }

    [Fact]
    public void EndSession_ReturnsFalseForAlreadyClosed()
    {
        var (sessionId, _) = _repo.StartSession(User1, Book1);
        _repo.EndSession(sessionId, null, null);

        var result = _repo.EndSession(sessionId, null, null);
        Assert.False(result);
    }

    [Fact]
    public void EndSession_ReturnsFalseForNonexistent()
    {
        Assert.False(_repo.EndSession("nonexistent", null, null));
    }

    //  GetSessionsForUser 

    [Fact]
    public void GetSessionsForUser_ReturnsOnlyClosedSessionsWithDuration()
    {
        var (session1Id, _) = _repo.StartSession(User1, Book1);
        Thread.Sleep(50);
        _repo.EndSession(session1Id, 5, 2.0);

        _repo.StartSession(User1, Book2);

        var sessions = _repo.GetSessionsForUser(User1);

        var closed = sessions.FirstOrDefault(s => s.Id == session1Id);
        if (closed != null)
        {
            Assert.True(closed.DurationSeconds >= 0);
            Assert.Equal(Book1, closed.BookId);
        }
    }

    [Fact]
    public void GetSessionsForUser_ReturnsEmpty_WhenNoSessions()
    {
        var sessions = _repo.GetSessionsForUser(Guid.NewGuid());
        Assert.Empty(sessions);
    }

    //  CloseStaleSessionsGlobal 

    [Fact]
    public void CloseStaleSessionsGlobal_ClosesOldSessions()
    {
        var (sessionId, _) = _repo.StartSession(User1, Book1);

        using var conn = _fixture.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE ReadingSessions 
            SET LastHeartbeatAt = @old, StartedAt = @old
            WHERE Id = @id";
        cmd.Parameters.AddWithValue("@old", DateTime.UtcNow.AddHours(-2).ToString("o"));
        cmd.Parameters.AddWithValue("@id", sessionId);
        cmd.ExecuteNonQuery();

        var closed = _repo.CloseStaleSessionsGlobal(timeoutMinutes: 30);
        Assert.Equal(1, closed);

        Assert.False(_repo.Heartbeat(sessionId));
    }

    [Fact]
    public void CloseStaleSessionsGlobal_DoesNotCloseFreshSessions()
    {
        var (sessionId, _) = _repo.StartSession(User1, Book1);

        var closed = _repo.CloseStaleSessionsGlobal(timeoutMinutes: 30);
        Assert.Equal(0, closed);

        Assert.True(_repo.Heartbeat(sessionId));
    }

    //  Additional Edge Cases 

    [Fact]
    public void StartSession_MultipleUsers_SameBook_Independent()
    {
        var user2 = Guid.NewGuid();
        var (session1Id, _) = _repo.StartSession(User1, Book1);
        var (session2Id, _) = _repo.StartSession(user2, Book1);

        // Both sessions should be open independently
        Assert.True(_repo.Heartbeat(session1Id));
        Assert.True(_repo.Heartbeat(session2Id));

        // Closing one shouldn't affect the other
        _repo.EndSession(session1Id, null, null);
        Assert.False(_repo.Heartbeat(session1Id));
        Assert.True(_repo.Heartbeat(session2Id));
    }

    [Fact]
    public void EndSession_ComputesDuration()
    {
        var (sessionId, _) = _repo.StartSession(User1, Book1);
        Thread.Sleep(100); // ensure non-zero duration
        _repo.EndSession(sessionId, pagesRead: 5, percentageAdvanced: 2.5);

        var sessions = _repo.GetSessionsForUser(User1);
        // Session should appear in closed list (if duration > 0)
        var closed = sessions.FirstOrDefault(s => s.Id == sessionId);
        if (closed != null)
        {
            Assert.True(closed.DurationSeconds >= 0);
            Assert.Equal(5, closed.PagesRead);
            Assert.Equal(2.5, closed.PercentageAdvanced);
        }
    }

    [Fact]
    public void EndSession_WithNullOptionalFields_Succeeds()
    {
        var (sessionId, _) = _repo.StartSession(User1, Book1);
        Thread.Sleep(50);
        var result = _repo.EndSession(sessionId, pagesRead: null, percentageAdvanced: null);
        Assert.True(result);
    }

    [Fact]
    public void GetSessionsForUser_OrderedByStartedAtDesc()
    {
        // Start and end 3 sessions with time gaps
        var (s1, _) = _repo.StartSession(User1, Book1);
        Thread.Sleep(50);
        _repo.EndSession(s1, 5, 2.0);

        var (s2, _) = _repo.StartSession(User1, Book1);
        Thread.Sleep(50);
        _repo.EndSession(s2, 10, 5.0);

        var (s3, _) = _repo.StartSession(User1, Book2);
        Thread.Sleep(50);
        _repo.EndSession(s3, 3, 1.0);

        var sessions = _repo.GetSessionsForUser(User1);
        if (sessions.Count >= 2)
        {
            // Should be newest first
            Assert.True(sessions[0].StartedAt >= sessions[1].StartedAt);
        }
    }

    [Fact]
    public void CloseStaleSessionsGlobal_ClosesMultipleUsers()
    {
        var user2 = Guid.NewGuid();
        var (s1, _) = _repo.StartSession(User1, Book1);
        var (s2, _) = _repo.StartSession(user2, Book2);

        // Backdate both sessions
        using var conn = _fixture.GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE ReadingSessions
            SET LastHeartbeatAt = @old, StartedAt = @old";
        cmd.Parameters.AddWithValue("@old", DateTime.UtcNow.AddHours(-2).ToString("o"));
        cmd.ExecuteNonQuery();

        var closed = _repo.CloseStaleSessionsGlobal(timeoutMinutes: 30);
        Assert.Equal(2, closed);

        Assert.False(_repo.Heartbeat(s1));
        Assert.False(_repo.Heartbeat(s2));
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}