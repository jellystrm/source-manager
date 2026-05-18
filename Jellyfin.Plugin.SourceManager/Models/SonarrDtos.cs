using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SourceManager.Models;

public sealed record SonarrSystemStatus(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("isProduction")] bool IsProduction,
    [property: JsonPropertyName("isAdmin")] bool IsAdmin,
    [property: JsonPropertyName("urlBase")] string UrlBase);

public sealed record SonarrQualityProfile(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name);

public sealed record SonarrLanguageProfile(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name);

public sealed record SonarrRootFolder(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("freeSpace")] long FreeSpace,
    [property: JsonPropertyName("unmappedFolders")] IReadOnlyList<object> UnmappedFolders);

public sealed record SonarrTag(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("label")] string Label);

public sealed record SonarrCreateTagDto(
    [property: JsonPropertyName("label")] string Label);

public sealed record SonarrSeasonStatistics(
    [property: JsonPropertyName("episodeFileCount")] int EpisodeFileCount,
    [property: JsonPropertyName("episodeCount")] int EpisodeCount,
    [property: JsonPropertyName("totalEpisodeCount")] int TotalEpisodeCount,
    [property: JsonPropertyName("sizeOnDisk")] long SizeOnDisk,
    [property: JsonPropertyName("percentOfEpisodes")] double PercentOfEpisodes);

public sealed record SonarrSeason(
    [property: JsonPropertyName("seasonNumber")] int SeasonNumber,
    [property: JsonPropertyName("monitored")] bool Monitored,
    [property: JsonPropertyName("statistics")] SonarrSeasonStatistics Statistics);

public sealed record SonarrSeriesStatistics(
    [property: JsonPropertyName("episodeFileCount")] int EpisodeFileCount,
    [property: JsonPropertyName("episodeCount")] int EpisodeCount,
    [property: JsonPropertyName("totalEpisodeCount")] int TotalEpisodeCount,
    [property: JsonPropertyName("sizeOnDisk")] long SizeOnDisk,
    [property: JsonPropertyName("percentOfEpisodes")] double PercentOfEpisodes,
    [property: JsonPropertyName("seasonCount")] int SeasonCount);

public sealed record SonarrSeries(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("tvdbId")] int TvdbId,
    [property: JsonPropertyName("imdbId")] string ImdbId,
    [property: JsonPropertyName("titleSlug")] string TitleSlug,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("qualityProfileId")] int QualityProfileId,
    [property: JsonPropertyName("languageProfileId")] int LanguageProfileId,
    [property: JsonPropertyName("seasonFolder")] bool SeasonFolder,
    [property: JsonPropertyName("monitored")] bool Monitored,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("overview")] string Overview,
    [property: JsonPropertyName("network")] string Network,
    [property: JsonPropertyName("images")] IReadOnlyList<object> Images,
    [property: JsonPropertyName("seasons")] IReadOnlyList<SonarrSeason> Seasons,
    [property: JsonPropertyName("year")] int Year,
    [property: JsonPropertyName("added")] string Added,
    [property: JsonPropertyName("hasFile")] bool HasFile,
    [property: JsonPropertyName("statistics")] SonarrSeriesStatistics Statistics);

public sealed record SonarrAddSeriesDto(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("tvdbId")] int TvdbId,
    [property: JsonPropertyName("qualityProfileId")] int QualityProfileId,
    [property: JsonPropertyName("languageProfileId")] int LanguageProfileId,
    [property: JsonPropertyName("titleSlug")] string TitleSlug,
    [property: JsonPropertyName("rootFolderPath")] string RootFolderPath,
    [property: JsonPropertyName("monitored")] bool Monitored,
    [property: JsonPropertyName("seasonFolder")] bool SeasonFolder,
    [property: JsonPropertyName("seasons")] IReadOnlyList<SonarrSeasonDto>? Seasons,
    [property: JsonPropertyName("tags")] IReadOnlyList<int>? Tags,
    [property: JsonPropertyName("addOptions")] SonarrAddOptions? AddOptions);

public sealed record SonarrSeasonDto(
    [property: JsonPropertyName("seasonNumber")] int SeasonNumber,
    [property: JsonPropertyName("monitored")] bool Monitored);

public sealed record SonarrAddOptions(
    [property: JsonPropertyName("searchForMissingEpisodes")] bool SearchForMissingEpisodes);

public sealed record SonarrUpdateSeriesDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("tvdbId")] int TvdbId,
    [property: JsonPropertyName("monitored")] bool Monitored,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("tags")] IReadOnlyList<int>? Tags);

public sealed record SonarrEpisode(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("seriesId")] int SeriesId,
    [property: JsonPropertyName("seasonNumber")] int SeasonNumber,
    [property: JsonPropertyName("episodeNumber")] int EpisodeNumber,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("monitored")] bool Monitored,
    [property: JsonPropertyName("hasFile")] bool HasFile);

public sealed record SonarrCommandDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("seriesId")] int? SeriesId);
