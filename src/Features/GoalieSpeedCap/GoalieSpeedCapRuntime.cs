using System.Text.Json.Serialization;
using UnityEngine;

namespace OPLServerBanners;

public sealed class GoalieSpeedCapSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("maxSpeed")]
    public float MaxSpeed { get; set; } = 6f;
}

internal static class GoalieSpeedCapRuntime
{
    private static readonly HarmonyLib.AccessTools.FieldRef<PlayerBody, float> DashVelocity =
        HarmonyLib.AccessTools.FieldRefAccess<PlayerBody, float>("dashVelocity");

    public static bool Applies(PlayerBody? body)
    {
        if (!ServerConfigManager.GoalieSpeedCap.Enabled || body == null)
        {
            return false;
        }

        Rigidbody rigidbody = body.Rigidbody;
        if (rigidbody == null || rigidbody.isKinematic)
        {
            return false;
        }

        Player? player = body.Player;
        return player != null && player.Role == PlayerRole.Goalie;
    }

    public static Vector3 ClampHorizontal(Vector3 velocity)
    {
        float maxSpeed = Mathf.Max(0f, ServerConfigManager.GoalieSpeedCap.MaxSpeed);
        Vector3 horizontal = new(velocity.x, 0f, velocity.z);
        if (horizontal.sqrMagnitude <= maxSpeed * maxSpeed)
        {
            return velocity;
        }

        horizontal = horizontal.normalized * maxSpeed;
        return new Vector3(horizontal.x, velocity.y, horizontal.z);
    }

    public static void CompensateDash(PlayerBody body, float staminaBefore, Vector3 direction)
    {
        if (body.Stamina.Value >= staminaBefore || !Applies(body))
        {
            return;
        }

        Vector3 dashVelocity = direction * DashVelocity.Invoke(body);
        Vector3 velocityBeforeDash = body.Rigidbody.linearVelocity + dashVelocity;
        body.Rigidbody.linearVelocity = ClampHorizontal(velocityBeforeDash) - dashVelocity;
    }
}
