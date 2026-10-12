#nullable disable

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace OPLServerBanners;

internal sealed class ZoneOverlayClient : MonoBehaviour
{
    private const float OverlayY = 0.025f;
    private const float HotkeyCooldownSeconds = 0.25f;
    private const float RetryIntervalSeconds = 0.75f;
    private const float BaseWorldLabelCharacterSize = 0.04f;
    private const float BaseWorldLabelHaloOffset = 0.024f;
    private const string WorldRootName = "DomaZoneOverlayWorld";
    private const string MinimapOverlayName = "DomaZoneOverlayMinimap";

    private static readonly Vector2[] WorldLabelHaloDirections =
    {
        new(-1f, 0f),
        new(1f, 0f),
        new(0f, -1f),
        new(0f, 1f),
        new(-0.7071f, -0.7071f),
        new(-0.7071f, 0.7071f),
        new(0.7071f, -0.7071f),
        new(0.7071f, 0.7071f)
    };

    private static readonly FieldInfo MinimapContentField =
        typeof(UIMinimap).GetField("content", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo MinimapRootField =
        typeof(UIMinimap).GetField("minimap", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo MinimapBackgroundField =
        typeof(UIMinimap).GetField("background", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo MinimapForegroundField =
        typeof(UIMinimap).GetField("foreground", BindingFlags.Instance | BindingFlags.NonPublic);

    private static ZoneOverlayClient instance;

    private readonly List<WorldZoneLabel> worldLabels = new();
    private GameObject worldRoot;
    private Mesh worldMesh;
    private MeshFilter worldMeshFilter;
    private Material worldMaterial;
    private InputAction toggleAction;
    private ZoneMinimapOverlayElement minimapZoneOverlay;
    private ZoneMinimapOverlayElement minimapLabelOverlay;
    private VisualElement minimapZoneParent;
    private VisualElement minimapLabelParent;
    private UnityEngine.Object currentLevel;
    private string activeToggleBinding = string.Empty;
    private PlayerTeam localTeam = PlayerTeam.None;
    private float appliedOutlineThickness = -1f;
    private float lastToggleAt;
    private float nextRetryAt;
    private bool settingsDirty = true;

    public static void Initialize()
    {
        if (instance != null)
        {
            return;
        }

        ZoneOverlaySettings.Load();
        GameObject host = new("OPLServerBanners.ZoneOverlayClient");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<ZoneOverlayClient>();
    }

    public static void Shutdown()
    {
        if (instance == null)
        {
            return;
        }

        Destroy(instance.gameObject);
        instance = null;
    }

    private void Awake()
    {
        ZoneOverlaySettings.Changed += OnSettingsChanged;
        RecreateToggleAction();
        EventManager.AddEventListener("Event_Everyone_OnLevelSpawned", OnLevelSpawned);
        EventManager.AddEventListener("Event_Everyone_OnLevelDespawned", OnLevelDespawned);
    }

    private void OnDestroy()
    {
        ZoneOverlaySettings.Changed -= OnSettingsChanged;
        EventManager.RemoveEventListener("Event_Everyone_OnLevelSpawned", OnLevelSpawned);
        EventManager.RemoveEventListener("Event_Everyone_OnLevelDespawned", OnLevelDespawned);
        DisposeToggleAction();
        DestroyWorldOverlay();
        RemoveMinimapOverlay();
        ZoneOverlaySettingsUI.CancelKeyListening();
    }

    private void Update()
    {
        ZoneOverlaySettingsUI.TickKeyListening();
        HandleToggleHotkey();
        RefreshLocalTeam();
        EnsureUiHooks();
        EnsureLevelOverlay();
        EnsureMinimapOverlay();
        ApplyVisibility();
        UpdateWorldLabelBillboards();
    }

    private void OnSettingsChanged()
    {
        settingsDirty = true;
        if (!string.Equals(activeToggleBinding, ZoneOverlaySettings.ToggleBinding, StringComparison.Ordinal))
        {
            RecreateToggleAction();
        }
    }

    private void OnLevelSpawned(Dictionary<string, object> message)
    {
        if (message != null && message.TryGetValue("level", out object levelObject))
        {
            currentLevel = levelObject as UnityEngine.Object;
        }

        BuildWorldOverlay();
    }

    private void OnLevelDespawned(Dictionary<string, object> message)
    {
        if (message == null || !message.TryGetValue("level", out object levelObject) || ReferenceEquals(levelObject, currentLevel))
        {
            currentLevel = null;
            DestroyWorldOverlay();
        }
    }

    private void RefreshLocalTeam()
    {
        PlayerTeam nextTeam = GetLocalPlayerTeam();
        if (nextTeam == localTeam)
        {
            return;
        }

        localTeam = nextTeam;
        settingsDirty = true;
    }

    private static PlayerTeam GetLocalPlayerTeam()
    {
        try
        {
            Player localPlayer = MonoBehaviourSingleton<PlayerManager>.Instance?.GetLocalPlayer();
            if (localPlayer == null)
            {
                return PlayerTeam.None;
            }

            return localPlayer.Team;
        }
        catch
        {
            return PlayerTeam.None;
        }
    }

    private void EnsureUiHooks()
    {
        if (Time.unscaledTime < nextRetryAt)
        {
            return;
        }

        nextRetryAt = Time.unscaledTime + RetryIntervalSeconds;
        UISettings settings = MonoBehaviourSingleton<UIManager>.Instance?.Settings;
        if (settings != null)
        {
            SettingsPresetTabUI.Inject(settings);
        }
    }

    private void EnsureLevelOverlay()
    {
        if (worldRoot != null)
        {
            return;
        }

        if (currentLevel == null)
        {
            currentLevel = FindFirstObjectByType<Puck>();
        }

        if (currentLevel != null)
        {
            BuildWorldOverlay();
        }
    }

    private void BuildWorldOverlay()
    {
        DestroyWorldOverlay();

        worldRoot = new GameObject(WorldRootName);
        worldRoot.transform.SetParent(transform, false);

        MeshFilter meshFilter = worldRoot.AddComponent<MeshFilter>();
        worldMeshFilter = meshFilter;
        MeshRenderer meshRenderer = worldRoot.AddComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        worldMaterial = CreateWorldMaterial();
        meshRenderer.sharedMaterial = worldMaterial;

        RebuildWorldMesh();

        GameObject labelsRoot = new("Labels");
        labelsRoot.transform.SetParent(worldRoot.transform, false);
        foreach (ZoneOverlayDefinitions.ZoneDefinition zone in ZoneOverlayDefinitions.Zones)
        {
            worldLabels.Add(CreateWorldLabel(labelsRoot.transform, zone));
        }

        ApplyDynamicSettings();
        ApplyVisibility();
    }

    private static Material CreateWorldMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Standard");
        Material material = new(shader);
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", Color.white);
        }

        material.renderQueue = 3000;
        if (material.HasProperty("_Cull"))
        {
            material.SetInt("_Cull", (int)CullMode.Off);
        }

        return material;
    }

    private Mesh BuildZoneMesh()
    {
        List<Vector3> vertices = new();
        List<Color> colors = new();
        List<int> triangles = new();
        float outlineThickness = ZoneOverlaySettings.OutlineThickness;

        foreach (ZoneOverlayDefinitions.ZoneDefinition zone in ZoneOverlayDefinitions.Zones)
        {
            int startIndex = vertices.Count;
            Color fillColor = WithAlpha(ZoneOverlayDefinitions.GetDisplayFillColor(zone, localTeam), ZoneOverlaySettings.ZoneOpacity);
            for (int i = 0; i < zone.Polygon.Length; i++)
            {
                Vector2 point = zone.Polygon[i];
                vertices.Add(new Vector3(point.x, OverlayY, point.y));
                colors.Add(fillColor);
            }

            for (int i = 0; i < zone.Triangles.Length; i++)
            {
                triangles.Add(startIndex + zone.Triangles[i]);
            }

            AddWorldOutlineGeometry(vertices, colors, triangles, zone, localTeam, outlineThickness);
        }

        Mesh mesh = new()
        {
            name = "DomaZoneOverlayMesh"
        };
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);
        return mesh;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        return new Color(color.r, color.g, color.b, alpha);
    }

    private static void AddWorldOutlineGeometry(
        List<Vector3> vertices,
        List<Color> colors,
        List<int> triangles,
        ZoneOverlayDefinitions.ZoneDefinition zone,
        PlayerTeam localTeam,
        float outlineThickness)
    {
        Vector2[] outer = zone.Polygon;
        if (outer.Length < 3 || outlineThickness <= 0.001f)
        {
            return;
        }

        Vector2[] inner = BuildOutlineInnerPolygon(outer, outlineThickness);
        if (inner.Length != outer.Length)
        {
            return;
        }

        Color outlineColor = WithAlpha(ZoneOverlayDefinitions.GetDisplayOutlineColor(zone, localTeam), ZoneOverlaySettings.ZoneOpacity);
        for (int i = 0; i < outer.Length; i++)
        {
            int next = (i + 1) % outer.Length;
            int start = vertices.Count;
            vertices.Add(new Vector3(outer[i].x, OverlayY + 0.002f, outer[i].y));
            vertices.Add(new Vector3(outer[next].x, OverlayY + 0.002f, outer[next].y));
            vertices.Add(new Vector3(inner[next].x, OverlayY + 0.002f, inner[next].y));
            vertices.Add(new Vector3(inner[i].x, OverlayY + 0.002f, inner[i].y));

            for (int colorIndex = 0; colorIndex < 4; colorIndex++)
            {
                colors.Add(outlineColor);
            }

            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }
    }

    private static Vector2[] BuildOutlineInnerPolygon(IReadOnlyList<Vector2> polygon, float thickness)
    {
        if (polygon == null || polygon.Count < 3 || thickness <= 0.001f)
        {
            return Array.Empty<Vector2>();
        }

        int count = polygon.Count;
        bool isCounterClockwise = SignedArea(polygon) >= 0f;
        Vector2[] normals = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % count];
            Vector2 edge = b - a;
            float length = edge.magnitude;
            if (length < 0.0001f)
            {
                normals[i] = Vector2.zero;
                continue;
            }

            Vector2 leftNormal = new Vector2(-edge.y, edge.x) / length;
            normals[i] = (isCounterClockwise ? leftNormal : -leftNormal) * thickness;
        }

        Vector2[] inner = new Vector2[count];
        float maxMiterLengthSqr = thickness * thickness * 16f;
        for (int i = 0; i < count; i++)
        {
            int previous = (i + count - 1) % count;
            int next = (i + 1) % count;
            Vector2 previousA = polygon[previous] + normals[previous];
            Vector2 previousB = polygon[i] + normals[previous];
            Vector2 currentA = polygon[i] + normals[i];
            Vector2 currentB = polygon[next] + normals[i];

            if (TryIntersectLines(previousA, previousB, currentA, currentB, out Vector2 intersection)
                && IsFinite(intersection)
                && (intersection - polygon[i]).sqrMagnitude <= maxMiterLengthSqr)
            {
                inner[i] = intersection;
                continue;
            }

            inner[i] = GetFallbackInsetPoint(polygon[i], normals[previous], normals[i], thickness);
        }

        return inner;
    }

    private static Vector2 GetFallbackInsetPoint(Vector2 point, Vector2 previousNormal, Vector2 currentNormal, float thickness)
    {
        Vector2 offset = previousNormal + currentNormal;
        if (offset.sqrMagnitude < 0.000001f)
        {
            offset = currentNormal.sqrMagnitude > 0.000001f ? currentNormal : previousNormal;
        }

        if (offset.sqrMagnitude < 0.000001f)
        {
            return point;
        }

        return point + offset.normalized * thickness;
    }

    private static bool TryIntersectLines(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, out Vector2 intersection)
    {
        Vector2 r = a2 - a1;
        Vector2 s = b2 - b1;
        float denominator = Cross(r, s);
        if (Mathf.Abs(denominator) < 0.000001f)
        {
            intersection = Vector2.zero;
            return false;
        }

        float t = Cross(b1 - a1, s) / denominator;
        intersection = a1 + r * t;
        return true;
    }

    private static bool IsFinite(Vector2 value)
    {
        return !float.IsNaN(value.x)
               && !float.IsNaN(value.y)
               && !float.IsInfinity(value.x)
               && !float.IsInfinity(value.y);
    }

    private static float SignedArea(IReadOnlyList<Vector2> polygon)
    {
        float area = 0f;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            area += a.x * b.y - a.y * b.x;
        }

        return area * 0.5f;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private WorldZoneLabel CreateWorldLabel(Transform parent, ZoneOverlayDefinitions.ZoneDefinition zone)
    {
        string labelText = ZoneOverlayDefinitions.GetDisplayName(zone, localTeam);
        Color textColor = ZoneOverlayDefinitions.GetDisplayTextColor(zone, localTeam);
        Color haloColor = ZoneOverlayDefinitions.GetDisplayTextOutlineColor(zone, localTeam);
        GameObject root = new("ZoneLabel_" + zone.Id);
        root.transform.SetParent(parent, false);
        root.transform.position = zone.LabelPosition;

        List<Transform> haloTransforms = new(WorldLabelHaloDirections.Length);
        List<TextMesh> textMeshes = new(WorldLabelHaloDirections.Length + 1);
        for (int i = 0; i < WorldLabelHaloDirections.Length; i++)
        {
            TextMesh halo = CreateTextMesh(root.transform, labelText, haloColor, Vector3.zero, 0);
            haloTransforms.Add(halo.transform);
            textMeshes.Add(halo);
        }

        textMeshes.Add(CreateTextMesh(root.transform, labelText, textColor, Vector3.zero, 1));

        return new WorldZoneLabel(root, zone.Id, haloTransforms.ToArray(), textMeshes.ToArray());
    }

    private static TextMesh CreateTextMesh(Transform parent, string text, Color color, Vector3 localPosition, int sortingOrder)
    {
        GameObject textObject = new("Text");
        textObject.transform.SetParent(parent, false);
        textObject.transform.localPosition = localPosition;
        TextMesh textMesh = textObject.AddComponent<TextMesh>();
        textMesh.text = text;
        textMesh.color = color;
        textMesh.fontSize = 64;
        textMesh.characterSize = BaseWorldLabelCharacterSize;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.fontStyle = FontStyle.Bold;
        MeshRenderer renderer = textObject.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = sortingOrder;
        }

        return textMesh;
    }

    private void ApplyVisibility()
    {
        if (settingsDirty)
        {
            ApplyDynamicSettings();
            settingsDirty = false;
        }

        bool enabled = ZoneOverlaySettings.Enabled;
        bool showWorld = enabled && ZoneOverlaySettings.ShowOnWorld;
        bool showMinimap = enabled && ZoneOverlaySettings.ShowOnMinimap;
        if (worldRoot != null)
        {
            worldRoot.SetActive(showWorld);
        }

        bool showWorldLabels = showWorld && ZoneOverlaySettings.ShowWorldZoneLabels;
        for (int i = 0; i < worldLabels.Count; i++)
        {
            worldLabels[i].Root.SetActive(showWorldLabels);
        }

        if (minimapZoneOverlay != null)
        {
            minimapZoneOverlay.style.display = showMinimap ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (minimapLabelOverlay != null)
        {
            minimapLabelOverlay.style.display = showMinimap ? DisplayStyle.Flex : DisplayStyle.None;
            minimapLabelOverlay.SetLabelsVisible(showMinimap && ZoneOverlaySettings.ShowMinimapZoneLabels);
        }

    }

    private void ApplyDynamicSettings()
    {
        if (worldMesh != null && Mathf.Abs(appliedOutlineThickness - ZoneOverlaySettings.OutlineThickness) > 0.001f)
        {
            RebuildWorldMesh();
        }
        else
        {
            ApplyWorldMeshOpacity();
        }

        ApplyWorldLabelDisplay();
        ApplyWorldLabelSize();
        minimapZoneOverlay?.MarkDirtyRepaint();
        minimapLabelOverlay?.RefreshLabels();
        minimapLabelOverlay?.MarkDirtyRepaint();
    }

    private void RebuildWorldMesh()
    {
        if (worldMeshFilter == null)
        {
            return;
        }

        if (worldMesh != null)
        {
            Destroy(worldMesh);
        }

        worldMesh = BuildZoneMesh();
        worldMeshFilter.sharedMesh = worldMesh;
        appliedOutlineThickness = ZoneOverlaySettings.OutlineThickness;
    }

    private void ApplyWorldMeshOpacity()
    {
        if (worldMesh == null)
        {
            return;
        }

        List<Color> colors = new();
        foreach (ZoneOverlayDefinitions.ZoneDefinition zone in ZoneOverlayDefinitions.Zones)
        {
            Color fillColor = WithAlpha(ZoneOverlayDefinitions.GetDisplayFillColor(zone, localTeam), ZoneOverlaySettings.ZoneOpacity);
            for (int i = 0; i < zone.Polygon.Length; i++)
            {
                colors.Add(fillColor);
            }

            Color outlineColor = WithAlpha(ZoneOverlayDefinitions.GetDisplayOutlineColor(zone, localTeam), ZoneOverlaySettings.ZoneOpacity);
            for (int i = 0; i < zone.Polygon.Length * 4; i++)
            {
                colors.Add(outlineColor);
            }
        }

        worldMesh.SetColors(colors);
    }

    private void ApplyWorldLabelDisplay()
    {
        for (int i = 0; i < worldLabels.Count && i < ZoneOverlayDefinitions.Zones.Length; i++)
        {
            WorldZoneLabel label = worldLabels[i];
            ZoneOverlayDefinitions.ZoneDefinition zone = ZoneOverlayDefinitions.Zones[i];
            string labelText = ZoneOverlayDefinitions.GetDisplayName(zone, localTeam);
            Color textColor = ZoneOverlayDefinitions.GetDisplayTextColor(zone, localTeam);
            Color haloColor = ZoneOverlayDefinitions.GetDisplayTextOutlineColor(zone, localTeam);
            for (int textIndex = 0; textIndex < label.TextMeshes.Length; textIndex++)
            {
                TextMesh textMesh = label.TextMeshes[textIndex];
                textMesh.text = labelText;
                if (textIndex < label.HaloTransforms.Length)
                {
                    textMesh.color = haloColor;
                }
                else
                {
                    textMesh.color = textColor;
                }
            }
        }
    }

    private void ApplyWorldLabelSize()
    {
        float size = ZoneOverlaySettings.WorldTextSize;
        float characterSize = BaseWorldLabelCharacterSize * size;
        float haloOffset = BaseWorldLabelHaloOffset * size;
        for (int i = 0; i < worldLabels.Count; i++)
        {
            WorldZoneLabel label = worldLabels[i];
            label.Root.transform.localScale = Vector3.one;
            for (int textIndex = 0; textIndex < label.TextMeshes.Length; textIndex++)
            {
                label.TextMeshes[textIndex].characterSize = characterSize;
            }

            for (int haloIndex = 0; haloIndex < label.HaloTransforms.Length; haloIndex++)
            {
                Vector2 direction = WorldLabelHaloDirections[haloIndex];
                label.HaloTransforms[haloIndex].localPosition = new Vector3(direction.x * haloOffset, direction.y * haloOffset, 0.01f);
            }
        }
    }

    private void UpdateWorldLabelBillboards()
    {
        if (!ZoneOverlaySettings.Enabled || !ZoneOverlaySettings.ShowOnWorld || !ZoneOverlaySettings.ShowWorldZoneLabels || worldLabels.Count == 0)
        {
            return;
        }

        Camera camera = GetActiveCamera();
        if (camera == null)
        {
            return;
        }

        Vector3 cameraPosition = camera.transform.position;
        for (int i = 0; i < worldLabels.Count; i++)
        {
            Transform labelTransform = worldLabels[i].Root.transform;
            Vector3 direction = labelTransform.position - cameraPosition;
            if (direction.sqrMagnitude > 0.001f)
            {
                labelTransform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
        }
    }

    private static Camera GetActiveCamera()
    {
        try
        {
            Player localPlayer = MonoBehaviourSingleton<PlayerManager>.Instance?.GetLocalPlayer();
            Camera playerCamera = localPlayer?.PlayerCamera?.UnityCamera;
            if (playerCamera != null && playerCamera.enabled)
            {
                return playerCamera;
            }
        }
        catch
        {
        }

        return Camera.main;
    }

    private void EnsureMinimapOverlay()
    {
        UIMinimap minimap = MonoBehaviourSingleton<UIManager>.Instance?.Minimap;
        if (minimap == null || !ZoneOverlaySettings.ShowOnMinimap)
        {
            RemoveMinimapOverlay();
            return;
        }

        VisualElement content = MinimapContentField?.GetValue(minimap) as VisualElement;
        if (content == null)
        {
            return;
        }

        VisualElement minimapRoot = MinimapRootField?.GetValue(minimap) as VisualElement;
        VisualElement background = MinimapBackgroundField?.GetValue(minimap) as VisualElement;
        VisualElement foreground = MinimapForegroundField?.GetValue(minimap) as VisualElement;
        VisualElement zoneParent = GetMinimapZoneParent(content, foreground, minimapRoot);
        if (minimapZoneOverlay == null
            || minimapZoneOverlay.parent != zoneParent
            || minimapLabelOverlay == null
            || minimapLabelOverlay.parent != content)
        {
            RemoveMinimapOverlay();
            minimapZoneParent = zoneParent;
            minimapZoneOverlay = new ZoneMinimapOverlayElement(renderZones: true, renderLabels: false)
            {
                name = MinimapOverlayName + "Zones",
                pickingMode = PickingMode.Ignore
            };
            StyleMinimapOverlay(minimapZoneOverlay);
            InsertMinimapZoneOverlay(zoneParent, background, minimapZoneOverlay);

            minimapLabelParent = content;
            minimapLabelOverlay = new ZoneMinimapOverlayElement(renderZones: false, renderLabels: true)
            {
                name = MinimapOverlayName + "Labels",
                pickingMode = PickingMode.Ignore
            };
            StyleMinimapOverlay(minimapLabelOverlay);
            content.Add(minimapLabelOverlay);
            minimapLabelOverlay.BringToFront();
        }

        MinimapVisualTransform rootTransform = GetElementVisualTransform(minimapRoot);
        MinimapVisualTransform contentTransform = GetElementVisualTransform(content);
        MinimapVisualTransform foregroundTransform = GetElementVisualTransform(foreground);
        MinimapVisualTransform backgroundTransform = GetElementVisualTransform(background);
        MinimapVisualTransform mapLayerTransform = SelectMapLayerTransform(
            contentTransform,
            foregroundTransform,
            backgroundTransform,
            out bool mapLayerIsContent);
        MinimapVisualTransform identityTransform = IdentityMinimapTransform();
        MinimapVisualTransform zoneOverlayTransform = zoneParent == content ? identityTransform : mapLayerTransform;
        MinimapVisualTransform labelOverlayTransform = mapLayerIsContent ? identityTransform : mapLayerTransform;
        float labelCounterRotationDegrees = -(
            rootTransform.RotationDegrees
            + contentTransform.RotationDegrees
            + labelOverlayTransform.RotationDegrees);

        MatchMinimapOverlayContentBox(minimapZoneOverlay, zoneParent, content);
        SetElementTransform(minimapZoneOverlay, zoneOverlayTransform);
        SetElementTransform(minimapLabelOverlay, labelOverlayTransform);
        ArrangeMinimapLayers(content, foreground, minimapZoneOverlay);
        minimapLabelOverlay.BringToFront();

        Bounds bounds = minimap.Bounds;
        if (bounds.size.x <= Mathf.Epsilon || bounds.size.z <= Mathf.Epsilon)
        {
            bounds = new Bounds(
                Vector3.zero,
                new Vector3(
                    ZoneOverlayDefinitions.MaxX - ZoneOverlayDefinitions.MinX,
                    0f,
                    ZoneOverlayDefinitions.MaxZ - ZoneOverlayDefinitions.MinZ));
        }

        minimapZoneOverlay.SetState(
            minimap.Team,
            bounds,
            false,
            ZoneOverlaySettings.ZoneOpacity,
            ZoneOverlaySettings.MinimapTextSize,
            ZoneOverlaySettings.OutlineThickness,
            0f);
        minimapLabelOverlay.SetState(
            minimap.Team,
            bounds,
            ZoneOverlaySettings.ShowMinimapZoneLabels,
            ZoneOverlaySettings.ZoneOpacity,
            ZoneOverlaySettings.MinimapTextSize,
            ZoneOverlaySettings.OutlineThickness,
            labelCounterRotationDegrees);
        bool visible = ZoneOverlaySettings.Enabled && ZoneOverlaySettings.ShowOnMinimap;
        minimapZoneOverlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        minimapLabelOverlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static void StyleMinimapOverlay(VisualElement overlay)
    {
        overlay.style.position = Position.Absolute;
        overlay.style.left = 0f;
        overlay.style.top = 0f;
        overlay.style.right = 0f;
        overlay.style.bottom = 0f;
        overlay.style.backgroundColor = Color.clear;
        overlay.style.transformOrigin = CenterTransformOrigin();
    }

    private static void SetElementTransform(VisualElement element, MinimapVisualTransform transform)
    {
        if (element == null)
        {
            return;
        }

        element.style.rotate = new Rotate(transform.RotationDegrees);
        element.style.translate = transform.Translate;
        element.style.transformOrigin = transform.TransformOrigin;
    }

    private static float GetElementRotationDegrees(VisualElement element)
    {
        if (element == null)
        {
            return 0f;
        }

        return AngleToDegrees(element.resolvedStyle.rotate.angle);
    }

    private static MinimapVisualTransform GetElementVisualTransform(VisualElement element)
    {
        if (element == null)
        {
            return IdentityMinimapTransform();
        }

        return new MinimapVisualTransform(
            GetElementRotationDegrees(element),
            element.resolvedStyle.translate,
            GetElementTransformOrigin(element));
    }

    private static TransformOrigin GetElementTransformOrigin(VisualElement element)
    {
        if (element == null || element.style.transformOrigin.keyword == StyleKeyword.Undefined)
        {
            return CenterTransformOrigin();
        }

        return element.style.transformOrigin.value;
    }

    private static MinimapVisualTransform SelectMapLayerTransform(
        MinimapVisualTransform contentTransform,
        MinimapVisualTransform foregroundTransform,
        MinimapVisualTransform backgroundTransform,
        out bool mapLayerIsContent)
    {
        mapLayerIsContent = true;
        if (Mathf.Abs(contentTransform.RotationDegrees) > 0.001f)
        {
            return contentTransform;
        }

        if (Mathf.Abs(foregroundTransform.RotationDegrees) > 0.001f)
        {
            mapLayerIsContent = false;
            return foregroundTransform;
        }

        if (Mathf.Abs(backgroundTransform.RotationDegrees) > 0.001f)
        {
            mapLayerIsContent = false;
            return backgroundTransform;
        }

        return contentTransform;
    }

    private static MinimapVisualTransform IdentityMinimapTransform()
    {
        return new MinimapVisualTransform(
            0f,
            new Translate(0f, 0f),
            CenterTransformOrigin());
    }

    private static float AngleToDegrees(Angle angle)
    {
        return angle.unit switch
        {
            AngleUnit.Radian => angle.value * Mathf.Rad2Deg,
            AngleUnit.Turn => angle.value * 360f,
            AngleUnit.Gradian => angle.value * 0.9f,
            _ => angle.value
        };
    }

    private static TransformOrigin CenterTransformOrigin()
    {
        return new TransformOrigin(new Length(50f, LengthUnit.Percent), new Length(50f, LengthUnit.Percent));
    }

    private static VisualElement GetMinimapZoneParent(VisualElement content, VisualElement foreground, VisualElement minimapRoot)
    {
        if (foreground?.parent != null && content?.parent == foreground.parent)
        {
            return foreground.parent;
        }

        return minimapRoot ?? content;
    }

    private static void MatchMinimapOverlayContentBox(VisualElement overlay, VisualElement zoneParent, VisualElement content)
    {
        if (overlay == null || content == null)
        {
            return;
        }

        if (zoneParent == content)
        {
            overlay.style.left = 0f;
            overlay.style.top = 0f;
            overlay.style.right = 0f;
            overlay.style.bottom = 0f;
            return;
        }

        Rect contentLayout = content.layout;
        float width = content.resolvedStyle.width;
        float height = content.resolvedStyle.height;
        if (float.IsNaN(width) || width <= 0f)
        {
            width = contentLayout.width;
        }

        if (float.IsNaN(height) || height <= 0f)
        {
            height = contentLayout.height;
        }

        if (float.IsNaN(width) || width <= 0f || float.IsNaN(height) || height <= 0f)
        {
            overlay.style.left = 0f;
            overlay.style.top = 0f;
            overlay.style.right = 0f;
            overlay.style.bottom = 0f;
            return;
        }

        overlay.style.left = contentLayout.x;
        overlay.style.top = contentLayout.y;
        overlay.style.right = StyleKeyword.Auto;
        overlay.style.bottom = StyleKeyword.Auto;
        overlay.style.width = width;
        overlay.style.height = height;
    }

    private static void InsertMinimapZoneOverlay(VisualElement parent, VisualElement background, VisualElement overlay)
    {
        if (background != null && background.parent == parent)
        {
            parent.Insert(parent.IndexOf(background) + 1, overlay);
            return;
        }

        parent.Insert(0, overlay);
    }

    private static void ArrangeMinimapLayers(VisualElement content, VisualElement foreground, VisualElement zoneOverlay)
    {
        if (zoneOverlay == null)
        {
            return;
        }

        VisualElement parent = zoneOverlay.parent;
        if (parent == null)
        {
            return;
        }

        if (foreground != null && foreground.parent == parent)
        {
            MoveChildBefore(parent, zoneOverlay, foreground);
        }

        if (content != null && content.parent == parent)
        {
            content.BringToFront();
        }
    }

    private static void MoveChildBefore(VisualElement parent, VisualElement child, VisualElement before)
    {
        if (parent == null || child == null || before == null || child.parent != parent || before.parent != parent || ReferenceEquals(child, before))
        {
            return;
        }

        int beforeIndex = parent.IndexOf(before);
        int childIndex = parent.IndexOf(child);
        if (beforeIndex < 0 || childIndex < 0 || childIndex < beforeIndex)
        {
            return;
        }

        child.RemoveFromHierarchy();
        beforeIndex = parent.IndexOf(before);
        parent.Insert(Mathf.Max(0, beforeIndex), child);
    }

    private void RemoveMinimapOverlay()
    {
        if (minimapZoneOverlay != null && minimapZoneOverlay.parent != null)
        {
            minimapZoneOverlay.RemoveFromHierarchy();
        }

        if (minimapLabelOverlay != null && minimapLabelOverlay.parent != null)
        {
            minimapLabelOverlay.RemoveFromHierarchy();
        }

        minimapZoneOverlay = null;
        minimapLabelOverlay = null;
        minimapZoneParent = null;
        minimapLabelParent = null;
    }

    private void HandleToggleHotkey()
    {
        if (toggleAction == null || ZoneOverlaySettingsUI.IsListeningForToggleKey)
        {
            return;
        }

        if (!toggleAction.WasPressedThisFrame() || Time.unscaledTime - lastToggleAt < HotkeyCooldownSeconds)
        {
            return;
        }

        if (IsUiBlockingHotkey())
        {
            return;
        }

        lastToggleAt = Time.unscaledTime;
        ZoneOverlaySettings.ToggleEnabled();
    }

    private static bool IsUiBlockingHotkey()
    {
        try
        {
            UIManager uiManager = MonoBehaviourSingleton<UIManager>.Instance;
            if (uiManager == null)
            {
                return false;
            }

            return uiManager.Chat?.IsFocused == true
                   || uiManager.Settings?.IsVisible == true
                   || uiManager.Appearance?.IsVisible == true
                   || uiManager.MainMenu?.IsVisible == true
                   || uiManager.PauseMenu?.IsVisible == true;
        }
        catch
        {
            return false;
        }
    }

    private void RecreateToggleAction()
    {
        DisposeToggleAction();
        try
        {
            string binding = ZoneOverlaySettings.ToggleBinding;
            activeToggleBinding = binding;
            if (string.IsNullOrWhiteSpace(binding))
            {
                return;
            }

            toggleAction = new InputAction(null, InputActionType.Button, binding);
            toggleAction.Enable();
        }
        catch (Exception exception)
        {
            activeToggleBinding = string.Empty;
            Plugin.LogError($"Failed to create zone overlay toggle keybind: {exception}");
        }
    }

    private void DisposeToggleAction()
    {
        if (toggleAction == null)
        {
            return;
        }

        toggleAction.Disable();
        toggleAction.Dispose();
        toggleAction = null;
    }

    private void DestroyWorldOverlay()
    {
        worldLabels.Clear();
        if (worldRoot != null)
        {
            Destroy(worldRoot);
            worldRoot = null;
        }

        if (worldMesh != null)
        {
            Destroy(worldMesh);
            worldMesh = null;
        }

        worldMeshFilter = null;
        appliedOutlineThickness = -1f;

        if (worldMaterial != null)
        {
            Destroy(worldMaterial);
            worldMaterial = null;
        }
    }

    private sealed class WorldZoneLabel
    {
        public readonly GameObject Root;
        public readonly string ZoneId;
        public readonly Transform[] HaloTransforms;
        public readonly TextMesh[] TextMeshes;

        public WorldZoneLabel(GameObject root, string zoneId, Transform[] haloTransforms, TextMesh[] textMeshes)
        {
            Root = root;
            ZoneId = zoneId;
            HaloTransforms = haloTransforms;
            TextMeshes = textMeshes;
        }
    }

    private readonly struct MinimapVisualTransform
    {
        public readonly float RotationDegrees;
        public readonly Translate Translate;
        public readonly TransformOrigin TransformOrigin;

        public MinimapVisualTransform(float rotationDegrees, Translate translate, TransformOrigin transformOrigin)
        {
            RotationDegrees = rotationDegrees;
            Translate = translate;
            TransformOrigin = transformOrigin;
        }
    }

    private sealed class ZoneMinimapOverlayElement : VisualElement
    {
        private readonly List<Label> labels = new();
        private readonly bool renderZones;
        private readonly bool renderLabels;
        private PlayerTeam team = PlayerTeam.None;
        private Bounds bounds;
        private bool showLabels = true;
        private float zoneOpacity = ZoneOverlaySettings.DefaultZoneOpacity;
        private float labelFontSize = ZoneOverlaySettings.DefaultMinimapTextSize;
        private float outlineThickness = ZoneOverlaySettings.DefaultOutlineThickness;
        private float labelCounterRotationDegrees;

        public ZoneMinimapOverlayElement(bool renderZones, bool renderLabels)
        {
            this.renderZones = renderZones;
            this.renderLabels = renderLabels;
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<GeometryChangedEvent>(_ => UpdateLabelPositions());
            EnsureLabels();
        }

        public void SetState(
            PlayerTeam nextTeam,
            Bounds nextBounds,
            bool nextShowLabels,
            float nextZoneOpacity,
            float nextLabelFontSize,
            float nextOutlineThickness,
            float nextLabelCounterRotationDegrees)
        {
            bool dirty = team != nextTeam
                         || bounds.center != nextBounds.center
                         || bounds.size != nextBounds.size
                         || showLabels != nextShowLabels
                         || Mathf.Abs(zoneOpacity - nextZoneOpacity) > 0.001f
                         || Mathf.Abs(labelFontSize - nextLabelFontSize) > 0.001f
                         || Mathf.Abs(outlineThickness - nextOutlineThickness) > 0.001f
                         || Mathf.Abs(Mathf.DeltaAngle(labelCounterRotationDegrees, nextLabelCounterRotationDegrees)) > 0.001f;
            team = nextTeam;
            bounds = nextBounds;
            showLabels = nextShowLabels;
            zoneOpacity = nextZoneOpacity;
            labelFontSize = nextLabelFontSize;
            outlineThickness = nextOutlineThickness;
            labelCounterRotationDegrees = nextLabelCounterRotationDegrees;

            if (dirty)
            {
                UpdateLabelDisplay();
                UpdateLabelStyles();
                MarkDirtyRepaint();
                UpdateLabelPositions();
            }
        }

        public void SetLabelsVisible(bool visible)
        {
            if (!renderLabels)
            {
                return;
            }

            showLabels = visible;
            UpdateLabelPositions();
        }

        public void RefreshLabels()
        {
            if (!renderLabels)
            {
                return;
            }

            UpdateLabelDisplay();
            UpdateLabelStyles();
            UpdateLabelPositions();
        }

        private void EnsureLabels()
        {
            if (!renderLabels)
            {
                return;
            }

            if (labels.Count == ZoneOverlayDefinitions.Zones.Length)
            {
                return;
            }

            Clear();
            labels.Clear();
            foreach (ZoneOverlayDefinitions.ZoneDefinition zone in ZoneOverlayDefinitions.Zones)
            {
                Label label = new(ZoneOverlayDefinitions.GetDisplayName(zone, team))
                {
                    pickingMode = PickingMode.Ignore
                };
                label.style.position = Position.Absolute;
                label.style.color = ZoneOverlayDefinitions.GetDisplayTextColor(zone, team);
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.whiteSpace = WhiteSpace.NoWrap;
                label.style.paddingLeft = 0f;
                label.style.paddingRight = 0f;
                label.style.paddingTop = 0f;
                label.style.paddingBottom = 0f;
                label.style.unityTextOutlineColor = ZoneOverlayDefinitions.GetDisplayTextOutlineColor(zone, team);
                Add(label);
                labels.Add(label);
            }

            UpdateLabelDisplay();
            UpdateLabelStyles();
        }

        private void UpdateLabelDisplay()
        {
            if (!renderLabels)
            {
                return;
            }

            EnsureLabels();
            for (int i = 0; i < labels.Count && i < ZoneOverlayDefinitions.Zones.Length; i++)
            {
                ZoneOverlayDefinitions.ZoneDefinition zone = ZoneOverlayDefinitions.Zones[i];
                labels[i].text = ZoneOverlayDefinitions.GetDisplayName(zone, team);
                labels[i].style.color = ZoneOverlayDefinitions.GetDisplayTextColor(zone, team);
                labels[i].style.unityTextOutlineColor = ZoneOverlayDefinitions.GetDisplayTextOutlineColor(zone, team);
            }
        }

        private void UpdateLabelStyles()
        {
            if (!renderLabels)
            {
                return;
            }

            float outlineWidth = Mathf.Max(1f, labelFontSize * 0.16f);
            for (int i = 0; i < labels.Count; i++)
            {
                labels[i].style.fontSize = labelFontSize;
                labels[i].style.unityTextOutlineWidth = outlineWidth;
            }
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            if (!renderZones)
            {
                return;
            }

            Rect rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            foreach (ZoneOverlayDefinitions.ZoneDefinition zone in ZoneOverlayDefinitions.Zones)
            {
                Vector2[] polygon = zone.Polygon;
                if (polygon.Length < 3)
                {
                    continue;
                }

                MeshWriteData mesh = context.Allocate(polygon.Length, zone.Triangles.Length);
                for (int i = 0; i < polygon.Length; i++)
                {
                    Vector2 point = WorldToMinimap(rect, polygon[i]);
                    mesh.SetNextVertex(new Vertex
                    {
                        position = new Vector3(point.x, point.y, Vertex.nearZ),
                        tint = WithAlpha(ZoneOverlayDefinitions.GetDisplayFillColor(zone, team), zoneOpacity),
                        uv = Vector2.zero
                    });
                }

                for (int i = 0; i < zone.Triangles.Length; i++)
                {
                    mesh.SetNextIndex((ushort)zone.Triangles[i]);
                }

                AddMinimapOutlineGeometry(context, rect, zone);
            }
        }

        private void AddMinimapOutlineGeometry(MeshGenerationContext context, Rect rect, ZoneOverlayDefinitions.ZoneDefinition zone)
        {
            Vector2[] outer = zone.Polygon;
            if (outer.Length < 3 || outlineThickness <= 0.001f)
            {
                return;
            }

            Vector2[] inner = BuildOutlineInnerPolygon(outer, outlineThickness);
            if (inner.Length != outer.Length)
            {
                return;
            }

            MeshWriteData mesh = context.Allocate(outer.Length * 4, outer.Length * 6);
            Color outlineColor = WithAlpha(ZoneOverlayDefinitions.GetDisplayOutlineColor(zone, team), zoneOpacity);
            for (int i = 0; i < outer.Length; i++)
            {
                int next = (i + 1) % outer.Length;
                Vector2 outerA = WorldToMinimap(rect, outer[i]);
                Vector2 outerB = WorldToMinimap(rect, outer[next]);
                Vector2 innerB = WorldToMinimap(rect, inner[next]);
                Vector2 innerA = WorldToMinimap(rect, inner[i]);

                mesh.SetNextVertex(new Vertex
                {
                    position = new Vector3(outerA.x, outerA.y, Vertex.nearZ),
                    tint = outlineColor,
                    uv = Vector2.zero
                });
                mesh.SetNextVertex(new Vertex
                {
                    position = new Vector3(outerB.x, outerB.y, Vertex.nearZ),
                    tint = outlineColor,
                    uv = Vector2.zero
                });
                mesh.SetNextVertex(new Vertex
                {
                    position = new Vector3(innerB.x, innerB.y, Vertex.nearZ),
                    tint = outlineColor,
                    uv = Vector2.zero
                });
                mesh.SetNextVertex(new Vertex
                {
                    position = new Vector3(innerA.x, innerA.y, Vertex.nearZ),
                    tint = outlineColor,
                    uv = Vector2.zero
                });

                ushort start = (ushort)(i * 4);
                mesh.SetNextIndex(start);
                mesh.SetNextIndex((ushort)(start + 1));
                mesh.SetNextIndex((ushort)(start + 2));
                mesh.SetNextIndex(start);
                mesh.SetNextIndex((ushort)(start + 2));
                mesh.SetNextIndex((ushort)(start + 3));
            }
        }

        private void UpdateLabelPositions()
        {
            if (!renderLabels)
            {
                return;
            }

            EnsureLabels();
            Rect rect = contentRect;
            bool visible = showLabels && rect.width > 0f && rect.height > 0f;

            for (int i = 0; i < labels.Count; i++)
            {
                Label label = labels[i];
                label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible)
                {
                    continue;
                }

                Vector3 worldPosition = ZoneOverlayDefinitions.Zones[i].LabelPosition;
                Vector2 position = WorldToMinimap(rect, new Vector2(worldPosition.x, worldPosition.z));
                float labelWidth = EstimateLabelWidth(label.text, labelFontSize);
                float labelHeight = EstimateLabelHeight(labelFontSize);
                label.style.width = labelWidth;
                label.style.height = labelHeight;
                label.style.left = position.x - labelWidth * 0.5f;
                label.style.top = position.y - labelHeight * 0.5f;
                label.style.rotate = new Rotate(labelCounterRotationDegrees);
                label.style.transformOrigin = CenterTransformOrigin();
            }
        }

        private static float EstimateLabelWidth(string text, float fontSize)
        {
            return Mathf.Max(24f, (text?.Length ?? 0) * fontSize * 0.62f + fontSize * 0.8f);
        }

        private static float EstimateLabelHeight(float fontSize)
        {
            return Mathf.Max(12f, fontSize * 1.45f);
        }

        private Vector2 WorldToMinimap(Rect rect, Vector2 worldPoint)
        {
            Vector2 orientedPoint = team == PlayerTeam.Blue ? worldPoint : -worldPoint;
            float width = Mathf.Max(bounds.size.x, Mathf.Epsilon);
            float height = Mathf.Max(bounds.size.z, Mathf.Epsilon);
            float offsetX = rect.width * ((orientedPoint.x + bounds.center.x) / width);
            float offsetY = rect.height * ((orientedPoint.y + bounds.center.z) / height);
            return new Vector2(rect.center.x - offsetX, rect.center.y + offsetY);
        }
    }
}
