using System;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OPLServerBanners;

internal static class MessageBoardClient
{
    private const float ServerResponseTimeoutSeconds = 2f;

    private static CustomMessagingManager? registeredMessagingManager;
    private static bool messageHandlerRegistered;
    private static bool waitingForBoard;
    private static float requestedBoardAt;
    private static bool wasConnectedClient;
    private static bool hasViewedBoardThisConnection;
    private static bool autoOpenPending;
    private static float autoOpenAt;

    private static MessageBoardDocument? latestBoard;

    public static void Initialize()
    {
        MessageBoardUI.EnsureSetup();
    }

    public static void Shutdown()
    {
        UnregisterMessageHandler();
        ResetConnectionState();
        MessageBoardUI.Destroy();
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
            if (wasConnectedClient)
            {
                ResetConnectionState();
                MessageBoardUI.Hide();
            }

            return;
        }

        EnsureMessageHandlerRegistered();

        if (!wasConnectedClient)
        {
            wasConnectedClient = true;
            RequestBoard(showOfflineStatus: false);
        }

        if (waitingForBoard && Time.unscaledTime - requestedBoardAt >= ServerResponseTimeoutSeconds)
        {
            waitingForBoard = false;
            autoOpenPending = false;
            if (MessageBoardUI.IsVisible && latestBoard == null)
            {
                MessageBoardUI.ShowErrorStatus("No message board response was received from the server.");
            }
        }

        if (autoOpenPending && !waitingForBoard && latestBoard != null && Time.unscaledTime >= autoOpenAt)
        {
            if (OpenBoard(refreshFromServer: false))
            {
                autoOpenPending = false;
            }
        }
    }

    public static bool OpenBoard(bool refreshFromServer = true)
    {
        if (GoaliePracticeMenuUI.IsVisible)
        {
            GoaliePracticeMenuUI.Hide();
        }

        if (CompetitiveSkatingConfigUI.IsVisible)
        {
            CompetitiveSkatingConfigUI.Hide();
        }

        if (!MessageBoardUI.Show())
        {
            return false;
        }

        if (latestBoard != null && latestBoard.Enabled && latestBoard.Tabs.Count > 0)
        {
            MessageBoardUI.ApplyDocument(latestBoard);
        }
        else if (latestBoard != null && !latestBoard.Enabled)
        {
            MessageBoardUI.ShowInfoStatus("This server has disabled its message board.");
        }
        else if (latestBoard != null)
        {
            MessageBoardUI.ShowInfoStatus("This server does not currently have any message board tabs configured.");
        }
        else
        {
            MessageBoardUI.ShowLoadingStatus("Loading message board...");
        }

        hasViewedBoardThisConnection = true;
        autoOpenPending = false;
        if (refreshFromServer)
        {
            RequestBoard(showOfflineStatus: latestBoard == null);
        }

        return true;
    }

    private static void HandleHotkeys()
    {
        Keyboard? keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        bool togglePressed = MessageBoardHotkeys.IsTogglePressed(keyboard);
        bool escapePressed = keyboard.escapeKey.wasPressedThisFrame;

        if (MessageBoardUI.IsVisible)
        {
            if (togglePressed || escapePressed)
            {
                MessageBoardUI.Hide();
            }

            return;
        }

        if (!togglePressed || IsChatFocused())
        {
            return;
        }

        OpenBoard();
    }

    private static bool RequestBoard(bool showOfflineStatus)
    {
        if (waitingForBoard)
        {
            return true;
        }

        if (!EnsureMessageHandlerRegistered() || !IsConnectedClient())
        {
            if (showOfflineStatus && MessageBoardUI.IsVisible)
            {
                MessageBoardUI.ShowErrorStatus("Message board is unavailable while disconnected.");
            }

            return false;
        }

        MessageBoardProtocol.Request request = new MessageBoardProtocol.Request();
        string json = JsonUtility.ToJson(request);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        try
        {
            using FastBufferWriter writer = new FastBufferWriter(bytes.Length, Allocator.Temp);
            writer.WriteBytes(bytes);
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                MessageBoardProtocol.ClientToServerListener,
                NetworkManager.ServerClientId,
                writer,
                NetworkDelivery.ReliableFragmentedSequenced);

            waitingForBoard = true;
            requestedBoardAt = Time.unscaledTime;
            return true;
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to request message board: {exception}");
            waitingForBoard = false;
            if (showOfflineStatus && MessageBoardUI.IsVisible)
            {
                MessageBoardUI.ShowErrorStatus("Message board request failed to send.");
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
            currentManager.RegisterNamedMessageHandler(MessageBoardProtocol.ServerToClientListener, ReceiveBoardState);
            registeredMessagingManager = currentManager;
            messageHandlerRegistered = true;
            return true;
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to register message board response handler: {exception}");
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
            registeredMessagingManager.UnregisterNamedMessageHandler(MessageBoardProtocol.ServerToClientListener);
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to unregister message board response handler: {exception}");
        }
        finally
        {
            registeredMessagingManager = null;
            messageHandlerRegistered = false;
        }
    }

    private static void ReceiveBoardState(ulong senderClientId, FastBufferReader reader)
    {
        try
        {
            string json = ReadJsonPayload(reader);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            MessageBoardProtocol.Response? response = JsonUtility.FromJson<MessageBoardProtocol.Response>(json);
            waitingForBoard = false;

            if (response == null)
            {
                if (MessageBoardUI.IsVisible && latestBoard == null)
                {
                    MessageBoardUI.ShowErrorStatus("Message board response could not be read.");
                }

                return;
            }

            if (!response.Success)
            {
                autoOpenPending = false;
                if (MessageBoardUI.IsVisible && latestBoard == null)
                {
                    MessageBoardUI.ShowErrorStatus(string.IsNullOrWhiteSpace(response.ErrorMessage)
                        ? "Message board is unavailable."
                        : response.ErrorMessage);
                }

                return;
            }

            if (!MessageBoardContentManager.TryDeserializeDocument(response.BoardJson, out MessageBoardDocument? document, out string errorMessage) || document == null)
            {
                autoOpenPending = false;
                if (MessageBoardUI.IsVisible && latestBoard == null)
                {
                    MessageBoardUI.ShowErrorStatus(errorMessage);
                }

                return;
            }

            latestBoard = document;

            if (MessageBoardUI.IsVisible)
            {
                if (document.Enabled && document.Tabs.Count > 0)
                {
                    MessageBoardUI.ApplyDocument(document);
                }
                else if (!document.Enabled)
                {
                    MessageBoardUI.ShowInfoStatus("This server has disabled its message board.");
                }
                else
                {
                    MessageBoardUI.ShowInfoStatus("This server does not currently have any message board tabs configured.");
                }
            }

            if (!hasViewedBoardThisConnection && document.Enabled && document.ShowOnJoin && document.Tabs.Count > 0)
            {
                autoOpenPending = true;
                autoOpenAt = Time.unscaledTime + (document.OpenDelayMs / 1000f);
            }
            else
            {
                autoOpenPending = false;
            }
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to receive message board state: {exception}");
            waitingForBoard = false;
            autoOpenPending = false;
            if (MessageBoardUI.IsVisible && latestBoard == null)
            {
                MessageBoardUI.ShowErrorStatus("Message board state could not be read.");
            }
        }
    }

    private static void ResetConnectionState()
    {
        waitingForBoard = false;
        wasConnectedClient = false;
        hasViewedBoardThisConnection = false;
        autoOpenPending = false;
        autoOpenAt = 0f;
        latestBoard = null;
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
}
