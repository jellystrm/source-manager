using Jellyfin.Plugin.SourceManager.Models;

namespace Jellyfin.Plugin.SourceManager.Services.SourceResolution;

public enum SourceKind { StreamUrl, Torrent }

public sealed record SourceResult(SourceKind Kind, string Value);

public interface ISourceResolver
{
    string Name { get; }
    Task<SourceResult?> ResolveAsync(MediaRequestRecord request, CancellationToken cancellationToken);
}
