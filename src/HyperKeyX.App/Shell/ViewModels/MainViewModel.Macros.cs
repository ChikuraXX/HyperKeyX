using HyperKeyX.Domain.Automation;
using HyperKeyX.Features.Macros.ViewModels;
using HyperKeyX.Shell.Navigation;

namespace HyperKeyX.Shell.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>解除当前触发器的宏引用，仅修改草稿；保存后才发布到运行配置。</summary>
    public void ClearSelectedTriggerMacro()
    {
        if (!CanEditTriggerConfiguration)
            return;
        SelectedTriggerConfig.IsMacroEnabled = false;
        SelectedTriggerConfig.SelectedMacroId = null;
    }

    private void CreateMacroFromUi()
    {
        if (RejectContextChangeWhileRecording())
            return;

        var macro = MacroLibrary.CreateMacro();
        Navigation.ActiveSection = MainSection.Macros;
        StatusText = L(
            $"已创建宏：{macro.Name}",
            $"Created macro: {macro.Name}");
        RaiseWorkspaceState();
    }

    private void CopyCurrentMacroFromUi()
    {
        if (RejectContextChangeWhileRecording() ||
            MacroLibrary.SelectedMacro is not { } source)
        {
            return;
        }

        var copy = MacroLibrary.CopyMacro(source);
        Navigation.ActiveSection = MainSection.Macros;
        StatusText = L(
            $"已创建副本：{copy.Name}",
            $"Created copy: {copy.Name}");
        RaiseWorkspaceState();
    }

    private void RestoreCurrentMacroFromUi()
    {
        if (RejectContextChangeWhileRecording() ||
            MacroLibrary.SelectedMacro is not { } macro)
        {
            return;
        }

        macro.RestoreSaved();
        StatusText = L("已恢复宏的已保存版本", "Restored the saved macro version");
        RaiseWorkspaceState();
    }

    private void OpenSelectedTriggerMacroFromUi()
    {
        if (RejectContextChangeWhileRecording() ||
            SelectedTriggerConfig.SelectedMacroId is not Guid macroId)
        {
            return;
        }

        var macro = MacroLibrary.Macros.FirstOrDefault(candidate =>
            candidate.MacroId == macroId && !candidate.IsDeleted && !candidate.IsNew);
        if (macro is null)
        {
            StatusText = MissingMacroReferenceText;
            return;
        }

        MacroLibrary.SelectedMacro = macro;
        Navigation.ActiveSection = MainSection.Macros;
        StatusText = L($"已打开宏：{macro.Name}", $"Opened macro: {macro.Name}");
    }

    private void CopySelectedTriggerMacroFromUi()
    {
        if (RejectContextChangeWhileRecording())
            return;

        var copy = CopySelectedTriggerMacro();
        if (copy is null)
        {
            StatusText = MissingMacroReferenceText;
            return;
        }

        Navigation.ActiveSection = MainSection.Macros;
        StatusText = L(
            "副本保存成功后才会绑定到当前触发器",
            "The copy will bind to this trigger after its first successful save");
    }

    private void OpenMacroReferenceFromUi(MacroReferenceViewModel? reference)
    {
        if (reference is null || RejectContextChangeWhileRecording())
            return;

        SelectedProfile = Profiles.FirstOrDefault(profile =>
            profile.Profile.ProfileId == reference.ProfileId);
        if (SelectedProfile is null)
        {
            StatusText = L("引用的配置已不存在", "The referenced profile no longer exists");
            return;
        }

        if (reference.Trigger.Kind == InputTriggerKind.Keyboard)
        {
            Navigation.ActiveSection = MainSection.Keyboard;
            SelectKey(reference.Trigger.Code);
        }
        else if (reference.Trigger.Kind == InputTriggerKind.MouseButton &&
                 Enum.IsDefined((MouseButtonKind)reference.Trigger.Code))
        {
            Navigation.ActiveSection = MainSection.Mouse;
            SelectMouseButton((MouseButtonKind)reference.Trigger.Code);
        }
    }
}
