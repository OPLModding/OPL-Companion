using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OPLServerBanners;

internal static class SettingsPresetStore
{
    private const string DirectoryName = "OPLServerBanners";
    private const string FileName = "SettingsPresets.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static SettingsPresetConfig config = new();
    private static bool loaded;

    public static SettingsPresetConfig Config
    {
        get
        {
            EnsureLoaded();
            return config;
        }
    }

    public static string GetConfigPath()
    {
        return Path.Combine(ServerFileSystemPaths.GetConfigDirectoryPath(), DirectoryName, FileName);
    }

    public static void Load()
    {
        string path = GetConfigPath();
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                config = JsonSerializer.Deserialize<SettingsPresetConfig>(json, JsonOptions) ?? new SettingsPresetConfig();
            }
            else
            {
                config = new SettingsPresetConfig();
                Save();
            }
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to load settings loadouts from '{path}': {exception}");
            config = new SettingsPresetConfig();
        }

        NormalizeConfig();
        loaded = true;
    }

    public static void Save()
    {
        string path = GetConfigPath();
        try
        {
            NormalizeConfig();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppDomain.CurrentDomain.BaseDirectory);
            File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to save settings loadouts to '{path}': {exception}");
        }
    }

    public static SettingsPreset? FindPreset(string? id)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        foreach (SettingsPreset preset in config.Presets)
        {
            if (string.Equals(preset.Id, id, StringComparison.Ordinal))
            {
                return preset;
            }
        }

        return null;
    }

    public static void AddPreset(SettingsPreset preset)
    {
        EnsureLoaded();
        NormalizePreset(preset);
        config.Presets.Add(preset);
        if (string.IsNullOrWhiteSpace(config.TogglePresetAId))
        {
            config.TogglePresetAId = preset.Id;
        }
        else if (string.IsNullOrWhiteSpace(config.TogglePresetBId))
        {
            config.TogglePresetBId = preset.Id;
        }

        Save();
    }

    public static bool DeletePreset(string id)
    {
        EnsureLoaded();
        int removed = config.Presets.RemoveAll(preset => string.Equals(preset.Id, id, StringComparison.Ordinal));
        if (removed <= 0)
        {
            return false;
        }

        if (string.Equals(config.TogglePresetAId, id, StringComparison.Ordinal))
        {
            config.TogglePresetAId = string.Empty;
        }

        if (string.Equals(config.TogglePresetBId, id, StringComparison.Ordinal))
        {
            config.TogglePresetBId = string.Empty;
        }

        Save();
        return true;
    }

    private static void EnsureLoaded()
    {
        if (!loaded)
        {
            Load();
        }
    }

    private static void NormalizeConfig()
    {
        config ??= new SettingsPresetConfig();
        config.SchemaVersion = config.SchemaVersion <= 0 ? 1 : config.SchemaVersion;
        config.ToggleKeybind = string.IsNullOrWhiteSpace(config.ToggleKeybind) ? "<Keyboard>/f9" : config.ToggleKeybind.Trim();
        config.WarmupObstacleEditorKeybind = string.IsNullOrWhiteSpace(config.WarmupObstacleEditorKeybind) ? "<Keyboard>/f3" : config.WarmupObstacleEditorKeybind.Trim();
        config.GoalieTrackCycleKeybind = string.IsNullOrWhiteSpace(config.GoalieTrackCycleKeybind) ? "<Keyboard>/f10" : config.GoalieTrackCycleKeybind.Trim();
        config.GoalieTrackCycleNativeKeyBind ??= new SettingsPresetKeyBind
        {
            Path = config.GoalieTrackCycleKeybind
        };
        NormalizeKeyBind(config.GoalieTrackCycleNativeKeyBind, config.GoalieTrackCycleKeybind);
        config.GoalieTrackCycleKeybind = config.GoalieTrackCycleNativeKeyBind.Path;
        config.TogglePresetAId = config.TogglePresetAId ?? string.Empty;
        config.TogglePresetBId = config.TogglePresetBId ?? string.Empty;
        config.Presets ??= new List<SettingsPreset>();

        HashSet<string> seenIds = new(StringComparer.Ordinal);
        foreach (SettingsPreset preset in config.Presets)
        {
            NormalizePreset(preset);
            if (!seenIds.Add(preset.Id))
            {
                preset.Id = Guid.NewGuid().ToString("N");
                seenIds.Add(preset.Id);
            }
        }

        if (FindPresetWithoutLoading(config.TogglePresetAId) == null)
        {
            config.TogglePresetAId = string.Empty;
        }

        if (FindPresetWithoutLoading(config.TogglePresetBId) == null)
        {
            config.TogglePresetBId = string.Empty;
        }
    }

    private static void NormalizePreset(SettingsPreset preset)
    {
        preset.Id = string.IsNullOrWhiteSpace(preset.Id) ? Guid.NewGuid().ToString("N") : preset.Id.Trim();
        preset.Name = string.IsNullOrWhiteSpace(preset.Name) ? "Loadout" : preset.Name.Trim();
        preset.CreatedAtUtc = string.IsNullOrWhiteSpace(preset.CreatedAtUtc) ? DateTime.UtcNow.ToString("O") : preset.CreatedAtUtc;
        preset.UpdatedAtUtc = string.IsNullOrWhiteSpace(preset.UpdatedAtUtc) ? preset.CreatedAtUtc : preset.UpdatedAtUtc;
        preset.NativeSettings ??= new Dictionary<string, SettingsPresetValue>(StringComparer.Ordinal);
        preset.KeyBinds ??= new Dictionary<string, SettingsPresetKeyBind>(StringComparer.Ordinal);
        preset.CompetitiveSkatingKeyBinds ??= new Dictionary<string, SettingsPresetPlayerPref>(StringComparer.Ordinal);
        preset.ExtraUiOptions ??= new Dictionary<string, SettingsPresetPlayerPref>(StringComparer.Ordinal);
    }

    private static void NormalizeKeyBind(SettingsPresetKeyBind keyBind, string defaultPath)
    {
        keyBind.ModifierPath = keyBind.ModifierPath?.Trim() ?? string.Empty;
        keyBind.Path = string.IsNullOrWhiteSpace(keyBind.Path) ? defaultPath : keyBind.Path.Trim();
        keyBind.Path = string.IsNullOrWhiteSpace(keyBind.Path) ? "<Keyboard>/f10" : keyBind.Path;
        keyBind.Interactions = keyBind.Interactions?.Trim() ?? string.Empty;
    }

    private static SettingsPreset? FindPresetWithoutLoading(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        foreach (SettingsPreset preset in config.Presets)
        {
            if (string.Equals(preset.Id, id, StringComparison.Ordinal))
            {
                return preset;
            }
        }

        return null;
    }
}
