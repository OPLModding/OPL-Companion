using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OPLServerBanners;

internal static class WarmupObstacleClientWorld
{
    private const int RaycastHitBufferSize = 128;

    private static readonly Dictionary<int, MirrorObstacle> Mirrors = new();
    private static readonly HashSet<int> IncomingObjectIds = new();
    private static readonly List<int> StaleObjectIds = new();
    private static readonly HashSet<int> IncomingPieceIds = new();
    private static readonly List<int> StalePieceIds = new();
    private static readonly RaycastHit[] RaycastHits = new RaycastHit[RaycastHitBufferSize];
    private static WarmupObstacleVisual? highlightedVisual;

    public static void ApplyState(WarmupObstacleProtocol.State state)
    {
        if (NetworkManager.Singleton?.IsServer == true)
        {
            return;
        }

        IncomingObjectIds.Clear();
        foreach (WarmupObstacleProtocol.PlacedObject placedObject in state.Objects)
        {
            IncomingObjectIds.Add(placedObject.Id);
        }

        StaleObjectIds.Clear();
        foreach (int objectId in Mirrors.Keys)
        {
            if (!IncomingObjectIds.Contains(objectId))
            {
                StaleObjectIds.Add(objectId);
            }
        }

        foreach (int staleId in StaleObjectIds)
        {
            DestroyMirror(staleId);
        }

        foreach (WarmupObstacleProtocol.PlacedObject placedObject in state.Objects)
        {
            ApplyObject(placedObject);
        }
    }

    public static void Clear()
    {
        StaleObjectIds.Clear();
        foreach (int objectId in Mirrors.Keys)
        {
            StaleObjectIds.Add(objectId);
        }

        foreach (int objectId in StaleObjectIds)
        {
            DestroyMirror(objectId);
        }

        SetHighlightedVisual(null);
    }

    public static bool TryFindLookedAtObstacle(Ray ray, float maxDistance, out int objectId)
    {
        objectId = 0;
        int hitCount = Physics.RaycastNonAlloc(ray, RaycastHits, maxDistance, ~0, QueryTriggerInteraction.Collide);
        WarmupObstacleVisual? nearestVisual = null;
        float nearestDistance = float.MaxValue;

        for (int index = 0; index < hitCount; index++)
        {
            RaycastHit hit = RaycastHits[index];
            if (hit.distance >= nearestDistance)
            {
                continue;
            }

            WarmupObstacleVisual? visual = hit.collider.GetComponentInParent<WarmupObstacleVisual>();
            if (!visual || visual.ObjectId < 0)
            {
                continue;
            }

            nearestVisual = visual;
            nearestDistance = hit.distance;
        }

        if (nearestVisual)
        {
            objectId = nearestVisual.ObjectId;
            SetHighlightedVisual(nearestVisual);
            return true;
        }

        SetHighlightedVisual(null);
        return false;
    }

    private static void ApplyObject(WarmupObstacleProtocol.PlacedObject placedObject)
    {
        if (!Mirrors.TryGetValue(placedObject.Id, out MirrorObstacle mirror) || !mirror.Root)
        {
            GameObject? root = WarmupObstacleAssets.CreateInstance(
                placedObject.AssetId,
                $"WarmupObstacleMirror_{placedObject.Id}_{placedObject.AssetId}",
                serverPhysical: false);
            if (!root)
            {
                return;
            }

            Object.DontDestroyOnLoad(root);
            WarmupObstacleVisual visual = root.GetComponent<WarmupObstacleVisual>() ?? root.AddComponent<WarmupObstacleVisual>();
            visual.Initialize(placedObject.Id, placedObject.AssetId);
            mirror = new MirrorObstacle(root);
            Mirrors[placedObject.Id] = mirror;
        }

        mirror.Root.transform.SetPositionAndRotation(
            placedObject.Position.ToVector3(),
            WarmupObstaclePlacement.GetWorldRotation(placedObject.AssetId, placedObject.RotationEuler.ToVector3()));
        mirror.Root.SetActive(!placedObject.IsBroken);

        if (placedObject.IsBroken)
        {
            ApplyPieces(mirror, placedObject);
        }
        else
        {
            DestroyPieces(mirror);
        }
    }

    private static void ApplyPieces(MirrorObstacle mirror, WarmupObstacleProtocol.PlacedObject placedObject)
    {
        IncomingPieceIds.Clear();
        foreach (WarmupObstacleProtocol.PieceState piece in placedObject.Pieces)
        {
            IncomingPieceIds.Add(piece.Id);
        }

        StalePieceIds.Clear();
        foreach (int pieceId in mirror.Pieces.Keys)
        {
            if (!IncomingPieceIds.Contains(pieceId))
            {
                StalePieceIds.Add(pieceId);
            }
        }

        foreach (int stalePieceId in StalePieceIds)
        {
            DestroyPiece(mirror, stalePieceId);
        }

        foreach (WarmupObstacleProtocol.PieceState pieceState in placedObject.Pieces)
        {
            if (!mirror.Pieces.TryGetValue(pieceState.Id, out GameObject piece) || !piece)
            {
                piece = WarmupObstacleAssets.CreateTargetPiece($"WarmupTargetPieceMirror_{placedObject.Id}_{pieceState.Id}", serverPhysical: false);
                Object.DontDestroyOnLoad(piece);
                WarmupObstacleVisual visual = piece.GetComponent<WarmupObstacleVisual>() ?? piece.AddComponent<WarmupObstacleVisual>();
                visual.Initialize(placedObject.Id, placedObject.AssetId);
                Collider? collider = piece.GetComponent<Collider>();
                if (collider)
                {
                    collider.enabled = true;
                    collider.isTrigger = true;
                }

                mirror.Pieces[pieceState.Id] = piece;
            }

            piece.transform.SetPositionAndRotation(pieceState.Position.ToVector3(), Quaternion.Euler(pieceState.RotationEuler.ToVector3()));
            piece.transform.localScale = pieceState.Scale.ToVector3();
        }
    }

    private static void DestroyMirror(int objectId)
    {
        if (!Mirrors.TryGetValue(objectId, out MirrorObstacle mirror))
        {
            return;
        }

        DestroyPieces(mirror);
        if (mirror.Root)
        {
            Object.Destroy(mirror.Root);
        }

        Mirrors.Remove(objectId);
    }

    private static void DestroyPieces(MirrorObstacle mirror)
    {
        StalePieceIds.Clear();
        foreach (int pieceId in mirror.Pieces.Keys)
        {
            StalePieceIds.Add(pieceId);
        }

        foreach (int pieceId in StalePieceIds)
        {
            DestroyPiece(mirror, pieceId);
        }
    }

    private static void DestroyPiece(MirrorObstacle mirror, int pieceId)
    {
        if (!mirror.Pieces.TryGetValue(pieceId, out GameObject piece))
        {
            return;
        }

        if (piece)
        {
            Object.Destroy(piece);
        }

        mirror.Pieces.Remove(pieceId);
    }

    private static void SetHighlightedVisual(WarmupObstacleVisual? visual)
    {
        if (highlightedVisual == visual)
        {
            return;
        }

        if (highlightedVisual)
        {
            highlightedVisual.SetHighlighted(false);
        }

        highlightedVisual = visual;
        if (highlightedVisual)
        {
            highlightedVisual.SetHighlighted(true);
        }
    }

    private sealed class MirrorObstacle
    {
        public MirrorObstacle(GameObject root)
        {
            Root = root;
        }

        public GameObject Root { get; }
        public Dictionary<int, GameObject> Pieces { get; } = new();
    }
}
