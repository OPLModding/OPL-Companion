using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace OPLServerBanners;

internal static class CompetitiveSkatingConfigRuntime
{
    private const string CompetitiveSkatingTypeName = "PuckCompetitiveSkating.SkatingPlugin";

    private static readonly BindingFlags PublicStaticFlags = BindingFlags.Public | BindingFlags.Static;
    private static readonly BindingFlags PublicInstanceFlags = BindingFlags.Public | BindingFlags.Instance;
    private static readonly BindingFlags InstanceFieldFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly FieldInfo[] SettingsFields = typeof(CompetitiveSkatingSettingsData).GetFields(PublicInstanceFlags);
    private static readonly Dictionary<string, FieldInfo> SettingsFieldMap = BuildSettingsFieldMap();
    private static readonly List<CompetitiveSkatingSettingDefinition> LegacySettingDefinitions = BuildLegacyDefinitions();
    private static readonly List<CompetitiveSkatingSettingDefinition> V1041SettingDefinitions = BuildV1041Definitions();
    private static readonly Dictionary<string, FieldInfo> PlayerBodyTackleFields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);

    private static Type? skatingPluginType;
    private static Dictionary<string, FieldInfo>? boundRuntimeFields;
    private static IReadOnlyList<CompetitiveSkatingSettingDefinition>? activeSettingDefinitions;
    private static Dictionary<string, CompetitiveSkatingSettingDefinition>? activeDefinitionMap;
    private static CompetitiveSkatingCompatibilityProfile activeProfile;
    private static string lastBindMessage = "CompetitiveSkating.dll has not been loaded on this server.";

    public static IReadOnlyList<CompetitiveSkatingSettingDefinition> Definitions => activeSettingDefinitions ?? V1041SettingDefinitions;
    public static string CompatibilityProfileName => activeProfile.ToString();
    public static int CurrentConfigVersion => activeProfile == CompetitiveSkatingCompatibilityProfile.V1041 ? 2 : 1;

    public static void Reset()
    {
        skatingPluginType = null;
        boundRuntimeFields = null;
        activeSettingDefinitions = null;
        activeDefinitionMap = null;
        activeProfile = CompetitiveSkatingCompatibilityProfile.None;
        PlayerBodyTackleFields.Clear();
        lastBindMessage = "CompetitiveSkating.dll has not been loaded on this server.";
    }

    public static bool SetCompatibilityProfile(string? profileName)
    {
        CompetitiveSkatingCompatibilityProfile profile = string.Equals(
            profileName,
            CompetitiveSkatingCompatibilityProfile.Legacy.ToString(),
            StringComparison.Ordinal)
            ? CompetitiveSkatingCompatibilityProfile.Legacy
            : CompetitiveSkatingCompatibilityProfile.V1041;

        if (activeProfile == profile && activeSettingDefinitions != null)
        {
            return false;
        }

        SetActiveProfile(profile);
        return true;
    }

    public static bool TryEnsureBound(out string message)
    {
        return TryEnsureBound(out message, out _);
    }

    public static bool TryEnsureBound(out string message, out bool isTerminalFailure)
    {
        if (skatingPluginType != null && boundRuntimeFields != null)
        {
            message = string.Empty;
            isTerminalFailure = false;
            return true;
        }

        Type? pluginType = AccessTools.TypeByName(CompetitiveSkatingTypeName);
        if (pluginType == null)
        {
            boundRuntimeFields = null;
            skatingPluginType = null;
            message = lastBindMessage = "CompetitiveSkating.dll has not loaded yet; OPL will keep probing.";
            // Workshop mods load asynchronously. A DLL that has not been loaded into the
            // current AppDomain is a transient condition, not an incompatible API.
            isTerminalFailure = false;
            return false;
        }

        if (!TryBindProfile(pluginType, V1041SettingDefinitions, CompetitiveSkatingCompatibilityProfile.V1041, out Dictionary<string, FieldInfo>? runtimeFields)
            && !TryBindProfile(pluginType, LegacySettingDefinitions, CompetitiveSkatingCompatibilityProfile.Legacy, out runtimeFields))
        {
            boundRuntimeFields = null;
            skatingPluginType = null;
            message = lastBindMessage = "CompetitiveSkating does not expose a supported settings field set.";
            isTerminalFailure = true;
            return false;
        }

        skatingPluginType = pluginType;
        boundRuntimeFields = runtimeFields;
        message = string.Empty;
        lastBindMessage = string.Empty;
        isTerminalFailure = false;
        return true;
    }

    public static string GetAvailabilityMessage()
    {
        return lastBindMessage;
    }

    public static CompetitiveSkatingSettingsData CaptureCurrentSettings()
    {
        if (!TryEnsureBound(out string message) || boundRuntimeFields == null)
        {
            throw new InvalidOperationException(message);
        }

        CompetitiveSkatingSettingsData settings = new CompetitiveSkatingSettingsData();
        foreach (CompetitiveSkatingSettingDefinition definition in Definitions)
        {
            float value = (float)boundRuntimeFields[definition.FieldName].GetValue(null);
            SettingsFieldMap[definition.FieldName].SetValue(settings, value);
        }

        return settings;
    }

    public static void ApplySettings(CompetitiveSkatingSettingsData settings)
    {
        if (!TryEnsureBound(out string message) || boundRuntimeFields == null)
        {
            throw new InvalidOperationException(message);
        }

        foreach (CompetitiveSkatingSettingDefinition definition in Definitions)
        {
            float value = GetValue(settings, definition.Id);
            boundRuntimeFields[definition.FieldName].SetValue(null, value);
        }

        ApplyTackleSettingsToActivePlayers();
    }

    public static CompetitiveSkatingSettingsData Clone(CompetitiveSkatingSettingsData source)
    {
        CompetitiveSkatingSettingsData clone = new CompetitiveSkatingSettingsData();
        foreach (FieldInfo field in SettingsFields)
        {
            field.SetValue(clone, field.GetValue(source));
        }

        return clone;
    }

    public static CompetitiveSkatingSettingsData CreateBuiltInDefaults()
    {
        return new CompetitiveSkatingSettingsData();
    }

    public static CompetitiveSkatingSettingsData ClampAll(CompetitiveSkatingSettingsData source)
    {
        CompetitiveSkatingSettingsData clone = Clone(source);
        foreach (CompetitiveSkatingSettingDefinition definition in Definitions)
        {
            float clamped = ClampStep(GetValue(clone, definition.Id), definition.MinValue, definition.MaxValue, definition.Step);
            SettingsFieldMap[definition.FieldName].SetValue(clone, clamped);
        }

        if (activeProfile == CompetitiveSkatingCompatibilityProfile.V1041
            && clone.CrossoverFalloffWindow < clone.CrossoverPerfectWindow)
        {
            clone.CrossoverFalloffWindow = clone.CrossoverPerfectWindow;
        }

        return clone;
    }

    public static bool AreEquivalent(CompetitiveSkatingSettingsData left, CompetitiveSkatingSettingsData right)
    {
        foreach (CompetitiveSkatingSettingDefinition definition in Definitions)
        {
            float leftValue = GetValue(left, definition.Id);
            float rightValue = GetValue(right, definition.Id);
            if (Mathf.Abs(leftValue - rightValue) > 0.0001f)
            {
                return false;
            }
        }

        return true;
    }

    public static bool AreAllZero(CompetitiveSkatingSettingsData settings)
    {
        foreach (CompetitiveSkatingSettingDefinition definition in Definitions)
        {
            if (Mathf.Abs(GetValue(settings, definition.Id)) > 0.0001f)
            {
                return false;
            }
        }

        return true;
    }

    public static float GetValue(CompetitiveSkatingSettingsData settings, string settingId)
    {
        if (!SettingsFieldMap.TryGetValue(settingId, out FieldInfo? field))
        {
            throw new KeyNotFoundException($"Unknown skating setting '{settingId}'.");
        }

        return (float)field.GetValue(settings);
    }

    public static bool TrySetValue(CompetitiveSkatingSettingsData settings, string settingId, float value, out float clampedValue)
    {
        clampedValue = 0f;
        CompetitiveSkatingSettingDefinition? definition = FindDefinition(settingId);
        if (definition == null || !SettingsFieldMap.TryGetValue(settingId, out FieldInfo? field))
        {
            return false;
        }

        clampedValue = ClampStep(value, definition.MinValue, definition.MaxValue, definition.Step);
        field.SetValue(settings, clampedValue);
        return true;
    }

    public static CompetitiveSkatingSettingDefinition? FindDefinition(string settingId)
    {
        return activeDefinitionMap != null && activeDefinitionMap.TryGetValue(settingId, out CompetitiveSkatingSettingDefinition? definition)
            ? definition
            : null;
    }

    public static string FormatValue(float value, float step)
    {
        int decimals = step >= 1f
            ? 0
            : step >= 0.1f
                ? 1
                : step >= 0.01f
                    ? 2
                    : 3;

        return value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    public static float ClampStep(float value, float minValue, float maxValue, float step)
    {
        float clamped = Mathf.Clamp(value, minValue, maxValue);
        if (step > 0f)
        {
            clamped = Mathf.Round(clamped / step) * step;
        }

        return clamped;
    }

    private static Dictionary<string, FieldInfo> BuildSettingsFieldMap()
    {
        Dictionary<string, FieldInfo> fieldMap = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
        foreach (FieldInfo field in SettingsFields)
        {
            fieldMap[field.Name] = field;
        }

        return fieldMap;
    }

    private static bool TryBindProfile(
        Type pluginType,
        IReadOnlyList<CompetitiveSkatingSettingDefinition> definitions,
        CompetitiveSkatingCompatibilityProfile profile,
        out Dictionary<string, FieldInfo>? runtimeFields)
    {
        runtimeFields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);

        foreach (CompetitiveSkatingSettingDefinition definition in definitions)
        {
            FieldInfo? field = pluginType.GetField(definition.FieldName, PublicStaticFlags);
            if (field == null || field.FieldType != typeof(float))
            {
                runtimeFields = null;
                return false;
            }

            runtimeFields[definition.FieldName] = field;
        }

        SetActiveProfile(profile);
        return true;
    }

    private static void SetActiveProfile(CompetitiveSkatingCompatibilityProfile profile)
    {
        activeProfile = profile;
        activeSettingDefinitions = profile == CompetitiveSkatingCompatibilityProfile.Legacy
            ? LegacySettingDefinitions
            : V1041SettingDefinitions;
        activeDefinitionMap = new Dictionary<string, CompetitiveSkatingSettingDefinition>(StringComparer.Ordinal);
        foreach (CompetitiveSkatingSettingDefinition definition in activeSettingDefinitions)
        {
            activeDefinitionMap[definition.Id] = definition;
        }
    }

    private static List<CompetitiveSkatingSettingDefinition> BuildLegacyDefinitions()
    {
        return new List<CompetitiveSkatingSettingDefinition>
        {
            new CompetitiveSkatingSettingDefinition(
                "Stamina",
                nameof(CompetitiveSkatingSettingsData.BaseStaminaDrain),
                "Base Stamina Drain",
                "Sets the baseline stamina burn applied while the custom skating mechanics are active.",
                0f,
                8f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Stamina",
                nameof(CompetitiveSkatingSettingsData.TransitionTaxMultiplier),
                "Transition Tax",
                "Scales the extra stamina cost for quick edge swaps, lateral transitions, and similar direction changes.",
                0f,
                4f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Stamina",
                nameof(CompetitiveSkatingSettingsData.PowerStanceStaminaDrain),
                "Carve Stamina Drain",
                "Adds stamina drain while the carve or power-stance movement is being held.",
                0f,
                8f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Starts And Braking",
                nameof(CompetitiveSkatingSettingsData.ExplosiveStartMultiplier),
                "Explosive Start",
                "Controls the burst applied when a skater accelerates sharply out of a near-stop.",
                0f,
                5f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Starts And Braking",
                nameof(CompetitiveSkatingSettingsData.DirectionalBrakeMultiplier),
                "Directional Brake",
                "Scales the braking force when the skater steers against their current movement.",
                0f,
                5f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Starts And Braking",
                nameof(CompetitiveSkatingSettingsData.EdgeBrakeFriction),
                "Edge Brake Friction",
                "Changes how aggressively the skates bite and shed speed while cutting on an edge.",
                0f,
                100f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Starts And Braking",
                nameof(CompetitiveSkatingSettingsData.SprintCoastBleedRate),
                "Sprint Coast Bleed",
                "Controls how quickly sprint momentum fades once a skater stops actively driving.",
                0f,
                5f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Glide And Turning",
                nameof(CompetitiveSkatingSettingsData.HighSpeedGlideFriction),
                "High-Speed Glide Friction",
                "Sets the drag applied while gliding at the faster end of the speed range.",
                0f,
                5f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Glide And Turning",
                nameof(CompetitiveSkatingSettingsData.LowSpeedGlideFriction),
                "Low-Speed Glide Friction",
                "Sets the drag applied while gliding at the slower end of the speed range.",
                0f,
                5f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Glide And Turning",
                nameof(CompetitiveSkatingSettingsData.PowerStanceTurnBoost),
                "Carve Turn Boost",
                "Adds extra turning authority while the carve or power-stance movement is active.",
                0f,
                4f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Lateral Skating",
                nameof(CompetitiveSkatingSettingsData.ForwardMaxLateralSpeed),
                "Forward Lateral Speed Cap",
                "Caps sideways skating speed while the skater is facing and moving forward.",
                0f,
                20f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Lateral Skating",
                nameof(CompetitiveSkatingSettingsData.BackwardMaxLateralSpeed),
                "Backward Lateral Speed Cap",
                "Caps sideways skating speed while the skater is moving backward.",
                0f,
                20f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Lateral Skating",
                nameof(CompetitiveSkatingSettingsData.ForwardLateralAcceleration),
                "Forward Lateral Acceleration",
                "Controls how quickly forward-facing lateral movement reaches its speed cap.",
                0f,
                40f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Lateral Skating",
                nameof(CompetitiveSkatingSettingsData.BackwardLateralAcceleration),
                "Backward Lateral Acceleration",
                "Controls how quickly backward lateral movement reaches its speed cap.",
                0f,
                40f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Lateral Skating",
                nameof(CompetitiveSkatingSettingsData.SkaterForwardCurve),
                "Forward Response Curve",
                "Shapes how strongly forward skating input ramps into the custom movement forces.",
                0.1f,
                4f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Lateral Skating",
                nameof(CompetitiveSkatingSettingsData.SkaterBackwardCurve),
                "Backward Response Curve",
                "Shapes how strongly backward skating input ramps into the custom movement forces.",
                0.1f,
                4f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Lateral Skating",
                nameof(CompetitiveSkatingSettingsData.AccelerationCurve),
                "Acceleration Curve",
                "Adjusts the overall force curve used when the mod converts input into acceleration.",
                0.1f,
                5f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Crossovers And Push",
                nameof(CompetitiveSkatingSettingsData.CrossoverBoostMultiplier),
                "Crossover Boost",
                "Scales the extra acceleration granted by a well-timed crossover push.",
                0f,
                4f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Crossovers And Push",
                nameof(CompetitiveSkatingSettingsData.IdealCrossoverTiming),
                "Ideal Crossover Timing",
                "Moves the timing window that the mod treats as the perfect crossover release point.",
                0f,
                1.5f,
                0.01f,
                "s"),
            new CompetitiveSkatingSettingDefinition(
                "Crossovers And Push",
                nameof(CompetitiveSkatingSettingsData.ResidualPushDecay),
                "Residual Push Decay",
                "Controls how quickly leftover push force fades after each stride or crossover.",
                0f,
                3f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Crossovers And Push",
                nameof(CompetitiveSkatingSettingsData.MaxVelocitySpikePerFrame),
                "Velocity Spike Clamp",
                "Limits sudden one-frame speed spikes so force bursts do not overshoot as hard.",
                0f,
                10f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Global Caps",
                nameof(CompetitiveSkatingSettingsData.GlobalMaxGroundSpeed),
                "Ground Speed Cap",
                "Sets the overall top speed limit the mod allows while grounded.",
                0f,
                25f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Global Caps",
                nameof(CompetitiveSkatingSettingsData.GlobalMaxAirSpeed),
                "Air Speed Cap",
                "Sets the overall top speed limit the mod allows while the player is airborne.",
                0f,
                25f,
                0.01f)
        };
    }

    private static List<CompetitiveSkatingSettingDefinition> BuildV1041Definitions()
    {
        List<CompetitiveSkatingSettingDefinition> definitions = new List<CompetitiveSkatingSettingDefinition>();
        foreach (CompetitiveSkatingSettingDefinition definition in LegacySettingDefinitions)
        {
            if (!string.Equals(definition.FieldName, nameof(CompetitiveSkatingSettingsData.EdgeBrakeFriction), StringComparison.Ordinal))
            {
                definitions.Add(definition);
            }
        }

        definitions.AddRange(new[]
        {
            new CompetitiveSkatingSettingDefinition(
                "Competitive Skating v1041",
                nameof(CompetitiveSkatingSettingsData.LateralChargedSpeedMultiplier),
                "Lateral Charge Speed Multiplier",
                "Sets the top-speed multiplier reached by holding one lateral-skate direction for the full charge duration. A value of 1.00 disables the charged speed bonus.",
                1f,
                2f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Competitive Skating v1041",
                nameof(CompetitiveSkatingSettingsData.LateralChargeSeconds),
                "Lateral Charge Time",
                "Sets how long a continuous lateral-skate hold takes to reach the charged speed bonus.",
                0.01f,
                5f,
                0.01f,
                "s"),
            new CompetitiveSkatingSettingDefinition(
                "Competitive Skating v1041",
                nameof(CompetitiveSkatingSettingsData.CrossoverPerfectWindow),
                "Crossover Perfect Window",
                "Sets the timing tolerance around the ideal crossover timing that grants the full crossover acceleration boost.",
                0f,
                1f,
                0.01f,
                "s"),
            new CompetitiveSkatingSettingDefinition(
                "Competitive Skating v1041",
                nameof(CompetitiveSkatingSettingsData.CrossoverFalloffWindow),
                "Crossover Falloff Window",
                "Sets when the crossover boost reaches zero after missing the ideal timing. It is kept at or above the perfect-window value.",
                0f,
                2f,
                0.01f,
                "s"),
            new CompetitiveSkatingSettingDefinition(
                "Competitive Skating v1041",
                nameof(CompetitiveSkatingSettingsData.LateralToForwardTransfer),
                "Lateral-To-Forward Transfer",
                "Controls how much lateral momentum lost to carving or glide friction is converted into forward movement.",
                0f,
                1f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Competitive Skating v1041",
                nameof(CompetitiveSkatingSettingsData.TackleSpeedThreshold),
                "Tackle Speed Threshold",
                "Sets the speed a skater must reach before the game's tackle logic can trigger. This is pushed to active player bodies immediately.",
                0f,
                25f,
                0.1f),
            new CompetitiveSkatingSettingDefinition(
                "Competitive Skating v1041",
                nameof(CompetitiveSkatingSettingsData.TackleForceThreshold),
                "Tackle Force Threshold",
                "Sets the collision-force threshold used by the game's tackle logic. This is pushed to active player bodies immediately.",
                0f,
                25f,
                0.1f),
            new CompetitiveSkatingSettingDefinition(
                "Competitive Skating v1041",
                nameof(CompetitiveSkatingSettingsData.TackleBounceMax),
                "Tackle Bounce Cap",
                "Sets the maximum bounce magnitude produced by a tackle. This is pushed to active player bodies immediately.",
                0f,
                20f,
                0.1f),
            new CompetitiveSkatingSettingDefinition(
                "Competitive Skating v1041",
                nameof(CompetitiveSkatingSettingsData.TackleForceMultiplier),
                "Tackle Force Multiplier",
                "Scales the force applied by a tackle. This is pushed to active player bodies immediately.",
                0f,
                1f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Competitive Skating v1041",
                nameof(CompetitiveSkatingSettingsData.SprintFatigueThreshold),
                "Sprint Fatigue Threshold",
                "Sets the fraction of the ground-speed cap at which sustained sprinting begins building fatigue.",
                0.1f,
                1f,
                0.01f),
            new CompetitiveSkatingSettingDefinition(
                "Competitive Skating v1041",
                nameof(CompetitiveSkatingSettingsData.SprintFatigueBuildSeconds),
                "Sprint Fatigue Build Time",
                "Sets how many seconds of qualifying sprinting it takes to build fatigue from zero to full.",
                0.1f,
                20f,
                0.1f,
                "s"),
            new CompetitiveSkatingSettingDefinition(
                "Competitive Skating v1041",
                nameof(CompetitiveSkatingSettingsData.SprintFatigueRecoverySeconds),
                "Sprint Fatigue Recovery Time",
                "Sets how many seconds it takes fatigue to recover from full to zero when the player is not qualifying for fatigue buildup.",
                0.1f,
                20f,
                0.1f,
                "s"),
            new CompetitiveSkatingSettingDefinition(
                "Competitive Skating v1041",
                nameof(CompetitiveSkatingSettingsData.SprintFatigueMinSpeedFraction),
                "Fatigued Sprint Speed Fraction",
                "Sets the fraction of the ground-speed cap available after sprint fatigue has fully built up.",
                0.1f,
                1f,
                0.01f)
        });

        return definitions;
    }

    private static void ApplyTackleSettingsToActivePlayers()
    {
        if (activeProfile != CompetitiveSkatingCompatibilityProfile.V1041
            || NetworkManager.Singleton?.IsServer != true
            || boundRuntimeFields == null)
        {
            return;
        }

        PlayerManager? playerManager = MonoBehaviourSingleton<PlayerManager>.Instance;
        if (playerManager == null)
        {
            return;
        }

        foreach (Player player in playerManager.GetPlayers())
        {
            PlayerBody? playerBody = player?.PlayerBody;
            if (playerBody == null)
            {
                continue;
            }

            ApplyPlayerBodyTackleField(playerBody, "tackleSpeedThreshold", nameof(CompetitiveSkatingSettingsData.TackleSpeedThreshold));
            ApplyPlayerBodyTackleField(playerBody, "tackleForceThreshold", nameof(CompetitiveSkatingSettingsData.TackleForceThreshold));
            ApplyPlayerBodyTackleField(playerBody, "tackleBounceMaximumMagnitude", nameof(CompetitiveSkatingSettingsData.TackleBounceMax));
            ApplyPlayerBodyTackleField(playerBody, "tackleForceMultiplier", nameof(CompetitiveSkatingSettingsData.TackleForceMultiplier));
        }
    }

    private static void ApplyPlayerBodyTackleField(PlayerBody playerBody, string playerBodyFieldName, string skatingFieldName)
    {
        if (!PlayerBodyTackleFields.TryGetValue(playerBodyFieldName, out FieldInfo? playerBodyField))
        {
            playerBodyField = playerBody.GetType().GetField(playerBodyFieldName, InstanceFieldFlags);
            if (playerBodyField == null || playerBodyField.FieldType != typeof(float))
            {
                return;
            }

            PlayerBodyTackleFields[playerBodyFieldName] = playerBodyField;
        }

        playerBodyField.SetValue(playerBody, boundRuntimeFields![skatingFieldName].GetValue(null));
    }
}

internal enum CompetitiveSkatingCompatibilityProfile
{
    None,
    Legacy,
    V1041
}
