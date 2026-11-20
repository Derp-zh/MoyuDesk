using System;
using System.Collections.Generic;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    private HashSet<AiPlatform>? conversationPlatformsForChecks;
    internal void SetConversationPlatformsForChecks(params AiPlatform[] platforms)
    {
        if (!Isolated) throw new InvalidOperationException("平台夹具仅限隔离数据目录。");
        conversationPlatformsForChecks = new(platforms);
    }
    // Null keeps the last verified presentation while a check is in flight.
    // A stopped client is still usable through its configured local catalog.
    internal bool? ConversationPlatformAvailable(AiPlatform platform)
    {
        if (conversationPlatformsForChecks is not null) return conversationPlatformsForChecks.Contains(platform);
        if (Smoke) return true;
        if (platform == AiPlatform.Codex)
            return AiSetupBusy ? null : Codex.MonitoringReady ||
                AiSetupReport.Catalog && (AiSetupReport.SourceReady || AiSetupReport.DesktopConnected);
        var module = AiModule(platform);
        if (module.Busy) return null;
        var report = module.Report;
        return report.Installed && report.Catalog && report.Configured &&
            report.State is AiConnectionState.Ready or AiConnectionState.NeedsOpen;
    }
}
