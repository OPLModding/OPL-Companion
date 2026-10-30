using System;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using OPLServerBanners.OPLArena;

namespace OPLServerBanners;

public sealed class Plugin : IPuckPlugin
{
    public const string ModName = "OPLServerBanners";
    public const string ModVersion = "1.2.0";
    public const string ModGuid = "doma.oplserverbanners";

    private static readonly Harmony Harmony = new(ModGuid);
    private static readonly string ActiveAssemblyPathKey = $"{ModGuid}.activeAssemblyPath";

    private static bool isActiveInstance;
    private static bool isDuplicateNoop;
    private static string currentAssemblyPath = string.Empty;

    public bool OnEnable()
    {
        try
        {
            Log($"Enabling {ModVersion}...");
            currentAssemblyPath = Assembly.GetExecutingAssembly().Location ?? string.Empty;
            if (IsAnotherCopyActive())
            {
                isDuplicateNoop = true;
                isActiveInstance = false;
                Log($"Another copy is already active at '{GetActiveAssemblyPath()}'; skipping enable for '{currentAssemblyPath}'.");
                return true;
            }

            ServerConfigManager.Load();
            if (IsDedicatedServer())
            {
                Log("Dedicated server detected; applying server-side patches.");
                if (ServerConfigManager.RequireWorkshopModForClients)
                {
                    ClientRequirement.Apply(Harmony);
                }
                else
                {
                    Log("Workshop required-mod advertisement is disabled by serverconfig.json.");
                }

                MessageBoardServer.Initialize();
                CompetitiveSkatingConfigServer.Initialize();
#if TROPHY_COMMANDS
                TrophyServer.Initialize();
#endif
                ApplyServerPatches();
                LogPatchedMethods();
                MarkThisCopyActive();
                return true;
            }

            if (ToasterReskinLoaderCompatibility.IsInstalled())
            {
                Log("Detected ToasterReskinLoader; deferring server browser refresh behavior to it while keeping OPL banner styling enabled.");
            }

            PersistClientAutoEnableIfNeeded();
            CompetitiveSkatingConfigServer.Initialize();
#if TROPHY_COMMANDS
            TrophyServer.Initialize();
#endif
            CompetitiveSkatingConfigClient.Initialize();
            SettingsPresetClient.Initialize();
            ZoneOverlayClient.Initialize();
            GoalieWarmupPuckTracking.Initialize();
            GoaliePracticeMenuClient.Initialize();
            MessageBoardClient.Initialize();
            OPLArenaIntegration.Initialize();
#if TROPHY_COMMANDS
            TrophyClient.Initialize();
#endif
            ApplyClientPatches();
            LogPatchedMethods();
            MarkThisCopyActive();
            Log("Enabled.");
            return true;
        }
        catch (Exception exception)
        {
            ClearActiveCopyIfOwned();
            LogError($"Failed to enable: {exception}");
            return false;
        }
    }

    public bool OnDisable()
    {
        try
        {
            if (isDuplicateNoop)
            {
                isDuplicateNoop = false;
                currentAssemblyPath = string.Empty;
                return true;
            }

            Log("Disabling...");
            GoalieWarmupPuckTracking.Shutdown();
#if TROPHY_COMMANDS
            TrophyClient.Shutdown();
#endif
            ZoneOverlayClient.Shutdown();
            SettingsPresetClient.Shutdown();
            CompetitiveSkatingConfigClient.Shutdown();
#if TROPHY_COMMANDS
            TrophyServer.Shutdown();
#endif
            CompetitiveSkatingConfigServer.Shutdown();
            GoaliePracticeMenuClient.Shutdown();
            MessageBoardClient.Shutdown();
            OPLArenaIntegration.Shutdown();
            MessageBoardServer.Shutdown();
            Harmony.UnpatchSelf();
            ClearActiveCopyIfOwned();

            Log("Disabled.");
            return true;
        }
        catch (Exception exception)
        {
            LogError($"Failed to disable: {exception}");
            return false;
        }
    }

    public static bool IsDedicatedServer()
    {
        return SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
    }

    public static void Log(string message)
    {
        Debug.Log($"[{ModName}] {message}");
    }

    public static void LogError(string message)
    {
        Debug.LogError($"[{ModName}] {message}");
    }

    private static void PersistClientAutoEnableIfNeeded()
    {
        try
        {
            global::ReconnectionState state = global::GlobalStateManager.ReconnectionState;
            string[]? requiredModIds = state.ClientRequiredModIds;
            string[]? pendingEnablingModIds = state.PendingEnablingModIds;

            if (state.Phase != global::ReconnectionPhase.AwaitingMods
                || requiredModIds == null
                || pendingEnablingModIds == null
                || !requiredModIds.Contains(ClientRequirement.WorkshopId)
                || !pendingEnablingModIds.Contains(ClientRequirement.WorkshopId))
            {
                return;
            }

            global::ModManager.SetModStatus(ClientRequirement.WorkshopId, true);
            Log($"Persisted auto-enabled required mod {ClientRequirement.WorkshopId} so it stays enabled after leaving the server.");
        }
        catch (Exception exception)
        {
            LogError($"Failed to persist auto-enabled required mod state: {exception}");
        }
    }

    private static void ApplyServerPatches()
    {
        GoaliePracticeMenuHotkeys.ApplyServerPatches(Harmony);
        GoalieSpeedCapPatches.ApplyPatches(Harmony);
#if TROPHY_COMMANDS
        TrophyServer.ApplyPatches(Harmony);
#endif
    }

    private static void ApplyClientPatches()
    {
        GoaliePracticeMenuHotkeys.ApplyClientPatches(Harmony);
        GoalieSpeedCapPatches.ApplyPatches(Harmony);
        SettingsPresetPatches.ApplyPatches(Harmony);
        GoalieWarmupPuckTracking.ApplyPatches(Harmony);
        LoadOrder.ApplyPatches(Harmony);
        ServerBrowserBannerStyling.ApplyPatches(Harmony);
#if TROPHY_COMMANDS
        TrophyServer.ApplyPatches(Harmony);
#endif
    }

    private static bool IsAnotherCopyActive()
    {
        string? activeAssemblyPath = GetActiveAssemblyPath();
        return !string.IsNullOrWhiteSpace(activeAssemblyPath)
               && !string.Equals(activeAssemblyPath, currentAssemblyPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetActiveAssemblyPath()
    {
        return AppDomain.CurrentDomain.GetData(ActiveAssemblyPathKey) as string;
    }

    private static void MarkThisCopyActive()
    {
        AppDomain.CurrentDomain.SetData(ActiveAssemblyPathKey, currentAssemblyPath);
        isActiveInstance = true;
        isDuplicateNoop = false;
    }

    private static void ClearActiveCopyIfOwned()
    {
        if (!isActiveInstance)
        {
            return;
        }

        string? activeAssemblyPath = GetActiveAssemblyPath();
        if (string.Equals(activeAssemblyPath, currentAssemblyPath, StringComparison.OrdinalIgnoreCase))
        {
            AppDomain.CurrentDomain.SetData(ActiveAssemblyPathKey, null);
        }

        isActiveInstance = false;
        currentAssemblyPath = string.Empty;
    }

    private static void LogPatchedMethods()
    {
        var methods = Harmony.GetPatchedMethods()
            .Select(method => new
            {
                Method = method,
                PatchInfo = Harmony.GetPatchInfo(method)
            })
            .Where(entry =>
                entry.PatchInfo != null &&
                (entry.PatchInfo.Prefixes.Any(patch => patch.owner == Harmony.Id) ||
                 entry.PatchInfo.Postfixes.Any(patch => patch.owner == Harmony.Id) ||
                 entry.PatchInfo.Transpilers.Any(patch => patch.owner == Harmony.Id) ||
                 entry.PatchInfo.Finalizers.Any(patch => patch.owner == Harmony.Id)))
            .Select(entry => entry.Method);

        foreach (var method in methods)
        {
            Log($"Patched {method.DeclaringType?.FullName}.{method.Name}");
        }
    }
}
