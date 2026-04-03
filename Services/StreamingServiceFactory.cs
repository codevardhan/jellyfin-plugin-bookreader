using System.Collections.Generic;
using System.Linq;

namespace JellyfinBookReader.Services;

/// <summary>
/// Resolves the correct <see cref="IBookStreamingService"/> for a given file path
/// by delegating to each registered implementation's <c>CanStream</c> check.
///
/// Registration order in <c>PluginServiceRegistrator</c> determines priority
/// when two implementations claim the same extension (shouldn't happen in practice).
/// </summary>
public class StreamingServiceFactory
{
    private readonly IReadOnlyList<IBookStreamingService> _services;

    public StreamingServiceFactory(IEnumerable<IBookStreamingService> services)
    {
        _services = services.ToList();
    }

    /// <summary>Returns the first service that can handle the file, or null.</summary>
    public IBookStreamingService? GetService(string filePath) =>
        _services.FirstOrDefault(s => s.CanStream(filePath));

    /// <summary>Returns true if any registered service can stream the file.</summary>
    public bool IsStreamable(string filePath) =>
        _services.Any(s => s.CanStream(filePath));
}
