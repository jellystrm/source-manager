using System.Globalization;
using System.Text;
using Jellyfin.Plugin.SourceManager.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SourceManager.Services;

public sealed class StrmWriterService
{
    private readonly LibraryPathService _libraryPaths;
    private readonly ILogger<StrmWriterService> _logger;

    public StrmWriterService(LibraryPathService libraryPaths, ILogger<StrmWriterService> logger)
    {
        _libraryPaths = libraryPaths;
        _logger = logger;
    }

    /// <summary>
    /// Writes a .strm file into the appropriate Jellyfin library folder.
    /// Path is discovered from the live Jellyfin virtual folder list;
    /// falls back to the explicit override paths in plugin config.
    ///
    /// Folder conventions (Jellyfin-compatible):
    ///   Movies  → {moviePath}/{Title}/{Title}.strm
    ///   Series  → {showPath}/{Title}/{Title}.strm
    ///   Episode → {showPath}/{Title}/Season {NN}/{Title} - S{NN}E{NN}.strm
    /// </summary>
    public string? WriteStrmFile(MediaRequestRecord request, string streamUrl)
    {
        var libraryPath = GetLibraryPath(request.MediaType);
        if (libraryPath is null)
        {
            _logger.LogWarning(
                "No library path available for {MediaType} — skipping .strm for request {RequestId}",
                request.MediaType, request.RequestId);
            return null;
        }

        try
        {
            var filePath = BuildFilePath(libraryPath, request);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, streamUrl, Encoding.UTF8);

            _logger.LogInformation(
                "Created .strm at {Path} for request {RequestId}",
                filePath, request.RequestId);

            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write .strm for request {RequestId}", request.RequestId);
            return null;
        }
    }

    /// <summary>Deletes the .strm file previously written for this request.</summary>
    public void DeleteStrmFile(MediaRequestRecord request)
    {
        var libraryPath = GetLibraryPath(request.MediaType);
        if (libraryPath is null) return;

        try
        {
            var filePath = BuildFilePath(libraryPath, request);
            if (!File.Exists(filePath)) return;

            File.Delete(filePath);

            // Remove the parent folder if it is now empty (per-movie folders).
            var dir = Path.GetDirectoryName(filePath)!;
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }

            _logger.LogInformation(
                "Deleted .strm at {Path} for request {RequestId}",
                filePath, request.RequestId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete .strm for request {RequestId}", request.RequestId);
        }
    }

    // -----------------------------------------------------------------------

    private string? GetLibraryPath(string mediaType) =>
        string.Equals(mediaType, RequestMediaType.Movie, StringComparison.OrdinalIgnoreCase)
            ? _libraryPaths.GetMoviePath()
            : _libraryPaths.GetShowPath();

    private static string BuildFilePath(string libraryPath, MediaRequestRecord request)
    {
        var safeName = SanitizeFilename(request.Title);

        return request.MediaType switch
        {
            // Movies/{Title}/{Title}.strm
            RequestMediaType.Movie =>
                Path.Combine(libraryPath, safeName, $"{safeName}.strm"),

            // Shows/{Title}/{Title}.strm  (series-level placeholder)
            RequestMediaType.Series =>
                Path.Combine(libraryPath, safeName, $"{safeName}.strm"),

            // Shows/{Title}/Season NN/{Title} - SNNENN.strm
            RequestMediaType.Episode =>
                Path.Combine(
                    libraryPath,
                    safeName,
                    string.Create(CultureInfo.InvariantCulture, $"Season {request.SeasonNumber!.Value:00}"),
                    string.Create(CultureInfo.InvariantCulture,
                        $"{safeName} - S{request.SeasonNumber!.Value:00}E{request.EpisodeNumber!.Value:00}.strm")),

            _ => Path.Combine(libraryPath, $"{request.RequestId}.strm")
        };
    }

    private static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray()).Trim();
    }
}
