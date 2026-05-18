using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.SourceManager.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SourceManager.Services.SourceResolution;

/// <summary>
/// Base resolver for phimapi.com-compatible APIs (KKPhim, OPhim).
/// Searches by title, verifies via TMDB ID, returns the first m3u8 stream URL found.
/// Series support is limited to writing one .strm per episode server entry.
/// </summary>
public abstract class PhimApiResolverBase : ISourceResolver
{
    private readonly IHttpClientFactory _factory;
    private readonly ILogger _logger;

    protected abstract string BaseUrl { get; }
    public abstract string Name { get; }

    protected PhimApiResolverBase(IHttpClientFactory factory, ILogger logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<SourceResult?> ResolveAsync(MediaRequestRecord request, CancellationToken cancellationToken)
    {
        using var http = _factory.CreateClient();
        http.BaseAddress = new Uri(BaseUrl);
        http.Timeout = TimeSpan.FromSeconds(20);

        SearchResponse? search;
        try
        {
            search = await http
                .GetFromJsonAsync<SearchResponse>(
                    $"/v1/api/tim-kiem?keyword={Uri.EscapeDataString(request.Title)}&limit=10",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "{Name}: search failed for '{Title}'", Name, request.Title);
            return null;
        }

        var items = search?.Data?.Items;
        if (items is null || items.Count == 0)
        {
            return null;
        }

        foreach (var item in items)
        {
            DetailResponse? detail;
            try
            {
                detail = await http
                    .GetFromJsonAsync<DetailResponse>($"/phim/{item.Slug}", cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                continue;
            }

            if (detail is null) continue;

            // Verify TMDB ID match for movies; for series we match by title proximity.
            if (string.Equals(request.MediaType, RequestMediaType.Movie, StringComparison.OrdinalIgnoreCase))
            {
                var remoteTmdbId = detail.Movie?.Tmdb?.Id;
                if (!string.Equals(remoteTmdbId, request.TmdbId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            var m3u8 = detail.Episodes?
                .SelectMany(s => s.ServerData ?? [])
                .Select(d => d.LinkM3u8)
                .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));

            if (!string.IsNullOrWhiteSpace(m3u8))
            {
                _logger.LogInformation(
                    "{Name}: found stream for '{Title}' (tmdb:{TmdbId})",
                    Name, request.Title, request.TmdbId);
                return new SourceResult(SourceKind.StreamUrl, m3u8);
            }
        }

        return null;
    }

    // -----------------------------------------------------------------------
    // JSON types (private — not part of public API)
    // -----------------------------------------------------------------------

    private sealed record SearchResponse(
        [property: JsonPropertyName("data")] SearchData? Data);

    private sealed record SearchData(
        [property: JsonPropertyName("items")] IReadOnlyList<SearchItem>? Items);

    private sealed record SearchItem(
        [property: JsonPropertyName("slug")] string Slug,
        [property: JsonPropertyName("name")] string Name);

    private sealed record DetailResponse(
        [property: JsonPropertyName("movie")] MovieDetail? Movie,
        [property: JsonPropertyName("episodes")] IReadOnlyList<EpisodeServer>? Episodes);

    private sealed record MovieDetail(
        [property: JsonPropertyName("tmdb")] TmdbRef? Tmdb);

    private sealed record TmdbRef(
        [property: JsonPropertyName("id")] string? Id);

    private sealed record EpisodeServer(
        [property: JsonPropertyName("server_data")] IReadOnlyList<EpisodeData>? ServerData);

    private sealed record EpisodeData(
        [property: JsonPropertyName("link_m3u8")] string? LinkM3u8);
}
