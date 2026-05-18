using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.SourceManager.Models;

namespace Jellyfin.Plugin.SourceManager.Services;

public sealed class TmdbMetadataService
{
    private const string DefaultTmdbApiKey = "4219e299c89411838049ab0dab19ebd5";
    private const string TmdbApiBaseUrl = "https://api.themoviedb.org/3";
    private const string TmdbImageBaseUrl = "https://image.tmdb.org/t/p";

    private readonly HttpClient _httpClient;

    public TmdbMetadataService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<RequestMetadata> GetMetadataAsync(
        string tmdbId,
        string mediaType,
        int? seasonNumber,
        int? episodeNumber,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(tmdbId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedTmdbId))
        {
            throw new ArgumentException("tmdbId must be a numeric TMDB id.", nameof(tmdbId));
        }

        return mediaType switch
        {
            RequestMediaType.Movie => await GetMovieMetadataAsync(parsedTmdbId, tmdbId, cancellationToken).ConfigureAwait(false),
            RequestMediaType.Series => await GetSeriesMetadataAsync(parsedTmdbId, tmdbId, cancellationToken).ConfigureAwait(false),
            RequestMediaType.Episode => await GetEpisodeMetadataAsync(parsedTmdbId, tmdbId, seasonNumber, episodeNumber, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentException("Unsupported media type.", nameof(mediaType))
        };
    }

    private async Task<RequestMetadata> GetMovieMetadataAsync(int parsedTmdbId, string tmdbId, CancellationToken cancellationToken)
    {
        var movie = await GetFromTmdbAsync<TmdbMovie>($"movie/{parsedTmdbId.ToString(CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
        if (movie is null)
        {
            return new RequestMetadata($"TMDB movie {tmdbId}", null);
        }

        var title = FirstNonEmpty(movie.Title, movie.OriginalTitle, $"TMDB movie {tmdbId}");
        return new RequestMetadata(title, GetPosterUrl(movie.PosterPath));
    }

    private async Task<RequestMetadata> GetSeriesMetadataAsync(int parsedTmdbId, string tmdbId, CancellationToken cancellationToken)
    {
        var series = await GetFromTmdbAsync<TmdbSeries>($"tv/{parsedTmdbId.ToString(CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
        if (series is null)
        {
            return new RequestMetadata($"TMDB series {tmdbId}", null);
        }

        var title = FirstNonEmpty(series.Name, series.OriginalName, $"TMDB series {tmdbId}");
        return new RequestMetadata(title, GetPosterUrl(series.PosterPath));
    }

    private async Task<RequestMetadata> GetEpisodeMetadataAsync(
        int parsedTmdbId,
        string tmdbId,
        int? seasonNumber,
        int? episodeNumber,
        CancellationToken cancellationToken)
    {
        if (!seasonNumber.HasValue || !episodeNumber.HasValue)
        {
            throw new ArgumentException("Episode requests require seasonNumber and episodeNumber.");
        }

        var series = await GetFromTmdbAsync<TmdbSeries>($"tv/{parsedTmdbId.ToString(CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
        var episode = await GetFromTmdbAsync<TmdbEpisode>(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"tv/{parsedTmdbId}/season/{seasonNumber.Value}/episode/{episodeNumber.Value}"),
                cancellationToken)
            .ConfigureAwait(false);

        var seriesTitle = FirstNonEmpty(series?.Name, series?.OriginalName, $"TMDB series {tmdbId}");
        var episodeLabel = $"S{seasonNumber.Value.ToString("00", CultureInfo.InvariantCulture)}E{episodeNumber.Value.ToString("00", CultureInfo.InvariantCulture)}";
        var episodeTitle = FirstNonEmpty(episode?.Name, episodeLabel);
        var title = string.Equals(episodeTitle, episodeLabel, StringComparison.Ordinal)
            ? $"{seriesTitle} - {episodeLabel}"
            : $"{seriesTitle} - {episodeLabel} - {episodeTitle}";

        var posterUrl = GetPosterUrl(episode?.StillPath) ?? GetPosterUrl(series?.PosterPath);
        return new RequestMetadata(title, posterUrl);
    }

    private async Task<T?> GetFromTmdbAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var apiKey = Plugin.Instance?.Configuration.TmdbApiKey;
        apiKey = string.IsNullOrWhiteSpace(apiKey) ? DefaultTmdbApiKey : apiKey;
        var requestUri = $"{TmdbApiBaseUrl}/{path}{separator}api_key={Uri.EscapeDataString(apiKey)}";
        try
        {
            return await _httpClient.GetFromJsonAsync<T>(requestUri, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static string? GetPosterUrl(string? posterPath)
    {
        if (string.IsNullOrWhiteSpace(posterPath))
        {
            return null;
        }

        var size = Plugin.Instance?.Configuration.TmdbPosterSize;
        size = string.IsNullOrWhiteSpace(size) ? "w500" : size;
        return $"{TmdbImageBaseUrl}/{size}{posterPath}";
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.First(value => !string.IsNullOrWhiteSpace(value))!;

    private sealed record TmdbMovie(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("original_title")] string? OriginalTitle,
        [property: JsonPropertyName("poster_path")] string? PosterPath);

    private sealed record TmdbSeries(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("original_name")] string? OriginalName,
        [property: JsonPropertyName("poster_path")] string? PosterPath);

    private sealed record TmdbEpisode(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("still_path")] string? StillPath);
}
