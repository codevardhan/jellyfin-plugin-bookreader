using System;
using System.Collections.Generic;
using JellyfinBookReader.Data;
using JellyfinBookReader.Dto;
using Microsoft.Extensions.Logging;

namespace JellyfinBookReader.Services;

public class ProgressService
{
    private readonly ProgressRepository _repo;
    private readonly ILogger<ProgressService> _logger;

    public ProgressService(ProgressRepository repo, ILogger<ProgressService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public ProgressDto? GetProgress(Guid userId, Guid bookId) =>
        _repo.Get(userId, bookId);

    public Dictionary<Guid, ProgressDto> GetAllProgress(Guid userId) =>
        _repo.GetAllForUser(userId);

    public (string Status, ProgressDto? ServerProgress) UpdateProgress(
        Guid userId, Guid bookId, ProgressUpdateDto update)
    {
        // Clamp percentage to valid range
        update.Percentage = Math.Clamp(update.Percentage, 0.0, 100.0);

        // Auto-set isFinished if percentage is 100
        if (update.Percentage >= 100.0)
        {
            update.IsFinished = true;
        }

        return _repo.Upsert(userId, bookId, update);
    }

    public bool ClearProgress(Guid userId, Guid bookId) =>
        _repo.Delete(userId, bookId);

    public BatchProgressResponse BatchUpdate(Guid userId, BatchProgressRequest request)
    {
        var response = new BatchProgressResponse();

        foreach (var item in request.Updates)
        {
            try
            {
                var update = new ProgressUpdateDto
                {
                    Percentage          = Math.Clamp(item.Percentage, 0.0, 100.0),
                    CurrentPage         = item.CurrentPage,
                    TotalPages          = item.TotalPages,
                    ChapterIndex        = item.ChapterIndex,
                    ChapterTitle        = item.ChapterTitle,
                    PageInChapter       = item.PageInChapter,
                    TotalPagesInChapter = item.TotalPagesInChapter,
                    Position            = item.Position,
                    IsFinished          = item.IsFinished || item.Percentage >= 100.0,
                    LastReadAt          = item.LastReadAt,
                };

                var (status, serverProgress) = _repo.Upsert(userId, item.BookId, update);

                response.Results.Add(new BatchProgressResult
                {
                    BookId = item.BookId,
                    Status = status,
                    ServerProgress = serverProgress,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update progress for book {BookId}", item.BookId);
                response.Results.Add(new BatchProgressResult
                {
                    BookId = item.BookId,
                    Status = "error",
                });
            }
        }

        return response;
    }
}