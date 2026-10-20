using HyperKeyX.Application.Common;
using HyperKeyX.Application.Platform;

namespace HyperKeyX.Application.Abstractions.Platform;

/// <summary>通过已有托盘图标发送通知，不创建或取得图标所有权。</summary>
public interface ITrayNotificationService
{
    /// <summary>提交系统通知；成功仅表示 Shell 接受请求，不保证用户看见弹出提示。</summary>
    OperationResult ShowNotification(LocalNotification notification);
}
