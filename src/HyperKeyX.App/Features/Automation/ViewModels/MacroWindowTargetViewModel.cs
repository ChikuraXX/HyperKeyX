using CommunityToolkit.Mvvm.ComponentModel;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Features.Automation.ViewModels;

public sealed class MacroWindowTargetViewModel(Guid targetId, Func<MacroWindowTarget?> read,
    Action<Action<MacroWindowTarget>> edit, bool isEnglish) : ObservableObject
{
    private bool _isEnglish = isEnglish;
    public Guid TargetId { get; } = targetId;
    public string Name { get => read()?.Name ?? ""; set { if (Name != value) edit(t => t.Name = value ?? ""); } }
    public string ProcessName { get => read()?.ProcessName ?? ""; set { if (ProcessName != value) edit(t => t.ProcessName = value ?? ""); } }
    public string ExecutablePath { get => read()?.ExecutablePath ?? ""; set { if (ExecutablePath != value) edit(t => t.ExecutablePath = value ?? ""); } }
    public string WindowClass { get => read()?.WindowClass ?? ""; set { if (WindowClass != value) edit(t => t.WindowClass = value ?? ""); } }
    public string Title { get => read()?.Title ?? ""; set { if (Title != value) edit(t => t.Title = value ?? ""); } }
    public int TitleMatchIndex { get => (int)(read()?.TitleMatchMode ?? WindowTitleMatchMode.Exact); set { if (value >= 0 && TitleMatchIndex != value) edit(t => t.TitleMatchMode = (WindowTitleMatchMode)value); } }
    public bool RequireDistinctWindow { get => read()?.RequireDistinctWindow ?? false; set { if (RequireDistinctWindow != value) edit(t => t.RequireDistinctWindow = value); } }
    public string NameLabel => L("目标名称", "Target name");
    public string ProcessNameLabel => L("进程文件名（必填，如 editor.exe）", "Process filename (required, e.g. editor.exe)");
    public string ExecutablePathLabel => L("可执行文件完整路径（可选）", "Full executable path (optional)");
    public string WindowClassLabel => L("窗口类（可选）", "Window class (optional)");
    public string TitleLabel => L("窗口标题（可选）", "Window title (optional)");
    public string TitleMatchLabel => L("标题匹配方式", "Title matching");
    public string DistinctLabel => L("必须匹配独立窗口", "Require a distinct window");
    public IReadOnlyList<string> TitleMatchOptions => [L("完全相同", "Exact"), L("包含文本", "Contains")];
    public string MatchHelp => L("所有已填写条件必须同时满足；匹配不到或匹配多个窗口时停止。", "All supplied criteria must match. Zero or multiple matching windows stop playback.");
    public void Refresh(bool isEnglish) { _isEnglish = isEnglish; OnPropertyChanged(string.Empty); }
    private string L(string chinese, string english) => _isEnglish ? english : chinese;
}
