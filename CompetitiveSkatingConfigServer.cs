using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace OPLServerBanners;

internal sealed class CompetitiveSkatingStoredConfig
{
    public bool ApplySkatingConfigOnStartup { get; set; } = true;

    public CompetitiveSkatingSettingsData Settings { get; set; } = new CompetitiveSkatingSettingsData();
}

internal static class CompetitiveSkatingConfigServer
{
    private const string ConfigFileName = "PakSkatingConfig.json";
    private const string LegacyConfigFileName = "skatingconfig.json";
    private const string MigrationBackupFileName = "PakSkatingConfig.pre-v1041.json";
    private const float BindRetryIntervalSeconds = 5f;

    private static readonly JsonSerializerSettings FileJsonSettings = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented
    };

    private static bool isInitialized;
    private static bool messageHandlerRegistered;
    private static bool initialStateBroadcastSent;
    private static CustomMessagingManager? registeredMessagingManager;

    private static CompetitiveSkatingSettingsData? defaultSettings;
    private static CompetitiveSkatingSettingsData? savedSettings;
    private static CompetitiveSkatingSettingsData? currentSettings;
    private static bool applySkatingConfigOnStartup = true;
    private static string statusKind = "info";
    private static string statusMessage = "Waiting for CompetitiveSkating.dll to become available.";
    private static float nextBindAttemptAt;
    private static bool bindRetryDisabled;

    public static void Initialize()
    {
        CompetitiveSkatingConfigRuntime.Reset();
        isInitialized = true;
        messageHandlerRegistered = false;
        initialStateBroadcastSent = false;
        registeredMessagingManager = null;
        defaultSettings = null;
        savedSettings = null;
        currentSettings = null;
        applySkatingConfigOnStartup = true;
        statusKind = "info";
        statusMessage = "Waiting for CompetitiveSkating.dll to become available.";
        nextBindAttemptAt = 0f;
        bindRetryDisabled = false;
    }

    public static void Shutdown()
    {
        isInitialized = false;
        UnregisterMessageHandler();
        initialStateBroadcastSent = false;
        defaultSettings = null;
        savedSettings = null;
        currentSettings = null;
        applySkatingConfigOnStartup = true;
        statusKind = "info";
        statusMessage = string.Empty;
        nextBindAttemptAt = 0f;
        bindRetryDisabled = false;
        CompetitiveSkatingConfigRuntime.Reset();
    }

    public static void Tick()
    {
        if (!isInitialized)
        {
            return;
        }

        if (!EnsureMessageHandlerRegistered())
        {
            return;
        }

        if (HasReadySettings() || bindRetryDisabled)
        {
            return;
        }

        if (Time.unscaledTime < nextBindAttemptAt)
        {
            return;
        }

        bool ready = EnsureSettingsReady();
        nextBindAttemptAt = ready || bindRetryDisabled ? 0f : Time.unscaledTime + BindRetryIntervalSeconds;
    }

    private static bool EnsureMessageHandlerRegistered()
    {
        NetworkManager? networkManager = NetworkManager.Singleton;
        if (!isInitialized || networkManager == null || !networkManager.IsServer)
        {
            UnregisterMessageHandler();
            return false;
        }

        CustomMessagingManager? currentManager = networkManager.CustomMessagingManager;
        if (currentManager == null)
        {
            return false;
        }

        if (messageHandlerRegistered && ReferenceEquals(registeredMessagingManager, currentManager))
        {
            return true;
        }

        UnregisterMessageHandler();

        try
        {
            currentManager.RegisterNamedMessageHandler(CompetitiveSkatingConfigProtocol.ClientToServerListener, ReceiveRequest);
            registeredMessagingManager = currentManager;
            messageHandlerRegistered = true;
            return true;
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to register skating config handler: {exception}");
            registeredMessagingManager = null;
            messageHandlerRegistered = false;
            return false;
        }
    }

    private static void UnregisterMessageHandler()
    {
        if (!messageHandlerRegistered || registeredMessagingManager == null)
        {
            registeredMessagingManager = null;
            messageHandlerRegistered = false;
            return;
        }

        try
        {
            registeredMessagingManager.UnregisterNamedMessageHandler(CompetitiveSkatingConfigProtocol.ClientToServerListener);
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to unregister skating config handler: {exception}");
        }
        finally
        {
            registeredMessagingManager = null;
            messageHandlerRegistered = false;
        }
    }

    private static bool EnsureSettingsReady()
    {
        if (!CompetitiveSkatingConfigRuntime.TryEnsureBound(out string bindMessage, out bool isTerminalFailure))
        {
            statusKind = "warning";
            statusMessage = bindMessage;
            if (isTerminalFailure && !bindRetryDisabled)
            {
                bindRetryDisabled = true;
                Plugin.Log($"{bindMessage} Automatic retry probing is disabled until the next plugin reload.");
                BroadcastState();
            }

            return false;
        }

        bindRetryDisabled = false;

        if (HasReadySettings())
        {
            if (!initialStateBroadcastSent)
            {
                BroadcastState();
                initialStateBroadcastSent = true;
            }

            return true;
        }

        try
        {
            CompetitiveSkatingSettingsData runtimeSettings = CompetitiveSkatingConfigRuntime.CaptureCurrentSettings();
            if (CompetitiveSkatingConfigRuntime.AreAllZero(runtimeSettings))
            {
                Plugin.Log("CompetitiveSkating reported all-zero runtime values; using the OPL skating default preset.");
            }

            defaultSettings = CompetitiveSkatingConfigRuntime.CreateBuiltInDefaults();

            CompetitiveSkatingStoredConfig storedConfig = LoadOrCreateStoredConfig(defaultSettings, out string loadMessage);
            applySkatingConfigOnStartup = storedConfig.ApplySkatingConfigOnStartup;
            savedSettings = CompetitiveSkatingConfigRuntime.ClampAll(
                storedConfig.Settings ?? CompetitiveSkatingConfigRuntime.Clone(defaultSettings));
            currentSettings = applySkatingConfigOnStartup
                ? CompetitiveSkatingConfigRuntime.Clone(savedSettings)
                : CompetitiveSkatingConfigRuntime.Clone(defaultSettings);
            CompetitiveSkatingConfigRuntime.ApplySettings(currentSettings);
            statusKind = "success";
            statusMessage = BuildStartupStatusMessage(loadMessage, applySkatingConfigOnStartup);

            BroadcastState();
            initialStateBroadcastSent = true;
            return true;
        }
        catch (Exception exception)
        {
            statusKind = "error";
            statusMessage = "Skating config could not be initialized.";
            Plugin.LogError($"Failed to initialize skating config: {exception}");
            return false;
        }
    }

    private static CompetitiveSkatingStoredConfig LoadOrCreateStoredConfig(
        CompetitiveSkatingSettingsData defaults,
        out string loadMessage)
    {
        string configPath = GetConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(configPath) ?? AppDomain.CurrentDomain.BaseDirectory);

        if (!File.Exists(configPath))
        {
            if (TryMigrateLegacyStoredConfig(defaults, out CompetitiveSkatingStoredConfig migratedConfig, out loadMessage))
            {
                SaveStoredConfig(migratedConfig);
                return migratedConfig;
            }

            CompetitiveSkatingStoredConfig createdConfig = CreateStoredConfig(
                defaults,
                ServerConfigManager.LegacyApplySkatingConfigOnStartup ?? true);
            SaveStoredConfig(createdConfig);
            loadMessage = "Created PakSkatingConfig.json from the current CompetitiveSkating defaults.";
            return createdConfig;
        }

        try
        {
            string json = File.ReadAllText(configPath);
            CompetitiveSkatingStoredConfig loadedConfig = DeserializeStoredConfig(json, defaults, out bool shouldResave, out bool usedLegacyShape);
            if (CompetitiveSkatingConfigRuntime.AreAllZero(loadedConfig.Settings))
            {
                loadedConfig.Settings = CompetitiveSkatingConfigRuntime.Clone(defaults);
                shouldResave = true;
                loadMessage = "Detected a zeroed PakSkatingConfig.json, so CompetitiveSkating defaults were restored.";
            }
            else if (usedLegacyShape)
            {
                loadMessage = "Loaded a legacy PakSkatingConfig.json format and upgraded it.";
            }
            else
            {
                loadMessage = "Loaded PakSkatingConfig.json.";
            }

            if (shouldResave)
            {
                CreateMigrationBackup(configPath);
                SaveStoredConfig(loadedConfig);
            }

            return loadedConfig;
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to load skating config file '{configPath}': {exception}");
            CompetitiveSkatingStoredConfig fallbackConfig = CreateStoredConfig(
                defaults,
                ServerConfigManager.LegacyApplySkatingConfigOnStartup ?? true);
            SaveStoredConfig(fallbackConfig);
            loadMessage = "PakSkatingConfig.json could not be read, so CompetitiveSkating defaults were restored.";
            return fallbackConfig;
        }
    }

    private static CompetitiveSkatingStoredConfig DeserializeStoredConfig(
        string json,
        CompetitiveSkatingSettingsData defaults,
        out bool shouldResave,
        out bool usedLegacyShape)
    {
        shouldResave = false;
        usedLegacyShape = false;

        if (string.IsNullOrWhiteSpace(json))
        {
            shouldResave = true;
            return CreateStoredConfig(defaults, ServerConfigManager.LegacyApplySkatingConfigOnStartup ?? true);
        }

        JObject root = JObject.Parse(json);
        if (HasStoredConfigShape(root))
        {
            JToken? settingsToken = root.GetValue("settings", StringComparison.OrdinalIgnoreCase);
            JObject? settingsObject = settingsToken as JObject;
            CompetitiveSkatingSettingsData settings = CompetitiveSkatingConfigRuntime.Clone(defaults);
            if (settingsObject != null && !ContainsOnlyZeroActiveSettings(settingsObject))
            {
                JsonConvert.PopulateObject(settingsObject.ToString(Formatting.None), settings);
            }
            else
            {
                shouldResave = true;
            }

            JToken? startupToken = root.GetValue("applySkatingConfigOnStartup", StringComparison.OrdinalIgnoreCase);
            bool startupToggle = startupToken?.Type == JTokenType.Boolean
                ? startupToken.Value<bool>()
                : ServerConfigManager.LegacyApplySkatingConfigOnStartup ?? true;

            int storedVersion = root.GetValue("configVersion", StringComparison.OrdinalIgnoreCase)?.Value<int?>() ?? 0;
            if (storedVersion < CompetitiveSkatingConfigRuntime.CurrentConfigVersion
                || !HasEveryActiveSetting(settingsObject)
                || HasObsoleteSettings(settingsObject))
            {
                shouldResave = true;
            }

            return CreateStoredConfig(CompetitiveSkatingConfigRuntime.ClampAll(settings), startupToggle);
        }

        usedLegacyShape = true;
        shouldResave = true;

        CompetitiveSkatingSettingsData legacySettings = CompetitiveSkatingConfigRuntime.Clone(defaults);
        JsonConvert.PopulateObject(json, legacySettings);
        return CreateStoredConfig(
            CompetitiveSkatingConfigRuntime.ClampAll(legacySettings),
            ServerConfigManager.LegacyApplySkatingConfigOnStartup ?? true);
    }

    private static bool TryMigrateLegacyStoredConfig(
        CompetitiveSkatingSettingsData defaults,
        out CompetitiveSkatingStoredConfig migratedConfig,
        out string loadMessage)
    {
        string legacyPath = GetLegacyConfigPath();
        migratedConfig = CreateStoredConfig(defaults, ServerConfigManager.LegacyApplySkatingConfigOnStartup ?? true);
        loadMessage = string.Empty;

        if (!File.Exists(legacyPath))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(legacyPath);
            CompetitiveSkatingSettingsData legacySettings = CompetitiveSkatingConfigRuntime.Clone(defaults);
            if (!string.IsNullOrWhiteSpace(json))
            {
                JsonConvert.PopulateObject(json, legacySettings);
            }

            if (CompetitiveSkatingConfigRuntime.AreAllZero(legacySettings))
            {
                legacySettings = CompetitiveSkatingConfigRuntime.Clone(defaults);
                loadMessage = "Migrated a legacy zeroed skatingconfig.json to config\\PakSkatingConfig.json and restored CompetitiveSkating defaults.";
            }
            else
            {
                loadMessage = "Migrated legacy skatingconfig.json to config\\PakSkatingConfig.json.";
            }

            migratedConfig = CreateStoredConfig(
                CompetitiveSkatingConfigRuntime.ClampAll(legacySettings),
                ServerConfigManager.LegacyApplySkatingConfigOnStartup ?? true);
            return true;
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to migrate legacy skating config file '{legacyPath}': {exception}");
            return false;
        }
    }

    private static CompetitiveSkatingStoredConfig CreateStoredConfig(
        CompetitiveSkatingSettingsData settings,
        bool startupToggle)
    {
        return new CompetitiveSkatingStoredConfig
        {
            ApplySkatingConfigOnStartup = startupToggle,
            Settings = CompetitiveSkatingConfigRuntime.Clone(settings)
        };
    }

    private static bool HasStoredConfigShape(JObject root)
    {
        foreach (JProperty property in root.Properties())
        {
            if (string.Equals(property.Name, "settings", StringComparison.OrdinalIgnoreCase)
                || string.Equals(property.Name, "applySkatingConfigOnStartup", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasEveryActiveSetting(JObject? settings)
    {
        if (settings == null)
        {
            return false;
        }

        foreach (CompetitiveSkatingSettingDefinition definition in CompetitiveSkatingConfigRuntime.Definitions)
        {
            if (settings.GetValue(definition.FieldName, StringComparison.OrdinalIgnoreCase) == null)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasObsoleteSettings(JObject? settings)
    {
        return CompetitiveSkatingConfigRuntime.CurrentConfigVersion >= 2
               && settings?.GetValue(nameof(CompetitiveSkatingSettingsData.EdgeBrakeFriction), StringComparison.OrdinalIgnoreCase) != null;
    }

    private static bool ContainsOnlyZeroActiveSettings(JObject settings)
    {
        bool hasActiveValue = false;
        foreach (CompetitiveSkatingSettingDefinition definition in CompetitiveSkatingConfigRuntime.Definitions)
        {
            JToken? value = settings.GetValue(definition.FieldName, StringComparison.OrdinalIgnoreCase);
            if (value == null)
            {
                continue;
            }

            if (!float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedValue)
                || Mathf.Abs(parsedValue) > 0.0001f)
            {
                return false;
            }

            hasActiveValue = true;
        }

        return hasActiveValue;
    }

    private static void CreateMigrationBackup(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return;
        }

        string backupPath = Path.Combine(
            Path.GetDirectoryName(configPath) ?? AppDomain.CurrentDomain.BaseDirectory,
            MigrationBackupFileName);
        if (!File.Exists(backupPath))
        {
            File.Copy(configPath, backupPath);
            Plugin.Log($"Backed up the pre-v1041 skating config to '{backupPath}'.");
        }
    }

    private static void SaveStoredConfig(CompetitiveSkatingStoredConfig storedConfig)
    {
        string configPath = GetConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(configPath) ?? AppDomain.CurrentDomain.BaseDirectory);
        storedConfig.Settings = CompetitiveSkatingConfigRuntime.ClampAll(
            storedConfig.Settings ?? new CompetitiveSkatingSettingsData());
        JObject settings = new JObject();
        foreach (CompetitiveSkatingSettingDefinition definition in CompetitiveSkatingConfigRuntime.Definitions)
        {
            settings[definition.FieldName] = CompetitiveSkatingConfigRuntime.GetValue(storedConfig.Settings, definition.Id);
        }

        JObject document = new JObject
        {
            ["ConfigVersion"] = CompetitiveSkatingConfigRuntime.CurrentConfigVersion,
            ["ApplySkatingConfigOnStartup"] = storedConfig.ApplySkatingConfigOnStartup,
            ["Settings"] = settings
        };
        string json = document.ToString(FileJsonSettings.Formatting);
        File.WriteAllText(configPath, json);
    }

    private static void SaveSettings(CompetitiveSkatingSettingsData settings)
    {
        SaveStoredConfig(CreateStoredConfig(settings, applySkatingConfigOnStartup));
    }

    private static void ReceiveRequest(ulong senderClientId, FastBufferReader reader)
    {
        try
        {
            string json = ReadJsonPayload(reader);
            CompetitiveSkatingConfigProtocol.Request? request = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonConvert.DeserializeObject<CompetitiveSkatingConfigProtocol.Request>(json);

            if (request == null)
            {
                SendState(senderClientId, "error", "Skating config request could not be read.");
                return;
            }

            switch (request.Action ?? string.Empty)
            {
                case CompetitiveSkatingConfigProtocol.ActionRequestState:
                    SendState(senderClientId);
                    break;
                case CompetitiveSkatingConfigProtocol.ActionApplySettings:
                    HandleApplySettings(senderClientId, request);
                    break;
                case CompetitiveSkatingConfigProtocol.ActionSaveSettings:
                    HandleSaveSettings(senderClientId, request);
                    break;
                case CompetitiveSkatingConfigProtocol.ActionSetValue:
                    HandleSetValue(senderClientId, request);
                    break;
                case CompetitiveSkatingConfigProtocol.ActionResetDefaults:
                    HandleResetDefaults(senderClientId);
                    break;
                case CompetitiveSkatingConfigProtocol.ActionReloadConfig:
                    HandleReloadConfig(senderClientId);
                    break;
                default:
                    SendState(senderClientId, "warning", "That skating config action is not supported by this server.");
                    break;
            }
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to process skating config request: {exception}");
            SendState(senderClientId, "error", "The server failed to process that skating config request.");
        }
    }

    private static void HandleApplySettings(ulong senderClientId, CompetitiveSkatingConfigProtocol.Request request)
    {
        if (!EnsureAuthorized(senderClientId))
        {
            return;
        }

        if (!EnsureSettingsReady())
        {
            SendState(senderClientId, statusKind, statusMessage);
            return;
        }

        try
        {
            currentSettings = CompetitiveSkatingConfigRuntime.ClampAll(request.Settings ?? new CompetitiveSkatingSettingsData());
            CompetitiveSkatingConfigRuntime.ApplySettings(currentSettings);
            BroadcastState("success", BuildSubmitStatusMessage());
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to apply submitted skating config: {exception}");
            SendState(senderClientId, "error", "The server could not apply the submitted skating config.");
        }
    }

    private static void HandleSaveSettings(ulong senderClientId, CompetitiveSkatingConfigProtocol.Request request)
    {
        if (!EnsureAuthorized(senderClientId))
        {
            return;
        }

        if (!EnsureSettingsReady())
        {
            SendState(senderClientId, statusKind, statusMessage);
            return;
        }

        try
        {
            savedSettings = CompetitiveSkatingConfigRuntime.ClampAll(request.Settings ?? new CompetitiveSkatingSettingsData());
            SaveSettings(savedSettings);
            BroadcastState("success", BuildSaveStatusMessage());
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to save submitted skating config: {exception}");
            SendState(senderClientId, "error", "The server could not save PakSkatingConfig.json.");
        }
    }

    private static void HandleSetValue(ulong senderClientId, CompetitiveSkatingConfigProtocol.Request request)
    {
        if (!EnsureAuthorized(senderClientId))
        {
            return;
        }

        if (!EnsureSettingsReady() || currentSettings == null)
        {
            SendState(senderClientId, statusKind, statusMessage);
            return;
        }

        CompetitiveSkatingSettingDefinition? definition = CompetitiveSkatingConfigRuntime.FindDefinition(request.SettingId);
        if (definition == null)
        {
            SendState(senderClientId, "warning", "That skating setting does not exist in this server build.");
            return;
        }

        if (!float.TryParse(request.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedValue))
        {
            SendState(senderClientId, "warning", $"'{definition.Label}' could not be parsed as a number.");
            return;
        }

        if (!CompetitiveSkatingConfigRuntime.TrySetValue(currentSettings, definition.Id, parsedValue, out float appliedValue))
        {
            SendState(senderClientId, "warning", $"'{definition.Label}' could not be updated.");
            return;
        }

        try
        {
            CompetitiveSkatingConfigRuntime.ApplySettings(currentSettings);
            savedSettings = CompetitiveSkatingConfigRuntime.Clone(currentSettings);
            SaveSettings(savedSettings);

            string unitSuffix = string.IsNullOrWhiteSpace(definition.Unit) ? string.Empty : $" {definition.Unit}";
            BroadcastState(
                "success",
                $"{definition.Label} is now {CompetitiveSkatingConfigRuntime.FormatValue(appliedValue, definition.Step)}{unitSuffix}. Changes apply server-wide and were saved to PakSkatingConfig.json.");
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to apply skating config setting '{definition.Id}': {exception}");
            SendState(senderClientId, "error", $"'{definition.Label}' could not be applied.");
        }
    }

    private static void HandleResetDefaults(ulong senderClientId)
    {
        if (!EnsureAuthorized(senderClientId))
        {
            return;
        }

        if (!EnsureSettingsReady() || defaultSettings == null)
        {
            SendState(senderClientId, statusKind, statusMessage);
            return;
        }

        try
        {
            savedSettings = CompetitiveSkatingConfigRuntime.Clone(defaultSettings);
            currentSettings = CompetitiveSkatingConfigRuntime.Clone(defaultSettings);
            CompetitiveSkatingConfigRuntime.ApplySettings(currentSettings);
            SaveSettings(savedSettings);
            BroadcastState("success", BuildDefaultsStatusMessage());
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to reset skating config defaults: {exception}");
            SendState(senderClientId, "error", "The server could not restore CompetitiveSkating defaults.");
        }
    }

    private static void HandleReloadConfig(ulong senderClientId)
    {
        if (!EnsureAuthorized(senderClientId))
        {
            return;
        }

        if (!EnsureSettingsReady() || defaultSettings == null)
        {
            SendState(senderClientId, statusKind, statusMessage);
            return;
        }

        try
        {
            CompetitiveSkatingStoredConfig storedConfig = LoadOrCreateStoredConfig(defaultSettings, out _);
            applySkatingConfigOnStartup = storedConfig.ApplySkatingConfigOnStartup;
            savedSettings = CompetitiveSkatingConfigRuntime.ClampAll(
                storedConfig.Settings ?? CompetitiveSkatingConfigRuntime.Clone(defaultSettings));
            currentSettings = CompetitiveSkatingConfigRuntime.Clone(savedSettings);
            CompetitiveSkatingConfigRuntime.ApplySettings(currentSettings);
            BroadcastState("info", BuildReloadStatusMessage());
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to reload skating config: {exception}");
            SendState(senderClientId, "error", "The server could not reload PakSkatingConfig.json.");
        }
    }

    private static bool EnsureAuthorized(ulong senderClientId)
    {
        if (!ServerConfigManager.CompetitiveSkatingAdminPanelEnabled)
        {
            SendState(senderClientId, "warning", "The skating admin panel is disabled in serverconfig.json.");
            return false;
        }

        if (IsClientAdmin(senderClientId))
        {
            return true;
        }

        SendState(senderClientId, "error", "Only Steam IDs listed in admin_steam_ids.json can edit skating config.");
        return false;
    }

    private static bool IsClientAdmin(ulong clientId)
    {
        PlayerManager? playerManager = MonoBehaviourSingleton<PlayerManager>.Instance;
        Player? player = playerManager?.GetPlayerByClientId(clientId);
        if (player == null)
        {
            return false;
        }

        string steamId = player.SteamId.Value.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(steamId))
        {
            global::AdminManager? adminManager = MonoBehaviourSingleton<global::AdminManager>.Instance;
            if (adminManager != null)
            {
                return adminManager.IsSteamIdAdmin(steamId);
            }
        }

        return player.AdminLevel.Value > 0;
    }

    private static void BroadcastState(string? overrideKind = null, string? overrideMessage = null)
    {
        NetworkManager? networkManager = NetworkManager.Singleton;
        if (networkManager?.CustomMessagingManager == null || !networkManager.IsServer)
        {
            return;
        }

        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            SendState(clientId, overrideKind, overrideMessage);
        }
    }

    private static void SendState(ulong recipientClientId, string? overrideKind = null, string? overrideMessage = null)
    {
        NetworkManager? networkManager = NetworkManager.Singleton;
        if (networkManager?.CustomMessagingManager == null)
        {
            return;
        }

        CompetitiveSkatingConfigProtocol.State response = BuildState(recipientClientId, overrideKind, overrideMessage);
        string json = JsonConvert.SerializeObject(response);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        using FastBufferWriter writer = new FastBufferWriter(bytes.Length, Allocator.Temp);
        writer.WriteBytes(bytes);
        networkManager.CustomMessagingManager.SendNamedMessage(
            CompetitiveSkatingConfigProtocol.ServerToClientListener,
            recipientClientId,
            writer,
            NetworkDelivery.ReliableFragmentedSequenced);
    }

    private static CompetitiveSkatingConfigProtocol.State BuildState(
        ulong recipientClientId,
        string? overrideKind,
        string? overrideMessage)
    {
        bool isAvailable = HasReadySettings();
        bool isPanelEnabled = ServerConfigManager.CompetitiveSkatingAdminPanelEnabled;
        bool isAdmin = isPanelEnabled && IsClientAdmin(recipientClientId);
        bool includeSettings = isAdmin;

        string effectiveKind = string.IsNullOrWhiteSpace(overrideKind) ? statusKind : overrideKind ?? statusKind;
        string effectiveMessage = string.IsNullOrWhiteSpace(overrideMessage)
            ? GetDefaultStatusMessage(isPanelEnabled, isAdmin, isAvailable, applySkatingConfigOnStartup)
            : overrideMessage ?? GetDefaultStatusMessage(isPanelEnabled, isAdmin, isAvailable, applySkatingConfigOnStartup);

        CompetitiveSkatingSettingsData snapshot = includeSettings && currentSettings != null
            ? CompetitiveSkatingConfigRuntime.Clone(currentSettings)
            : includeSettings && defaultSettings != null
                ? CompetitiveSkatingConfigRuntime.Clone(defaultSettings)
                : new CompetitiveSkatingSettingsData();

        return new CompetitiveSkatingConfigProtocol.State
        {
            IsAdmin = isAdmin,
            AdminPanelEnabled = isPanelEnabled,
            CanEditSettings = isPanelEnabled && isAdmin && isAvailable,
            IsModAvailable = isAvailable,
            CompatibilityProfile = isAvailable ? CompetitiveSkatingConfigRuntime.CompatibilityProfileName : string.Empty,
            ApplySkatingConfigOnStartup = applySkatingConfigOnStartup,
            StatusKind = effectiveKind,
            StatusMessage = effectiveMessage,
            ConfigPath = GetConfigPath(),
            DefaultSettings = includeSettings && defaultSettings != null
                ? CompetitiveSkatingConfigRuntime.Clone(defaultSettings)
                : new CompetitiveSkatingSettingsData(),
            SavedSettings = includeSettings && savedSettings != null
                ? CompetitiveSkatingConfigRuntime.Clone(savedSettings)
                : includeSettings && defaultSettings != null
                    ? CompetitiveSkatingConfigRuntime.Clone(defaultSettings)
                    : new CompetitiveSkatingSettingsData(),
            Settings = snapshot
        };
    }

    private static string GetDefaultStatusMessage(bool isPanelEnabled, bool isAdmin, bool isAvailable, bool applySkatingConfigOnStartup)
    {
        if (!isPanelEnabled)
        {
            return "The skating admin panel is disabled in serverconfig.json.";
        }

        if (!isAvailable)
        {
            return string.IsNullOrWhiteSpace(statusMessage)
                ? CompetitiveSkatingConfigRuntime.GetAvailabilityMessage()
                : statusMessage;
        }

        if (!isAdmin)
        {
            return applySkatingConfigOnStartup
                ? "Skating config is active on this server, but only Steam IDs in admin_steam_ids.json can edit it."
                : "Skating config startup loading is disabled on this server, and only Steam IDs in admin_steam_ids.json can edit it.";
        }

        return string.IsNullOrWhiteSpace(statusMessage)
            ? applySkatingConfigOnStartup
                ? "Default stages built-in values, Load stages PakSkatingConfig.json, Save writes the file only, and Submit applies the draft live only."
                : "Default stages built-in values, Load stages PakSkatingConfig.json, Save writes the file only, and Submit applies the draft live only. Startup still uses CompetitiveSkating defaults while applySkatingConfigOnStartup is false."
            : statusMessage;
    }

    private static string BuildStartupStatusMessage(string loadMessage, bool applySkatingConfigOnStartup)
    {
        return applySkatingConfigOnStartup
            ? $"{loadMessage} Startup is using PakSkatingConfig.json values."
            : $"{loadMessage} Startup is using CompetitiveSkating defaults because applySkatingConfigOnStartup is false.";
    }

    private static bool HasReadySettings()
    {
        return defaultSettings != null && savedSettings != null && currentSettings != null;
    }

    private static string BuildSubmitStatusMessage()
    {
        return applySkatingConfigOnStartup
            ? "Submitted skating config was applied server-wide. PakSkatingConfig.json was not changed, so restart will still use the saved file values."
            : "Submitted skating config was applied server-wide. PakSkatingConfig.json was not changed, and startup still uses CompetitiveSkating defaults while applySkatingConfigOnStartup is false.";
    }

    private static string BuildSaveStatusMessage()
    {
        return applySkatingConfigOnStartup
            ? "Saved the current draft to PakSkatingConfig.json. Live server values were not changed, and restart will use the saved file values."
            : "Saved the current draft to PakSkatingConfig.json. Live server values were not changed, and startup still uses CompetitiveSkating defaults while applySkatingConfigOnStartup is false.";
    }

    private static string BuildDefaultsStatusMessage()
    {
        return applySkatingConfigOnStartup
            ? "CompetitiveSkating defaults were restored, applied server-wide, and saved to PakSkatingConfig.json."
            : "CompetitiveSkating defaults were restored, applied server-wide, and saved to PakSkatingConfig.json. Startup is still using built-in defaults because applySkatingConfigOnStartup is false.";
    }

    private static string BuildReloadStatusMessage()
    {
        return applySkatingConfigOnStartup
            ? "Reloaded PakSkatingConfig.json and applied it server-wide."
            : "Reloaded PakSkatingConfig.json and applied it server-wide. Startup still uses CompetitiveSkating defaults while applySkatingConfigOnStartup is false.";
    }

    private static string ReadJsonPayload(FastBufferReader reader)
    {
        int remainingBytes = reader.Length - reader.Position;
        if (remainingBytes <= 0)
        {
            return string.Empty;
        }

        byte[] bytes = new byte[remainingBytes];
        for (int index = 0; index < remainingBytes; index++)
        {
            reader.ReadByte(out bytes[index]);
        }

        return Encoding.UTF8.GetString(bytes).Trim();
    }

    private static string GetConfigPath()
    {
        return Path.Combine(ServerFileSystemPaths.GetConfigDirectoryPath(), ConfigFileName);
    }

    private static string GetLegacyConfigPath()
    {
        return Path.Combine(ServerFileSystemPaths.GetPluginDirectoryPath(), LegacyConfigFileName);
    }
}
