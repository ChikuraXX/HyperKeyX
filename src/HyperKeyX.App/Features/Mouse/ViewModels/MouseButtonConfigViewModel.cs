using System.Collections.ObjectModel;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Features.Automation.ViewModels;
using HyperKeyX.Features.Macros.ViewModels;

namespace HyperKeyX.Features.Mouse.ViewModels;

public sealed class MouseButtonConfigViewModel : TriggerConfigViewModel
{
    private readonly MouseButtonConfig _config;

    public MouseButtonConfigViewModel(
        MouseButtonKind button,
        MouseButtonConfig? config = null,
        bool isEnglish = false,
        ObservableCollection<MacroChoiceViewModel>? publishedMacroChoices = null)
        : base(CreateIdentity(button), publishedMacroChoices)
    {
        Button = button;
        _config = config?.DeepClone() ?? new MouseButtonConfig();
        LoadBehavior(_config, isEnglish);
        UpdateLocalization(isEnglish);
        MarkSaved();
    }

    public MouseButtonKind Button { get; }

    public MouseButtonConfig ToConfig()
    {
        WriteBehaviorTo(_config);
        return _config.DeepClone();
    }

    public override void UpdateLocalization(bool isEnglish)
    {
        SetLanguage(isEnglish);
        TriggerDisplayName = Button switch
        {
            MouseButtonKind.Left => isEnglish ? "Left button" : "左键",
            MouseButtonKind.Right => isEnglish ? "Right button" : "右键",
            MouseButtonKind.Middle => isEnglish ? "Middle button" : "中键",
            MouseButtonKind.XButton1 => isEnglish ? "Button 4" : "侧键 1",
            MouseButtonKind.XButton2 => isEnglish ? "Button 5" : "侧键 2",
            _ => throw new InvalidOperationException("鼠标按钮编辑器包含无效按钮。")
        };
    }

    private static InputTriggerIdentity CreateIdentity(MouseButtonKind button)
    {
        var identity = InputTriggerIdentity.FromMouse(button);
        if (!identity.IsValid)
            throw new ArgumentOutOfRangeException(nameof(button), button, "不支持该鼠标按钮。");

        return identity;
    }
}
