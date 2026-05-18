# Source Manager

Source Manager is a Jellyfin plugin that supports the Source Manager client request workflow. It accepts authenticated user requests, lets admins approve or reject them, persists state in plugin-owned SQLite storage, emits updates over SSE, and maps approved requests to Jellyfin library items by TMDB provider IDs.

## Current Capabilities

- `GET /SourceManager/Capabilities`
- `POST /SourceManager/Request`
- `GET /SourceManager/Requests?userId=...`
- `GET /SourceManager/Requests/Events?userId=...`
- `GET /SourceManager/Admin/Requests?status=...`
- `POST /SourceManager/Admin/Requests/{requestId}/Approve`
- `POST /SourceManager/Admin/Requests/{requestId}/Reject`
- `POST /SourceManager/Admin/Requests/{requestId}/Refresh`

The plugin supports `movie`, `series`, and `episode` requests. Episode requests use the series TMDB id plus `seasonNumber` and `episodeNumber`.

## Standalone Build

Build from the repository root:

```bash
dotnet restore Jellyfin.Plugin.SourceManager.sln
dotnet publish Jellyfin.Plugin.SourceManager/Jellyfin.Plugin.SourceManager.csproj -c Release
```

For local development inside a Jellyfin source checkout, the project uses local Jellyfin project references when they are available. Outside the source checkout, it falls back to Jellyfin package references.

## Manual Install

1. Stop Jellyfin.
2. Create a versioned plugin folder under the Jellyfin plugin data directory, for example:

```text
/config/plugins/Source Manager_1.0.2.0/
```

3. Copy the publish output into that folder.
4. Start Jellyfin.

The plugin configuration is stored by Jellyfin in the normal plugin configuration directory. Request state is stored in Jellyfin data under `data/source-manager/source-requests.db`.

## Catalog Packaging

Add this repository URL in Jellyfin Dashboard -> Plugins -> Repositories:

```text
https://raw.githubusercontent.com/jellystrm/plugins/main/manifest.json
```

That URL loads this plugin catalog. Each manifest entry points Jellyfin to the matching GitHub release ZIP, for example `source-manager-1.0.0.0.zip`.

Use the `Release` GitHub Actions workflow to publish a version. It builds the plugin ZIP, updates `manifest.json` with the generated checksum, pushes the manifest to `main`, and creates or updates the matching GitHub release asset.

Run it from GitHub Actions with a version such as:

```text
1.0.0.0
```

For local verification before running CI/CD:

```bash
make release VERSION=1.0.0.0
```

This restores, builds, publishes, zips the plugin into `dist/`, and updates local `manifest.json` with the generated checksum.

## Repository Layout

```text
.
├── Jellyfin.Plugin.SourceManager.sln
├── Jellyfin.Plugin.SourceManager/
│   ├── Jellyfin.Plugin.SourceManager.csproj
│   ├── Plugin.cs
│   ├── PluginServiceRegistrator.cs
│   ├── Controllers/
│   ├── Models/
│   ├── Services/
│   └── Support/
├── manifest.json
├── build.yaml
├── Makefile
└── scripts/
```

## Phase 2 Direction

The source-manager layer will add authorized source providers, stream URL refresh, generated-file manifests, and safe STRM writing into configured Jellyfin library roots.
