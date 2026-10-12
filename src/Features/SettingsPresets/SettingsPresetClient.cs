using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace OPLServerBanners;

internal static class SettingsPresetClient
{
    private const float ToggleCooldownSeconds = 0.25f;
    private const float SettingsTabInjectionRetrySeconds = 1f;

    private static InputAction? toggleAction;
    private static bool isListeningForToggleKey;
    private static bool isListeningForWarmupObstacleEditorKey;
    private static string lastAppliedTogglePresetId = string.Empty;
    private static float lastToggleAt;
    private static float nextSettingsTabInjectionCheckAt;

    public static bool IsListeningForToggleKey => isListeningForToggleKey;
    public static bool IsListeningForWarmupObstacleEditorKey => isListeningForWarmupObstacleEditorKey;

    public static void Initialize()
    {
        SettingsPresetStore.Load();
        RecreateToggleAction();
    }

    public static void Shutdown()
    {
        toggleAction?.Disable();
        toggleAction?.Dispose();
        toggleAction = null;
        isListeningForToggleKey = false;
        isListeningForWarmupObstacleEditorKey = false;
        lastAppliedTogglePresetId = string.Empty;
        lastToggleAt = 0f;
        nextSettingsTabInjectionCheckAt = 0f;
    }

    public static void Tick(global::PlayerInput playerInput)
    {
        if (!IsLocalPlayerInput(playerInput))
        {
            return;
        }

        HandleToggleHotkey();
    }

    public static void TickUi()
    {
        EnsureSettingsTabInjectedIfNeeded();

        if (isListeningForToggleKey)
        {
            HandleToggleKeyListening();
        }

#if WARMUP_OBSTACLES
        if (isListeningForWarmupObstacleEditorKey)
        {
            HandleWarmupObstacleEditorKeyListening();
        }
#endif
    }

    private static void EnsureSettingsTabInjectedIfNeeded()
    {
        if (SettingsPresetTabUI.HasInjectedContent ||
            Time.unscaledTime < nextSettingsTabInjectionCheckAt)
        {
            return;
        }

        nextSettingsTabInjectionCheckAt = Time.unscaledTime + SettingsTabInjectionRetrySeconds;
        EnsureSettingsTabInjected();
    }

    public static void StartListeningForToggleKey()
    {
        GoalieWarmupPuckTracking.CancelCycleKeyListening();
        isListeningForWarmupObstacleEditorKey = false;
        isListeningForToggleKey = true;
    }

    public static void CancelToggleKeyListening()
    {
        isListeningForToggleKey = false;
    }

    public static void StartListeningForWarmupObstacleEditorKey()
    {
        GoalieWarmupPuckTracking.CancelCycleKeyListening();
        isListeningForToggleKey = false;
        isListeningForWarmupObstacleEditorKey = true;
    }

    public static void CancelWarmupObstacleEditorKeyListening()
    {
        isListeningForWarmupObstacleEditorKey = false;
    }

    public static SettingsPresetApplyResult? ToggleSelectedPresets()
    {
        SettingsPresetConfig config = SettingsPresetStore.Config;
        SettingsPreset? presetA = SettingsPresetStore.FindPreset(config.TogglePresetAId);
        SettingsPreset? presetB = SettingsPresetStore.FindPreset(config.TogglePresetBId);
        if (presetA == null || presetB == null)
        {
            SettingsPresetTabUI.SetStatus("Choose both toggle loadouts before using the live toggle.", "warning");
            return null;
        }

        if (string.Equals(presetA.Id, presetB.Id, StringComparison.Ordinal))
        {
            SettingsPresetTabUI.SetStatus("Choose two different loadouts for live toggle A/B.", "warning");
            return null;
        }

        SettingsPreset target = string.Equals(lastAppliedTogglePresetId, presetA.Id, StringComparison.Ordinal)
            ? presetB
            : presetA;

        SettingsPresetApplyResult result = SettingsPresetRuntime.ApplyPreset(target, SettingsPresetApplyMode.LiveToggle);
        lastAppliedTogglePresetId = target.Id;

        string message = result.BuildSummary(target, SettingsPresetApplyMode.LiveToggle);
        SettingsPresetTabUI.SetStatus($"Applied \"{target.Name}\"", result.Success ? "success" : "warning");
        Plugin.Log(message);
        return result;
    }

    private static void HandleToggleHotkey()
    {
        if (toggleAction == null || isListeningForToggleKey || isListeningForWarmupObstacleEditorKey)
        {
            return;
        }

        if (!toggleAction.WasPressedThisFrame() || Time.unscaledTime - lastToggleAt < ToggleCooldownSeconds)
        {
            return;
        }

        if (IsUiBlockingHotkey())
        {
            return;
        }

        lastToggleAt = Time.unscaledTime;
        ToggleSelectedPresets();
    }

    private static void EnsureSettingsTabInjected()
    {
        try
        {
            UISettings? settings = MonoBehaviourSingleton<UIManager>.Instance?.Settings;
            if (settings != null)
            {
                SettingsPresetTabUI.Inject(settings);
            }
        }
        catch
        {
        }
    }

    private static void HandleToggleKeyListening()
    {
        Keyboard? keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (((ButtonControl)keyboard.escapeKey).wasPressedThisFrame)
        {
            isListeningForToggleKey = false;
            SettingsPresetTabUI.Refresh("Toggle keybind change cancelled.", "info");
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
            SettingsPresetStore.Config.ToggleKeybind = bindingPath;
            SettingsPresetStore.Save();
            RecreateToggleAction();
            isListeningForToggleKey = false;
            SettingsPresetTabUI.Refresh($"Toggle key set to {SettingsPresetRuntime.GetKeyDisplayName(bindingPath)}.", "success");
            return;
        }
    }

#if WARMUP_OBSTACLES
    private static void HandleWarmupObstacleEditorKeyListening()
    {
        Keyboard? keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (((ButtonControl)keyboard.escapeKey).wasPressedThisFrame)
        {
            isListeningForWarmupObstacleEditorKey = false;
            SettingsPresetTabUI.Refresh("Obstacle editor keybind change cancelled.", "info");
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
            SettingsPresetStore.Config.WarmupObstacleEditorKeybind = bindingPath;
            SettingsPresetStore.Save();
            WarmupObstacleClient.RefreshEditorToggleAction();
            isListeningForWarmupObstacleEditorKey = false;
            SettingsPresetTabUI.Refresh($"Obstacle editor key set to {SettingsPresetRuntime.GetKeyDisplayName(bindingPath)}.", "success");
            return;
        }
    }
#endif

    private static void RecreateToggleAction()
    {
        try
        {
            toggleAction?.Disable();
            toggleAction?.Dispose();
            toggleAction = null;

            string binding = SettingsPresetStore.Config.ToggleKeybind;
            if (string.IsNullOrWhiteSpace(binding))
            {
                return;
            }

            toggleAction = new InputAction(null, InputActionType.Button, binding);
            toggleAction.Enable();
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to create settings loadout toggle keybind: {exception}");
        }
    }

    private static bool IsUiBlockingHotkey()
    {
        try
        {
            UIManager? uiManager = MonoBehaviourSingleton<UIManager>.Instance;
            if (uiManager == null)
            {
                return false;
            }

            if (uiManager.Chat?.IsFocused == true
                || uiManager.Settings?.IsVisible == true
                || uiManager.Appearance?.IsVisible == true
                || uiManager.MainMenu?.IsVisible == true
                || uiManager.PauseMenu?.IsVisible == true
                || MessageBoardUI.IsVisible
                || GoaliePracticeMenuUI.IsVisible
                || CompetitiveSkatingConfigUI.IsVisible
#if WARMUP_OBSTACLES
                || WarmupObstacleEditorUI.IsVisible
#endif
                )
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool IsLocalPlayerInput(global::PlayerInput playerInput)
    {
        return NetworkManager.Singleton != null
               && ((NetworkBehaviour)playerInput).OwnerClientId == NetworkManager.Singleton.LocalClientId;
    }
}
