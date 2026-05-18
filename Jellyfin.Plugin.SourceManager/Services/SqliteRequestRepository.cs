using System.Globalization;
using Jellyfin.Plugin.SourceManager.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.SourceManager.Services;

public sealed class SqliteRequestRepository : IRequestRepository, IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _databaseLock = new(1, 1);
    private bool _initialized;

    public SqliteRequestRepository(IApplicationPaths applicationPaths)
    {
        var dataPath = Path.Combine(applicationPaths.DataPath, "source-manager");
        Directory.CreateDirectory(dataPath);

        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataPath, "source-requests.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        _connectionString = connectionStringBuilder.ToString();
    }

    public async Task<MediaRequestRecord> CreateOrGetActiveAsync(
        string userId,
        string tmdbId,
        string mediaType,
        int? seasonNumber,
        int? episodeNumber,
        string requestKey,
        RequestMetadata metadata,
        string? tvdbId,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _databaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            var existing = await GetActiveByRequestKeyAsync(connection, userId, requestKey, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var requestId = Guid.NewGuid().ToString("N");

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO media_requests (
                    request_id,
                    user_id,
                    tmdb_id,
                    media_type,
                    season_number,
                    episode_number,
                    request_key,
                    title,
                    poster_url,
                    status,
                    requested_at,
                    updated_at,
                    jellyfin_item_id,
                    reject_reason,
                    stream_url,
                    tvdb_id)
                VALUES (
                    $request_id,
                    $user_id,
                    $tmdb_id,
                    $media_type,
                    $season_number,
                    $episode_number,
                    $request_key,
                    $title,
                    $poster_url,
                    $status,
                    $requested_at,
                    $updated_at,
                    NULL,
                    NULL,
                    NULL,
                    $tvdb_id);
                """;
            AddParameter(command, "$request_id", requestId);
            AddParameter(command, "$user_id", userId);
            AddParameter(command, "$tmdb_id", tmdbId);
            AddParameter(command, "$media_type", mediaType);
            AddParameter(command, "$season_number", seasonNumber);
            AddParameter(command, "$episode_number", episodeNumber);
            AddParameter(command, "$request_key", requestKey);
            AddParameter(command, "$title", metadata.Title);
            AddParameter(command, "$poster_url", metadata.PosterUrl);
            AddParameter(command, "$status", RequestStatus.Pending);
            AddParameter(command, "$requested_at", now);
            AddParameter(command, "$updated_at", now);
            AddParameter(command, "$tvdb_id", tvdbId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return (await GetByIdCoreAsync(connection, requestId, cancellationToken).ConfigureAwait(false))!;
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    public async Task<IReadOnlyList<MediaRequestRecord>> GetByUserAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE user_id = $user_id ORDER BY requested_at DESC";
        AddParameter(command, "$user_id", userId);
        return await QueryAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MediaRequestRecord>> GetByStatusAsync(string status, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        if (string.Equals(status, RequestStatus.All, StringComparison.OrdinalIgnoreCase))
        {
            command.CommandText = SelectSql + " ORDER BY requested_at DESC";
        }
        else
        {
            command.CommandText = SelectSql + " WHERE status = $status ORDER BY requested_at DESC";
            AddParameter(command, "$status", status);
        }

        return await QueryAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MediaRequestRecord?> GetByIdAsync(string requestId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await GetByIdCoreAsync(connection, requestId, cancellationToken).ConfigureAwait(false);
    }

    public Task<MediaRequestRecord?> SetProcessingAsync(string requestId, string? streamUrl, CancellationToken cancellationToken)
        => UpdateStatusAsync(
            requestId,
            RequestStatus.Processing,
            requiredCurrentStatus: RequestStatus.Pending,
            jellyfinItemId: null,
            rejectReason: null,
            clearRejectReason: true,
            streamUrl: streamUrl,
            cancellationToken);

    public Task<MediaRequestRecord?> SetRejectedAsync(string requestId, string? reason, CancellationToken cancellationToken)
        => UpdateStatusAsync(
            requestId,
            RequestStatus.Rejected,
            requiredCurrentStatus: null,
            jellyfinItemId: null,
            rejectReason: reason,
            clearRejectReason: false,
            streamUrl: null,
            cancellationToken);

    public Task<MediaRequestRecord?> SetReadyAsync(string requestId, string jellyfinItemId, CancellationToken cancellationToken)
        => UpdateStatusAsync(
            requestId,
            RequestStatus.Ready,
            requiredCurrentStatus: RequestStatus.Processing,
            jellyfinItemId,
            rejectReason: null,
            clearRejectReason: true,
            streamUrl: null,
            cancellationToken);

    public async Task<IReadOnlyList<MediaRequestRecord>> GetProcessingByContentAsync(
        string tmdbId,
        string mediaType,
        int? seasonNumber,
        int? episodeNumber,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectSql + """
             WHERE status = $status
               AND tmdb_id = $tmdb_id
               AND media_type = $media_type
               AND ($season_number IS NULL OR season_number = $season_number)
               AND ($episode_number IS NULL OR episode_number = $episode_number)
             ORDER BY requested_at ASC
            """;
        AddParameter(command, "$status", RequestStatus.Processing);
        AddParameter(command, "$tmdb_id", tmdbId);
        AddParameter(command, "$media_type", mediaType);
        AddParameter(command, "$season_number", seasonNumber);
        AddParameter(command, "$episode_number", episodeNumber);
        return await QueryAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateStreamUrlAsync(string requestId, string streamUrl, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _databaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE media_requests
                SET stream_url = $stream_url,
                    updated_at = $updated_at
                WHERE request_id = $request_id
                """;
            AddParameter(command, "$request_id", requestId);
            AddParameter(command, "$stream_url", streamUrl);
            AddParameter(command, "$updated_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    public void Dispose()
    {
        _databaseLock.Dispose();
    }

    private const string SelectSql = """
        SELECT
            request_id,
            user_id,
            tmdb_id,
            media_type,
            season_number,
            episode_number,
            request_key,
            title,
            poster_url,
            status,
            requested_at,
            updated_at,
            jellyfin_item_id,
            reject_reason,
            stream_url,
            tvdb_id
        FROM media_requests
        """;

    private async Task<MediaRequestRecord?> UpdateStatusAsync(
        string requestId,
        string status,
        string? requiredCurrentStatus,
        string? jellyfinItemId,
        string? rejectReason,
        bool clearRejectReason,
        string? streamUrl,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _databaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE media_requests
                SET status = $status,
                    updated_at = $updated_at,
                    jellyfin_item_id = COALESCE($jellyfin_item_id, jellyfin_item_id),
                    reject_reason = CASE WHEN $clear_reject_reason THEN NULL ELSE $reject_reason END,
                    stream_url = COALESCE($stream_url, stream_url)
                WHERE request_id = $request_id
                  AND ($required_current_status IS NULL OR status = $required_current_status)
                """;
            AddParameter(command, "$request_id", requestId);
            AddParameter(command, "$status", status);
            AddParameter(command, "$required_current_status", requiredCurrentStatus);
            AddParameter(command, "$updated_at", now);
            AddParameter(command, "$jellyfin_item_id", jellyfinItemId);
            AddParameter(command, "$reject_reason", rejectReason);
            AddParameter(command, "$clear_reject_reason", clearRejectReason);
            AddParameter(command, "$stream_url", streamUrl);
            var rows = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (rows == 0)
            {
                return null;
            }

            return await GetByIdCoreAsync(connection, requestId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _databaseLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            await using var createCmd = connection.CreateCommand();
            createCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS media_requests (
                    request_id TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    tmdb_id TEXT NOT NULL,
                    media_type TEXT NOT NULL,
                    season_number INTEGER,
                    episode_number INTEGER,
                    request_key TEXT NOT NULL,
                    title TEXT NOT NULL,
                    poster_url TEXT,
                    status TEXT NOT NULL,
                    requested_at INTEGER NOT NULL,
                    updated_at INTEGER NOT NULL,
                    jellyfin_item_id TEXT,
                    reject_reason TEXT,
                    stream_url TEXT,
                    tvdb_id TEXT
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ux_media_requests_active
                    ON media_requests(user_id, request_key)
                    WHERE status <> 'rejected';

                CREATE INDEX IF NOT EXISTS ix_media_requests_user
                    ON media_requests(user_id, requested_at DESC);

                CREATE INDEX IF NOT EXISTS ix_media_requests_status
                    ON media_requests(status, requested_at DESC);

                CREATE INDEX IF NOT EXISTS ix_media_requests_processing_lookup
                    ON media_requests(status, tmdb_id, media_type, season_number, episode_number);
                """;
            await createCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            // Migration: add stream_url column to existing databases that predate this column.
            await using var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('media_requests') WHERE name = 'stream_url'";
            var count = (long)(await checkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            if (count == 0)
            {
                await using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE media_requests ADD COLUMN stream_url TEXT";
                await alterCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // Migration: add tvdb_id column. Series now store the real TMDB id in
            // tmdb_id; tvdb_id holds the Jellyseerr/Sonarr TVDB id for lookup/delete.
            await using var tvdbCheckCmd = connection.CreateCommand();
            tvdbCheckCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('media_requests') WHERE name = 'tvdb_id'";
            var tvdbCount = (long)(await tvdbCheckCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            if (tvdbCount == 0)
            {
                await using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE media_requests ADD COLUMN tvdb_id TEXT";
                await alterCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            _initialized = true;
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    private async Task<MediaRequestRecord?> GetActiveByRequestKeyAsync(
        SqliteConnection connection,
        string userId,
        string requestKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = SelectSql + """
             WHERE user_id = $user_id
               AND request_key = $request_key
               AND status <> $rejected
             ORDER BY requested_at ASC
             LIMIT 1
            """;
        AddParameter(command, "$user_id", userId);
        AddParameter(command, "$request_key", requestKey);
        AddParameter(command, "$rejected", RequestStatus.Rejected);
        return (await QueryAsync(command, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    private async Task<MediaRequestRecord?> GetByIdCoreAsync(
        SqliteConnection connection,
        string requestId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE request_id = $request_id LIMIT 1";
        AddParameter(command, "$request_id", requestId);
        return (await QueryAsync(command, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task<IReadOnlyList<MediaRequestRecord>> QueryAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var results = new List<MediaRequestRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var seasonNumberIsNull = await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false);
            var episodeNumberIsNull = await reader.IsDBNullAsync(5, cancellationToken).ConfigureAwait(false);
            var posterUrlIsNull = await reader.IsDBNullAsync(8, cancellationToken).ConfigureAwait(false);
            var jellyfinItemIdIsNull = await reader.IsDBNullAsync(12, cancellationToken).ConfigureAwait(false);
            var rejectReasonIsNull = await reader.IsDBNullAsync(13, cancellationToken).ConfigureAwait(false);
            var streamUrlIsNull = await reader.IsDBNullAsync(14, cancellationToken).ConfigureAwait(false);
            var tvdbIdIsNull = await reader.IsDBNullAsync(15, cancellationToken).ConfigureAwait(false);

            results.Add(new MediaRequestRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                seasonNumberIsNull ? null : reader.GetInt32(4),
                episodeNumberIsNull ? null : reader.GetInt32(5),
                reader.GetString(6),
                reader.GetString(7),
                posterUrlIsNull ? null : reader.GetString(8),
                reader.GetString(9),
                reader.GetInt64(10),
                reader.GetInt64(11),
                jellyfinItemIdIsNull ? null : reader.GetString(12),
                rejectReasonIsNull ? null : reader.GetString(13),
                streamUrlIsNull ? null : reader.GetString(14),
                tvdbIdIsNull ? null : reader.GetString(15)));
        }

        return results;
    }

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
