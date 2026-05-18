using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SourceManager.Services.SourceResolution;

public sealed class KkPhimResolver : PhimApiResolverBase
{
    public KkPhimResolver(IHttpClientFactory factory, ILogger<KkPhimResolver> logger)
        : base(factory, logger)
    {
    }

    protected override string BaseUrl => "https://phimapi.com";
    public override string Name => "KKPhim";
}
