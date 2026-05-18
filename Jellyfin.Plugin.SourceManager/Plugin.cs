using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SourceManager.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SourceManager;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override Guid Id => Guid.Parse("b0e976c7-b9e6-4a30-93d9-0a7858c6dd96");

    public override string Name => "Source Manager";

    public override string Description => "Resolves Source Manager client requests and manages Jellyfin stream sources.";

    public override string ConfigurationFileName => "Jellyfin.Plugin.SourceManager.xml";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Pages.SourceManager.html"
        };
    }
}
