using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Taskboard.Core;

namespace Taskboard.Desktop;

internal sealed partial class BoardController
{
    private Task<CodexThread[]>? conversationLoad;
    private DateTime conversationLoadedAt;
    private CodexThread[] codexDirectory = [];
    internal CodexThread[] ConversationFixture { get; set; } = [];
    internal string CodexConnectionHint => Codex.ConnectionHint;
    internal void EnsureCodexConnection() { if (!Isolated && !exiting && !dataTransition) Codex.EnsureConnection(); }
    private void RefreshCodexConnectionHint() { foreach (var column in Columns) column.RefreshCodexConnectionHint(); }
    internal async Task<CodexThread[]> LoadConversations(Func<Task<CodexThread[]>>? load = null)
    {
        if (Isolated && load is null) { RefreshCodexDirectory(ConversationFixture); return ConversationFixture.Where(x => x.ParentId.Length == 0).ToArray(); }
        EnsureCodexConnection();
        // Empty/error/cancelled reads must not poison subsequent user retries.
        if (conversationLoad is null || conversationLoad.IsFaulted || conversationLoad.IsCanceled ||
            conversationLoad.IsCompletedSuccessfully && conversationLoad.Result.Length == 0 || DateTime.UtcNow - conversationLoadedAt > TimeSpan.FromSeconds(30))
        { conversationLoadedAt = DateTime.UtcNow; conversationLoad = (load ?? Codex.Catalog)(); }
        var values = await conversationLoad;
        RefreshCodexDirectory(values);
        return codexDirectory.Where(x => x.ParentId.Length == 0).ToArray();
    }
    internal void RefreshCodexDirectory(CodexThread[] values, bool notify = true)
    {
        var previous = codexDirectory.ToDictionary(x => x.Id, StringComparer.Ordinal);
        codexDirectory = CodexThreadFamilies.Resolve(values.Select(x => previous.TryGetValue(x.Id, out var newer) && newer.UpdatedAt > x.UpdatedAt ? newer : x));
        var names = codexDirectory.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var changed = new HashSet<Guid>();
        foreach (var task in Engine.State.Tasks.Concat(Engine.State.Sessions.Select(x => x.Working)))
            foreach (var binding in task.Stages.Select(x => x.Codex).OfType<CodexBinding>().Where(x => x.Platform == AiPlatform.Codex))
            {
                if (names.TryGetValue(binding.ThreadId, out var own) && !string.IsNullOrWhiteSpace(own.Name) && binding.ThreadName != own.Name)
                { binding.ThreadName = own.Name; changed.Add(task.Id); }
                // Retain absent/archived sources, but revoke a source when current
                // structured metadata explicitly contradicts its former parent.
                if (binding.RelatedSources.RemoveAll(x => names.TryGetValue(x.ThreadId, out var current) && current.SessionRoot != binding.ThreadId) > 0)
                    changed.Add(task.Id);
                foreach (var thread in codexDirectory.Where(x => x.ParentId.Length > 0 && x.SessionRoot == binding.ThreadId).Take(128))
                {
                    var related = binding.RelatedSources.FirstOrDefault(x => x.ThreadId == thread.Id);
                    if (related is null)
                    {
                        if (binding.RelatedSources.Count >= 128) continue;
                        binding.RelatedSources.Add(new CodexBinding { Platform = AiPlatform.Codex, ThreadId = thread.Id,
                            ThreadName = thread.Name, BoundAtUtc = binding.BoundAtUtc, Generation = binding.Generation });
                        changed.Add(task.Id);
                    }
                    else if (related.ThreadName != thread.Name) { related.ThreadName = thread.Name; changed.Add(task.Id); }
                }
            }
        if (notify && changed.Count > 0) Engine.Notify(changed.ToArray());
    }
}
