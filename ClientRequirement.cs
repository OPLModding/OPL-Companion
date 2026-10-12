using System;
using HarmonyLib;

namespace OPLServerBanners;

public static class ClientRequirement
{
    internal const string WorkshopId = "3726105987";

    public static void Apply(Harmony harmony)
    {
        int patchedTargets = 0;

        if (TryPatchPropertyGetter(harmony, "ModManager", "ClientRequiredModIds"))
        {
            patchedTargets++;
        }

        if (TryPatchPropertyGetter(harmony, "ServerConfig", "ClientRequiredModIds"))
        {
            patchedTargets++;
        }

        if (patchedTargets == 0)
        {
            Plugin.LogError("ClientRequirement: no compatible required-mod hook was found; clients will not be auto-enabled.");
            return;
        }

        Plugin.Log($"ClientRequirement: broadcasting Workshop ID {WorkshopId} through {patchedTargets} compatible hook(s).");
    }

    private static bool TryPatchPropertyGetter(Harmony harmony, string typeName, string propertyName)
    {
        Type? type = AccessTools.TypeByName(typeName);
        if (type == null)
        {
            return false;
        }

        var getter = AccessTools.PropertyGetter(type, propertyName);
        if (getter == null)
        {
            return false;
        }

        harmony.Patch(getter, postfix: new HarmonyMethod(typeof(ClientRequirement), nameof(Postfix)));
        Plugin.Log($"ClientRequirement: patched {typeName}.{propertyName}.");
        return true;
    }

    private static void Postfix(ref string[] __result)
    {
        if (__result == null)
        {
            __result = new[] { WorkshopId };
            return;
        }

        for (int index = 0; index < __result.Length; index++)
        {
            if (string.Equals(__result[index], WorkshopId, StringComparison.Ordinal))
            {
                return;
            }
        }

        string[] requiredModIds = new string[__result.Length + 1];
        Array.Copy(__result, requiredModIds, __result.Length);
        requiredModIds[requiredModIds.Length - 1] = WorkshopId;
        __result = requiredModIds;
    }
}
