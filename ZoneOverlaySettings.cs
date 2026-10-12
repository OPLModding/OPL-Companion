#nullable disable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace OPLServerBanners;

internal static class ZoneOverlaySettings
{
    private const string KeyPrefix = "OPLServerBanners.ZoneOverlay.";
    private const string EnabledKey = KeyPrefix + "Enabled";
    private const string ToggleBindingKey = KeyPrefix + "ToggleBinding";
    private const string ShowWorldLabelsKey = KeyPrefix + "ShowWorldZoneLabels";
    private const string ShowMinimapLabelsKey = KeyPrefix + "ShowMinimapZoneLabels";
    private const string WorldTextSizeKey = KeyPrefix + "WorldTextSize";
    private const string MinimapTextSizeKey = KeyPrefix + "MinimapTextSize";
    private const string ZoneOpacityKey = KeyPrefix + "ZoneOpacity";
    private const string OutlineThicknessKey = KeyPrefix + "OutlineThickness";
    private const string DisplayModeKey = KeyPrefix + "DisplayMode";
    private const string DefaultsVersionKey = KeyPrefix + "DefaultsVersion";
    private const string ZoneColorKeyPrefix = KeyPrefix + "ZoneColor.";
    private const string ZoneTextColorKeyPrefix = KeyPrefix + "ZoneTextColor.";
    private const string ZoneTextOutlineColorKeyPrefix = KeyPrefix + "ZoneTextOutlineColor.";
    private const string DefaultToggleBinding = "<Keyboard>/k";
    private const string DefaultZoneTextColorHex = "#000000";
    private const int CurrentDefaultsVersion = 3;

    public const float DefaultWorldTextSize = 1.5f;
    public const float MinWorldTextSize = 0.5f;
    public const float MaxWorldTextSize = 2f;
    public const float DefaultMinimapTextSize = 10f;
    public const float MinMinimapTextSize = 5f;
    public const float MaxMinimapTextSize = 16f;
    public const float DefaultZoneOpacity = 0.7f;
    public const float MinZoneOpacity = 0f;
    public const float MaxZoneOpacity = 1f;
    public const float DefaultOutlineThickness = 0.15f;
    public const float MinOutlineThickness = 0.04f;
    public const float MaxOutlineThickness = 0.8f;

    private static bool loaded;
    private static bool enabled;
    private static bool showWorldZoneLabels = true;
    private static bool showMinimapZoneLabels = true;
    private static string toggleBinding = DefaultToggleBinding;
    private static float worldTextSize = DefaultWorldTextSize;
    private static float minimapTextSize = DefaultMinimapTextSize;
    private static float zoneOpacity = DefaultZoneOpacity;
    private static float outlineThickness = DefaultOutlineThickness;
    private static ZoneOverlayDisplayMode displayMode = ZoneOverlayDisplayMode.WorldAndMinimap;
    private static readonly string[] ZoneIds =
    {
        "Z1",
        "Z2",
        "Z3",
        "Z4",
        "Z5",
        "Z6"
    };

    private static readonly Dictionary<string, string> zoneColorHexes = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> zoneTextColorHexes = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> zoneTextOutlineColorHexes = new(StringComparer.Ordinal);

    public static event Action Changed;

    public static bool Enabled
    {
        get
        {
            EnsureLoaded();
            return enabled;
        }
    }

    public static bool ShowWorldZoneLabels
    {
        get
        {
            EnsureLoaded();
            return showWorldZoneLabels;
        }
    }

    public static bool ShowMinimapZoneLabels
    {
        get
        {
            EnsureLoaded();
            return showMinimapZoneLabels;
        }
    }

    public static string ToggleBinding
    {
        get
        {
            EnsureLoaded();
            return toggleBinding;
        }
    }

    public static float WorldTextSize
    {
        get
        {
            EnsureLoaded();
            return worldTextSize;
        }
    }

    public static float MinimapTextSize
    {
        get
        {
            EnsureLoaded();
            return minimapTextSize;
        }
    }

    public static float ZoneOpacity
    {
        get
        {
            EnsureLoaded();
            return zoneOpacity;
        }
    }

    public static float OutlineThickness
    {
        get
        {
            EnsureLoaded();
            return outlineThickness;
        }
    }

    public static ZoneOverlayDisplayMode DisplayMode
    {
        get
        {
            EnsureLoaded();
            return displayMode;
        }
    }

    public static bool ShowOnWorld
    {
        get
        {
            EnsureLoaded();
            return displayMode != ZoneOverlayDisplayMode.Minimap;
        }
    }

    public static bool ShowOnMinimap
    {
        get
        {
            EnsureLoaded();
            return displayMode != ZoneOverlayDisplayMode.World;
        }
    }

    public static void Load()
    {
        MigrateDefaultValues();
        enabled = PlayerPrefs.GetInt(EnabledKey, 0) != 0;
        showWorldZoneLabels = PlayerPrefs.GetInt(ShowWorldLabelsKey, 1) != 0;
        showMinimapZoneLabels = PlayerPrefs.GetInt(ShowMinimapLabelsKey, 1) != 0;
        worldTextSize = Clamp(PlayerPrefs.GetFloat(WorldTextSizeKey, DefaultWorldTextSize), MinWorldTextSize, MaxWorldTextSize);
        minimapTextSize = Clamp(PlayerPrefs.GetFloat(MinimapTextSizeKey, DefaultMinimapTextSize), MinMinimapTextSize, MaxMinimapTextSize);
        zoneOpacity = Clamp(PlayerPrefs.GetFloat(ZoneOpacityKey, DefaultZoneOpacity), MinZoneOpacity, MaxZoneOpacity);
        outlineThickness = Clamp(PlayerPrefs.GetFloat(OutlineThicknessKey, DefaultOutlineThickness), MinOutlineThickness, MaxOutlineThickness);
        displayMode = ParseDisplayMode(PlayerPrefs.GetString(DisplayModeKey, ZoneOverlayDisplayMode.WorldAndMinimap.ToString()));
        toggleBinding = PlayerPrefs.GetString(ToggleBindingKey, DefaultToggleBinding);
        if (string.IsNullOrWhiteSpace(toggleBinding))
        {
            toggleBinding = DefaultToggleBinding;
        }

        LoadColorSettings();
        loaded = true;
    }

    private static void MigrateDefaultValues()
    {
        if (PlayerPrefs.GetInt(DefaultsVersionKey, 0) >= CurrentDefaultsVersion)
        {
            return;
        }

        MigrateFloatDefault(WorldTextSizeKey, 1f, DefaultWorldTextSize);
        MigrateFloatDefault(MinimapTextSizeKey, 8f, DefaultMinimapTextSize);
        MigrateFloatDefault(ZoneOpacityKey, 0.6f, DefaultZoneOpacity);
        MigrateFloatDefault(OutlineThicknessKey, 0.24f, DefaultOutlineThickness);
        MigrateFloatDefault(OutlineThicknessKey, 0.14f, DefaultOutlineThickness);
        PlayerPrefs.SetInt(DefaultsVersionKey, CurrentDefaultsVersion);
        PlayerPrefs.Save();
    }

    private static void MigrateFloatDefault(string key, float oldDefault, float newDefault)
    {
        if (!PlayerPrefs.HasKey(key) || Mathf.Abs(PlayerPrefs.GetFloat(key) - oldDefault) < 0.001f)
        {
            PlayerPrefs.SetFloat(key, newDefault);
        }
    }

    public static void SetEnabled(bool value)
    {
        EnsureLoaded();
        if (enabled == value)
        {
            return;
        }

        enabled = value;
        PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
        SaveAndNotify();
    }

    public static void ToggleEnabled()
    {
        SetEnabled(!Enabled);
    }

    public static void SetShowWorldZoneLabels(bool value)
    {
        EnsureLoaded();
        if (showWorldZoneLabels == value)
        {
            return;
        }

        showWorldZoneLabels = value;
        PlayerPrefs.SetInt(ShowWorldLabelsKey, value ? 1 : 0);
        SaveAndNotify();
    }

    public static void SetShowMinimapZoneLabels(bool value)
    {
        EnsureLoaded();
        if (showMinimapZoneLabels == value)
        {
            return;
        }

        showMinimapZoneLabels = value;
        PlayerPrefs.SetInt(ShowMinimapLabelsKey, value ? 1 : 0);
        SaveAndNotify();
    }

    public static void SetToggleBinding(string bindingPath)
    {
        EnsureLoaded();
        string nextBinding = string.IsNullOrWhiteSpace(bindingPath) ? DefaultToggleBinding : bindingPath;
        if (string.Equals(toggleBinding, nextBinding, StringComparison.Ordinal))
        {
            return;
        }

        toggleBinding = nextBinding;
        PlayerPrefs.SetString(ToggleBindingKey, nextBinding);
        SaveAndNotify();
    }

    public static void SetWorldTextSize(float value)
    {
        EnsureLoaded();
        float nextValue = Clamp(value, MinWorldTextSize, MaxWorldTextSize);
        if (Mathf.Abs(worldTextSize - nextValue) < 0.001f)
        {
            return;
        }

        worldTextSize = nextValue;
        PlayerPrefs.SetFloat(WorldTextSizeKey, nextValue);
        SaveAndNotify();
    }

    public static void SetMinimapTextSize(float value)
    {
        EnsureLoaded();
        float nextValue = Clamp(value, MinMinimapTextSize, MaxMinimapTextSize);
        if (Mathf.Abs(minimapTextSize - nextValue) < 0.001f)
        {
            return;
        }

        minimapTextSize = nextValue;
        PlayerPrefs.SetFloat(MinimapTextSizeKey, nextValue);
        SaveAndNotify();
    }

    public static void SetZoneOpacity(float value)
    {
        EnsureLoaded();
        float nextValue = Clamp(value, MinZoneOpacity, MaxZoneOpacity);
        if (Mathf.Abs(zoneOpacity - nextValue) < 0.001f)
        {
            return;
        }

        zoneOpacity = nextValue;
        PlayerPrefs.SetFloat(ZoneOpacityKey, nextValue);
        SaveAndNotify();
    }

    public static void SetOutlineThickness(float value)
    {
        EnsureLoaded();
        float nextValue = Clamp(value, MinOutlineThickness, MaxOutlineThickness);
        if (Mathf.Abs(outlineThickness - nextValue) < 0.001f)
        {
            return;
        }

        outlineThickness = nextValue;
        PlayerPrefs.SetFloat(OutlineThicknessKey, nextValue);
        SaveAndNotify();
    }

    public static void SetDisplayMode(ZoneOverlayDisplayMode value)
    {
        EnsureLoaded();
        if (displayMode == value)
        {
            return;
        }

        displayMode = value;
        PlayerPrefs.SetString(DisplayModeKey, value.ToString());
        SaveAndNotify();
    }

    public static string GetZoneColorHex(string zoneId)
    {
        EnsureLoaded();
        return zoneColorHexes.TryGetValue(zoneId, out string value) ? value : GetDefaultZoneColorHex(zoneId);
    }

    public static Color GetZoneColor(string zoneId)
    {
        return ParseHexColor(GetZoneColorHex(zoneId), GetDefaultZoneColorHex(zoneId));
    }

    public static bool SetZoneColorHex(string zoneId, string value)
    {
        return SetStoredHexColor(zoneColorHexes, GetZoneColorKey(zoneId), zoneId, value);
    }

    public static string GetZoneTextColorHex(string zoneId)
    {
        EnsureLoaded();
        return zoneTextColorHexes.TryGetValue(zoneId, out string value) ? value : DefaultZoneTextColorHex;
    }

    public static Color GetZoneTextColor(string zoneId)
    {
        return ParseHexColor(GetZoneTextColorHex(zoneId), DefaultZoneTextColorHex);
    }

    public static bool SetZoneTextColorHex(string zoneId, string value)
    {
        return SetStoredHexColor(zoneTextColorHexes, GetZoneTextColorKey(zoneId), zoneId, value);
    }

    public static string GetZoneTextOutlineColorHex(string zoneId)
    {
        EnsureLoaded();
        return zoneTextOutlineColorHexes.TryGetValue(zoneId, out string value) ? value : GetZoneColorHex(zoneId);
    }

    public static Color GetZoneTextOutlineColor(string zoneId)
    {
        return ParseHexColor(GetZoneTextOutlineColorHex(zoneId), GetZoneColorHex(zoneId));
    }

    public static bool SetZoneTextOutlineColorHex(string zoneId, string value)
    {
        return SetStoredHexColor(zoneTextOutlineColorHexes, GetZoneTextOutlineColorKey(zoneId), zoneId, value);
    }

    public static bool IsValidHexColor(string value)
    {
        return TryNormalizeHex(value, out _);
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

        List<char> chars = new();
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

    private static void EnsureLoaded()
    {
        if (!loaded)
        {
            Load();
        }
    }

    private static void SaveAndNotify()
    {
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    private static float Clamp(float value, float min, float max)
    {
        return Mathf.Clamp(value, min, max);
    }

    private static ZoneOverlayDisplayMode ParseDisplayMode(string value)
    {
        if (Enum.TryParse(value, out ZoneOverlayDisplayMode parsed))
        {
            return parsed;
        }

        return ZoneOverlayDisplayMode.WorldAndMinimap;
    }

    private static void LoadColorSettings()
    {
        zoneColorHexes.Clear();
        zoneTextColorHexes.Clear();
        zoneTextOutlineColorHexes.Clear();

        for (int i = 0; i < ZoneIds.Length; i++)
        {
            string zoneId = ZoneIds[i];
            zoneColorHexes[zoneId] = LoadHexColor(GetZoneColorKey(zoneId), GetDefaultZoneColorHex(zoneId));
            zoneTextColorHexes[zoneId] = LoadHexColor(GetZoneTextColorKey(zoneId), DefaultZoneTextColorHex);

            string outlineKey = GetZoneTextOutlineColorKey(zoneId);
            if (!PlayerPrefs.HasKey(outlineKey))
            {
                continue;
            }

            string outlineFallback = zoneColorHexes.TryGetValue(zoneId, out string zoneColorHex)
                ? zoneColorHex
                : GetDefaultZoneColorHex(zoneId);
            if (TryNormalizeHex(PlayerPrefs.GetString(outlineKey, outlineFallback), out string normalizedOutline))
            {
                zoneTextOutlineColorHexes[zoneId] = normalizedOutline;
            }
            else
            {
                PlayerPrefs.DeleteKey(outlineKey);
            }
        }
    }

    private static string LoadHexColor(string key, string fallback)
    {
        string raw = PlayerPrefs.GetString(key, fallback);
        return TryNormalizeHex(raw, out string normalized) ? normalized : fallback;
    }

    private static bool SetStoredHexColor(Dictionary<string, string> store, string key, string zoneId, string value)
    {
        EnsureLoaded();
        if (!IsKnownZoneId(zoneId) || !TryNormalizeHex(value, out string normalized))
        {
            return false;
        }

        if (store.TryGetValue(zoneId, out string current) && string.Equals(current, normalized, StringComparison.Ordinal))
        {
            return true;
        }

        store[zoneId] = normalized;
        PlayerPrefs.SetString(key, normalized);
        SaveAndNotify();
        return true;
    }

    private static Color ParseHexColor(string value, string fallback)
    {
        if (!ColorUtility.TryParseHtmlString(value, out Color color)
            && !ColorUtility.TryParseHtmlString(fallback, out color))
        {
            color = Color.white;
        }

        return new Color(color.r, color.g, color.b, 1f);
    }

    private static bool TryNormalizeHex(string value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string text = value.Trim();
        if (text.StartsWith("#", StringComparison.Ordinal))
        {
            text = text.Substring(1);
        }

        if (text.Length != 3 && text.Length != 4 && text.Length != 6 && text.Length != 8)
        {
            return false;
        }

        for (int i = 0; i < text.Length; i++)
        {
            if (!IsHexDigit(text[i]))
            {
                return false;
            }
        }

        if (!ColorUtility.TryParseHtmlString("#" + text, out Color color))
        {
            return false;
        }

        normalized = "#" + ColorUtility.ToHtmlStringRGB(color);
        return true;
    }

    private static bool IsHexDigit(char character)
    {
        return (character >= '0' && character <= '9')
               || (character >= 'a' && character <= 'f')
               || (character >= 'A' && character <= 'F');
    }

    private static bool IsKnownZoneId(string zoneId)
    {
        for (int i = 0; i < ZoneIds.Length; i++)
        {
            if (string.Equals(ZoneIds[i], zoneId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetDefaultZoneColorHex(string zoneId)
    {
        return zoneId switch
        {
            "Z1" => "#50C878",
            "Z2" => "#6395EE",
            "Z3" => "#FFDE21",
            "Z4" => "#E28A5A",
            "Z5" => "#DA2C43",
            "Z6" => "#9768E2",
            _ => "#FFFFFF"
        };
    }

    private static string GetZoneColorKey(string zoneId)
    {
        return ZoneColorKeyPrefix + zoneId;
    }

    private static string GetZoneTextColorKey(string zoneId)
    {
        return ZoneTextColorKeyPrefix + zoneId;
    }

    private static string GetZoneTextOutlineColorKey(string zoneId)
    {
        return ZoneTextOutlineColorKeyPrefix + zoneId;
    }
}

internal enum ZoneOverlayDisplayMode
{
    WorldAndMinimap,
    World,
    Minimap
}
