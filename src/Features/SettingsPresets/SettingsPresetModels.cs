using System;
using System.Collections.Generic;

namespace OPLServerBanners;

internal sealed class SettingsPresetConfig
{
    public int SchemaVersion { get; set; } = 1;

    public string ToggleKeybind { get; set; } = "<Keyboard>/f9";

    public string GoalieTrackCycleKeybind { get; set; } = "<Keyboard>/f10";

    public SettingsPresetKeyBind? GoalieTrackCycleNativeKeyBind { get; set; }

    public string WarmupObstacleEditorKeybind { get; set; } = "<Keyboard>/f3";

    public string TogglePresetAId { get; set; } = string.Empty;

    public string TogglePresetBId { get; set; } = string.Empty;

    public List<SettingsPreset> Presets { get; set; } = new();
}

internal sealed class SettingsPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Loadout";

    public string CreatedAtUtc { get; set; } = DateTime.UtcNow.ToString("O");

    public string UpdatedAtUtc { get; set; } = DateTime.UtcNow.ToString("O");

    public Dictionary<string, SettingsPresetValue> NativeSettings { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, SettingsPresetKeyBind> KeyBinds { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, SettingsPresetPlayerPref> CompetitiveSkatingKeyBinds { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, SettingsPresetPlayerPref> ExtraUiOptions { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class SettingsPresetValue
{
    public string Kind { get; set; } = "string";

    public string TypeName { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

internal sealed class SettingsPresetKeyBind
{
    public string ModifierPath { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Interactions { get; set; } = string.Empty;
}

internal sealed class SettingsPresetPlayerPref
{
    public string Kind { get; set; } = "int";

    public string Value { get; set; } = string.Empty;
}

internal enum SettingsPresetApplyMode
{
    Full,
    LiveToggle
}

internal sealed class SettingsPresetApplyResult
{
    public int NativeSettingsApplied { get; set; }

    public int KeyBindsApplied { get; set; }

    public int CompetitiveSkatingKeyBindsApplied { get; set; }

    public int ExtraUiOptionsApplied { get; set; }

    public List<string> Warnings { get; } = new();

    public bool Success => Warnings.Count == 0;

    public string BuildSummary(SettingsPreset preset, SettingsPresetApplyMode mode)
    {
        string modeLabel = mode == SettingsPresetApplyMode.LiveToggle ? "live toggle" : "full apply";
        string summary = $"Applied '{preset.Name}' ({modeLabel}): {NativeSettingsApplied} settings, {KeyBindsApplied} keybinds";
        if (ExtraUiOptionsApplied > 0)
        {
            summary += $", {ExtraUiOptionsApplied} Extra UI options";
        }

        if (CompetitiveSkatingKeyBindsApplied > 0)
        {
            summary += $", {CompetitiveSkatingKeyBindsApplied} Competitive Skating keybinds";
        }

        if (Warnings.Count > 0)
        {
            summary += $". {Warnings.Count} warning(s); check the log for details.";
        }
        else
        {
            summary += ".";
        }

        return summary;
    }
}
