using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace OPLServerBanners;

internal static class SettingsPresetRuntime
{
    private const string KindBool = "bool";
    private const string KindInt = "int";
    private const string KindFloat = "float";
    private const string KindString = "string";
    private const string KindEnum = "enum";
    private const string CompetitiveSkatingPluginTypeName = "PuckCompetitiveSkating.SkatingPlugin";
    private const string CompetitiveSkatingUiTypeName = "PuckCompetitiveSkating.CompSkatingUI";

    private static readonly BindingFlags PublicStaticFlags = BindingFlags.Public | BindingFlags.Static;
    private static readonly BindingFlags AnyStaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private static readonly NativeSettingDefinition[] NativeSettings =
    {
        new("CameraAngle", "Game", true),
        new("Handedness", "Game", true),
        new("ShowPuckSilhouette", "Game", true),
        new("ShowTeamColorBar", "Game", true),
        new("ShowPuckOutline", "Game", true),
        new("ShowPuckElevation", "Game", true),
        new("ShowPlayerUsernames", "Game", true),
        new("PlayerUsernamesFadeThreshold", "Game", true),
        new("UseNetworkSmoothing", "Game", true),
        new("NetworkSmoothingStrength", "Game", true),
        new("MaxMatchmakingPing", "Game", true),
        new("FilterChatProfanity", "Game", true),
        new("Units", "Game", true),
        new("ShowGameUserInterface", "Game", true),
        new("UserInterfaceScale", "Game", true),
        new("ChatOpacity", "Game", true),
        new("ChatScale", "Game", true),
        new("MinimapOpacity", "Game", true),
        new("MinimapBackgroundOpacity", "Game", true),
        new("MinimapHorizontalPosition", "Game", true),
        new("MinimapVerticalPosition", "Game", true),
        new("MinimapScale", "Game", true),
        new("ShowMinimapSticks", "Game", true),
        new("ShowMinimapPuckElevation", "Game", true),
        new("ShowMinimapFallenIndicator", "Game", true),

        new("GlobalStickSensitivity", "Controls", true),
        new("HorizontalStickSensitivity", "Controls", true),
        new("VerticalStickSensitivity", "Controls", true),
        new("LookSensitivity", "Controls", true),

        new("GlobalVolume", "Audio", true),
        new("AmbientVolume", "Audio", true),
        new("GameVolume", "Audio", true),
        new("VoiceVolume", "Audio", true),
        new("UIVolume", "Audio", true),

        new("FullScreenMode", "Video", false),
        new("DisplayIndex", "Video", false),
        new("ResolutionIndex", "Video", false),
        new("VSync", "Video", false),
        new("FpsLimit", "Video", false),
        new("Fov", "Video", true),
        new("Quality", "Video", false),
        new("ShadowQuality", "Video", false),
        new("MotionBlur", "Video", false)
    };

    private static readonly ExtraUiOptionDefinition[] ExtraUiOptions =
    {
        ExtraInt("HideChat", 0),
        ExtraFloat("ChatHorizontal", 0f),
        ExtraFloat("ChatVertical", 0f),
        ExtraInt("HideScoreboard", 0),
        ExtraInt("ShowScoreboardOnTab", 0),
        ExtraFloat("ScoreboardHorizontal", 50f),
        ExtraFloat("ScoreboardVertical", 0f),
        ExtraInt("HideSpeed", 0),
        ExtraFloat("SpeedHorizontal", 0f),
        ExtraFloat("SpeedVertical", 0f),
        ExtraFloat("SpeedSize", 1f),
        ExtraInt("SpeedColorR", 255),
        ExtraInt("SpeedColorG", 255),
        ExtraInt("SpeedColorB", 255),
        ExtraInt("SpeedColorA", 255),
        ExtraInt("SpeedStaminaBasedColor", 0),
        ExtraInt("HideStamina", 0),
        ExtraFloat("StaminaHorizontal", 50f),
        ExtraFloat("StaminaVertical", 0f),
        ExtraFloat("StaminaRotation", 0f),
        ExtraFloat("StaminaSIZE", 1f),
        ExtraInt("StaminaColorR", 255),
        ExtraInt("StaminaColorG", 255),
        ExtraInt("StaminaColorB", 255),
        ExtraInt("StaminaColorA", 255),
        ExtraInt("StaminaEColorR", 255),
        ExtraInt("StaminaEColorG", 126),
        ExtraInt("StaminaEColorB", 0),
        ExtraInt("StaminaEColorA", 192),
        ExtraInt("HideMinimap", 0),
        ExtraInt("HideGoalAnnouncement", 0)
    };

    private static readonly CompetitiveSkatingKeyBindDefinition[] CompetitiveSkatingKeyBinds =
    {
        new("CompetitiveSkating_CarveBind", "CarveBind", "CARVE", "<Keyboard>/c"),
        new("CompetitiveSkating_LeftBind", "LeftBind", "LEFT LATERAL SKATE", "<Keyboard>/q"),
        new("CompetitiveSkating_RightBind", "RightBind", "RIGHT LATERAL SKATE", "<Keyboard>/e")
    };

    private static readonly Dictionary<string, MethodInfo?> NativeUpdateMethodCache = new(StringComparer.Ordinal);

    public static SettingsPreset CaptureCurrentPreset(string name)
    {
        DateTime now = DateTime.UtcNow;
        SettingsPreset preset = new SettingsPreset
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = SanitizePresetName(name),
            CreatedAtUtc = now.ToString("O"),
            UpdatedAtUtc = now.ToString("O")
        };

        CaptureNativeSettings(preset);
        CaptureKeyBinds(preset);
        CaptureCompetitiveSkatingKeyBinds(preset);
        CaptureExtraUiOptions(preset);

        return preset;
    }

    public static void OverwritePreset(SettingsPreset preset, string name)
    {
        preset.Name = SanitizePresetName(name);
        preset.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        preset.NativeSettings.Clear();
        preset.KeyBinds.Clear();
        preset.CompetitiveSkatingKeyBinds.Clear();
        preset.ExtraUiOptions.Clear();

        CaptureNativeSettings(preset);
        CaptureKeyBinds(preset);
        CaptureCompetitiveSkatingKeyBinds(preset);
        CaptureExtraUiOptions(preset);
    }

    public static SettingsPresetApplyResult ApplyPreset(SettingsPreset preset, SettingsPresetApplyMode mode)
    {
        SettingsPresetApplyResult result = new SettingsPresetApplyResult();
        ApplyNativeSettings(preset, mode, result);
        ApplyKeyBinds(preset, result);

        if (preset.CompetitiveSkatingKeyBinds.Count > 0)
        {
            ApplyCompetitiveSkatingKeyBinds(preset, result);
        }

        if (preset.ExtraUiOptions.Count > 0)
        {
            ApplyExtraUiOptions(preset, result);
        }

        foreach (string warning in result.Warnings)
        {
            Plugin.LogError($"Loadout '{preset.Name}' warning: {warning}");
        }

        return result;
    }

    public static string GetKeyDisplayName(string bindingPath)
    {
        if (string.IsNullOrWhiteSpace(bindingPath))
        {
            return "None";
        }

        int slashIndex = bindingPath.LastIndexOf('/');
        string text = slashIndex >= 0 ? bindingPath.Substring(slashIndex + 1) : bindingPath;
        if (string.IsNullOrWhiteSpace(text))
        {
            return bindingPath;
        }

        List<char> chars = new List<char>();
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (index == 0)
            {
                chars.Add(char.ToUpperInvariant(character));
            }
            else if (char.IsUpper(character))
            {
                chars.Add(' ');
                chars.Add(character);
            }
            else
            {
                chars.Add(character);
            }
        }

        return new string(chars.ToArray());
    }

    public static bool IsExtraUiOptionsLoaded()
    {
        return FindType("ItalianosExtraUIOptions.ExtraUIOptions") != null
               || FindType("SetFieldVisibility") != null;
    }

    public static bool HasStoredExtraUiOptions()
    {
        return ExtraUiOptions.Any(option => PlayerPrefs.HasKey(option.Key));
    }

    public static bool IsCompetitiveSkatingLoaded()
    {
        return FindType(CompetitiveSkatingPluginTypeName) != null;
    }

    public static bool HasStoredCompetitiveSkatingKeyBinds()
    {
        return CompetitiveSkatingKeyBinds.Any(binding => PlayerPrefs.HasKey(binding.Key));
    }

    public static IReadOnlyList<string> GetCapturedSettingSummary(SettingsPreset preset)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string key in preset.NativeSettings.Keys)
        {
            NativeSettingDefinition? definition = NativeSettings.FirstOrDefault(setting => setting.Id == key);
            string group = definition?.Group ?? "Other";
            counts.TryGetValue(group, out int count);
            counts[group] = count + 1;
        }

        List<string> parts = new List<string>();
        foreach (string group in new[] { "Game", "Controls", "Audio", "Video" })
        {
            if (counts.TryGetValue(group, out int count) && count > 0)
            {
                parts.Add($"{group}: {count}");
            }
        }

        if (preset.KeyBinds.Count > 0)
        {
            parts.Add($"Keybinds: {preset.KeyBinds.Count}");
        }

        if (preset.CompetitiveSkatingKeyBinds.Count > 0)
        {
            parts.Add($"Competitive Skating: {preset.CompetitiveSkatingKeyBinds.Count}");
        }

        if (preset.ExtraUiOptions.Count > 0)
        {
            parts.Add($"Extra UI: {preset.ExtraUiOptions.Count}");
        }

        return parts;
    }

    private static void CaptureNativeSettings(SettingsPreset preset)
    {
        Type settingsManagerType = typeof(SettingsManager);
        foreach (NativeSettingDefinition definition in NativeSettings)
        {
            FieldInfo? field = settingsManagerType.GetField(definition.Id, PublicStaticFlags);
            if (field == null)
            {
                continue;
            }

            object? rawValue = field.GetValue(null);
            if (rawValue == null)
            {
                continue;
            }

            preset.NativeSettings[definition.Id] = CreateStoredValue(rawValue, field.FieldType);
        }
    }

    private static void CaptureKeyBinds(SettingsPreset preset)
    {
        try
        {
            if (InputManager.RebindableInputActions.Count > 0)
            {
                InputManager.SaveKeyBinds();
            }

            foreach (KeyValuePair<string, KeyBind> entry in InputManager.KeyBinds)
            {
                KeyBind keyBind = entry.Value;
                preset.KeyBinds[entry.Key] = new SettingsPresetKeyBind
                {
                    ModifierPath = keyBind.ModifierPath ?? string.Empty,
                    Path = keyBind.Path ?? string.Empty,
                    Interactions = keyBind.Interactions ?? string.Empty
                };
            }
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to capture keybind settings: {exception}");
        }
    }

    private static void CaptureCompetitiveSkatingKeyBinds(SettingsPreset preset)
    {
        Type? pluginType = FindType(CompetitiveSkatingPluginTypeName);
        foreach (CompetitiveSkatingKeyBindDefinition binding in CompetitiveSkatingKeyBinds)
        {
            preset.CompetitiveSkatingKeyBinds[binding.Key] = new SettingsPresetPlayerPref
            {
                Kind = KindString,
                Value = ReadCompetitiveSkatingBinding(pluginType, binding)
            };
        }
    }

    private static void CaptureExtraUiOptions(SettingsPreset preset)
    {
        foreach (ExtraUiOptionDefinition option in ExtraUiOptions)
        {
            preset.ExtraUiOptions[option.Key] = option.Kind == KindFloat
                ? new SettingsPresetPlayerPref
                {
                    Kind = KindFloat,
                    Value = PlayerPrefs.GetFloat(option.Key, option.DefaultFloat).ToString(CultureInfo.InvariantCulture)
                }
                : new SettingsPresetPlayerPref
                {
                    Kind = KindInt,
                    Value = PlayerPrefs.GetInt(option.Key, option.DefaultInt).ToString(CultureInfo.InvariantCulture)
                };
        }
    }

    private static void ApplyNativeSettings(
        SettingsPreset preset,
        SettingsPresetApplyMode mode,
        SettingsPresetApplyResult result)
    {
        foreach (NativeSettingDefinition definition in NativeSettings)
        {
            if (mode == SettingsPresetApplyMode.LiveToggle && !definition.IncludeInLiveToggle)
            {
                continue;
            }

            if (!preset.NativeSettings.TryGetValue(definition.Id, out SettingsPresetValue? storedValue))
            {
                continue;
            }

            MethodInfo? updateMethod = GetNativeUpdateMethod(definition);
            ParameterInfo[]? parameters = updateMethod?.GetParameters();
            if (updateMethod == null || parameters == null || parameters.Length != 1)
            {
                result.Warnings.Add($"Native setting '{definition.Id}' is not available in this Puck build.");
                continue;
            }

            try
            {
                if (!TryConvertStoredValue(storedValue, parameters[0].ParameterType, out object? convertedValue))
                {
                    result.Warnings.Add($"Native setting '{definition.Id}' could not be converted from '{storedValue.Value}'.");
                    continue;
                }

                updateMethod.Invoke(null, new[] { convertedValue });
                result.NativeSettingsApplied++;
            }
            catch (Exception exception)
            {
                result.Warnings.Add($"Native setting '{definition.Id}' failed to apply: {exception.GetBaseException().Message}");
            }
        }
    }

    private static MethodInfo? GetNativeUpdateMethod(NativeSettingDefinition definition)
    {
        if (NativeUpdateMethodCache.TryGetValue(definition.Id, out MethodInfo? cachedMethod))
        {
            return cachedMethod;
        }

        MethodInfo? method = typeof(SettingsManager).GetMethod(definition.UpdateMethodName, PublicStaticFlags);
        NativeUpdateMethodCache[definition.Id] = method;
        return method;
    }

    private static void ApplyKeyBinds(SettingsPreset preset, SettingsPresetApplyResult result)
    {
        if (preset.KeyBinds.Count == 0)
        {
            return;
        }

        try
        {
            if (InputManager.RebindableInputActions.Count == 0)
            {
                result.Warnings.Add("Puck input actions are not ready yet, so keybinds were skipped.");
                return;
            }

            foreach (KeyValuePair<string, SettingsPresetKeyBind> entry in preset.KeyBinds)
            {
                SettingsPresetKeyBind keyBind = entry.Value;
                InputManager.RebindAction(entry.Key, EmptyToNull(keyBind.ModifierPath), EmptyToNull(keyBind.Path));
                InputManager.SetActionInteractions(entry.Key, keyBind.Interactions ?? string.Empty);
                result.KeyBindsApplied++;
            }

            InputManager.SaveKeyBinds();
        }
        catch (Exception exception)
        {
            result.Warnings.Add($"Keybinds failed to apply: {exception.GetBaseException().Message}");
        }
    }

    private static void ApplyCompetitiveSkatingKeyBinds(SettingsPreset preset, SettingsPresetApplyResult result)
    {
        Type? pluginType = FindType(CompetitiveSkatingPluginTypeName);
        bool appliedAny = false;

        foreach (CompetitiveSkatingKeyBindDefinition binding in CompetitiveSkatingKeyBinds)
        {
            if (!preset.CompetitiveSkatingKeyBinds.TryGetValue(binding.Key, out SettingsPresetPlayerPref? storedValue))
            {
                continue;
            }

            try
            {
                string value = storedValue.Value ?? binding.DefaultBinding;
                PlayerPrefs.SetString(binding.Key, value);
                TrySetCompetitiveSkatingBindingField(pluginType, binding, value, result);
                result.CompetitiveSkatingKeyBindsApplied++;
                appliedAny = true;
            }
            catch (Exception exception)
            {
                result.Warnings.Add($"Competitive Skating keybind '{binding.Key}' failed to apply: {exception.GetBaseException().Message}");
            }
        }

        if (!appliedAny)
        {
            return;
        }

        PlayerPrefs.Save();
        TryRefreshCompetitiveSkatingBindings(pluginType);
        TryRefreshCompetitiveSkatingSettingsMenu(preset);
    }

    private static void ApplyExtraUiOptions(SettingsPreset preset, SettingsPresetApplyResult result)
    {
        foreach (KeyValuePair<string, SettingsPresetPlayerPref> entry in preset.ExtraUiOptions)
        {
            try
            {
                if (entry.Value.Kind == KindFloat)
                {
                    float value = float.Parse(entry.Value.Value, CultureInfo.InvariantCulture);
                    PlayerPrefs.SetFloat(entry.Key, value);
                }
                else
                {
                    int value = int.Parse(entry.Value.Value, CultureInfo.InvariantCulture);
                    PlayerPrefs.SetInt(entry.Key, value);
                }

                result.ExtraUiOptionsApplied++;
            }
            catch (Exception exception)
            {
                result.Warnings.Add($"Extra UI option '{entry.Key}' failed to apply: {exception.GetBaseException().Message}");
            }
        }

        PlayerPrefs.Save();
        TryRefreshExtraUiOptions();
        TryRefreshExtraUiSettingsControls();
    }

    private static SettingsPresetValue CreateStoredValue(object rawValue, Type valueType)
    {
        string kind = KindString;
        string value;
        if (valueType == typeof(bool))
        {
            kind = KindBool;
            value = ((bool)rawValue).ToString(CultureInfo.InvariantCulture);
        }
        else if (valueType == typeof(int))
        {
            kind = KindInt;
            value = ((int)rawValue).ToString(CultureInfo.InvariantCulture);
        }
        else if (valueType == typeof(float))
        {
            kind = KindFloat;
            value = ((float)rawValue).ToString(CultureInfo.InvariantCulture);
        }
        else if (valueType.IsEnum)
        {
            kind = KindEnum;
            value = rawValue.ToString() ?? string.Empty;
        }
        else
        {
            value = Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return new SettingsPresetValue
        {
            Kind = kind,
            TypeName = valueType.FullName ?? string.Empty,
            Value = value
        };
    }

    private static bool TryConvertStoredValue(SettingsPresetValue storedValue, Type targetType, out object? convertedValue)
    {
        convertedValue = null;
        string value = storedValue.Value ?? string.Empty;
        try
        {
            if (targetType == typeof(bool))
            {
                convertedValue = bool.Parse(value);
                return true;
            }

            if (targetType == typeof(int))
            {
                convertedValue = int.Parse(value, CultureInfo.InvariantCulture);
                return true;
            }

            if (targetType == typeof(float))
            {
                convertedValue = float.Parse(value, CultureInfo.InvariantCulture);
                return true;
            }

            if (targetType == typeof(string))
            {
                convertedValue = value;
                return true;
            }

            if (targetType.IsEnum)
            {
                convertedValue = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue)
                    ? Enum.ToObject(targetType, intValue)
                    : Enum.Parse(targetType, value, ignoreCase: true);
                return true;
            }

            convertedValue = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            convertedValue = null;
            return false;
        }
    }

    private static string ReadCompetitiveSkatingBinding(Type? pluginType, CompetitiveSkatingKeyBindDefinition binding)
    {
        try
        {
            FieldInfo? field = pluginType?.GetField(binding.FieldName, AnyStaticFlags);
            if (field != null && field.FieldType == typeof(string))
            {
                object? rawValue = field.GetValue(null);
                if (rawValue is string value)
                {
                    return value;
                }
            }
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to read Competitive Skating keybind '{binding.Key}': {exception.GetBaseException().Message}");
        }

        return PlayerPrefs.GetString(binding.Key, binding.DefaultBinding);
    }

    private static void TrySetCompetitiveSkatingBindingField(
        Type? pluginType,
        CompetitiveSkatingKeyBindDefinition binding,
        string value,
        SettingsPresetApplyResult result)
    {
        if (pluginType == null)
        {
            return;
        }

        FieldInfo? field = pluginType.GetField(binding.FieldName, AnyStaticFlags);
        if (field == null || field.FieldType != typeof(string))
        {
            result.Warnings.Add($"Competitive Skating keybind field '{binding.FieldName}' is not available in this mod version.");
            return;
        }

        field.SetValue(null, value);
    }

    private static void TryRefreshCompetitiveSkatingBindings(Type? pluginType)
    {
        try
        {
            pluginType ??= FindType(CompetitiveSkatingPluginTypeName);
            MethodInfo? method = pluginType?.GetMethod("RefreshBindings", AnyStaticFlags);
            method?.Invoke(null, Array.Empty<object?>());
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to refresh Competitive Skating keybinds after loadout apply: {exception.GetBaseException().Message}");
        }
    }

    private static void TryRefreshCompetitiveSkatingSettingsMenu(SettingsPreset preset)
    {
        try
        {
            Type? uiType = FindType(CompetitiveSkatingUiTypeName);
            FieldInfo? keybindsContentField = uiType?.GetField("_keybindsContent", AnyStaticFlags);
            VisualElement? keybindsContent = keybindsContentField?.GetValue(null) as VisualElement;
            if (keybindsContent == null)
            {
                return;
            }

            foreach (CompetitiveSkatingKeyBindDefinition binding in CompetitiveSkatingKeyBinds)
            {
                if (!preset.CompetitiveSkatingKeyBinds.TryGetValue(binding.Key, out SettingsPresetPlayerPref? storedValue))
                {
                    continue;
                }

                Button? button = FindCompetitiveSkatingBindButton(keybindsContent, binding.MenuLabel);
                if (button != null)
                {
                    button.text = FormatCompetitiveSkatingPath(storedValue.Value ?? binding.DefaultBinding);
                }
            }
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to refresh Competitive Skating settings menu after loadout apply: {exception.GetBaseException().Message}");
        }
    }

    private static Button? FindCompetitiveSkatingBindButton(VisualElement keybindsContent, string actionLabel)
    {
        foreach (VisualElement row in keybindsContent.Children())
        {
            Label? label = null;
            Button? button = null;
            foreach (VisualElement child in row.Children())
            {
                label ??= child as Label;
                button ??= child as Button;
            }

            if (label != null
                && button != null
                && string.Equals(label.text, actionLabel, StringComparison.OrdinalIgnoreCase))
            {
                return button;
            }
        }

        foreach (VisualElement element in EnumerateDescendants(keybindsContent))
        {
            if (element is not Label label || !string.Equals(label.text, actionLabel, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            VisualElement? parent = label.parent;
            return parent?.Children().OfType<Button>().FirstOrDefault();
        }

        return null;
    }

    private static string FormatCompetitiveSkatingPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "NONE";
        }

        string[] parts = path.Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < parts.Length; index++)
        {
            string[] segments = parts[index].Split('/');
            string text = segments.Length > 0 ? segments[segments.Length - 1].ToUpperInvariant() : parts[index].ToUpperInvariant();
            switch (text)
            {
                case "LEFTBUTTON":
                    text = "L-CLICK";
                    break;
                case "RIGHTBUTTON":
                    text = "R-CLICK";
                    break;
                case "MIDDLEBUTTON":
                    text = "M-CLICK";
                    break;
                case "FORWARDBUTTON":
                    text = "MOUSE 4";
                    break;
                case "BACKBUTTON":
                    text = "MOUSE 5";
                    break;
            }

            parts[index] = text.Trim();
        }

        return string.Join(" + ", parts);
    }

    private static void TryRefreshExtraUiOptions()
    {
        try
        {
            Type? setFieldVisibilityType = FindType("SetFieldVisibility");
            MethodInfo? method = setFieldVisibilityType?.GetMethod("HideShowAll", PublicStaticFlags);
            ParameterInfo[]? parameters = method?.GetParameters();
            if (method == null)
            {
                return;
            }

            object?[] args = parameters != null && parameters.Length == 1
                ? new object?[] { true }
                : Array.Empty<object?>();
            method.Invoke(null, args);
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to refresh Extra UI Options after loadout apply: {exception.GetBaseException().Message}");
        }
    }

    private static void TryRefreshExtraUiSettingsControls()
    {
        try
        {
            UISettings? uiSettings = MonoBehaviourSingleton<UIManager>.Instance?.Settings;
            FieldInfo? settingsField = typeof(UISettings).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic);
            VisualElement? settingsRoot = settingsField?.GetValue(uiSettings) as VisualElement;
            if (settingsRoot == null)
            {
                return;
            }

            SyncToggle(settingsRoot, "DISABLE CHAT", "HideChat");
            SyncToggle(settingsRoot, "HIDE SCOREBOARD", "HideScoreboard");
            SyncToggle(settingsRoot, "SHOW SCOREBOARD ON TAB", "ShowScoreboardOnTab");
            SyncToggle(settingsRoot, "HIDE SPEED", "HideSpeed");
            SyncToggle(settingsRoot, "SPEED TEXT COLOR BASED ON STAMINA", "SpeedStaminaBasedColor");
            SyncToggle(settingsRoot, "HIDE STAMINA", "HideStamina");
            SyncToggle(settingsRoot, "HIDE MINIMAP", "HideMinimap");
            SyncToggle(settingsRoot, "HIDE GOAL ANNOUNCEMENT", "HideGoalAnnouncement");

            SyncFloatRow(settingsRoot, "CHAT HORIZONTAL POSITION", "ChatHorizontal");
            SyncFloatRow(settingsRoot, "CHAT VERTICAL POSITION", "ChatVertical");
            SyncFloatRow(settingsRoot, "SCOREBOARD HORIZONTAL POSITION", "ScoreboardHorizontal");
            SyncFloatRow(settingsRoot, "SCOREBOARD VERTICAL POSITION", "ScoreboardVertical");
            SyncFloatRow(settingsRoot, "SPEED HORIZONTAL POSITION", "SpeedHorizontal");
            SyncFloatRow(settingsRoot, "SPEED VERTICAL POSITION", "SpeedVertical");
            SyncFloatRow(settingsRoot, "SPEED SIZE", "SpeedSize");
            SyncFloatRow(settingsRoot, "STAMINA HORIZONTAL POSITION", "StaminaHorizontal");
            SyncFloatRow(settingsRoot, "STAMINA VERTICAL POSITION", "StaminaVertical");
            SyncFloatRow(settingsRoot, "STAMINA SIZE", "StaminaSIZE");

            SyncRotationDropdown(settingsRoot);
            SyncColorRow(settingsRoot, "SPEED TEXT COLOR", "SpeedColorR", "SpeedColorG", "SpeedColorB", "SpeedColorA");
            SyncColorRow(settingsRoot, "STAMINA COLOR", "StaminaColorR", "StaminaColorG", "StaminaColorB", "StaminaColorA");
            SyncColorRow(settingsRoot, "STAMINA EXHAUSTION COLOR", "StaminaEColorR", "StaminaEColorG", "StaminaEColorB", "StaminaEColorA");
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to refresh Extra UI Options settings controls: {exception.GetBaseException().Message}");
        }
    }

    private static void SyncToggle(VisualElement settingsRoot, string labelText, string prefsKey)
    {
        VisualElement? row = FindRowByLabel(settingsRoot, labelText);
        Toggle? toggle = row == null ? null : FindDescendant<Toggle>(row);
        if (toggle != null)
        {
            toggle.value = PlayerPrefs.GetInt(prefsKey, 0) == 1;
        }
    }

    private static void SyncFloatRow(VisualElement settingsRoot, string labelText, string prefsKey)
    {
        VisualElement? row = FindRowByLabel(settingsRoot, labelText);
        if (row == null)
        {
            return;
        }

        float value = PlayerPrefs.GetFloat(prefsKey, 0f);
        FloatField? field = FindDescendant<FloatField>(row);
        Slider? slider = FindDescendant<Slider>(row);
        if (field != null)
        {
            field.value = value;
        }

        if (slider != null)
        {
            slider.value = value;
        }
    }

    private static void SyncRotationDropdown(VisualElement settingsRoot)
    {
        VisualElement? row = FindRowByLabel(settingsRoot, "STAMINA ROTATION");
        PopupField<string>? popup = row == null ? null : FindDescendant<PopupField<string>>(row);
        if (popup == null)
        {
            return;
        }

        float value = PlayerPrefs.GetFloat("StaminaRotation", 0f);
        popup.value = $"{Mathf.RoundToInt(value)} Degrees";
    }

    private static void SyncColorRow(
        VisualElement settingsRoot,
        string labelText,
        string redKey,
        string greenKey,
        string blueKey,
        string alphaKey)
    {
        VisualElement? row = FindRowByLabel(settingsRoot, labelText);
        if (row == null)
        {
            return;
        }

        List<IntegerField> fields = FindDescendants<IntegerField>(row);
        if (fields.Count < 4)
        {
            return;
        }

        fields[0].value = PlayerPrefs.GetInt(redKey, 255);
        fields[1].value = PlayerPrefs.GetInt(greenKey, 255);
        fields[2].value = PlayerPrefs.GetInt(blueKey, 255);
        fields[3].value = PlayerPrefs.GetInt(alphaKey, 255);
    }

    private static VisualElement? FindRowByLabel(VisualElement root, string labelText)
    {
        foreach (VisualElement element in EnumerateDescendants(root))
        {
            if (element is Label label && string.Equals(label.text, labelText, StringComparison.Ordinal))
            {
                return label.parent;
            }
        }

        return null;
    }

    private static T? FindDescendant<T>(VisualElement root) where T : VisualElement
    {
        foreach (VisualElement element in EnumerateDescendants(root))
        {
            if (element is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static List<T> FindDescendants<T>(VisualElement root) where T : VisualElement
    {
        List<T> matches = new List<T>();
        foreach (VisualElement element in EnumerateDescendants(root))
        {
            if (element is T match)
            {
                matches.Add(match);
            }
        }

        return matches;
    }

    private static IEnumerable<VisualElement> EnumerateDescendants(VisualElement root)
    {
        foreach (VisualElement child in root.Children())
        {
            yield return child;

            foreach (VisualElement descendant in EnumerateDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static Type? FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(fullName, throwOnError: false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    private static string SanitizePresetName(string name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "Loadout" : trimmed;
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static ExtraUiOptionDefinition ExtraInt(string key, int defaultValue)
    {
        return new ExtraUiOptionDefinition(key, KindInt, defaultValue, 0f);
    }

    private static ExtraUiOptionDefinition ExtraFloat(string key, float defaultValue)
    {
        return new ExtraUiOptionDefinition(key, KindFloat, 0, defaultValue);
    }

    private sealed class NativeSettingDefinition
    {
        public NativeSettingDefinition(string id, string group, bool includeInLiveToggle)
        {
            Id = id;
            Group = group;
            IncludeInLiveToggle = includeInLiveToggle;
            UpdateMethodName = $"Update{id}";
        }

        public string Id { get; }

        public string Group { get; }

        public bool IncludeInLiveToggle { get; }

        public string UpdateMethodName { get; }
    }

    private sealed class ExtraUiOptionDefinition
    {
        public ExtraUiOptionDefinition(string key, string kind, int defaultInt, float defaultFloat)
        {
            Key = key;
            Kind = kind;
            DefaultInt = defaultInt;
            DefaultFloat = defaultFloat;
        }

        public string Key { get; }

        public string Kind { get; }

        public int DefaultInt { get; }

        public float DefaultFloat { get; }
    }

    private sealed class CompetitiveSkatingKeyBindDefinition
    {
        public CompetitiveSkatingKeyBindDefinition(string key, string fieldName, string menuLabel, string defaultBinding)
        {
            Key = key;
            FieldName = fieldName;
            MenuLabel = menuLabel;
            DefaultBinding = defaultBinding;
        }

        public string Key { get; }

        public string FieldName { get; }

        public string MenuLabel { get; }

        public string DefaultBinding { get; }
    }
}
