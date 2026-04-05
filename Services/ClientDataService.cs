using System;
using System.Collections.Generic;
using JellyfinBookReader.Data;
using JellyfinBookReader.Dto;
using Microsoft.Extensions.Logging;

namespace JellyfinBookReader.Services;

public class ClientDataService
{
    private readonly ClientDataRepository _repo;
    private readonly ILogger<ClientDataService> _logger;

    public ClientDataService(ClientDataRepository repo, ILogger<ClientDataService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public ClientDataDto? GetClientData(Guid userId, Guid bookId) =>
        _repo.Get(userId, bookId);

    public Dictionary<Guid, ClientDataDto> GetAllClientData(Guid userId) =>
        _repo.GetAllForUser(userId);

    /// <summary>
    /// Upsert the client data blob for a single book.
    /// Returns "updated" or "conflict"; on conflict, serverData is populated.
    /// </summary>
    public (string Status, ClientDataDto? ServerData) UpdateClientData(
        Guid userId,
        Guid bookId,
        ClientDataUpdateDto update)
    {
        return _repo.Upsert(userId, bookId, update);
    }

    public bool DeleteClientData(Guid userId, Guid bookId) =>
        _repo.Delete(userId, bookId);

    /// <summary>
    /// Batch upsert for offline sync catch-up. Mirrors BatchUpdate in ProgressService.
    /// Maximum 100 items per call — validated at the controller layer.
    /// </summary>
    public BatchClientDataResponse BatchUpdate(Guid userId, BatchClientDataRequest request)
    {
        var response = new BatchClientDataResponse();

        foreach (var item in request.Updates)
        {
            try
            {
                var update = new ClientDataUpdateDto
                {
                    Data = item.Data,
                    UpdatedAt = item.UpdatedAt,
                };

                var (status, serverData) = _repo.Upsert(userId, item.BookId, update);

                response.Results.Add(new BatchClientDataResult
                {
                    BookId = item.BookId,
                    Status = status,
                    ServerData = serverData,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update client data for book {BookId}", item.BookId);
                response.Results.Add(new BatchClientDataResult
                {
                    BookId = item.BookId,
                    Status = "error",
                });
            }
        }

        return response;
    }
}