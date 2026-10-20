using System.Text.Json;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Infrastructure.Persistence;

/// <summary>先查原始 JSON 结构，避免默认值、空成员过滤或复制掩盖轨迹损坏。</summary>
internal static class PathJsonContract
{
    public static int Version(JsonElement root)
    {
        CheckDuplicateProperties(root);
        var version = Required(root, "schemaVersion");
        if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var result) || result is not (1 or 2))
            throw new JsonException("Unsupported schema version.");
        return result;
    }

    public static void ValidateConfiguration(JsonElement root, int version)
    {
        long points = 0;
        if (TryProperty(root, "macros", out var macros) && macros.ValueKind == JsonValueKind.Array)
        {
            foreach (var macro in macros.EnumerateArray())
            {
                if (version == 1 && macro.ValueKind == JsonValueKind.Null)
                    continue;
                points += ValidateMacro(macro, version);
                if (points > MousePathLimits.MaximumDocumentPoints)
                    throw new JsonException("The document contains too many path points.");
            }
        }
        else if (version == 2)
            throw new JsonException("V2 requires a macros array.");

        if (version == 2)
        {
            var profiles = Required(root, "profiles");
            if (profiles.ValueKind != JsonValueKind.Array || profiles.EnumerateArray().Any(profile => profile.ValueKind != JsonValueKind.Object))
                throw new JsonException("V2 requires valid profile objects.");
        }
    }

    public static long ValidateMacro(JsonElement macro, int version)
    {
        if (macro.ValueKind != JsonValueKind.Object)
            throw new JsonException("Invalid macro object.");
        if (version == 1 && (TryProperty(macro, "pathGroups", out _) || TryProperty(macro, "windowTargets", out _)))
            throw new JsonException("V1 cannot contain V2 path metadata.");

        var steps = Required(macro, "steps");
        if (steps.ValueKind != JsonValueKind.Array)
            throw new JsonException("Invalid macro steps.");
        long count = 0;
        foreach (var step in steps.EnumerateArray())
        {
            if (version == 1 && step.ValueKind == JsonValueKind.Null)
                continue;
            if (step.ValueKind != JsonValueKind.Object)
                throw new JsonException("Invalid macro step.");
            var hasPath = TryProperty(step, "mousePath", out var path);
            var isPath = TryProperty(step, "actionType", out var action) &&
                action.ValueKind == JsonValueKind.Number && action.TryGetInt32(out var type) && type == 6;
            if (version == 1 && (hasPath || isPath))
                throw new JsonException("V1 cannot contain MousePath.");
            if (!isPath)
            {
                if (hasPath && path.ValueKind != JsonValueKind.Null)
                    throw new JsonException("Unexpected MousePath payload.");
                continue;
            }
            RequireFields(path, "groupId", "mode", "sourceKind", "source", "edits", "processing", "timing");
            var source = Required(path, "source");
            var points = Required(source, "points");
            if (points.ValueKind != JsonValueKind.Array)
                throw new JsonException("Invalid raw points.");
            count += points.GetArrayLength();
            if (count > MousePathLimits.MaximumMacroPoints)
                throw new JsonException("The macro contains too many raw points.");
            foreach (var point in points.EnumerateArray())
                RequireFields(point, "pointId", "offsetUs", "sequence", "x", "y");
            var edits = Required(path, "edits");
            if (edits.ValueKind != JsonValueKind.Array || edits.GetArrayLength() > MousePathLimits.MaximumMacroPoints)
                throw new JsonException("Invalid path edits.");
            foreach (var edit in edits.EnumerateArray())
            {
                RequireFields(edit, "pointId");
                if (TryProperty(edit, "insertAfterPointId", out var after) && after.ValueKind != JsonValueKind.Null) count++;
            }
            if (count > MousePathLimits.MaximumMacroPoints) throw new JsonException("The macro contains too many source and inserted points.");
            RequireFields(Required(path, "processing"), "samplingWindowUs", "movementThresholdPx", "stillnessDurationUs",
                "smoothingEnabled", "smoothingMaxDeviationPx", "simplificationEnabled", "simplificationMaxDeviationPx", "simplificationMaxTimeErrorUs");
            RequireFields(Required(path, "timing"), "mode", "fixedDurationUs", "speedPxPerSecond",
                "maxSpeedPxPerSecond", "maxAccelerationPxPerSecondSquared");
        }

        if (version == 2)
        {
            ValidateObjectArray(macro, "windowTargets", target => RequireFields(target, "targetId", "processName", "titleMatchMode"));
            ValidateObjectArray(macro, "pathGroups", group =>
            {
                RequireFields(group, "groupId", "coordinateMode", "origin", "recordedFrame", "randomization");
                RequireFields(Required(group, "origin"), "x", "y");
                RequireFields(Required(group, "recordedFrame"), "left", "top", "width", "height");
                RequireFields(Required(group, "randomization"), "translationEnabled", "translationXPx", "translationYPx",
                    "jitterEnabled", "jitterAmplitudePx", "jitterCorrelationDistancePx");
            });
        }
        return count;
    }

    public static JsonElement Required(JsonElement element, string name)
    {
        if (!TryProperty(element, name, out var value) || value.ValueKind == JsonValueKind.Null)
            throw new JsonException($"Required field is missing: {name}.");
        return value;
    }

    private static void RequireFields(JsonElement element, params string[] fields)
    {
        foreach (var field in fields)
            _ = Required(element, field);
    }

    private static void ValidateObjectArray(JsonElement owner, string name, Action<JsonElement> validate)
    {
        var array = Required(owner, name);
        if (array.ValueKind != JsonValueKind.Array)
            throw new JsonException($"Invalid array: {name}.");
        foreach (var item in array.EnumerateArray())
            validate(item);
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement result)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    result = property.Value;
                    return true;
                }
        result = default;
        return false;
    }

    private static void CheckDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new JsonException($"Duplicate property: {property.Name}.");
                CheckDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                CheckDuplicateProperties(item);
    }
}
