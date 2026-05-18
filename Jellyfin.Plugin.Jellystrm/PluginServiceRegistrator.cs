using Jellyfin.Plugin.Jellystrm.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Jellystrm;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient<TmdbMetadataService>();
        serviceCollection.AddSingleton<IRequestRepository, SqliteRequestRepository>();
        serviceCollection.AddSingleton<RequestEventBroker>();
        serviceCollection.AddSingleton<LibraryRequestMatcher>();
        serviceCollection.AddSingleton<RequestWorkflowService>();
        serviceCollection.AddHostedService<LibraryMonitorService>();
    }
}
