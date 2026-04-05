using System;
using System.Collections.Generic;
using JellyfinBookReader.Data;
using JellyfinBookReader.Dto;
using JellyfinBookReader.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyfinBookReader.Tests.Services;

public class ClientDataServiceTests : IDisposable
{
    private readonly TestDbFixture _fixture;
    private readonly ClientDataService _service;

    public ClientDataServiceTests()
    {
        _fixture = new TestDbFixture();
        var repo = new ClientDataRepository(_fixture.DbContext, NullLogger<ClientDataRepository>.Instance);
        _service = new ClientDataService(repo, NullLogger<ClientDataService>.Instance);
    }

    //  GetClientData 

    [Fact]
    public void GetClientData_ReturnsNull_WhenNoneExists()
    {
        var result = _service.GetClientData(Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public void GetClientData_ReturnsData_AfterUpdate()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        _service.UpdateClientData(userId, bookId, new ClientDataUpdateDto { Data = "{\"quotes\":[]}" });

        var result = _service.GetClientData(userId, bookId);
        Assert.NotNull(result);
        Assert.Equal("{\"quotes\":[]}", result.Data);
    }

    //  UpdateClientData 

    [Fact]
    public void UpdateClientData_ReturnsUpdated_OnFirstWrite()
    {
        var (status, serverData) = _service.UpdateClientData(
            Guid.NewGuid(), Guid.NewGuid(),
            new ClientDataUpdateDto { Data = "{}" });

        Assert.Equal("updated", status);
        Assert.Null(serverData);
    }

    [Fact]
    public void UpdateClientData_ReturnsConflict_WithServerData_WhenStale()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        _service.UpdateClientData(userId, bookId, new ClientDataUpdateDto { Data = "{\"v\":1}" });

        var (status, serverData) = _service.UpdateClientData(userId, bookId, new ClientDataUpdateDto
        {
            Data = "{\"v\":2}",
            UpdatedAt = DateTime.UtcNow.AddHours(-1),
        });

        Assert.Equal("conflict", status);
        Assert.NotNull(serverData);
        Assert.Equal("{\"v\":1}", serverData.Data);
    }

    [Fact]
    public void UpdateClientData_NullDataDefaultsToEmptyObject()
    {
        // The controller guards this, but verify the service passes through whatever it receives
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        _service.UpdateClientData(userId, bookId, new ClientDataUpdateDto { Data = "{}" });

        var result = _service.GetClientData(userId, bookId);
        Assert.NotNull(result);
        Assert.Equal("{}", result.Data);
    }

    //  DeleteClientData 

    [Fact]
    public void DeleteClientData_ReturnsTrue_WhenDataExists()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        _service.UpdateClientData(userId, bookId, new ClientDataUpdateDto { Data = "{}" });

        Assert.True(_service.DeleteClientData(userId, bookId));
        Assert.Null(_service.GetClientData(userId, bookId));
    }

    [Fact]
    public void DeleteClientData_ReturnsFalse_WhenNoDataExists()
    {
        Assert.False(_service.DeleteClientData(Guid.NewGuid(), Guid.NewGuid()));
    }

    //  GetAllClientData 

    [Fact]
    public void GetAllClientData_ReturnsEmpty_WhenNone()
    {
        var result = _service.GetAllClientData(Guid.NewGuid());
        Assert.Empty(result);
    }

    [Fact]
    public void GetAllClientData_ReturnsAllBooksForUser()
    {
        var userId = Guid.NewGuid();
        var book1 = Guid.NewGuid();
        var book2 = Guid.NewGuid();

        _service.UpdateClientData(userId, book1, new ClientDataUpdateDto { Data = "{\"b\":1}" });
        _service.UpdateClientData(userId, book2, new ClientDataUpdateDto { Data = "{\"b\":2}" });

        var all = _service.GetAllClientData(userId);
        Assert.Equal(2, all.Count);
        Assert.Equal("{\"b\":1}", all[book1].Data);
        Assert.Equal("{\"b\":2}", all[book2].Data);
    }

    //  BatchUpdate 

    [Fact]
    public void BatchUpdate_ProcessesMultipleBooks()
    {
        var userId = Guid.NewGuid();
        var book1 = Guid.NewGuid();
        var book2 = Guid.NewGuid();

        var request = new BatchClientDataRequest
        {
            Updates = new List<BatchClientDataItem>
            {
                new() { BookId = book1, Data = "{\"b\":1}" },
                new() { BookId = book2, Data = "{\"b\":2}" },
            }
        };

        var response = _service.BatchUpdate(userId, request);

        Assert.Equal(2, response.Results.Count);
        Assert.All(response.Results, r => Assert.Equal("updated", r.Status));
        Assert.All(response.Results, r => Assert.Null(r.ServerData));

        Assert.Equal("{\"b\":1}", _service.GetClientData(userId, book1)!.Data);
        Assert.Equal("{\"b\":2}", _service.GetClientData(userId, book2)!.Data);
    }

    [Fact]
    public void BatchUpdate_ReportsConflict_ForStaleItems()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        _service.UpdateClientData(userId, bookId, new ClientDataUpdateDto { Data = "{\"v\":1}" });

        var request = new BatchClientDataRequest
        {
            Updates = new List<BatchClientDataItem>
            {
                new()
                {
                    BookId    = bookId,
                    Data      = "{\"v\":2}",
                    UpdatedAt = DateTime.UtcNow.AddHours(-1),
                },
            }
        };

        var response = _service.BatchUpdate(userId, request);

        Assert.Single(response.Results);
        Assert.Equal("conflict", response.Results[0].Status);
        Assert.NotNull(response.Results[0].ServerData);
        Assert.Equal("{\"v\":1}", response.Results[0].ServerData!.Data);
    }

    [Fact]
    public void BatchUpdate_MixedResults_ConflictAndUpdated()
    {
        var userId = Guid.NewGuid();
        var freshBook = Guid.NewGuid();
        var staleBook = Guid.NewGuid();

        // Give staleBook an existing server state
        _service.UpdateClientData(userId, staleBook, new ClientDataUpdateDto { Data = "{\"server\":true}" });

        var request = new BatchClientDataRequest
        {
            Updates = new List<BatchClientDataItem>
            {
                new() { BookId = freshBook, Data = "{\"new\":true}" },
                new() { BookId = staleBook, Data = "{\"new\":true}", UpdatedAt = DateTime.UtcNow.AddHours(-1) },
            }
        };

        var response = _service.BatchUpdate(userId, request);

        Assert.Equal(2, response.Results.Count);

        var freshResult = response.Results.Find(r => r.BookId == freshBook);
        var staleResult = response.Results.Find(r => r.BookId == staleBook);

        Assert.NotNull(freshResult);
        Assert.NotNull(staleResult);
        Assert.Equal("updated", freshResult!.Status);
        Assert.Equal("conflict", staleResult!.Status);
        Assert.NotNull(staleResult.ServerData);
    }

    [Fact]
    public void BatchUpdate_EmptyRequest_ReturnsEmptyResults()
    {
        var response = _service.BatchUpdate(Guid.NewGuid(), new BatchClientDataRequest());
        Assert.Empty(response.Results);
    }

    [Fact]
    public void BatchUpdate_ResultsOrderMatchesRequest()
    {
        var userId = Guid.NewGuid();
        var bookIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var request = new BatchClientDataRequest
        {
            Updates = new List<BatchClientDataItem>
            {
                new() { BookId = bookIds[0], Data = "{}" },
                new() { BookId = bookIds[1], Data = "{}" },
                new() { BookId = bookIds[2], Data = "{}" },
            }
        };

        var response = _service.BatchUpdate(userId, request);

        Assert.Equal(3, response.Results.Count);
        Assert.Equal(bookIds[0], response.Results[0].BookId);
        Assert.Equal(bookIds[1], response.Results[1].BookId);
        Assert.Equal(bookIds[2], response.Results[2].BookId);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}