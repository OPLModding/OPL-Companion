using HarmonyLib;
using Unity.Netcode;

namespace OPLServerBanners;

internal static class GoaliePracticeMenuHotkeys
{
    public static void ApplyClientPatches(Harmony harmony)
    {
        harmony.CreateClassProcessor(typeof(BlockPauseWhileGoaliePracticeMenuOpen)).Patch();
        harmony.CreateClassProcessor(typeof(PlayerInputUpdatePatch)).Patch();
        harmony.CreateClassProcessor(typeof(UIManagerUpdatePatch)).Patch();
        harmony.CreateClassProcessor(typeof(NetworkManagerUpdatePatch)).Patch();
    }

    public static void ApplyServerPatches(Harmony harmony)
    {
        harmony.CreateClassProcessor(typeof(NetworkManagerUpdatePatch)).Patch();
    }

    [HarmonyPatch(typeof(UIManager), "OnPauseActionPerformed")]
    private static class BlockPauseWhileGoaliePracticeMenuOpen
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            if (GoaliePracticeMenuUI.ShouldBlockPauseAction()
                || MessageBoardUI.ShouldBlockPauseAction()
                || CompetitiveSkatingConfigUI.ShouldBlockPauseAction())
            {
                return false;
            }

#if WARMUP_OBSTACLES
            if (WarmupObstacleEditorUI.ShouldBlockPauseAction())
            {
                return false;
            }
#endif

            return true;
        }
    }

    [HarmonyPatch(typeof(global::PlayerInput), "Update")]
    private static class PlayerInputUpdatePatch
    {
        [HarmonyPostfix]
        private static void Postfix(global::PlayerInput __instance)
        {
            if (!IsLocalPlayerInput(__instance))
            {
                return;
            }

            SettingsPresetClient.Tick(__instance);
            CompetitiveSkatingConfigClient.Tick(__instance);
            GoaliePracticeMenuClient.Tick(__instance);
            MessageBoardClient.Tick(__instance);
#if WARMUP_OBSTACLES
            WarmupObstacleClient.Tick(__instance);
#endif
        }
    }

    [HarmonyPatch(typeof(UIManager), "Update")]
    private static class UIManagerUpdatePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            SettingsPresetClient.TickUi();
            GoalieWarmupPuckTracking.TickUi();

            if (CompetitiveSkatingConfigClient.NeedsUiTick)
            {
                CompetitiveSkatingConfigClient.TickUi();
            }

            GoaliePracticeMenuClient.TickUi();
            MessageBoardClient.TickUi();
#if TROPHY_COMMANDS
            TrophyClient.Tick();
#endif
        }
    }

    [HarmonyPatch(typeof(NetworkManager), "NetworkUpdate")]
    private static class NetworkManagerUpdatePatch
    {
        [HarmonyPostfix]
        private static void Postfix(NetworkUpdateStage updateStage)
        {
            if (updateStage == NetworkUpdateStage.EarlyUpdate && NetworkManager.Singleton?.IsServer == true)
            {
                MessageBoardServer.Tick();
                CompetitiveSkatingConfigServer.Tick();
#if TROPHY_COMMANDS
                TrophyServer.Tick();
#endif
            }
        }
    }

    private static bool IsLocalPlayerInput(global::PlayerInput playerInput)
    {
        NetworkManager? networkManager = NetworkManager.Singleton;
        return networkManager != null
               && ((NetworkBehaviour)playerInput).OwnerClientId == networkManager.LocalClientId;
    }
}
