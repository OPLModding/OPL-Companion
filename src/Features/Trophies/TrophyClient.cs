using System;
using System.Text;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OPLServerBanners;

internal static class TrophyClient
{
    private static CustomMessagingManager? registeredMessagingManager;
    private static bool messageHandlerRegistered;
    private static bool requestedState;
    private static GameObject? trophyRoot;
    private static string trophyId = string.Empty;

    public static void Initialize()
    {
    }

    public static void Shutdown()
    {
        UnregisterMessageHandler();
        DestroyTrophy();
        TrophyAssets.Teardown();
        requestedState = false;
    }

    public static void Tick()
    {
        NetworkManager? networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsConnectedClient)
        {
            requestedState = false;
            DestroyTrophy();
            return;
        }

        if (networkManager.IsServer)
        {
            DestroyTrophy();
            return;
        }

        if (EnsureMessageHandlerRegistered() && !requestedState)
        {
            RequestState(networkManager);
        }
    }

    private static void RequestState(NetworkManager networkManager)
    {
        byte[] payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new TrophyProtocol.Request { Action = TrophyProtocol.ActionRequestState }));
        using FastBufferWriter writer = new FastBufferWriter(payload.Length, Allocator.Temp);
        writer.WriteBytes(payload);
        networkManager.CustomMessagingManager.SendNamedMessage(TrophyProtocol.ClientToServerListener, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
        requestedState = true;
    }

    private static bool EnsureMessageHandlerRegistered()
    {
        CustomMessagingManager? manager = NetworkManager.Singleton?.CustomMessagingManager;
        if (manager == null)
        {
            return false;
        }

        if (messageHandlerRegistered && ReferenceEquals(registeredMessagingManager, manager))
        {
            return true;
        }

        UnregisterMessageHandler();
        manager.RegisterNamedMessageHandler(TrophyProtocol.ServerToClientListener, ReceiveState);
        registeredMessagingManager = manager;
        messageHandlerRegistered = true;
        requestedState = false;
        return true;
    }

    private static void UnregisterMessageHandler()
    {
        if (messageHandlerRegistered && registeredMessagingManager != null)
        {
            registeredMessagingManager.UnregisterNamedMessageHandler(TrophyProtocol.ServerToClientListener);
        }

        registeredMessagingManager = null;
        messageHandlerRegistered = false;
    }

    private static void ReceiveState(ulong senderClientId, FastBufferReader reader)
    {
        int remaining = reader.Length - reader.Position;
        if (remaining <= 0)
        {
            return;
        }

        byte[] payload = new byte[remaining];
        for (int index = 0; index < payload.Length; index++)
        {
            reader.ReadByte(out payload[index]);
        }

        TrophyProtocol.State? state = JsonConvert.DeserializeObject<TrophyProtocol.State>(Encoding.UTF8.GetString(payload));
        if (state == null || !state.IsSpawned)
        {
            DestroyTrophy();
            return;
        }

        if (!trophyRoot || !string.Equals(trophyId, state.TrophyId, StringComparison.OrdinalIgnoreCase))
        {
            DestroyTrophy();
            trophyRoot = TrophyAssets.CreateInstance(state.TrophyId, $"TrophyMirror_{state.TrophyId}", serverPhysical: false);
            trophyId = state.TrophyId;
            if (!trophyRoot)
            {
                return;
            }

            Object.DontDestroyOnLoad(trophyRoot);
        }

        trophyRoot.transform.position = state.Position.ToVector3();
    }

    private static void DestroyTrophy()
    {
        if (trophyRoot)
        {
            Object.Destroy(trophyRoot);
        }

        trophyRoot = null;
        trophyId = string.Empty;
    }
}
