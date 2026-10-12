using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OPLServerBanners;

internal static class WarmupObstacleServer
{
    private const float BrokenTargetRespawnSeconds = 5f;
    private const float PieceSyncIntervalSeconds = 0.1f;

    private static readonly List<ServerObstacle> Obstacles = new();
    private static readonly HashSet<int> DirtyObjectIds = new();

    private static bool isInitialized;
    private static bool messageHandlerRegistered;
    private static CustomMessagingManager? registeredMessagingManager;
    private static int nextObjectId = 1;
    private static ClearVote? clearVote;
    private static float nextPieceSyncAt;

    public static void Initialize()
    {
        isInitialized = true;
        messageHandlerRegistered = false;
        registeredMessagingManager = null;
        nextPieceSyncAt = 0f;
    }

    public static void ApplyPatches(Harmony harmony)
    {
        harmony.CreateClassProcessor(typeof(EventManagerTriggerEventPatch)).Patch();
    }

    public static void Shutdown()
    {
        isInitialized = false;
        UnregisterMessageHandler();
        ClearAllObjects(broadcast: false);
        clearVote = null;
        DirtyObjectIds.Clear();
        nextObjectId = 1;
    }

    public static void Tick()
    {
        if (!isInitialized)
        {
            return;
        }

        if (!EnsureMessageHandlerRegistered())
        {
            return;
        }

        ProcessClearVote();
        ProcessBrokenTargetRespawns();
        if (Time.unscaledTime >= nextPieceSyncAt && HasBrokenObstacles())
        {
            BroadcastState();
            nextPieceSyncAt = Time.unscaledTime + PieceSyncIntervalSeconds;
        }
    }

    public static bool TryHandleChatCommand(Player player, string command, string[] args)
    {
        if (string.Equals(command, "/wbw", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length == 1 && string.Equals(args[0].Trim(), "cup", StringComparison.OrdinalIgnoreCase))
            {
                ToggleTrophy(player, WarmupObstacleCatalog.WbwCupId);
            }
            else
            {
                SendChat(player.OwnerClientId, "Usage: /wbw cup", "#b8b8b8");
            }

            return true;
        }

        if (string.Equals(command, "/bi", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length == 1 && string.Equals(args[0].Trim(), "cup", StringComparison.OrdinalIgnoreCase))
            {
                ToggleTrophy(player, WarmupObstacleCatalog.BiCupId);
            }
            else
            {
                SendChat(player.OwnerClientId, "Usage: /bi cup", "#b8b8b8");
            }

            return true;
        }

        if (!string.Equals(command, "/obstacles", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(command, "/obs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer)
        {
            return true;
        }

        string subCommand = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : string.Empty;
        switch (subCommand)
        {
            case "clear":
                StartClearVote(player.OwnerClientId);
                break;
            case "yes":
            case "y":
                CastClearVote(player.OwnerClientId, voteYes: true);
                break;
            case "no":
            case "n":
                CastClearVote(player.OwnerClientId, voteYes: false);
                break;
            case "count":
                SendChat(player.OwnerClientId, $"Warmup obstacles: {Obstacles.Count}/{WarmupObstacleProtocol.MaxPlacedObjects}", "#b8b8b8");
                break;
            default:
                SendChat(player.OwnerClientId, "Usage: /obstacles clear | yes | no | count", "#b8b8b8");
                break;
        }

        return true;
    }

    private static void ToggleTrophy(Player player, string trophyAssetId)
    {
        if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (!CompetitiveSkatingConfigServer.IsClientAdmin(player.OwnerClientId))
        {
            SendChat(player.OwnerClientId, "Only Steam IDs listed in admin_steam_ids.json can spawn or despawn trophies.", "#e74c3c");
            return;
        }

        if (!IsWarmup())
        {
            SendChat(player.OwnerClientId, "Trophies can only be spawned during warmup.", "#e74c3c");
            return;
        }

        if (!WarmupObstacleCatalog.TryGet(trophyAssetId, out WarmupObstacleDefinition definition))
        {
            SendChat(player.OwnerClientId, "That trophy is not available.", "#e74c3c");
            return;
        }

        ServerObstacle? existingTrophy = Obstacles.FirstOrDefault(obstacle =>
            string.Equals(obstacle.AssetId, trophyAssetId, StringComparison.OrdinalIgnoreCase));
        if (existingTrophy != null)
        {
            DestroyObstacle(existingTrophy);
            Obstacles.Remove(existingTrophy);
            DirtyObjectIds.Add(existingTrophy.Id);
            BroadcastState("success", $"Despawned {definition.Label}.");
            SendChat(player.OwnerClientId, $"Despawned {definition.Label}.", "#2ecc71");
            return;
        }

        foreach (ServerObstacle otherTrophy in Obstacles
                     .Where(obstacle => WarmupObstacleCatalog.TryGet(obstacle.AssetId, out WarmupObstacleDefinition candidate) && candidate.IsTrophy)
                     .ToArray())
        {
            DestroyObstacle(otherTrophy);
            Obstacles.Remove(otherTrophy);
            DirtyObjectIds.Add(otherTrophy.Id);
        }

        if (Obstacles.Count >= WarmupObstacleProtocol.MaxPlacedObjects)
        {
            SendChat(player.OwnerClientId, $"The obstacle cap is {WarmupObstacleProtocol.MaxPlacedObjects}. Delete an obstacle before spawning a trophy.", "#e74c3c");
            return;
        }

        ServerObstacle? trophy = CreateServerObstacle(definition, Vector3.zero, Vector3.zero);
        if (trophy == null)
        {
            SendChat(player.OwnerClientId, $"Failed to spawn {definition.Label}. Check the s4_trophies bundle on the server.", "#e74c3c");
            return;
        }

        Obstacles.Add(trophy);
        DirtyObjectIds.Add(trophy.Id);
        BroadcastState("success", $"Spawned {definition.Label} at centre ice.");
        SendChat(player.OwnerClientId, $"Spawned {definition.Label} at centre ice. Use the same command to despawn it.", "#2ecc71");
    }

    public static void TryBreakTarget(int objectId, Collision collision, Puck puck)
    {
        if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        ServerObstacle? obstacle = Obstacles.FirstOrDefault(candidate => candidate.Id == objectId);
        if (obstacle == null || obstacle.IsBroken || !WarmupObstacleCatalog.TryGet(obstacle.AssetId, out WarmupObstacleDefinition definition) || !definition.IsBreakableTarget)
        {
            return;
        }

        Vector3 hitPoint = TryGetAverageContactPoint(collision, out Vector3 contactPoint)
            ? contactPoint
            : puck.transform.position;
        float hitSpeed = collision?.relativeVelocity.magnitude ?? puck.Rigidbody.linearVelocity.magnitude;
        int pieceCount = DetermineTargetPieceCount(obstacle, hitPoint, hitSpeed);

        obstacle.IsBroken = true;
        obstacle.RespawnAt = Time.time + BrokenTargetRespawnSeconds;
        if (obstacle.Root)
        {
            obstacle.Root.SetActive(false);
        }

        SpawnTargetPieces(obstacle, hitPoint, puck.Rigidbody ? puck.Rigidbody.linearVelocity : Vector3.zero, pieceCount, hitSpeed);
        DirtyObjectIds.Add(obstacle.Id);
        BroadcastState("success", $"Target broke into {pieceCount} piece{(pieceCount == 1 ? string.Empty : "s")}.");
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
            currentManager.RegisterNamedMessageHandler(WarmupObstacleProtocol.ClientToServerListener, ReceiveRequest);
            registeredMessagingManager = currentManager;
            messageHandlerRegistered = true;
            return true;
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to register warmup obstacle server message handler: {exception}");
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
            registeredMessagingManager.UnregisterNamedMessageHandler(WarmupObstacleProtocol.ClientToServerListener);
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to unregister warmup obstacle server message handler: {exception}");
        }
        finally
        {
            registeredMessagingManager = null;
            messageHandlerRegistered = false;
        }
    }

    private static void ReceiveRequest(ulong senderClientId, FastBufferReader reader)
    {
        try
        {
            string json = ReadJsonPayload(reader);
            WarmupObstacleProtocol.Request? request = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonConvert.DeserializeObject<WarmupObstacleProtocol.Request>(json);
            if (request == null)
            {
                SendState(senderClientId, "error", "Obstacle request could not be read.");
                return;
            }

            switch (request.Action ?? string.Empty)
            {
                case WarmupObstacleProtocol.ActionRequestState:
                    SendState(senderClientId);
                    break;
                case WarmupObstacleProtocol.ActionPlace:
                    PlaceObject(senderClientId, request);
                    break;
                case WarmupObstacleProtocol.ActionDelete:
                    DeleteObject(senderClientId, request.ObjectId);
                    break;
                case WarmupObstacleProtocol.ActionStartClearVote:
                    StartClearVote(senderClientId);
                    break;
                case WarmupObstacleProtocol.ActionCastClearVote:
                    CastClearVote(senderClientId, request.VoteYes);
                    break;
                default:
                    SendState(senderClientId, "warning", "That obstacle action is not supported by this server.");
                    break;
            }
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to process warmup obstacle request: {exception}");
            SendState(senderClientId, "error", "The server failed to process that obstacle request.");
        }
    }

    private static void PlaceObject(ulong senderClientId, WarmupObstacleProtocol.Request request)
    {
        if (!EnsureWarmup(senderClientId))
        {
            return;
        }

        if (!WarmupObstacleCatalog.TryGet(request.AssetId, out WarmupObstacleDefinition definition))
        {
            SendState(senderClientId, "warning", "That obstacle asset is not available.");
            return;
        }

        if (Obstacles.Count >= WarmupObstacleProtocol.MaxPlacedObjects)
        {
            SendState(senderClientId, "warning", $"The obstacle cap is {WarmupObstacleProtocol.MaxPlacedObjects}. Delete something before placing more.");
            return;
        }

        Vector3 userEuler = WarmupObstaclePlacement.SanitizeUserEuler(request.RotationEuler?.ToVector3() ?? Vector3.zero);
        Vector3 position = request.Position?.ToVector3() ?? Vector3.zero;
        ServerObstacle? obstacle = CreateServerObstacle(definition, position, userEuler);
        if (obstacle == null)
        {
            SendState(senderClientId, "error", $"Failed to create '{definition.Label}'. Check the asset bundle files.");
            return;
        }

        Obstacles.Add(obstacle);
        DirtyObjectIds.Add(obstacle.Id);
        BroadcastState("success", $"Placed {definition.Label}. {Obstacles.Count}/{WarmupObstacleProtocol.MaxPlacedObjects}");
    }

    private static ServerObstacle? CreateServerObstacle(WarmupObstacleDefinition definition, Vector3 position, Vector3 userEuler)
    {
        int objectId = nextObjectId++;
        GameObject? root = WarmupObstacleAssets.CreateInstance(definition.Id, $"WarmupObstacle_{objectId}_{definition.Id}", serverPhysical: true);
        if (!root)
        {
            return null;
        }

        root.transform.SetPositionAndRotation(position, WarmupObstaclePlacement.GetWorldRotation(definition.Id, userEuler));
        WarmupObstacleAssets.ClampBottomToIce(root.transform);
        Object.DontDestroyOnLoad(root);

        WarmupObstacleVisual visual = root.GetComponent<WarmupObstacleVisual>() ?? root.AddComponent<WarmupObstacleVisual>();
        visual.Initialize(objectId, definition.Id);

        if (definition.IsPasser)
        {
            WarmupPasserBumper passer = root.GetComponent<WarmupPasserBumper>() ?? root.AddComponent<WarmupPasserBumper>();
            passer.Configure(objectId);
        }

        if (definition.IsBreakableTarget)
        {
            WarmupBreakableTarget target = root.GetComponent<WarmupBreakableTarget>() ?? root.AddComponent<WarmupBreakableTarget>();
            target.Configure(objectId);
        }

        return new ServerObstacle
        {
            Id = objectId,
            AssetId = definition.Id,
            Root = root,
            Position = root.transform.position,
            UserEuler = userEuler
        };
    }

    private static void DeleteObject(ulong senderClientId, int objectId)
    {
        if (!EnsureWarmup(senderClientId))
        {
            return;
        }

        ServerObstacle? obstacle = Obstacles.FirstOrDefault(candidate => candidate.Id == objectId);
        if (obstacle == null)
        {
            SendState(senderClientId, "warning", "That obstacle is no longer available.");
            return;
        }

        DestroyObstacle(obstacle);
        Obstacles.Remove(obstacle);
        DirtyObjectIds.Add(objectId);
        BroadcastState("success", $"Deleted obstacle {objectId}. {Obstacles.Count}/{WarmupObstacleProtocol.MaxPlacedObjects}");
    }

    private static bool EnsureWarmup(ulong clientId)
    {
        if (IsWarmup())
        {
            return true;
        }

        SendState(clientId, "warning", "Warmup obstacles can only be edited during warmup.");
        return false;
    }

    private static bool IsWarmup()
    {
        GameManager? gameManager = NetworkBehaviourSingleton<GameManager>.Instance;
        return gameManager != null && gameManager.Phase == GamePhase.Warmup;
    }

    private static void StartClearVote(ulong initiatorClientId)
    {
        if (!EnsureWarmup(initiatorClientId))
        {
            return;
        }

        if (Obstacles.Count == 0)
        {
            SendState(initiatorClientId, "info", "There are no warmup obstacles to clear.");
            return;
        }

        Player? player = GetPlayer(initiatorClientId);
        if (player == null)
        {
            SendState(initiatorClientId, "warning", "Only connected players can start an obstacle clear vote.");
            return;
        }

        if (clearVote != null && clearVote.IsActive)
        {
            CastClearVote(initiatorClientId, voteYes: true);
            return;
        }

        List<Player> eligible = GetEligibleClearVotePlayers();
        int requiredVotes = Mathf.Max(1, (eligible.Count / 2) + 1);
        clearVote = new ClearVote
        {
            Initiator = player.Username.Value.ToString(),
            ExpiresAt = Time.unscaledTime + WarmupObstacleProtocol.ClearVoteSeconds,
            RequiredVotes = requiredVotes,
            EligibleSteamIds = new HashSet<string>(eligible.Select(GetSteamId), StringComparer.Ordinal)
        };

        if (clearVote.EligibleSteamIds.Contains(GetSteamId(player)))
        {
            clearVote.YesSteamIds.Add(GetSteamId(player));
        }

        BroadcastState("info", $"{clearVote.Initiator} started a vote to clear all warmup obstacles.");
        SendChatToEligible($"Obstacle clear vote started by {clearVote.Initiator}. Use F1/F2 in the editor or /obstacles yes/no.", "#e67e22");
        CompleteClearVoteIfReady();
    }

    private static void CastClearVote(ulong clientId, bool voteYes)
    {
        if (clearVote == null || !clearVote.IsActive)
        {
            SendState(clientId, "info", "There is no active obstacle clear vote.");
            return;
        }

        Player? player = GetPlayer(clientId);
        if (player == null)
        {
            return;
        }

        string steamId = GetSteamId(player);
        if (!clearVote.EligibleSteamIds.Contains(steamId))
        {
            SendState(clientId, "warning", "Only skaters can vote to clear warmup obstacles.");
            return;
        }

        clearVote.YesSteamIds.Remove(steamId);
        clearVote.NoSteamIds.Remove(steamId);
        if (voteYes)
        {
            clearVote.YesSteamIds.Add(steamId);
        }
        else
        {
            clearVote.NoSteamIds.Add(steamId);
        }

        BroadcastState("info", $"Obstacle clear vote: {clearVote.YesSteamIds.Count}/{clearVote.RequiredVotes} yes.");
        CompleteClearVoteIfReady();
    }

    private static void ProcessClearVote()
    {
        if (clearVote == null || !clearVote.IsActive)
        {
            return;
        }

        if (CompleteClearVoteIfReady())
        {
            return;
        }

        if (Time.unscaledTime < clearVote.ExpiresAt)
        {
            return;
        }

        clearVote.Result = "failed";
        BroadcastState("warning", "Obstacle clear vote timed out.");
        SendChatToEligible("Obstacle clear vote failed.", "#e74c3c");
        clearVote = null;
    }

    private static bool CompleteClearVoteIfReady()
    {
        if (clearVote == null || !clearVote.IsActive)
        {
            return false;
        }

        if (clearVote.YesSteamIds.Count < clearVote.RequiredVotes)
        {
            return false;
        }

        int removed = Obstacles.Count;
        ClearAllObjects(broadcast: false);
        clearVote.Result = "passed";
        BroadcastState("success", $"Obstacle clear vote passed. Removed {removed} object{(removed == 1 ? string.Empty : "s")}.");
        SendChatToEligible($"Obstacle clear vote passed. Removed {removed} object{(removed == 1 ? string.Empty : "s")}.", "#2ecc71");
        clearVote = null;
        return true;
    }

    private static void ProcessBrokenTargetRespawns()
    {
        for (int index = Obstacles.Count - 1; index >= 0; index--)
        {
            ServerObstacle obstacle = Obstacles[index];
            if (!obstacle.IsBroken || obstacle.RespawnAt <= 0f || Time.time < obstacle.RespawnAt)
            {
                continue;
            }

            DestroyPieces(obstacle);
            obstacle.IsBroken = false;
            obstacle.RespawnAt = 0f;
            if (obstacle.Root)
            {
                obstacle.Root.transform.SetPositionAndRotation(obstacle.Position, WarmupObstaclePlacement.GetWorldRotation(obstacle.AssetId, obstacle.UserEuler));
                WarmupObstacleAssets.ClampBottomToIce(obstacle.Root.transform);
                obstacle.Position = obstacle.Root.transform.position;
                obstacle.Root.SetActive(true);
                WarmupBreakableTarget? target = obstacle.Root.GetComponent<WarmupBreakableTarget>();
                target?.Arm();
            }

            DirtyObjectIds.Add(obstacle.Id);
            BroadcastState("success", "Target respawned.");
        }
    }

    private static void ClearAllObjects(bool broadcast)
    {
        for (int index = Obstacles.Count - 1; index >= 0; index--)
        {
            DestroyObstacle(Obstacles[index]);
        }

        Obstacles.Clear();
        DirtyObjectIds.Clear();
        if (broadcast)
        {
            BroadcastState("success", "Cleared all warmup obstacles.");
        }
    }

    private static void DestroyObstacle(ServerObstacle obstacle)
    {
        DestroyPieces(obstacle);
        if (obstacle.Root)
        {
            Object.Destroy(obstacle.Root);
        }
    }

    private static void DestroyPieces(ServerObstacle obstacle)
    {
        foreach (GameObject piece in obstacle.Pieces)
        {
            if (piece)
            {
                Object.Destroy(piece);
            }
        }

        obstacle.Pieces.Clear();
    }

    private static void SpawnTargetPieces(ServerObstacle obstacle, Vector3 hitPoint, Vector3 puckVelocity, int pieceCount, float hitSpeed)
    {
        DestroyPieces(obstacle);

        Bounds bounds = default;
        bool hasBounds = obstacle.Root && WarmupObstacleAssets.TryGetWorldBounds(obstacle.Root, out bounds);
        Vector3 center = hasBounds ? bounds.center : obstacle.Position + Vector3.up * 0.5f;
        Vector3 size = hasBounds ? bounds.size : new Vector3(1.2f, 0.4f, 1.2f);
        Vector3 away = Vector3.ProjectOnPlane(center - hitPoint, Vector3.up);
        if (away.sqrMagnitude < 0.001f)
        {
            away = Vector3.ProjectOnPlane(puckVelocity, Vector3.up);
        }

        if (away.sqrMagnitude < 0.001f)
        {
            away = obstacle.Root ? obstacle.Root.transform.forward : Vector3.forward;
        }

        away.Normalize();

        float speedFactor = Mathf.InverseLerp(4f, 28f, hitSpeed);
        for (int index = 0; index < pieceCount; index++)
        {
            GameObject piece = WarmupObstacleAssets.CreateTargetPiece($"WarmupTargetPiece_{obstacle.Id}_{index}", serverPhysical: true);
            float lane = pieceCount <= 1 ? 0f : Mathf.Lerp(-0.5f, 0.5f, index / (float)(pieceCount - 1));
            Vector3 tangent = Vector3.Cross(Vector3.up, away).normalized;
            Vector3 piecePosition = center + tangent * lane * Mathf.Max(size.x, size.z) * 0.25f + Vector3.up * (0.04f * index);
            Quaternion pieceRotation = Quaternion.LookRotation(away, Vector3.up) * Quaternion.Euler(UnityEngine.Random.Range(-18f, 18f), lane * 38f, UnityEngine.Random.Range(-35f, 35f));
            piece.transform.SetPositionAndRotation(piecePosition, pieceRotation);
            piece.transform.localScale = new Vector3(
                Mathf.Max(0.18f, size.x / Mathf.Max(pieceCount, 2) * UnityEngine.Random.Range(0.55f, 0.95f)),
                Mathf.Clamp(size.y * UnityEngine.Random.Range(0.12f, 0.28f), 0.04f, 0.22f),
                Mathf.Max(0.14f, size.z * UnityEngine.Random.Range(0.16f, 0.34f)));
            Object.DontDestroyOnLoad(piece);

            WarmupObstacleVisual visual = piece.GetComponent<WarmupObstacleVisual>() ?? piece.AddComponent<WarmupObstacleVisual>();
            visual.Initialize(obstacle.Id, obstacle.AssetId);

            Rigidbody? rigidbody = piece.GetComponent<Rigidbody>();
            if (rigidbody)
            {
                float impulseScale = Mathf.Lerp(0.9f, 2.4f, speedFactor);
                Vector3 impulse = (away + tangent * lane * 0.85f + Vector3.up * UnityEngine.Random.Range(0.45f, 0.9f)).normalized * impulseScale;
                rigidbody.linearVelocity = puckVelocity * 0.18f + impulse;
                rigidbody.angularVelocity = new Vector3(UnityEngine.Random.Range(-5f, 5f), UnityEngine.Random.Range(-7f, 7f), UnityEngine.Random.Range(-5f, 5f)) * Mathf.Lerp(0.7f, 1.4f, speedFactor);
            }

            obstacle.Pieces.Add(piece);
        }
    }

    private static int DetermineTargetPieceCount(ServerObstacle obstacle, Vector3 hitPoint, float hitSpeed)
    {
        float centerFactor = 0.5f;
        if (obstacle.Root && WarmupObstacleAssets.TryGetWorldBounds(obstacle.Root, out Bounds bounds))
        {
            Vector3 planar = Vector3.ProjectOnPlane(hitPoint - bounds.center, Vector3.up);
            float radius = Mathf.Max(0.2f, Mathf.Max(bounds.extents.x, bounds.extents.z));
            centerFactor = 1f - Mathf.Clamp01(planar.magnitude / radius);
        }

        float speedFactor = Mathf.InverseLerp(4f, 28f, hitSpeed);
        float breakScore = Mathf.Clamp01(speedFactor * 0.58f + centerFactor * 0.42f);
        return Mathf.Clamp(1 + Mathf.RoundToInt(breakScore * 4f), 1, 5);
    }

    private static bool TryGetAverageContactPoint(Collision collision, out Vector3 contactPoint)
    {
        contactPoint = Vector3.zero;
        if (collision == null || collision.contactCount == 0)
        {
            return false;
        }

        for (int index = 0; index < collision.contactCount; index++)
        {
            contactPoint += collision.GetContact(index).point;
        }

        contactPoint /= collision.contactCount;
        return true;
    }

    private static void BroadcastState(string? overrideKind = null, string? overrideMessage = null)
    {
        NetworkManager? networkManager = NetworkManager.Singleton;
        if (networkManager?.CustomMessagingManager == null || !networkManager.IsServer)
        {
            return;
        }

        if (clearVote == null || !clearVote.IsActive)
        {
            WarmupObstacleProtocol.State sharedState = BuildState(NetworkManager.ServerClientId, overrideKind, overrideMessage);
            string json = JsonConvert.SerializeObject(sharedState);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            foreach (ulong clientId in networkManager.ConnectedClientsIds)
            {
                SendStateBytes(networkManager, clientId, bytes);
            }

            return;
        }

        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            SendState(clientId, overrideKind, overrideMessage);
        }
    }

    private static void SendState(ulong recipientClientId, string? overrideKind = null, string? overrideMessage = null)
    {
        NetworkManager? networkManager = NetworkManager.Singleton;
        if (networkManager?.CustomMessagingManager == null)
        {
            return;
        }

        WarmupObstacleProtocol.State state = BuildState(recipientClientId, overrideKind, overrideMessage);
        string json = JsonConvert.SerializeObject(state);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        SendStateBytes(networkManager, recipientClientId, bytes);
    }

    private static void SendStateBytes(NetworkManager networkManager, ulong recipientClientId, byte[] bytes)
    {
        using FastBufferWriter writer = new FastBufferWriter(bytes.Length, Allocator.Temp);
        writer.WriteBytes(bytes);
        networkManager.CustomMessagingManager.SendNamedMessage(
            WarmupObstacleProtocol.ServerToClientListener,
            recipientClientId,
            writer,
            NetworkDelivery.ReliableFragmentedSequenced);
    }

    private static WarmupObstacleProtocol.State BuildState(ulong recipientClientId, string? overrideKind, string? overrideMessage)
    {
        return new WarmupObstacleProtocol.State
        {
            IsWarmup = IsWarmup(),
            CanEdit = IsWarmup(),
            MaxObjects = WarmupObstacleProtocol.MaxPlacedObjects,
            ObjectCount = Obstacles.Count,
            StatusKind = string.IsNullOrWhiteSpace(overrideKind) ? "info" : overrideKind ?? "info",
            StatusMessage = string.IsNullOrWhiteSpace(overrideMessage)
                ? $"Warmup obstacles: {Obstacles.Count}/{WarmupObstacleProtocol.MaxPlacedObjects}"
                : overrideMessage ?? string.Empty,
            Assets = WarmupObstacleCatalog.ToAssetInfos(),
            Objects = Obstacles.Select(ToProtocolObject).ToArray(),
            ClearVote = BuildClearVoteState(recipientClientId)
        };
    }

    private static bool HasBrokenObstacles()
    {
        for (int index = 0; index < Obstacles.Count; index++)
        {
            if (Obstacles[index].IsBroken)
            {
                return true;
            }
        }

        return false;
    }

    private static WarmupObstacleProtocol.PlacedObject ToProtocolObject(ServerObstacle obstacle)
    {
        if (obstacle.Root && !obstacle.IsBroken)
        {
            obstacle.Position = obstacle.Root.transform.position;
        }

        return new WarmupObstacleProtocol.PlacedObject
        {
            Id = obstacle.Id,
            AssetId = obstacle.AssetId,
            Position = new WarmupObstacleProtocol.Vec3Data(obstacle.Position),
            RotationEuler = new WarmupObstacleProtocol.Vec3Data(obstacle.UserEuler),
            IsBroken = obstacle.IsBroken,
            Pieces = obstacle.Pieces
                .Where(piece => piece)
                .Select((piece, index) => new WarmupObstacleProtocol.PieceState
                {
                    Id = index,
                    Position = new WarmupObstacleProtocol.Vec3Data(piece.transform.position),
                    RotationEuler = new WarmupObstacleProtocol.Vec3Data(piece.transform.rotation.eulerAngles),
                    Scale = new WarmupObstacleProtocol.Vec3Data(piece.transform.localScale)
                })
                .ToArray()
        };
    }

    private static WarmupObstacleProtocol.ClearVoteState? BuildClearVoteState(ulong recipientClientId)
    {
        if (clearVote == null || !clearVote.IsActive)
        {
            return null;
        }

        Player? player = GetPlayer(recipientClientId);
        string steamId = player != null ? GetSteamId(player) : string.Empty;
        return new WarmupObstacleProtocol.ClearVoteState
        {
            IsActive = true,
            Initiator = clearVote.Initiator,
            YesCount = clearVote.YesSteamIds.Count,
            NoCount = clearVote.NoSteamIds.Count,
            RequiredVotes = clearVote.RequiredVotes,
            EligibleSkaters = clearVote.EligibleSteamIds.Count,
            SecondsRemaining = Mathf.Max(0f, clearVote.ExpiresAt - Time.unscaledTime),
            LocalPlayerHasVoted = !string.IsNullOrWhiteSpace(steamId) && (clearVote.YesSteamIds.Contains(steamId) || clearVote.NoSteamIds.Contains(steamId)),
            Result = clearVote.Result
        };
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

    private static Player? GetPlayer(ulong clientId)
    {
        return MonoBehaviourSingleton<PlayerManager>.Instance?.GetPlayerByClientId(clientId);
    }

    private static string GetSteamId(Player player)
    {
        return player.SteamId.Value.ToString();
    }

    private static List<Player> GetEligibleClearVotePlayers()
    {
        PlayerManager? playerManager = MonoBehaviourSingleton<PlayerManager>.Instance;
        if (playerManager == null)
        {
            return new List<Player>();
        }

        List<Player> skaters = playerManager.GetPlayers()
            .Where(player =>
                player &&
                player.Role == PlayerRole.Attacker &&
                (player.Team == PlayerTeam.Red || player.Team == PlayerTeam.Blue))
            .ToList();
        if (skaters.Count > 0)
        {
            return skaters;
        }

        return playerManager.GetPlayers()
            .Where(player => player && (player.Team == PlayerTeam.Red || player.Team == PlayerTeam.Blue))
            .ToList();
    }

    private static void SendChat(ulong clientId, string content, string color)
    {
        _ = color;
        ChatManager? chatManager = NetworkBehaviourSingleton<ChatManager>.Instance;
        if (chatManager)
        {
            chatManager.Server_SendChatMessageToClients(content, new[] { clientId });
        }
    }

    private static void SendChatToEligible(string content, string color)
    {
        _ = color;
        List<ulong> recipients = GetEligibleClearVotePlayers()
            .Select(player => player.OwnerClientId)
            .Distinct()
            .ToList();
        if (recipients.Count == 0 && NetworkManager.Singleton)
        {
            recipients.AddRange(NetworkManager.Singleton.ConnectedClientsIds);
        }

        if (recipients.Count > 0)
        {
            ChatManager? chatManager = NetworkBehaviourSingleton<ChatManager>.Instance;
            if (chatManager)
            {
                chatManager.Server_SendChatMessageToClients(content, recipients.ToArray());
            }
        }
    }

    private sealed class ServerObstacle
    {
        public int Id;
        public string AssetId = string.Empty;
        public GameObject? Root;
        public Vector3 Position;
        public Vector3 UserEuler;
        public bool IsBroken;
        public float RespawnAt;
        public readonly List<GameObject> Pieces = new();
    }

    private sealed class ClearVote
    {
        public string Initiator = string.Empty;
        public float ExpiresAt;
        public int RequiredVotes;
        public string Result = string.Empty;
        public HashSet<string> EligibleSteamIds = new(StringComparer.Ordinal);
        public HashSet<string> YesSteamIds = new(StringComparer.Ordinal);
        public HashSet<string> NoSteamIds = new(StringComparer.Ordinal);

        public bool IsActive => string.IsNullOrWhiteSpace(Result);
    }

    [HarmonyPatch(typeof(EventManager), nameof(EventManager.TriggerEvent))]
    private static class EventManagerTriggerEventPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(string eventName, Dictionary<string, object>? message)
        {
            if (!string.Equals(eventName, "Event_Server_OnChatCommand", StringComparison.Ordinal) || message == null)
            {
                return true;
            }

            if (!message.TryGetValue("command", out object commandValue))
            {
                return true;
            }

            string command = commandValue?.ToString() ?? string.Empty;
            if (!string.Equals(command, "/obstacles", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(command, "/obs", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!message.TryGetValue("clientId", out object clientIdValue))
            {
                return false;
            }

            ulong clientId;
            try
            {
                clientId = Convert.ToUInt64(clientIdValue);
            }
            catch
            {
                return false;
            }

            Player player = MonoBehaviourSingleton<PlayerManager>.Instance.GetPlayerByClientId(clientId);
            if (!player)
            {
                return false;
            }

            string[] args = Array.Empty<string>();
            if (message.TryGetValue("args", out object argsValue))
            {
                args = argsValue switch
                {
                    string[] stringArgs => stringArgs,
                    object[] objectArgs => objectArgs.Select(value => value?.ToString() ?? string.Empty).ToArray(),
                    IEnumerable<string> enumerableArgs => enumerableArgs.ToArray(),
                    _ => args
                };
            }

            return !TryHandleChatCommand(player, command, args);
        }
    }
}

internal sealed class WarmupBreakableTarget : MonoBehaviour
{
    private const float ArmDelaySeconds = 0.2f;

    private int objectId;
    private float armedAt;

    public void Configure(int id)
    {
        objectId = id;
        Arm();
    }

    public void Arm()
    {
        armedAt = Time.time + ArmDelaySeconds;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer || Time.time < armedAt)
        {
            return;
        }

        Puck? puck = collision.rigidbody ? collision.rigidbody.GetComponent<Puck>() : collision.gameObject.GetComponentInParent<Puck>();
        if (!puck || puck.IsReplay.Value)
        {
            return;
        }

        WarmupObstacleServer.TryBreakTarget(objectId, collision, puck);
    }
}

internal sealed class WarmupPasserBumper : MonoBehaviour
{
    private const float DetectRadius = 1.5f;
    private const float DetectRadiusSqr = DetectRadius * DetectRadius;
    private const float RedirectSpeed = 18f;
    private const float CooldownSeconds = 0.5f;

    private int objectId;
    private float cooldown;

    public void Configure(int id)
    {
        objectId = id;
        cooldown = 0f;
    }

    private void FixedUpdate()
    {
        if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (cooldown > 0f)
        {
            cooldown -= Time.fixedDeltaTime;
            return;
        }

        PuckManager? puckManager = MonoBehaviourSingleton<PuckManager>.Instance;
        if (puckManager == null)
        {
            return;
        }

        foreach (Puck puck in puckManager.GetPucks(includeReplay: true))
        {
            if (!puck || puck.IsReplay.Value || !puck.Rigidbody)
            {
                continue;
            }

            Vector3 delta = puck.transform.position - transform.position;
            if (delta.sqrMagnitude > DetectRadiusSqr)
            {
                continue;
            }

            Player? targetPlayer = GetLatestHumanTouchingPlayer(puck);
            if (!targetPlayer)
            {
                continue;
            }

            Vector3 targetPosition = targetPlayer.Stick
                ? targetPlayer.Stick.BladeHandlePosition
                : targetPlayer.transform.position;
            Vector3 direction = targetPosition - puck.transform.position;
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = transform.forward;
            }

            puck.Rigidbody.linearVelocity = direction.normalized * RedirectSpeed;
            puck.Rigidbody.angularVelocity = Vector3.zero;
            cooldown = CooldownSeconds;
            Plugin.Log($"Warmup passer {objectId} redirected puck toward {targetPlayer.Username.Value}.");
            return;
        }
    }

    private static Player? GetLatestHumanTouchingPlayer(Puck puck)
    {
        List<KeyValuePair<Player, float>> collisions = puck.GetPlayerCollisions();
        if (collisions == null || collisions.Count == 0)
        {
            return null;
        }

        for (int index = collisions.Count - 1; index >= 0; index--)
        {
            Player player = collisions[index].Key;
            if (!player)
            {
                continue;
            }

            string steamId = player.SteamId.Value.ToString();
            if (steamId == "1" || steamId == "2")
            {
                continue;
            }

            return player;
        }

        return null;
    }
}
