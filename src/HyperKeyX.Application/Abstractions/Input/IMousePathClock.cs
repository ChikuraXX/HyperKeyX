namespace HyperKeyX.Application.Abstractions.Input;

public interface IMousePathClock
{
    long NowUs { get; }
    Task DelayUntilAsync(long deadlineUs, CancellationToken cancellationToken);
}
