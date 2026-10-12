using System;

namespace OPLServerBanners;

internal static class WarmupObstacleProtocol
{
    public const string ClientToServerListener = "doma_warmup_obstacles_client";
    public const string ServerToClientListener = "doma_warmup_obstacles_server";

    public const string ActionRequestState = "request_state";
    public const string ActionPlace = "place";
    public const string ActionDelete = "delete";
    public const string ActionStartClearVote = "start_clear_vote";
    public const string ActionCastClearVote = "cast_clear_vote";

    public const int MaxPlacedObjects = 60;
    public const float ClearVoteSeconds = 30f;

    [Serializable]
    internal sealed class Vec3Data
    {
        public float X;
        public float Y;
        public float Z;

        public Vec3Data()
        {
        }

        public Vec3Data(UnityEngine.Vector3 value)
        {
            X = value.x;
            Y = value.y;
            Z = value.z;
        }

        public UnityEngine.Vector3 ToVector3()
        {
            return new UnityEngine.Vector3(X, Y, Z);
        }
    }

    [Serializable]
    internal sealed class AssetInfo
    {
        public string Id = string.Empty;
        public string Label = string.Empty;
        public string Source = string.Empty;
        public bool IsPasser;
        public bool IsBreakableTarget;
    }

    [Serializable]
    internal sealed class Request
    {
        public string Action = string.Empty;
        public string AssetId = string.Empty;
        public int ObjectId;
        public Vec3Data Position = new Vec3Data();
        public Vec3Data RotationEuler = new Vec3Data();
        public bool VoteYes;
    }

    [Serializable]
    internal sealed class State
    {
        public bool IsWarmup;
        public bool CanEdit;
        public int MaxObjects = MaxPlacedObjects;
        public int ObjectCount;
        public string StatusKind = string.Empty;
        public string StatusMessage = string.Empty;
        public AssetInfo[] Assets = Array.Empty<AssetInfo>();
        public PlacedObject[] Objects = Array.Empty<PlacedObject>();
        public ClearVoteState? ClearVote;
    }

    [Serializable]
    internal sealed class PlacedObject
    {
        public int Id;
        public string AssetId = string.Empty;
        public Vec3Data Position = new Vec3Data();
        public Vec3Data RotationEuler = new Vec3Data();
        public bool IsBroken;
        public PieceState[] Pieces = Array.Empty<PieceState>();
    }

    [Serializable]
    internal sealed class PieceState
    {
        public int Id;
        public Vec3Data Position = new Vec3Data();
        public Vec3Data RotationEuler = new Vec3Data();
        public Vec3Data Scale = new Vec3Data(UnityEngine.Vector3.one);
    }

    [Serializable]
    internal sealed class ClearVoteState
    {
        public bool IsActive;
        public string Initiator = string.Empty;
        public int YesCount;
        public int NoCount;
        public int RequiredVotes;
        public int EligibleSkaters;
        public float SecondsRemaining;
        public bool LocalPlayerHasVoted;
        public string Result = string.Empty;
    }
}
