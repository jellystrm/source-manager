namespace Jellyfin.Plugin.SourceManager.Models;

public static class RequestStatus
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Ready = "ready";
    public const string Rejected = "rejected";
    public const string All = "all";
}
