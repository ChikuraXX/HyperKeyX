using HyperKeyX.Application.Abstractions.Processes;
using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Application.Automation;

/// <summary>
/// 将编辑中的配置与自动化运行期配置隔离，并根据前台程序选择保存快照。
/// </summary>
public sealed class AutomationRuntimeCoordinator : IAutomationRuntimeCoordinator, IDisposable
{
    private readonly object _transition = new();
    private readonly ITurboOrchestratorService _orchestrator;
    private readonly IProcessMonitorService _processMonitor;
    private readonly IRuntimeProfileCatalog _profileCatalog;
    private AutomationRuntimeContext _context = AutomationRuntimeContext.Stopped;
    private bool _disposed;

    public event EventHandler<AutomationRuntimeContext>? ContextChanged;
    public event EventHandler<string>? RuntimeError;

    public AutomationRuntimeCoordinator(
        ITurboOrchestratorService orchestrator,
        IProcessMonitorService processMonitor,
        IRuntimeProfileCatalog profileCatalog)
    {
        _orchestrator = orchestrator;
        _processMonitor = processMonitor;
        _profileCatalog = profileCatalog;
        _processMonitor.ForegroundProcessChanged += OnForegroundProcessChanged;
        _orchestrator.RuntimeError += OnRuntimeError;
        _orchestrator.EmergencyStopRequested += OnEmergencyStopRequested;
    }

    public bool IsRunning
    {
        get
        {
            lock (_transition)
                return _context.IsRunning;
        }
    }

    public AutomationRuntimeContext CurrentContext
    {
        get
        {
            lock (_transition)
                return _context;
        }
    }

    public void UpdateProfiles(IReadOnlyCollection<AppProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        AutomationRuntimeContext? changed = null;

        lock (_transition)
        {
            ThrowIfDisposed();
            _profileCatalog.ReplaceProfiles(profiles);
            if (_context.IsRunning)
                changed = ApplyForegroundProcess(_context.ForegroundProcessName);
        }

        Publish(changed);
    }

    public void Start()
    {
        AutomationRuntimeContext changed;

        lock (_transition)
        {
            ThrowIfDisposed();
            if (_context.IsRunning)
                return;

            try
            {
                _orchestrator.SetActiveProfile(null);
                _processMonitor.Start();
                var known = _processMonitor.TryGetForegroundProcessName(out var processName);
                changed = ApplyForegroundProcess(known ? processName : string.Empty);
                _orchestrator.Start();
                _context = changed with
                {
                    IsRunning = true,
                    WasEmergencyStopped = false
                };
                changed = _context;
            }
            catch
            {
                _orchestrator.SetActiveProfile(null);
                _orchestrator.Stop();
                _processMonitor.Stop();
                _context = AutomationRuntimeContext.Stopped;
                throw;
            }
        }

        Publish(changed);
    }

    public void Stop()
    {
        StopCore(wasEmergencyStopped: false);
    }

    private void StopCore(bool wasEmergencyStopped)
    {
        AutomationRuntimeContext? changed = null;

        lock (_transition)
        {
            if (_disposed)
                return;
            if (!_context.IsRunning && !wasEmergencyStopped)
            {
                // 输出已停止不代表物理键收尾已完成；幂等入口仍应协调剩余清理。
                _orchestrator.Stop();
                return;
            }

            _orchestrator.SetActiveProfile(null);
            _orchestrator.Stop();
            _processMonitor.Stop();
            _context = new AutomationRuntimeContext(
                false,
                _context.IsProcessKnown,
                wasEmergencyStopped,
                _context.ForegroundProcessName,
                null,
                string.Empty);
            changed = _context;
        }

        Publish(changed);
    }

    private AutomationRuntimeContext ApplyForegroundProcess(string processName)
    {
        var known = !string.IsNullOrWhiteSpace(processName);
        var normalized = known ? Path.GetFileName(processName) : string.Empty;
        var snapshot = known
            ? _profileCatalog.ResolveKnownProcess(normalized)
            : null;

        _orchestrator.SetActiveProfile(snapshot);
        _context = new AutomationRuntimeContext(
            _context.IsRunning,
            known,
            false,
            normalized,
            snapshot?.ProfileId,
            snapshot?.DisplayName ?? string.Empty);
        return _context;
    }

    private void OnForegroundProcessChanged(object? sender, string processName)
    {
        AutomationRuntimeContext? changed = null;

        lock (_transition)
        {
            if (_context.IsRunning)
                changed = ApplyForegroundProcess(processName);
        }

        Publish(changed);
    }

    private void OnEmergencyStopRequested(object? sender, EventArgs e)
    {
        // 低级键盘钩子线程不能同步等待钩子和自动化任务退出。
        _ = Task.Run(() => StopCore(wasEmergencyStopped: true));
    }

    private void OnRuntimeError(object? sender, string message) =>
        RuntimeError?.Invoke(this, message);

    private void Publish(AutomationRuntimeContext? context)
    {
        if (context != null)
            ContextChanged?.Invoke(this, context);
    }

    public void Dispose()
    {
        lock (_transition)
        {
            if (_disposed)
                return;
        }

        Stop();
        _processMonitor.ForegroundProcessChanged -= OnForegroundProcessChanged;
        _orchestrator.RuntimeError -= OnRuntimeError;
        _orchestrator.EmergencyStopRequested -= OnEmergencyStopRequested;

        lock (_transition)
            _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AutomationRuntimeCoordinator));
    }
}
