using System;
using Jellyfin.Plugin.Jellystrm.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Jellystrm;

public class Plugin : BasePlugin<PluginConfiguration>
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override Guid Id => Guid.Parse("b0e976c7-b9e6-4a30-93d9-0a7858c6dd96");

    public override string Name => "Jellystrm";

    public override string Description => "Resolves Jellystrm client requests and manages Jellyfin stream sources.";

    public override string ConfigurationFileName => "Jellyfin.Plugin.Jellystrm.xml";
}
