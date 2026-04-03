using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Channels;
using System.Threading.Tasks;
using JellyfinBookReader.Dto;
using JellyfinBookReader.Services;
using JellyfinBookReader.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyfinBookReader.Api;

/// <summary>
/// REST API controller for the Book Reader plugin.
/// Feature 1: Book file serving (download + cover).
/// Feature 2: Library browsing (list, detail, authors, stats).
/// Feature 3: Reading progress sync.
/// Feature 4: Reading session tracking.
/// Feature 5: Page-level streaming with adaptive warm-up cache.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BookReaderController : ControllerBase
{
    private readonly BookService _bookService;
    private readonly CoverService _coverService;
    private readonly ProgressService _progressService;
    private readonly SessionService _sessionService;
    private readonly StreamingServiceFactory _streamingFactory;
    private readonly BookPageCache _pageCache;
    private readonly Channel<WarmUpRequest> _warmUpChannel;
    private readonly ILogger<BookReaderController> _logger;

    public BookReaderController(
        BookService bookService,
        CoverService coverService,
        ProgressService progressService,
        SessionService sessionService,
        StreamingServiceFactory streamingFactory,
        BookPageCache pageCache,
        Channel<WarmUpRequest> warmUpChannel,
        ILogger<BookReaderController> logger)
    {
        _bookService = bookService;
        _coverService = coverService;
        _progressService = progressService;
        _sessionService = sessionService;
        _streamingFactory = streamingFactory;
        _pageCache = pageCache;
        _warmUpChannel = warmUpChannel;
        _logger = logger;
    }

    //  Helpers 

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
                 ?? User.FindFirst("Jellyfin-UserId");

        if (claim != null && Guid.TryParse(claim.Value, out var userId))
            return userId;

        throw new UnauthorizedAccessException("Could not resolve user ID from auth token.");
    }

    private bool IsAdmin()
    {
        var adminClaim = User.FindFirst("Jellyfin-IsApiKey");
        if (adminClaim != null) return true;

        var roleClaim = User.FindFirst(System.Security.Claims.ClaimTypes.Role);
        if (roleClaim != null && roleClaim.Value.Contains("admin", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var claim in User.Claims)
        {
            if (claim.Type.Contains("Administrator", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(claim.Value, "true", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private Func<Guid, ProgressDto?> GetProgressLookup()
    {
        var userId = GetUserId();
        var progressMap = _progressService.GetAllProgress(userId);
        return bookId => progressMap.TryGetValue(bookId, out var p) ? p : null;
    }

    private void PublishWarmUp(Guid bookId, string filePath, int startPage, int pageCount)
    {
        // TryWrite — non-blocking. If the channel is full, DropOldest handles it.
        // Never await here; session/page responses must not block on cache warming.
        _warmUpChannel.Writer.TryWrite(
            new WarmUpRequest(bookId, filePath, startPage, pageCount));
    }

    //  Feature 1: File Serving 

    /// <summary>
    /// Download the full book file.
    /// GET /api/BookReader/books/{id}/file
    /// </summary>
    [HttpGet("books/{id}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetBookFile([FromRoute] Guid id)
    {
        var item = _bookService.GetBookItem(id);
        if (item == null)
            return NotFound(new { error = "Book not found." });

        var filePath = item.Path!;
        var mimeType = MimeTypeHelper.GetMimeType(filePath);
        var fileName = Path.GetFileName(filePath);

        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, mimeType, fileName, enableRangeProcessing: true);
    }

    /// <summary>
    /// Get the cover image for a book.
    /// GET /api/BookReader/books/{id}/cover
    /// </summary>
    [HttpGet("books/{id}/cover")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBookCover([FromRoute] Guid id)
    {
        var item = _bookService.GetBookItem(id);
        if (item == null)
            return NotFound(new { error = "Book not found." });

        var (stream, contentType) = await _coverService.GetCoverAsync(item).ConfigureAwait(false);

        if (stream == null || contentType == null)
            return NotFound(new { error = "No cover image available for this book." });

        Response.Headers["Cache-Control"] = "public, max-age=86400";
        return File(stream, contentType);
    }

    //  Feature 2: Library Browsing 

    /// <summary>
    /// List books with filtering, sorting, and pagination.
    /// GET /api/BookReader/books
    /// </summary>
    [HttpGet("books")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetBooks([FromQuery] BookQueryParams query)
    {
        var config = Plugin.Instance?.Configuration;
        var defaultPageSize = config?.DefaultPageSize ?? 20;
        var maxPageSize = config?.MaxPageSize ?? 100;

        var progressLookup = GetProgressLookup();
        var result = _bookService.QueryBooks(query, progressLookup, defaultPageSize, maxPageSize);
        return Ok(result);
    }

    /// <summary>
    /// Get a single book's details.
    /// GET /api/BookReader/books/{id}
    /// </summary>
    [HttpGet("books/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetBook([FromRoute] Guid id)
    {
        var item = _bookService.GetBookItem(id);
        if (item == null)
            return NotFound(new { error = "Book not found." });

        var progress = _progressService.GetProgress(GetUserId(), id);
        var dto = BookMapper.ToDto(item, progress);
        return Ok(dto);
    }

    /// <summary>
    /// List all authors with book counts.
    /// GET /api/BookReader/authors
    /// </summary>
    [HttpGet("authors")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetAuthors()
    {
        var authors = _bookService.GetAllAuthors();
        return Ok(new PagedResult<AuthorDto>
        {
            Items = authors,
            TotalCount = authors.Count,
            Limit = authors.Count,
            Offset = 0,
        });
    }

    /// <summary>
    /// Collection-level statistics.
    /// GET /api/BookReader/stats
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetStats()
    {
        var progressLookup = GetProgressLookup();
        var stats = _bookService.GetCollectionStats(progressLookup);
        return Ok(stats);
    }

    //  Feature 3: Reading Progress 

    /// <summary>
    /// Get reading progress for the authenticated user on a specific book.
    /// GET /api/BookReader/books/{id}/progress
    /// </summary>
    [HttpGet("books/{id}/progress")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetProgress([FromRoute] Guid id)
    {
        var progress = _progressService.GetProgress(GetUserId(), id);
        if (progress == null)
            return NotFound(new { error = "No reading progress found for this book." });

        return Ok(progress);
    }

    /// <summary>
    /// Create or update reading progress.
    /// PUT /api/BookReader/books/{id}/progress
    /// </summary>
    [HttpPut("books/{id}/progress")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult UpdateProgress([FromRoute] Guid id, [FromBody] ProgressUpdateDto update)
    {
        var item = _bookService.GetBookItem(id);
        if (item == null)
            return NotFound(new { error = "Book not found." });

        var (status, serverProgress) = _progressService.UpdateProgress(GetUserId(), id, update);

        if (status == "conflict")
            return Conflict(new
            {
                error = "Progress conflict: server has a newer update.",
                serverProgress,
            });

        return Ok(new { status = "updated" });
    }

    /// <summary>
    /// Clear reading progress (mark as unread).
    /// DELETE /api/BookReader/books/{id}/progress
    /// </summary>
    [HttpDelete("books/{id}/progress")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DeleteProgress([FromRoute] Guid id)
    {
        var deleted = _progressService.ClearProgress(GetUserId(), id);
        if (!deleted)
            return NotFound(new { error = "No reading progress found for this book." });

        return Ok(new { status = "deleted" });
    }

    /// <summary>
    /// Bulk progress sync for offline clients.
    /// PUT /api/BookReader/progress/batch
    /// </summary>
    [HttpPut("progress/batch")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult BatchUpdateProgress([FromBody] BatchProgressRequest request)
    {
        if (request.Updates == null || request.Updates.Count == 0)
            return BadRequest(new { error = "No updates provided." });

        if (request.Updates.Count > 100)
            return BadRequest(new { error = "Maximum 100 updates per batch." });

        var result = _progressService.BatchUpdate(GetUserId(), request);
        return Ok(result);
    }

    //  Feature 4: Reading Sessions 

    /// <summary>
    /// Begin a reading session and trigger background cache warm-up.
    /// Auto-closes any existing open session for the same book.
    /// POST /api/BookReader/sessions/start
    /// </summary>
    [HttpPost("sessions/start")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult StartSession([FromBody] StartSessionRequest request)
    {
        var item = _bookService.GetBookItem(request.BookId);
        if (item == null)
            return NotFound(new { error = "Book not found." });

        var result = _sessionService.StartSession(GetUserId(), request.BookId);

        // Fire warm-up for the first N pages — do not await, must not block response.
        if (_streamingFactory.IsStreamable(item.Path!))
        {
            var initialPages = Plugin.Instance?.Configuration?.WarmUpInitialPages ?? 10;
            PublishWarmUp(request.BookId, item.Path!, startPage: 0, pageCount: initialPages);
        }

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Keep-alive for an active reading session.
    /// POST /api/BookReader/sessions/heartbeat
    /// </summary>
    [HttpPost("sessions/heartbeat")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Heartbeat([FromBody] HeartbeatRequest request)
    {
        if (string.IsNullOrEmpty(request.SessionId))
            return BadRequest(new { error = "sessionId is required." });

        var ok = _sessionService.Heartbeat(request.SessionId);
        if (!ok)
            return NotFound(new { error = "Session not found or already closed." });

        return Ok(new { status = "ok" });
    }

    /// <summary>
    /// End a reading session and evict the book's page cache.
    /// POST /api/BookReader/sessions/end
    /// </summary>
    [HttpPost("sessions/end")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult EndSession([FromBody] EndSessionRequest request)
    {
        if (string.IsNullOrEmpty(request.SessionId))
            return BadRequest(new { error = "sessionId is required." });

        var ok = _sessionService.EndSession(request);
        if (!ok)
            return NotFound(new { error = "Session not found or already closed." });

        // Evict cached pages — frees memory for in-memory stores, deletes temp dir for disk stores.
        // Resolve the bookId from the closed session so we evict the right entry.
        var session = _sessionService.GetClosedSession(request.SessionId);
        if (session != null)
            _pageCache.Evict(session.BookId);

        return Ok(new { status = "ended" });
    }

    /// <summary>
    /// Reading statistics for the authenticated user.
    /// GET /api/BookReader/sessions/stats
    /// </summary>
    [HttpGet("sessions/stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetSessionStats([FromQuery] Guid? userId)
    {
        var currentUserId = GetUserId();

        if (userId.HasValue && userId.Value != currentUserId)
        {
            if (!IsAdmin())
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "Admin privileges required to view another user's stats." });
        }

        var targetUserId = userId ?? currentUserId;
        var stats = _sessionService.GetStats(targetUserId);
        return Ok(stats);
    }

    //  Feature 5: Page Streaming 

    /// <summary>
    /// Returns the total page count and format metadata for a streamable book.
    /// Clients should call this once before requesting individual pages.
    /// GET /api/BookReader/books/{id}/manifest
    /// </summary>
    [HttpGet("books/{id}/manifest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetManifest([FromRoute] Guid id)
    {
        var item = _bookService.GetBookItem(id);
        if (item == null)
            return NotFound(new { error = "Book not found." });

        var service = _streamingFactory.GetService(item.Path!);
        var format = Path.GetExtension(item.Path).TrimStart('.').ToLowerInvariant();

        if (service == null)
        {
            return Ok(new BookManifestDto
            {
                BookId = id,
                Format = format,
                IsStreamable = false,
                TotalPages = 0,
            });
        }

        var totalPages = await service.GetPageCountAsync(item.Path!).ConfigureAwait(false);

        return Ok(new BookManifestDto
        {
            BookId = id,
            Format = format,
            IsStreamable = true,
            TotalPages = totalPages,
        });
    }

    /// <summary>
    /// Streams a single page by zero-based index.
    /// Returns the extracted image (or EPUB chapter) directly.
    /// Serves from the warm-up cache when available; falls back to live extraction
    /// and triggers a prefetch window for subsequent pages.
    /// GET /api/BookReader/books/{id}/pages/{pageNumber}
    /// </summary>
    [HttpGet("books/{id}/pages/{pageNumber:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetPage([FromRoute] Guid id, [FromRoute] int pageNumber)
    {
        var item = _bookService.GetBookItem(id);
        if (item == null)
            return NotFound(new { error = "Book not found." });

        // 1. Cache hit — fast path, no archive I/O
        if (_pageCache.TryGet(id, pageNumber, out var cachedBytes, out var cachedContentType))
        {
            Response.Headers["X-Cache"] = "HIT";
            return File(cachedBytes!, cachedContentType!);
        }

        // 2. Format not streamable
        var service = _streamingFactory.GetService(item.Path!);
        if (service == null)
            return UnprocessableEntity(new { error = "This book format does not support page streaming." });

        // 3. Cache miss — extract live
        var (stream, contentType) = await service
            .GetPageAsync(item.Path!, pageNumber)
            .ConfigureAwait(false);

        if (stream == null || contentType == null)
            return NotFound(new { error = $"Page {pageNumber} not found." });

        byte[] bytes;
        await using (stream.ConfigureAwait(false))
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms).ConfigureAwait(false);
            bytes = ms.ToArray();
        }

        // Write to cache so subsequent requests for this page are served from memory/disk
        _pageCache.Set(id, item.Path!, pageNumber, bytes, contentType);

        // Trigger prefetch for the next window — fire and forget
        var prefetchWindow = Plugin.Instance?.Configuration?.WarmUpPrefetchWindow ?? 5;
        PublishWarmUp(id, item.Path!, startPage: pageNumber + 1, pageCount: prefetchWindow);

        Response.Headers["X-Cache"] = "MISS";
        return File(bytes, contentType);
    }
}
