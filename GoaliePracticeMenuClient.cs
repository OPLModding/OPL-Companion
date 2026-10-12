using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace OPLServerBanners;

internal static class GoaliePracticeMenuClient
{
    private const float ServerResponseTimeoutSeconds = 2f;
    private const long DebouncedSendDelayMs = 120L;

    private static readonly Dictionary<string, int> PendingRequestVersions = new Dictionary<string, int>();

    private static CustomMessagingManager? registeredMessagingManager;
    private static bool messageHandlerRegistered;
    private static bool waitingForServerState;
    private static float requestedServerStateAt;
    private static bool isLocalGoalieEligible;

    private static GoaliePracticeMenuProtocol.State? latestState;

    public static void Initialize()
    {
        GoaliePracticeMenuUI.EnsureSetup();
    }

    public static void Shutdown()
    {
        UnregisterMessageHandler();
        waitingForServerState = false;
        isLocalGoalieEligible = false;
        latestState = null;
        PendingRequestVersions.Clear();
        GoaliePracticeMenuUI.Destroy();
    }

    public static void Tick(global::PlayerInput playerInput)
    {
        if (!IsLocalPlayerInput(playerInput))
        {
            return;
        }

        HandleHotkeys();
    }

    public static void TickUi()
    {
        if (!IsConnectedClient())
        {
            waitingForServerState = false;
            isLocalGoalieEligible = false;
            latestState = null;
            if (GoaliePracticeMenuUI.IsVisible)
            {
                GoaliePracticeMenuUI.Hide();
            }

            GoaliePracticeMenuUI.UpdateOpenHint(false);
            return;
        }

        EnsureMessageHandlerRegistered();
        bool isWarmup = IsWarmup();
        isLocalGoalieEligible = isWarmup && IsLocalGoalie();
        if (!isWarmup)
        {
            waitingForServerState = false;
            latestState = null;
        }

        GoaliePracticeMenuUI.UpdateOpenHint(isLocalGoalieEligible && !GoaliePracticeMenuUI.IsVisible);

        if (GoaliePracticeMenuUI.IsVisible && !isLocalGoalieEligible)
        {
            GoaliePracticeMenuUI.Hide();
            return;
        }

        if (waitingForServerState && Time.unscaledTime - requestedServerStateAt >= ServerResponseTimeoutSeconds)
        {
            waitingForServerState = false;
            GoaliePracticeMenuUI.ShowOfflineStatus("No practice menu response from the server.");
        }
    }

    public static void OpenMenu()
    {
        if (!isLocalGoalieEligible)
        {
            return;
        }

        if (MessageBoardUI.IsVisible)
        {
            MessageBoardUI.Hide();
        }

        if (CompetitiveSkatingConfigUI.IsVisible)
        {
            CompetitiveSkatingConfigUI.Hide();
        }

        GoaliePracticeMenuUI.Show();
        if (latestState != null)
        {
            GoaliePracticeMenuUI.ApplyServerState(latestState);
        }
        else
        {
            GoaliePracticeMenuUI.ShowLoadingStatus("Loading practice state...");
        }

        RequestState();
    }

    public static void RequestState()
    {
        SendRequest(GoaliePracticeMenuProtocol.ActionRequestState, string.Empty, showOfflineStatus: true);
    }

    public static void SendImmediateRequest(string action, string value)
    {
        SendRequest(action, value, showOfflineStatus: true);
    }

    public static void SendDebouncedRequest(string action, string value)
    {
        int nextVersion = PendingRequestVersions.TryGetValue(action, out int version)
            ? version + 1
            : 1;
        PendingRequestVersions[action] = nextVersion;

        GoaliePracticeMenuUI.Schedule(() =>
        {
            if (!PendingRequestVersions.TryGetValue(action, out int latestVersion) || latestVersion != nextVersion)
            {
                return;
            }

            PendingRequestVersions.Remove(action);
            SendRequest(action, value, showOfflineStatus: true);
        }, DebouncedSendDelayMs);
    }

    private static void HandleHotkeys()
    {
        if (MessageBoardUI.IsVisible)
        {
            return;
        }

        Keyboard? keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        bool menuTogglePressed = ((ButtonControl)keyboard.f6Key).wasPressedThisFrame;
        bool escapePressed = ((ButtonControl)keyboard.escapeKey).wasPressedThisFrame;

        if (GoaliePracticeMenuUI.IsVisible)
        {
            if (menuTogglePressed || escapePressed)
            {
                GoaliePracticeMenuUI.Hide();
            }

            return;
        }

        if (!menuTogglePressed || !isLocalGoalieEligible || IsChatFocused())
        {
            return;
        }

        OpenMenu();
    }

    private static bool SendRequest(string action, string value, bool showOfflineStatus)
    {
        if (!EnsureMessageHandlerRegistered() || !IsConnectedClient())
        {
            if (showOfflineStatus)
            {
                GoaliePracticeMenuUI.ShowOfflineStatus("Practice menu is unavailable while disconnected.");
            }

            return false;
        }

        GoaliePracticeMenuProtocol.Request request = new GoaliePracticeMenuProtocol.Request
        {
            Action = action,
            Value = value
        };

        string json = JsonUtility.ToJson(request);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        try
        {
            using (FastBufferWriter writer = new FastBufferWriter(bytes.Length, Allocator.Temp))
            {
                writer.WriteBytes(bytes);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                    GoaliePracticeMenuProtocol.ClientToServerListener,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.ReliableFragmentedSequenced);
            }

            waitingForServerState = action == GoaliePracticeMenuProtocol.ActionRequestState;
            if (waitingForServerState)
            {
                requestedServerStateAt = Time.unscaledTime;
            }

            return true;
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to send practice menu request: {exception}");
            if (showOfflineStatus)
            {
                GoaliePracticeMenuUI.ShowOfflineStatus("Practice menu request failed to send.");
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
            currentManager.RegisterNamedMessageHandler(GoaliePracticeMenuProtocol.ServerToClientListener, ReceiveMenuState);
            registeredMessagingManager = currentManager;
            messageHandlerRegistered = true;
            return true;
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to register practice menu message handler: {exception}");
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
            registeredMessagingManager.UnregisterNamedMessageHandler(GoaliePracticeMenuProtocol.ServerToClientListener);
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to unregister practice menu message handler: {exception}");
        }
        finally
        {
            registeredMessagingManager = null;
            messageHandlerRegistered = false;
        }
    }

    private static void ReceiveMenuState(ulong senderClientId, FastBufferReader reader)
    {
        try
        {
            string json = ReadJsonPayload(reader);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            GoaliePracticeMenuProtocol.State? state = JsonUtility.FromJson<GoaliePracticeMenuProtocol.State>(json);
            if (state == null)
            {
                return;
            }

            latestState = state;
            waitingForServerState = false;
            if (GoaliePracticeMenuUI.IsVisible)
            {
                GoaliePracticeMenuUI.ApplyServerState(state);
            }
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to receive practice menu state: {exception}");
            waitingForServerState = false;
            GoaliePracticeMenuUI.ShowOfflineStatus("Practice menu state could not be read.");
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
        for (int i = 0; i < remainingBytes; i++)
        {
            reader.ReadByte(out bytes[i]);
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

    private static bool IsLocalGoalie()
    {
        Player? localPlayer = MonoBehaviourSingleton<PlayerManager>.Instance?.GetLocalPlayer();
        return localPlayer != null
               && localPlayer.Role == PlayerRole.Goalie
               && localPlayer.PlayerPosition != null
               && (localPlayer.Team == PlayerTeam.Red || localPlayer.Team == PlayerTeam.Blue);
    }

    private static bool IsWarmup()
    {
        GameManager? gameManager = NetworkBehaviourSingleton<GameManager>.Instance;
        return gameManager != null && gameManager.Phase == GamePhase.Warmup;
    }

    private static bool IsChatFocused()
    {
        UIChat? chat = MonoBehaviourSingleton<UIManager>.Instance?.Chat;
        return chat != null && chat.IsFocused;
    }

    public static string FormatFloat(float value, float step)
    {
        float rounded = step > 0f ? Mathf.Round(value / step) * step : value;
        return rounded.ToString(step >= 1f ? "0" : "0.0", CultureInfo.InvariantCulture);
    }
}
