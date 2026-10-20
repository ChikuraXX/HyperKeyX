using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Features.Automation.Services;

/// <summary>
/// 记录“复制宏后再绑定触发器”的延迟意图，保存成功前不修改原触发器引用。
/// </summary>
public sealed record PendingMacroBindingIntent(
    Guid MacroId,
    Guid ProfileId,
    InputTriggerIdentity Trigger);
