using System;
using System.Text;
using Unity.Collections;
using Unity.Netcode;

namespace OPLServerBanners;

internal static class MessageBoardServer
{
    private static bool isInitialized;
    private static bool messageHandlerRegistered;
    private static CustomMessagingManager? registeredMessagingManager;
    private static MessageBoardLoadResult? cachedResponseSource;
    private static byte[]? cachedResponseBytes;

    public static void Initialize()
    {
        isInitialized = true;
        try
        {
            MessageBoardContentManager.EnsureFilesExist();
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to initialize message board files: {exception.Message}");
        }
    }

    public static void Shutdown()
    {
        isInitialized = false;
        UnregisterMessageHandler();
        cachedResponseSource = null;
        cachedResponseBytes = null;
    }

    public static void Tick()
    {
        if (!isInitialized)
        {
            return;
        }

        EnsureMessageHandlerRegistered();
    }

    private static bool EnsureMessageHandlerRegistered()
    {
        NetworkManager? networkManager = NetworkManager.Singleton;
        if (!isInitialized || networkManager == null || !networkManager.IsServer)
        {
            UnregisterMessageHandler();
            return false;
        }

        CustomMessagingManager? currentManager = networkManager.CustomMessagingManager;
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
            currentManager.RegisterNamedMessageHandler(MessageBoardProtocol.ClientToServerListener, ReceiveBoardRequest);
            registeredMessagingManager = currentManager;
            messageHandlerRegistered = true;
            return true;
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to register message board handler: {exception}");
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
            registeredMessagingManager.UnregisterNamedMessageHandler(MessageBoardProtocol.ClientToServerListener);
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to unregister message board handler: {exception}");
        }
        finally
        {
            registeredMessagingManager = null;
            messageHandlerRegistered = false;
        }
    }

    private static void ReceiveBoardRequest(ulong senderClientId, FastBufferReader reader)
    {
        try
        {
            string json = ReadJsonPayload(reader);
            MessageBoardProtocol.Request? request = string.IsNullOrWhiteSpace(json)
                ? null
                : UnityEngine.JsonUtility.FromJson<MessageBoardProtocol.Request>(json);

            if (request != null && !string.Equals(request.Action, MessageBoardProtocol.ActionRequestBoard, StringComparison.Ordinal))
            {
                return;
            }

            MessageBoardLoadResult loadResult = MessageBoardContentManager.LoadBoardFromDisk();
            SendResponseBytes(senderClientId, GetResponseBytes(loadResult));
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to process message board request: {exception}");
            SendResponse(senderClientId, new MessageBoardProtocol.Response
            {
                Success = false,
                ErrorMessage = "The server failed to prepare the message board."
            });
        }
    }

    private static byte[] GetResponseBytes(MessageBoardLoadResult loadResult)
    {
        if (ReferenceEquals(cachedResponseSource, loadResult) && cachedResponseBytes != null)
        {
            return cachedResponseBytes;
        }

        MessageBoardProtocol.Response response = new MessageBoardProtocol.Response
        {
            Success = loadResult.Success,
            Revision = loadResult.Revision,
            BoardJson = loadResult.RawJson,
            ErrorMessage = loadResult.ErrorMessage
        };
        string json = UnityEngine.JsonUtility.ToJson(response);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        cachedResponseSource = loadResult;
        cachedResponseBytes = bytes;
        return bytes;
    }

    private static void SendResponse(ulong recipientClientId, MessageBoardProtocol.Response response)
    {
        NetworkManager? networkManager = NetworkManager.Singleton;
        if (networkManager?.CustomMessagingManager == null)
        {
            return;
        }

        string json = UnityEngine.JsonUtility.ToJson(response);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        SendResponseBytes(recipientClientId, bytes);
    }

    private static void SendResponseBytes(ulong recipientClientId, byte[] bytes)
    {
        NetworkManager? networkManager = NetworkManager.Singleton;
        if (networkManager?.CustomMessagingManager == null)
        {
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(bytes.Length, Allocator.Temp);
        writer.WriteBytes(bytes);
        networkManager.CustomMessagingManager.SendNamedMessage(
            MessageBoardProtocol.ServerToClientListener,
            recipientClientId,
            writer,
            NetworkDelivery.ReliableFragmentedSequenced);
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
}
