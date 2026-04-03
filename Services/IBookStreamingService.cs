using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace JellyfinBookReader.Services;

/// <summary>
/// Contract for format-specific book streaming.
/// Implementations extract individual pages from archive-based formats (CBZ, CBR, EPUB).
/// </summary>
public interface IBookStreamingService
{
    /// <summary>Returns true if this implementation handles the given file path.</summary>
    bool CanStream(string filePath);

    /// <summary>Returns the total number of streamable pages (or chapters) in the book.</summary>
    Task<int> GetPageCountAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Extracts a single page by zero-based index.
    /// Returns (null, null) when the index is out of range or extraction fails.
    /// Caller is responsible for disposing the returned stream.
    /// </summary>
    Task<(Stream? Stream, string? ContentType)> GetPageAsync(
        string filePath, int pageIndex, CancellationToken ct = default);
}
