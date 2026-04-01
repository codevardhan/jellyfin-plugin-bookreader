using System;
using System.Collections.Generic;
using JellyfinBookReader.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace JellyfinBookReader;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths appPaths, IXmlSerializer xmlSerializer)
        : base(appPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Book Reader";

    public override string Description =>
        "Exposes a REST API for book reading apps — browse the library, download books, and sync reading progress.";

    public override Guid Id => new("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    public IEnumerable<PluginPageInfo> GetPages() => Array.Empty<PluginPageInfo>();
}