using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Domain.Automation;

/// <summary>
/// 对持久化前的宏步骤执行范围和输入配对校验。
/// </summary>
public static class MacroSequenceValidator
{
    public const int MaximumStepCount = 512;
    public const int MaximumDurationMs = 60_000;
    public const int MaximumWheelMagnitude = 1_200;

    public static MacroSequenceValidationResult Validate(
        IReadOnlyList<MacroStep> steps,
        bool isMacroEnabled)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var issues = new List<MacroValidationIssue>();
        var heldKeys = new Dictionary<int, int>();
        var heldButtons = new Dictionary<MouseButtonKind, int>();

        if (isMacroEnabled && steps.Count == 0)
        {
            issues.Add(new MacroValidationIssue(
                MacroValidationIssueCode.EnabledMacroHasNoSteps,
                -1));
        }

        if (steps.Count > MaximumStepCount)
        {
            issues.Add(new MacroValidationIssue(
                MacroValidationIssueCode.TooManySteps,
                MaximumStepCount));
        }

        // 分别跟踪键盘和鼠标的按下行，允许 Ctrl+C 这类交叠组合。
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            if (step is null)
            {
                issues.Add(new(MacroValidationIssueCode.InvalidStep, index));
                continue;
            }
            if ((step.ActionType == MacroActionType.MousePath &&
                 (step.DurationMs != 0 || !MousePathValidator.ValidatePayload(step.MousePath, index).IsValid)) ||
                (step.ActionType != MacroActionType.MousePath && step.MousePath is not null))
                issues.Add(new(MacroValidationIssueCode.InvalidMousePath, index));
            if (step.DurationMs is < 0 or > MaximumDurationMs)
            {
                issues.Add(new MacroValidationIssue(
                    MacroValidationIssueCode.InvalidDuration,
                    index));
            }
            if (!MacroDelay.HasValidRange(step))
                issues.Add(new(MacroValidationIssueCode.InvalidRandomDuration, index));

            if (!Enum.IsDefined(step.ActionType))
            {
                issues.Add(new MacroValidationIssue(
                    MacroValidationIssueCode.UndefinedActionType,
                    index));
                continue;
            }

            switch (step.ActionType)
            {
                case MacroActionType.KeyDown:
                case MacroActionType.KeyUp:
                    ValidateKeyboardStep(
                        step,
                        index,
                        heldKeys,
                        issues);
                    break;

                case MacroActionType.MouseButtonDown:
                case MacroActionType.MouseButtonUp:
                    ValidateMouseStep(
                        step,
                        index,
                        heldButtons,
                        issues);
                    break;

                case MacroActionType.MouseWheel:
                    if (step.WheelDelta == 0 ||
                        step.WheelDelta is < -MaximumWheelMagnitude or
                            > MaximumWheelMagnitude)
                    {
                        issues.Add(new MacroValidationIssue(
                            MacroValidationIssueCode.InvalidWheelDelta,
                            index));
                    }

                    break;

                case MacroActionType.Delay:
                    break;
            }
        }

        issues.AddRange(heldKeys.Values.Select(index =>
            new MacroValidationIssue(
                MacroValidationIssueCode.InputStillDownAtEnd,
                index)));
        issues.AddRange(heldButtons.Values.Select(index =>
            new MacroValidationIssue(
                MacroValidationIssueCode.InputStillDownAtEnd,
                index)));

        return new MacroSequenceValidationResult(issues
            .OrderBy(issue => issue.StepIndex)
            .ThenBy(issue => issue.Code));
    }

    private static void ValidateKeyboardStep(
        MacroStep step,
        int index,
        IDictionary<int, int> held,
        ICollection<MacroValidationIssue> issues)
    {
        var identity = KeyboardKeyIdentity.FromMacroStep(step);
        if (!identity.IsValid)
        {
            issues.Add(new MacroValidationIssue(
                MacroValidationIssueCode.InvalidKeyboardKey,
                index));
            return;
        }

        ValidatePair(
            identity.StorageKey,
            step.ActionType == MacroActionType.KeyDown,
            index,
            held,
            issues);
    }

    private static void ValidateMouseStep(
        MacroStep step,
        int index,
        IDictionary<MouseButtonKind, int> held,
        ICollection<MacroValidationIssue> issues)
    {
        if (!InputTriggerIdentity.FromMouse(step.MouseButton).IsValid)
        {
            issues.Add(new MacroValidationIssue(
                MacroValidationIssueCode.InvalidMouseButton,
                index));
            return;
        }

        ValidatePair(
            step.MouseButton,
            step.ActionType == MacroActionType.MouseButtonDown,
            index,
            held,
            issues);
    }

    private static void ValidatePair<T>(
        T input,
        bool isDown,
        int index,
        IDictionary<T, int> held,
        ICollection<MacroValidationIssue> issues)
        where T : notnull
    {
        if (isDown)
        {
            if (!held.TryAdd(input, index))
            {
                issues.Add(new MacroValidationIssue(
                    MacroValidationIssueCode.DuplicateInputDown,
                    index));
            }

            return;
        }

        if (!held.Remove(input))
        {
            issues.Add(new MacroValidationIssue(
                MacroValidationIssueCode.InputUpWithoutDown,
                index));
        }
    }
}
