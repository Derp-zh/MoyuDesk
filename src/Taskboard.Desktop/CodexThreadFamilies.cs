using System;
using System.Collections.Generic;
using System.Linq;

namespace Taskboard.Desktop;

// Only explicit local parent IDs establish a family. Names never establish identity.
internal static class CodexThreadFamilies
{
    internal static CodexThread[] Resolve(IEnumerable<CodexThread> source)
    {
        var rows = source.GroupBy(x => x.Id, StringComparer.Ordinal).Where(x => x.Distinct().Count() == 1)
            .Select(x => x.First()).ToDictionary(x => x.Id, StringComparer.Ordinal);
        string Root(CodexThread thread)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int depth = 0; depth < 64; depth++)
            {
                if (!seen.Add(thread.Id)) return "";
                if (thread.ParentId.Length == 0) return thread.Id;
                if (!rows.TryGetValue(thread.ParentId, out thread!)) return "";
            }
            return "";
        }
        return rows.Values.Select(x => x with { RootId = Root(x) }).ToArray();
    }
}
