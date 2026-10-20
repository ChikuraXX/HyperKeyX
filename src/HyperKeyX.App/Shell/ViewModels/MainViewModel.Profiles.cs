using CommunityToolkit.Mvvm.Input;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Configuration;
using HyperKeyX.Domain.Profiles;
using HyperKeyX.Features.Automation.Services;
using HyperKeyX.Features.Keyboard.ViewModels;
using HyperKeyX.Features.Mouse.ViewModels;
using HyperKeyX.Features.Profiles.ViewModels;
using HyperKeyX.Shell.Navigation;
using HyperKeyX.Shell.Files;
using HyperKeyX.Application.Common;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace HyperKeyX.Shell.ViewModels;

public sealed partial class MainViewModel
{
    private void LoadProfiles()
    {
        _profiles = _publishedDocument.Profiles
            .Select(profile => profile.DeepClone())
            .ToList();
        Profiles.Clear();
        foreach (var profile in _profiles)
            Profiles.Add(new AppProfileViewModel(profile));

        SelectedProfile = Profiles.FirstOrDefault(profile => profile.IsGlobal)
            ?? Profiles.FirstOrDefault();
    }

    [RelayCommand]
    private void AddProfile()
    {
        if (PendingConfigurationImport is not null)
        {
            StatusText = L(
                "完整配置替换待保存，不能单独添加配置",
                "Save or discard the pending replacement before adding a profile");
            return;
        }

        if (RejectContextChangeWhileRecording())
            return;

        var suffix = 1;
        string target;
        do
        {
            target = $"new_process_{suffix}.exe";
            suffix++;
        }
        while (_profiles.Any(profile =>
                   string.Equals(profile.TargetProcessName, target, StringComparison.OrdinalIgnoreCase)));

        var profile = new AppProfile
        {
            TargetProcessName = target,
            KeyConfigs = new Dictionary<int, KeyConfig>(),
            MouseButtonConfigs = new Dictionary<MouseButtonKind, MouseButtonConfig>()
        };
        _profiles.Add(profile);
        var viewModel = new AppProfileViewModel(profile);
        Profiles.Add(viewModel);
        if (!ProcessNames.Contains(target, StringComparer.OrdinalIgnoreCase))
            ProcessNames.Add(target);
        SelectedProfile = viewModel;
        _hasPendingProfileWorkspaceChanges = true;
        RaiseWorkspaceState();

        StatusText = SaveAll()
            ? L("已添加配置", "Profile added")
            : L("配置已添加，但保存失败", "Profile added, but saving failed");
    }

    [RelayCommand]
    private void DeleteProfile(AppProfileViewModel? profile)
    {
        if (PendingConfigurationImport is not null)
        {
            StatusText = L(
                "完整配置替换待保存，不能单独删除配置",
                "Save or discard the pending replacement before deleting a profile");
            return;
        }

        if (RejectContextChangeWhileRecording())
            return;

        if (profile == null)
            return;

        if (profile.IsGlobal)
        {
            StatusText = L("Global 配置不能删除", "The Global profile cannot be deleted");
            return;
        }

        _profiles.Remove(profile.Profile);
        Profiles.Remove(profile);
        DisposeProfileEditors(profile.Profile.ProfileId);
        SelectedProfile = Profiles.FirstOrDefault(candidate => candidate.IsGlobal)
            ?? Profiles.FirstOrDefault();
        _hasPendingProfileWorkspaceChanges = true;
        RaiseWorkspaceState();

        StatusText = SaveAll()
            ? L("配置已删除", "Profile deleted")
            : L("配置已删除，但保存失败", "Profile deleted, but saving failed");
    }

    private void RaiseTriggerDraftState()
    {
        RaiseWorkspaceState();
    }

    private bool SaveAll() => SaveProfileWorkspace();

    [RelayCommand]
    private async Task ExportProfiles()
    {
        if (RejectContextChangeWhileRecording())
            return;

        try
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            picker.FileTypeChoices.Add(IsEnglish ? "JSON configuration" : "JSON 配置", new[] { ".json" });
            picker.SuggestedFileName = "HyperKeyX_Profiles";

            var file = await picker.PickSaveFileAsync();
            if (file == null)
                return;

            var profileCount = 0;
            var result = await PickedFileExport.WriteAsync(file, async stream =>
            {
                var exported = await ExportPublishedConfigurationAsync(stream);
                profileCount = exported.Succeeded ? exported.Value : 0;
                return exported.Succeeded ? OperationResult.Success()
                    : OperationResult.Failure(exported.ErrorMessage ?? "Export failed");
            });
            if (!result.Succeeded)
            {
                StatusText = L("导出失败", "Export failed");
                return;
            }

            StatusText = L(
                $"已导出完整配置（{profileCount} 个配置文件）",
                $"Exported the full configuration ({profileCount} profiles)");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] 导出失败: {ex}");
            StatusText = L("导出失败", "Export failed");
        }
    }

    [RelayCommand]
    private async Task ImportProfiles()
    {
        if (RejectContextChangeWhileRecording())
            return;

        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            picker.FileTypeFilter.Add(".json");

            var file = await picker.PickSingleFileAsync();
            if (file == null)
                return;

            await using var stream = await file.OpenStreamForReadAsync();
            var result = await PrepareConfigurationImportAsync(stream);
            if (!result.Succeeded)
            {
                StatusText = HasUnsavedChanges
                    ? L(
                        "请先保存或放弃当前更改，再导入完整配置",
                        "Save or discard current changes before importing")
                    : L("导入失败", "Import failed");
                return;
            }

            if (!await ConfirmPreparedConfigurationImportWithPromptAsync())
            {
                StatusText = L("已取消导入", "Import cancelled");
                return;
            }

            StatusText = L(
                "完整配置替换已暂存，请使用“保存全部”发布",
                "The full replacement is staged; use Save All to publish it");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] 导入失败: {ex}");
            StatusText = L("导入失败", "Import failed");
        }
    }
}
