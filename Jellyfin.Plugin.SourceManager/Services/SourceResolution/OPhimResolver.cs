using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SourceManager.Services.SourceResolution;

public sealed class OPhimResolver : PhimApiResolverBase
{
    public OPhimResolver(IHttpClientFactory factory, ILogger<OPhimResolver> logger)
        : base(factory, logger)
    {
    }

    protected override string BaseUrl => "https://ophim1.com";
    public override string Name => "OPhim";
}
