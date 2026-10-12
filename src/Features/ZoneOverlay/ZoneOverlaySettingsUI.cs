#nullable disable

using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace OPLServerBanners;

internal static class ZoneOverlaySettingsUI
{
    private static bool isListeningForToggleKey;

    public static bool IsListeningForToggleKey => isListeningForToggleKey;

    public static void StartListeningForToggleKey()
    {
        isListeningForToggleKey = true;
        SettingsPresetTabUI.Refresh("Press any keyboard key for the zone overlay toggle, or Esc to cancel.", "info");
    }

    public static void TickKeyListening()
    {
        if (!isListeningForToggleKey)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (((ButtonControl)keyboard.escapeKey).wasPressedThisFrame)
        {
            isListeningForToggleKey = false;
            SettingsPresetTabUI.Refresh("Zone toggle keybind change cancelled.", "info");
            return;
        }

        if (!((ButtonControl)keyboard.anyKey).wasPressedThisFrame)
        {
            return;
        }

        foreach (KeyControl key in keyboard.allKeys)
        {
            if (!((ButtonControl)key).wasPressedThisFrame)
            {
                continue;
            }

            string bindingPath = "<Keyboard>/" + key.name;
            ZoneOverlaySettings.SetToggleBinding(bindingPath);
            isListeningForToggleKey = false;
            SettingsPresetTabUI.Refresh($"Zone toggle key set to {ZoneOverlaySettings.GetKeyDisplayName(bindingPath)}.", "success");
            return;
        }
    }

    public static void CancelKeyListening()
    {
        if (!isListeningForToggleKey)
        {
            return;
        }

        isListeningForToggleKey = false;
        SettingsPresetTabUI.Refresh();
    }
}
