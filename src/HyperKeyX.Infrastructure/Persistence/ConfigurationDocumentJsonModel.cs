using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Configuration;
using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Infrastructure.Persistence;

internal sealed class ConfigurationDocumentJsonModel
{
    public int SchemaVersion { get; set; }
    public List<MacroDefinition?>? Macros { get; set; } = [];
    public List<StoredProfile?>? Profiles { get; set; } = [];

    public static ConfigurationDocumentJsonModel FromDomain(
        AutomationConfigurationDocument document) => new()
    {
        SchemaVersion = document.SchemaVersion,
        Macros = document.Macros
            .Select(macro => (MacroDefinition?)macro.DeepClone())
            .ToList(),
        Profiles = document.Profiles
            .Select(profile => (StoredProfile?)StoredProfile.FromDomain(profile))
            .ToList()
    };

    public AutomationConfigurationDocument ToDomain() => new()
    {
        SchemaVersion = SchemaVersion,
        Macros = (Macros ?? [])
            .Where(macro => macro is not null)
            .Select(macro => macro!.DeepClone())
            .ToList(),
        Profiles = (Profiles ?? [])
            .Where(profile => profile is not null)
            .Select(profile => profile!.ToDomain())
            .ToList()
    };
}

internal sealed class StoredProfile
{
    public Guid ProfileId { get; set; }
    public string TargetProcessName { get; set; } = string.Empty;
    public Dictionary<int, StoredKeyConfig?>? KeyConfigs { get; set; } = [];
    public Dictionary<MouseButtonKind, StoredMouseButtonConfig?>? MouseButtonConfigs
    {
        get;
        set;
    } = [];

    public static StoredProfile FromDomain(AppProfile profile) => new()
    {
        ProfileId = profile.ProfileId,
        TargetProcessName = profile.TargetProcessName,
        KeyConfigs = profile.KeyConfigs.ToDictionary(
            pair => pair.Key,
            pair => (StoredKeyConfig?)StoredKeyConfig.FromDomain(pair.Value)),
        MouseButtonConfigs = profile.MouseButtonConfigs.ToDictionary(
            pair => pair.Key,
            pair => (StoredMouseButtonConfig?)StoredMouseButtonConfig.FromDomain(pair.Value))
    };

    public AppProfile ToDomain() => new()
    {
        ProfileId = ProfileId,
        TargetProcessName = TargetProcessName ?? string.Empty,
        KeyConfigs = (KeyConfigs ?? [])
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!.ToDomain()),
        MouseButtonConfigs = (MouseButtonConfigs ?? [])
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!.ToDomain())
    };
}

internal abstract class StoredTriggerBehavior
{
    public bool IsTurboEnabled { get; set; }
    public int KeyDownDuration { get; set; } = 100;
    public int KeyUpDuration { get; set; } = 100;
    public int TriggerMode { get; set; }
    public int LongPressThresholdMs { get; set; } = 500;
    public bool PassThroughOriginalInput { get; set; }
    public bool IsMacroEnabled { get; set; }
    public Guid? MacroId { get; set; }

    protected void CopyToDomain(TriggerBehaviorConfig target)
    {
        target.IsTurboEnabled = IsTurboEnabled;
        target.KeyDownDuration = KeyDownDuration;
        target.KeyUpDuration = KeyUpDuration;
        target.TriggerMode = TriggerMode;
        target.LongPressThresholdMs = LongPressThresholdMs;
        target.PassThroughOriginalInput = PassThroughOriginalInput;
        target.IsMacroEnabled = IsMacroEnabled;
        target.MacroId = MacroId;
        target.MacroSteps = [];
    }

    protected static void CopyFromDomain(
        TriggerBehaviorConfig source,
        StoredTriggerBehavior target)
    {
        target.IsTurboEnabled = source.IsTurboEnabled;
        target.KeyDownDuration = source.KeyDownDuration;
        target.KeyUpDuration = source.KeyUpDuration;
        target.TriggerMode = source.TriggerMode;
        target.LongPressThresholdMs = source.LongPressThresholdMs;
        target.PassThroughOriginalInput = source.PassThroughOriginalInput;
        target.IsMacroEnabled = source.IsMacroEnabled;
        target.MacroId = source.MacroId;
    }
}

internal sealed class StoredKeyConfig : StoredTriggerBehavior
{
    public int TargetVirtualKey { get; set; }
    public int TargetScanCode { get; set; }
    public bool IsExtendedKey { get; set; }

    public static StoredKeyConfig FromDomain(KeyConfig source)
    {
        var target = new StoredKeyConfig
        {
            TargetVirtualKey = source.TargetVirtualKey,
            TargetScanCode = source.TargetScanCode,
            IsExtendedKey = source.IsExtendedKey
        };
        CopyFromDomain(source, target);
        return target;
    }

    public KeyConfig ToDomain()
    {
        var target = new KeyConfig
        {
            TargetVirtualKey = TargetVirtualKey,
            TargetScanCode = TargetScanCode,
            IsExtendedKey = IsExtendedKey
        };
        CopyToDomain(target);
        return target;
    }
}

internal sealed class StoredMouseButtonConfig : StoredTriggerBehavior
{
    public StoredMouseButtonConfig()
    {
        PassThroughOriginalInput = true;
    }

    public static StoredMouseButtonConfig FromDomain(MouseButtonConfig source)
    {
        var target = new StoredMouseButtonConfig();
        CopyFromDomain(source, target);
        return target;
    }

    public MouseButtonConfig ToDomain()
    {
        var target = new MouseButtonConfig();
        CopyToDomain(target);
        return target;
    }
}
