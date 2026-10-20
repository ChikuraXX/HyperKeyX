using HyperKeyX.Domain.Appearance;

namespace HyperKeyX.Application.Abstractions.Appearance;

/// <summary>提供当前账户可用的本机字体快照；不安装字体、不下载在线字体。</summary>
public interface IInstalledFontCatalog
{
    /// <summary>同步枚举名称和字形覆盖；调用方应在后台线程执行，失败向调用方传播。</summary>
    IReadOnlyList<InstalledFontFamily> ReadInstalledFamilies();
}
