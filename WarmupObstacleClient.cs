using System;
using System.Text;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace OPLServerBanners;

internal static class WarmupObstacleClient
{
    private const float ServerResponseTimeoutSeconds = 2f;

    private static CustomMessagingManager? registeredMessagingManager;
    private static bool messageHandlerRegistered;
    private static bool waitingForServerState;
    private static float requestedServerStateAt;
    private static InputAction? editorToggleAction;
    private static WarmupObstacleProtocol.State? latestState;

    public static void Initialize()
    {
        WarmupObstacleEditorUI.EnsureSetup();
        RefreshEditorToggleAction();
    }

    public static void Shutdown()
    {
        UnregisterMessageHandler();
        editorToggleAction?.Disable();
        editorToggleAction?.Dispose();
        editorToggleAction = null;
        waitingForServerState = false;
        latestState = null;
        WarmupObstacleEditorUI.Destroy();
        WarmupObstacleClientWorld.Clear();
        WarmupObstacleAssets.Teardown();
    }

    public static void Tick(global::PlayerInput playerInput)
    {
        if (!IsLocalPlayerInput(playerInput))
        {
            return;
        }

        HandleHotkeys();
        if (WarmupObstacleEditorUI.IsVisible)
        {
            ResetLocalPlayerInputs();
        }
    }

    public static void TickUi()
    {
        if (!IsConnectedClient())
        {
            waitingForServerState = false;
            latestState = null;
            WarmupObstacleClientWorld.Clear();
            if (WarmupObstacleEditorUI.IsVisible)
            {
                WarmupObstacleEditorUI.Hide();
            }

            WarmupObstacleEditorUI.UpdateOpenHint(false);
            return;
        }

        EnsureMessageHandlerRegistered();
        WarmupObstacleEditorUI.UpdateOpenHint(IsWarmup() && !WarmupObstacleEditorUI.IsVisible);
        if (WarmupObstacleEditorUI.IsVisible)
        {
            WarmupObstacleEditorUI.Tick();
        }

        if (waitingForServerState && Time.unscaledTime - requestedServerStateAt >= ServerResponseTimeoutSeconds)
        {
            waitingForServerState = false;
            WarmupObstacleEditorUI.ShowStatus("error", "No obstacle editor response from the server.");
        }
    }

    public static void OpenEditor()
    {
        if (!IsConnectedClient())
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

        if (GoaliePracticeMenuUI.IsVisible)
        {
            GoaliePracticeMenuUI.Hide();
        }

        WarmupObstacleEditorUI.Show();
        if (latestState != null)
        {
            WarmupObstacleEditorUI.ApplyState(latestState);
        }
        else
        {
            WarmupObstacleEditorUI.ShowStatus("info", "Loading obstacle state...");
        }

        RequestState();
    }

    public static void RequestState()
    {
        SendRequest(new WarmupObstacleProtocol.Request
        {
            Action = WarmupObstacleProtocol.ActionRequestState
        }, showOfflineStatus: true);
    }

    public static void PlaceObject(string assetId, Vector3 position, Vector3 rotationEuler)
    {
        SendRequest(new WarmupObstacleProtocol.Request
        {
            Action = WarmupObstacleProtocol.ActionPlace,
            AssetId = assetId,
            Position = new WarmupObstacleProtocol.Vec3Data(position),
            RotationEuler = new WarmupObstacleProtocol.Vec3Data(WarmupObstaclePlacement.SanitizeUserEuler(rotationEuler))
        }, showOfflineStatus: true);
    }

    public static void DeleteObject(int objectId)
    {
        SendRequest(new WarmupObstacleProtocol.Request
        {
            Action = WarmupObstacleProtocol.ActionDelete,
            ObjectId = objectId
        }, showOfflineStatus: true);
    }

    public static void StartClearVote()
    {
        SendRequest(new WarmupObstacleProtocol.Request
        {
            Action = WarmupObstacleProtocol.ActionStartClearVote
        }, showOfflineStatus: true);
    }

    public static void CastClearVote(bool voteYes)
    {
        SendRequest(new WarmupObstacleProtocol.Request
        {
            Action = WarmupObstacleProtocol.ActionCastClearVote,
            VoteYes = voteYes
        }, showOfflineStatus: true);
    }

    public static void RefreshEditorToggleAction()
    {
        try
        {
            editorToggleAction?.Disable();
            editorToggleAction?.Dispose();
            editorToggleAction = null;

            string binding = SettingsPresetStore.Config.WarmupObstacleEditorKeybind;
            if (string.IsNullOrWhiteSpace(binding))
            {
                binding = "<Keyboard>/f3";
            }

            editorToggleAction = new InputAction("WarmupObstacleEditor", InputActionType.Button, binding);
            editorToggleAction.Enable();
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to create warmup obstacle editor keybind: {exception}");
        }
    }

    private static void HandleHotkeys()
    {
        Keyboard? keyboard = Keyboard.current;
        bool editorTogglePressed = editorToggleAction?.WasPressedThisFrame() == true;
        bool escapePressed = keyboard != null && ((ButtonControl)keyboard.escapeKey).wasPressedThisFrame;

        if (WarmupObstacleEditorUI.IsVisible)
        {
            if (editorTogglePressed || escapePressed)
            {
                WarmupObstacleEditorUI.Hide();
            }

            return;
        }

        if (!editorTogglePressed || IsChatFocused())
        {
            return;
        }

        OpenEditor();
    }

    private static bool SendRequest(WarmupObstacleProtocol.Request request, bool showOfflineStatus)
    {
        if (!EnsureMessageHandlerRegistered() || !IsConnectedClient())
        {
            if (showOfflineStatus)
            {
                WarmupObstacleEditorUI.ShowStatus("error", "Obstacle editor is unavailable while disconnected.");
            }

            return false;
        }

        string json = JsonConvert.SerializeObject(request);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        try
        {
            using (FastBufferWriter writer = new FastBufferWriter(bytes.Length, Allocator.Temp))
            {
                writer.WriteBytes(bytes);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                    WarmupObstacleProtocol.ClientToServerListener,
                    NetworkManager.ServerClientId,
                    writer,
                    NetworkDelivery.ReliableFragmentedSequenced);
            }

            waitingForServerState = request.Action == WarmupObstacleProtocol.ActionRequestState;
            if (waitingForServerState)
            {
                requestedServerStateAt = Time.unscaledTime;
            }

            return true;
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to send warmup obstacle request: {exception}");
            if (showOfflineStatus)
            {
                WarmupObstacleEditorUI.ShowStatus("error", "Obstacle editor request failed to send.");
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
            currentManager.RegisterNamedMessageHandler(WarmupObstacleProtocol.ServerToClientListener, ReceiveState);
            registeredMessagingManager = currentManager;
            messageHandlerRegistered = true;
            return true;
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to register warmup obstacle client message handler: {exception}");
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
            registeredMessagingManager.UnregisterNamedMessageHandler(WarmupObstacleProtocol.ServerToClientListener);
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to unregister warmup obstacle client message handler: {exception}");
        }
        finally
        {
            registeredMessagingManager = null;
            messageHandlerRegistered = false;
        }
    }

    private static void ReceiveState(ulong senderClientId, FastBufferReader reader)
    {
        try
        {
            string json = ReadJsonPayload(reader);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            WarmupObstacleProtocol.State? state = JsonConvert.DeserializeObject<WarmupObstacleProtocol.State>(json);
            if (state == null)
            {
                return;
            }

            latestState = state;
            waitingForServerState = false;
            WarmupObstacleClientWorld.ApplyState(state);
            if (WarmupObstacleEditorUI.IsVisible)
            {
                WarmupObstacleEditorUI.ApplyState(state);
            }
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to receive warmup obstacle state: {exception}");
            waitingForServerState = false;
            WarmupObstacleEditorUI.ShowStatus("error", "Obstacle editor state could not be read.");
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

    private static void ResetLocalPlayerInputs()
    {
        try
        {
            Player? localPlayer = MonoBehaviourSingleton<PlayerManager>.Instance?.GetLocalPlayer();
            if (localPlayer?.PlayerInput != null)
            {
                localPlayer.PlayerInput.ResetInputs(localPlayer.Handedness.Value);
            }
        }
        catch
        {
        }
    }
}
