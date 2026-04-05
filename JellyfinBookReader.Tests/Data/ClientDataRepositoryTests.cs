using System;
using System.Linq;
using System.Threading;
using JellyfinBookReader.Data;
using JellyfinBookReader.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyfinBookReader.Tests.Data;

public class ClientDataRepositoryTests : IDisposable
{
    private readonly TestDbFixture _fixture;
    private readonly ClientDataRepository _repo;

    private static readonly Guid User1 = Guid.NewGuid();
    private static readonly Guid User2 = Guid.NewGuid();
    private static readonly Guid Book1 = Guid.NewGuid();
    private static readonly Guid Book2 = Guid.NewGuid();

    public ClientDataRepositoryTests()
    {
        _fixture = new TestDbFixture();
        _repo = new ClientDataRepository(_fixture.DbContext, NullLogger<ClientDataRepository>.Instance);
    }

    //  Get 

    [Fact]
    public void Get_ReturnsNull_WhenNoDataExists()
    {
        var result = _repo.Get(User1, Book1);
        Assert.Null(result);
    }

    [Fact]
    public void Get_ReturnsData_AfterUpsert()
    {
        _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = "{\"quotes\":[]}" });

        var result = _repo.Get(User1, Book1);
        Assert.NotNull(result);
        Assert.Equal("{\"quotes\":[]}", result.Data);
    }

    //  Upsert 

    [Fact]
    public void Upsert_CreatesNewRow_WithProvidedData()
    {
        const string json = "{\"quotes\":[{\"id\":\"abc\",\"text\":\"Hello world\"}]}";

        var (status, _) = _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = json });

        Assert.Equal("updated", status);
        var stored = _repo.Get(User1, Book1);
        Assert.NotNull(stored);
        Assert.Equal(json, stored.Data);
    }

    [Fact]
    public void Upsert_OverwritesExistingRow()
    {
        _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = "{\"v\":1}" });
        _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = "{\"v\":2}" });

        var result = _repo.Get(User1, Book1);
        Assert.NotNull(result);
        Assert.Equal("{\"v\":2}", result.Data);
    }

    [Fact]
    public void Upsert_SetsUpdatedAt_Automatically()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = "{}" });
        var after = DateTime.UtcNow.AddSeconds(1);

        var result = _repo.Get(User1, Book1);
        Assert.NotNull(result);
        Assert.InRange(result.UpdatedAt, before, after);
    }

    [Fact]
    public void Upsert_ReturnsUpdated_WhenNoUpdatedAtSent()
    {
        // First write — no prior server state, so no conflict check possible.
        var (status, _) = _repo.Upsert(User1, Book1, new ClientDataUpdateDto
        {
            Data = "{}",
            UpdatedAt = null,
        });

        Assert.Equal("updated", status);
    }

    //  Conflict detection 

    [Fact]
    public void Upsert_DetectsConflict_WhenClientUpdatedAtIsOlderThanServer()
    {
        _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = "{\"v\":1}" });

        Thread.Sleep(50); // ensure server timestamp is measurably newer

        var (status, serverData) = _repo.Upsert(User1, Book1, new ClientDataUpdateDto
        {
            Data = "{\"v\":2}",
            UpdatedAt = DateTime.UtcNow.AddHours(-1), // stale
        });

        Assert.Equal("conflict", status);
        Assert.NotNull(serverData);
        Assert.Equal("{\"v\":1}", serverData.Data);
    }

    [Fact]
    public void Upsert_NoConflict_WhenClientUpdatedAtIsNewerThanServer()
    {
        _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = "{\"v\":1}" });

        var (status, _) = _repo.Upsert(User1, Book1, new ClientDataUpdateDto
        {
            Data = "{\"v\":2}",
            UpdatedAt = DateTime.UtcNow.AddHours(1), // fresher
        });

        Assert.Equal("updated", status);
        Assert.Equal("{\"v\":2}", _repo.Get(User1, Book1)!.Data);
    }

    [Fact]
    public void Upsert_NoConflict_WhenExactSameUpdatedAt()
    {
        _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = "{\"v\":1}" });
        var current = _repo.Get(User1, Book1)!;

        // Exact same timestamp is not strictly older — no conflict.
        var (status, _) = _repo.Upsert(User1, Book1, new ClientDataUpdateDto
        {
            Data = "{\"v\":2}",
            UpdatedAt = current.UpdatedAt,
        });

        Assert.Equal("updated", status);
    }

    [Fact]
    public void Upsert_Conflict_DoesNotMutateStoredData()
    {
        _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = "{\"v\":1}" });

        _repo.Upsert(User1, Book1, new ClientDataUpdateDto
        {
            Data = "{\"v\":99}",
            UpdatedAt = DateTime.UtcNow.AddHours(-1),
        });

        // Original data must be intact.
        Assert.Equal("{\"v\":1}", _repo.Get(User1, Book1)!.Data);
    }

    //  GetAllForUser 

    [Fact]
    public void GetAllForUser_ReturnsAllBooksForUser()
    {
        _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = "{\"a\":1}" });
        _repo.Upsert(User1, Book2, new ClientDataUpdateDto { Data = "{\"a\":2}" });
        _repo.Upsert(User2, Book1, new ClientDataUpdateDto { Data = "{\"a\":3}" });

        var user1Data = _repo.GetAllForUser(User1);

        Assert.Equal(2, user1Data.Count);
        Assert.True(user1Data.ContainsKey(Book1));
        Assert.True(user1Data.ContainsKey(Book2));
        Assert.Equal("{\"a\":1}", user1Data[Book1].Data);
        Assert.Equal("{\"a\":2}", user1Data[Book2].Data);
    }

    [Fact]
    public void GetAllForUser_ReturnsEmpty_WhenNoData()
    {
        var result = _repo.GetAllForUser(Guid.NewGuid());
        Assert.Empty(result);
    }

    [Fact]
    public void GetAllForUser_IsolatedPerUser()
    {
        _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = "{\"u\":1}" });
        _repo.Upsert(User2, Book1, new ClientDataUpdateDto { Data = "{\"u\":2}" });

        var user2Data = _repo.GetAllForUser(User2);
        Assert.Single(user2Data);
        Assert.Equal("{\"u\":2}", user2Data[Book1].Data);
    }

    //  Delete 

    [Fact]
    public void Delete_RemovesRow()
    {
        _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = "{}" });

        var deleted = _repo.Delete(User1, Book1);
        Assert.True(deleted);
        Assert.Null(_repo.Get(User1, Book1));
    }

    [Fact]
    public void Delete_ReturnsFalse_WhenNoRowExists()
    {
        var deleted = _repo.Delete(User1, Guid.NewGuid());
        Assert.False(deleted);
    }

    [Fact]
    public void Delete_DoesNotAffectOtherBooks()
    {
        _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = "{\"b\":1}" });
        _repo.Upsert(User1, Book2, new ClientDataUpdateDto { Data = "{\"b\":2}" });

        _repo.Delete(User1, Book1);

        Assert.Null(_repo.Get(User1, Book1));
        Assert.NotNull(_repo.Get(User1, Book2));
    }

    [Fact]
    public void Delete_DoesNotAffectOtherUsers()
    {
        _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = "{\"u\":1}" });
        _repo.Upsert(User2, Book1, new ClientDataUpdateDto { Data = "{\"u\":2}" });

        _repo.Delete(User1, Book1);

        Assert.Null(_repo.Get(User1, Book1));
        Assert.NotNull(_repo.Get(User2, Book1));
    }

    //  Data integrity 

    [Fact]
    public void Upsert_HandlesLargeJsonBlob()
    {
        // Simulate a realistic client payload: 50 saved quotes
        var quotes = string.Join(",", System.Linq.Enumerable.Range(1, 50)
            .Select(i => $"{{\"id\":\"{Guid.NewGuid()}\",\"text\":\"Quote number {i}\",\"page\":{i * 4}}}"));
        var json = $"{{\"quotes\":[{quotes}]}}";

        _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = json });

        var result = _repo.Get(User1, Book1);
        Assert.NotNull(result);
        Assert.Equal(json, result.Data);
    }

    [Fact]
    public void Upsert_HandlesUnicodeInData()
    {
        const string json = "{\"quotes\":[{\"text\":\"第一章：开始 — ñ ü ö ☕ 🎉\"}]}";

        _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = json });

        var result = _repo.Get(User1, Book1);
        Assert.NotNull(result);
        Assert.Equal(json, result.Data);
    }

    [Fact]
    public void Upsert_StoresMinimalEmptyObject()
    {
        _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = "{}" });

        var result = _repo.Get(User1, Book1);
        Assert.NotNull(result);
        Assert.Equal("{}", result.Data);
    }

    [Fact]
    public void GetAllForUser_CorrectlyMapsUpdatedAt()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        _repo.Upsert(User1, Book1, new ClientDataUpdateDto { Data = "{}" });
        var after = DateTime.UtcNow.AddSeconds(1);

        var all = _repo.GetAllForUser(User1);
        Assert.InRange(all[Book1].UpdatedAt, before, after);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}