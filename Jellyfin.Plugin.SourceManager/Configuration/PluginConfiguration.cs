using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SourceManager.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string? TmdbApiKey { get; set; }

    public string TmdbPosterSize { get; set; } = "w500";

    /// <summary>
    /// Root directory where .strm files are written.
    /// Movies go to {StrmLibraryPath}/movies/, shows to {StrmLibraryPath}/shows/.
    /// Add those sub-paths as separate Jellyfin libraries (Movies / TV Shows).
    /// </summary>
    public string? StrmLibraryPath { get; set; }

    /// <summary>
    /// API key for the Radarr-compatible endpoint.
    /// In Jellyseerr: set this value as the Radarr API key,
    /// and use http://&lt;host&gt;:8096/SourceManager/radarr as the Radarr URL.
    /// </summary>
    public string RadarrApiKey { get; set; } = GenerateKey();

    private static string GenerateKey()
        => Guid.NewGuid().ToString("N");
}
