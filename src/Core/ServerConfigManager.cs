using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OPLServerBanners;

public sealed class ServerBannerConfig
{
    [JsonPropertyName("configVersion")]
    public int ConfigVersion { get; set; } = ServerConfigManager.CurrentConfigVersion;

    [JsonPropertyName("requireWorkshopModForClients")]
    public bool RequireWorkshopModForClients { get; set; } = true;

    [JsonPropertyName("competitiveSkatingAdminPanelEnabled")]
    public bool CompetitiveSkatingAdminPanelEnabled { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("applySkatingConfigOnStartup")]
    public bool? ApplySkatingConfigOnStartup { get; set; }

    [JsonPropertyName("goalieSpeedCap")]
    public GoalieSpeedCapSettings GoalieSpeedCap { get; set; } = new();

    [JsonPropertyName("servers")]
    public List<ServerBannerEntry> Servers { get; set; } = new();
}

public sealed class ServerBannerEntry
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = string.Empty;
}

public static class ServerConfigManager
{
    public const int CurrentConfigVersion = 2;

    private const string ConfigFileName = "serverconfig.json";
    private const string ToasterBannerSignature = "<color=orange>Toaster's Rink</color>";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly Dictionary<string, ServerBannerEntry> EntriesByEndpoint = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex RichTextTagRegex = new("<.*?>", RegexOptions.Compiled);

    private static bool _loaded;
    private static bool? _legacyApplySkatingConfigOnStartup;
    private static bool _requireWorkshopModForClients = true;
    private static bool _competitiveSkatingAdminPanelEnabled = true;
    private static GoalieSpeedCapSettings _goalieSpeedCap = new();

    public static bool? LegacyApplySkatingConfigOnStartup
    {
        get
        {
            if (!_loaded)
            {
                Load();
            }

            return _legacyApplySkatingConfigOnStartup;
        }
    }

    public static bool CompetitiveSkatingAdminPanelEnabled
    {
        get
        {
            if (!_loaded)
            {
                Load();
            }

            return _competitiveSkatingAdminPanelEnabled;
        }
    }

    public static bool RequireWorkshopModForClients
    {
        get
        {
            if (!_loaded)
            {
                Load();
            }

            return _requireWorkshopModForClients;
        }
    }

    public static GoalieSpeedCapSettings GoalieSpeedCap
    {
        get
        {
            if (!_loaded)
            {
                Load();
            }

            return _goalieSpeedCap;
        }
    }

    public static void Load()
    {
        var configPath = GetConfigPath();
        var config = ReadOrCreateConfig(configPath);

        EntriesByEndpoint.Clear();
        _requireWorkshopModForClients = config.RequireWorkshopModForClients;
        _competitiveSkatingAdminPanelEnabled = config.CompetitiveSkatingAdminPanelEnabled;
        _legacyApplySkatingConfigOnStartup = config.ApplySkatingConfigOnStartup;
        _goalieSpeedCap = config.GoalieSpeedCap ?? new GoalieSpeedCapSettings();
        foreach (var entry in config.Servers)
        {
            if (!entry.Enabled || string.IsNullOrWhiteSpace(entry.Endpoint))
            {
                continue;
            }

            var normalizedEndpoint = NormalizeEndpoint(entry.Endpoint);
            EntriesByEndpoint[normalizedEndpoint] = entry;
        }

        _loaded = true;
        Plugin.Log($"Loaded banner config for {EntriesByEndpoint.Count} servers from {configPath}.");
        Plugin.Log($"Goalie speed cap: {(_goalieSpeedCap.Enabled ? "enabled" : "disabled")}, max speed {_goalieSpeedCap.MaxSpeed:0.###}.");
    }

    public static bool TryGetEntry(EndPoint endPoint, out ServerBannerEntry entry)
    {
        if (!_loaded)
        {
            Load();
        }

        return EntriesByEndpoint.TryGetValue(FormatEndpoint(endPoint), out entry!);
    }

    public static bool ShouldApplyBanner(EndPoint endPoint, string? serverName)
    {
        if (!_loaded)
        {
            Load();
        }

        if (TargetsToasterCompanionBanner(serverName))
        {
            return false;
        }

        return EntriesByEndpoint.ContainsKey(FormatEndpoint(endPoint)) || StartsWithOpl(serverName);
    }

    public static string GetConfigPath()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(assemblyDirectory, ConfigFileName);
    }

    private static ServerBannerConfig ReadOrCreateConfig(string configPath)
    {
        if (!File.Exists(configPath))
        {
            var defaultConfig = CreateDefaultConfig();
            WriteConfig(configPath, defaultConfig);
            return defaultConfig;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<ServerBannerConfig>(json, JsonOptions);
            if (config == null)
            {
                Plugin.LogError($"{ConfigFileName} was empty; using defaults without replacing the existing file.");
                return CreateDefaultConfig();
            }

            config.Servers ??= new List<ServerBannerEntry>();
            if (TryMigrateConfig(json, out var migratedJson))
            {
                WriteConfigText(configPath, migratedJson);
                Plugin.Log($"Updated {ConfigFileName} to config version {CurrentConfigVersion} while preserving existing settings.");
            }

            return config;
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to read {ConfigFileName}; using defaults without replacing the existing file: {exception.Message}");
            return CreateDefaultConfig();
        }
    }

    private static void WriteConfig(string configPath, ServerBannerConfig config)
    {
        WriteConfigText(configPath, JsonSerializer.Serialize(config, JsonOptions));
    }

    private static void WriteConfigText(string configPath, string json)
    {
        try
        {
            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(configPath, json);
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to write {ConfigFileName}: {exception.Message}");
        }
    }

    private static bool TryMigrateConfig(string json, out string migratedJson)
    {
        migratedJson = json;

        try
        {
            var properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions);
            if (properties == null)
            {
                return false;
            }

            var migrated = false;
            if (!TryFindProperty(properties, "configVersion", out var versionPropertyName, out var versionValue))
            {
                properties["configVersion"] = SerializeToJsonElement(CurrentConfigVersion);
                migrated = true;
            }
            else if (versionValue.ValueKind == JsonValueKind.Number
                     && versionValue.TryGetInt32(out var configVersion)
                     && configVersion < CurrentConfigVersion)
            {
                properties[versionPropertyName] = SerializeToJsonElement(CurrentConfigVersion);
                migrated = true;
            }

            if (!TryFindProperty(properties, "goalieSpeedCap", out var goaliePropertyName, out var goalieValue))
            {
                properties["goalieSpeedCap"] = SerializeToJsonElement(new GoalieSpeedCapSettings());
                migrated = true;
            }
            else if (goalieValue.ValueKind == JsonValueKind.Object)
            {
                var goalieProperties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    goalieValue.GetRawText(),
                    JsonOptions);

                if (goalieProperties != null)
                {
                    var goalieMigrated = false;
                    if (!TryFindProperty(goalieProperties, "enabled", out _, out _))
                    {
                        goalieProperties["enabled"] = SerializeToJsonElement(true);
                        goalieMigrated = true;
                    }

                    if (!TryFindProperty(goalieProperties, "maxSpeed", out _, out _))
                    {
                        goalieProperties["maxSpeed"] = SerializeToJsonElement(9.25f);
                        goalieMigrated = true;
                    }

                    if (goalieMigrated)
                    {
                        properties[goaliePropertyName] = SerializeToJsonElement(goalieProperties);
                        migrated = true;
                    }
                }
            }

            if (!migrated)
            {
                return false;
            }

            migratedJson = JsonSerializer.Serialize(properties, JsonOptions);
            return true;
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to migrate {ConfigFileName}; keeping the existing file unchanged: {exception.Message}");
            return false;
        }
    }

    private static bool TryFindProperty(
        Dictionary<string, JsonElement> properties,
        string propertyName,
        out string actualPropertyName,
        out JsonElement value)
    {
        foreach (var property in properties)
        {
            if (string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                actualPropertyName = property.Key;
                value = property.Value;
                return true;
            }
        }

        actualPropertyName = string.Empty;
        value = default;
        return false;
    }

    private static JsonElement SerializeToJsonElement<T>(T value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        return document.RootElement.Clone();
    }

    private static ServerBannerConfig CreateDefaultConfig()
    {
        return new ServerBannerConfig
        {
            Servers = new List<ServerBannerEntry>
            {
                new() { Endpoint = "139.99.209.165:20004" },
                new() { Endpoint = "139.99.209.165:20002" },
                new() { Endpoint = "139.99.209.165:20006" },
                new() { Endpoint = "139.99.209.165:20000" }
            }
        };
    }

    private static string FormatEndpoint(EndPoint endPoint)
    {
        return NormalizeEndpoint($"{endPoint.ipAddress}:{endPoint.port}");
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        return endpoint.Replace(" ", string.Empty);
    }

    private static bool StartsWithOpl(string? serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName))
        {
            return false;
        }

        var plainName = RichTextTagRegex.Replace(serverName, string.Empty).TrimStart();
        return plainName.StartsWith("OPL", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TargetsToasterCompanionBanner(string? serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName))
        {
            return false;
        }

        return serverName.Contains(ToasterBannerSignature, StringComparison.Ordinal);
    }
}
