namespace Jellyfin.Plugin.SourceManager.Models;

public sealed record CapabilitiesResponse(bool SupportsRequest, string Version, bool SupportsSse);

public sealed record CreateRequestDto(
    string TmdbId,
    string MediaType,
    string UserId,
    int? SeasonNumber,
    int? EpisodeNumber);

public sealed record RejectRequestDto(string? Reason);

public sealed record MediaRequestDto(
    string RequestId,
    string TmdbId,
    string Title,
    string? PosterUrl,
    string MediaType,
    string Status,
    long RequestedAt,
    string? JellyfinItemId,
    int? SeasonNumber,
    int? EpisodeNumber,
    string? RejectReason);

public sealed record RequestMetadata(string Title, string? PosterUrl);

public sealed record MediaRequestRecord(
    string RequestId,
    string UserId,
    string TmdbId,
    string MediaType,
    int? SeasonNumber,
    int? EpisodeNumber,
    string RequestKey,
    string Title,
    string? PosterUrl,
    string Status,
    long RequestedAt,
    long UpdatedAt,
    string? JellyfinItemId,
    string? RejectReason)
{
    public MediaRequestDto ToDto()
        => new(
            RequestId,
            TmdbId,
            Title,
            PosterUrl,
            MediaType,
            Status,
            RequestedAt,
            JellyfinItemId,
            SeasonNumber,
            EpisodeNumber,
            RejectReason);
}
