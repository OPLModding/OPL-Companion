using System;
using System.IO;
using System.Reflection;

namespace OPLServerBanners;

internal static class ServerFileSystemPaths
{
    private const string ServerConfigFileName = "server_config.json";
    private const string ConfigDirectoryName = "config";

    public static string GetGameRootPath()
    {
        string baseDirectory = GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        if (LooksLikeGameRoot(baseDirectory))
        {
            return baseDirectory;
        }

        DirectoryInfo? current = new DirectoryInfo(GetPluginDirectoryPath());
        while (current != null)
        {
            if (LooksLikeGameRoot(current.FullName))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return baseDirectory;
    }

    public static string GetConfigDirectoryPath()
    {
        return Path.Combine(GetGameRootPath(), ConfigDirectoryName);
    }

    public static string GetPluginDirectoryPath()
    {
        string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                                   ?? AppDomain.CurrentDomain.BaseDirectory;
        return GetFullPath(assemblyDirectory);
    }

    private static bool LooksLikeGameRoot(string path)
    {
        return File.Exists(Path.Combine(path, ServerConfigFileName))
               || Directory.Exists(Path.Combine(path, ConfigDirectoryName))
               || File.Exists(Path.Combine(path, "Puck.exe"));
    }

    private static string GetFullPath(string path)
    {
        return Path.GetFullPath(string.IsNullOrWhiteSpace(path)
            ? AppDomain.CurrentDomain.BaseDirectory
            : path);
    }
}
