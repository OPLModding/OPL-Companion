using HarmonyLib;

namespace OPLServerBanners;

internal static class SettingsPresetPatches
{
    public static void ApplyPatches(Harmony harmony)
    {
        harmony.CreateClassProcessor(typeof(UISettingsInitializePatch)).Patch();
    }

    [HarmonyPatch(typeof(UISettings), "Initialize")]
    private static class UISettingsInitializePatch
    {
        [HarmonyPostfix]
        private static void Postfix(UISettings __instance)
        {
            SettingsPresetTabUI.Inject(__instance);
        }
    }
}
