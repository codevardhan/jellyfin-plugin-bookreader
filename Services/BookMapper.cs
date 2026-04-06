using System;
using System.IO;
using System.Linq;
using JellyfinBookReader.Dto;
using MediaBrowser.Controller.Entities;

namespace JellyfinBookReader.Services;

public static class BookMapper
{
    public static BookDto ToDto(BaseItem item, ProgressDto? progress = null)
    {
        var filePath = item.Path ?? string.Empty;
        long fileSize = 0;
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            fileSize = new FileInfo(filePath).Length;
        }

        var format = Path.GetExtension(filePath)
            .TrimStart('.')
            .ToLowerInvariant();

        // Walk up the parent chain to find the library root (topmost ancestor).
        // The library root is the CollectionFolder whose own parent is null.
        Guid? libraryId = null;
        var ancestor = item.GetParent();
        while (ancestor != null)
        {
            var next = ancestor.GetParent();
            if (next == null)
            {
                libraryId = ancestor.Id;
                break;
            }
            ancestor = next;
        }

        return new BookDto
        {
            Id = item.Id,
            Title = item.Name ?? string.Empty,
            SortTitle = item.SortName ?? item.Name ?? string.Empty,
            Authors = BookService.GetAuthorNames(item),
            Genres = item.Genres?.ToList() ?? new(),
            Description = item.Overview,
            Publisher = item.Studios?.FirstOrDefault(),
            PublishedYear = item.ProductionYear,
            Format = format,
            FileSize = fileSize,
            CoverUrl = $"/api/BookReader/books/{item.Id}/cover",
            DateAdded = item.DateCreated,
            Progress = progress,
            LibraryId = libraryId,
        };
    }
}