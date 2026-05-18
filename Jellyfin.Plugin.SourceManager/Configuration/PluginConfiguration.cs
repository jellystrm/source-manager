using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SourceManager.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string? TmdbApiKey { get; set; }

    public string TmdbPosterSize { get; set; } = "w500";
}
