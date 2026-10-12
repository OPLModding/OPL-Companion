using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace OPLServerBanners;

internal static class GoalieWarmupPuckTracking
{
    private const float ConeHalfAngleDegrees = 45f;
    private const float MaxGoaliePuckRange = 20f;
    private const float CycleCooldownSeconds = 0.15f;
    private const float ControlsRebindInputDebounceSeconds = 0.12f;
    private const float NativeCycleRegistrationRetrySeconds = 1f;
    private const float CandidateRefreshIntervalSeconds = 0.12f;
    private const string DefaultCycleBindingPath = "<Keyboard>/f10";
    private const string ButtonWithOptionalModifierComposite = "ButtonWithOneOrNoneModifier";
    private const string CycleActionName = "TRACK GOALIE CYCLE";
    private const string LegacyCycleActionName = "Goalie Cycle";
    private const string CycleRowName = "GoalieCycleKeyBindInput";

    private static readonly float ConeDotMinimum = Mathf.Cos(ConeHalfAngleDegrees * Mathf.Deg2Rad);
    private static readonly float MaxGoaliePuckRangeSqr = MaxGoaliePuckRange * MaxGoaliePuckRange;
    private static readonly List<PuckCandidate> Candidates = new();
    private static readonly FieldInfo? UiSettingsSettingsField = typeof(UISettings).GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? UiSettingsActionMapField = typeof(UISettings).GetField("actionNameKeyBindFieldMap", BindingFlags.Instance | BindingFlags.NonPublic);

    private static InputAction? cycleAction;
    private static bool isListeningForCycleKey;
    private static bool isListeningForControlsCycleKeyBind;
    private static float controlsCycleKeyBindStartedAt;
    private static Puck? selectedPuckCache;
    private static ulong? selectedPuckNetworkObjectId;
    private static float lastCycleAt;
    private static float nextNativeCycleRegistrationCheckAt;
    private static float nextCandidateRefreshAt;
    private static string cachedCycleButtonPath = string.Empty;
    private static ButtonControl? cachedCycleButtonControl;
    private static string cachedCycleModifierPath = string.Empty;
    private static ButtonControl? cachedCycleModifierControl;

    public static bool IsListeningForCycleKey => isListeningForCycleKey;

    public static void ApplyPatches(Harmony harmony)
    {
        harmony.CreateClassProcessor(typeof(UISettingsInitializePatch)).Patch();
        harmony.CreateClassProcessor(typeof(InputManagerRebindButtonInteractivelyPatch)).Patch();
        harmony.CreateClassProcessor(typeof(PlayerInputUpdateLookAnglePatch)).Patch();
    }

    public static void Initialize()
    {
        RecreateCycleAction();
        EventManager.AddEventListener("Event_OnKeyBindsSaved", Event_OnKeyBindsSaved);
        EventManager.AddEventListener("Event_OnKeyBindRebindComplete", Event_OnKeyBindRebindComplete);
    }

    public static void Shutdown()
    {
        EventManager.RemoveEventListener("Event_OnKeyBindsSaved", Event_OnKeyBindsSaved);
        EventManager.RemoveEventListener("Event_OnKeyBindRebindComplete", Event_OnKeyBindRebindComplete);
        UnregisterNativeCycleAction();
        cycleAction?.Disable();
        cycleAction?.Dispose();
        cycleAction = null;
        isListeningForCycleKey = false;
        isListeningForControlsCycleKeyBind = false;
        controlsCycleKeyBindStartedAt = 0f;
        selectedPuckCache = null;
        selectedPuckNetworkObjectId = null;
        lastCycleAt = 0f;
        nextNativeCycleRegistrationCheckAt = 0f;
        nextCandidateRefreshAt = 0f;
        cachedCycleButtonPath = string.Empty;
        cachedCycleButtonControl = null;
        cachedCycleModifierPath = string.Empty;
        cachedCycleModifierControl = null;
        Candidates.Clear();
    }

    public static void TickUi()
    {
        if (Time.unscaledTime >= nextNativeCycleRegistrationCheckAt)
        {
            nextNativeCycleRegistrationCheckAt = Time.unscaledTime + NativeCycleRegistrationRetrySeconds;
            EnsureNativeCycleActionRegistered();
        }

        if (isListeningForCycleKey)
        {
            HandleCycleKeyListening();
        }

        if (isListeningForControlsCycleKeyBind)
        {
            HandleControlsCycleKeyBindListening();
        }
    }

    public static void StartListeningForCycleKey()
    {
        SettingsPresetClient.CancelToggleKeyListening();
        SettingsPresetClient.CancelWarmupObstacleEditorKeyListening();
        isListeningForCycleKey = true;
    }

    public static void CancelCycleKeyListening()
    {
        isListeningForCycleKey = false;
        CancelControlsCycleKeyBindListening(false);
    }

    private static void Event_OnKeyBindsSaved(Dictionary<string, object> message)
    {
        SyncConfigFromNativeKeyBind();
        EnsureControlsRowInjected();
    }

    private static void Event_OnKeyBindRebindComplete(Dictionary<string, object> message)
    {
        if (message.TryGetValue("actionName", out object actionName)
            && string.Equals(actionName as string, CycleActionName, StringComparison.Ordinal))
        {
            SyncConfigFromNativeKeyBind();
        }
    }

    private static bool TryHandleGoalieWarmupTracking(global::PlayerInput playerInput, float deltaTime)
    {
        if (!IsEligible(playerInput, out Player? player, out PlayerCamera? playerCamera, out PlayerBody? playerBody)
            || !playerInput.TrackInput.ClientValue)
        {
            ResetSelection();
            return false;
        }

        if (playerInput.LookInput.ClientValue)
        {
            return false;
        }

        Player currentPlayer = player!;
        PlayerCamera currentPlayerCamera = playerCamera!;
        PlayerBody currentPlayerBody = playerBody!;
        bool cyclePressed = ConsumeCyclePressed();
        if (!TryGetSelectedPuck(currentPlayer, cyclePressed, out Puck? selectedPuck) || selectedPuck == null)
        {
            ResetSelection();
            return false;
        }

        Vector3 toPuck = selectedPuck.transform.position - currentPlayerCamera.transform.position;
        if (toPuck.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        Quaternion targetRotation = Quaternion.LookRotation(toPuck);
        Quaternion localRotation = Quaternion.Inverse(currentPlayerBody.transform.rotation) * targetRotation;
        Vector3 targetAngle = Utils.WrapEulerAngles(localRotation.eulerAngles);
        targetAngle = Utils.Vector2Clamp(targetAngle, playerInput.MinimumLookAngle, playerInput.MaximumLookAngle);
        playerInput.LookAngleInput.ClientValue = Vector3.LerpUnclamped((Vector3)playerInput.LookAngleInput.ClientValue, targetAngle, deltaTime * 10f);
        return true;
    }

    private static bool IsEligible(
        global::PlayerInput playerInput,
        out Player? player,
        out PlayerCamera? playerCamera,
        out PlayerBody? playerBody)
    {
        player = null;
        playerCamera = null;
        playerBody = null;

        NetworkManager? networkManager = NetworkManager.Singleton;
        if (networkManager == null
            || !networkManager.IsConnectedClient
            || ((NetworkBehaviour)playerInput).OwnerClientId != networkManager.LocalClientId)
        {
            return false;
        }

        GameManager? gameManager = NetworkBehaviourSingleton<GameManager>.Instance;
        if (gameManager == null || gameManager.Phase != GamePhase.Warmup)
        {
            return false;
        }

        player = playerInput.Player;
        if (player == null
            || player.Role != PlayerRole.Goalie
            || (player.Team != PlayerTeam.Red && player.Team != PlayerTeam.Blue))
        {
            player = null;
            return false;
        }

        playerCamera = player.PlayerCamera;
        playerBody = player.PlayerBody;
        if (playerCamera == null || playerBody == null)
        {
            player = null;
            playerCamera = null;
            playerBody = null;
            return false;
        }

        return true;
    }

    private static bool TryGetSelectedPuck(Player player, bool cyclePressed, out Puck? selectedPuck)
    {
        selectedPuck = null;

        PuckManager? puckManager = MonoBehaviourSingleton<PuckManager>.Instance;
        if (puckManager == null || player.PlayerBody == null)
        {
            return false;
        }

        if (!cyclePressed
            && Time.unscaledTime < nextCandidateRefreshAt
            && TryUseCachedSelectedPuck(player, out selectedPuck))
        {
            return true;
        }

        List<Puck> pucks = puckManager.GetPucks(false);
        if (!cyclePressed && TryFindPuckByNetworkObjectId(pucks, selectedPuckNetworkObjectId, out selectedPuck))
        {
            nextCandidateRefreshAt = Time.unscaledTime + CandidateRefreshIntervalSeconds;
            CacheSelectedPuck(selectedPuck);
            return true;
        }

        Vector3 origin = player.PlayerBody.transform.position;
        Vector3 forward = GetGoalieForward(player);
        if (forward.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        nextCandidateRefreshAt = Time.unscaledTime + CandidateRefreshIntervalSeconds;
        if (!cyclePressed)
        {
            if (TryFindNearestCandidate(pucks, origin, forward, out selectedPuck))
            {
                CacheSelectedPuck(selectedPuck);
                return true;
            }

            if (TryFindPuckByNetworkObjectId(pucks, selectedPuckNetworkObjectId, out selectedPuck))
            {
                CacheSelectedPuck(selectedPuck);
                return true;
            }

            ResetSelection();
            return false;
        }

        Candidates.Clear();
        foreach (Puck puck in pucks)
        {
            if (TryGetCandidateDistanceSqr(puck, origin, forward, out float distanceSqr))
            {
                Candidates.Add(new PuckCandidate(puck, distanceSqr));
            }
        }

        if (Candidates.Count == 0)
        {
            if (TryFindPuckByNetworkObjectId(pucks, selectedPuckNetworkObjectId, out selectedPuck))
            {
                CacheSelectedPuck(selectedPuck);
                return true;
            }

            ResetSelection();
            return false;
        }

        Candidates.Sort((left, right) => left.DistanceSqr.CompareTo(right.DistanceSqr));

        int selectedIndex = FindCandidateIndex(selectedPuckNetworkObjectId);
        if (cyclePressed)
        {
            selectedIndex = selectedIndex < 0 ? 0 : (selectedIndex + 1) % Candidates.Count;
        }
        else if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }

        selectedPuck = Candidates[selectedIndex].Puck;
        CacheSelectedPuck(selectedPuck);
        return true;
    }

    private static bool TryUseCachedSelectedPuck(Player player, out Puck? puck)
    {
        puck = null;
        if (selectedPuckCache == null
            || !selectedPuckNetworkObjectId.HasValue
            || player.PlayerBody == null
            || selectedPuckCache.NetworkObjectId != selectedPuckNetworkObjectId.Value)
        {
            selectedPuckCache = null;
            return false;
        }

        Vector3 origin = player.PlayerBody.transform.position;
        Vector3 forward = GetGoalieForward(player);
        if (forward.sqrMagnitude <= 0.0001f
            || !TryGetCandidateDistanceSqr(selectedPuckCache, origin, forward, out _))
        {
            return false;
        }

        puck = selectedPuckCache;
        return true;
    }

    private static bool TryFindNearestCandidate(
        List<Puck> pucks,
        Vector3 origin,
        Vector3 forward,
        out Puck? selectedPuck)
    {
        selectedPuck = null;
        float bestDistanceSqr = float.MaxValue;
        foreach (Puck puck in pucks)
        {
            if (!TryGetCandidateDistanceSqr(puck, origin, forward, out float distanceSqr))
            {
                continue;
            }

            if (distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                selectedPuck = puck;
            }
        }

        return selectedPuck != null;
    }

    private static bool TryGetCandidateDistanceSqr(Puck? puck, Vector3 origin, Vector3 forward, out float distanceSqr)
    {
        distanceSqr = 0f;
        if (puck == null)
        {
            return false;
        }

        Vector3 toPuck = puck.transform.position - origin;
        Vector3 flatToPuck = new Vector3(toPuck.x, 0f, toPuck.z);
        distanceSqr = flatToPuck.sqrMagnitude;
        if (distanceSqr > MaxGoaliePuckRangeSqr)
        {
            return false;
        }

        if (distanceSqr <= 0.0001f)
        {
            return true;
        }

        float dot = Vector3.Dot(forward, flatToPuck / Mathf.Sqrt(distanceSqr));
        return dot >= ConeDotMinimum;
    }

    private static void CacheSelectedPuck(Puck? puck)
    {
        selectedPuckCache = puck;
        selectedPuckNetworkObjectId = puck?.NetworkObjectId;
    }

    private static bool TryFindPuckByNetworkObjectId(List<Puck> pucks, ulong? networkObjectId, out Puck? puck)
    {
        puck = null;
        if (!networkObjectId.HasValue)
        {
            return false;
        }

        foreach (Puck candidate in pucks)
        {
            if (candidate != null && candidate.NetworkObjectId == networkObjectId.Value)
            {
                puck = candidate;
                return true;
            }
        }

        return false;
    }

    private static Vector3 GetGoalieForward(Player player)
    {
        Vector3 forward = player.PlayerBody != null ? player.PlayerBody.transform.forward : GetTeamForward(player.Team);
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = GetTeamForward(player.Team);
        }

        return forward.normalized;
    }

    private static Vector3 GetTeamForward(PlayerTeam team)
    {
        return team == PlayerTeam.Blue ? Vector3.back : Vector3.forward;
    }

    private static int FindCandidateIndex(ulong? networkObjectId)
    {
        if (!networkObjectId.HasValue)
        {
            return -1;
        }

        for (int index = 0; index < Candidates.Count; index++)
        {
            if (Candidates[index].Puck.NetworkObjectId == networkObjectId.Value)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool ConsumeCyclePressed()
    {
        if (cycleAction == null || isListeningForCycleKey || IsUiBlockingCycleInput())
        {
            return false;
        }

        if ((!cycleAction.WasPerformedThisFrame() && !IsConfiguredCycleBindingPressedThisFrame())
            || Time.unscaledTime - lastCycleAt < CycleCooldownSeconds)
        {
            return false;
        }

        lastCycleAt = Time.unscaledTime;
        return true;
    }

    private static bool IsConfiguredCycleBindingPressedThisFrame()
    {
        try
        {
            SettingsPresetKeyBind keyBind = GetConfiguredCycleKeyBind();
            if (!IsModifierBindingPressed(keyBind.ModifierPath))
            {
                return false;
            }

            return IsButtonBindingPressedThisFrame(keyBind.Path);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsModifierBindingPressed(string? bindingPath)
    {
        if (string.IsNullOrWhiteSpace(bindingPath))
        {
            return true;
        }

        string path = (bindingPath ?? string.Empty).Trim();
        if (TryGetMouseScrollDirection(path, out _))
        {
            return IsButtonBindingPressedThisFrame(path);
        }

        return GetCachedButtonControl(path, ref cachedCycleModifierPath, ref cachedCycleModifierControl) is ButtonControl control
               && control.isPressed;
    }

    private static bool IsButtonBindingPressedThisFrame(string? bindingPath)
    {
        if (string.IsNullOrWhiteSpace(bindingPath))
        {
            return false;
        }

        string path = (bindingPath ?? string.Empty).Trim();
        if (TryGetMouseScrollDirection(path, out int scrollDirection))
        {
            Mouse? mouse = Mouse.current;
            return mouse != null && mouse.scroll.ReadValue().y * scrollDirection > 0f;
        }

        return GetCachedButtonControl(path, ref cachedCycleButtonPath, ref cachedCycleButtonControl) is ButtonControl control
               && control.wasPressedThisFrame;
    }

    private static ButtonControl? GetCachedButtonControl(
        string bindingPath,
        ref string cachedPath,
        ref ButtonControl? cachedControl)
    {
        if (!string.Equals(bindingPath, cachedPath, StringComparison.Ordinal)
            || cachedControl == null)
        {
            cachedPath = bindingPath;
            cachedControl = UnityEngine.InputSystem.InputSystem.FindControl(bindingPath) as ButtonControl;
        }

        return cachedControl;
    }

    private static bool TryGetMouseScrollDirection(string bindingPath, out int direction)
    {
        if (bindingPath.EndsWith("/scroll/up", StringComparison.OrdinalIgnoreCase))
        {
            direction = 1;
            return true;
        }

        if (bindingPath.EndsWith("/scroll/down", StringComparison.OrdinalIgnoreCase))
        {
            direction = -1;
            return true;
        }

        direction = 0;
        return false;
    }

    private static void HandleCycleKeyListening()
    {
        Keyboard? keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (((ButtonControl)keyboard.escapeKey).wasPressedThisFrame)
        {
            isListeningForCycleKey = false;
            SettingsPresetTabUI.Refresh("Goalie cycle keybind change cancelled.", "info");
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
            StoreCycleKeyBind(null, bindingPath, string.Empty);
            SettingsPresetStore.Save();
            RecreateCycleAction();
            InputManager.SaveKeyBinds();
            isListeningForCycleKey = false;
            return;
        }
    }

    private static void StartControlsCycleKeyBindListening()
    {
        try
        {
            EnsureNativeCycleActionRegistered();
            SettingsPresetClient.CancelToggleKeyListening();
            isListeningForCycleKey = false;
            isListeningForControlsCycleKeyBind = true;
            controlsCycleKeyBindStartedAt = Time.unscaledTime;
            cycleAction?.Disable();

            Plugin.Log($"Rebinding {CycleActionName} with custom scroll-aware listener.");
            EventManager.TriggerEvent("Event_OnKeyBindRebindStart", new Dictionary<string, object>
            {
                { "actionName", CycleActionName },
                { "isComposite", false }
            });
        }
        catch (Exception exception)
        {
            isListeningForControlsCycleKeyBind = false;
            cycleAction?.Enable();
            Plugin.LogError($"Failed to start goalie cycle keybind listener: {exception}");
        }
    }

    private static void HandleControlsCycleKeyBindListening()
    {
        Keyboard? keyboard = Keyboard.current;
        if (keyboard != null && ((ButtonControl)keyboard.escapeKey).wasPressedThisFrame)
        {
            CancelControlsCycleKeyBindListening(true);
            return;
        }

        if (Time.unscaledTime - controlsCycleKeyBindStartedAt < ControlsRebindInputDebounceSeconds)
        {
            return;
        }

        if (TryReadControlsCycleBinding(out string bindingPath))
        {
            CompleteControlsCycleKeyBind(bindingPath);
        }
    }

    private static void CancelControlsCycleKeyBindListening(bool triggerEvent)
    {
        if (!isListeningForControlsCycleKeyBind)
        {
            return;
        }

        isListeningForControlsCycleKeyBind = false;
        controlsCycleKeyBindStartedAt = 0f;
        cycleAction?.Enable();

        if (!triggerEvent)
        {
            return;
        }

        Plugin.Log($"Rebinding {CycleActionName} was cancelled.");
        EventManager.TriggerEvent("Event_OnKeyBindRebindCancel", new Dictionary<string, object>
        {
            { "actionName", CycleActionName }
        });
    }

    private static bool TryReadControlsCycleBinding(out string bindingPath)
    {
        bindingPath = string.Empty;

        Mouse? mouse = Mouse.current;
        if (mouse != null)
        {
            float scrollY = mouse.scroll.ReadValue().y;
            if (scrollY > 0.01f)
            {
                bindingPath = "<Mouse>/scroll/up";
                return true;
            }

            if (scrollY < -0.01f)
            {
                bindingPath = "<Mouse>/scroll/down";
                return true;
            }

            if (((ButtonControl)mouse.leftButton).wasPressedThisFrame)
            {
                bindingPath = "<Mouse>/leftButton";
                return true;
            }

            if (((ButtonControl)mouse.rightButton).wasPressedThisFrame)
            {
                bindingPath = "<Mouse>/rightButton";
                return true;
            }

            if (((ButtonControl)mouse.middleButton).wasPressedThisFrame)
            {
                bindingPath = "<Mouse>/middleButton";
                return true;
            }
        }

        Keyboard? keyboard = Keyboard.current;
        if (keyboard == null || !((ButtonControl)keyboard.anyKey).wasPressedThisFrame)
        {
            return false;
        }

        foreach (KeyControl key in keyboard.allKeys)
        {
            if (ReferenceEquals(key, keyboard.escapeKey) || !((ButtonControl)key).wasPressedThisFrame)
            {
                continue;
            }

            bindingPath = "<Keyboard>/" + key.name;
            return true;
        }

        return false;
    }

    private static void CompleteControlsCycleKeyBind(string bindingPath)
    {
        try
        {
            string interactions = GetCurrentCycleInteractions();
            isListeningForControlsCycleKeyBind = false;
            controlsCycleKeyBindStartedAt = 0f;

            InputManager.RebindAction(CycleActionName, null, bindingPath);
            InputManager.SetActionInteractions(CycleActionName, interactions);
            StoreCycleKeyBind(null, bindingPath, interactions);
            SettingsPresetStore.Save();
            cycleAction?.Enable();
            InputManager.SaveKeyBinds();

            Plugin.Log($"Rebound {CycleActionName} to {bindingPath}.");
            EventManager.TriggerEvent("Event_OnKeyBindRebindComplete", new Dictionary<string, object>
            {
                { "actionName", CycleActionName }
            });
            EnsureControlsRowInjected();
        }
        catch (Exception exception)
        {
            isListeningForControlsCycleKeyBind = false;
            controlsCycleKeyBindStartedAt = 0f;
            cycleAction?.Enable();
            Plugin.LogError($"Failed to bind goalie cycle key: {exception}");
            EventManager.TriggerEvent("Event_OnKeyBindRebindCancel", new Dictionary<string, object>
            {
                { "actionName", CycleActionName }
            });
        }
    }

    private static string GetCurrentCycleInteractions()
    {
        try
        {
            if (cycleAction != null && cycleAction.bindings.Count > 0)
            {
                return cycleAction.bindings[0].effectiveInteractions ?? string.Empty;
            }
        }
        catch
        {
        }

        SettingsPresetKeyBind? keyBind = SettingsPresetStore.Config.GoalieTrackCycleNativeKeyBind;
        return keyBind?.Interactions ?? string.Empty;
    }

    private static void RecreateCycleAction()
    {
        try
        {
            bool wasEnabled = cycleAction?.enabled == true;
            UnregisterNativeCycleAction();
            cycleAction?.Disable();
            cycleAction?.Dispose();

            SettingsPresetKeyBind keyBind = GetConfiguredCycleKeyBind();
            cycleAction = CreateCycleAction(keyBind);
            RegisterNativeCycleAction();
            if (wasEnabled || !cycleAction.enabled)
            {
                cycleAction.Enable();
            }
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to create goalie puck cycle keybind: {exception}");
        }
    }

    private static InputAction CreateCycleAction(SettingsPresetKeyBind keyBind)
    {
        InputAction action = new InputAction(CycleActionName, InputActionType.Button);
        action.AddCompositeBinding(ButtonWithOptionalModifierComposite)
            .With("modifier", keyBind.ModifierPath ?? string.Empty)
            .With("button", keyBind.Path);
        return action;
    }

    private static SettingsPresetKeyBind GetConfiguredCycleKeyBind()
    {
        SettingsPresetConfig config = SettingsPresetStore.Config;
        SettingsPresetKeyBind keyBind = config.GoalieTrackCycleNativeKeyBind ??= new SettingsPresetKeyBind();
        string legacyPath = string.IsNullOrWhiteSpace(config.GoalieTrackCycleKeybind)
            ? DefaultCycleBindingPath
            : config.GoalieTrackCycleKeybind.Trim();

        keyBind.ModifierPath = keyBind.ModifierPath?.Trim() ?? string.Empty;
        keyBind.Path = string.IsNullOrWhiteSpace(keyBind.Path) ? legacyPath : keyBind.Path.Trim();
        keyBind.Path = string.IsNullOrWhiteSpace(keyBind.Path) ? DefaultCycleBindingPath : keyBind.Path;
        keyBind.Interactions = keyBind.Interactions?.Trim() ?? string.Empty;
        config.GoalieTrackCycleKeybind = keyBind.Path;
        return keyBind;
    }

    private static void ApplyConfiguredCycleKeyBind()
    {
        SettingsPresetKeyBind keyBind = GetConfiguredCycleKeyBind();
        InputManager.RebindAction(CycleActionName, EmptyToNull(keyBind.ModifierPath), keyBind.Path);
        InputManager.SetActionInteractions(CycleActionName, keyBind.Interactions ?? string.Empty);
    }

    private static void StoreCycleKeyBind(string? modifierPath, string? path, string? interactions)
    {
        SettingsPresetConfig config = SettingsPresetStore.Config;
        SettingsPresetKeyBind keyBind = config.GoalieTrackCycleNativeKeyBind ??= new SettingsPresetKeyBind();
        string trimmedPath = path?.Trim() ?? string.Empty;
        keyBind.ModifierPath = modifierPath?.Trim() ?? string.Empty;
        keyBind.Path = string.IsNullOrWhiteSpace(trimmedPath) ? DefaultCycleBindingPath : trimmedPath;
        keyBind.Interactions = interactions?.Trim() ?? string.Empty;
        config.GoalieTrackCycleKeybind = keyBind.Path;
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void RegisterNativeCycleAction()
    {
        if (cycleAction == null)
        {
            return;
        }

        RemoveNativeCycleActionReferences();
        InputManager.InputActions.Add(cycleAction);
        InputManager.RebindableInputActions.Add(cycleAction);
        InputManager.KeyBinds[CycleActionName] = new KeyBind(cycleAction);
        ApplyConfiguredCycleKeyBind();
        InputManager.KeyBinds[CycleActionName] = new KeyBind(cycleAction);
    }

    private static void EnsureNativeCycleActionRegistered()
    {
        if (cycleAction == null)
        {
            return;
        }

        bool isRegistered = InputManager.RebindableInputActions.Exists(action =>
            ReferenceEquals(action, cycleAction)
            || string.Equals(action?.name, CycleActionName, StringComparison.Ordinal));
        if (!isRegistered)
        {
            RegisterNativeCycleAction();
        }
    }

    private static void UnregisterNativeCycleAction()
    {
        RemoveNativeCycleActionReferences();
        InputManager.KeyBinds.Remove(CycleActionName);
        InputManager.KeyBinds.Remove(LegacyCycleActionName);
    }

    private static void RemoveNativeCycleActionReferences()
    {
        InputManager.InputActions.RemoveAll(IsCycleActionReference);
        InputManager.RebindableInputActions.RemoveAll(IsCycleActionReference);
        InputManager.KeyBinds.Remove(LegacyCycleActionName);
    }

    private static bool IsCycleActionReference(InputAction? action)
    {
        return action == null
               || string.Equals(action.name, CycleActionName, StringComparison.Ordinal)
               || string.Equals(action.name, LegacyCycleActionName, StringComparison.Ordinal);
    }

    private static void SyncConfigFromNativeKeyBind()
    {
        try
        {
            if (!InputManager.KeyBinds.TryGetValue(CycleActionName, out KeyBind keyBind)
                || string.IsNullOrWhiteSpace(keyBind.Path))
            {
                return;
            }

            StoreCycleKeyBind(keyBind.ModifierPath, keyBind.Path, keyBind.Interactions);
            SettingsPresetStore.Save();
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to sync goalie puck cycle keybind: {exception}");
        }
    }

    private static void EnsureControlsRowInjected()
    {
        try
        {
            UISettings? uiSettings = MonoBehaviourSingleton<UIManager>.Instance?.Settings;
            if (uiSettings != null)
            {
                InjectControlsRow(uiSettings);
            }
        }
        catch
        {
        }
    }

    private static void InjectControlsRow(UISettings uiSettings)
    {
        VisualElement? settings = UiSettingsSettingsField?.GetValue(uiSettings) as VisualElement;
        if (settings == null)
        {
            return;
        }

        VisualElement? existingRow = settings.Q<VisualElement>(CycleRowName);
        KeyBindField? existingField = existingRow?.Q<KeyBindField>();
        if (existingField != null)
        {
            RetargetCycleKeyBindField(existingField);
            RegisterCycleField(uiSettings, existingField);
            uiSettings.UpdateKeyBindInputs(InputManager.KeyBinds);
            return;
        }

        VisualElement? trackRow = settings.Q<VisualElement>("TrackKeyBindInput");
        VisualElement? parent = trackRow?.parent;
        KeyBindField? sourceField = trackRow?.Q<KeyBindField>();
        if (trackRow == null || parent == null || sourceField == null)
        {
            return;
        }

        if (!TryCloneNativeKeyBindRow(trackRow, out VisualElement? row, out KeyBindField? field) || row == null || field == null)
        {
            row = CloneVisualElementTree(trackRow);
            field = row.Q<KeyBindField>();
            if (field == null)
            {
                field = CreateCycleKeyBindField(sourceField);
                row.Add(field);
            }
        }

        row.name = CycleRowName;
        RetargetCycleKeyBindField(field);

        int insertIndex = GetChildIndex(parent, trackRow);
        parent.Insert(insertIndex < 0 ? parent.childCount : insertIndex + 1, row);

        RegisterCycleField(uiSettings, field);
        uiSettings.UpdateKeyBindInputs(InputManager.KeyBinds);
    }

    private static bool TryCloneNativeKeyBindRow(VisualElement sourceRow, out VisualElement? row, out KeyBindField? field)
    {
        row = null;
        field = null;

        try
        {
            VisualTreeAsset? sourceAsset = sourceRow.visualTreeAssetSource;
            if (sourceAsset == null)
            {
                return false;
            }

            TemplateContainer clonedTree = sourceAsset.CloneTree();
            VisualElement? clonedRow = clonedTree.Q<VisualElement>(sourceRow.name);
            KeyBindField? clonedField = clonedRow?.Q<KeyBindField>();
            if (clonedRow == null || clonedField == null)
            {
                return false;
            }

            clonedRow.parent?.Remove(clonedRow);
            row = clonedRow;
            field = clonedField;
            return true;
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to clone native goalie cycle keybind row: {exception.GetBaseException().Message}");
            return false;
        }
    }

    private static VisualElement CloneVisualElementTree(VisualElement source)
    {
        VisualElement clone = CreateElementLike(source);
        CopyElementShape(source, clone);

        if (ShouldCloneChildren(source))
        {
            foreach (VisualElement child in source.Children())
            {
                clone.Add(CloneVisualElementTree(child));
            }
        }

        return clone;
    }

    private static VisualElement CreateElementLike(VisualElement source)
    {
        try
        {
            return source switch
            {
                KeyBindField => new KeyBindField(),
                Label label => new Label(label.text),
                TextField => new TextField(),
                DropdownField => new DropdownField(),
                Button button => new Button { text = button.text },
                Toggle toggle => new Toggle(toggle.label),
                Slider => new Slider(),
                SliderInt => new SliderInt(),
                ScrollView scrollView => new ScrollView(scrollView.mode),
                _ => Activator.CreateInstance(source.GetType()) as VisualElement ?? new VisualElement()
            };
        }
        catch
        {
            return new VisualElement();
        }
    }

    private static bool ShouldCloneChildren(VisualElement source)
    {
        return source is not Label
               && source is not TextField
               && source is not DropdownField
               && source is not Button
               && source is not Toggle
               && source is not Slider
               && source is not SliderInt
               && source is not ScrollView;
    }

    private static void RetargetCycleKeyBindField(KeyBindField field)
    {
        field.name = "GoalieCycleKeyBindField";
        field.Label = CycleActionName;
        field.Path = GetCycleKeyBindDisplayName();
        field.InteractionType = KeyBindInteractionType.Press;
        field.Interaction = KeyBindInteraction.Press;
    }

    private static KeyBindField CreateCycleKeyBindField(KeyBindField sourceField)
    {
        KeyBindField field = new KeyBindField
        {
            name = "GoalieCycleKeyBindField",
            Label = CycleActionName,
            Path = GetCycleKeyBindDisplayName(),
            InteractionType = KeyBindInteractionType.Press,
            Interaction = KeyBindInteraction.Press
        };
        CopyElementShape(sourceField, field);

        Label nameLabel = new Label
        {
            name = "NameLabel"
        };
        CopyElementShape(sourceField.Q<Label>("NameLabel"), nameLabel);
        field.Add(nameLabel);

        TextField pathTextField = new TextField
        {
            name = "PathTextField"
        };
        TextField? sourcePathTextField = sourceField.Q<TextField>("PathTextField");
        CopyElementShape(sourcePathTextField, pathTextField);
        pathTextField.focusable = sourcePathTextField?.focusable ?? true;
        field.Add(pathTextField);

        DropdownField interactionDropdownField = new DropdownField
        {
            name = "InteractionDropdownField"
        };
        CopyElementShape(sourceField.Q<DropdownField>("InteractionDropdownField"), interactionDropdownField);
        field.Add(interactionDropdownField);

        return field;
    }

    private static void RegisterCycleField(UISettings uiSettings, KeyBindField field)
    {
        Dictionary<string, KeyBindField>? map = UiSettingsActionMapField?.GetValue(uiSettings) as Dictionary<string, KeyBindField>;
        if (map != null)
        {
            map[CycleActionName] = field;
        }

        field.Click = () =>
        {
            EventManager.TriggerEvent("Event_OnSettingsKeyBindInputClicked", new Dictionary<string, object>
            {
                { "actionName", CycleActionName }
            });
        };
        field.InteractionChange = interaction =>
        {
            EventManager.TriggerEvent("Event_OnSettingsKeyBindInputInteractionChanged", new Dictionary<string, object>
            {
                { "actionName", CycleActionName },
                { "interaction", interaction }
            });
            SyncConfigFromNativeKeyBind();
        };
    }

    private static string GetCycleKeyBindDisplayName()
    {
        string modifierPath = string.Empty;
        string path = GetConfiguredCycleKeyBind().Path;

        try
        {
            if (InputManager.KeyBinds.TryGetValue(CycleActionName, out KeyBind keyBind))
            {
                modifierPath = keyBind.ModifierPath ?? string.Empty;
                path = string.IsNullOrWhiteSpace(keyBind.Path) ? path : keyBind.Path;
            }
        }
        catch
        {
        }

        string display = GetBindingDisplayName(path);
        if (!string.IsNullOrWhiteSpace(modifierPath))
        {
            display = $"{GetBindingDisplayName(modifierPath)} + {display}";
        }

        return display;
    }

    private static string GetBindingDisplayName(string bindingPath)
    {
        if (bindingPath.StartsWith("<Mouse>/scroll/", StringComparison.OrdinalIgnoreCase))
        {
            return "Scroll " + SettingsPresetRuntime.GetKeyDisplayName(bindingPath);
        }

        return SettingsPresetRuntime.GetKeyDisplayName(bindingPath);
    }

    private static int GetChildIndex(VisualElement parent, VisualElement child)
    {
        int index = 0;
        foreach (VisualElement current in parent.Children())
        {
            if (ReferenceEquals(current, child))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    private static void CopyElementShape(VisualElement? source, VisualElement target)
    {
        if (source == null)
        {
            return;
        }

        target.name = source.name;
        target.tooltip = source.tooltip;
        target.pickingMode = source.pickingMode;
        target.focusable = source.focusable;
        target.tabIndex = source.tabIndex;
        target.viewDataKey = source.viewDataKey;

        foreach (string className in source.GetClasses())
        {
            if (!string.IsNullOrWhiteSpace(className))
            {
                target.AddToClassList(className);
            }
        }

        CopyInlineStyles(source, target);
        CopyChildShapesByIndex(source, target);
    }

    private static void CopyInlineStyles(VisualElement source, VisualElement target)
    {
        IStyle sourceStyle = source.style;
        IStyle targetStyle = target.style;

        foreach (PropertyInfo property in typeof(IStyle).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            try
            {
                object? value = property.GetValue(sourceStyle);
                property.SetValue(targetStyle, value);
            }
            catch
            {
            }
        }
    }

    private static void CopyChildShapesByIndex(VisualElement source, VisualElement target)
    {
        using IEnumerator<VisualElement> sourceChildren = source.Children().GetEnumerator();
        using IEnumerator<VisualElement> targetChildren = target.Children().GetEnumerator();

        while (sourceChildren.MoveNext() && targetChildren.MoveNext())
        {
            CopyElementShape(sourceChildren.Current, targetChildren.Current);
        }
    }

    private static bool IsUiBlockingCycleInput()
    {
        try
        {
            UIManager? uiManager = MonoBehaviourSingleton<UIManager>.Instance;
            if (uiManager == null)
            {
                return false;
            }

            return uiManager.Chat?.IsFocused == true
                   || uiManager.Settings?.IsVisible == true
                   || uiManager.Appearance?.IsVisible == true
                   || uiManager.MainMenu?.IsVisible == true
                   || uiManager.PauseMenu?.IsVisible == true
                   || MessageBoardUI.IsVisible
                   || GoaliePracticeMenuUI.IsVisible
                   || CompetitiveSkatingConfigUI.IsVisible
                   || SettingsPresetClient.IsListeningForToggleKey;
        }
        catch
        {
            return false;
        }
    }

    private static void ResetSelection()
    {
        selectedPuckCache = null;
        selectedPuckNetworkObjectId = null;
        nextCandidateRefreshAt = 0f;
    }

    private readonly struct PuckCandidate
    {
        public PuckCandidate(Puck puck, float distanceSqr)
        {
            Puck = puck;
            DistanceSqr = distanceSqr;
        }

        public Puck Puck { get; }

        public float DistanceSqr { get; }
    }

    [HarmonyPatch(typeof(UISettings), "Initialize")]
    private static class UISettingsInitializePatch
    {
        [HarmonyPostfix]
        private static void Postfix(UISettings __instance)
        {
            try
            {
                InjectControlsRow(__instance);
            }
            catch (Exception exception)
            {
                Plugin.LogError($"Failed to inject goalie cycle keybind row: {exception}");
            }
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.RebindButtonInteractively))]
    private static class InputManagerRebindButtonInteractivelyPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(string actionName)
        {
            if (!string.Equals(actionName, CycleActionName, StringComparison.Ordinal))
            {
                return true;
            }

            StartControlsCycleKeyBindListening();
            return false;
        }
    }

    [HarmonyPatch(typeof(global::PlayerInput), "UpdateLookAngle")]
    private static class PlayerInputUpdateLookAnglePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(global::PlayerInput __instance, float deltaTime)
        {
            try
            {
                return !TryHandleGoalieWarmupTracking(__instance, deltaTime);
            }
            catch (Exception exception)
            {
                ResetSelection();
                Plugin.LogError($"Goalie warmup puck tracking failed: {exception}");
                return true;
            }
        }
    }
}
