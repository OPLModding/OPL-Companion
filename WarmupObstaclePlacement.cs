using UnityEngine;

namespace OPLServerBanners;

internal static class WarmupObstaclePlacement
{
    private const float SnapAngle = 15f;

    public static Vector3 SanitizeUserEuler(Vector3 euler)
    {
        return new Vector3(
            SnapTo(euler.x, SnapAngle),
            NormalizeAngle(euler.y),
            SnapTo(euler.z, SnapAngle));
    }

    public static Quaternion GetWorldRotation(string assetId, Vector3 userEuler)
    {
        Vector3 sanitizedEuler = SanitizeUserEuler(userEuler);
        Vector3 baseEuler = WarmupObstacleCatalog.TryGet(assetId, out WarmupObstacleDefinition definition)
            ? definition.BaseEulerOffset
            : Vector3.zero;
        return Quaternion.Euler(baseEuler + sanitizedEuler);
    }

    public static float NormalizeAngle(float angle)
    {
        while (angle < 0f)
        {
            angle += 360f;
        }

        while (angle >= 360f)
        {
            angle -= 360f;
        }

        return angle;
    }

    private static float SnapTo(float value, float step)
    {
        return step <= 0f
            ? value
            : Mathf.Round(value / step) * step;
    }
}
