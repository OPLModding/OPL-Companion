using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OPLServerBanners;

internal enum WarmupObstacleBundleKey
{
    PreviousTraining,
    MultiSheetTraining,
    PuckPrac,
    S4Trophies
}

internal sealed class WarmupObstacleDefinition
{
    public WarmupObstacleDefinition(
        string id,
        string label,
        WarmupObstacleBundleKey bundleKey,
        string assetPath,
        string? childName = null,
        bool isPasser = false,
        bool isBreakableTarget = false,
        bool isTrophy = false,
        Vector3? baseEulerOffset = null)
    {
        Id = id;
        Label = label;
        BundleKey = bundleKey;
        AssetPath = assetPath;
        ChildName = childName;
        IsPasser = isPasser;
        IsBreakableTarget = isBreakableTarget;
        IsTrophy = isTrophy;
        BaseEulerOffset = baseEulerOffset ?? Vector3.zero;
    }

    public string Id { get; }
    public string Label { get; }
    public WarmupObstacleBundleKey BundleKey { get; }
    public string AssetPath { get; }
    public string? ChildName { get; }
    public bool IsPasser { get; }
    public bool IsBreakableTarget { get; }
    public bool IsTrophy { get; }
    public Vector3 BaseEulerOffset { get; }

    public string SourceLabel => BundleKey switch
    {
        WarmupObstacleBundleKey.PreviousTraining => "3764816187",
        WarmupObstacleBundleKey.MultiSheetTraining => "3771130437",
        WarmupObstacleBundleKey.PuckPrac => "3673692112",
        WarmupObstacleBundleKey.S4Trophies => "S4 trophies",
        _ => string.Empty
    };
}

internal static class WarmupObstacleCatalog
{
    public const string PasserId = "passer";
    public const string TargetId = "target";
    public const string WbwCupId = "wbw_cup";
    public const string BiCupId = "bi_cup";

    private const string TrainingRootAssetPath = "assets/trainingprefab.prefab";

    private static readonly WarmupObstacleDefinition[] AssetDefinitions =
    {
        new(PasserId, "Passer", WarmupObstacleBundleKey.PuckPrac, "assets/passer.fbx", isPasser: true, baseEulerOffset: new Vector3(-90f, 0f, 0f)),
        new(TargetId, "Breakable Target", WarmupObstacleBundleKey.PuckPrac, "assets/target.fbx", isBreakableTarget: true, baseEulerOffset: new Vector3(-90f, 0f, 0f)),
        new(WbwCupId, "WBW Cup", WarmupObstacleBundleKey.S4Trophies, "Assets/Trophys/Prefabs/S4 WBW CUP.prefab", isTrophy: true),
        new(BiCupId, "BI Cup", WarmupObstacleBundleKey.S4Trophies, "Assets/Trophys/Prefabs/S4 BI CUP.prefab", isTrophy: true),

        new("cone", "Cone", WarmupObstacleBundleKey.MultiSheetTraining, TrainingRootAssetPath, "Cone"),
        new("center_push_beam", "Center Push Beam", WarmupObstacleBundleKey.MultiSheetTraining, TrainingRootAssetPath, "Untitl234ed"),
        new("shooter_side_rail", "Shooter Side Rail", WarmupObstacleBundleKey.MultiSheetTraining, TrainingRootAssetPath, "Untitled 1"),
        new("far_end_rail", "Far End Rail", WarmupObstacleBundleKey.MultiSheetTraining, TrainingRootAssetPath, "Untitled 1 (1)"),

        new("pipe", "Pipe", WarmupObstacleBundleKey.PreviousTraining, TrainingRootAssetPath, "pipe"),
        new("pipe_1", "Pipe Variant 1", WarmupObstacleBundleKey.PreviousTraining, TrainingRootAssetPath, "pipe (1)"),
        new("pipe_2", "Pipe Variant 2", WarmupObstacleBundleKey.PreviousTraining, TrainingRootAssetPath, "pipe (2)"),
        new("pipe_3", "Pipe Variant 3", WarmupObstacleBundleKey.PreviousTraining, TrainingRootAssetPath, "pipe (3)"),
        new("pipe_5", "Pipe Variant 5", WarmupObstacleBundleKey.PreviousTraining, TrainingRootAssetPath, "pipe (5)"),
        new("sticks_after_another", "Sticks After Another", WarmupObstacleBundleKey.PreviousTraining, TrainingRootAssetPath, "sticksafteranother"),
        new("puck_under_tool", "Puck Under Tool", WarmupObstacleBundleKey.PreviousTraining, TrainingRootAssetPath, "puckundertool"),
        new("puck_under_tool_1", "Puck Under Tool Variant 1", WarmupObstacleBundleKey.PreviousTraining, TrainingRootAssetPath, "puckundertool (1)"),
        new("puck_under_tool_2", "Puck Under Tool Variant 2", WarmupObstacleBundleKey.PreviousTraining, TrainingRootAssetPath, "puckundertool (2)"),
        new("puck_under_tool_3", "Puck Under Tool Variant 3", WarmupObstacleBundleKey.PreviousTraining, TrainingRootAssetPath, "puckundertool (3)"),
        new("puck_under_tool_4", "Puck Under Tool Variant 4", WarmupObstacleBundleKey.PreviousTraining, TrainingRootAssetPath, "puckundertool (4)"),
        new("saucer_trainer_tool", "Saucer Trainer Tool", WarmupObstacleBundleKey.PreviousTraining, TrainingRootAssetPath, "SaucerTrainerTool"),
        new("one_sticks_under_tool_v2", "One Stick Under Tool", WarmupObstacleBundleKey.PreviousTraining, TrainingRootAssetPath, "OneSticksUnderTool_v2"),
        new("one_sticks_under_tool_v2_1", "One Stick Under Tool Variant 1", WarmupObstacleBundleKey.PreviousTraining, TrainingRootAssetPath, "OneSticksUnderTool_v2 (1)"),
        new("one_sticks_under_tool_v2_2", "One Stick Under Tool Variant 2", WarmupObstacleBundleKey.PreviousTraining, TrainingRootAssetPath, "OneSticksUnderTool_v2 (2)"),
        new("one_sticks_under_tool_v2_4", "One Stick Under Tool Variant 4", WarmupObstacleBundleKey.PreviousTraining, TrainingRootAssetPath, "OneSticksUnderTool_v2 (4)")
    };

    private static readonly Dictionary<string, WarmupObstacleDefinition> DefinitionsById =
        AssetDefinitions.ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);

    private static readonly WarmupObstacleProtocol.AssetInfo[] AssetInfos =
        AssetDefinitions
            .Select(definition => new WarmupObstacleProtocol.AssetInfo
            {
                Id = definition.Id,
                Label = definition.Label,
                Source = definition.SourceLabel,
                IsPasser = definition.IsPasser,
                IsBreakableTarget = definition.IsBreakableTarget
            })
            .ToArray();

    public static IReadOnlyList<WarmupObstacleDefinition> Definitions => AssetDefinitions;

    public static bool TryGet(string assetId, out WarmupObstacleDefinition definition)
    {
        return DefinitionsById.TryGetValue(assetId ?? string.Empty, out definition!);
    }

    public static WarmupObstacleProtocol.AssetInfo[] ToAssetInfos()
    {
        return AssetInfos;
    }
}
