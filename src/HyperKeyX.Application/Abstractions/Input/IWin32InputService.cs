using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Application.Abstractions.Input;

/// <summary>
/// 通过 Win32 SendInput 发送底层键盘输入的服务接口
/// </summary>
public interface IWin32InputService
{
    void SendKeyDown(KeyboardKeyIdentity key);
    void SendKeyUp(KeyboardKeyIdentity key);
}
