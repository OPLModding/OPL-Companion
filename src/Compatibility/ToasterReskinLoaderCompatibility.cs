using System;
using System.Collections;
using System.Reflection;

namespace OPLServerBanners;

internal static class ToasterReskinLoaderCompatibility
{
    private const string PluginTypeName = "ToasterReskinLoader.Plugin";
    private const string QoLRunnerTypeName = "ToasterReskinLoader.qol.QoLRunner";
    private const string ToasterAssemblyName = "ToasterReskinLoader";

    private static Type? _pluginType;
    private static Type? _qoLRunnerType;
    private static PropertyInfo? _runnerInstanceProperty;
    private static PropertyInfo? _runnerConfigProperty;
    private static FieldInfo? _savedPasswordsEnabledField;
    private static FieldInfo? _savedPasswordsField;

    public static bool IsInstalled()
    {
        return ResolvePluginType() != null || ResolveQoLRunnerType() != null;
    }

    public static bool HasSavedPassword(EndPoint endPoint)
    {
        if (endPoint == null)
        {
            return false;
        }

        var config = GetQoLConfig();
        if (config == null)
        {
            return false;
        }

        if (ResolveSavedPasswordsEnabledField(config.GetType())?.GetValue(config) is not bool isEnabled || !isEnabled)
        {
            return false;
        }

        if (ResolveSavedPasswordsField(config.GetType())?.GetValue(config) is not IDictionary savedPasswords)
        {
            return false;
        }

        return savedPasswords.Contains(FormatEndpointKey(endPoint));
    }

    private static object? GetQoLConfig()
    {
        var qoLRunnerType = ResolveQoLRunnerType();
        if (qoLRunnerType == null)
        {
            return null;
        }

        var runnerInstance = ResolveRunnerInstanceProperty(qoLRunnerType)?.GetValue(null);
        if (runnerInstance == null)
        {
            return null;
        }

        return ResolveRunnerConfigProperty(qoLRunnerType)?.GetValue(runnerInstance);
    }

    private static Type? ResolvePluginType()
    {
        _pluginType ??= FindType(PluginTypeName);
        return _pluginType;
    }

    private static Type? ResolveQoLRunnerType()
    {
        _qoLRunnerType ??= FindType(QoLRunnerTypeName);
        return _qoLRunnerType;
    }

    private static PropertyInfo? ResolveRunnerInstanceProperty(Type qoLRunnerType)
    {
        _runnerInstanceProperty ??= qoLRunnerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        return _runnerInstanceProperty;
    }

    private static PropertyInfo? ResolveRunnerConfigProperty(Type qoLRunnerType)
    {
        _runnerConfigProperty ??= qoLRunnerType.GetProperty("Config", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return _runnerConfigProperty;
    }

    private static FieldInfo? ResolveSavedPasswordsEnabledField(Type configType)
    {
        _savedPasswordsEnabledField ??= configType.GetField("enableSavedServerPasswords", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return _savedPasswordsEnabledField;
    }

    private static FieldInfo? ResolveSavedPasswordsField(Type configType)
    {
        _savedPasswordsField ??= configType.GetField("savedServerPasswords", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return _savedPasswordsField;
    }

    private static Type? FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (!string.Equals(assembly.GetName().Name, ToasterAssemblyName, StringComparison.Ordinal))
                {
                    continue;
                }

                var resolvedType = assembly.GetType(fullName, throwOnError: false);
                if (resolvedType != null)
                {
                    return resolvedType;
                }
            }
            catch
            {
            }
        }

        return Type.GetType($"{fullName}, {ToasterAssemblyName}", throwOnError: false);
    }

    private static string FormatEndpointKey(EndPoint endPoint)
    {
        return $"{endPoint.ipAddress}:{endPoint.port}";
    }
}
