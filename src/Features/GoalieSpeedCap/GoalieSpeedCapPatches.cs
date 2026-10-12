using HarmonyLib;
using UnityEngine;

namespace OPLServerBanners;

internal static class GoalieSpeedCapPatches
{
    public static void ApplyPatches(Harmony harmony)
    {
        harmony.CreateClassProcessor(typeof(MovementFixedUpdatePatch)).Patch();
        harmony.CreateClassProcessor(typeof(GoalieDashLeftPatch)).Patch();
        harmony.CreateClassProcessor(typeof(GoalieDashRightPatch)).Patch();
    }

    [HarmonyPatch(typeof(Movement), "FixedUpdate")]
    private static class MovementFixedUpdatePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Movement __instance)
        {
            if (!GoalieSpeedCapRuntime.Applies(__instance.PlayerBody))
            {
                return;
            }

            __instance.Rigidbody.linearVelocity =
                GoalieSpeedCapRuntime.ClampHorizontal(__instance.Rigidbody.linearVelocity);
        }
    }

    [HarmonyPatch(typeof(PlayerBody), "DashLeft")]
    private static class GoalieDashLeftPatch
    {
        [HarmonyPrefix]
        private static void Prefix(PlayerBody __instance, out float __state)
        {
            __state = __instance.Stamina.Value;
        }

        [HarmonyPostfix]
        private static void Postfix(PlayerBody __instance, float __state)
        {
            GoalieSpeedCapRuntime.CompensateDash(
                __instance,
                __state,
                -__instance.transform.right);
        }
    }

    [HarmonyPatch(typeof(PlayerBody), "DashRight")]
    private static class GoalieDashRightPatch
    {
        [HarmonyPrefix]
        private static void Prefix(PlayerBody __instance, out float __state)
        {
            __state = __instance.Stamina.Value;
        }

        [HarmonyPostfix]
        private static void Postfix(PlayerBody __instance, float __state)
        {
            GoalieSpeedCapRuntime.CompensateDash(
                __instance,
                __state,
                __instance.transform.right);
        }
    }
}
