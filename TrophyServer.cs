using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace OPLServerBanners;

internal static class TrophyServer
{
    private static bool isInitialized;
    private static bool messageHandlerRegistered;
    private static CustomMessagingManager? registeredMessagingManager;
    private static bool isTrophySpawned;
    private static string spawnedTrophyId = string.Empty;
    private static Vector3 spawnedPosition = Vector3.zero;

    public static void Initialize()
    {
        isInitialized = true;
    }

    public static void Shutdown()
    {
        isInitialized = false;
        UnregisterMessageHandler();
        Despawn(broadcast: false);
    }

    public static void ApplyPatches(Harmony harmony)
    {
        harmony.CreateClassProcessor(typeof(EventManagerTriggerEventPatch)).Patch();
    }

    public static void Tick()
    {
        EnsureMessageHandlerRegistered();
    }

    private static void Toggle(Player player, string trophyId)
    {
        if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (!IsListedAdmin(player))
        {
            return;
        }

        if (isTrophySpawned && string.Equals(spawnedTrophyId, trophyId, StringComparison.OrdinalIgnoreCase))
        {
            Despawn(broadcast: true);
            return;
        }

        // Dedicated servers are state-only: clients own their local visual instances.
        // This avoids any shader/material work on a headless server.
        isTrophySpawned = true;
        spawnedTrophyId = trophyId;
        spawnedPosition = Vector3.zero;
        BroadcastState();
    }

    private static void Despawn(bool broadcast)
    {
        isTrophySpawned = false;
        spawnedTrophyId = string.Empty;
        spawnedPosition = Vector3.zero;
        if (broadcast)
        {
            BroadcastState();
        }
    }

    private static bool EnsureMessageHandlerRegistered()
    {
        NetworkManager? networkManager = NetworkManager.Singleton;
        if (!isInitialized || networkManager == null || !networkManager.IsServer || networkManager.CustomMessagingManager == null)
        {
            UnregisterMessageHandler();
            return false;
        }

        CustomMessagingManager manager = networkManager.CustomMessagingManager;
        if (messageHandlerRegistered && ReferenceEquals(registeredMessagingManager, manager))
        {
            return true;
        }

        UnregisterMessageHandler();
        manager.RegisterNamedMessageHandler(TrophyProtocol.ClientToServerListener, ReceiveRequest);
        registeredMessagingManager = manager;
        messageHandlerRegistered = true;
        return true;
    }

    private static void UnregisterMessageHandler()
    {
        if (messageHandlerRegistered && registeredMessagingManager != null)
        {
            registeredMessagingManager.UnregisterNamedMessageHandler(TrophyProtocol.ClientToServerListener);
        }

        registeredMessagingManager = null;
        messageHandlerRegistered = false;
    }

    private static void ReceiveRequest(ulong senderClientId, FastBufferReader reader)
    {
        SendState(senderClientId);
    }

    private static void BroadcastState()
    {
        NetworkManager? networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsServer)
        {
            return;
        }

        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            SendState(clientId);
        }
    }

    private static void SendState(ulong clientId)
    {
        NetworkManager? networkManager = NetworkManager.Singleton;
        if (networkManager?.CustomMessagingManager == null)
        {
            return;
        }

        TrophyProtocol.State state = new TrophyProtocol.State
        {
            IsSpawned = isTrophySpawned && !string.IsNullOrWhiteSpace(spawnedTrophyId),
            TrophyId = spawnedTrophyId,
            Position = new TrophyProtocol.Vec3Data(spawnedPosition)
        };
        byte[] payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(state));
        using FastBufferWriter writer = new FastBufferWriter(payload.Length, Allocator.Temp);
        writer.WriteBytes(payload);
        networkManager.CustomMessagingManager.SendNamedMessage(TrophyProtocol.ServerToClientListener, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
    }

    private static bool IsListedAdmin(Player player)
    {
        string steamId = player.SteamId.Value.ToString().Trim();
        global::AdminManager? adminManager = MonoBehaviourSingleton<global::AdminManager>.Instance;
        return !string.IsNullOrWhiteSpace(steamId) && adminManager != null && adminManager.IsSteamIdAdmin(steamId);
    }

    [HarmonyPatch(typeof(EventManager), nameof(EventManager.TriggerEvent))]
    private static class EventManagerTriggerEventPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(string eventName, Dictionary<string, object>? message)
        {
            if (!string.Equals(eventName, "Event_Server_OnChatCommand", StringComparison.Ordinal) || message == null ||
                !message.TryGetValue("command", out object commandValue) || !message.TryGetValue("clientId", out object clientIdValue))
            {
                return true;
            }

            string command = commandValue?.ToString() ?? string.Empty;
            string? trophyId = string.Equals(command, "/wbw", StringComparison.OrdinalIgnoreCase) ? TrophyAssets.WbwCupId
                : string.Equals(command, "/bi", StringComparison.OrdinalIgnoreCase) ? TrophyAssets.BiCupId
                : null;
            if (trophyId == null)
            {
                return true;
            }

            string[] args = message.TryGetValue("args", out object argsValue)
                ? argsValue switch
                {
                    string[] stringArgs => stringArgs,
                    object[] objectArgs => objectArgs.Select(value => value?.ToString() ?? string.Empty).ToArray(),
                    IEnumerable<string> values => values.ToArray(),
                    _ => Array.Empty<string>()
                }
                : Array.Empty<string>();
            if (args.Length != 1 || !string.Equals(args[0].Trim(), "cup", StringComparison.OrdinalIgnoreCase) || !ulong.TryParse(clientIdValue?.ToString(), out ulong clientId))
            {
                return false;
            }

            Player? player = MonoBehaviourSingleton<PlayerManager>.Instance?.GetPlayerByClientId(clientId);
            if (player)
            {
                Toggle(player, trophyId);
            }

            return false;
        }
    }
}
