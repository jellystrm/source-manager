using System.Globalization;
using Jellyfin.Plugin.SourceManager.Models;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.SourceManager.Services;

public sealed class RequestWorkflowService
{
    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        RequestStatus.Pending,
        RequestStatus.Processing,
        RequestStatus.Ready,
        RequestStatus.Rejected,
        RequestStatus.All
    };

    private readonly IRequestRepository _repository;
    private readonly TmdbMetadataService _metadataService;
    private readonly LibraryRequestMatcher _libraryRequestMatcher;
    private readonly ILibraryManager _libraryManager;
    private readonly RequestEventBroker _eventBroker;
    private readonly StrmWriterService _strmWriter;

    public RequestWorkflowService(
        IRequestRepository repository,
        TmdbMetadataService metadataService,
        LibraryRequestMatcher libraryRequestMatcher,
        ILibraryManager libraryManager,
        RequestEventBroker eventBroker,
        StrmWriterService strmWriter)
    {
        _repository = repository;
        _metadataService = metadataService;
        _libraryRequestMatcher = libraryRequestMatcher;
        _libraryManager = libraryManager;
        _eventBroker = eventBroker;
        _strmWriter = strmWriter;
    }

    public async Task<MediaRequestRecord> CreateRequestAsync(CreateRequestDto request, CancellationToken cancellationToken)
    {
        var mediaType = NormalizeMediaType(request.MediaType);
        ValidateEpisodeFields(mediaType, request.SeasonNumber, request.EpisodeNumber);

        var metadata = await _metadataService
            .GetMetadataAsync(request.TmdbId, mediaType, request.SeasonNumber, request.EpisodeNumber, cancellationToken)
            .ConfigureAwait(false);
        var requestKey = BuildRequestKey(request.TmdbId, mediaType, request.SeasonNumber, request.EpisodeNumber);

        return await _repository
            .CreateOrGetActiveAsync(
                request.UserId,
                request.TmdbId,
                mediaType,
                request.SeasonNumber,
                request.EpisodeNumber,
                requestKey,
                metadata,
                tvdbId: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<MediaRequestRecord>> GetUserRequestsAsync(string userId, CancellationToken cancellationToken)
        => _repository.GetByUserAsync(userId, cancellationToken);

    public Task<IReadOnlyList<MediaRequestRecord>> GetAdminRequestsAsync(string status, CancellationToken cancellationToken)
    {
        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? RequestStatus.Pending : status.ToLowerInvariant();
        if (!ValidStatuses.Contains(normalizedStatus))
        {
            throw new ArgumentException("Unsupported request status.", nameof(status));
        }

        return _repository.GetByStatusAsync(normalizedStatus, cancellationToken);
    }

    public async Task<IReadOnlyList<MediaRequestRecord>> ApproveAsync(
        string requestId,
        string? streamUrl,
        CancellationToken cancellationToken)
    {
        var normalizedUrl = string.IsNullOrWhiteSpace(streamUrl) ? null : streamUrl.Trim();

        var processing = await _repository
            .SetProcessingAsync(requestId, normalizedUrl, cancellationToken)
            .ConfigureAwait(false);

        if (processing is null)
        {
            return Array.Empty<MediaRequestRecord>();
        }

        Publish(processing);

        // Write .strm file when a stream URL is supplied by the admin.
        if (!string.IsNullOrWhiteSpace(normalizedUrl))
        {
            _strmWriter.WriteStrmFile(processing, normalizedUrl);
        }

        var refreshed = await _libraryRequestMatcher
            .RefreshRequestAsync(processing, _libraryManager, cancellationToken)
            .ConfigureAwait(false);

        if (refreshed is null || string.Equals(refreshed.Status, processing.Status, StringComparison.OrdinalIgnoreCase))
        {
            return new[] { processing };
        }

        Publish(refreshed);
        return new[] { processing, refreshed };
    }

    public async Task<MediaRequestRecord?> RejectAsync(string requestId, string? reason, CancellationToken cancellationToken)
    {
        var rejected = await _repository.SetRejectedAsync(requestId, reason, cancellationToken).ConfigureAwait(false);
        if (rejected is not null)
        {
            Publish(rejected);

            // Clean up any .strm file that was written when the request was approved.
            if (!string.IsNullOrWhiteSpace(rejected.StreamUrl))
            {
                _strmWriter.DeleteStrmFile(rejected);
            }
        }

        return rejected;
    }

    public async Task<MediaRequestRecord?> RefreshAsync(string requestId, CancellationToken cancellationToken)
    {
        var request = await _repository.GetByIdAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return null;
        }

        var refreshed = await _libraryRequestMatcher
            .RefreshRequestAsync(request, _libraryManager, cancellationToken)
            .ConfigureAwait(false);

        if (refreshed is not null && !string.Equals(refreshed.Status, request.Status, StringComparison.OrdinalIgnoreCase))
        {
            Publish(refreshed);
        }

        return refreshed;
    }

    public void Publish(MediaRequestRecord request)
        => _eventBroker.Publish(request.UserId, request.ToDto());

    public static string BuildRequestKey(string tmdbId, string mediaType, int? seasonNumber, int? episodeNumber)
    {
        return mediaType switch
        {
            RequestMediaType.Movie => $"movie:{tmdbId}",
            RequestMediaType.Series => $"series:{tmdbId}",
            RequestMediaType.Episode => string.Create(
                CultureInfo.InvariantCulture,
                $"episode:{tmdbId}:s{seasonNumber!.Value}:e{episodeNumber!.Value}"),
            _ => throw new ArgumentException("Unsupported media type.", nameof(mediaType))
        };
    }

    private static string NormalizeMediaType(string mediaType)
    {
        var normalized = mediaType.ToLowerInvariant();
        return normalized is RequestMediaType.Movie or RequestMediaType.Series or RequestMediaType.Episode
            ? normalized
            : throw new ArgumentException("mediaType must be movie, series, or episode.", nameof(mediaType));
    }

    private static void ValidateEpisodeFields(string mediaType, int? seasonNumber, int? episodeNumber)
    {
        if (mediaType == RequestMediaType.Episode && (!seasonNumber.HasValue || !episodeNumber.HasValue))
        {
            throw new ArgumentException("Episode requests require seasonNumber and episodeNumber.");
        }

        if (mediaType != RequestMediaType.Episode && (seasonNumber.HasValue || episodeNumber.HasValue))
        {
            throw new ArgumentException("seasonNumber and episodeNumber are only valid for episode requests.");
        }
    }
}
