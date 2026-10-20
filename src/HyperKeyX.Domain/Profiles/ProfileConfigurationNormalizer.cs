using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Domain.Profiles;

/// <summary>
/// 在配置进入运行期之前统一修正边界值，并隔离反序列化产生的空对象。
/// </summary>
public static class ProfileConfigurationNormalizer
{
    private const int MaxProfiles = 100;
    private const int MaxKeysPerProfile = 512;
    private const int MaxMouseButtonsPerProfile = 5;
    private const int MaxMacroSteps = 512;

    public static List<AppProfile> NormalizeForStorage(IEnumerable<AppProfile?>? source)
    {
        _ = TryNormalize(
            source,
            requireSourceProfile: false,
            useMacroReferences: false,
            out var profiles,
            out _);
        return profiles;
    }

    public static List<AppProfile> NormalizeForDocumentStorage(
        IEnumerable<AppProfile?>? source)
    {
        _ = TryNormalize(
            source,
            requireSourceProfile: false,
            useMacroReferences: true,
            out var profiles,
            out _);
        return profiles;
    }

    public static bool TryNormalizeImport(
        IEnumerable<AppProfile?>? source,
        out List<AppProfile> profiles,
        out string error)
    {
        return TryNormalize(
            source,
            requireSourceProfile: true,
            useMacroReferences: false,
            out profiles,
            out error);
    }

    private static bool TryNormalize(
        IEnumerable<AppProfile?>? source,
        bool requireSourceProfile,
        bool useMacroReferences,
        out List<AppProfile> profiles,
        out string error)
    {
        profiles = new List<AppProfile>();
        error = string.Empty;
        if (source == null)
        {
            error = "配置根节点为空。";
            return false;
        }

        var seenProfileIds = new HashSet<Guid>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceProfileCount = 0;

        foreach (var candidate in source.Take(MaxProfiles))
        {
            if (candidate == null)
                continue;

            sourceProfileCount++;
            var target = NormalizeProcessName(candidate.TargetProcessName);
            if (!seenTargets.Add(target))
                continue;

            var profileId = candidate.ProfileId;
            if (profileId == Guid.Empty || !seenProfileIds.Add(profileId))
            {
                profileId = Guid.NewGuid();
                seenProfileIds.Add(profileId);
            }

            var profile = new AppProfile
            {
                ProfileId = profileId,
                TargetProcessName = target,
                KeyConfigs = new Dictionary<int, KeyConfig>(),
                MouseButtonConfigs = new Dictionary<MouseButtonKind, MouseButtonConfig>()
            };

            var sourceKeyConfigs = candidate.KeyConfigs ?? new Dictionary<int, KeyConfig>();
            var migrateLegacyCalculatorKey = !sourceKeyConfigs.ContainsKey(0xB7);
            foreach (var pair in sourceKeyConfigs.Take(MaxKeysPerProfile))
            {
                if (pair.Value == null)
                    continue;

                // 旧界面曾把 Calc 错写为 VK_MEDIA_STOP；没有新键时迁移到 VK_LAUNCH_APP2。
                var storageKey = pair.Key == 0xB2 && migrateLegacyCalculatorKey
                    ? 0xB7
                    : pair.Key;
                var identity = KeyboardKeyIdentity.FromStorageKey(storageKey, pair.Value);
                if (!identity.IsValid)
                    continue;

                profile.KeyConfigs[identity.StorageKey] = NormalizeKeyConfig(
                    pair.Value,
                    identity,
                    useMacroReferences);
            }

            var sourceMouseConfigs = candidate.MouseButtonConfigs ??
                                     new Dictionary<MouseButtonKind, MouseButtonConfig>();
            foreach (var pair in sourceMouseConfigs
                         .Where(pair =>
                             pair.Key is >= MouseButtonKind.Left and <= MouseButtonKind.XButton2 &&
                             pair.Value != null)
                         .Take(MaxMouseButtonsPerProfile))
            {
                profile.MouseButtonConfigs[pair.Key] = NormalizeMouseButtonConfig(
                    pair.Value,
                    useMacroReferences);
            }

            profiles.Add(profile);
        }

        if (requireSourceProfile && sourceProfileCount == 0)
        {
            error = "配置中没有有效的应用配置。";
            return false;
        }

        // Global 是无匹配进程时的确定性回退，始终保留一个。
        if (profiles.All(profile => !string.IsNullOrWhiteSpace(profile.TargetProcessName)))
        {
            profiles.Insert(0, new AppProfile
            {
                TargetProcessName = string.Empty,
                KeyConfigs = new Dictionary<int, KeyConfig>(),
                MouseButtonConfigs = new Dictionary<MouseButtonKind, MouseButtonConfig>()
            });
        }

        return true;
    }

    private static KeyConfig NormalizeKeyConfig(
        KeyConfig source,
        KeyboardKeyIdentity identity,
        bool useMacroReferences)
    {
        var target = new KeyConfig
        {
            TargetVirtualKey = identity.VirtualKeyCode,
            TargetScanCode = Math.Clamp(identity.ScanCode, 0, ushort.MaxValue),
            IsExtendedKey = identity.IsExtendedKey
        };
        ApplyNormalizedBehavior(source, target, useMacroReferences);
        return target;
    }

    private static MouseButtonConfig NormalizeMouseButtonConfig(
        MouseButtonConfig source,
        bool useMacroReferences)
    {
        var target = new MouseButtonConfig();
        ApplyNormalizedBehavior(source, target, useMacroReferences);
        return target;
    }

    private static void ApplyNormalizedBehavior(
        TriggerBehaviorConfig source,
        TriggerBehaviorConfig target,
        bool useMacroReferences)
    {
        target.MacroId = source.MacroId;
        target.KeyDownDuration = Math.Clamp(source.KeyDownDuration, 10, 1000);
        target.KeyUpDuration = Math.Clamp(source.KeyUpDuration, 10, 1000);
        target.TriggerMode = Math.Clamp(source.TriggerMode, 0, 2);
        target.LongPressThresholdMs = Math.Clamp(source.LongPressThresholdMs, 250, 2000);
        target.PassThroughOriginalInput = source.PassThroughOriginalInput;

        if (useMacroReferences)
        {
            // 新文档只保存稳定引用；无效引用由文档校验器报告，不能在此静默关闭宏。
            target.IsMacroEnabled = source.IsMacroEnabled;
            target.IsTurboEnabled = source.IsTurboEnabled && !source.IsMacroEnabled;
            target.MacroSteps = [];
            target.RuntimeMacro = null;
            return;
        }

        target.RuntimeMacro = source.IsMacroEnabled ? source.RuntimeMacro?.DeepClone() : null;
        if (target.RuntimeMacro is not null &&
            (!MousePathValidator.Validate(target.RuntimeMacro).IsValid ||
             !MacroSequenceValidator.Validate(target.RuntimeMacro.Steps, true).IsValid))
            throw new InvalidOperationException("Invalid complete runtime macro.");
        var macroSteps = NormalizeMacroSteps(target.RuntimeMacro?.Steps ?? source.MacroSteps);
        var macroEnabled = source.IsMacroEnabled && macroSteps.Count > 0;
        target.IsMacroEnabled = macroEnabled;
        target.IsTurboEnabled = source.IsTurboEnabled && !macroEnabled;
        target.MacroSteps = macroSteps;
    }

    private static List<MacroStep> NormalizeMacroSteps(IEnumerable<MacroStep>? source)
    {
        var macroSteps = new List<MacroStep>();
        foreach (var step in source ?? Array.Empty<MacroStep>())
        {
            if (macroSteps.Count == MaxMacroSteps)
                break;

            if (step == null || !Enum.IsDefined(typeof(MacroActionType), step.ActionType))
                continue;

            if (!MacroDelay.HasValidRange(step))
                throw new InvalidOperationException("Invalid random macro delay.");

            if (step.ActionType == MacroActionType.MousePath)
            {
                // 新载荷不能落入旧键盘分支被删除，也不能用旧时长范围截断。
                if (step.DurationMs != 0 || !MousePathValidator.ValidatePayload(step.MousePath).IsValid)
                    throw new InvalidOperationException("Invalid runtime mouse path.");
                macroSteps.Add(step.DeepClone());
                continue;
            }

            if (step.ActionType is MacroActionType.MouseButtonDown or MacroActionType.MouseButtonUp)
            {
                if (step.MouseButton is < MouseButtonKind.Left or > MouseButtonKind.XButton2)
                    continue;

                macroSteps.Add(new MacroStep
                {
                    ActionType = step.ActionType,
                    MouseButton = step.MouseButton,
                    RandomDurationMaxMs = step.RandomDurationMaxMs,
                    DelayDistribution = step.DelayDistribution,
                    DurationMs = Math.Clamp(step.DurationMs, 0, 60_000)
                });
                continue;
            }

            if (step.ActionType == MacroActionType.MouseWheel)
            {
                if (step.WheelDelta == 0)
                    continue;

                macroSteps.Add(new MacroStep
                {
                    ActionType = MacroActionType.MouseWheel,
                    WheelDelta = Math.Clamp(step.WheelDelta, -1200, 1200),
                    RandomDurationMaxMs = step.RandomDurationMaxMs,
                    DelayDistribution = step.DelayDistribution,
                    DurationMs = Math.Clamp(step.DurationMs, 0, 60_000)
                });
                continue;
            }

            if (step.ActionType == MacroActionType.Delay)
            {
                macroSteps.Add(new MacroStep
                {
                    ActionType = MacroActionType.Delay,
                    RandomDurationMaxMs = step.RandomDurationMaxMs,
                    DelayDistribution = step.DelayDistribution,
                    DurationMs = Math.Clamp(step.DurationMs, 0, 60_000)
                });
                continue;
            }

            if (step.VirtualKeyCode is <= 0 or > 0xFF)
                continue;

            macroSteps.Add(new MacroStep
            {
                ActionType = step.ActionType,
                VirtualKeyCode = step.VirtualKeyCode,
                ScanCode = Math.Clamp(step.ScanCode, 0, ushort.MaxValue),
                IsExtendedKey = step.IsExtendedKey,
                RandomDurationMaxMs = step.RandomDurationMaxMs,
                DelayDistribution = step.DelayDistribution,
                DurationMs = Math.Clamp(step.DurationMs, 0, 60_000)
            });
        }

        return macroSteps;
    }

    private static string NormalizeProcessName(string? processName)
    {
        var value = (processName ?? string.Empty).Trim();
        if (value.Length == 0)
            return string.Empty;

        value = Path.GetFileName(value);
        return value.Length <= 260 ? value : value[..260];
    }
}
