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
    [JsonPropertyName("requireWorkshopModForClients")]
    public bool RequireWorkshopModForClients { get; set; } = true;

    [JsonPropertyName("competitiveSkatingAdminPanelEnabled")]
    public bool CompetitiveSkatingAdminPanelEnabled { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("applySkatingConfigOnStartup")]
    public bool? ApplySkatingConfigOnStartup { get; set; }

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

    public static void Load()
    {
        var configPath = GetConfigPath();
        var config = ReadOrCreateConfig(configPath);

        EntriesByEndpoint.Clear();
        _requireWorkshopModForClients = config.RequireWorkshopModForClients;
        _competitiveSkatingAdminPanelEnabled = config.CompetitiveSkatingAdminPanelEnabled;
        _legacyApplySkatingConfigOnStartup = config.ApplySkatingConfigOnStartup;
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
        try
        {
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<ServerBannerConfig>(json, JsonOptions);
                return config ?? CreateDefaultConfig();
            }
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to read {ConfigFileName}: {exception.Message}");
        }

        var defaultConfig = CreateDefaultConfig();
        WriteConfig(configPath, defaultConfig);
        return defaultConfig;
    }

    private static void WriteConfig(string configPath, ServerBannerConfig config)
    {
        try
        {
            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(configPath, JsonSerializer.Serialize(config, JsonOptions));
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Failed to write {ConfigFileName}: {exception.Message}");
        }
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
