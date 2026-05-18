using System.Globalization;
using System.Net.Mime;
using Jellyfin.Plugin.SourceManager.Models;
using Jellyfin.Plugin.SourceManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SourceManager.Controllers;

/// <summary>
/// Exposes a Radarr-compatible API so Jellyseerr can point to Source Manager
/// instead of a real Radarr instance.
///
/// Jellyseerr config:
///   URL  → http://&lt;jellyfin-host&gt;:8096/SourceManager/radarr
///   Key  → value of Plugin.Instance.Configuration.RadarrApiKey
/// </summary>
[ApiController]
[Route("SourceManager/radarr")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class RadarrCompatController : ControllerBase
{
    private const int QualityProfileId = 1;
    private const int RootFolderId = 1;

    // Jellyseerr-originated requests are attributed to this synthetic user.
    private const string JellyseerrUserId = "00000000000000000000000000000001";

    private readonly IRequestRepository _repository;
    private readonly TmdbMetadataService _tmdbService;
    private readonly LibraryPathService _libraryPaths;
    private readonly ILogger<RadarrCompatController> _logger;

    public RadarrCompatController(
        IRequestRepository repository,
        TmdbMetadataService tmdbService,
        LibraryPathService libraryPaths,
        ILogger<RadarrCompatController> logger)
    {
        _repository = repository;
        _tmdbService = tmdbService;
        _libraryPaths = libraryPaths;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // Auth
    // -----------------------------------------------------------------------

    private bool IsAuthorized()
    {
        var configuredKey = Plugin.Instance?.Configuration.RadarrApiKey;
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            return false;
        }

        // Jellyseerr sends the key as the X-Api-Key header (set via axios default headers).
        if (Request.Headers.TryGetValue("X-Api-Key", out var headerKey) &&
            string.Equals(headerKey, configuredKey, StringComparison.Ordinal))
        {
            return true;
        }

        // Fallback: apikey query param.
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
    // System status — Jellyseerr calls this to verify the connection.
    // -----------------------------------------------------------------------

    [HttpGet("api/v3/system/status")]
    [ProducesResponseType<RadarrSystemStatus>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetSystemStatus()
    {
        if (!IsAuthorized()) return Unauthorized401();

        return Ok(new RadarrSystemStatus(
            Version: "5.0.0.0",
            IsProduction: true,
            IsAdmin: true,
            UrlBase: "/SourceManager/radarr"));
    }

    // -----------------------------------------------------------------------
    // Quality profiles — return one fake profile.
    // -----------------------------------------------------------------------

    [HttpGet("api/v3/qualityProfile")]
    [ProducesResponseType<IReadOnlyList<RadarrQualityProfile>>(StatusCodes.Status200OK)]
    public IActionResult GetQualityProfiles()
    {
        if (!IsAuthorized()) return Unauthorized401();

        return Ok(new[] { new RadarrQualityProfile(QualityProfileId, "Source Manager") });
    }

    // -----------------------------------------------------------------------
    // Root folders — return StrmLibraryPath (or a placeholder).
    // -----------------------------------------------------------------------

    [HttpGet("api/v3/rootfolder")]
    [ProducesResponseType<IReadOnlyList<RadarrRootFolder>>(StatusCodes.Status200OK)]
    public IActionResult GetRootFolders()
    {
        if (!IsAuthorized()) return Unauthorized401();

        var path = _libraryPaths.GetMoviePath() ?? "/data/strm/movies";
        return Ok(new[]
        {
            new RadarrRootFolder(RootFolderId, path, FreeSpace: 0, TotalSpace: 0, UnmappedFolders: Array.Empty<object>())
        });
    }

    // -----------------------------------------------------------------------
    // Tags
    // -----------------------------------------------------------------------

    [HttpGet("api/v3/tag")]
    [ProducesResponseType<IReadOnlyList<RadarrTag>>(StatusCodes.Status200OK)]
    public IActionResult GetTags()
    {
        if (!IsAuthorized()) return Unauthorized401();
        return Ok(Array.Empty<RadarrTag>());
    }

    [HttpPost("api/v3/tag")]
    [ProducesResponseType<RadarrTag>(StatusCodes.Status201Created)]
    public IActionResult CreateTag([FromBody] RadarrCreateTagDto body)
    {
        if (!IsAuthorized()) return Unauthorized401();
        // We don't persist tags — just echo back with a synthetic id.
        return StatusCode(StatusCodes.Status201Created, new RadarrTag(1, body.Label));
    }

    [HttpPut("api/v3/tag/{id:int}")]
    [ProducesResponseType<RadarrTag>(StatusCodes.Status200OK)]
    public IActionResult UpdateTag(int id, [FromBody] RadarrTag body)
    {
        if (!IsAuthorized()) return Unauthorized401();
        return Ok(body);
    }

    // -----------------------------------------------------------------------
    // Queue — always empty (Source Manager is not a download client).
    // -----------------------------------------------------------------------

    [HttpGet("api/v3/queue")]
    [ProducesResponseType<RadarrQueue>(StatusCodes.Status200OK)]
    public IActionResult GetQueue()
    {
        if (!IsAuthorized()) return Unauthorized401();
        return Ok(new RadarrQueue(Page: 1, PageSize: 20, TotalRecords: 0, Records: Array.Empty<object>()));
    }

    // -----------------------------------------------------------------------
    // Movie lookup — called before add to check if already tracked.
    // -----------------------------------------------------------------------

    [HttpGet("api/v3/movie/lookup")]
    [ProducesResponseType<IReadOnlyList<RadarrMovie>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LookupMovie(
        [FromQuery] string term,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized()) return Unauthorized401();

        // term format: "tmdb:{id}"
        var tmdbIdStr = term.StartsWith("tmdb:", StringComparison.OrdinalIgnoreCase)
            ? term["tmdb:".Length..]
            : term;

        if (!int.TryParse(tmdbIdStr, NumberStyles.None, CultureInfo.InvariantCulture, out var tmdbId))
        {
            return Ok(Array.Empty<RadarrMovie>());
        }

        // Check if we already have a request for this movie.
        var existing = await FindByTmdbIdAsync(tmdbId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Ok(new[] { ToRadarrMovie(existing) });
        }

        // Not tracked yet — fetch basic info from TMDB so Jellyseerr can show title/year.
        // Return id=0 so Jellyseerr knows it hasn't been added to "Radarr" yet.
        try
        {
            var metadata = await _tmdbService
                .GetMetadataAsync(tmdbIdStr, RequestMediaType.Movie, null, null, cancellationToken)
                .ConfigureAwait(false);

            return Ok(new[]
            {
                new RadarrMovie(
                    Id: 0,
                    Title: metadata.Title,
                    IsAvailable: false,
                    Monitored: false,
                    TmdbId: tmdbId,
                    ImdbId: string.Empty,
                    TitleSlug: BuildTitleSlug(metadata.Title, tmdbId),
                    FolderName: string.Empty,
                    Path: string.Empty,
                    ProfileId: QualityProfileId,
                    QualityProfileId: QualityProfileId,
                    Added: DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    HasFile: false,
                    Tags: Array.Empty<int>(),
                    MovieFile: null)
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch TMDB metadata for lookup of {TmdbId}", tmdbId);
            return Ok(Array.Empty<RadarrMovie>());
        }
    }

    // -----------------------------------------------------------------------
    // List all tracked movies.
    // -----------------------------------------------------------------------

    [HttpGet("api/v3/movie")]
    [ProducesResponseType<IReadOnlyList<RadarrMovie>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMovies(CancellationToken cancellationToken)
    {
        if (!IsAuthorized()) return Unauthorized401();

        var requests = await _repository
            .GetByStatusAsync(RequestStatus.All, cancellationToken)
            .ConfigureAwait(false);

        var movies = requests
            .Where(r => string.Equals(r.MediaType, RequestMediaType.Movie, StringComparison.OrdinalIgnoreCase))
            .Select(ToRadarrMovie)
            .ToArray();

        return Ok(movies);
    }

    // -----------------------------------------------------------------------
    // Get single movie by id (= tmdbId).
    // -----------------------------------------------------------------------

    [HttpGet("api/v3/movie/{id:int}")]
    [ProducesResponseType<RadarrMovie>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMovie(int id, CancellationToken cancellationToken)
    {
        if (!IsAuthorized()) return Unauthorized401();

        var record = await FindByTmdbIdAsync(id, cancellationToken).ConfigureAwait(false);
        return record is null ? NotFound() : Ok(ToRadarrMovie(record));
    }

    // -----------------------------------------------------------------------
    // Add movie — Jellyseerr calls this when an admin approves a request.
    // -----------------------------------------------------------------------

    [HttpPost("api/v3/movie")]
    [ProducesResponseType<RadarrMovie>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddMovie(
        [FromBody] RadarrAddMovieDto body,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized()) return Unauthorized401();

        var tmdbIdStr = body.TmdbId.ToString(CultureInfo.InvariantCulture);

        // Idempotent — return existing record if already tracked.
        var existing = await FindByTmdbIdAsync(body.TmdbId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Radarr compat: movie tmdb:{TmdbId} already tracked as request {RequestId}",
                body.TmdbId, existing.RequestId);
            return Ok(ToRadarrMovie(existing));
        }

        _logger.LogInformation(
            "Radarr compat: creating request for movie tmdb:{TmdbId} ({Title})",
            body.TmdbId, body.Title);

        RequestMetadata metadata;
        try
        {
            metadata = await _tmdbService
                .GetMetadataAsync(tmdbIdStr, RequestMediaType.Movie, null, null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            metadata = new RequestMetadata(body.Title, null);
        }

        var requestKey = RequestWorkflowService.BuildRequestKey(tmdbIdStr, RequestMediaType.Movie, null, null);
        var record = await _repository
            .CreateOrGetActiveAsync(
                JellyseerrUserId,
                tmdbIdStr,
                RequestMediaType.Movie,
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
        return StatusCode(StatusCodes.Status201Created, ToRadarrMovie(result));
    }

    // -----------------------------------------------------------------------
    // Update movie — Jellyseerr calls this to flip monitored flag.
    // -----------------------------------------------------------------------

    [HttpPut("api/v3/movie")]
    [ProducesResponseType<RadarrMovie>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMovie(
        [FromBody] RadarrUpdateMovieDto body,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized()) return Unauthorized401();

        var record = await FindByTmdbIdAsync(body.TmdbId, cancellationToken).ConfigureAwait(false);
        return record is null ? NotFound() : Ok(ToRadarrMovie(record));
    }

    // -----------------------------------------------------------------------
    // Delete movie — Jellyseerr calls this to withdraw a request.
    // -----------------------------------------------------------------------

    [HttpDelete("api/v3/movie/{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMovie(int id, CancellationToken cancellationToken)
    {
        if (!IsAuthorized()) return Unauthorized401();

        var record = await FindByTmdbIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        await _repository
            .SetRejectedAsync(record.RequestId, reason: "Withdrawn via Jellyseerr", cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Radarr compat: request {RequestId} (tmdb:{TmdbId}) withdrawn via Jellyseerr",
            record.RequestId, id);

        return Ok();
    }

    // -----------------------------------------------------------------------
    // Command — Jellyseerr sends "MoviesSearch" after adding.
    // We log it; stream searching will be wired to Gelato integration later.
    // -----------------------------------------------------------------------

    [HttpPost("api/v3/command")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public IActionResult SendCommand([FromBody] RadarrCommandDto body)
    {
        if (!IsAuthorized()) return Unauthorized401();

        _logger.LogInformation(
            "Radarr compat: received command '{Name}' for movies [{Ids}]",
            body.Name,
            body.MovieIds is null ? "" : string.Join(", ", body.MovieIds));

        return StatusCode(StatusCodes.Status201Created, new { id = 1, name = body.Name, status = "queued" });
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<MediaRequestRecord?> FindByTmdbIdAsync(int tmdbId, CancellationToken cancellationToken)
    {
        var tmdbIdStr = tmdbId.ToString(CultureInfo.InvariantCulture);
        var all = await _repository
            .GetByStatusAsync(RequestStatus.All, cancellationToken)
            .ConfigureAwait(false);

        return all.FirstOrDefault(r =>
            string.Equals(r.TmdbId, tmdbIdStr, StringComparison.Ordinal) &&
            string.Equals(r.MediaType, RequestMediaType.Movie, StringComparison.OrdinalIgnoreCase));
    }

    private static RadarrMovie ToRadarrMovie(MediaRequestRecord r)
    {
        var tmdbId = int.TryParse(r.TmdbId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id : 0;

        var hasFile = string.Equals(r.Status, RequestStatus.Ready, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(r.StreamUrl);

        RadarrMovieFile? movieFile = hasFile
            ? new RadarrMovieFile(
                Id: tmdbId,
                MovieId: tmdbId,
                Size: 0,
                DateAdded: DateTimeOffset.FromUnixTimeMilliseconds(r.UpdatedAt).ToString("o", CultureInfo.InvariantCulture),
                QualityCutoffNotMet: false)
            : null;

        return new RadarrMovie(
            Id: tmdbId,
            Title: r.Title,
            IsAvailable: hasFile,
            Monitored: !string.Equals(r.Status, RequestStatus.Rejected, StringComparison.OrdinalIgnoreCase),
            TmdbId: tmdbId,
            ImdbId: string.Empty,
            TitleSlug: BuildTitleSlug(r.Title, tmdbId),
            FolderName: string.Empty,
            Path: string.Empty,
            ProfileId: QualityProfileId,
            QualityProfileId: QualityProfileId,
            Added: DateTimeOffset.FromUnixTimeMilliseconds(r.RequestedAt).ToString("o", CultureInfo.InvariantCulture),
            HasFile: hasFile,
            Tags: Array.Empty<int>(),
            MovieFile: movieFile);
    }

    private static string BuildTitleSlug(string title, int tmdbId)
    {
        var safe = new string(title
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray())
            .Trim('-');
        return $"{safe}-{tmdbId.ToString(CultureInfo.InvariantCulture)}";
    }
}
