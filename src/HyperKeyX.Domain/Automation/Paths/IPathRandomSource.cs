namespace HyperKeyX.Domain.Automation.Paths;

public interface IPathRandomSource
{
    /// <summary>返回 [0, 1)；生产随机源和固定种子测试共用此边界。</summary>
    double NextUnit();
}
