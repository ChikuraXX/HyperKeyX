using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Features.Automation.ViewModels;

internal static class MacroHistoryBudget
{
    public static long Estimate(IEnumerable<MacroEditTransaction> transactions,
        IEnumerable<MacroStep> current, IEnumerable<MacroStep> baseline,
        MacroPathContextSnapshot context, MacroPathContextSnapshot savedContext)
    {
        var retainedSources = current.Concat(baseline).Where(s => s.MousePath is not null)
            .Select(s => s.MousePath!.Source).ToHashSet(ReferenceEqualityComparer.Instance);
        var sources = new HashSet<MousePathSource>(ReferenceEqualityComparer.Instance);
        var snapshots = new HashSet<MacroStepSnapshot>(ReferenceEqualityComparer.Instance);
        var contexts = new HashSet<MacroPathContextSnapshot>(ReferenceEqualityComparer.Instance) { context, savedContext };
        long bytes = 0;
        foreach (var transaction in transactions)
        {
            bytes += 128;
            foreach (var snapshot in transaction.Before.Concat(transaction.After))
            {
                if (!snapshots.Add(snapshot)) continue;
                bytes += 256;
                if (snapshot.Model.MousePath is not { } path) continue;
                bytes += 384 + path.Edits.Count * 160L;
                // 只计历史独占的源块；当前草稿和保存基线仍持有的源不重复扣预算。
                if (!retainedSources.Contains(path.Source) && sources.Add(path.Source))
                    bytes += 128 + path.Source.Points.Count * 96L;
            }
            foreach (var stored in new[] { transaction.ContextBefore, transaction.ContextAfter })
            {
                if (stored is null || !contexts.Add(stored)) continue;
                bytes += 128 + stored.Groups.Count * 512L + stored.RecordingIssues.Count * 128L;
                foreach (var target in stored.Targets)
                    bytes += 256 + 2L * ((target.Name?.Length ?? 0) + (target.ProcessName?.Length ?? 0) +
                        (target.ExecutablePath?.Length ?? 0) + (target.WindowClass?.Length ?? 0) + (target.Title?.Length ?? 0));
            }
        }
        return bytes;
    }
}
