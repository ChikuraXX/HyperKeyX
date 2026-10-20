namespace HyperKeyX.Domain.Preferences;

public enum AppLanguageMode
{
    SimplifiedChinese = 0,
    English = 1,
    System = 2
}

public enum AppThemeMode
{
    Light = 0,
    Dark = 1,
    System = 2
}

public enum StartupPageMode
{
    Keyboard = 0,
    Profiles = 1,
    LastVisited = 2,
    /// <summary>启动时打开鼠标配置。</summary>
    Mouse = 3,
    /// <summary>启动时打开宏库。</summary>
    Macros = 4
}

public enum AppPageId
{
    Keyboard = 0,
    Profiles = 1,
    Settings = 2,
    // 保留已有枚举值，避免旧偏好文件反序列化到错误页面。
    Mouse = 3,
    Macros = 4
}

public enum SettingsCategoryId
{
    General = 0,
    Appearance = 1,
    /// <summary>软件介绍、版本更新及项目支持入口，保留原有分类编号。</summary>
    About = 2
}

public enum AccentMode
{
    System = 0,
    Custom = 1
}

public enum BackdropMode
{
    System = 0,
    Mica = 1,
    Acrylic = 2,
    Solid = 3
}

public enum AppFontOption
{
    System = 0,
    SegoeUiVariable = 1,
    MicrosoftYaHeiUi = 2
}

public enum UiScaleMode
{
    System = 0,
    Percent100 = 100,
    Percent125 = 125,
    Percent150 = 150,
    Percent175 = 175,
    Percent200 = 200
}
