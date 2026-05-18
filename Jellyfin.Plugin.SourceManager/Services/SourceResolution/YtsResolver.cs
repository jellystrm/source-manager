using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.SourceManager.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SourceManager.Services.SourceResolution;

/// <summary>
/// Finds a torrent for movies via the YTS public API.
/// Returns a magnet link as SourceResult(Torrent, magnetUrl).
/// Series are not supported by YTS — returns null for non-movie requests.
/// </summary>
public sealed class YtsResolver : ISourceResolver
{
    private static readonly string[] Trackers =
    [
        "udp://open.demonii.com:1337/announce",
        "udp://tracker.openbittorrent.com:80",
        "udp://tracker.opentrackr.org:1337/announce",
        "udp://p4p.arenabg.com:1337",
        "udp://tracker.leechers-paradise.org:6969"
    ];

    private readonly IHttpClientFactory _factory;
    private readonly ILogger<YtsResolver> _logger;

    public string Name => "YTS";

    public YtsResolver(IHttpClientFactory factory, ILogger<YtsResolver> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<SourceResult?> ResolveAsync(MediaRequestRecord request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.MediaType, RequestMediaType.Movie, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        using var http = _factory.CreateClient();
        http.BaseAddress = new Uri("https://yts.mx");
        http.Timeout = TimeSpan.FromSeconds(15);

        YtsResponse? response;
        try
        {
            response = await http
                .GetFromJsonAsync<YtsResponse>(
                    $"/api/v2/list_movies.json?query_term={Uri.EscapeDataString(request.Title)}&limit=5",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "YTS: search failed for '{Title}'", request.Title);
            return null;
        }

        var movies = response?.Data?.Movies;
        if (movies is null || movies.Count == 0)
        {
            return null;
        }

        // Pick best quality torrent across all results, prefer 1080p then 720p.
        var best = movies
            .SelectMany(m => m.Torrents ?? [])
            .OrderByDescending(t => t.Quality switch
            {
                "2160p" => 4,
                "1080p" => 3,
                "720p"  => 2,
                "480p"  => 1,
                _       => 0
            })
            .ThenByDescending(t => t.Seeds)
            .FirstOrDefault();

        if (best?.Hash is null)
        {
            return null;
        }

        var magnet = BuildMagnet(best.Hash, movies[0].Title);
        _logger.LogInformation(
            "YTS: found torrent for '{Title}' quality={Quality} seeds={Seeds}",
            request.Title, best.Quality, best.Seeds);

        return new SourceResult(SourceKind.Torrent, magnet);
    }

    private static string BuildMagnet(string hash, string title)
    {
        var tr = string.Join("&", Trackers.Select(t => $"tr={Uri.EscapeDataString(t)}"));
        return $"magnet:?xt=urn:btih:{hash}&dn={Uri.EscapeDataString(title)}&{tr}";
    }

    // -----------------------------------------------------------------------
    // JSON types
    // -----------------------------------------------------------------------

    private sealed record YtsResponse(
        [property: JsonPropertyName("data")] YtsData? Data);

    private sealed record YtsData(
        [property: JsonPropertyName("movies")] IReadOnlyList<YtsMovie>? Movies);

    private sealed record YtsMovie(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("year")] int Year,
        [property: JsonPropertyName("torrents")] IReadOnlyList<YtsTorrent>? Torrents);

    private sealed record YtsTorrent(
        [property: JsonPropertyName("hash")] string Hash,
        [property: JsonPropertyName("quality")] string Quality,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("seeds")] int Seeds);
}
