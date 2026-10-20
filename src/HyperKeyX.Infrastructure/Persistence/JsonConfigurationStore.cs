using System.Diagnostics;
using System.Text;
using HyperKeyX.Application.Abstractions.Persistence;
using HyperKeyX.Application.Common;
using HyperKeyX.Domain.Configuration;
using HyperKeyX.Domain.Automation.Paths;
using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Infrastructure.Persistence;

/// <summary>
/// 加载版本化配置文档，并通过同目录原子替换持久化完整快照。
/// </summary>
public sealed class JsonConfigurationStore : IConfigurationStore
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(
        encoderShouldEmitUTF8Identifier: false);

    private readonly object _sync = new();
    private bool _saveBlocked;
    private bool _allowOrdinaryRecoveryWrite;

    private string BackupFilePath => ConfigFilePath + ".bak";
    private string TempFilePath => ConfigFilePath + ".tmp";

    public JsonConfigurationStore(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ConfigFilePath = paths.ProfilesFilePath;
    }

    public string ConfigFilePath { get; }

    public ConfigurationLoadResult Load()
    {
        lock (_sync)
        {
            var primaryExists = File.Exists(ConfigFilePath);
            if (TryLoadFile(ConfigFilePath, out var primary))
            {
                SetWritableState(allowOrdinaryRecoveryWrite: false);
                return new ConfigurationLoadResult(
                    primary.Document,
                    ConfigurationLoadSource.Primary,
                    primary.WasMigrated,
                    IsSaveBlocked: false,
                    WarningMessage: null);
            }

            var backupExists = File.Exists(BackupFilePath);
            if (TryLoadFile(BackupFilePath, out var backup))
            {
                SetWritableState(allowOrdinaryRecoveryWrite: true);
                return new ConfigurationLoadResult(
                    backup.Document,
                    ConfigurationLoadSource.Backup,
                    backup.WasMigrated,
                    IsSaveBlocked: false,
                    WarningMessage: primaryExists
                        ? "The primary configuration could not be loaded; the backup was used."
                        : "The primary configuration is missing; the backup was used.");
            }

            var hasUnreadableData = primaryExists || backupExists;
            _saveBlocked = hasUnreadableData;
            _allowOrdinaryRecoveryWrite = false;
            return new ConfigurationLoadResult(
                CreateDefaultDocument(),
                ConfigurationLoadSource.Default,
                WasMigrated: false,
                IsSaveBlocked: hasUnreadableData,
                WarningMessage: hasUnreadableData
                    ? "No valid configuration file could be loaded. Saving is blocked until a replacement is confirmed."
                    : null);
        }
    }

    public OperationResult Save(
        AutomationConfigurationDocument document,
        ConfigurationWriteIntent intent = ConfigurationWriteIntent.Ordinary)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (_sync)
        {
            string json;
            try
            {
                // 候选文档必须在创建临时文件前完成校验，失败时磁盘保持完全不变。
                json = ConfigurationDocumentJsonSerializer.Serialize(document);
            }
            catch (Exception exception)
            {
                return OperationResult.Failure(exception.Message);
            }

            var primaryExists = File.Exists(ConfigFilePath);
            var primaryIsValid = TryLoadFile(ConfigFilePath, out _);
            if (!CanWrite(intent, primaryExists, primaryIsValid))
            {
                return OperationResult.Failure(
                    "Saving is blocked until a valid replacement is confirmed.");
            }

            try
            {
                WriteTempFile(json);
                if (!primaryExists)
                {
                    File.Move(TempFilePath, ConfigFilePath);
                }
                else if (primaryIsValid)
                {
                    File.Replace(
                        TempFilePath,
                        ConfigFilePath,
                        BackupFilePath,
                        ignoreMetadataErrors: true);
                }
                else
                {
                    // 从备份恢复时不把损坏主文件写回已知良好的备份。
                    File.Replace(
                        TempFilePath,
                        ConfigFilePath,
                        destinationBackupFileName: null,
                        ignoreMetadataErrors: true);
                }

                SetWritableState(allowOrdinaryRecoveryWrite: false);
                return OperationResult.Success();
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[JsonConfigurationStore] 保存失败: {exception}");
                return OperationResult.Failure(exception.Message);
            }
            finally
            {
                TryDeleteTempFile();
            }
        }
    }

    private bool CanWrite(
        ConfigurationWriteIntent intent,
        bool primaryExists,
        bool primaryIsValid)
    {
        if (intent == ConfigurationWriteIntent.ConfirmedReplacement)
            return true;

        if (_saveBlocked)
            return false;

        if (primaryExists)
        {
            if (primaryIsValid)
                return true;

            // 恢复授权只代表上次加载成功；写入前必须确认备份仍然可读。
            return _allowOrdinaryRecoveryWrite
                && TryLoadFile(BackupFilePath, out _);
        }

        if (!File.Exists(BackupFilePath))
            return true;

        return TryLoadFile(BackupFilePath, out _);
    }

    private void WriteTempFile(string json)
    {
        var bytes = Utf8WithoutBom.GetBytes(json);
        using var stream = new FileStream(
            TempFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static AutomationConfigurationDocument CreateDefaultDocument() => new()
    {
        Profiles = ProfileConfigurationNormalizer.NormalizeForDocumentStorage([])
    };

    private static bool TryLoadFile(
        string path,
        out ConfigurationDeserializeResult result)
    {
        result = default!;
        if (!File.Exists(path))
            return false;

        try
        {
            using var stream = File.OpenRead(path);
            var parsed = ConfigurationDocumentJsonSerializer.Deserialize(
                BoundedJsonReader.Read(stream, MousePathLimits.MaximumDocumentBytes));
            if (!parsed.Succeeded)
                return false;

            result = parsed.Value;
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[JsonConfigurationStore] 加载 {path} 失败: {exception}");
            return false;
        }
    }

    private void SetWritableState(bool allowOrdinaryRecoveryWrite)
    {
        _saveBlocked = false;
        _allowOrdinaryRecoveryWrite = allowOrdinaryRecoveryWrite;
    }

    private void TryDeleteTempFile()
    {
        try
        {
            if (File.Exists(TempFilePath))
                File.Delete(TempFilePath);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"[JsonConfigurationStore] 清理临时文件失败: {exception.Message}");
        }
    }
}
