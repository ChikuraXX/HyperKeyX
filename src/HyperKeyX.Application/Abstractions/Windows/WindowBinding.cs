namespace HyperKeyX.Application.Abstractions.Windows;

/// <summary>BindingId 对应原生适配器持有的生命周期监视，不能仅按句柄重建绑定。</summary>
public sealed record WindowBinding(Guid BindingId, WindowCandidate Candidate);
