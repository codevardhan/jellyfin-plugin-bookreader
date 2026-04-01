using System;
using System.IO;
using System.Security.Claims;
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
    private readonly ILogger<BookReaderController> _logger;

    public BookReaderController(
        BookService bookService,
        CoverService coverService,
        ProgressService progressService,
        SessionService sessionService,
        ILogger<BookReaderController> logger)
    {
        _bookService = bookService;
        _coverService = coverService;
        _progressService = progressService;
        _sessionService = sessionService;
        _logger = logger;
    }

    //  Helpers 

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
                 ?? User.FindFirst("Jellyfin-UserId");

        if (claim != null && Guid.TryParse(claim.Value, out var userId))
        {
            return userId;
        }

        throw new UnauthorizedAccessException("Could not resolve user ID from auth token.");
    }

    private bool IsAdmin()
    {
        // Check claims for admin status — works across Jellyfin versions
        var adminClaim = User.FindFirst("Jellyfin-IsApiKey");
        if (adminClaim != null) return true; // API keys have admin access

        var roleClaim = User.FindFirst(System.Security.Claims.ClaimTypes.Role);
        if (roleClaim != null && roleClaim.Value.Contains("admin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check if the Jellyfin-IsAdministrator claim exists
        foreach (var claim in User.Claims)
        {
            if (claim.Type.Contains("Administrator", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(claim.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private Func<Guid, ProgressDto?> GetProgressLookup()
    {
        var userId = GetUserId();
        var progressMap = _progressService.GetAllProgress(userId);
        return bookId => progressMap.TryGetValue(bookId, out var p) ? p : null;
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
        {
            return NotFound(new { error = "Book not found." });
        }

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
        {
            return NotFound(new { error = "Book not found." });
        }

        var (stream, contentType) = await _coverService.GetCoverAsync(item).ConfigureAwait(false);

        if (stream == null || contentType == null)
        {
            return NotFound(new { error = "No cover image available for this book." });
        }

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
        {
            return NotFound(new { error = "Book not found." });
        }

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
        {
            return NotFound(new { error = "No reading progress found for this book." });
        }

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
        {
            return NotFound(new { error = "Book not found." });
        }

        var (status, serverProgress) = _progressService.UpdateProgress(GetUserId(), id, update);

        if (status == "conflict")
        {
            return Conflict(new
            {
                error = "Progress conflict: server has a newer update.",
                serverProgress,
            });
        }

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
        {
            return NotFound(new { error = "No reading progress found for this book." });
        }

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
        {
            return BadRequest(new { error = "No updates provided." });
        }

        if (request.Updates.Count > 100)
        {
            return BadRequest(new { error = "Maximum 100 updates per batch." });
        }

        var result = _progressService.BatchUpdate(GetUserId(), request);
        return Ok(result);
    }

    //  Feature 4: Reading Sessions 

    /// <summary>
    /// Begin a reading session. Auto-closes any existing open session for the same book.
    /// POST /api/BookReader/sessions/start
    /// </summary>
    [HttpPost("sessions/start")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult StartSession([FromBody] StartSessionRequest request)
    {
        // Verify the book exists
        var item = _bookService.GetBookItem(request.BookId);
        if (item == null)
        {
            return NotFound(new { error = "Book not found." });
        }

        var result = _sessionService.StartSession(GetUserId(), request.BookId);
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
        {
            return BadRequest(new { error = "sessionId is required." });
        }

        var ok = _sessionService.Heartbeat(request.SessionId);
        if (!ok)
        {
            return NotFound(new { error = "Session not found or already closed." });
        }

        return Ok(new { status = "ok" });
    }

    /// <summary>
    /// End a reading session.
    /// POST /api/BookReader/sessions/end
    /// </summary>
    [HttpPost("sessions/end")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult EndSession([FromBody] EndSessionRequest request)
    {
        if (string.IsNullOrEmpty(request.SessionId))
        {
            return BadRequest(new { error = "sessionId is required." });
        }

        var ok = _sessionService.EndSession(request);
        if (!ok)
        {
            return NotFound(new { error = "Session not found or already closed." });
        }

        return Ok(new { status = "ended" });
    }

    /// <summary>
    /// Reading statistics for the authenticated user.
    /// Pass ?userId= for a different user (admin only).
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
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "Admin privileges required to view another user's stats." });
            }
        }

        var targetUserId = userId ?? currentUserId;
        var stats = _sessionService.GetStats(targetUserId);
        return Ok(stats);
    }
}