using System;
using System.Text;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace OPLServerBanners;

internal static class CompetitiveSkatingConfigClient
{
    private const float ServerResponseTimeoutSeconds = 2f;

    private static CustomMessagingManager? registeredMessagingManager;
    private static bool messageHandlerRegistered;
    private static bool waitingForServerState;
    private static float requestedServerStateAt;
    private static bool isServerAdmin;
    private static bool lastOpenHintState;
    private static bool hasLastOpenHintState;
    private static bool hasPendingSubmitChanges;
    private static bool hasPendingSaveChanges;
    private static CompetitiveSkatingSettingsData? draftSettings;
    private static PendingServerRequestKind pendingServerRequestKind;
    private static int lastStateTickFrame = -1;

    private static CompetitiveSkatingConfigProtocol.State? latestState;

    public static bool NeedsUiTick => waitingForServerState || CompetitiveSkatingConfigUI.IsVisible || lastOpenHintState;
    public static bool HasPendingChanges => hasPendingSubmitChanges || hasPendingSaveChanges;

    public static void Initialize()
    {
    }

    public static void Shutdown()
    {
        UnregisterMessageHandler();
        waitingForServerState = false;
        requestedServerStateAt = 0f;
        isServerAdmin = false;
        lastOpenHintState = false;
        hasLastOpenHintState = false;
        hasPendingSubmitChanges = false;
        hasPendingSaveChanges = false;
        draftSettings = null;
        pendingServerRequestKind = PendingServerRequestKind.None;
        lastStateTickFrame = -1;
        latestState = null;
        CompetitiveSkatingConfigUI.Destroy();
    }

    public static void Tick(global::PlayerInput playerInput)
    {
        if (!IsLocalPlayerInput(playerInput))
        {
            return;
        }

        TickState();
        HandleHotkeys();
    }

    public static void TickUi()
    {
        TickState();
    }

    private static void TickState()
    {
        int currentFrame = Time.frameCount;
        if (lastStateTickFrame == currentFrame)
        {
            return;
        }

        lastStateTickFrame = currentFrame;

        if (!IsConnectedClient())
        {
            waitingForServerState = false;
            requestedServerStateAt = 0f;
            isServerAdmin = false;
            hasPendingSubmitChanges = false;
            hasPendingSaveChanges = false;
            draftSettings = null;
            pendingServerRequestKind = PendingServerRequestKind.None;
            latestState = null;
            if (CompetitiveSkatingConfigUI.IsVisible)
            {
                CompetitiveSkatingConfigUI.Hide();
            }

            SetOpenHint(false);
            return;
        }

        EnsureMessageHandlerRegistered();
        if (latestState == null && !waitingForServerState)
        {
            RequestStateSilently();
        }

        bool adminPanelEnabled = latestState?.AdminPanelEnabled != false;
        isServerAdmin = adminPanelEnabled && latestState?.IsAdmin == true;
        SetOpenHint(isServerAdmin && !CompetitiveSkatingConfigUI.IsVisible);

        if (CompetitiveSkatingConfigUI.IsVisible && (!adminPanelEnabled || !isServerAdmin))
        {
            CompetitiveSkatingConfigUI.Hide();
            return;
        }

        if (waitingForServerState && Time.unscaledTime - requestedServerStateAt >= ServerResponseTimeoutSeconds)
        {
            waitingForServerState = false;
            pendingServerRequestKind = PendingServerRequestKind.None;
            if (CompetitiveSkatingConfigUI.IsVisible)
            {
                CompetitiveSkatingConfigUI.ShowOfflineStatus("No skating config response was received from the server.");
            }
        }
    }

    public static void OpenMenu()
    {
        if (!isServerAdmin || latestState?.AdminPanelEnabled == false)
        {
            return;
        }

        if (!EnsureMessageHandlerRegistered())
        {
            CompetitiveSkatingConfigUI.ShowOfflineStatus("Skating config is unavailable right now.");
            return;
        }

        if (MessageBoardUI.IsVisible)
        {
            MessageBoardUI.Hide();
        }

        if (GoaliePracticeMenuUI.IsVisible)
        {
            GoaliePracticeMenuUI.Hide();
        }

        if (latestState != null)
        {
            ResetDraftToLiveState();
        }
        
        CompetitiveSkatingConfigUI.Show();
        if (latestState != null)
        {
            RefreshUi();
        }
        else
        {
            CompetitiveSkatingConfigUI.ShowLoadingStatus("Loading skating config...");
        }

        RequestState();
    }

    public static void RequestState()
    {
        SendRequest(
            CompetitiveSkatingConfigProtocol.ActionRequestState,
            string.Empty,
            string.Empty,
            null,
            PendingServerRequestKind.RefreshState,
            showOfflineStatus: true);
    }

    private static void RequestStateSilently()
    {
        SendRequest(
            CompetitiveSkatingConfigProtocol.ActionRequestState,
            string.Empty,
            string.Empty,
            null,
            PendingServerRequestKind.None,
            showOfflineStatus: false);
    }

    public static void SendImmediateRequest(string action, string settingId, string value)
    {
        SendRequest(action, settingId, value, null, PendingServerRequestKind.ReloadConfig, showOfflineStatus: true);
    }

    public static void StageValue(string settingId, float value)
    {
        if (latestState?.CanEditSettings != true)
        {
            return;
        }

        EnsureDraftSettings();
        if (draftSettings == null)
        {
            return;
        }

        if (!CompetitiveSkatingConfigRuntime.TrySetValue(draftSettings, settingId, value, out _))
        {
            return;
        }

        UpdateDirtyFlags();
        RefreshUi();
    }

    public static void SubmitDraft()
    {
        if (latestState?.CanEditSettings != true || draftSettings == null || !hasPendingSubmitChanges)
        {
            return;
        }

        CompetitiveSkatingSettingsData payload = CompetitiveSkatingConfigRuntime.Clone(draftSettings);
        if (!SendRequest(
                CompetitiveSkatingConfigProtocol.ActionApplySettings,
                string.Empty,
                string.Empty,
                payload,
                PendingServerRequestKind.ApplySettings,
                showOfflineStatus: true))
        {
            return;
        }

        CompetitiveSkatingConfigUI.ShowLoadingStatus("Applying skating config to the live server...");
    }

    public static void SaveDraft()
    {
        if (latestState?.CanEditSettings != true || draftSettings == null || !hasPendingSaveChanges)
        {
            return;
        }

        CompetitiveSkatingSettingsData payload = CompetitiveSkatingConfigRuntime.Clone(draftSettings);
        if (!SendRequest(
                CompetitiveSkatingConfigProtocol.ActionSaveSettings,
                string.Empty,
                string.Empty,
                payload,
                PendingServerRequestKind.SaveSettings,
                showOfflineStatus: true))
        {
            return;
        }

        CompetitiveSkatingConfigUI.ShowLoadingStatus("Saving PakSkatingConfig.json...");
    }

    public static void RevertDraft()
    {
        if (latestState == null)
        {
            return;
        }

        ResetDraftToLiveState();
        RefreshUi();
    }

    public static void LoadSavedSettings()
    {
        if (latestState?.CanEditSettings != true)
        {
            return;
        }

        draftSettings = CompetitiveSkatingConfigRuntime.Clone(latestState.SavedSettings);
        UpdateDirtyFlags();
        RefreshUi();
    }

    public static void StageDefaultSettings()
    {
        if (latestState?.CanEditSettings != true)
        {
            return;
        }

        draftSettings = CompetitiveSkatingConfigRuntime.Clone(latestState.DefaultSettings);
        UpdateDirtyFlags();
        RefreshUi();
    }

    public static void RestoreDefaults()
    {
        if (latestState?.CanEditSettings != true)
        {
            return;
        }

        if (!SendRequest(
                CompetitiveSkatingConfigProtocol.ActionResetDefaults,
                string.Empty,
                string.Empty,
                null,
                PendingServerRequestKind.ResetDefaults,
                showOfflineStatus: true))
        {
            return;
        }

        CompetitiveSkatingConfigUI.ShowLoadingStatus("Restoring CompetitiveSkating defaults...");
    }

    private static void HandleHotkeys()
    {
        Keyboard? keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        bool menuTogglePressed = ((ButtonControl)keyboard.f3Key).wasPressedThisFrame;
        bool escapePressed = ((ButtonControl)keyboard.escapeKey).wasPressedThisFrame;

        if (CompetitiveSkatingConfigUI.IsVisible)
        {
            if (menuTogglePressed || escapePressed)
            {
                CompetitiveSkatingConfigUI.Hide();
            }

            return;
        }

        if (!menuTogglePressed || IsChatFocused())
        {
            return;
        }

        if (latestState?.AdminPanelEnabled == false)
        {
            return;
        }

        if (!isServerAdmin)
        {
            if (!waitingForServerState)
            {
                RequestStateSilently();
            }

            return;
        }

        OpenMenu();
    }

    private static bool SendRequest(
        string action,
        string settingId,
        string value,
        CompetitiveSkatingSettingsData? settings,
        PendingServerRequestKind requestKind,
        bool showOfflineStatus)
    {
        if (!EnsureMessageHandlerRegistered() || !IsConnectedClient())
        {
            if (showOfflineStatus)
            {
                CompetitiveSkatingConfigUI.ShowOfflineStatus("Skating config is unavailable while disconnected.");
            }

            return false;
        }

        CompetitiveSkatingConfigProtocol.Request request = new CompetitiveSkatingConfigProtocol.Request
        {
            Action = action,
            SettingId = settingId,
            Value = value,
            Settings = settings != null
                ? CompetitiveSkatingConfigRuntime.Clone(settings)
                : new CompetitiveSkatingSettingsData()
        };

        string json = JsonConvert.SerializeObject(request);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        try
        {
            using (FastBufferWriter writer = new FastBufferWriter(bytes.Length, Allocator.Temp))
            {
                writer.WriteBytes(bytes);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                    CompetitiveSkatingConfigProtocol.ClientToServerListener,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.ReliableFragmentedSequenced);
            }

            waitingForServerState = action == CompetitiveSkatingConfigProtocol.ActionRequestState
                                 || action == CompetitiveSkatingConfigProtocol.ActionApplySettings
                                 || action == CompetitiveSkatingConfigProtocol.ActionSaveSettings
                                 || action == CompetitiveSkatingConfigProtocol.ActionSetValue
                                 || action == CompetitiveSkatingConfigProtocol.ActionResetDefaults
                                 || action == CompetitiveSkatingConfigProtocol.ActionReloadConfig;
            if (waitingForServerState)
            {
                requestedServerStateAt = Time.unscaledTime;
                pendingServerRequestKind = requestKind;
            }
            else
            {
                pendingServerRequestKind = PendingServerRequestKind.None;
            }

            return true;
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to send skating config request: {exception}");
            pendingServerRequestKind = PendingServerRequestKind.None;
            if (showOfflineStatus)
            {
                CompetitiveSkatingConfigUI.ShowOfflineStatus("Skating config request failed to send.");
            }

            return false;
        }
    }

    private static bool EnsureMessageHandlerRegistered()
    {
        CustomMessagingManager? currentManager = NetworkManager.Singleton?.CustomMessagingManager;
        if (currentManager == null)
        {
            return false;
        }

        if (messageHandlerRegistered && ReferenceEquals(registeredMessagingManager, currentManager))
        {
            return true;
        }

        UnregisterMessageHandler();

        try
        {
            currentManager.RegisterNamedMessageHandler(CompetitiveSkatingConfigProtocol.ServerToClientListener, ReceiveServerState);
            registeredMessagingManager = currentManager;
            messageHandlerRegistered = true;
            return true;
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to register skating config response handler: {exception}");
            registeredMessagingManager = null;
            messageHandlerRegistered = false;
            return false;
        }
    }

    private static void UnregisterMessageHandler()
    {
        if (!messageHandlerRegistered || registeredMessagingManager == null)
        {
            registeredMessagingManager = null;
            messageHandlerRegistered = false;
            return;
        }

        try
        {
            registeredMessagingManager.UnregisterNamedMessageHandler(CompetitiveSkatingConfigProtocol.ServerToClientListener);
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to unregister skating config response handler: {exception}");
        }
        finally
        {
            registeredMessagingManager = null;
            messageHandlerRegistered = false;
        }
    }

    private static void ReceiveServerState(ulong senderClientId, FastBufferReader reader)
    {
        try
        {
            string json = ReadJsonPayload(reader);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            CompetitiveSkatingConfigProtocol.State? state = JsonConvert.DeserializeObject<CompetitiveSkatingConfigProtocol.State>(json);
            if (state == null)
            {
                return;
            }

            if (state.IsModAvailable
                && CompetitiveSkatingConfigRuntime.SetCompatibilityProfile(state.CompatibilityProfile))
            {
                CompetitiveSkatingConfigUI.RefreshDefinitionLayout();
            }

            latestState = state;
            waitingForServerState = false;
            PendingServerRequestKind completedRequestKind = pendingServerRequestKind;
            pendingServerRequestKind = PendingServerRequestKind.None;

            bool shouldResetDraft = !CompetitiveSkatingConfigUI.IsVisible || !HasPendingChanges;
            if (completedRequestKind == PendingServerRequestKind.ApplySettings
                || completedRequestKind == PendingServerRequestKind.ResetDefaults
                || completedRequestKind == PendingServerRequestKind.ReloadConfig)
            {
                ResetDraftToLiveState();
            }
            else if (completedRequestKind == PendingServerRequestKind.SaveSettings)
            {
                ResetDraftToSavedState();
            }
            else if (shouldResetDraft)
            {
                ResetDraftToLiveState();
            }
            else
            {
                UpdateDirtyFlags();
            }

            RefreshUi();
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to receive skating config state: {exception}");
            waitingForServerState = false;
            pendingServerRequestKind = PendingServerRequestKind.None;
            CompetitiveSkatingConfigUI.ShowOfflineStatus("Skating config state could not be read.");
        }
    }

    private static string ReadJsonPayload(FastBufferReader reader)
    {
        int remainingBytes = reader.Length - reader.Position;
        if (remainingBytes <= 0)
        {
            return string.Empty;
        }

        byte[] bytes = new byte[remainingBytes];
        for (int index = 0; index < remainingBytes; index++)
        {
            reader.ReadByte(out bytes[index]);
        }

        return Encoding.UTF8.GetString(bytes).Trim();
    }

    private static bool IsConnectedClient()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient;
    }

    private static bool IsLocalPlayerInput(global::PlayerInput playerInput)
    {
        return NetworkManager.Singleton != null
               && ((NetworkBehaviour)playerInput).OwnerClientId == NetworkManager.Singleton.LocalClientId;
    }

    private static bool IsChatFocused()
    {
        UIChat? chat = MonoBehaviourSingleton<UIManager>.Instance?.Chat;
        return chat != null && chat.IsFocused;
    }

    private static void EnsureDraftSettings()
    {
        if (draftSettings == null && latestState != null)
        {
            draftSettings = CompetitiveSkatingConfigRuntime.Clone(latestState.Settings);
        }
    }

    private static void ResetDraftToLiveState()
    {
        if (latestState == null)
        {
            draftSettings = null;
            hasPendingSubmitChanges = false;
            hasPendingSaveChanges = false;
            return;
        }

        draftSettings = CompetitiveSkatingConfigRuntime.Clone(latestState.Settings);
        UpdateDirtyFlags();
    }

    private static void ResetDraftToSavedState()
    {
        if (latestState == null)
        {
            draftSettings = null;
            hasPendingSubmitChanges = false;
            hasPendingSaveChanges = false;
            return;
        }

        draftSettings = CompetitiveSkatingConfigRuntime.Clone(latestState.SavedSettings);
        UpdateDirtyFlags();
    }

    private static void UpdateDirtyFlags()
    {
        if (latestState == null || draftSettings == null)
        {
            hasPendingSubmitChanges = false;
            hasPendingSaveChanges = false;
            return;
        }

        hasPendingSubmitChanges = !CompetitiveSkatingConfigRuntime.AreEquivalent(draftSettings, latestState.Settings);
        hasPendingSaveChanges = !CompetitiveSkatingConfigRuntime.AreEquivalent(draftSettings, latestState.SavedSettings);
    }

    private static void RefreshUi()
    {
        if (latestState == null || !CompetitiveSkatingConfigUI.IsVisible)
        {
            return;
        }

        EnsureDraftSettings();
        CompetitiveSkatingConfigUI.ApplyServerState(
            latestState,
            draftSettings ?? latestState.Settings,
            hasPendingSubmitChanges,
            hasPendingSaveChanges,
            waitingForServerState);
    }

    private static void SetOpenHint(bool shouldShow)
    {
        if (hasLastOpenHintState && lastOpenHintState == shouldShow)
        {
            return;
        }

        CompetitiveSkatingConfigUI.UpdateOpenHint(shouldShow);
        lastOpenHintState = shouldShow;
        hasLastOpenHintState = true;
    }

    private enum PendingServerRequestKind
    {
        None,
        RefreshState,
        ApplySettings,
        SaveSettings,
        ResetDefaults,
        ReloadConfig
    }
}
