using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using JellyfinBookReader.Dto;
using JellyfinBookReader.Utils;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace JellyfinBookReader.Services;

public class BookService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<BookService> _logger;

    public BookService(ILibraryManager libraryManager, ILogger<BookService> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Resolve a Jellyfin item by ID, returning it only if it's a valid book with a file on disk.
    /// </summary>
    public BaseItem? GetBookItem(Guid itemId)
    {
        // Primary lookup — fast in-memory path.
        var item = _libraryManager.GetItemById(itemId);

        if (item == null)
        {
            // Fallback: GetItemById has version-specific cache/lazy-load edge
            // cases in Jellyfin 10.8–10.10 where it returns null even for items
            // that exist in the library.  GetItemsResult goes through the same
            // query path that successfully powers GetAllBooks, so it is reliable.
            _logger.LogDebug(
                "GetItemById returned null for {Id} — trying query fallback", itemId);

            item = _libraryManager.GetItemsResult(new InternalItemsQuery
            {
                ItemIds = new[] { itemId },
                IsVirtualItem = false,
                Recursive = true,
            }).Items.FirstOrDefault();
        }

        if (item == null)
        {
            _logger.LogDebug("Item not found: {Id}", itemId);
            return null;
        }

        if (item.MediaType != MediaType.Book && !IsBookByPath(item))
        {
            _logger.LogDebug("Item {Id} is not a book (type: {Type})", itemId, item.GetType().Name);
            return null;
        }

        if (string.IsNullOrEmpty(item.Path) || !File.Exists(item.Path))
        {
            _logger.LogWarning("Book file not found on disk: {Path}", item.Path);
            return null;
        }

        if (!MimeTypeHelper.IsSupportedBookFormat(item.Path))
        {
            _logger.LogDebug("Unsupported book format: {Path}", item.Path);
            return null;
        }

        return item;
    }

    /// <summary>
    /// Query books with filtering, sorting, and pagination.
    /// Returns the paged result and the full list of matching items (for stats).
    /// </summary>
    public PagedResult<BookDto> QueryBooks(
        BookQueryParams query,
        Func<Guid, ProgressDto?> progressLookup,
        int defaultPageSize,
        int maxPageSize)
    {
        var allBooks = GetAllBooks();
        IEnumerable<BaseItem> filtered = allBooks;

        //  Filters 

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            filtered = filtered.Where(b =>
                (b.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                GetAuthorNames(b).Any(a => a.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            var author = query.Author.Trim();
            filtered = filtered.Where(b =>
                GetAuthorNames(b).Any(a => a.Equals(author, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(query.Genre))
        {
            var genre = query.Genre.Trim();
            filtered = filtered.Where(b =>
                b.Genres?.Any(g => g.Equals(genre, StringComparison.OrdinalIgnoreCase)) ?? false);
        }

        if (!string.IsNullOrWhiteSpace(query.Format))
        {
            var fmt = query.Format.Trim().TrimStart('.');
            filtered = filtered.Where(b =>
                Path.GetExtension(b.Path ?? "").TrimStart('.')
                    .Equals(fmt, StringComparison.OrdinalIgnoreCase));
        }

        if (query.LibraryId.HasValue)
        {
            var libId = query.LibraryId.Value;
            filtered = filtered.Where(b =>
            {
                var parent = b.GetParent();
                while (parent != null)
                {
                    if (parent.Id == libId) return true;
                    parent = parent.GetParent();
                }
                return false;
            });
        }

        // Status filter requires progress lookup
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            filtered = query.Status.ToLowerInvariant() switch
            {
                "reading" => filtered.Where(b =>
                {
                    var p = progressLookup(b.Id);
                    return p != null && !p.IsFinished;
                }),
                "finished" => filtered.Where(b =>
                {
                    var p = progressLookup(b.Id);
                    return p?.IsFinished == true;
                }),
                "unread" => filtered.Where(b => progressLookup(b.Id) == null),
                _ => filtered
            };
        }

        // Materialize before sorting (we need progress for some sort modes)
        var materialized = filtered.ToList();
        var totalCount = materialized.Count;

        //  Sort 

        var desc = query.SortOrder.Equals("desc", StringComparison.OrdinalIgnoreCase);

        materialized = (query.Sort?.ToLowerInvariant() switch
        {
            "author" => desc
                ? materialized.OrderByDescending(b => GetAuthorNames(b).FirstOrDefault() ?? "")
                : materialized.OrderBy(b => GetAuthorNames(b).FirstOrDefault() ?? ""),
            "dateadded" => desc
                ? materialized.OrderByDescending(b => b.DateCreated)
                : materialized.OrderBy(b => b.DateCreated),
            "lastread" => desc
                ? materialized.OrderByDescending(b => progressLookup(b.Id)?.LastReadAt ?? DateTime.MinValue)
                : materialized.OrderBy(b => progressLookup(b.Id)?.LastReadAt ?? DateTime.MinValue),
            "progress" => desc
                ? materialized.OrderByDescending(b => progressLookup(b.Id)?.Percentage ?? -1)
                : materialized.OrderBy(b => progressLookup(b.Id) == null ? double.MaxValue
                             : progressLookup(b.Id)!.Percentage),
            _ => desc // default: title
                ? materialized.OrderByDescending(b => b.SortName ?? b.Name ?? "")
                : materialized.OrderBy(b => b.SortName ?? b.Name ?? ""),
        }).ToList();

        //  Paginate 

        var limit = Math.Clamp(query.Limit ?? defaultPageSize, 1, maxPageSize);
        var offset = Math.Max(query.Offset, 0);

        var page = materialized
            .Skip(offset)
            .Take(limit)
            .Select(b => BookMapper.ToDto(b, progressLookup(b.Id)))
            .ToList();

        return new PagedResult<BookDto>
        {
            Items = page,
            TotalCount = totalCount,
            Limit = limit,
            Offset = offset,
        };
    }

    /// <summary>
    /// Get all book items in the library.
    /// </summary>
    public BaseItem[] GetAllBooks()
    {
        // Jellyfin 10.11 indexes books under different BaseItemKind values depending on
        // how the file was scanned. Try each type that could contain book files.
        var typesToTry = new[]
        {
            new[] { BaseItemKind.Book },
            new[] { BaseItemKind.Video },
        };

        foreach (var types in typesToTry)
        {
            try
            {
                var query = new InternalItemsQuery
                {
                    IncludeItemTypes = types,
                    IsVirtualItem = false,
                    Recursive = true,
                };

                var results = _libraryManager.GetItemsResult(query).Items
                    .Where(i => !string.IsNullOrEmpty(i.Path)
                             && File.Exists(i.Path)
                             && MimeTypeHelper.IsSupportedBookFormat(i.Path))
                    .ToArray();

                if (results.Length > 0)
                    return results;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("deserialize"))
            {
                _logger.LogWarning(
                    "Jellyfin DB has items that cannot be deserialized (type={Types}). " +
                    "Run a library rescan from the Jellyfin dashboard to fix. Error: {Message}",
                    string.Join(",", types.Select(t => t.ToString())), ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query books with types {Types}",
                    string.Join(",", types.Select(t => t.ToString())));
            }
        }

        return Array.Empty<BaseItem>();
    }

    /// <summary>
    /// Get all unique authors with book counts.
    /// </summary>
    public List<AuthorDto> GetAllAuthors()
    {
        return GetAllBooks()
            .SelectMany(b => GetAuthorNames(b))
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AuthorDto { Name = g.Key, BookCount = g.Count() })
            .OrderBy(a => a.Name)
            .ToList();
    }

    /// <summary>
    /// Get collection-level statistics.
    /// </summary>
    public CollectionStatsDto GetCollectionStats(Func<Guid, ProgressDto?> progressLookup)
    {
        var books = GetAllBooks();
        var authors = books
            .SelectMany(b => GetAuthorNames(b))
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var formatBreakdown = books
            .GroupBy(b => Path.GetExtension(b.Path ?? "").TrimStart('.').ToLowerInvariant())
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToDictionary(g => g.Key, g => g.Count());

        var totalSize = books
            .Sum(b => !string.IsNullOrEmpty(b.Path) && File.Exists(b.Path)
                ? new FileInfo(b.Path).Length
                : 0L);

        var recentlyAdded = books
            .OrderByDescending(b => b.DateCreated)
            .Take(5)
            .Select(b => BookMapper.ToDto(b, progressLookup(b.Id)))
            .ToList();

        return new CollectionStatsDto
        {
            TotalBooks = books.Length,
            TotalAuthors = authors,
            FormatBreakdown = formatBreakdown,
            TotalFileSize = totalSize,
            RecentlyAdded = recentlyAdded,
        };
    }

    /// <summary>
    /// Extract author names from a Jellyfin item.
    /// Uses parent folder name (Jellyfin's recommended structure), then Studios as fallback.
    /// People API is intentionally avoided due to signature changes across Jellyfin versions.
    /// </summary>
    public static List<string> GetAuthorNames(BaseItem item)
    {
        // 1. Jellyfin's recommended book folder structure is Author/BookTitle/file.epub
        //    so the parent folder name is often the author
        var parent = item.GetParent();
        if (parent != null && !string.IsNullOrWhiteSpace(parent.Name))
        {
            // Only use parent name if it doesn't look like a library root
            var grandParent = parent.GetParent();
            if (grandParent != null)
            {
                return new List<string> { parent.Name };
            }
        }

        // 2. Fallback: some providers put author in Studios
        if (item.Studios?.Length > 0)
        {
            return item.Studios.ToList();
        }

        return new List<string>();
    }

    private static bool IsBookByPath(BaseItem item) =>
        !string.IsNullOrEmpty(item.Path) && MimeTypeHelper.IsSupportedBookFormat(item.Path);
}