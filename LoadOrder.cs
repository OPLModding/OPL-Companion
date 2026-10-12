using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine.UIElements;

namespace OPLServerBanners;

public static class LoadOrder
{
    private const int FastPassConcurrency = 12;
    private const int BackgroundRetryConcurrency = 4;
    private const int FastPassConnectTimeoutMs = 350;
    private const int FastPassResponseTimeoutMs = 450;
    private const int RetryConnectTimeoutMs = 1000;
    private const int RetryResponseTimeoutMs = 1000;
    private const int UiFlushDebounceMs = 200;

    // The stock browser only knows ip:port before preview data arrives, so priority
    // has to be keyed by endpoint rather than server name.
    private static readonly Dictionary<string, int> PreferredServerPriority = new(StringComparer.OrdinalIgnoreCase)
    {
        ["139.99.209.165:20004"] = 0,
        ["139.99.209.165:20002"] = 1,
        ["139.99.209.165:20006"] = 2,
        ["139.99.209.165:20000"] = 3,
        // Add more exact endpoint matches here as ip:port.
        // Host labels such as "ARMA:27002" will only match if the browser
        // receives that exact endpoint string before preview data loads.
    };

    private static readonly ConditionalWeakTable<UIServerBrowser, BrowserRefreshState> RefreshStates = new();
    private static readonly FieldInfo? RefreshButtonField = AccessTools.Field(typeof(UIServerBrowser), "refreshButton");
    private static readonly MethodInfo? AddServerMethod = AccessTools.Method(typeof(UIServerBrowser), "AddServer");
    private static readonly MethodInfo? RemoveAllServersMethod = AccessTools.Method(typeof(UIServerBrowser), "RemoveAllServers");
    private static readonly MethodInfo? FilterServersMethod = AccessTools.Method(typeof(UIServerBrowser), "FilterServers");
    private static readonly MethodInfo? SortServersMethod = AccessTools.Method(typeof(UIServerBrowser), "SortServers");
    private static readonly MethodInfo? StyleServerMethod = AccessTools.Method(typeof(UIServerBrowser), "StyleServer");
    private static readonly MethodInfo? SetServerPreviewDataMethod = AccessTools.Method(typeof(UIServerBrowser), "SetServerPreviewData");

    public static void ApplyPatches(Harmony harmony)
    {
        harmony.CreateClassProcessor(typeof(UpdateEndPointsPatch)).Patch();
    }

    private sealed class BrowserRefreshState
    {
        public readonly object SyncRoot = new();

        public readonly Dictionary<string, PendingPreviewData> PendingPreviewData = new(StringComparer.OrdinalIgnoreCase);

        public int Generation;

        public CancellationTokenSource? CancellationSource;

        public bool UiFlushScheduled;
    }

    private sealed class PendingPreviewData
    {
        public EndPoint EndPoint = null!;

        public ServerPreviewData PreviewData = null!;
    }

    [HarmonyPatch(typeof(UIServerBrowser), "UpdateEndPoints")]
    private static class UpdateEndPointsPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(UIServerBrowser __instance, EndPoint[] endPoints)
        {
            if (ToasterReskinLoaderCompatibility.IsInstalled())
            {
                return true;
            }

            if (!HasRequiredMembers())
            {
                Plugin.LogError("LoadOrder optimization is missing required UIServerBrowser members; falling back to the stock browser flow.");
                return true;
            }

            StartOptimizedRefresh(__instance, endPoints ?? Array.Empty<EndPoint>());
            return false;
        }
    }

    private static bool HasRequiredMembers()
    {
        return RefreshButtonField != null &&
               AddServerMethod != null &&
               RemoveAllServersMethod != null &&
               FilterServersMethod != null &&
               SortServersMethod != null &&
               StyleServerMethod != null &&
               SetServerPreviewDataMethod != null;
    }

    private static void StartOptimizedRefresh(UIServerBrowser browser, IReadOnlyList<EndPoint> endPoints)
    {
        var orderedEndPoints = PrioritizeEndpoints(endPoints).ToArray();
        var state = RefreshStates.GetValue(browser, _ => new BrowserRefreshState());

        state.CancellationSource?.Cancel();
        state.CancellationSource?.Dispose();
        state.CancellationSource = new CancellationTokenSource();
        lock (state.SyncRoot)
        {
            state.PendingPreviewData.Clear();
            state.UiFlushScheduled = false;
        }

        var generation = ++state.Generation;
        var cancellationToken = state.CancellationSource.Token;

        PrepareBrowserRows(browser, orderedEndPoints);
        Plugin.Log($"Optimizing server browser refresh for {orderedEndPoints.Length} endpoints.");

        _ = RunRefreshAsync(browser, orderedEndPoints, generation, cancellationToken);
    }

    private static IEnumerable<EndPoint> PrioritizeEndpoints(IReadOnlyList<EndPoint> endPoints)
    {
        return endPoints
            .Select((endPoint, index) => new
            {
                EndPoint = endPoint,
                OriginalIndex = index,
                Priority = GetPreferredPriority(endPoint)
            })
            .OrderBy(entry => entry.Priority)
            .ThenBy(entry => entry.OriginalIndex)
            .Select(entry => entry.EndPoint);
    }

    private static int GetPreferredPriority(EndPoint endPoint)
    {
        return PreferredServerPriority.TryGetValue(FormatEndPointKey(endPoint), out var priority)
            ? priority
            : int.MaxValue;
    }

    private static string FormatEndPointKey(EndPoint endPoint)
    {
        return $"{endPoint.ipAddress}:{endPoint.port}";
    }

    private static void PrepareBrowserRows(UIServerBrowser browser, IReadOnlyList<EndPoint> endPoints)
    {
        InvokeBrowserMethod(RemoveAllServersMethod, browser);
        SetRefreshEnabled(browser, enabled: false);

        foreach (var endPoint in endPoints)
        {
            InvokeBrowserMethod(AddServerMethod, browser, endPoint);
        }

        InvokeBrowserMethod(FilterServersMethod, browser);
        InvokeBrowserMethod(SortServersMethod, browser);
    }

    private static async Task RunRefreshAsync(
        UIServerBrowser browser,
        IReadOnlyList<EndPoint> endPoints,
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            var unresolvedEndPoints = await RunProbePassAsync(
                browser,
                endPoints,
                generation,
                GetProbeConcurrencyLimit(),
                TryProbeServerFastPassAsync,
                cancellationToken);

            if (!IsCurrentRefresh(browser, generation, cancellationToken))
            {
                return;
            }

            EnqueueBrowserMainThread(browser, generation, cancellationToken, () => SetRefreshEnabled(browser, enabled: true));
            ScheduleBrowserUiFlush(browser, generation, cancellationToken);
            Plugin.Log($"Fast pass resolved {endPoints.Count - unresolvedEndPoints.Count}/{endPoints.Count} servers.");

            if (unresolvedEndPoints.Count > 0)
            {
                _ = RetrySlowServersInBackgroundAsync(browser, unresolvedEndPoints, generation, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Optimized server refresh failed: {exception}");
            if (IsCurrentRefresh(browser, generation, cancellationToken))
            {
                EnqueueBrowserMainThread(browser, generation, cancellationToken, () => SetRefreshEnabled(browser, enabled: true));
            }
        }
    }

    private static int GetProbeConcurrencyLimit()
    {
        return FastPassConcurrency;
    }

    private static async Task<List<EndPoint>> RunProbePassAsync(
        UIServerBrowser browser,
        IReadOnlyList<EndPoint> endPoints,
        int generation,
        int concurrency,
        Func<EndPoint, CancellationToken, Task<ServerPreviewData?>> probeAsync,
        CancellationToken cancellationToken)
    {
        var unresolvedEndPoints = new List<EndPoint>();
        var unresolvedLock = new object();
        using var semaphore = new SemaphoreSlim(concurrency);

        var tasks = endPoints.Select(async endPoint =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var previewData = await probeAsync(endPoint, cancellationToken);

                if (previewData == null)
                {
                    lock (unresolvedLock)
                    {
                        unresolvedEndPoints.Add(endPoint);
                    }

                    return;
                }

                ApplyPreviewData(browser, endPoint, previewData, generation, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return unresolvedEndPoints;
    }

    private static Task<ServerPreviewData?> TryProbeServerFastPassAsync(EndPoint endPoint, CancellationToken cancellationToken)
    {
        return TryProbeServerAsync(
            endPoint,
            FastPassConnectTimeoutMs,
            FastPassResponseTimeoutMs,
            cancellationToken);
    }

    private static async Task RetrySlowServersInBackgroundAsync(
        UIServerBrowser browser,
        IReadOnlyList<EndPoint> unresolvedEndPoints,
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            var stillUnresolved = await RunProbePassAsync(
                browser,
                unresolvedEndPoints,
                generation,
                BackgroundRetryConcurrency,
                TryProbeServerRetryPassAsync,
                cancellationToken);

            if (IsCurrentRefresh(browser, generation, cancellationToken))
            {
                ScheduleBrowserUiFlush(browser, generation, cancellationToken);

                if (stillUnresolved.Count > 0)
                {
                    Plugin.Log($"Background retry still could not resolve {stillUnresolved.Count} servers.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Plugin.LogError($"Background server retry failed: {exception}");
        }
    }

    private static Task<ServerPreviewData?> TryProbeServerRetryPassAsync(EndPoint endPoint, CancellationToken cancellationToken)
    {
        return TryProbeServerAsync(
            endPoint,
            RetryConnectTimeoutMs,
            RetryResponseTimeoutMs,
            cancellationToken);
    }

    private static Task<ServerPreviewData?> TryProbeServerAsync(
        EndPoint endPoint,
        int connectTimeoutMs,
        int responseTimeoutMs,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => ProbeServer(endPoint, connectTimeoutMs, responseTimeoutMs, cancellationToken),
            cancellationToken);
    }

    private static ServerPreviewData? ProbeServer(
        EndPoint endPoint,
        int connectTimeoutMs,
        int responseTimeoutMs,
        CancellationToken cancellationToken)
    {
        var tcpClient = new TCPClient(endPoint, connectTimeoutMs, responseTimeoutMs);
        var responseEvent = new ManualResetEventSlim(initialState: false);
        double pingTimestamp = 0.0;
        ServerPreviewData? previewData = null;

        tcpClient.OnConnected += () =>
        {
            var message = JsonSerializer.Serialize(new TCPServerPreviewRequest());
            tcpClient.SendMessage(message);
        };

        tcpClient.OnMessageSent += message =>
        {
            try
            {
                var parsedMessage = JsonSerializer.Deserialize<TCPServerMessage>(message);
                if (parsedMessage?.type == TCPServerMessageType.PreviewRequest)
                {
                    pingTimestamp = Utils.GetTimestamp();
                }
            }
            catch (Exception exception)
            {
                Plugin.LogError($"Failed to parse preview request for {FormatEndPointKey(endPoint)}: {exception.Message}");
            }
        };

        tcpClient.OnMessageReceived += message =>
        {
            try
            {
                var parsedMessage = JsonSerializer.Deserialize<TCPServerMessage>(message);
                if (parsedMessage?.type != TCPServerMessageType.PreviewResponse)
                {
                    return;
                }

                var previewResponse = JsonSerializer.Deserialize<TCPServerPreviewResponse>(message);
                if (previewResponse == null)
                {
                    return;
                }

                previewData = new ServerPreviewData
                {
                    name = previewResponse.name,
                    players = previewResponse.players,
                    maxPlayers = previewResponse.maxPlayers,
                    isPasswordProtected = previewResponse.isPasswordProtected,
                    clientRequiredModIds = previewResponse.clientRequiredModIds ?? Array.Empty<string>(),
                    ping = (int)(Utils.GetTimestamp() - pingTimestamp)
                };

                responseEvent.Set();
            }
            catch (Exception exception)
            {
                Plugin.LogError($"Failed to parse preview response for {FormatEndPointKey(endPoint)}: {exception.Message}");
            }
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            tcpClient.Connect();

            if (!tcpClient.IsConnected)
            {
                return null;
            }

            responseEvent.Wait(responseTimeoutMs, cancellationToken);
            return previewData;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            responseEvent.Dispose();

            if (tcpClient.IsConnected)
            {
                tcpClient.Disconnect();
            }
        }
    }

    private static void ApplyPreviewData(
        UIServerBrowser browser,
        EndPoint endPoint,
        ServerPreviewData previewData,
        int generation,
        CancellationToken cancellationToken)
    {
        if (!IsCurrentRefresh(browser, generation, cancellationToken)
            || !RefreshStates.TryGetValue(browser, out var state))
        {
            return;
        }

        lock (state.SyncRoot)
        {
            state.PendingPreviewData[FormatEndPointKey(endPoint)] = new PendingPreviewData
            {
                EndPoint = endPoint,
                PreviewData = previewData
            };
        }

        ScheduleBrowserUiFlush(browser, generation, cancellationToken);
    }

    private static void EnqueueBrowserMainThread(
        UIServerBrowser browser,
        int generation,
        CancellationToken cancellationToken,
        Action action)
    {
        var threadManager = MonoBehaviourSingleton<ThreadManager>.Instance;
        if (threadManager == null)
        {
            return;
        }

        threadManager.Enqueue(() =>
        {
            if (IsCurrentRefresh(browser, generation, cancellationToken))
            {
                action();
            }
        });
    }

    private static void ScheduleBrowserUiFlush(UIServerBrowser browser, int generation, CancellationToken cancellationToken)
    {
        if (!IsCurrentRefresh(browser, generation, cancellationToken) ||
            !RefreshStates.TryGetValue(browser, out var state))
        {
            return;
        }

        lock (state.SyncRoot)
        {
            if (state.UiFlushScheduled)
            {
                return;
            }

            state.UiFlushScheduled = true;
        }

        _ = FlushBrowserUiAsync(browser, generation, cancellationToken);
    }

    private static async Task FlushBrowserUiAsync(UIServerBrowser browser, int generation, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(UiFlushDebounceMs, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ResetBrowserUiFlush(browser);
            return;
        }

        var threadManager = MonoBehaviourSingleton<ThreadManager>.Instance;
        if (threadManager == null)
        {
            ResetBrowserUiFlush(browser);
            return;
        }

        threadManager.Enqueue(() =>
        {
            if (!RefreshStates.TryGetValue(browser, out var state))
            {
                return;
            }

            if (!IsCurrentRefresh(browser, generation, cancellationToken))
            {
                return;
            }

            List<PendingPreviewData> pendingPreviewData;
            lock (state.SyncRoot)
            {
                state.UiFlushScheduled = false;
                pendingPreviewData = state.PendingPreviewData.Values.ToList();
                state.PendingPreviewData.Clear();
            }

            foreach (var pendingPreview in pendingPreviewData)
            {
                InvokeBrowserMethod(SetServerPreviewDataMethod, browser, pendingPreview.EndPoint, pendingPreview.PreviewData);
                InvokeBrowserMethod(StyleServerMethod, browser, pendingPreview.EndPoint);
            }

            InvokeBrowserMethod(FilterServersMethod, browser);
            InvokeBrowserMethod(SortServersMethod, browser);
        });
    }

    private static void ResetBrowserUiFlush(UIServerBrowser browser)
    {
        if (RefreshStates.TryGetValue(browser, out var state))
        {
            lock (state.SyncRoot)
            {
                state.UiFlushScheduled = false;
            }
        }
    }

    private static bool IsCurrentRefresh(UIServerBrowser browser, int generation, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return RefreshStates.TryGetValue(browser, out var state) &&
               state.Generation == generation &&
               state.CancellationSource != null &&
               !state.CancellationSource.IsCancellationRequested;
    }

    private static void SetRefreshEnabled(UIServerBrowser browser, bool enabled)
    {
        if (RefreshButtonField?.GetValue(browser) is Button refreshButton)
        {
            refreshButton.SetEnabled(enabled);
        }
    }

    private static void InvokeBrowserMethod(MethodInfo? methodInfo, UIServerBrowser browser, params object[] args)
    {
        methodInfo?.Invoke(browser, args);
    }
}
