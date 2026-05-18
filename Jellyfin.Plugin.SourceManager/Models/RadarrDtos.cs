using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SourceManager.Models;

public sealed record RadarrSystemStatus(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("isProduction")] bool IsProduction,
    [property: JsonPropertyName("isAdmin")] bool IsAdmin,
    [property: JsonPropertyName("urlBase")] string UrlBase);

public sealed record RadarrQualityProfile(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name);

public sealed record RadarrRootFolder(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("freeSpace")] long FreeSpace,
    [property: JsonPropertyName("totalSpace")] long TotalSpace,
    [property: JsonPropertyName("unmappedFolders")] IReadOnlyList<object> UnmappedFolders);

public sealed record RadarrTag(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("label")] string Label);

public sealed record RadarrCreateTagDto(
    [property: JsonPropertyName("label")] string Label);

public sealed record RadarrQueue(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("totalRecords")] int TotalRecords,
    [property: JsonPropertyName("records")] IReadOnlyList<object> Records);

public sealed record RadarrMovieFile(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("movieId")] int MovieId,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("dateAdded")] string DateAdded,
    [property: JsonPropertyName("qualityCutoffNotMet")] bool QualityCutoffNotMet);

public sealed record RadarrMovie(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("isAvailable")] bool IsAvailable,
    [property: JsonPropertyName("monitored")] bool Monitored,
    [property: JsonPropertyName("tmdbId")] int TmdbId,
    [property: JsonPropertyName("imdbId")] string ImdbId,
    [property: JsonPropertyName("titleSlug")] string TitleSlug,
    [property: JsonPropertyName("folderName")] string FolderName,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("profileId")] int ProfileId,
    [property: JsonPropertyName("qualityProfileId")] int QualityProfileId,
    [property: JsonPropertyName("added")] string Added,
    [property: JsonPropertyName("hasFile")] bool HasFile,
    [property: JsonPropertyName("tags")] IReadOnlyList<int> Tags,
    [property: JsonPropertyName("movieFile")] RadarrMovieFile? MovieFile);

public sealed record RadarrAddMovieDto(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("qualityProfileId")] int QualityProfileId,
    [property: JsonPropertyName("profileId")] int ProfileId,
    [property: JsonPropertyName("titleSlug")] string TitleSlug,
    [property: JsonPropertyName("minimumAvailability")] string MinimumAvailability,
    [property: JsonPropertyName("tmdbId")] int TmdbId,
    [property: JsonPropertyName("year")] int Year,
    [property: JsonPropertyName("rootFolderPath")] string RootFolderPath,
    [property: JsonPropertyName("monitored")] bool Monitored,
    [property: JsonPropertyName("tags")] IReadOnlyList<int>? Tags,
    [property: JsonPropertyName("addOptions")] RadarrAddOptions? AddOptions);

public sealed record RadarrUpdateMovieDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("tmdbId")] int TmdbId,
    [property: JsonPropertyName("monitored")] bool Monitored,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("tags")] IReadOnlyList<int>? Tags);

public sealed record RadarrAddOptions(
    [property: JsonPropertyName("searchForMovie")] bool SearchForMovie);

public sealed record RadarrCommandDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("movieIds")] IReadOnlyList<int>? MovieIds);
