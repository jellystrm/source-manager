using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SourceManager.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string? TmdbApiKey { get; set; }

    public string TmdbPosterSize { get; set; } = "w500";

    /// <summary>
    /// Override path for the Jellyfin Movies library root.
    /// Leave blank to auto-discover from Jellyfin virtual folders.
    /// Example: /media/movies
    /// </summary>
    public string? MovieLibraryPath { get; set; }

    /// <summary>
    /// Override path for the Jellyfin TV Shows library root.
    /// Leave blank to auto-discover from Jellyfin virtual folders.
    /// Example: /media/shows
    /// </summary>
    public string? ShowLibraryPath { get; set; }

    /// <summary>Kept for migration only — no longer used.</summary>
    [Obsolete("Use MovieLibraryPath / ShowLibraryPath instead.")]
    public string? StrmLibraryPath { get; set; }

    /// <summary>
    /// Enable automatic stream resolution for processing requests.
    /// When true, SourceResolutionService will try KKPhim → OPhim → YTS every 60 seconds.
    /// </summary>
    public bool EnableAutoResolution { get; set; } = true;

    /// <summary>qBittorrent Web API URL, e.g. http://localhost:8080</summary>
    public string? QBittorrentUrl { get; set; }

    /// <summary>qBittorrent username (default: admin)</summary>
    public string QBittorrentUsername { get; set; } = "admin";

    /// <summary>qBittorrent password</summary>
    public string? QBittorrentPassword { get; set; }

    /// <summary>
    /// API key for the Radarr-compatible endpoint (movies).
    /// In Jellyseerr: use http://&lt;host&gt;:8096/SourceManager/radarr as the Radarr URL.
    /// </summary>
    public string RadarrApiKey { get; set; } = GenerateKey();

    /// <summary>
    /// API key for the Sonarr-compatible endpoint (TV series).
    /// In Jellyseerr: use http://&lt;host&gt;:8096/SourceManager/sonarr as the Sonarr URL.
    /// </summary>
    public string SonarrApiKey { get; set; } = GenerateKey();

    private static string GenerateKey()
        => Guid.NewGuid().ToString("N");
}
