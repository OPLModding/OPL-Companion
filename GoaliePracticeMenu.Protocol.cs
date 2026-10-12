using System;

namespace OPLServerBanners;

internal static class GoaliePracticeMenuProtocol
{
    public const string ClientToServerListener = "doma_goalie_prac_menu_client";
    public const string ServerToClientListener = "doma_goalie_prac_menu_server";

    public const string ActionRequestState = "request_state";
    public const string ActionToggleSession = "toggle_session";
    public const string ActionResetDefaults = "reset_defaults";
    public const string ActionSetShotSelection = "set_shot_selection";
    public const string ActionSetShotDistance = "set_shot_distance";
    public const string ActionSetShotArea = "set_shot_area";
    public const string ActionSetConstrainPasses = "set_constrain_passes";
    public const string ActionSetPassCount = "set_pass_count";
    public const string ActionSetPassLateralRange = "set_pass_lateral_range";
    public const string ActionSetMinShotSpeedKph = "set_min_shot_speed_kph";
    public const string ActionSetMaxShotSpeedKph = "set_max_shot_speed_kph";
    public const string ActionSetMaxShotReleaseHeight = "set_max_shot_release_height";
    public const string ActionSetMinTimeBetweenShots = "set_min_time_between_shots";
    public const string ActionSetMaxTimeBetweenShots = "set_max_time_between_shots";

    public const int MaxPassCount = 7;
    public const float MaxPassLateralRange = 25f;
    public const float MaxShotSpeedKph = 190f;
    public const float MaxShotReleaseHeight = 2f;
    public const float MaxTimeBetweenShots = 10f;
    public const float MinSpeedGapKph = 1f;
    public const float MinTimeGapSeconds = 0.1f;

    [Serializable]
    internal sealed class Request
    {
        public string Action = string.Empty;
        public string Value = string.Empty;
    }

    [Serializable]
    internal sealed class State
    {
        public bool IsGoalieEligible = false;
        public bool IsSessionActive = false;
        public bool CanToggleSession = false;
        public bool CanEditSettings = false;
        public bool IsWarmup = false;
        public string StatusKind = string.Empty;
        public string StatusMessage = string.Empty;
        public string SummaryText = string.Empty;
        public string ShotSelection = "all";
        public string ShotDistance = "all";
        public string ShotArea = "all";
        public bool ConstrainPasses = true;
        public int PassCount = 0;
        public float PassLateralRange = 0f;
        public float MinShotSpeedKph = 0f;
        public float MaxShotSpeedKph = 0f;
        public float MaxShotReleaseHeight = 0f;
        public float MinTimeBetweenShots = 0f;
        public float MaxTimeBetweenShots = 0f;
    }
}
