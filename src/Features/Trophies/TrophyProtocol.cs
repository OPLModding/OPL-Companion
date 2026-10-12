using System;
using UnityEngine;

namespace OPLServerBanners;

internal static class TrophyProtocol
{
    public const string ClientToServerListener = "doma_trophies_client";
    public const string ServerToClientListener = "doma_trophies_server";
    public const string ActionRequestState = "request_state";

    [Serializable]
    internal sealed class Request
    {
        public string Action = string.Empty;
    }

    [Serializable]
    internal sealed class State
    {
        public bool IsSpawned;
        public string TrophyId = string.Empty;
        public Vec3Data Position = new Vec3Data();
    }

    [Serializable]
    internal sealed class Vec3Data
    {
        public float X;
        public float Y;
        public float Z;

        public Vec3Data()
        {
        }

        public Vec3Data(Vector3 value)
        {
            X = value.x;
            Y = value.y;
            Z = value.z;
        }

        public Vector3 ToVector3()
        {
            return new Vector3(X, Y, Z);
        }
    }
}
