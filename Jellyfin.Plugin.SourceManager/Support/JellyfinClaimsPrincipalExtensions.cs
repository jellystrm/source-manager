using System.Security.Claims;

namespace Jellyfin.Plugin.SourceManager.Support;

public static class JellyfinClaimsPrincipalExtensions
{
    private const string JellyfinUserIdClaim = "Jellyfin-UserId";

    public static Guid GetJellyfinUserId(this ClaimsPrincipal user)
    {
        var value = user.Claims.FirstOrDefault(claim => claim.Type.Equals(JellyfinUserIdClaim, StringComparison.OrdinalIgnoreCase))?.Value;
        return Guid.TryParse(value, out var userId) ? userId : Guid.Empty;
    }
}
