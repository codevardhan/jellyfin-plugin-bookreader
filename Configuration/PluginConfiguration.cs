using MediaBrowser.Model.Plugins;

namespace JellyfinBookReader.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Minutes before an open reading session is considered stale and auto-closed.
    /// </summary>
    public int StaleSessionTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// Maximum number of books returned per page in list endpoints.
    /// </summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// Default number of books returned per page.
    /// </summary>
    public int DefaultPageSize { get; set; } = 20;
}