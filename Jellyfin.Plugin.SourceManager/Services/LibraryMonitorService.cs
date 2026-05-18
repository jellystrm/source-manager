using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SourceManager.Services;

public sealed class LibraryMonitorService : IHostedService, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly LibraryRequestMatcher _libraryRequestMatcher;
    private readonly RequestWorkflowService _requestWorkflowService;
    private readonly ILogger<LibraryMonitorService> _logger;
    private bool _disposed;

    public LibraryMonitorService(
        ILibraryManager libraryManager,
        LibraryRequestMatcher libraryRequestMatcher,
        RequestWorkflowService requestWorkflowService,
        ILogger<LibraryMonitorService> logger)
    {
        _libraryManager = libraryManager;
        _libraryRequestMatcher = libraryRequestMatcher;
        _requestWorkflowService = requestWorkflowService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnLibraryItemChanged;
        _libraryManager.ItemUpdated += OnLibraryItemChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnLibraryItemChanged;
        _libraryManager.ItemUpdated -= OnLibraryItemChanged;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _libraryManager.ItemAdded -= OnLibraryItemChanged;
        _libraryManager.ItemUpdated -= OnLibraryItemChanged;
    }

    private void OnLibraryItemChanged(object? sender, ItemChangeEventArgs e)
    {
        _ = HandleLibraryItemChangedAsync(e);
    }

    private async Task HandleLibraryItemChangedAsync(ItemChangeEventArgs e)
    {
        try
        {
            var updated = await _libraryRequestMatcher.MarkReadyForItemAsync(e.Item, CancellationToken.None).ConfigureAwait(false);
            foreach (var request in updated)
            {
                _requestWorkflowService.Publish(request);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process Source Manager library item update for {ItemId}", e.Item?.Id);
        }
    }
}
