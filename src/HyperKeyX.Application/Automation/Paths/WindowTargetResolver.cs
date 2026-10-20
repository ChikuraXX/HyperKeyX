using HyperKeyX.Application.Abstractions.Windows;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Paths;

public sealed record WindowTargetResolution(WindowTargetSession? Session, IReadOnlyList<PathIssue> Issues)
{
    public bool IsValid => Session is not null && Issues.Count == 0;
}

public sealed class WindowTargetResolver(IWindowTargetService service)
{
    public WindowTargetResolution BindAll(IReadOnlyList<MacroWindowTarget> targets, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var bindings = new Dictionary<Guid, WindowBinding>();
        try
        {
            if (targets.Any(t => t.TargetId == Guid.Empty || string.IsNullOrWhiteSpace(t.ProcessName) ||
                !Enum.IsDefined(t.TitleMatchMode)) || targets.Select(t => t.TargetId).Distinct().Count() != targets.Count)
                return new(null, [new(PathIssueCode.InvalidTarget)]);
            var candidates = targets.Count > 0 ? service.EnumerateCandidates(token) : [];
            var selected = new List<(MacroWindowTarget Target, WindowCandidate Candidate)>();
            foreach (var target in targets)
            {
                token.ThrowIfCancellationRequested();
                var matches = candidates.Where(c => Matches(target, c)).Take(2).ToArray();
                if (matches.Length != 1)
                    return new(null, [new(matches.Length == 0 ? PathIssueCode.TargetUnavailable : PathIssueCode.AmbiguousTarget,
                        TargetId: target.TargetId)]);
                if (selected.Any(p => p.Candidate.Handle == matches[0].Handle &&
                    (p.Target.RequireDistinctWindow || target.RequireDistinctWindow)))
                    return new(null, [new(PathIssueCode.InvalidTarget, TargetId: target.TargetId)]);
                selected.Add((target, matches[0]));
            }

            // 先完成全体消歧，再申请实例监视；任何失败都释放已经取得的绑定。
            foreach (var (target, candidate) in selected)
            {
                token.ThrowIfCancellationRequested();
                var binding = bindings.Values.FirstOrDefault(b => b.Candidate.Handle == candidate.Handle)
                    ?? service.TryBind(candidate);
                if (binding is null)
                    return FailureAndRelease(bindings, new(PathIssueCode.TargetUnavailable, TargetId: target.TargetId));
                bindings.Add(target.TargetId, binding);
            }
            var session = new WindowTargetSession(service, bindings);
            if (session.ValidateBindings() is { } issue)
            {
                session.Dispose();
                return new(null, new[] { issue }.Concat(session.CleanupIssues).ToArray());
            }
            return new(session, []);
        }
        catch (OperationCanceledException)
        {
            _ = FailureAndRelease(bindings, new(PathIssueCode.TargetUnavailable));
            throw;
        }
        catch (Exception)
        {
            return FailureAndRelease(bindings, new(PathIssueCode.TargetUnavailable));
        }
    }

    public static bool Matches(MacroWindowTarget target, WindowCandidate candidate) => candidate.IsVisible &&
        string.Equals(target.ProcessName, candidate.ProcessName, StringComparison.OrdinalIgnoreCase) &&
        (string.IsNullOrEmpty(target.ExecutablePath) || string.Equals(target.ExecutablePath, candidate.ExecutablePath, StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrEmpty(target.WindowClass) || string.Equals(target.WindowClass, candidate.WindowClass, StringComparison.Ordinal)) &&
        (string.IsNullOrEmpty(target.Title) || (target.TitleMatchMode == WindowTitleMatchMode.Exact
            ? string.Equals(target.Title, candidate.Title, StringComparison.Ordinal)
            : candidate.Title.Contains(target.Title, StringComparison.Ordinal)));

    private WindowTargetResolution FailureAndRelease(Dictionary<Guid, WindowBinding> bindings, PathIssue issue)
    {
        var session = new WindowTargetSession(service, bindings);
        session.Dispose();
        return new(null, new[] { issue }.Concat(session.CleanupIssues).ToArray());
    }
}
