# Source Manager

Jellyfin plugin (.NET 9, GPL-3.0) that acts as the backend for a self-hosted movie/series request and delivery pipeline.
Current version: **1.0.8.0** — targeting Jellyfin 10.11.x (`targetAbi: 10.11.0.0`).

---

## Architecture overview

```
User (Jellyseerr UI or client app)
    │ request movie / series
    ▼
Jellyseerr  ──approve──►  Source Manager (fake Radarr / Sonarr)
                                │
                          SQLite request record  (status: processing)
                                │
                    SourceResolutionService (background, every 60 s)
                          │            │           │
                    KkPhimResolver  OPhimResolver  YtsResolver
                          │            │           │
                       m3u8 URL    m3u8 URL    magnet link
                          │            │           │
                    .strm file    .strm file   qBittorrent
                          │            │           │
                    Jellyfin library scan ◄─────────┘
                          │
                   status: ready  ──SSE──► client notified
```

### User-facing roles

| Component | Role |
|---|---|
| **Jellyseerr** | Request UI for users, approval UI for admins |
| **Source Manager** | Backend: receives approved requests, finds streams, writes files |
| **Jellyfin** | Media server, scans library, serves video |
| **qBittorrent** | Optional torrent download client |

---

## Request lifecycle

```
pending  →  processing  →  ready
                │
                └──  rejected  (at any point)
```

| Status | When |
|---|---|
| `pending` | Request created but not yet approved |
| `processing` | Approved (Jellyseerr → fake Radarr/Sonarr POST), stream URL not yet found |
| `ready` | `.strm` written + Jellyfin library item found and matched by TMDB ID |
| `rejected` | Admin rejected, or withdrawn via Jellyseerr DELETE |

---

## Jellyseerr integration (fake Radarr + Sonarr)

Jellyseerr is configured with **two fake service instances**:

### Radarr — movies
- URL: `http://<jellyfin-host>:8096/SourceManager/radarr`
- API key: `PluginConfiguration.RadarrApiKey` (auto-generated GUID, shown in dashboard)
- Identifier: TMDB ID (stored in `tmdb_id` column, `media_type = 'movie'`)

### Sonarr — TV series
- URL: `http://<jellyfin-host>:8096/SourceManager/sonarr`
- API key: `PluginConfiguration.SonarrApiKey` (auto-generated GUID, shown in dashboard)
- Identifier: TVDB ID (stored in `tmdb_id` column, `media_type = 'series'`)
  > Note: for series the `tmdb_id` column stores the TVDB ID — Sonarr uses TVDB, not TMDB. The column is reused to avoid a schema change.

### Jellyseerr setup steps (in Jellyseerr UI)
1. Settings → Services → Radarr → Add Radarr Server → paste Radarr URL + API key
2. Quality Profile: **Source Manager** · Root Folder: your Movies library path
3. Settings → Services → Sonarr → Add Sonarr Server → paste Sonarr URL + API key
4. Quality Profile: **Source Manager** · Language Profile: **Any** · Root Folder: your Shows library path
5. Test Connection → Save (both)

Both real Radarr and fake Radarr can coexist in Jellyseerr — admin chooses which to route each request to.

---

## Automatic source resolution

`SourceResolutionService` (`BackgroundService`) runs every **60 seconds**, picks up all `processing` requests with `stream_url IS NULL`, and tries resolvers in order:

### 1. KkPhimResolver — `phimapi.com`
- Searches by title, verifies by TMDB ID (`movie.tmdb.id` field in API response)
- Returns first `link_m3u8` URL → written as `.strm` file
- Movies only (series support limited)

### 2. OPhimResolver — `ophim1.com`
- Same API format as KKPhim (both use `PhimApiResolverBase`)
- Fallback if KKPhim doesn't have the title

### 3. YtsResolver — `yts.mx`
- Movies only
- Picks best quality torrent (2160p > 1080p > 720p), sorted by seeds
- Returns magnet link → sent to qBittorrent Web API
- Requires `QBittorrentUrl` to be configured

If no resolver finds a source: request stays in `processing`, admin supplies URL manually via `POST /SourceManager/Admin/Requests/{id}/Approve` with `{ "streamUrl": "..." }`.

---

## .strm file placement

`LibraryPathService` resolves the physical library path:
1. **Plugin config override** (`MovieLibraryPath` / `ShowLibraryPath`) — if set, takes priority
2. **Auto-discovery** via `ILibraryManager.GetVirtualFolders()` — finds the Jellyfin library with `CollectionType = "movies"` or `"tvshows"` and returns its first `Locations` path
3. **Fallback** `/data/strm/movies` or `/data/strm/shows`

File naming follows Jellyfin conventions:
```
{moviePath}/{Title}/{Title}.strm
{showPath}/{Title}/Season NN/{Title} - SNNENN.strm
```

On rejection, the `.strm` file is deleted and the now-empty parent folder is removed.

---

## Source Manager native API (for direct client integrations)

These exist for clients that talk directly to Source Manager (pre-Jellyseerr flow). With Jellyseerr as the front-end, they are not used but remain functional.

```
GET  /SourceManager/Capabilities
POST /SourceManager/Request           { tmdbId, mediaType, userId, seasonNumber?, episodeNumber? }
GET  /SourceManager/Requests?userId=…
GET  /SourceManager/Requests/Events?userId=…   (SSE stream)
GET  /SourceManager/Admin/Requests?status=…
POST /SourceManager/Admin/Requests/{id}/Approve  { streamUrl? }
POST /SourceManager/Admin/Requests/{id}/Reject   { reason? }
POST /SourceManager/Admin/Requests/{id}/Refresh
```

`mediaType` values: `movie` | `series` | `episode`

---

## Fake Radarr API endpoints (`/SourceManager/radarr/api/v3/…`)

All require `X-Api-Key: <RadarrApiKey>` header (or `?apikey=`).

| Method | Path | Description |
|---|---|---|
| GET | `system/status` | Returns version 5.0.0.0 |
| GET | `qualityProfile` | Returns `[{ id:1, name:"Source Manager" }]` |
| GET | `rootfolder` | Returns discovered Movies library path |
| GET/POST/PUT | `tag`, `tag/{id}` | Stateless echo |
| GET | `queue` | Always empty |
| GET | `movie/lookup?term=tmdb:{id}` | Checks DB; returns id=0 if not tracked |
| GET | `movie` | All movie requests as RadarrMovie |
| GET | `movie/{id}` | Single movie by TMDB ID |
| POST | `movie` | Approve trigger → creates request, advances to processing |
| PUT | `movie` | Noop — returns existing record |
| DELETE | `movie/{id}` | Calls SetRejectedAsync("Withdrawn via Jellyseerr") |
| POST | `command` | Logs command; returns `{ status:"queued" }` |

---

## Fake Sonarr API endpoints (`/SourceManager/sonarr/api/v3/…`)

All require `X-Api-Key: <SonarrApiKey>`.

| Method | Path | Description |
|---|---|---|
| GET | `system/status` | Returns version 4.0.0.0 |
| GET | `qualityProfile` | Returns `[{ id:1, name:"Source Manager" }]` |
| GET | `languageProfile` | Returns `[{ id:1, name:"Any" }]` |
| GET | `rootfolder` | Returns discovered TV Shows library path |
| GET/POST/PUT | `tag`, `tag/{id}` | Stateless echo |
| GET | `series/lookup?term=tvdb:{id}` | Checks DB; empty if not tracked |
| GET | `series` | All series requests |
| GET | `series/{id}` | Single series by TVDB ID |
| POST | `series` | Approve trigger → creates request, advances to processing |
| PUT | `series/{id}` | Noop — returns existing record |
| DELETE | `series/{id}` | SetRejectedAsync("Withdrawn via Jellyseerr") |
| POST | `command` | Logs command; returns `{ status:"queued" }` |
| GET | `episode?seriesId={id}` | Always returns `[]` |
| GET | `episodefile?seriesId={id}` | Always returns `[]` |

---

## SQLite schema

Database: `{JellyfinData}/source-manager/source-requests.db`

```sql
CREATE TABLE media_requests (
    request_id      TEXT PRIMARY KEY,
    user_id         TEXT NOT NULL,
    tmdb_id         TEXT NOT NULL,   -- TMDB ID for movies/episodes; TVDB ID for series
    media_type      TEXT NOT NULL,   -- 'movie' | 'series' | 'episode'
    season_number   INTEGER,
    episode_number  INTEGER,
    request_key     TEXT NOT NULL,   -- unique dedup key: 'movie:{id}', 'series:{id}', 'episode:{id}:sX:eY'
    title           TEXT NOT NULL,
    poster_url      TEXT,
    status          TEXT NOT NULL,   -- pending | processing | ready | rejected
    requested_at    INTEGER NOT NULL, -- Unix ms
    updated_at      INTEGER NOT NULL,
    jellyfin_item_id TEXT,           -- set when status=ready (Jellyfin item GUID)
    reject_reason   TEXT,
    stream_url      TEXT             -- m3u8 URL, .strm path, or magnet link
);
```

Migrations run at startup using `pragma_table_info` checks (no migration library).

---

## Plugin configuration fields (`PluginConfiguration`)

| Field | Default | Description |
|---|---|---|
| `TmdbApiKey` | null | Override TMDB API key (uses built-in key if null) |
| `TmdbPosterSize` | `w500` | TMDB image width (`w185` `w342` `w500` `original`) |
| `MovieLibraryPath` | null | Override path for Movies library (auto-discovered if null) |
| `ShowLibraryPath` | null | Override path for TV Shows library (auto-discovered if null) |
| `EnableAutoResolution` | `true` | Run SourceResolutionService background loop |
| `QBittorrentUrl` | null | qBittorrent Web API base URL, e.g. `http://localhost:8080` |
| `QBittorrentUsername` | `admin` | qBittorrent login |
| `QBittorrentPassword` | null | qBittorrent login |
| `RadarrApiKey` | auto-GUID | Shared secret for Radarr compat endpoint |
| `SonarrApiKey` | auto-GUID | Shared secret for Sonarr compat endpoint |
| `StrmLibraryPath` | null | **Deprecated** — use `MovieLibraryPath`/`ShowLibraryPath` |

---

## Code structure

```
Jellyfin.Plugin.SourceManager/
├── Configuration/
│   └── PluginConfiguration.cs
├── Controllers/
│   ├── RadarrCompatController.cs   # /SourceManager/radarr
│   ├── SonarrCompatController.cs   # /SourceManager/sonarr
│   └── SourceManagerController.cs  # /SourceManager/* (native API)
├── Models/
│   ├── RadarrDtos.cs               # Radarr API v3 DTOs
│   ├── SonarrDtos.cs               # Sonarr API v3 DTOs
│   ├── RequestDtos.cs              # MediaRequestRecord, MediaRequestDto, etc.
│   ├── RequestMediaType.cs         # "movie" | "series" | "episode"
│   └── RequestStatus.cs            # "pending" | "processing" | "ready" | "rejected"
├── Services/
│   ├── IRequestRepository.cs
│   ├── SqliteRequestRepository.cs  # SQLite impl (lazy init + migrations)
│   ├── LibraryPathService.cs       # Discovers Jellyfin library paths via ILibraryManager
│   ├── LibraryRequestMatcher.cs    # Matches Jellyfin library items to requests by TMDB ID
│   ├── LibraryMonitorService.cs    # IHostedService: listens for Jellyfin item added events
│   ├── RequestWorkflowService.cs   # Approve/reject/refresh business logic
│   ├── RequestEventBroker.cs       # Fan-out SSE events per userId (Channel<T>)
│   ├── StrmWriterService.cs        # Writes/deletes .strm files in library folders
│   ├── TmdbMetadataService.cs      # TMDB API calls (title, poster)
│   ├── SourceResolutionService.cs  # BackgroundService orchestrator
│   └── SourceResolution/
│       ├── ISourceResolver.cs      # interface + SourceResult(Kind, Value)
│       ├── PhimApiResolverBase.cs  # shared base for KKPhim + OPhim
│       ├── KkPhimResolver.cs       # phimapi.com
│       ├── OPhimResolver.cs        # ophim1.com
│       ├── YtsResolver.cs          # yts.mx → magnet
│       └── QBittorrentClient.cs    # qBittorrent Web API v2
├── Support/
│   ├── JellyfinAuthorizationPolicies.cs
│   └── JellyfinClaimsPrincipalExtensions.cs
├── Pages/
│   └── SourceManager.html          # Plugin dashboard page (config UI)
├── Plugin.cs                       # BasePlugin entry point
└── PluginServiceRegistrator.cs     # DI registrations
```

---

## Build & release

```bash
# Local build
dotnet build Jellyfin.Plugin.SourceManager.sln -c Release

# Full release (build + zip + update manifest.json)
make release VERSION=1.0.8.0

# CI release (GitHub Actions → builds ZIP, creates GitHub release, updates manifest)
gh workflow run release.yml --field version=1.0.8.0
```

The `Release` workflow:
1. Restores + builds + publishes
2. Zips into `dist/source-manager-{VERSION}.zip`
3. Updates `manifest.json` checksum
4. Commits manifest back to `main`
5. Creates / updates GitHub release asset

---

## Manual install

1. Stop Jellyfin
2. Create folder: `{JellyfinData}/plugins/Source Manager_{VERSION}/`
3. Copy DLL + deps from publish output into that folder
4. Start Jellyfin

---

## Jellyfin catalog install

Add repository in Dashboard → Plugins → Repositories:

```
https://raw.githubusercontent.com/jellystrm/source-manager/main/manifest.json
```

---

## Known gaps / next work

- **Series stream resolution**: KKPhim/OPhim resolvers return one URL (series-level). Proper per-episode `.strm` writing (one file per episode) is not yet implemented.
- **Year in file paths**: Jellyfin prefers `Title (Year)/Title (Year).strm` for disambiguation. Year is not currently stored in the DB or included in paths.
- **Series TVDB→TMDB bridge**: Sonarr uses TVDB IDs; the TMDB metadata service is not called for series coming from Jellyseerr. Poster URLs are missing for those requests.
- **qBittorrent download completion tracking**: After sending a magnet, the plugin does not poll qBittorrent for completion — the request stays in `processing` until Jellyfin's library scan picks up the downloaded file.
- **Jellyseerr client-side integration**: Streamyfin or a custom app can integrate Jellyseerr via its REST API (`POST /api/v1/request`, `GET /api/v1/search`, auth via `POST /api/v1/auth/jellyfin`).
