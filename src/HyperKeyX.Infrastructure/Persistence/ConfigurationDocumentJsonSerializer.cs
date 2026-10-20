using System.Text.Json;
using System.Text;
using HyperKeyX.Domain.Automation.Paths;
using HyperKeyX.Application.Common;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Configuration;
using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Infrastructure.Persistence;

public sealed record ConfigurationDeserializeResult(
    AutomationConfigurationDocument Document,
    bool WasMigrated);

public static class ConfigurationDocumentJsonSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    internal static JsonSerializerOptions CreateOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new MousePathSourceJsonConverter() }
    };

    public static string Serialize(AutomationConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!AutomationConfigurationValidator.Validate(document).IsValid)
            throw new InvalidDataException("The automation configuration is invalid.");

        var prepared = Prepare(document);
        if (!AutomationConfigurationValidator.Validate(prepared).IsValid)
            throw new InvalidDataException("The automation configuration is invalid.");

        var normalized = NormalizeProfiles(prepared);
        if (!AutomationConfigurationValidator.Validate(normalized).IsValid)
            throw new InvalidDataException("The automation configuration is invalid.");

        var json = JsonSerializer.Serialize(
            ConfigurationDocumentJsonModel.FromDomain(normalized),
            JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MousePathLimits.MaximumDocumentBytes)
            throw new InvalidDataException("The configuration exceeds the byte limit.");
        return json;
    }

    public static OperationResult<ConfigurationDeserializeResult> Deserialize(
        string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return OperationResult<ConfigurationDeserializeResult>.Failure(
                "The configuration JSON is empty.");
        }

        try
        {
            if (Encoding.UTF8.GetByteCount(json) > MousePathLimits.MaximumDocumentBytes)
                return OperationResult<ConfigurationDeserializeResult>.Failure("The configuration exceeds the byte limit.");
            using var parsed = JsonDocument.Parse(json);
            return parsed.RootElement.ValueKind switch
            {
                JsonValueKind.Array => DeserializeLegacy(json),
                JsonValueKind.Object => DeserializeCurrent(json, parsed.RootElement),
                _ => OperationResult<ConfigurationDeserializeResult>.Failure(
                    "The configuration root must be an object or array.")
            };
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException or InvalidDataException)
        {
            return OperationResult<ConfigurationDeserializeResult>.Failure(
                exception.Message);
        }
    }

    private static OperationResult<ConfigurationDeserializeResult> DeserializeCurrent(
        string json, JsonElement root)
    {
        var version = PathJsonContract.Version(root);
        PathJsonContract.ValidateConfiguration(root, version);
        var stored = JsonSerializer.Deserialize<ConfigurationDocumentJsonModel>(
            json,
            JsonOptions);
        if (stored is null)
        {
            return OperationResult<ConfigurationDeserializeResult>.Failure(
                "The configuration document is empty.");
        }

        if (stored.SchemaVersion is not (1 or 2))
        {
            return OperationResult<ConfigurationDeserializeResult>.Failure(
                $"Unsupported configuration schema version: {stored.SchemaVersion}.");
        }

        // 路径字段先校验再深拷贝，禁止复制代码掩盖空成员或非法编辑。
        foreach (var macro in stored.Macros ?? [])
            if (macro is not null && !MousePathValidator.Validate(macro).IsValid)
                return OperationResult<ConfigurationDeserializeResult>.Failure("Invalid mouse path data.");
        stored.SchemaVersion = AutomationConfigurationDocument.CurrentSchemaVersion;
        return Complete(stored.ToDomain(), wasMigrated: version == 1);
    }

    private static OperationResult<ConfigurationDeserializeResult> DeserializeLegacy(
        string json)
    {
        var profiles = JsonSerializer.Deserialize<List<AppProfile?>>(json, JsonOptions);
        if (profiles is null)
        {
            return OperationResult<ConfigurationDeserializeResult>.Failure(
                "The legacy configuration is empty.");
        }

        return Complete(
            LegacyConfigurationMigrator.Migrate(profiles),
            wasMigrated: true);
    }

    private static OperationResult<ConfigurationDeserializeResult> Complete(
        AutomationConfigurationDocument document,
        bool wasMigrated)
    {
        var prepared = Prepare(document);
        if (!AutomationConfigurationValidator.Validate(prepared).IsValid)
        {
            return OperationResult<ConfigurationDeserializeResult>.Failure(
                "The configuration document failed validation.");
        }

        var normalized = NormalizeProfiles(prepared);
        if (!AutomationConfigurationValidator.Validate(normalized).IsValid)
        {
            return OperationResult<ConfigurationDeserializeResult>.Failure(
                "The normalized configuration document failed validation.");
        }

        return OperationResult<ConfigurationDeserializeResult>.Success(
            new ConfigurationDeserializeResult(normalized, wasMigrated));
    }

    private static AutomationConfigurationDocument Prepare(
        AutomationConfigurationDocument document)
    {
        var macros = (document.Macros ?? [])
            .Where(macro => macro is not null)
            .Select(macro =>
            {
                var clone = macro.DeepClone();
                clone.Name = (clone.Name ?? string.Empty).Trim();
                clone.Description = (clone.Description ?? string.Empty).Trim();
                return clone;
            })
            .ToList();

        return new AutomationConfigurationDocument
        {
            SchemaVersion = document.SchemaVersion,
            Macros = macros,
            Profiles = (document.Profiles ?? [])
                .Where(profile => profile is not null)
                .Select(profile => profile.DeepClone())
                .ToList()
        };
    }

    private static AutomationConfigurationDocument NormalizeProfiles(
        AutomationConfigurationDocument document) => new()
    {
        SchemaVersion = document.SchemaVersion,
        Macros = document.Macros
            .Select(macro => macro.DeepClone())
            .ToList(),
        Profiles = ProfileConfigurationNormalizer
            .NormalizeForDocumentStorage(document.Profiles)
    };
}
