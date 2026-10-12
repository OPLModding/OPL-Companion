using System;

namespace OPLServerBanners;

internal static class CompetitiveSkatingConfigProtocol
{
    public const string ClientToServerListener = "opl_skating_config_client";
    public const string ServerToClientListener = "opl_skating_config_server";

    public const string ActionRequestState = "request_state";
    public const string ActionApplySettings = "apply_settings";
    public const string ActionSaveSettings = "save_settings";
    public const string ActionSetValue = "set_value";
    public const string ActionResetDefaults = "reset_defaults";
    public const string ActionReloadConfig = "reload_config";

    [Serializable]
    internal sealed class Request
    {
        public string Action = string.Empty;
        public string SettingId = string.Empty;
        public string Value = string.Empty;
        public CompetitiveSkatingSettingsData Settings = new CompetitiveSkatingSettingsData();
    }

    [Serializable]
    internal sealed class State
    {
        public bool IsAdmin = false;
        public bool AdminPanelEnabled = true;
        public bool CanEditSettings = false;
        public bool IsModAvailable = false;
        public string CompatibilityProfile = string.Empty;
        public bool ApplySkatingConfigOnStartup = true;
        public string StatusKind = string.Empty;
        public string StatusMessage = string.Empty;
        public string ConfigPath = string.Empty;
        public CompetitiveSkatingSettingsData DefaultSettings = new CompetitiveSkatingSettingsData();
        public CompetitiveSkatingSettingsData SavedSettings = new CompetitiveSkatingSettingsData();
        public CompetitiveSkatingSettingsData Settings = new CompetitiveSkatingSettingsData();
    }
}
