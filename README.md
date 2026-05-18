# Jellystrm

Jellystrm is a Jellyfin plugin that supports the Jellystrm client request workflow. It accepts authenticated user requests, lets admins approve or reject them, persists state in plugin-owned SQLite storage, emits updates over SSE, and maps approved requests to Jellyfin library items by TMDB provider IDs.

## Current Capabilities

- `GET /Jellystrm/Capabilities`
- `POST /Jellystrm/Request`
- `GET /Jellystrm/Requests?userId=...`
- `GET /Jellystrm/Requests/Events?userId=...`
- `GET /Jellystrm/Admin/Requests?status=...`
- `POST /Jellystrm/Admin/Requests/{requestId}/Approve`
- `POST /Jellystrm/Admin/Requests/{requestId}/Reject`
- `POST /Jellystrm/Admin/Requests/{requestId}/Refresh`

The plugin supports `movie`, `series`, and `episode` requests. Episode requests use the series TMDB id plus `seasonNumber` and `episodeNumber`.

## Standalone Build

Build from the repository root:

```bash
dotnet restore Jellyfin.Plugin.Jellystrm.sln
dotnet publish Jellyfin.Plugin.Jellystrm/Jellyfin.Plugin.Jellystrm.csproj -c Release
```

For local development inside a Jellyfin source checkout, the project uses local Jellyfin project references when they are available. Outside the source checkout, it falls back to Jellyfin package references.

## Manual Install

1. Stop Jellyfin.
2. Create a versioned plugin folder under the Jellyfin plugin data directory, for example:

```text
/config/plugins/Jellystrm_1.0.0.0/
```

3. Copy the publish output into that folder.
4. Start Jellyfin.

The plugin configuration is stored by Jellyfin in the normal plugin configuration directory. Request state is stored in Jellyfin data under `data/jellystrm/source-requests.db`.

## Catalog Packaging

Add this repository URL in Jellyfin Dashboard -> Plugins -> Repositories:

```text
https://raw.githubusercontent.com/jellystrm/source-manager/main/manifest.json
```

That URL loads this plugin catalog. Each manifest entry points Jellyfin to the matching GitHub release ZIP, for example `jellystrm-1.0.0.0.zip`.

Use `build.yaml` with JPRM to produce the plugin ZIP and repository manifest:

```bash
python -m jprm plugin build --output ./build
```

`manifest.template.json` is only a hand-editable placeholder. Prefer the JPRM-generated manifest for releases.

The repository also includes Streamyfin-style release helpers:

```bash
make release VERSION=1.0.0.0
```

This restores, builds, publishes, zips the plugin into `dist/`, and updates `manifest.json` with the generated checksum.

## Repository Layout

```text
.
├── Jellyfin.Plugin.Jellystrm.sln
├── Jellyfin.Plugin.Jellystrm/
│   ├── Jellyfin.Plugin.Jellystrm.csproj
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

The jellystrm layer will add authorized source providers, stream URL refresh, generated-file manifests, and safe STRM writing into configured Jellyfin library roots.
