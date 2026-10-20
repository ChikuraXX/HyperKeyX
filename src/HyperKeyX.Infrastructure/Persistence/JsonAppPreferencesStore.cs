using System.Text.Json;
using HyperKeyX.Application.Abstractions.Persistence;
using HyperKeyX.Domain.Preferences;

namespace HyperKeyX.Infrastructure.Persistence;

public sealed class JsonAppPreferencesStore : IAppPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IAppPaths _paths;

    public JsonAppPreferencesStore(IAppPaths paths)
    {
        _paths = paths;
    }

    public string PreferencesFilePath => _paths.PreferencesFilePath;

    public AppPreferences Load()
    {
        try
        {
            if (!File.Exists(PreferencesFilePath))
                return AppPreferencesNormalizer.Normalize(null);

            return AppPreferencesNormalizer.Normalize(ReadPreferences(File.ReadAllText(PreferencesFilePath)));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[JsonAppPreferencesStore] 加载偏好失败: {ex}");
            return AppPreferencesNormalizer.Normalize(null);
        }
    }

    public bool Save(AppPreferences preferences)
    {
        var tempPath = PreferencesFilePath + ".tmp";
        try
        {
            var normalized = AppPreferencesNormalizer.Normalize(preferences);
            var json = JsonSerializer.Serialize(normalized, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, PreferencesFilePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[JsonAppPreferencesStore] 保存偏好失败: {ex}");
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[JsonAppPreferencesStore] 清理临时文件失败: {ex.Message}");
            }
        }
    }

    private static AppPreferences ReadPreferences(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("schemaVersion", out _))
        {
            var language = root.TryGetProperty("languageIndex", out var languageElement)
                ? languageElement.GetInt32()
                : (int)AppLanguageMode.System;
            var theme = root.TryGetProperty("themeIndex", out var themeElement)
                ? themeElement.GetInt32()
                : (int)AppThemeMode.System;
            return new AppPreferences
            {
                Language = (AppLanguageMode)language,
                Theme = (AppThemeMode)theme
            };
        }

        return JsonSerializer.Deserialize<AppPreferences>(json, JsonOptions)
            ?? new AppPreferences();
    }
}
