namespace HyperKeyX.Domain.Automation;

/// <summary>随机延时的取值形状；正态模式以区间中点为均值、区间宽度的六分之一为标准差。</summary>
public enum MacroDelayDistribution
{
    Uniform,
    Normal
}
