using System.Text;
using System.Text.Json;
using HyperKeyX.Application.Abstractions.Persistence;
using HyperKeyX.Application.Automation;
using HyperKeyX.Application.Common;
using HyperKeyX.Domain.Automation;
using HyperKeyX.Domain.Automation.Paths;
using HyperKeyX.Domain.Configuration;
using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Infrastructure.Persistence;

/// <summary>
/// 读写不包含配置文件或触发器引用的单宏离线包。
/// </summary>
public sealed class JsonMacroPackageTransferService : IMacroPackageTransferService
{
    private const int CurrentPackageSchemaVersion = 2;
    private const string MacroPackageKind = "macro";

    private static readonly JsonSerializerOptions JsonOptions = ConfigurationDocumentJsonSerializer.CreateOptions();

    public async Task<OperationResult> ExportAsync(
        Stream destination,
        MacroDefinition macro,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(macro);
            cancellationToken.ThrowIfCancellationRequested();

            var validated = NormalizeAndValidate(macro);
            if (!validated.Succeeded)
                return OperationResult.Failure(validated.ErrorMessage!);

            var package = new MacroPackageJsonModel
            {
                SchemaVersion = CurrentPackageSchemaVersion,
                Kind = MacroPackageKind,
                Macro = validated.Value
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(package, JsonOptions);
            if (bytes.LongLength > MousePathLimits.MaximumPackageBytes)
                return OperationResult.Failure("The macro package exceeds the byte limit.");
            await destination.WriteAsync(bytes, cancellationToken);
            await destination.FlushAsync(cancellationToken);
            return OperationResult.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return OperationResult.Failure(exception.Message);
        }
    }

    public async Task<OperationResult<MacroPackageImportResult>> ImportAsync(
        Stream source,
        IReadOnlyCollection<MacroDefinition> existingMacros,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(existingMacros);
            cancellationToken.ThrowIfCancellationRequested();

            var json = await BoundedJsonReader.ReadAsync(
                source,
                MousePathLimits.MaximumPackageBytes,
                cancellationToken);
            using var parsed = JsonDocument.Parse(json);
            var version = PathJsonContract.Version(parsed.RootElement);
            PathJsonContract.ValidateMacro(PathJsonContract.Required(parsed.RootElement, "macro"), version);
            var package = JsonSerializer.Deserialize<MacroPackageJsonModel>(json, JsonOptions);
            if (package is null)
            {
                return OperationResult<MacroPackageImportResult>.Failure(
                    "The macro package is empty.");
            }

            if (package.SchemaVersion is not (1 or CurrentPackageSchemaVersion))
            {
                return OperationResult<MacroPackageImportResult>.Failure(
                    $"Unsupported macro package schema version: {package.SchemaVersion}.");
            }

            if (!string.Equals(
                    package.Kind,
                    MacroPackageKind,
                    StringComparison.Ordinal))
            {
                return OperationResult<MacroPackageImportResult>.Failure(
                    "The JSON document is not a macro package.");
            }

            if (package.Macro is null)
            {
                return OperationResult<MacroPackageImportResult>.Failure(
                    "The macro package does not contain a macro.");
            }

            var validated = NormalizeAndValidate(package.Macro);
            if (!validated.Succeeded)
            {
                return OperationResult<MacroPackageImportResult>.Failure(
                    validated.ErrorMessage!);
            }

            var resolved = MacroImportConflictResolver.Resolve(
                validated.Value,
                existingMacros);
            return OperationResult<MacroPackageImportResult>.Success(resolved);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return OperationResult<MacroPackageImportResult>.Failure(
                exception.Message);
        }
    }

    private static OperationResult<MacroDefinition> NormalizeAndValidate(
        MacroDefinition macro)
    {
        if (!MousePathValidator.Validate(macro).IsValid)
            return OperationResult<MacroDefinition>.Failure("The macro contains invalid path data.");
        var normalized = macro.DeepClone();
        normalized.Name = (normalized.Name ?? string.Empty).Trim();
        normalized.Description = (normalized.Description ?? string.Empty).Trim();

        var validation = AutomationConfigurationValidator.Validate(new()
        {
            Macros = [normalized],
            Profiles =
            [
                new AppProfile
                {
                    ProfileId = Guid.NewGuid(),
                    TargetProcessName = string.Empty,
                    KeyConfigs = [],
                    MouseButtonConfigs = []
                }
            ]
        });
        if (validation.IsValid)
            return OperationResult<MacroDefinition>.Success(normalized);

        // 错误码保持稳定，后续界面层可按本地化资源映射为用户文本。
        var issueCodes = string.Join(
            ", ",
            validation.Issues.Select(issue => issue.Code).Distinct());
        return OperationResult<MacroDefinition>.Failure(
            $"The macro package contains an invalid macro: {issueCodes}.");
    }

    private sealed class MacroPackageJsonModel
    {
        public int SchemaVersion { get; set; }
        public string Kind { get; set; } = string.Empty;
        public MacroDefinition? Macro { get; set; }
    }
}
