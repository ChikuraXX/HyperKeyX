using HyperKeyX.Application.Abstractions.Input;
using HyperKeyX.Application.Abstractions.Windows;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Paths;

public sealed record MousePathPlaybackResult(IReadOnlyList<PathIssue> Issues, bool Cancelled = false)
{
    public bool Succeeded => !Cancelled && Issues.Count == 0;
}

public sealed class MousePathPlaybackService(IMousePathClock clock, IPointerInputService pointer,
    IWindowTargetService windows, IWin32InputService keyboard, IMouseInputService mouse,
    AutomationInputLease leases, IPathRandomSource? random = null)
{
    private readonly IPathRandomSource _random = random ?? new RuntimeRandom();

    internal MousePathExecutionSession CreateSession(MacroDefinition macro, OwnedInputState owned, CancellationToken token) =>
        new(macro, owned, clock, pointer, windows, _random, token);

    public async Task<MousePathPlaybackResult> PlayOnceAsync(MacroDefinition macro, CancellationToken token)
    {
        var issues = new List<PathIssue>();
        var cancelled = false;
        IDisposable? lease = null;
        MousePathExecutionSession? session = null;
        var owned = new OwnedInputState(keyboard, mouse);
        try
        {
            var admission = leases.TryAcquire(true, token);
            if (!admission.Succeeded) return new([admission.Issue!]);
            lease = admission.Lease;
            session = CreateSession(macro, owned, token);
            await session.PlayCycleAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { cancelled = true; }
        catch (MousePathExecutionException exception) { issues.AddRange(exception.Issues); }
        catch (Exception) { issues.Add(new(PathIssueCode.InputSendFailed)); }
        finally
        {
            owned.Cleanup();
            issues.AddRange(owned.Failures.Select(f => f.Issue));
            session?.Dispose();
            if (session is not null) issues.AddRange(session.CleanupIssues);
            lease?.Dispose();
        }
        return new(issues.AsReadOnly(), cancelled);
    }

    private sealed class RuntimeRandom : IPathRandomSource { public double NextUnit() => Random.Shared.NextDouble(); }
}
