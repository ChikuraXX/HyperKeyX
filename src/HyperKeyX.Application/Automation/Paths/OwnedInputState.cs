using HyperKeyX.Application.Abstractions.Input;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Paths;

public sealed record OwnedInputFailure(PathIssue Issue, Exception Exception);

/// <summary>由一个执行会话串行使用，仅成功发送的按下会成为本会话所有。</summary>
public sealed class OwnedInputState(IWin32InputService keyboard, IMouseInputService mouse)
{
    private readonly List<HeldInput> _held = [];
    private readonly List<OwnedInputFailure> _failures = [];
    public bool HasHeldInput => _held.Count > 0;
    public bool HasHeldButtons => _held.Any(i => i.Button != MouseButtonKind.None);
    public IReadOnlyList<OwnedInputFailure> Failures => _failures.AsReadOnly();

    public void KeyDown(KeyboardKeyIdentity key) => Down(new(key, MouseButtonKind.None), () => keyboard.SendKeyDown(key));
    public void KeyUp(KeyboardKeyIdentity key) => Up(new(key, MouseButtonKind.None), () => keyboard.SendKeyUp(key));
    public void ButtonDown(MouseButtonKind button) => Down(new(default, button), () => mouse.SendButtonDown(button));
    public void ButtonUp(MouseButtonKind button) => Up(new(default, button), () => mouse.SendButtonUp(button));
    public void Wheel(int delta) => Send(() => mouse.SendWheel(delta));

    private void Down(HeldInput input, Action send)
    {
        Send(send);
        if (!_held.Contains(input)) _held.Add(input);
    }
    private void Up(HeldInput input, Action send)
    {
        Send(send);
        _held.Remove(input);
    }
    private void Send(Action send)
    {
        if (_failures.Count > 0) throw new InvalidOperationException("输入会话已经失败，不能继续发送动作。");
        try { send(); }
        catch (Exception exception)
        {
            _failures.Add(new(new(PathIssueCode.InputSendFailed), exception));
            throw;
        }
    }

    public void Cleanup()
    {
        // 单个弹起失败不影响其余输入；失败项及历史诊断保留，不能冒充已释放。
        for (var index = _held.Count - 1; index >= 0; index--)
        {
            var input = _held[index];
            try
            {
                if (input.Button != MouseButtonKind.None) mouse.SendButtonUp(input.Button);
                else keyboard.SendKeyUp(input.Key);
                _held.RemoveAt(index);
            }
            catch (Exception exception)
            {
                _failures.Add(new(new(PathIssueCode.InputReleaseFailed), exception));
            }
        }
    }

    private readonly record struct HeldInput(KeyboardKeyIdentity Key, MouseButtonKind Button);
}
