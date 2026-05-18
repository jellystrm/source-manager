using Jellyfin.Plugin.SourceManager.Models;
using Jellyfin.Plugin.SourceManager.Services.SourceResolution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SourceManager.Services;

/// <summary>
/// Background service that periodically scans for requests in the "processing"
/// state that have no stream URL yet, then tries each resolver in priority order:
///   1. KKPhim  (Vietnamese m3u8 streams)
///   2. OPhim   (Vietnamese m3u8 streams)
///   3. YTS     (torrent via qBittorrent)
///
/// On success: writes a .strm file (for stream URLs) or queues a torrent download.
/// On failure: leaves the request in "processing" so the admin can supply a URL manually.
/// </summary>
public sealed class SourceResolutionService : BackgroundService
{
    private readonly IRequestRepository _repository;
    private readonly StrmWriterService _strmWriter;
    private readonly IReadOnlyList<ISourceResolver> _resolvers;
    private readonly QBittorrentClient _qbittorrent;
    private readonly ILogger<SourceResolutionService> _logger;

    public SourceResolutionService(
        IRequestRepository repository,
        StrmWriterService strmWriter,
        KkPhimResolver kkPhim,
        OPhimResolver oPhim,
        YtsResolver yts,
        QBittorrentClient qbittorrent,
        ILogger<SourceResolutionService> logger)
    {
        _repository = repository;
        _strmWriter = strmWriter;
        _resolvers = [kkPhim, oPhim, yts];
        _qbittorrent = qbittorrent;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give Jellyfin time to finish startup before the first scan.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (Plugin.Instance?.Configuration.EnableAutoResolution == true)
            {
                await ResolveAllAsync(stoppingToken).ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ResolveAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MediaRequestRecord> processing;
        try
        {
            processing = await _repository
                .GetByStatusAsync(RequestStatus.Processing, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SourceResolution: failed to query processing requests");
            return;
        }

        var unresolved = processing
            .Where(r => string.IsNullOrEmpty(r.StreamUrl))
            .ToList();

        if (unresolved.Count == 0) return;

        _logger.LogDebug("SourceResolution: scanning {Count} unresolved request(s)", unresolved.Count);

        foreach (var request in unresolved)
        {
            if (cancellationToken.IsCancellationRequested) break;
            await TryResolveOneAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task TryResolveOneAsync(MediaRequestRecord request, CancellationToken cancellationToken)
    {
        foreach (var resolver in _resolvers)
        {
            SourceResult? result;
            try
            {
                result = await resolver.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SourceResolution: {Resolver} threw for request {RequestId}",
                    resolver.Name, request.RequestId);
                continue;
            }

            if (result is null) continue;

            await ApplyResultAsync(request, result, cancellationToken).ConfigureAwait(false);
            return;
        }

        _logger.LogDebug(
            "SourceResolution: no source found for '{Title}' (tmdb:{TmdbId})",
            request.Title, request.TmdbId);
    }

    private async Task ApplyResultAsync(
        MediaRequestRecord request,
        SourceResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await _repository
                .UpdateStreamUrlAsync(request.RequestId, result.Value, cancellationToken)
                .ConfigureAwait(false);

            if (result.Kind == SourceKind.StreamUrl)
            {
                _strmWriter.WriteStrmFile(request, result.Value);
            }
            else if (result.Kind == SourceKind.Torrent && _qbittorrent.IsConfigured)
            {
                var savePath = GetTorrentSavePath(request);
                await _qbittorrent
                    .AddTorrentAsync(result.Value, savePath, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SourceResolution: failed to apply result for request {RequestId}",
                request.RequestId);
        }
    }

    private static string GetTorrentSavePath(MediaRequestRecord request)
    {
        var basePath = Plugin.Instance?.Configuration.StrmLibraryPath ?? "/data/strm";
        return string.Equals(request.MediaType, RequestMediaType.Movie, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(basePath, "movies")
            : Path.Combine(basePath, "shows");
    }
}
