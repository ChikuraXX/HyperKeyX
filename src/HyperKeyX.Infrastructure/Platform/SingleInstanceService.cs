using System.Security.Principal;
using HyperKeyX.Application.Abstractions.Platform;
using HyperKeyX.Application.Common;

namespace HyperKeyX.Infrastructure.Platform;

public sealed class SingleInstanceService : ISingleInstanceService
{
    private readonly object _gate = new();
    private readonly string _mutexName;
    private readonly string _activationEventName;
    private readonly CancellationTokenSource _stopping = new();
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private Task? _listener;
    private bool _hasAttemptedAcquire;
    private bool _isDisposed;

    public SingleInstanceService()
        : this(WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName)
    {
    }

    internal SingleInstanceService(string userScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userScope);
        var scope = SanitizeScope(userScope);
        _mutexName = $@"Local\HyperKeyX.{scope}.Mutex";
        _activationEventName = $@"Local\HyperKeyX.{scope}.Activate";
    }

    public event EventHandler? ActivationRequested;

    public bool IsPrimary { get; private set; }

    public bool TryAcquire()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_hasAttemptedAcquire)
                return IsPrimary;

            _hasAttemptedAcquire = true;
            // createdNew 不受互斥量同线程重入影响，可稳定区分同进程重复协调器。
            _mutex = new Mutex(
                initiallyOwned: true,
                _mutexName,
                out var createdNew);
            IsPrimary = createdNew;

            _activationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                _activationEventName);
            if (IsPrimary)
                _listener = Task.Run(Listen);
            return IsPrimary;
        }
    }

    public OperationResult SignalPrimary()
    {
        lock (_gate)
        {
            if (_isDisposed)
                return OperationResult.Failure("单实例协调器已释放。");
            if (!_hasAttemptedAcquire || _activationEvent is null)
                return OperationResult.Failure("尚未检查单实例所有权。");

            try
            {
                return _activationEvent.Set()
                    ? OperationResult.Success()
                    : OperationResult.Failure("无法通知主实例。");
            }
            catch (ObjectDisposedException)
            {
                return OperationResult.Failure("单实例激活通道已关闭。");
            }
        }
    }

    public void Dispose()
    {
        Task? listener;
        lock (_gate)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _stopping.Cancel();
            listener = _listener;
        }

        if (listener is not null && Task.CurrentId != listener.Id)
        {
            try
            {
                _ = listener.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // 释放路径不传播后台监听器异常。
            }
        }

        lock (_gate)
        {
            if (IsPrimary && _mutex is not null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // 非所有者线程释放时由句柄关闭完成最终清理。
                }
            }

            IsPrimary = false;
            _activationEvent?.Dispose();
            _mutex?.Dispose();
            _stopping.Dispose();
            _activationEvent = null;
            _mutex = null;
            _listener = null;
        }
    }

    private void Listen()
    {
        var activationEvent = _activationEvent;
        if (activationEvent is null)
            return;

        var handles = new[] { activationEvent, _stopping.Token.WaitHandle };
        while (WaitHandle.WaitAny(handles) == 0)
        {
            RaiseActivationRequested();
            if (_stopping.IsCancellationRequested)
                break;
        }
    }

    private void RaiseActivationRequested()
    {
        var handlers = ActivationRequested;
        if (handlers is null)
            return;

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                // 一个订阅者失败不能终止后续激活请求或其他订阅者。
            }
        }
    }

    private static string SanitizeScope(string scope)
    {
        var sanitized = string.Concat(scope.Select(character =>
            char.IsAsciiLetterOrDigit(character) ? character : '-'));
        return string.IsNullOrWhiteSpace(sanitized) ? "CurrentUser" : sanitized;
    }
}
