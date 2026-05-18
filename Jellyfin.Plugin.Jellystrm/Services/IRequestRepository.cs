using Jellyfin.Plugin.Jellystrm.Models;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Jellystrm.Services;

public interface IRequestRepository
{
    Task<MediaRequestRecord> CreateOrGetActiveAsync(
        string userId,
        string tmdbId,
        string mediaType,
        int? seasonNumber,
        int? episodeNumber,
        string requestKey,
        RequestMetadata metadata,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MediaRequestRecord>> GetByUserAsync(string userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MediaRequestRecord>> GetByStatusAsync(string status, CancellationToken cancellationToken);

    Task<MediaRequestRecord?> GetByIdAsync(string requestId, CancellationToken cancellationToken);

    Task<MediaRequestRecord?> SetProcessingAsync(string requestId, CancellationToken cancellationToken);

    Task<MediaRequestRecord?> SetRejectedAsync(string requestId, string? reason, CancellationToken cancellationToken);

    Task<MediaRequestRecord?> SetReadyAsync(string requestId, string jellyfinItemId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MediaRequestRecord>> GetProcessingByContentAsync(
        string tmdbId,
        string mediaType,
        int? seasonNumber,
        int? episodeNumber,
        CancellationToken cancellationToken);
}
