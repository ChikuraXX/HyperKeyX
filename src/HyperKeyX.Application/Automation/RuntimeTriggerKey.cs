using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Application.Automation;

/// <summary>
/// 将配置文件与输入来源组合成运行期唯一键，避免不同设备或配置互相取消任务。
/// </summary>
public readonly record struct RuntimeTriggerKey(
    Guid ProfileId,
    InputTriggerIdentity Trigger)
{
    public bool IsValid => ProfileId != Guid.Empty && Trigger.IsValid;
}
