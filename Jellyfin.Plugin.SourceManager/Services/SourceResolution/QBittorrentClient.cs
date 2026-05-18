using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SourceManager.Services.SourceResolution;

/// <summary>
/// Thin wrapper around the qBittorrent Web API v2.
/// Session cookie is cached per-instance and refreshed on 403.
/// </summary>
public sealed class QBittorrentClient
{
    private readonly IHttpClientFactory _factory;
    private readonly ILogger<QBittorrentClient> _logger;
    private string? _sessionCookie;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Plugin.Instance?.Configuration.QBittorrentUrl);

    public QBittorrentClient(IHttpClientFactory factory, ILogger<QBittorrentClient> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task AddTorrentAsync(string magnetUrl, string savePath, CancellationToken cancellationToken)
    {
        await EnsureLoggedInAsync(cancellationToken).ConfigureAwait(false);

        using var http = CreateClient();
        var content = new MultipartFormDataContent
        {
            { new StringContent(magnetUrl), "urls" },
            { new StringContent(savePath), "savepath" },
            { new StringContent("source-manager"), "category" }
        };

        var response = await http.PostAsync("/api/v2/torrents/add", content, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            // Session expired — re-login once.
            _sessionCookie = null;
            await EnsureLoggedInAsync(cancellationToken).ConfigureAwait(false);
            using var http2 = CreateClient();
            response = await http2.PostAsync("/api/v2/torrents/add", content, cancellationToken)
                .ConfigureAwait(false);
        }

        response.EnsureSuccessStatusCode();
        _logger.LogInformation("qBittorrent: torrent queued → {SavePath}", savePath);
    }

    private async Task EnsureLoggedInAsync(CancellationToken cancellationToken)
    {
        if (_sessionCookie is not null) return;

        var config = Plugin.Instance!.Configuration;
        using var http = CreateClient();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = config.QBittorrentUsername ?? "admin",
            ["password"] = config.QBittorrentPassword ?? string.Empty
        });

        var response = await http.PostAsync("/api/v2/auth/login", form, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        _sessionCookie = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault()
            : null;

        _logger.LogDebug("qBittorrent: logged in");
    }

    private HttpClient CreateClient()
    {
        var http = _factory.CreateClient();
        var baseUrl = Plugin.Instance?.Configuration.QBittorrentUrl ?? "http://localhost:8080";
        http.BaseAddress = new Uri(baseUrl);
        http.Timeout = TimeSpan.FromSeconds(10);

        if (_sessionCookie is not null)
        {
            http.DefaultRequestHeaders.Add("Cookie", _sessionCookie);
        }

        return http;
    }
}
