using Jellyfin.Plugin.SourceManager.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.SourceManager.Services;

public sealed class LibraryRequestMatcher
{
    private readonly IRequestRepository _repository;

    public LibraryRequestMatcher(IRequestRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<MediaRequestRecord>> MarkReadyForItemAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var match = GetContentMatch(item);
        if (match is null)
        {
            return Array.Empty<MediaRequestRecord>();
        }

        var requests = await _repository
            .GetProcessingByContentAsync(match.TmdbId, match.MediaType, match.SeasonNumber, match.EpisodeNumber, cancellationToken)
            .ConfigureAwait(false);

        var updated = new List<MediaRequestRecord>();
        foreach (var request in requests)
        {
            var ready = await _repository.SetReadyAsync(request.RequestId, item.Id.ToString("N"), cancellationToken).ConfigureAwait(false);
            if (ready is not null)
            {
                updated.Add(ready);
            }
        }

        return updated;
    }

    public async Task<MediaRequestRecord?> RefreshRequestAsync(MediaRequestRecord request, ILibraryManager libraryManager, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Status, RequestStatus.Processing, StringComparison.OrdinalIgnoreCase))
        {
            return request;
        }

        var item = FindMatchingItem(request, libraryManager);
        if (item is null)
        {
            return request;
        }

        return await _repository.SetReadyAsync(request.RequestId, item.Id.ToString("N"), cancellationToken).ConfigureAwait(false);
    }

    private static BaseItem? FindMatchingItem(MediaRequestRecord request, ILibraryManager libraryManager)
    {
        var itemTypes = request.MediaType switch
        {
            RequestMediaType.Movie => new[] { Jellyfin.Data.Enums.BaseItemKind.Movie },
            RequestMediaType.Series => new[] { Jellyfin.Data.Enums.BaseItemKind.Series },
            RequestMediaType.Episode => new[] { Jellyfin.Data.Enums.BaseItemKind.Episode },
            _ => Array.Empty<Jellyfin.Data.Enums.BaseItemKind>()
        };

        if (itemTypes.Length == 0)
        {
            return null;
        }

        var items = libraryManager.GetItemList(new InternalItemsQuery
        {
            Recursive = true,
            IncludeItemTypes = itemTypes,
            DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
            {
                EnableImages = false
            }
        });

        return items.FirstOrDefault(item => IsMatch(item, request));
    }

    private static bool IsMatch(BaseItem item, MediaRequestRecord request)
    {
        return request.MediaType switch
        {
            RequestMediaType.Movie => item is Movie && HasTmdbId(item, request.TmdbId),
            RequestMediaType.Series => item is Series && HasTmdbId(item, request.TmdbId),
            RequestMediaType.Episode => item is Episode episode
                && request.SeasonNumber.HasValue
                && request.EpisodeNumber.HasValue
                && episode.ParentIndexNumber == request.SeasonNumber.Value
                && episode.ContainsEpisodeNumber(request.EpisodeNumber.Value)
                && episode.Series is not null
                && HasTmdbId(episode.Series, request.TmdbId),
            _ => false
        };
    }

    private static ContentMatch? GetContentMatch(BaseItem item)
    {
        return item switch
        {
            Movie movie when TryGetTmdbId(movie, out var tmdbId) => new ContentMatch(tmdbId, RequestMediaType.Movie, null, null),
            Series series when TryGetTmdbId(series, out var tmdbId) => new ContentMatch(tmdbId, RequestMediaType.Series, null, null),
            Episode episode when episode.Series is not null
                && episode.ParentIndexNumber.HasValue
                && episode.IndexNumber.HasValue
                && TryGetTmdbId(episode.Series, out var tmdbId) => new ContentMatch(
                    tmdbId,
                    RequestMediaType.Episode,
                    episode.ParentIndexNumber.Value,
                    episode.IndexNumber.Value),
            _ => null
        };
    }

    private static bool HasTmdbId(BaseItem item, string tmdbId)
        => string.Equals(item.GetProviderId(MetadataProvider.Tmdb), tmdbId, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetTmdbId(BaseItem item, out string tmdbId)
    {
        tmdbId = item.GetProviderId(MetadataProvider.Tmdb) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(tmdbId);
    }

    private sealed record ContentMatch(string TmdbId, string MediaType, int? SeasonNumber, int? EpisodeNumber);
}
