using System.Globalization;
using System.Net.Mime;
using Jellyfin.Plugin.SourceManager.Models;
using Jellyfin.Plugin.SourceManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SourceManager.Controllers;

/// <summary>
/// Exposes a Sonarr-compatible API so Jellyseerr can point to Source Manager
/// instead of a real Sonarr instance for TV series requests.
///
/// Jellyseerr config:
///   URL  → http://&lt;jellyfin-host&gt;:8096/SourceManager/sonarr
///   Key  → value of Plugin.Instance.Configuration.SonarrApiKey
///
/// Series are identified by their TVDB ID, stored in the tmdb_id column
/// with media_type = 'series'.
/// </summary>
[ApiController]
[Route("SourceManager/sonarr")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class SonarrCompatController : ControllerBase
{
    private const int QualityProfileId = 1;
    private const int LanguageProfileId = 1;
    private const int RootFolderId = 1;

    private const string JellyseerrUserId = "00000000000000000000000000000001";

    private readonly IRequestRepository _repository;
    private readonly LibraryPathService _libraryPaths;
    private readonly ILogger<SonarrCompatController> _logger;

    public SonarrCompatController(
        IRequestRepository repository,
        LibraryPathService libraryPaths,
        ILogger<SonarrCompatController> logger)
    {
        _repository = repository;
        _libraryPaths = libraryPaths;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // Auth
    // -----------------------------------------------------------------------

    private bool IsAuthorized()
    {
        var configuredKey = Plugin.Instance?.Configuration.SonarrApiKey;
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            return false;
        }

        if (Request.Headers.TryGetValue("X-Api-Key", out var headerKey) &&
            string.Equals(headerKey, configuredKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (Request.Query.TryGetValue("apikey", out var queryKey) &&
            string.Equals(queryKey, configuredKey, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private IActionResult Unauthorized401() =>
        StatusCode(StatusCodes.Status401Unauthorized, new { message = "Unauthorized" });

    // -----------------------------------------------------------------------
    // System status
    // -----------------------------------------------------------------------

    [HttpGet("api/v3/system/status")]
    [ProducesResponseType<SonarrSystemStatus>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetSystemStatus()
    {
        if (!IsAuthorized()) return Unauthorized401();

        return Ok(new SonarrSystemStatus(
            Version: "4.0.0.0",
            IsProduction: true,
            IsAdmin: true,
            UrlBase: "/SourceManager/sonarr"));
    }

    // -----------------------------------------------------------------------
    // Quality profiles
    // -----------------------------------------------------------------------

    [HttpGet("api/v3/qualityProfile")]
    [ProducesResponseType<IReadOnlyList<SonarrQualityProfile>>(StatusCodes.Status200OK)]
    public IActionResult GetQualityProfiles()
    {
        if (!IsAuthorized()) return Unauthorized401();
        return Ok(new[] { new SonarrQualityProfile(QualityProfileId, "Source Manager") });
    }

    // -----------------------------------------------------------------------
    // Language profiles (Sonarr v3-specific)
    // -----------------------------------------------------------------------

    [HttpGet("api/v3/languageProfile")]
    [ProducesResponseType<IReadOnlyList<SonarrLanguageProfile>>(StatusCodes.Status200OK)]
    public IActionResult GetLanguageProfiles()
    {
        if (!IsAuthorized()) return Unauthorized401();
        return Ok(new[] { new SonarrLanguageProfile(LanguageProfileId, "Any") });
    }

    // -----------------------------------------------------------------------
    // Root folders
    // -----------------------------------------------------------------------

    [HttpGet("api/v3/rootfolder")]
    [ProducesResponseType<IReadOnlyList<SonarrRootFolder>>(StatusCodes.Status200OK)]
    public IActionResult GetRootFolders()
    {
        if (!IsAuthorized()) return Unauthorized401();

        var path = _libraryPaths.GetShowPath() ?? "/data/strm/shows";
        return Ok(new[]
        {
            new SonarrRootFolder(RootFolderId, path, FreeSpace: 0, UnmappedFolders: Array.Empty<object>())
        });
    }

    // -----------------------------------------------------------------------
    // Tags
    // -----------------------------------------------------------------------

    [HttpGet("api/v3/tag")]
    [ProducesResponseType<IReadOnlyList<SonarrTag>>(StatusCodes.Status200OK)]
    public IActionResult GetTags()
    {
        if (!IsAuthorized()) return Unauthorized401();
        return Ok(Array.Empty<SonarrTag>());
    }

    [HttpPost("api/v3/tag")]
    [ProducesResponseType<SonarrTag>(StatusCodes.Status201Created)]
    public IActionResult CreateTag([FromBody] SonarrCreateTagDto body)
    {
        if (!IsAuthorized()) return Unauthorized401();
        return StatusCode(StatusCodes.Status201Created, new SonarrTag(1, body.Label));
    }

    [HttpPut("api/v3/tag/{id:int}")]
    [ProducesResponseType<SonarrTag>(StatusCodes.Status200OK)]
    public IActionResult UpdateTag(int id, [FromBody] SonarrTag body)
    {
        if (!IsAuthorized()) return Unauthorized401();
        return Ok(body);
    }

    // -----------------------------------------------------------------------
    // Series lookup — called before add to check if already tracked.
    // -----------------------------------------------------------------------

    [HttpGet("api/v3/series/lookup")]
    [ProducesResponseType<IReadOnlyList<SonarrSeries>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LookupSeries(
        [FromQuery] string term,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized()) return Unauthorized401();

        // term format: "tvdb:{id}"
        var tvdbIdStr = term.StartsWith("tvdb:", StringComparison.OrdinalIgnoreCase)
            ? term["tvdb:".Length..]
            : term;

        if (!int.TryParse(tvdbIdStr, NumberStyles.None, CultureInfo.InvariantCulture, out var tvdbId))
        {
            return Ok(Array.Empty<SonarrSeries>());
        }

        var existing = await FindByTvdbIdAsync(tvdbId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Ok(new[] { ToSonarrSeries(existing) });
        }

        // Not tracked — return empty so Jellyseerr knows it can add it.
        return Ok(Array.Empty<SonarrSeries>());
    }

    // -----------------------------------------------------------------------
    // List all tracked series.
    // -----------------------------------------------------------------------

    [HttpGet("api/v3/series")]
    [ProducesResponseType<IReadOnlyList<SonarrSeries>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSeries(CancellationToken cancellationToken)
    {
        if (!IsAuthorized()) return Unauthorized401();

        var all = await _repository
            .GetByStatusAsync(RequestStatus.All, cancellationToken)
            .ConfigureAwait(false);

        var series = all
            .Where(r => string.Equals(r.MediaType, RequestMediaType.Series, StringComparison.OrdinalIgnoreCase))
            .Select(ToSonarrSeries)
            .ToArray();

        return Ok(series);
    }

    // -----------------------------------------------------------------------
    // Get single series by id (= tvdbId).
    // -----------------------------------------------------------------------

    [HttpGet("api/v3/series/{id:int}")]
    [ProducesResponseType<SonarrSeries>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSeriesById(int id, CancellationToken cancellationToken)
    {
        if (!IsAuthorized()) return Unauthorized401();

        var record = await FindByTvdbIdAsync(id, cancellationToken).ConfigureAwait(false);
        return record is null ? NotFound() : Ok(ToSonarrSeries(record));
    }

    // -----------------------------------------------------------------------
    // Add series — Jellyseerr calls this when an admin approves a request.
    // -----------------------------------------------------------------------

    [HttpPost("api/v3/series")]
    [ProducesResponseType<SonarrSeries>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddSeries(
        [FromBody] SonarrAddSeriesDto body,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized()) return Unauthorized401();

        var tvdbIdStr = body.TvdbId.ToString(CultureInfo.InvariantCulture);

        var existing = await FindByTvdbIdAsync(body.TvdbId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Sonarr compat: series tvdb:{TvdbId} already tracked as request {RequestId}",
                body.TvdbId, existing.RequestId);
            return Ok(ToSonarrSeries(existing));
        }

        _logger.LogInformation(
            "Sonarr compat: creating request for series tvdb:{TvdbId} ({Title})",
            body.TvdbId, body.Title);

        var metadata = new RequestMetadata(body.Title, null);
        var requestKey = RequestWorkflowService.BuildRequestKey(tvdbIdStr, RequestMediaType.Series, null, null);

        var record = await _repository
            .CreateOrGetActiveAsync(
                JellyseerrUserId,
                tvdbIdStr,
                RequestMediaType.Series,
                null,
                null,
                requestKey,
                metadata,
                cancellationToken)
            .ConfigureAwait(false);

        // Jellyseerr already approved — advance to processing immediately.
        var processing = await _repository
            .SetProcessingAsync(record.RequestId, streamUrl: null, cancellationToken)
            .ConfigureAwait(false);

        var result = processing ?? record;
        return StatusCode(StatusCodes.Status201Created, ToSonarrSeries(result));
    }

    // -----------------------------------------------------------------------
    // Update series — Jellyseerr calls this to flip monitored flag.
    // -----------------------------------------------------------------------

    [HttpPut("api/v3/series/{id:int}")]
    [ProducesResponseType<SonarrSeries>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSeries(int id, CancellationToken cancellationToken)
    {
        if (!IsAuthorized()) return Unauthorized401();

        var record = await FindByTvdbIdAsync(id, cancellationToken).ConfigureAwait(false);
        return record is null ? NotFound() : Ok(ToSonarrSeries(record));
    }

    // -----------------------------------------------------------------------
    // Delete series — Jellyseerr calls this to withdraw a request.
    // -----------------------------------------------------------------------

    [HttpDelete("api/v3/series/{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSeries(int id, CancellationToken cancellationToken)
    {
        if (!IsAuthorized()) return Unauthorized401();

        var record = await FindByTvdbIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        await _repository
            .SetRejectedAsync(record.RequestId, reason: "Withdrawn via Jellyseerr", cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Sonarr compat: request {RequestId} (tvdb:{TvdbId}) withdrawn via Jellyseerr",
            record.RequestId, id);

        return Ok();
    }

    // -----------------------------------------------------------------------
    // Command — Jellyseerr sends "SeriesSearch" after adding.
    // -----------------------------------------------------------------------

    [HttpPost("api/v3/command")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public IActionResult SendCommand([FromBody] SonarrCommandDto body)
    {
        if (!IsAuthorized()) return Unauthorized401();

        _logger.LogInformation(
            "Sonarr compat: received command '{Name}' for series {SeriesId}",
            body.Name,
            body.SeriesId?.ToString(CultureInfo.InvariantCulture) ?? "all");

        return StatusCode(StatusCodes.Status201Created, new { id = 1, name = body.Name, status = "queued" });
    }

    // -----------------------------------------------------------------------
    // Episodes — Jellyseerr checks these to show download progress.
    // We return empty arrays; the admin workflow handles stream assignment.
    // -----------------------------------------------------------------------

    [HttpGet("api/v3/episode")]
    [ProducesResponseType<IReadOnlyList<SonarrEpisode>>(StatusCodes.Status200OK)]
    public IActionResult GetEpisodes([FromQuery] int seriesId)
    {
        if (!IsAuthorized()) return Unauthorized401();
        return Ok(Array.Empty<SonarrEpisode>());
    }

    [HttpGet("api/v3/episodefile")]
    [ProducesResponseType<IReadOnlyList<object>>(StatusCodes.Status200OK)]
    public IActionResult GetEpisodeFiles([FromQuery] int seriesId)
    {
        if (!IsAuthorized()) return Unauthorized401();
        return Ok(Array.Empty<object>());
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<MediaRequestRecord?> FindByTvdbIdAsync(int tvdbId, CancellationToken cancellationToken)
    {
        var tvdbIdStr = tvdbId.ToString(CultureInfo.InvariantCulture);
        var all = await _repository
            .GetByStatusAsync(RequestStatus.All, cancellationToken)
            .ConfigureAwait(false);

        return all.FirstOrDefault(r =>
            string.Equals(r.TmdbId, tvdbIdStr, StringComparison.Ordinal) &&
            string.Equals(r.MediaType, RequestMediaType.Series, StringComparison.OrdinalIgnoreCase));
    }

    private static SonarrSeries ToSonarrSeries(MediaRequestRecord r)
    {
        var tvdbId = int.TryParse(r.TmdbId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id : 0;

        var hasFile = string.Equals(r.Status, RequestStatus.Ready, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(r.StreamUrl);

        return new SonarrSeries(
            Id: tvdbId,
            Title: r.Title,
            TvdbId: tvdbId,
            ImdbId: string.Empty,
            TitleSlug: BuildTitleSlug(r.Title, tvdbId),
            Path: string.Empty,
            QualityProfileId: QualityProfileId,
            LanguageProfileId: LanguageProfileId,
            SeasonFolder: true,
            Monitored: !string.Equals(r.Status, RequestStatus.Rejected, StringComparison.OrdinalIgnoreCase),
            Status: "continuing",
            Overview: string.Empty,
            Network: string.Empty,
            Images: Array.Empty<object>(),
            Seasons: Array.Empty<SonarrSeason>(),
            Year: 0,
            Added: DateTimeOffset.FromUnixTimeMilliseconds(r.RequestedAt).ToString("o", CultureInfo.InvariantCulture),
            HasFile: hasFile,
            Statistics: new SonarrSeriesStatistics(0, 0, 0, 0, 0.0, 0));
    }

    private static string BuildTitleSlug(string title, int tvdbId)
    {
        var safe = new string(title
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray())
            .Trim('-');
        return $"{safe}-{tvdbId.ToString(CultureInfo.InvariantCulture)}";
    }
}
