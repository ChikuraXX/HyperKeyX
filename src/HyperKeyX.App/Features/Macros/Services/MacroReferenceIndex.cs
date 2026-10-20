using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Configuration;
using HyperKeyX.Features.Automation.ViewModels;
using HyperKeyX.Features.Macros.ViewModels;

namespace HyperKeyX.Features.Macros.Services;

/// <summary>
/// 合并已发布配置和未保存触发器草稿中的宏引用，不单独持久化索引。
/// </summary>
public sealed class MacroReferenceIndex
{
    private readonly List<IndexedReference> _published = [];
    private readonly Dictionary<DraftTriggerKey, IndexedReference> _drafts = [];

    public event EventHandler? Changed;

    public void RebuildPublished(
        AutomationConfigurationDocument document,
        bool isEnglish = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        var rebuilt = new List<IndexedReference>();

        foreach (var profile in document.Profiles ?? [])
        {
            if (profile is null)
                continue;

            foreach (var pair in profile.KeyConfigs ?? [])
            {
                if (pair.Value?.MacroId is not Guid macroId ||
                    macroId == Guid.Empty)
                {
                    continue;
                }

                var key = KeyboardKeyIdentity.FromStorageKey(pair.Key, pair.Value);
                var trigger = InputTriggerIdentity.FromKeyboard(key);
                if (!trigger.IsValid)
                    continue;

                rebuilt.Add(new IndexedReference(
                    macroId,
                    new MacroReferenceViewModel(
                        profile.ProfileId,
                        profile.DisplayName,
                        trigger,
                        KeyboardKeyDisplayNameFormatter.GetDisplayName(key, isEnglish),
                        IsUnsavedDraft: false)));
            }

            foreach (var pair in profile.MouseButtonConfigs ?? [])
            {
                if (pair.Value?.MacroId is not Guid macroId ||
                    macroId == Guid.Empty)
                {
                    continue;
                }

                var trigger = InputTriggerIdentity.FromMouse(pair.Key);
                if (!trigger.IsValid)
                    continue;

                rebuilt.Add(new IndexedReference(
                    macroId,
                    new MacroReferenceViewModel(
                        profile.ProfileId,
                        profile.DisplayName,
                        trigger,
                        MouseDisplayName(pair.Key, isEnglish),
                        IsUnsavedDraft: false)));
            }
        }

        _published.Clear();
        _published.AddRange(rebuilt);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UpsertDraftReference(
        Guid profileId,
        string profileName,
        InputTriggerIdentity trigger,
        string triggerDisplayName,
        Guid? macroId)
    {
        if (!trigger.IsValid)
            throw new ArgumentOutOfRangeException(nameof(trigger));

        var key = new DraftTriggerKey(profileId, trigger);
        _drafts.Remove(key);
        if (macroId is Guid id && id != Guid.Empty)
        {
            _drafts[key] = new IndexedReference(
                id,
                new MacroReferenceViewModel(
                    profileId,
                    profileName ?? string.Empty,
                    trigger,
                    triggerDisplayName ?? string.Empty,
                    IsUnsavedDraft: true));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool RemoveDraftReference(
        Guid profileId,
        InputTriggerIdentity trigger)
    {
        var removed = _drafts.Remove(new DraftTriggerKey(profileId, trigger));
        if (removed)
            Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    public void ClearDraftReferences()
    {
        if (_drafts.Count == 0)
            return;

        _drafts.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<MacroReferenceViewModel> GetReferences(Guid macroId) =>
        _published
            .Concat(_drafts.Values)
            .Where(reference => reference.MacroId == macroId)
            .Select(reference => reference.Row)
            .OrderBy(reference => reference.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Trigger.Kind)
            .ThenBy(reference => reference.Trigger.Code)
            .ThenBy(reference => reference.IsUnsavedDraft)
            .ToArray();

    public bool HasReferences(Guid macroId) =>
        _published.Any(reference => reference.MacroId == macroId) ||
        _drafts.Values.Any(reference => reference.MacroId == macroId);

    public int GetReferenceCount(Guid macroId) => GetReferences(macroId).Count;

    private static string MouseDisplayName(
        MouseButtonKind button,
        bool isEnglish) => button switch
    {
        MouseButtonKind.Left => isEnglish ? "Left button" : "左键",
        MouseButtonKind.Right => isEnglish ? "Right button" : "右键",
        MouseButtonKind.Middle => isEnglish ? "Middle button" : "中键",
        MouseButtonKind.XButton1 => isEnglish ? "Button 4" : "侧键 1",
        MouseButtonKind.XButton2 => isEnglish ? "Button 5" : "侧键 2",
        _ => button.ToString()
    };

    private readonly record struct DraftTriggerKey(
        Guid ProfileId,
        InputTriggerIdentity Trigger);

    private sealed record IndexedReference(
        Guid MacroId,
        MacroReferenceViewModel Row);
}
