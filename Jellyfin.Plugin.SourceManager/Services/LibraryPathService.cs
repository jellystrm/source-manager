using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SourceManager.Services;

/// <summary>
/// Resolves the physical paths of Jellyfin movie/show libraries.
/// Priority: explicit override in plugin config → auto-discovered from Jellyfin virtual folders.
/// </summary>
public sealed class LibraryPathService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LibraryPathService> _logger;

    public LibraryPathService(ILibraryManager libraryManager, ILogger<LibraryPathService> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>Physical root of the Jellyfin Movies library.</summary>
    public string? GetMoviePath()
    {
        var configured = Plugin.Instance?.Configuration.MovieLibraryPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Discover("movies");
    }

    /// <summary>Physical root of the Jellyfin TV Shows library.</summary>
    public string? GetShowPath()
    {
        var configured = Plugin.Instance?.Configuration.ShowLibraryPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Discover("tvshows");
    }

    private string? Discover(string collectionType)
    {
        try
        {
            var folders = _libraryManager.GetVirtualFolders();
            var match = folders.FirstOrDefault(f =>
                string.Equals(
                    f.CollectionType?.ToString(),
                    collectionType,
                    StringComparison.OrdinalIgnoreCase));

            var path = match?.Locations?.FirstOrDefault();

            if (path is not null)
            {
                _logger.LogDebug(
                    "LibraryPath: discovered {Type} library at {Path}",
                    collectionType, path);
            }
            else
            {
                _logger.LogWarning(
                    "LibraryPath: no Jellyfin {Type} library found — set {Config} in plugin config",
                    collectionType,
                    collectionType == "movies" ? "MovieLibraryPath" : "ShowLibraryPath");
            }

            return path;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LibraryPath: failed to query virtual folders");
            return null;
        }
    }
}
