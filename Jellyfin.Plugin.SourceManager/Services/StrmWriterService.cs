using System.Globalization;
using System.Text;
using Jellyfin.Plugin.SourceManager.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SourceManager.Services;

public sealed class StrmWriterService
{
    private readonly ILogger<StrmWriterService> _logger;

    public StrmWriterService(ILogger<StrmWriterService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Writes a .strm file for the given request under the configured library path.
    /// Returns the full path of the created file, or null if StrmLibraryPath is not configured.
    /// </summary>
    /// <remarks>
    /// Layout written:
    ///   movies/  → add as Jellyfin "Movies" library
    ///   shows/   → add as Jellyfin "TV Shows" library
    ///
    /// Episode files use the per-season sub-folder pattern that Jellyfin expects.
    /// </remarks>
    public string? WriteStrmFile(MediaRequestRecord request, string streamUrl)
    {
        var basePath = Plugin.Instance?.Configuration.StrmLibraryPath;
        if (string.IsNullOrWhiteSpace(basePath))
        {
            _logger.LogWarning(
                "StrmLibraryPath is not configured — skipping .strm creation for request {RequestId}",
                request.RequestId);
            return null;
        }

        try
        {
            var filePath = BuildFilePath(basePath, request);
            var directory = Path.GetDirectoryName(filePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(filePath, streamUrl, Encoding.UTF8);
            _logger.LogInformation(
                "Created .strm file at {Path} for request {RequestId}",
                filePath,
                request.RequestId);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write .strm file for request {RequestId}", request.RequestId);
            return null;
        }
    }

    /// <summary>
    /// Deletes the .strm file previously created for this request, if it still exists.
    /// </summary>
    public void DeleteStrmFile(MediaRequestRecord request)
    {
        var basePath = Plugin.Instance?.Configuration.StrmLibraryPath;
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return;
        }

        try
        {
            var filePath = BuildFilePath(basePath, request);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation(
                    "Deleted .strm file at {Path} for request {RequestId}",
                    filePath,
                    request.RequestId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete .strm file for request {RequestId}", request.RequestId);
        }
    }

    private static string BuildFilePath(string basePath, MediaRequestRecord request)
    {
        var safeName = SanitizeFilename(request.Title);

        return request.MediaType switch
        {
            RequestMediaType.Movie =>
                Path.Combine(basePath, "movies", $"{safeName}.strm"),

            RequestMediaType.Series =>
                Path.Combine(basePath, "shows", $"{safeName}.strm"),

            RequestMediaType.Episode =>
                Path.Combine(
                    basePath,
                    "shows",
                    string.Create(CultureInfo.InvariantCulture, $"Season {request.SeasonNumber!.Value:00}"),
                    $"{safeName}.strm"),

            _ => Path.Combine(basePath, $"{request.RequestId}.strm")
        };
    }

    private static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray()).Trim();
    }
}
