using System;

namespace OPLServerBanners;

[Serializable]
internal sealed class CompetitiveSkatingSettingsData
{
    // This is the OPL default preset and union of supported CompetitiveSkating versions.
    // Runtime definitions decide which fields are active and which fields are written to a server's config.
    public float ExplosiveStartMultiplier = 2.5f;
    public float DirectionalBrakeMultiplier = 1f;
    public float SkaterForwardCurve = 3.2f;
    public float SkaterBackwardCurve = 3.3f;
    public float ForwardMaxLateralSpeed = 8f;
    public float BackwardMaxLateralSpeed = 10.5f;
    public float ForwardLateralAcceleration = 30f;
    public float BackwardLateralAcceleration = 20f;
    public float AccelerationCurve = 2f;
    public float EdgeBrakeFriction = 50f;
    public float LateralChargedSpeedMultiplier = 1.18f;
    public float LateralChargeSeconds = 1.6f;
    public float SprintCoastBleedRate = 0.1f;
    public float HighSpeedGlideFriction = 0.1f;
    public float LowSpeedGlideFriction = 1.1f;
    public float PowerStanceTurnBoost = 1.4f;
    public float BaseStaminaDrain = 0.37f;
    public float TransitionTaxMultiplier = 2.1f;
    public float PowerStanceStaminaDrain = 0.3f;
    public float CrossoverBoostMultiplier = 0f;
    public float IdealCrossoverTiming = 0.3f;
    public float CrossoverPerfectWindow = 0.2f;
    public float CrossoverFalloffWindow = 0.35f;
    public float ResidualPushDecay = 1.5f;
    public float LateralToForwardTransfer = 0.2f;
    public float MaxVelocitySpikePerFrame = 1.5f;
    public float TackleSpeedThreshold = 8.5f;
    public float TackleForceThreshold = 12f;
    public float TackleBounceMax = 5f;
    public float TackleForceMultiplier = 0.07f;
    public float GlobalMaxGroundSpeed = 14.5f;
    public float SprintFatigueThreshold = 1f;
    public float SprintFatigueBuildSeconds = 20f;
    public float SprintFatigueRecoverySeconds = 0.1f;
    public float SprintFatigueMinSpeedFraction = 1f;
    public float GlobalMaxAirSpeed = 10.5f;
}

internal sealed class CompetitiveSkatingSettingDefinition
{
    public CompetitiveSkatingSettingDefinition(
        string section,
        string fieldName,
        string label,
        string tooltip,
        float minValue,
        float maxValue,
        float step,
        string unit = "")
    {
        Section = section;
        FieldName = fieldName;
        Label = label;
        Tooltip = tooltip;
        MinValue = minValue;
        MaxValue = maxValue;
        Step = step;
        Unit = unit;
    }

    public string Id => FieldName;
    public string Section { get; }
    public string FieldName { get; }
    public string Label { get; }
    public string Tooltip { get; }
    public float MinValue { get; }
    public float MaxValue { get; }
    public float Step { get; }
    public string Unit { get; }
}
