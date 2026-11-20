using System.Linq;
using System.Text.Json;
namespace Taskboard.Desktop;
internal sealed record CodexTurnMetadata(string Id, string Status, string? StopReason = null, long? StartedAt = null)
{
    // Retain only the protocol enum. Error prose, URLs, outputs and account
    // quota snapshots never enter the metadata projection.
    internal static string? ReadStopReason(JsonElement error)
    {
        if (error.ValueKind != JsonValueKind.Object || !error.TryGetProperty("codexErrorInfo", out var info)) return null;
        if (info.ValueKind == JsonValueKind.String)
            return info.GetString() is "usageLimitExceeded" or "rateLimitExceeded" ? info.GetString() : null;
        if (info.ValueKind == JsonValueKind.Object)
        {
            var properties = info.EnumerateObject().Take(2).ToArray();
            if (properties.Length == 1 && properties[0].Name is "usageLimitExceeded" or "rateLimitExceeded") return properties[0].Name;
        }
        return null;
    }
}

internal sealed record CodexDesktopSnapshot(JsonElement Status, CodexTurnMetadata[] Turns, bool LiveCapable, string Reason, string HostId);
