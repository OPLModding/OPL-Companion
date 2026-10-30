using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;

#nullable disable

namespace OPLServerBanners.OPLArena;

public static class OPLArenaIntegration
{
    private static GameObject _bootstrapObject;

    public static void Initialize()
    {
        TlanSydneyAdapter.TryInstallPatch();

        _bootstrapObject = new GameObject("SydneyTLANArenaAdapter");
        UnityEngine.Object.DontDestroyOnLoad(_bootstrapObject);
        _bootstrapObject.AddComponent<TlanSydneyBootstrap>();
    }

    public static void Shutdown()
    {
        TlanSydneyAdapter.Shutdown();

        if (_bootstrapObject != null)
        {
            UnityEngine.Object.Destroy(_bootstrapObject);
            _bootstrapObject = null;
        }
    }
}

internal sealed class TlanSydneyBootstrap : MonoBehaviour
{
    private float _nextAttempt;
    private float _nextUiCheck;

    private void Update()
    {
        if (!TlanSydneyAdapter.PatchInstalled && Time.unscaledTime >= _nextAttempt)
        {
            TlanSydneyAdapter.TryInstallPatch();
            _nextAttempt = Time.unscaledTime + 1f;
        }

        if (Time.unscaledTime >= _nextUiCheck)
        {
            TlanSydneyAdapter.TryAddOplArenaSettingsToggle();
            TlanSydneyAdapter.UpdateMenuPreview();
            _nextUiCheck = Time.unscaledTime + 0.25f;
        }
    }
}

internal static class TlanSydneyAdapter
{
    private const string HarmonyId = "doma.oplcompanion.opl.arena";
    private const string ArenaBundleTypeName = "TLANArena.Scenery.ArenaBundle";
    private const string ResolverMethodName = "ResolveAssets";

    private static Harmony _harmony;
    private static AssetBundle _sydneyBundle;
    private static GameObject _sydneyPrefab;
    private static PropertyInfo _arenaPrefabProperty;
    private static PropertyInfo _bundleFolderProperty;
    private static PropertyInfo _arenaRootProperty;
    private static SydneyAdapterConfig _adapterConfig;
    private static bool _patchInstalled;
    private static GameObject _menuBackdrop;
    private static bool _menuPreviewAttempted;
    private static readonly List<Renderer> _menuHiddenRenderers = new List<Renderer>();
    private static readonly List<bool> _menuHiddenRendererStates = new List<bool>();
    private static bool _menuRenderSettingsSaved;
    private static Material _menuOriginalSkybox;
    private static Material _menuSkyboxMaterial;
    private static AssetBundle _tlanSkyboxBundle;
    private static bool _settingsToggleAdded;
    private static bool _settingsPatchInstalled;
    private static Color _menuAmbientLight;
    private static float _menuAmbientIntensity;
    private static float _menuReflectionIntensity;
    private static bool _menuFog;
    private static Color _menuFogColor;
    private static float _menuFogDensity;
    private static float _menuFogStartDistance;
    private static float _menuFogEndDistance;
    private static TlanMenuRenderConfig _runtimeLightingConfig;
    private static Light _runtimeSun;
    private static float _nextRuntimeSunSearch;

    public static bool PatchInstalled => _patchInstalled;

    public static void UpdateMenuPreview()
    {
        bool inLockerRoom = string.Equals(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, "locker_room", StringComparison.OrdinalIgnoreCase);
        if (!inLockerRoom)
        {
            ClearMenuPreview();
            _menuPreviewAttempted = false;
            return;
        }

        if (_menuBackdrop != null || _menuPreviewAttempted)
        {
            return;
        }

        if (!IsOplArenaEnabled() || !_patchInstalled || !IsTlanEnabledAlongsideThisAdapter())
        {
            ClearMenuPreview();
            _menuPreviewAttempted = false;
            return;
        }

        if (!LoadSydneyPrefab())
        {
            return;
        }

        _menuPreviewAttempted = true;

        try
        {
            TlanMenuRenderConfig menuConfig = LoadTlanMenuRenderConfig();
            Renderer currentMenuIce = FindCurrentMenuIceRenderer();
            HideVanillaMenuArena();
            _menuBackdrop = UnityEngine.Object.Instantiate(_sydneyPrefab);
            _menuBackdrop.name = "SydneyTLANArenaMenuBackdrop";
            _menuBackdrop.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _menuBackdrop.transform.localScale = Vector3.one;

            DisableMenuPhysics(_menuBackdrop);
            DisableMenuArenaBehaviours(_menuBackdrop);
            DisableMenuShadows(_menuBackdrop);
            _menuBackdrop.AddComponent<SydneyMenuBackdropMarker>();
            ApplyCurrentMenuIceMaterial(_menuBackdrop, currentMenuIce);
            HideMenuSnapshotIce(_menuBackdrop);
            BeforeFloodlightAttach(_menuBackdrop);
            float menuFloodlightIntensity = menuConfig.GetEffectiveFloodlightIntensity(_adapterConfig ?? LoadAdapterConfig());

            SydneyLampGlow[] lampGlows = _menuBackdrop.GetComponentsInChildren<SydneyLampGlow>(true);
            for (int i = 0; i < lampGlows.Length; i++)
            {
                lampGlows[i].ConfigurePreview(
                    (_adapterConfig ?? LoadAdapterConfig()).floodlightEmissionMultiplier,
                    (_adapterConfig ?? LoadAdapterConfig()).floodlightIntensityMax,
                    true,
                    menuFloodlightIntensity,
                    menuConfig.floodlightColorEnabled ? menuConfig.floodlightColor : Color.white);
            }

            DisableEmbeddedSydneySky(_menuBackdrop);
            ApplyOverheadNettingVisibility(_menuBackdrop);
            CaptureMenuRenderSettings();
            ApplyTlanMenuRenderSettings(_menuBackdrop, menuConfig);
            StaticBatchingUtility.Combine(_menuBackdrop);
            Debug.Log("[SydneyTLANArena] Lightweight main-menu stadium snapshot enabled: active TLAN settings, live TRL ice, effective floodlight intensity=" + menuFloodlightIntensity.ToString("0.#") + ".");
        }
        catch (Exception ex)
        {
            Debug.LogError("[SydneyTLANArena] Could not create the conditional main-menu stadium preview: " + ex);
            ClearMenuPreview();
        }
    }

    private static bool IsTlanEnabledAlongsideThisAdapter()
    {
        return _sydneyPrefab != null;
    }

    internal static bool IsOplArenaEnabled()
    {
        _adapterConfig ??= LoadAdapterConfig();
        return _adapterConfig.oplArenaEnabled;
    }

    public static void TryAddOplArenaSettingsToggle()
    {
        if (_settingsToggleAdded)
        {
            return;
        }

        UISettings[] settingsObjects = UnityEngine.Object.FindObjectsByType<UISettings>(FindObjectsSortMode.None);
        for (int i = 0; i < settingsObjects.Length; i++)
        {
            if (TryAddOplArenaSettingsToggle(settingsObjects[i]))
            {
                _settingsToggleAdded = true;
                return;
            }
        }
    }

    private static bool TryAddOplArenaSettingsToggle(object settingsObject)
    {
        if (settingsObject == null)
        {
            return false;
        }

        FieldInfo tabViewField = AccessTools.Field(settingsObject.GetType(), "tabView");
        TabView tabView = tabViewField == null ? null : tabViewField.GetValue(settingsObject) as TabView;
        if (tabView == null)
        {
            return false;
        }

        Tab gameTab = null;
        for (int i = 0; i < tabView.childCount; i++)
        {
            Tab candidate;
            try
            {
                candidate = tabView.GetTab(i);
            }
            catch
            {
                continue;
            }
            if (candidate != null && candidate.label.IndexOf("Game", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                gameTab = candidate;
                break;
            }
        }

        if (gameTab == null || gameTab.contentContainer == null)
        {
            return false;
        }

        FieldInfo silhouetteToggleField = AccessTools.Field(settingsObject.GetType(), "showPuckSilhouetteToggle");
        Toggle silhouetteToggle = silhouetteToggleField == null
            ? null
            : silhouetteToggleField.GetValue(settingsObject) as Toggle;
        VisualElement silhouetteRow = silhouetteToggle == null
            ? null
            : FindNamedAncestor(silhouetteToggle, "ShowPuckSilhouetteToggle");
        if (silhouetteRow == null)
        {
            silhouetteRow = silhouetteToggle;
        }

        VisualElement rowParent = silhouetteRow == null ? null : silhouetteRow.parent;
        if (silhouetteToggle != null && rowParent != null)
        {
            Toggle nativeOplToggle = new Toggle("OPL ARENA")
            {
                name = "SydneyTLANArenaOplToggle",
                value = IsOplArenaEnabled()
            };
            CopyClassList(silhouetteToggle, nativeOplToggle);
            nativeOplToggle.RegisterValueChangedCallback(change => SetOplArenaEnabled(change.newValue));

            VisualElement oplRow = nativeOplToggle;
            if (!ReferenceEquals(silhouetteRow, silhouetteToggle))
            {
                oplRow = new VisualElement
                {
                    name = "SydneyTLANArenaOplToggleRow"
                };
                CopyClassList(silhouetteRow, oplRow);
                oplRow.Add(nativeOplToggle);
            }

            rowParent.Insert(rowParent.IndexOf(silhouetteRow), oplRow);
            Debug.Log("[SydneyTLANArena] Added OPL ARENA as its own native Game settings row.");
            return true;
        }

        if (gameTab.contentContainer.Q<VisualElement>("SydneyTLANArenaOplToggleRow") != null)
        {
            return true;
        }

        VisualElement row = new VisualElement
        {
            name = "SydneyTLANArenaOplToggleRow"
        };
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.alignItems = Align.Center;
        row.style.minHeight = 54f;
        row.style.paddingLeft = 18f;
        row.style.paddingRight = 18f;
        row.style.marginBottom = 6f;

        VisualElement textColumn = new VisualElement();
        textColumn.style.flexGrow = 1f;
        Label title = new Label("OPL ARENA");
        title.style.fontSize = 16f;
        Label description = new Label("Sydney arena and OPL lighting effects");
        description.style.fontSize = 12f;
        description.style.color = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 1f));
        textColumn.Add(title);
        textColumn.Add(description);

        Toggle fallbackToggle = new Toggle
        {
            name = "SydneyTLANArenaOplToggle",
            value = IsOplArenaEnabled()
        };
        fallbackToggle.RegisterValueChangedCallback(change => SetOplArenaEnabled(change.newValue));
        row.Add(textColumn);
        row.Add(fallbackToggle);
        VisualElement fallbackSilhouetteRow = FindSettingsRowByLabel(gameTab.contentContainer, "Show Puck Silhouette");
        int insertIndex = 0;
        if (fallbackSilhouetteRow != null)
        {
            for (int i = 0; i < gameTab.contentContainer.childCount; i++)
            {
                if (ReferenceEquals(gameTab.contentContainer.ElementAt(i), fallbackSilhouetteRow))
                {
                    insertIndex = i;
                    break;
                }
            }
        }

        gameTab.contentContainer.Insert(insertIndex, row);
        Debug.Log("[SydneyTLANArena] Added OPL ARENA toggle to Settings > Game.");
        return true;
    }

    private static VisualElement FindSettingsRowByLabel(VisualElement root, string labelText)
    {
        Label label = FindLabel(root, labelText);
        if (label == null)
        {
            return null;
        }

        VisualElement row = label;
        while (row.parent != null && !ReferenceEquals(row.parent, root))
        {
            row = row.parent;
        }

        return ReferenceEquals(row.parent, root) ? row : null;
    }

    private static VisualElement FindNamedAncestor(VisualElement element, string name)
    {
        VisualElement current = element;
        while (current != null)
        {
            if (string.Equals(current.name, name, StringComparison.Ordinal))
            {
                return current;
            }
            current = current.parent;
        }

        return null;
    }

    private static void CopyClassList(VisualElement source, VisualElement destination)
    {
        if (source == null || destination == null)
        {
            return;
        }

        foreach (string className in source.GetClasses())
        {
            destination.AddToClassList(className);
        }
    }

    private static Label FindLabel(VisualElement root, string labelText)
    {
        if (root is Label label && label.text.IndexOf(labelText, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return label;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Label result = FindLabel(root.ElementAt(i), labelText);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private static void AfterSettingsInitialized(object __instance)
    {
        if (TryAddOplArenaSettingsToggle(__instance))
        {
            _settingsToggleAdded = true;
        }
    }

    private static void SetOplArenaEnabled(bool enabled)
    {
        _adapterConfig ??= LoadAdapterConfig();
        if (_adapterConfig.oplArenaEnabled == enabled)
        {
            return;
        }

        _adapterConfig.oplArenaEnabled = enabled;
        SaveAdapterConfig(_adapterConfig);
        AddOplArenaChangeChatMessage();
        ClearMenuPreview();
        _menuPreviewAttempted = false;

        if (enabled)
        {
            if (_patchInstalled)
            {
                ApplySydneyAssets();
                RefreshTlanArenaAssets();
            }
            else
            {
                if (TryInstallPatch())
                {
                    RefreshTlanArenaAssets();
                }
            }
            Debug.Log("[SydneyTLANArena] OPL ARENA enabled. TLAN arena assets were refreshed for the next/current arena load.");
        }
        else
        {
            RestoreTlanAssetsAfterDisable();
            Debug.Log("[SydneyTLANArena] OPL ARENA disabled. TLAN standard arena assets were restored for the next/current arena load.");
        }
    }

    private static void AddOplArenaChangeChatMessage()
    {
        ChatManager chatManager = ChatManager.Instance;
        if (chatManager == null)
        {
            Debug.LogWarning("[SydneyTLANArena] Could not display the OPL ARENA change message because ChatManager is unavailable.");
            return;
        }

        chatManager.AddChatMessage(new ChatMessage
        {
            Content = "Changes will be reflected next time you enter a server",
            Timestamp = Utils.GetTimestamp(),
            IsQuickChat = false,
            IsTeamChat = false,
            IsSystem = true
        });
    }

    private static void RestoreTlanAssetsAfterDisable()
    {
        RefreshTlanArenaAssets();
    }

    private static void RefreshTlanArenaAssets()
    {
        if (!_patchInstalled)
        {
            return;
        }

        try
        {
            Type arenaBundleType = AccessTools.TypeByName(ArenaBundleTypeName);
            MethodInfo resolver = arenaBundleType == null
                ? null
                : AccessTools.Method(arenaBundleType, ResolverMethodName, Type.EmptyTypes);
            if (resolver == null)
            {
                return;
            }

            resolver.Invoke(null, null);
        }
        catch (Exception ex)
        {
            Exception detail = ex is TargetInvocationException && ex.InnerException != null
                ? ex.InnerException
                : ex;
            Debug.LogWarning("[SydneyTLANArena] Could not refresh TLAN arena assets after changing OPL ARENA: " + detail);
        }
    }

    private static void HideVanillaMenuArena()
    {
        LockerRoomPlayer[] players = UnityEngine.Object.FindObjectsByType<LockerRoomPlayer>(FindObjectsSortMode.None);
        LockerRoomStick[] sticks = UnityEngine.Object.FindObjectsByType<LockerRoomStick>(FindObjectsSortMode.None);
        Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || IsMenuPlayerRenderer(renderer, players, sticks) || IsMenuIceRenderer(renderer))
            {
                continue;
            }

            _menuHiddenRenderers.Add(renderer);
            _menuHiddenRendererStates.Add(renderer.enabled);
            renderer.enabled = false;
        }
    }

    private static bool IsMenuPlayerRenderer(Renderer renderer, LockerRoomPlayer[] players, LockerRoomStick[] sticks)
    {
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && renderer.transform.IsChildOf(players[i].transform))
            {
                return true;
            }
        }
        for (int i = 0; i < sticks.Length; i++)
        {
            if (sticks[i] != null && renderer.transform.IsChildOf(sticks[i].transform))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsMenuIceRenderer(Renderer renderer)
    {
        string objectName = renderer.gameObject.name;
        return string.Equals(objectName, "Ice Bottom", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(objectName, "Ice Top", StringComparison.OrdinalIgnoreCase);
    }

    private static void HideMenuSnapshotIce(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        int hidden = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && (IsMenuIceRenderer(renderers[i]) || LooksLikeIce(renderers[i])))
            {
                renderers[i].enabled = false;
                hidden++;
            }
        }
        Debug.Log("[SydneyTLANArena] Menu snapshot is using the locker-room/TRL ice renderer; hid " + hidden + " duplicate Sydney ice renderer(s).");
    }

    private static void DisableMenuPhysics(GameObject root)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
            {
                colliders[i].enabled = false;
            }
        }

        Rigidbody[] rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            if (rigidbodies[i] != null)
            {
                rigidbodies[i].isKinematic = true;
                rigidbodies[i].detectCollisions = false;
            }
        }
    }

    private static void DisableMenuArenaBehaviours(GameObject root)
    {
        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null)
            {
                behaviours[i].enabled = false;
            }
        }
    }

    private static void DisableMenuShadows(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
            {
                continue;
            }

            renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderers[i].receiveShadows = false;
        }
    }

    private static Renderer FindCurrentMenuIceRenderer()
    {
        Renderer source = FindSceneRenderer("Ice Bottom");
        if (source != null)
        {
            return source;
        }

        Renderer[] sceneRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        for (int i = 0; i < sceneRenderers.Length; i++)
        {
            if (sceneRenderers[i] != null && LooksLikeIce(sceneRenderers[i]))
            {
                return sceneRenderers[i];
            }
        }
        return null;
    }

    private static void ApplyCurrentMenuIceMaterial(GameObject root, Renderer sourceIceBottom)
    {
        if (sourceIceBottom == null)
        {
            Debug.LogWarning("[SydneyTLANArena] Could not find the current locker-room ice material to copy into the menu snapshot.");
            return;
        }

        Renderer[] targetRenderers = root.GetComponentsInChildren<Renderer>(true);
        int copied = 0;
        for (int i = 0; i < targetRenderers.Length; i++)
        {
            if (targetRenderers[i] != null && LooksLikeIce(targetRenderers[i]))
            {
                CopyIceMaterial(sourceIceBottom, targetRenderers[i]);
                copied++;
            }
        }

        Renderer sourceIceTop = FindSceneRenderer("Ice Top");
        Renderer targetIceTop = FindRenderer(root, "Ice Top");
        if (sourceIceTop != null && targetIceTop != null)
        {
            CopyIceMaterial(sourceIceTop, targetIceTop);
        }

        Debug.Log("[SydneyTLANArena] Copied the active TRL/locker-room ice material to " + copied + " menu snapshot renderer(s).");
    }

    private static Renderer FindSceneRenderer(string objectName)
    {
        Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && string.Equals(renderers[i].gameObject.name, objectName, StringComparison.OrdinalIgnoreCase))
            {
                return renderers[i];
            }
        }
        return null;
    }

    private static void CopyIceMaterial(Renderer source, Renderer target)
    {
        Material sourceMaterial = source.material;
        Material targetMaterial = target.material;
        if (sourceMaterial == null || targetMaterial == null)
        {
            return;
        }

        string[] textureProperties = { "_BaseMap", "baseColorTexture", "_MainTex" };
        for (int i = 0; i < textureProperties.Length; i++)
        {
            if (sourceMaterial.HasProperty(textureProperties[i]) && targetMaterial.HasProperty(textureProperties[i]))
            {
                Texture texture = sourceMaterial.GetTexture(textureProperties[i]);
                if (texture != null)
                {
                    targetMaterial.SetTexture(textureProperties[i], texture);
                }
            }
        }

        string[] colorProperties = { "_BaseColor", "_Color" };
        for (int i = 0; i < colorProperties.Length; i++)
        {
            if (sourceMaterial.HasProperty(colorProperties[i]) && targetMaterial.HasProperty(colorProperties[i]))
            {
                targetMaterial.SetColor(colorProperties[i], sourceMaterial.GetColor(colorProperties[i]));
            }
        }

        if (sourceMaterial.HasProperty("_Smoothness") && targetMaterial.HasProperty("_Smoothness"))
        {
            targetMaterial.SetFloat("_Smoothness", sourceMaterial.GetFloat("_Smoothness"));
        }
    }

    private static void DisableEmbeddedSydneySky(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && IsSydneySkyRenderer(renderers[i]))
            {
                renderers[i].enabled = false;
            }
        }
    }

    private static void CaptureMenuRenderSettings()
    {
        if (!_menuRenderSettingsSaved)
        {
            _menuAmbientLight = RenderSettings.ambientLight;
            _menuAmbientIntensity = RenderSettings.ambientIntensity;
            _menuReflectionIntensity = RenderSettings.reflectionIntensity;
            _menuFog = RenderSettings.fog;
            _menuFogColor = RenderSettings.fogColor;
            _menuFogDensity = RenderSettings.fogDensity;
            _menuFogStartDistance = RenderSettings.fogStartDistance;
            _menuFogEndDistance = RenderSettings.fogEndDistance;
            _menuOriginalSkybox = RenderSettings.skybox;
            _menuRenderSettingsSaved = true;
        }
    }

    private static void ApplyTlanMenuRenderSettings(GameObject root, TlanMenuRenderConfig config)
    {
        if (config == null)
        {
            return;
        }

        float nightFactor = config.GetNightFactor();
        RenderSettings.fog = config.fogEnabled;
        RenderSettings.fogColor = Color.Lerp(config.fogDayColor, config.fogNightColor, nightFactor);
        RenderSettings.fogStartDistance = Mathf.Lerp(config.fogDayStart, config.fogNightStart, nightFactor);
        RenderSettings.fogEndDistance = Mathf.Lerp(config.fogDayEnd, config.fogNightEnd, nightFactor);
        ApplyTlanMenuSkybox(config);

        bool hasActiveDirectional = false;
        Light[] sceneLights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        for (int i = 0; i < sceneLights.Length; i++)
        {
            if (sceneLights[i] != null && sceneLights[i].enabled && sceneLights[i].type == LightType.Directional)
            {
                hasActiveDirectional = true;
                break;
            }
        }

        if (!hasActiveDirectional && root != null)
        {
            float dayFactor = 1f - nightFactor;
            GameObject sunObject = new GameObject("SydneyTLANArenaMenuSun");
            sunObject.transform.SetParent(root.transform, false);
            sunObject.transform.rotation = Quaternion.Euler(90f - Mathf.Lerp(-25f, 65f, dayFactor), 25f, 0f);
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Color.Lerp(new Color(0.22f, 0.3f, 0.5f, 1f), Color.white, dayFactor);
            sun.intensity = Mathf.Lerp(0.08f, 1f, dayFactor) * config.sunIntensityScale;
            sun.shadows = LightShadows.None;
            sun.cullingMask = ~0;
            sun.renderingLayerMask = -1;
        }

        ApplyTlanMenuPostProcessing(root, config);

        // TLAN's active volume remains in control when one already exists. A
        // small fallback volume is created only for a direct locker-room boot.
        Debug.Log("[SydneyTLANArena] Applied active TLAN locker-room render profile: time=" +
                  config.manualHour.ToString("0.##") + ", nightFactor=" + nightFactor.ToString("0.##") +
                  ", bloom=" + config.bloomIntensity.ToString("0.##") + ", TRL ice renderer retained.");
    }

    private static void ApplyTlanMenuPostProcessing(GameObject root, TlanMenuRenderConfig config)
    {
        Type volumeType = Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
        Type volumeProfileType = Type.GetType("UnityEngine.Rendering.VolumeProfile, Unity.RenderPipelines.Core.Runtime");
        if (volumeType == null || volumeProfileType == null)
        {
            return;
        }

        UnityEngine.Object[] existingVolumes = Resources.FindObjectsOfTypeAll(volumeType);
        for (int i = 0; i < existingVolumes.Length; i++)
        {
            Component existingVolume = existingVolumes[i] as Component;
            if (existingVolume != null && existingVolume.gameObject != root && existingVolume.gameObject.activeInHierarchy &&
                existingVolume.gameObject.scene.name == UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
            {
                return;
            }
        }

        Type bloomType = Type.GetType("UnityEngine.Rendering.Universal.Bloom, Unity.RenderPipelines.Universal.Runtime");
        Type tonemappingType = Type.GetType("UnityEngine.Rendering.Universal.Tonemapping, Unity.RenderPipelines.Universal.Runtime");
        if (bloomType == null && tonemappingType == null)
        {
            return;
        }

        GameObject volumeObject = new GameObject("SydneyTLANArenaMenuPostProcessing");
        volumeObject.transform.SetParent(root.transform, false);
        Component volume = volumeObject.AddComponent(volumeType);
        SetMember(volume, "isGlobal", true);
        SetMember(volume, "priority", 1000f);

        ScriptableObject profile = ScriptableObject.CreateInstance(volumeProfileType);
        profile.name = "Sydney TLAN Menu Render Profile (Runtime)";
        SetMember(volume, "sharedProfile", profile);

        if (bloomType != null && config.bloomEnabled)
        {
            object bloom = AddVolumeOverride(profile, bloomType);
            SetVolumeParameter(bloom, "threshold", config.bloomThreshold);
            SetVolumeParameter(bloom, "intensity", config.bloomIntensity);
            SetVolumeParameter(bloom, "scatter", config.bloomScatter);
        }

        if (tonemappingType != null)
        {
            object tonemapping = AddVolumeOverride(profile, tonemappingType);
            Type modeType = GetValueType(tonemapping, "mode");
            if (modeType != null)
            {
                try
                {
                    SetVolumeParameter(tonemapping, "mode", Enum.Parse(modeType, string.IsNullOrEmpty(config.toneMapping) ? "Neutral" : config.toneMapping, true));
                }
                catch
                {
                    SetVolumeParameter(tonemapping, "mode", Enum.Parse(modeType, "Neutral", true));
                }
            }
            SetVolumeParameter(tonemapping, "postExposure", config.toneMappingExposure);
            SetVolumeParameter(tonemapping, "contrast", config.toneMappingContrast);
        }

        SydneyMenuPostProcessingMarker marker = volumeObject.AddComponent<SydneyMenuPostProcessingMarker>();
        marker.Initialize(profile);
    }

    private static void ApplyTlanMenuSkybox(TlanMenuRenderConfig config)
    {
        Material currentSkybox = RenderSettings.skybox;
        if (currentSkybox != null && currentSkybox.name.IndexOf("TLANskybox", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _menuSkyboxMaterial = UnityEngine.Object.Instantiate(currentSkybox);
            _menuSkyboxMaterial.name = "TLANskybox Menu Preview (Runtime)";
            ApplySkyboxProfile(_menuSkyboxMaterial, config);
            RenderSettings.skybox = _menuSkyboxMaterial;
            DynamicGI.UpdateEnvironment();
            Debug.Log("[SydneyTLANArena] The active TLAN skybox was already present in locker_room; cloned it for the menu time profile.");
            return;
        }

        string bundlePath = GetTlanSkyboxBundlePath();
        if (string.IsNullOrEmpty(bundlePath) || !File.Exists(bundlePath))
        {
            Debug.LogWarning("[SydneyTLANArena] TLAN skybox bundle was not found; locker_room will retain its existing skybox.");
            return;
        }

        try
        {
            _tlanSkyboxBundle = AssetBundle.LoadFromFile(bundlePath);
            if (_tlanSkyboxBundle == null)
            {
                Debug.LogWarning("[SydneyTLANArena] Unity could not load the TLAN skybox bundle for the locker-room preview.");
                return;
            }

            string materialPath = null;
            string[] assetNames = _tlanSkyboxBundle.GetAllAssetNames();
            for (int i = 0; i < assetNames.Length; i++)
            {
                if (assetNames[i].EndsWith("tlanskybox.mat", StringComparison.OrdinalIgnoreCase))
                {
                    materialPath = assetNames[i];
                    break;
                }
            }

            if (materialPath == null)
            {
                Debug.LogWarning("[SydneyTLANArena] TLAN skybox bundle did not contain a TLANskybox material.");
                UnloadTlanSkyboxBundle();
                return;
            }

            Material sourceMaterial = _tlanSkyboxBundle.LoadAsset<Material>(materialPath);
            if (sourceMaterial == null)
            {
                Debug.LogWarning("[SydneyTLANArena] TLANskybox material could not be loaded from the workshop bundle.");
                UnloadTlanSkyboxBundle();
                return;
            }

            _menuSkyboxMaterial = UnityEngine.Object.Instantiate(sourceMaterial);
            _menuSkyboxMaterial.name = "TLANskybox Menu Preview (Runtime)";
            ApplySkyboxProfile(_menuSkyboxMaterial, config);
            RenderSettings.skybox = _menuSkyboxMaterial;
            DynamicGI.UpdateEnvironment();
            Debug.Log("[SydneyTLANArena] Loaded the TLAN skybox directly for the locker-room preview at " + bundlePath + ".");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SydneyTLANArena] Could not apply the TLAN skybox to locker_room: " + ex.Message);
            if (_menuSkyboxMaterial != null)
            {
                UnityEngine.Object.Destroy(_menuSkyboxMaterial);
                _menuSkyboxMaterial = null;
            }
            UnloadTlanSkyboxBundle();
        }
    }

    private static void ApplySkyboxProfile(Material material, TlanMenuRenderConfig config)
    {
        if (material == null || config == null)
        {
            return;
        }

        if (material.HasProperty("_Transition"))
        {
            material.SetFloat("_Transition", Mathf.Lerp(0f, 1f, config.GetNightFactor()));
        }

        if (material.HasProperty("_SunDirection"))
        {
            float dayFactor = 1f - config.GetNightFactor();
            Quaternion rotation = Quaternion.Euler(90f - Mathf.Lerp(-25f, 65f, dayFactor), 25f, 0f);
            Vector3 direction = rotation * Vector3.forward;
            material.SetVector("_SunDirection", new Vector4(direction.x, direction.y, direction.z, 0f));
        }
    }

    private static string GetTlanSkyboxBundlePath()
    {
        string puckDirectory = null;
        string pluginDirectory = Path.GetDirectoryName(typeof(TlanSydneyAdapter).Assembly.Location);
        if (!string.IsNullOrEmpty(pluginDirectory))
        {
            puckDirectory = Path.GetFullPath(Path.Combine(pluginDirectory, "..", ".."));
        }
        if (string.IsNullOrEmpty(puckDirectory) && !string.IsNullOrEmpty(Application.dataPath))
        {
            puckDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }
        if (string.IsNullOrEmpty(puckDirectory))
        {
            return null;
        }

        return Path.Combine(puckDirectory, "..", "..", "workshop", "content", "2994020", "3739199569", "AssetBundles", "tlanskybox");
    }

    private static void UnloadTlanSkyboxBundle()
    {
        if (_tlanSkyboxBundle != null)
        {
            _tlanSkyboxBundle.Unload(false);
            _tlanSkyboxBundle = null;
        }
    }

    private static object AddVolumeOverride(ScriptableObject profile, Type overrideType)
    {
        MethodInfo[] methods = profile.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (method.Name != "Add" || !method.IsGenericMethodDefinition || method.GetGenericArguments().Length != 1)
            {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            object[] arguments = parameters.Length == 0 ? null : new object[parameters.Length];
            for (int parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
            {
                arguments[parameterIndex] = parameterIndex == 0 && parameters[parameterIndex].ParameterType == typeof(bool);
            }

            try
            {
                return method.MakeGenericMethod(overrideType).Invoke(profile, arguments);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private static void SetVolumeParameter(object component, string memberName, object value)
    {
        if (component == null)
        {
            return;
        }

        object parameter = GetMember(component, memberName);
        if (parameter == null)
        {
            return;
        }

        SetMember(parameter, "overrideState", true);
        SetMember(parameter, "value", value);
    }

    private static Type GetValueType(object component, string memberName)
    {
        object parameter = GetMember(component, memberName);
        if (parameter == null)
        {
            return null;
        }

        PropertyInfo valueProperty = parameter.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public);
        return valueProperty == null ? null : valueProperty.PropertyType;
    }

    private static object GetMember(object target, string memberName)
    {
        if (target == null)
        {
            return null;
        }

        Type type = target.GetType();
        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.CanRead)
        {
            return property.GetValue(target, null);
        }

        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
        return field == null ? null : field.GetValue(target);
    }

    private static void SetMember(object target, string memberName, object value)
    {
        if (target == null)
        {
            return;
        }

        Type type = target.GetType();
        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.CanWrite)
        {
            try
            {
                property.SetValue(target, value, null);
            }
            catch
            {
            }
            return;
        }

        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (field != null)
        {
            try
            {
                field.SetValue(target, value);
            }
            catch
            {
            }
        }
    }

    public static void ClearMenuPreview()
    {
        if (_menuBackdrop != null)
        {
            UnityEngine.Object.Destroy(_menuBackdrop);
            _menuBackdrop = null;
        }

        for (int i = 0; i < _menuHiddenRenderers.Count; i++)
        {
            if (_menuHiddenRenderers[i] != null)
            {
                _menuHiddenRenderers[i].enabled = _menuHiddenRendererStates[i];
            }
        }
        _menuHiddenRenderers.Clear();
        _menuHiddenRendererStates.Clear();

        if (_menuRenderSettingsSaved)
        {
            RenderSettings.ambientLight = _menuAmbientLight;
            RenderSettings.ambientIntensity = _menuAmbientIntensity;
            RenderSettings.reflectionIntensity = _menuReflectionIntensity;
            RenderSettings.fog = _menuFog;
            RenderSettings.fogColor = _menuFogColor;
            RenderSettings.fogDensity = _menuFogDensity;
            RenderSettings.fogStartDistance = _menuFogStartDistance;
            RenderSettings.fogEndDistance = _menuFogEndDistance;
            RenderSettings.skybox = _menuOriginalSkybox;
            DynamicGI.UpdateEnvironment();
            _menuOriginalSkybox = null;
            _menuRenderSettingsSaved = false;
        }

        if (_menuSkyboxMaterial != null)
        {
            UnityEngine.Object.Destroy(_menuSkyboxMaterial);
            _menuSkyboxMaterial = null;
        }
        UnloadTlanSkyboxBundle();

    }

    public static bool TryInstallPatch()
    {
        _adapterConfig ??= LoadAdapterConfig();
        TryInstallSettingsPatch();
        if (!_adapterConfig.oplArenaEnabled)
        {
            return false;
        }

        if (_patchInstalled)
        {
            return true;
        }

        try
        {
            Type arenaBundleType = AccessTools.TypeByName(ArenaBundleTypeName);
            if (arenaBundleType == null)
            {
                return false;
            }

            MethodInfo resolver = AccessTools.Method(arenaBundleType, ResolverMethodName, Type.EmptyTypes);
            if (resolver == null)
            {
                Debug.LogWarning("[SydneyTLANArena] TLAN ArenaBundle.ResolveAssets() was not found.");
                return false;
            }

            _arenaPrefabProperty = AccessTools.Property(arenaBundleType, "ArenaPrefab");
            _bundleFolderProperty = AccessTools.Property(arenaBundleType, "BundleFolder");
            if (_arenaPrefabProperty == null || !_arenaPrefabProperty.CanWrite || _bundleFolderProperty == null || !_bundleFolderProperty.CanWrite)
            {
                Debug.LogWarning("[SydneyTLANArena] TLAN ArenaBundle properties were not writable.");
                return false;
            }

            _harmony ??= new Harmony(HarmonyId);
            _harmony.Patch(resolver, postfix: new HarmonyMethod(typeof(TlanSydneyAdapter), nameof(AfterResolveAssets)));

            Type arenaSpawnerType = AccessTools.TypeByName("TLANArena.Scenery.ArenaSpawner");
            MethodInfo spawn = arenaSpawnerType == null
                ? null
                : AccessTools.Method(arenaSpawnerType, "Spawn", new[] { typeof(MonoBehaviour) });
            _arenaRootProperty = arenaSpawnerType == null
                ? null
                : AccessTools.Property(arenaSpawnerType, "Root");
            if (spawn == null || _arenaRootProperty == null)
            {
                Debug.LogWarning("[SydneyTLANArena] TLAN ArenaSpawner.Spawn() was not found; the Sydney backdrop cannot be filtered.");
            }
            else
            {
                _harmony.Patch(spawn, postfix: new HarmonyMethod(typeof(TlanSydneyAdapter), nameof(AfterArenaSpawn)));
            }

            Type floodlightsType = AccessTools.TypeByName("TLANArena.Lighting.ArenaFloodlights");
            MethodInfo attachFloodlights = floodlightsType == null
                ? null
                : AccessTools.Method(floodlightsType, "Attach", new[] { typeof(GameObject) });
            if (attachFloodlights != null)
            {
                _harmony.Patch(attachFloodlights, prefix: new HarmonyMethod(typeof(TlanSydneyAdapter), nameof(BeforeFloodlightAttach)));
            }
            else
            {
                Debug.LogWarning("[SydneyTLANArena] TLAN ArenaFloodlights.Attach() was not found; runtime Sydney lights cannot be created.");
            }

            Type chatManagerType = AccessTools.TypeByName("ChatManager");
            MethodInfo sendChat = chatManagerType == null
                ? null
                : AccessTools.Method(chatManagerType, "Client_SendChatMessageRpc");
            if (sendChat != null)
            {
                _harmony.Patch(sendChat, postfix: new HarmonyMethod(typeof(TlanSydneyAdapter), nameof(AfterChatMessageSent)));
            }
            else
            {
                Debug.LogWarning("[SydneyTLANArena] ChatManager.Client_SendChatMessageRpc() was not found; warmup siren commands are unavailable.");
            }

            _patchInstalled = true;
            Debug.Log("[SydneyTLANArena] Connected to TLAN Arena. Sydney prefab will use TLAN skybox and lighting controls.");
            ApplySydneyAssets();
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError("[SydneyTLANArena] Could not install the TLAN adapter: " + ex);
            return false;
        }
    }

    private static void TryInstallSettingsPatch()
    {
        if (_settingsPatchInstalled)
        {
            return;
        }

        try
        {
            Type settingsType = AccessTools.TypeByName("UISettings");
            MethodInfo initialize = settingsType == null
                ? null
                : AccessTools.Method(settingsType, "Initialize", new[] { typeof(VisualElement) });
            if (initialize == null)
            {
                return;
            }

            _harmony ??= new Harmony(HarmonyId);
            _harmony.Patch(initialize, postfix: new HarmonyMethod(typeof(TlanSydneyAdapter), nameof(AfterSettingsInitialized)));
            _settingsPatchInstalled = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SydneyTLANArena] Could not add the OPL ARENA Settings > Game toggle: " + ex.Message);
        }
    }

    private static void AfterResolveAssets()
    {
        try
        {
            if (!IsOplArenaEnabled())
            {
                return;
            }
            ApplySydneyAssets();
        }
        catch (Exception ex)
        {
            Debug.LogError("[SydneyTLANArena] Could not replace the TLAN arena prefab with Sydney: " + ex);
        }
    }

    private static void AfterChatMessageSent(string content)
    {
        if (!IsOplArenaEnabled())
        {
            return;
        }
        SydneyGoalSirenController.TryHandleWarmupCommand(content);
    }

    private static bool ApplySydneyAssets()
    {
        if (!IsOplArenaEnabled())
        {
            return false;
        }
        if (!LoadSydneyPrefab())
        {
            return false;
        }

        _arenaPrefabProperty.SetValue(null, _sydneyPrefab, null);
        _bundleFolderProperty.SetValue(null, Path.GetDirectoryName(GetSydneyBundlePath()), null);
        Debug.Log("[SydneyTLANArena] Applied Sydney arena prefab and metadata source. Reload the arena/level to use it if one is already spawned.");
        return true;
    }

    private static void AfterArenaSpawn()
    {
        try
        {
            if (!IsOplArenaEnabled())
            {
                return;
            }
            if (_arenaRootProperty == null)
            {
                return;
            }

            GameObject arenaRoot = _arenaRootProperty.GetValue(null, null) as GameObject;
            if (arenaRoot == null)
            {
                return;
            }

            int disabled = 0;
            Renderer[] renderers = arenaRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !IsSydneySkyRenderer(renderer))
                {
                    continue;
                }

                renderer.enabled = false;
                disabled++;
            }

            if (disabled > 0)
            {
                Debug.Log("[SydneyTLANArena] Disabled " + disabled + " embedded Sydney sky/backdrop renderer(s); TLAN skybox remains active.");
            }
            else
            {
                Debug.Log("[SydneyTLANArena] No embedded Sydney sky/backdrop renderer matched the filter.");
            }

            ApplyOverheadNettingVisibility(arenaRoot);
            ApplyIceReflectionFilter(arenaRoot);

            SydneyGoalSirenController sirenController = arenaRoot.GetComponent<SydneyGoalSirenController>();
            if (sirenController == null)
            {
                sirenController = arenaRoot.AddComponent<SydneyGoalSirenController>();
            }
            sirenController.Initialize(arenaRoot, (_adapterConfig ?? LoadAdapterConfig()).goalSirenIntensityMultiplier);

            // Some TLAN builds do not invoke their native floodlight attachment
            // successfully for the Sydney hierarchy. Ensure Companion still owns
            // its visual lighting in that case, without creating a duplicate when
            // the normal TLAN hook already ran.
            if (arenaRoot.transform.Find("SydneyTLANArenaRuntimeLights") == null)
            {
                BeforeFloodlightAttach(arenaRoot);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[SydneyTLANArena] Could not hide Sydney's embedded sky/backdrop: " + ex);
        }
    }

    private static void BeforeFloodlightAttach(GameObject arenaRoot)
    {
        try
        {
            if (!IsOplArenaEnabled())
            {
                return;
            }
            if (arenaRoot == null || arenaRoot.transform.Find("SydneyTLANArenaRuntimeLights") != null)
            {
                return;
            }

            GameObject runtimeLights = new GameObject("SydneyTLANArenaRuntimeLights");
            runtimeLights.transform.SetParent(arenaRoot.transform, false);
            bool menuPreview = arenaRoot.GetComponent<SydneyMenuBackdropMarker>() != null;
            _adapterConfig = LoadAdapterConfig();
            bool floodlightShadows = !menuPreview && _adapterConfig.floodlightShadows;
            Light[] nativeFloodlights = menuPreview ? new Light[0] : FindNativeFloodlights(arenaRoot, runtimeLights);

            ResolveFloodlightTransforms(arenaRoot, nativeFloodlights, out Vector3[] positions, out Vector3[] directions);
            Texture2D panelCookie = menuPreview ? null : CreateFloodlightCookie();
            for (int i = 0; !menuPreview && i < positions.Length; i++)
            {
                Vector3 position = positions[i];
                GameObject lightRoot = new GameObject("Flood_Lamps_Runtime_" + i);
                lightRoot.transform.SetParent(runtimeLights.transform, false);
                lightRoot.transform.localPosition = position;
                lightRoot.transform.localRotation = Quaternion.LookRotation(directions[i], Vector3.up);

                GameObject lightObject = new GameObject("Spot Light");
                lightObject.transform.SetParent(lightRoot.transform, false);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Spot;
                light.range = 220f;
                light.spotAngle = 95f;
                light.innerSpotAngle = 55f;
                light.intensity = 0f;
                light.color = Color.white;
                light.cookie = panelCookie;
                light.shadows = floodlightShadows ? LightShadows.Soft : LightShadows.None;
                light.shadowStrength = floodlightShadows ? 0.85f : 0f;
                light.shadowBias = floodlightShadows ? 0.03f : 0f;
                light.shadowNormalBias = floodlightShadows ? 0.4f : 0f;
                light.shadowNearPlane = floodlightShadows ? 0.5f : 0.1f;
                light.renderMode = LightRenderMode.Auto;
                light.cullingMask = ~0;
                light.enabled = false;
            }

            Light[] runtimeLightComponents = runtimeLights.GetComponentsInChildren<Light>(true);
            if (!menuPreview)
            {
                SydneyFloodlightIntensity intensityControl = runtimeLights.AddComponent<SydneyFloodlightIntensity>();
                intensityControl.Initialize(
                    runtimeLightComponents,
                    _adapterConfig.floodlightIntensityMultiplier,
                    _adapterConfig.floodlightIntensityMax,
                    nativeFloodlights,
                    401.73407f,
                    arenaRoot);
                intensityControl.SetShadowsEnabled(floodlightShadows);
            }

            Renderer lampRenderer = FindRenderer(arenaRoot, "Flood_Lamps");
            Renderer floodlightStructureRenderer = FindRenderer(arenaRoot, "Floodlights");
            if (lampRenderer != null)
            {
                lampRenderer.shadowCastingMode = menuPreview || !floodlightShadows
                    ? UnityEngine.Rendering.ShadowCastingMode.Off
                    : UnityEngine.Rendering.ShadowCastingMode.On;
            }
            if (floodlightStructureRenderer != null)
            {
                floodlightStructureRenderer.shadowCastingMode = menuPreview || !floodlightShadows
                    ? UnityEngine.Rendering.ShadowCastingMode.Off
                    : UnityEngine.Rendering.ShadowCastingMode.On;
            }
            if (lampRenderer != null)
            {
                SydneyLampGlow glow = runtimeLights.AddComponent<SydneyLampGlow>();
                glow.Initialize(lampRenderer, runtimeLightComponents, _adapterConfig.floodlightEmissionMultiplier, _adapterConfig.floodlightIntensityMax);
            }
            else
            {
                Debug.LogWarning("[SydneyTLANArena] The Sydney Flood_Lamps mesh was not found; runtime lights will have no visible lamp emission.");
            }

            if (!menuPreview)
            {
                SydneyIceReflectionContribution reflectionContribution = runtimeLights.AddComponent<SydneyIceReflectionContribution>();
                reflectionContribution.Initialize(runtimeLightComponents, _adapterConfig.floodlightIntensityMax, _adapterConfig.floodlightReflectionMultiplier);
            }

            Debug.Log("[SydneyTLANArena] Created " + (menuPreview ? "lightweight menu floodlight panels" : "4 runtime floodlights at the Sydney lamp-array positions") +
                      " (native source candidates=" + nativeFloodlights.Length + ", Companion-owned state fallback enabled).");
        }
        catch (Exception ex)
        {
            Debug.LogError("[SydneyTLANArena] Could not create Sydney runtime floodlights: " + ex);
        }
    }

    private static Renderer FindRenderer(GameObject root, string objectName)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && string.Equals(renderers[i].gameObject.name, objectName, StringComparison.OrdinalIgnoreCase))
            {
                return renderers[i];
            }
        }

        return null;
    }

    private static Light[] FindNativeFloodlights(GameObject arenaRoot, GameObject runtimeLights)
    {
        if (arenaRoot == null)
        {
            return new Light[0];
        }

        List<Light> matches = new List<Light>();
        Light[] lights = arenaRoot.GetComponentsInChildren<Light>(true);
        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light == null || (runtimeLights != null && light.transform.IsChildOf(runtimeLights.transform)))
            {
                continue;
            }

            Transform current = light.transform.parent;
            bool underFloodlight = false;
            while (current != null && current != arenaRoot.transform.parent)
            {
                if (current.name.IndexOf("Flood_Lamps", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.name.IndexOf("Floodlight", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    underFloodlight = true;
                    break;
                }
                current = current.parent;
            }

            if (underFloodlight)
            {
                matches.Add(light);
            }
        }

        Debug.Log("[SydneyTLANArena] Companion found " + matches.Count +
                  " native floodlight source(s) under the Sydney prefab; native names are treated as optional compatibility hints.");
        return matches.ToArray();
    }

    private static void ResolveFloodlightTransforms(GameObject arenaRoot, Light[] nativeFloodlights, out Vector3[] positions, out Vector3[] directions)
    {
        positions = new[]
        {
            new Vector3(-48.22f, 42f, -71.46f),
            new Vector3(-48.22f, 42f, 71.46f),
            new Vector3(48.22f, 42f, -71.46f),
            new Vector3(48.22f, 42f, 71.46f)
        };
        directions = new[]
        {
            new Vector3(0.7071f, 0f, 0.7071f),
            new Vector3(0.7071f, 0f, -0.7071f),
            new Vector3(-0.7071f, 0f, 0.7071f),
            new Vector3(-0.7071f, 0f, -0.7071f)
        };

        if (arenaRoot == null || nativeFloodlights == null || nativeFloodlights.Length == 0)
        {
            return;
        }

        List<Light> sources = new List<Light>();
        for (int i = 0; i < nativeFloodlights.Length; i++)
        {
            Light source = nativeFloodlights[i];
            if (source != null && !sources.Contains(source))
            {
                sources.Add(source);
            }
        }

        sources.Sort((left, right) =>
        {
            Vector3 leftPosition = left.transform.position;
            Vector3 rightPosition = right.transform.position;
            int xComparison = leftPosition.x.CompareTo(rightPosition.x);
            return xComparison != 0 ? xComparison : leftPosition.z.CompareTo(rightPosition.z);
        });

        int count = Mathf.Min(positions.Length, sources.Count);
        for (int i = 0; i < count; i++)
        {
            Light source = sources[i];
            positions[i] = arenaRoot.transform.InverseTransformPoint(source.transform.position);
            Vector3 localDirection = arenaRoot.transform.InverseTransformDirection(source.transform.forward).normalized;
            if (localDirection.sqrMagnitude > 0.001f)
            {
                directions[i] = localDirection;
            }
        }

        if (count > 0)
        {
            Debug.Log("[SydneyTLANArena] Runtime floodlight transforms resolved from the Sydney prefab's native lamp sources.");
        }
    }

    private static Texture2D CreateFloodlightCookie()
    {
        const int size = 256;
        Texture2D cookie = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
        {
            name = "Sydney Floodlight Rectangular Cookie (Runtime)",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        float halfWidth = 0.82f;
        float halfHeight = halfWidth / (4.31f / 3.20f);
        float feather = 0.055f;
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = ((y + 0.5f) / size) * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float u = ((x + 0.5f) / size) * 2f - 1f;
                float distanceToEdge = Mathf.Min(halfWidth - Mathf.Abs(u), halfHeight - Mathf.Abs(v));
                float mask = Mathf.Clamp01(distanceToEdge / feather);
                pixels[y * size + x] = new Color(mask, mask, mask, mask);
            }
        }

        cookie.SetPixels(pixels);
        cookie.Apply(false, true);
        return cookie;
    }

    private static void ApplyIceReflectionFilter(GameObject arenaRoot)
    {
        try
        {
            if (arenaRoot == null || arenaRoot.GetComponent<SydneyIceReflectionFilterMarker>() != null)
            {
                return;
            }

            SydneyAdapterConfig config = _adapterConfig ?? LoadAdapterConfig();
            if (!config.iceReflectionFilter)
            {
                arenaRoot.AddComponent<SydneyIceReflectionFilterMarker>();
                Debug.Log("[SydneyTLANArena] Ice reflection filter is disabled in SydneyTLANArena.json.");
                return;
            }

            Renderer[] renderers = arenaRoot.GetComponentsInChildren<Renderer>(true);
            int tunedRenderers = 0;
            int tunedMaterials = 0;
            const uint floodlightRenderingLayer = 1u << 1;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null)
                {
                    continue;
                }

                bool iceRenderer = LooksLikeIce(renderer);
                uint renderingLayerMask = renderer.renderingLayerMask;
                if (renderingLayerMask == 0u)
                {
                    renderingLayerMask = 1u;
                }
                if (iceRenderer)
                {
                    renderingLayerMask &= ~floodlightRenderingLayer;
                    if (renderingLayerMask == 0u)
                    {
                        renderingLayerMask = 1u;
                    }
                }
                else
                {
                    renderingLayerMask |= floodlightRenderingLayer;
                }
                renderer.renderingLayerMask = renderingLayerMask;

                if (!iceRenderer)
                {
                    continue;
                }

                Material[] materials = renderer.materials;
                bool tunedRenderer = false;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null || material.shader == null || material.shader.name.IndexOf("Lit", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    bool tunedMaterial = false;

                    if (material.HasProperty("_SpecularHighlights"))
                    {
                        material.SetFloat("_SpecularHighlights", 0f);
                        tunedMaterial = true;
                        tunedRenderer = true;
                    }
                    material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    tunedMaterial = true;
                    tunedRenderer = true;

                    if (tunedMaterial)
                    {
                        tunedMaterials++;
                    }
                }

                if (tunedRenderer)
                {
                    tunedRenderers++;
                }
            }

            SydneyFloodlightIntensity[] floodlightControllers = arenaRoot.GetComponentsInChildren<SydneyFloodlightIntensity>(true);
            for (int controllerIndex = 0; controllerIndex < floodlightControllers.Length; controllerIndex++)
            {
                floodlightControllers[controllerIndex].SetRenderingLayerMask((int)floodlightRenderingLayer);
            }

            arenaRoot.AddComponent<SydneyIceReflectionFilterMarker>();
            Debug.Log("[SydneyTLANArena] Ice reflection filter applied to " + tunedRenderers +
                      " renderer(s) and " + tunedMaterials + " material(s): floodlights excluded from the ice rendering layer; rectangular reflection contribution retained.");
        }
        catch (Exception ex)
        {
            Debug.LogError("[SydneyTLANArena] Could not apply the ice reflection filter: " + ex);
        }
    }

    private static void ApplyOverheadNettingVisibility(GameObject arenaRoot)
    {
        SydneyAdapterConfig config = _adapterConfig ?? LoadAdapterConfig();
        if (config.overheadNettingEnabled)
        {
            Debug.Log("[SydneyTLANArena] Overhead netting is enabled in SydneyTLANArena.json.");
            return;
        }

        Renderer[] sceneRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        Renderer[] arenaRenderers = arenaRoot.GetComponentsInChildren<Renderer>(true);
        HashSet<Renderer> renderers = new HashSet<Renderer>();
        for (int i = 0; i < sceneRenderers.Length; i++)
        {
            if (sceneRenderers[i] != null)
            {
                renderers.Add(sceneRenderers[i]);
            }
        }
        for (int i = 0; i < arenaRenderers.Length; i++)
        {
            if (arenaRenderers[i] != null)
            {
                renderers.Add(arenaRenderers[i]);
            }
        }

        int hidden = 0;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !IsOverheadNetting(renderer))
            {
                continue;
            }

            renderer.enabled = false;
            hidden++;
        }

        Debug.Log("[SydneyTLANArena] Overhead netting is disabled by default; hidden " + hidden + " renderer(s). The Sydney Goal_Nets end-netting mesh was included; goal-cage netting remains excluded.");
    }

    private static bool IsOverheadNetting(Renderer renderer)
    {
        string path = GetTransformPath(renderer.transform);
        bool sydneyGoalNets = path.IndexOf("OplSydney_Model/Goal_Nets", StringComparison.OrdinalIgnoreCase) >= 0;
        if (sydneyGoalNets)
        {
            // The Sydney bundle does not contain a separate object named for the
            // upper end-netting. Its Goal_Nets renderer is the end-netting mesh:
            // world Z +/-45.9, world Y 2.93..6.35. It is not the goal cage net.
            return true;
        }

        if (path.IndexOf("Goal", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("Goal Nets", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("GoalNet", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        string objectName = renderer.gameObject.name;
        string materialName = string.Empty;
        Material[] materials = renderer.sharedMaterials;
        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] != null)
            {
                materialName += " " + materials[i].name;
            }
        }

        bool namedNetting = path.IndexOf("netting", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            path.IndexOf("net", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            materialName.IndexOf("net", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!namedNetting)
        {
            return false;
        }

        Vector3 centre = renderer.bounds.center;
        return centre.y > 3.5f && renderer.bounds.max.y > 4.5f ||
               objectName.IndexOf("netting", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string GetTransformPath(Transform transform)
    {
        string path = transform == null ? string.Empty : transform.name;
        Transform parent = transform == null ? null : transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }

    private static bool LooksLikeIce(Renderer renderer)
    {
        string objectName = renderer.gameObject.name;
        string materialName = string.Empty;
        Material[] materials = renderer.sharedMaterials;
        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] != null)
            {
                materialName += " " + materials[i].name;
            }
        }

        bool namedIce = objectName.IndexOf("ice", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        materialName.IndexOf("ice", StringComparison.OrdinalIgnoreCase) >= 0;
        Vector3 size = renderer.bounds.size;
        if (namedIce && size.x > 20f && size.z > 20f && size.y < 5f)
        {
            return true;
        }

        string objectNameLower = objectName.ToLowerInvariant();
        if ((objectNameLower == "rink" || objectNameLower == "rink floor" || objectNameLower == "floor") &&
            size.x > 50f && size.z > 25f && size.y < 5f)
        {
            return true;
        }

        Vector3 centre = renderer.bounds.center;
        return size.x > 60f && size.x < 110f &&
               size.z > 30f && size.z < 70f &&
               size.y < 3f && centre.y > -2f && centre.y < 4f;
    }

    private static SydneyAdapterConfig LoadAdapterConfig()
    {
        SydneyAdapterConfig config = new SydneyAdapterConfig();
        try
        {
            string pluginDirectory = Path.GetDirectoryName(typeof(TlanSydneyAdapter).Assembly.Location);
            string configPath = Path.Combine(pluginDirectory, "SydneyTLANArena.json");
            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);
                SydneyAdapterConfig loaded = JsonUtility.FromJson<SydneyAdapterConfig>(json);
                if (loaded != null)
                {
                    if (json.IndexOf("\"oplArenaEnabled\"", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        loaded.oplArenaEnabled = true;
                    }
                    config = loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SydneyTLANArena] Could not read SydneyTLANArena.json; using default floodlight intensity settings: " + ex.Message);
        }

        config.floodlightIntensityMultiplier = Mathf.Clamp(config.floodlightIntensityMultiplier, 1f, 5f);
        config.floodlightIntensityMax = Mathf.Clamp(config.floodlightIntensityMax, 1f, 2500f);
        config.floodlightEmissionMultiplier = Mathf.Clamp(config.floodlightEmissionMultiplier, 0.1f, 10f);
        if (config.floodlightReflectionMultiplier < 0f)
        {
            config.floodlightReflectionMultiplier = 1f;
        }
        config.floodlightReflectionMultiplier = Mathf.Clamp(config.floodlightReflectionMultiplier, 0f, 5f);
        if (config.goalSirenIntensityMultiplier <= 0f)
        {
            config.goalSirenIntensityMultiplier = 6f;
        }
        config.goalSirenIntensityMultiplier = Mathf.Clamp(config.goalSirenIntensityMultiplier, 0.1f, 10f);
        Debug.Log("[SydneyTLANArena] Floodlight intensity multiplier=" + config.floodlightIntensityMultiplier.ToString("0.##") +
                  "x, effective maximum=" + config.floodlightIntensityMax.ToString("0.#") + ".");
        return config;
    }

    private static void SaveAdapterConfig(SydneyAdapterConfig config)
    {
        try
        {
            string pluginDirectory = Path.GetDirectoryName(typeof(TlanSydneyAdapter).Assembly.Location);
            string configPath = Path.Combine(pluginDirectory, "SydneyTLANArena.json");
            File.WriteAllText(configPath, JsonUtility.ToJson(config, true));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SydneyTLANArena] Could not save the OPL ARENA setting: " + ex.Message);
        }
    }

    private static TlanMenuRenderConfig LoadTlanMenuRenderConfig()
    {
        TlanMenuRenderConfig config = new TlanMenuRenderConfig();
        try
        {
            string pluginDirectory = Path.GetDirectoryName(typeof(TlanSydneyAdapter).Assembly.Location);
            string puckDirectory = Path.GetFullPath(Path.Combine(pluginDirectory, "..", ".."));
            string configPath = Path.Combine(puckDirectory, "config", "TLANArena", "client.json");
            if (!File.Exists(configPath) && !string.IsNullOrEmpty(Application.dataPath))
            {
                string applicationPuckDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                configPath = Path.Combine(applicationPuckDirectory, "config", "TLANArena", "client.json");
            }
            if (File.Exists(configPath))
            {
                TlanMenuRenderConfig loaded = JsonUtility.FromJson<TlanMenuRenderConfig>(File.ReadAllText(configPath));
                if (loaded != null)
                {
                    config = loaded;
                }
            }
            else
            {
                Debug.LogWarning("[SydneyTLANArena] TLAN client.json was not found; using the menu's 12:00 AM fallback render profile.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SydneyTLANArena] Could not read TLAN client.json for the locker-room render profile; using defaults: " + ex.Message);
        }

        config.manualHour = Mathf.Repeat(config.manualHour, 24f);
        config.sunIntensityScale = Mathf.Max(0f, config.sunIntensityScale);
        config.floodlightIntensity = Mathf.Max(0f, config.floodlightIntensity);
        config.floodlightOffIntensity = Mathf.Max(0f, config.floodlightOffIntensity);
        config.bloomIntensity = Mathf.Max(0f, config.bloomIntensity);
        config.fogNightStart = Mathf.Max(0f, config.fogNightStart);
        config.fogNightEnd = Mathf.Max(config.fogNightStart + 1f, config.fogNightEnd);
        config.fogDayStart = Mathf.Max(0f, config.fogDayStart);
        config.fogDayEnd = Mathf.Max(config.fogDayStart + 1f, config.fogDayEnd);
        return config;
    }

    internal static bool TryGetIndependentFloodlightState(GameObject arenaRoot, out bool enabled, out float baseIntensity, out Color colour)
    {
        TlanMenuRenderConfig config = _runtimeLightingConfig ?? (_runtimeLightingConfig = LoadTlanMenuRenderConfig());
        baseIntensity = Mathf.Max(0f, config.floodlightIntensity);
        colour = config.floodlightColorEnabled ? config.floodlightColor : Color.white;

        if (!config.floodlightsEnabled)
        {
            enabled = config.floodlightOffIntensity > 0.01f;
            baseIntensity = Mathf.Max(0f, config.floodlightOffIntensity);
            return true;
        }

        float sunElevation;
        if (TryGetSunElevation(arenaRoot, out sunElevation))
        {
            enabled = sunElevation <= config.floodlightOnElevationDeg;
            return true;
        }

        // Compatibility fallback for TLAN builds that expose neither active
        // native lamps nor a usable directional sun. Manual settings remain
        // deterministic; real-time settings use the local clock, matching
        // TLAN's default host-local fallback.
        if (string.Equals(config.timeSource, "Manual", StringComparison.OrdinalIgnoreCase))
        {
            enabled = config.GetNightFactor() > 0.01f;
        }
        else
        {
            float hour = (float)DateTime.Now.TimeOfDay.TotalHours;
            enabled = hour < 6f || hour >= 18f;
        }

        return true;
    }

    private static bool TryGetSunElevation(GameObject arenaRoot, out float elevation)
    {
        Light bestSun = RenderSettings.sun;
        if (bestSun == null || bestSun.type != LightType.Directional)
        {
            if (Time.unscaledTime >= _nextRuntimeSunSearch || _runtimeSun == null)
            {
                _runtimeSun = null;
                Light[] sceneLights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                float bestIntensity = -1f;
                for (int i = 0; i < sceneLights.Length; i++)
                {
                    Light candidate = sceneLights[i];
                    if (candidate == null || candidate.type != LightType.Directional ||
                        (arenaRoot != null && candidate.transform.IsChildOf(arenaRoot.transform)) ||
                        candidate.gameObject.name.IndexOf("Flood", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    if (candidate.intensity > bestIntensity)
                    {
                        _runtimeSun = candidate;
                        bestIntensity = candidate.intensity;
                    }
                }

                _nextRuntimeSunSearch = Time.unscaledTime + 1f;
            }

            bestSun = _runtimeSun;
        }

        if (bestSun == null)
        {
            elevation = 0f;
            return false;
        }

        Vector3 lightDirection = -bestSun.transform.forward;
        elevation = Mathf.Asin(Mathf.Clamp(lightDirection.y, -1f, 1f)) * Mathf.Rad2Deg;
        return true;
    }

    private static bool IsSydneySkyRenderer(Renderer renderer)
    {
        string objectName = renderer.gameObject.name;
        if (IsSkyName(objectName))
        {
            return true;
        }

        Material[] materials = renderer.sharedMaterials;
        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material != null && IsSkyName(material.name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSkyName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        string normalized = name.Replace(" (Instance)", string.Empty).Trim();
        return string.Equals(normalized, "Sky", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "env_Sky", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LoadSydneyPrefab()
    {
        if (_sydneyPrefab != null && _sydneyBundle != null)
        {
            return true;
        }

        string bundlePath = GetSydneyBundlePath();
        if (!File.Exists(bundlePath))
        {
            Debug.LogError("[SydneyTLANArena] Sydney asset bundle was not found at " + bundlePath);
            return false;
        }

        _sydneyBundle = AssetBundle.LoadFromFile(bundlePath);
        if (_sydneyBundle == null)
        {
            Debug.LogError("[SydneyTLANArena] Unity could not load the Sydney asset bundle.");
            return false;
        }

        string[] assetNames = _sydneyBundle.GetAllAssetNames();
        string prefabPath = null;
        for (int i = 0; i < assetNames.Length; i++)
        {
            string assetName = assetNames[i];
            if (string.Equals(assetName, "assets/oplsydney/arena.prefab", StringComparison.OrdinalIgnoreCase) ||
                (assetName.EndsWith("/arena.prefab", StringComparison.OrdinalIgnoreCase) && prefabPath == null))
            {
                prefabPath = assetName;
                if (string.Equals(assetName, "assets/oplsydney/arena.prefab", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }

        if (prefabPath == null)
        {
            Debug.LogError("[SydneyTLANArena] The Sydney asset bundle does not contain Arena.prefab.");
            return false;
        }

        _sydneyPrefab = _sydneyBundle.LoadAsset<GameObject>(prefabPath);
        if (_sydneyPrefab == null)
        {
            Debug.LogError("[SydneyTLANArena] Could not load Sydney Arena.prefab from the asset bundle.");
            return false;
        }

        Debug.Log("[SydneyTLANArena] Loaded Sydney arena prefab from " + bundlePath);
        return true;
    }

    private static string GetSydneyBundlePath()
    {
        string pluginDirectory = Path.GetDirectoryName(typeof(TlanSydneyAdapter).Assembly.Location);
        return Path.Combine(pluginDirectory, "AssetBundles", "oplsydney");
    }

    public static void Shutdown()
    {
        ClearMenuPreview();

        if (_harmony != null)
        {
            _harmony.UnpatchSelf();
        }

        _patchInstalled = false;
        _arenaPrefabProperty = null;
        _bundleFolderProperty = null;
        _arenaRootProperty = null;
        _adapterConfig = null;
        _runtimeLightingConfig = null;
        _runtimeSun = null;
        _nextRuntimeSunSearch = 0f;
        _sydneyPrefab = null;
        _settingsToggleAdded = false;
        _settingsPatchInstalled = false;

        if (_sydneyBundle != null)
        {
            _sydneyBundle.Unload(false);
            _sydneyBundle = null;
        }
    }
}

internal sealed class SydneyAdapterConfig
{
    public bool oplArenaEnabled = true;
    public float floodlightIntensityMultiplier = 5f;
    public float floodlightIntensityMax = 2500f;
    public float floodlightEmissionMultiplier = 3f;
    public float floodlightReflectionMultiplier = 1f;
    public bool floodlightShadows = false;
    public float goalSirenIntensityMultiplier = 6f;
    public bool iceReflectionFilter = true;
    public bool overheadNettingEnabled = false;
}

internal sealed class TlanMenuRenderConfig
{
    public float manualHour = 0f;
    public string timeSource = "Manual";
    public float sunIntensityScale = 1.4566f;
    public bool floodlightsEnabled = true;
    public float floodlightIntensity = 401.7f;
    public float floodlightOffIntensity = 0f;
    public bool floodlightColorEnabled = true;
    public Color floodlightColor = new Color(1f, 0.7725f, 0.5608f, 1f);
    public bool bloomEnabled = true;
    public float bloomThreshold = 1f;
    public float bloomIntensity = 1.12f;
    public float bloomScatter = 0.7f;
    public string toneMapping = "neutral";
    public float toneMappingExposure = 0.35f;
    public float toneMappingContrast = 10f;
    public bool fogEnabled = true;
    public float fogNightStart = 50f;
    public float fogNightEnd = 200f;
    public float fogDayStart = 400f;
    public float fogDayEnd = 1400f;
    public float floodlightOnElevationDeg = 3f;
    public float floodlightHysteresisDeg = 2f;
    public Color fogNightColor = new Color(0.1f, 0.13f, 0.2f, 1f);
    public Color fogDayColor = new Color(0.68f, 0.74f, 0.82f, 1f);

    public float GetNightFactor()
    {
        float hour = manualHour;
        while (hour < 0f)
        {
            hour += 24f;
        }
        while (hour >= 24f)
        {
            hour -= 24f;
        }

        if (hour >= 6f && hour < 18f)
        {
            return 0f;
        }
        if (hour >= 5f && hour < 6f)
        {
            return 1f - (hour - 5f);
        }
        if (hour >= 18f && hour < 19f)
        {
            return hour - 18f;
        }
        return 1f;
    }

    public float GetEffectiveFloodlightIntensity(SydneyAdapterConfig adapterConfig)
    {
        if (!floodlightsEnabled)
        {
            return Mathf.Max(0f, floodlightOffIntensity);
        }

        float baseIntensity = Mathf.Max(0f, floodlightIntensity);
        float multiplier = adapterConfig == null ? 1f : Mathf.Max(0f, adapterConfig.floodlightIntensityMultiplier);
        float maximum = adapterConfig == null ? 2500f : Mathf.Max(1f, adapterConfig.floodlightIntensityMax);
        return Mathf.Clamp(baseIntensity * multiplier, 0f, maximum);
    }
}

internal sealed class SydneyMenuBackdropMarker : MonoBehaviour
{
}

internal sealed class SydneyMenuPostProcessingMarker : MonoBehaviour
{
    private UnityEngine.Object _profile;

    public void Initialize(UnityEngine.Object profile)
    {
        _profile = profile;
    }

    private void OnDestroy()
    {
        if (_profile != null)
        {
            UnityEngine.Object.Destroy(_profile);
            _profile = null;
        }
    }
}

internal sealed class SydneyMenuBakedReflection : MonoBehaviour
{
    private const int MaskSize = 64;
    private const float IceHalfWidth = 30f;
    private const float IceHalfLength = 42f;
    private const float CookieHalfWidth = 0.82f;
    private const float CookieHalfHeight = 0.61f;
    private const float CookieFeather = 0.055f;
    private static readonly Vector3[] FloodlightPositions =
    {
        new Vector3(-48.22f, 42f, -71.46f),
        new Vector3(-48.22f, 42f, 71.46f),
        new Vector3(48.22f, 42f, -71.46f),
        new Vector3(48.22f, 42f, 71.46f)
    };
    private static readonly Vector3[] FloodlightDirections =
    {
        new Vector3(0.7071f, 0f, 0.7071f),
        new Vector3(0.7071f, 0f, -0.7071f),
        new Vector3(-0.7071f, 0f, 0.7071f),
        new Vector3(-0.7071f, 0f, -0.7071f)
    };
    private Material _material;
    private Texture2D _maskTexture;
    private Mesh _mesh;

    public void Initialize(float intensity, float reflectionMultiplier, Color reflectionColour)
    {
        CreateReflectionLayer(intensity, reflectionMultiplier, reflectionColour);
    }

    private void CreateReflectionLayer(float intensity, float reflectionMultiplier, Color reflectionColour)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
        if (shader == null)
        {
            Debug.LogWarning("[SydneyTLANArena] No unlit shader was found; baked locker-room floodlight reflections are unavailable.");
            return;
        }

        _maskTexture = new Texture2D(MaskSize, MaskSize, TextureFormat.RGBA32, false, true)
        {
            name = "Sydney Menu Baked Floodlight Reflection Mask (Runtime)",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] pixels = new Color[MaskSize * MaskSize];
        for (int y = 0; y < MaskSize; y++)
        {
            float z = Mathf.Lerp(-IceHalfLength, IceHalfLength, (y + 0.5f) / MaskSize);
            for (int x = 0; x < MaskSize; x++)
            {
                float xPosition = Mathf.Lerp(-IceHalfWidth, IceHalfWidth, (x + 0.5f) / MaskSize);
                float mask = 0f;
                Vector3 target = new Vector3(xPosition, 0.025f, z);
                for (int lightIndex = 0; lightIndex < FloodlightPositions.Length; lightIndex++)
                {
                    Vector3 source = FloodlightPositions[lightIndex];
                    Vector3 direction = FloodlightDirections[lightIndex].normalized;
                    Vector3 ray = target - source;
                    float distance = ray.magnitude;
                    if (distance < 0.1f)
                    {
                        continue;
                    }

                    Vector3 rayDirection = ray / distance;
                    float coneLimit = Mathf.Cos(95f * 0.5f * Mathf.Deg2Rad);
                    if (Vector3.Dot(rayDirection, direction) < coneLimit)
                    {
                        continue;
                    }

                    float depth = Vector3.Dot(ray, direction);
                    if (depth <= 0.1f)
                    {
                        continue;
                    }

                    Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
                    float cookieScale = Mathf.Tan(95f * 0.5f * Mathf.Deg2Rad);
                    float cookieX = Vector3.Dot(ray, right) / depth / cookieScale;
                    float cookieY = Vector3.Dot(ray, Vector3.up) / depth / cookieScale;
                    float distanceToEdge = Mathf.Min(CookieHalfWidth - Mathf.Abs(cookieX), CookieHalfHeight - Mathf.Abs(cookieY));
                    float lightMask = Mathf.Clamp01(distanceToEdge / CookieFeather);
                    mask = Mathf.Max(mask, lightMask);
                }

                pixels[y * MaskSize + x] = new Color(mask, mask, mask, mask);
            }
        }

        _maskTexture.SetPixels(pixels);
        _maskTexture.Apply(false, true);

        float normalizedIntensity = Mathf.Clamp01(intensity / 2000f);
        float strength = Mathf.Lerp(0.05f, 1.25f, normalizedIntensity) * Mathf.Clamp(reflectionMultiplier, 0f, 5f) * 0.65f;
        Color colour = new Color(reflectionColour.r * strength, reflectionColour.g * strength, reflectionColour.b * strength, 1f);
        _material = new Material(shader)
        {
            name = "Sydney Menu Baked Floodlight Reflection (Runtime)",
            renderQueue = 3000
        };
        _material.SetTexture("_BaseMap", _maskTexture);
        _material.SetTexture("_MainTex", _maskTexture);
        _material.SetColor("_BaseColor", colour);
        if (_material.HasProperty("_Color"))
        {
            _material.SetColor("_Color", colour);
        }
        _material.SetInt("_Cull", 0);
        if (_material.HasProperty("_Surface"))
        {
            _material.SetFloat("_Surface", 1f);
        }
        if (_material.HasProperty("_Blend"))
        {
            _material.SetFloat("_Blend", 2f);
        }
        if (_material.HasProperty("_SrcBlend"))
        {
            _material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        }
        if (_material.HasProperty("_DstBlend"))
        {
            _material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        }
        if (_material.HasProperty("_ZWrite"))
        {
            _material.SetFloat("_ZWrite", 0f);
        }

        GameObject reflectionObject = new GameObject("Sydney Menu Baked Floodlight Reflection");
        reflectionObject.transform.SetParent(transform, false);
        MeshFilter meshFilter = reflectionObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = reflectionObject.AddComponent<MeshRenderer>();
        _mesh = new Mesh { name = "Sydney Menu Baked Floodlight Reflection Plane (Runtime)" };
        _mesh.vertices = new[]
        {
            new Vector3(-IceHalfWidth, 0.025f, -IceHalfLength),
            new Vector3(-IceHalfWidth, 0.025f, IceHalfLength),
            new Vector3(IceHalfWidth, 0.025f, IceHalfLength),
            new Vector3(IceHalfWidth, 0.025f, -IceHalfLength)
        };
        _mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0f)
        };
        _mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        _mesh.RecalculateNormals();
        meshFilter.sharedMesh = _mesh;
        meshRenderer.sharedMaterial = _material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        Debug.Log("[SydneyTLANArena] Created softened neutral rectangular floodlight reflection snapshot for the locker-room ice (64x64, menu-only).");
    }

    private void OnDestroy()
    {
        if (_material != null)
        {
            UnityEngine.Object.Destroy(_material);
        }
        if (_maskTexture != null)
        {
            UnityEngine.Object.Destroy(_maskTexture);
        }
        if (_mesh != null)
        {
            UnityEngine.Object.Destroy(_mesh);
        }
    }
}

internal sealed class SydneyIceReflectionFilterMarker : MonoBehaviour
{
}

internal sealed class SydneyFloodlightIntensity : MonoBehaviour
{
    private const float StatePollInterval = 0.1f;
    private Light[] _lights;
    private Light[] _nativeFloodlights;
    private float[] _baseIntensities;
    private float[] _lastAppliedIntensities;
    private bool[] _lastAppliedEnabled;
    private Color[] _lastAppliedColours;
    private float _multiplier;
    private float _maximum;
    private float _fallbackBaseIntensity;
    private int _renderingLayerMask = -1;
    private bool _shadowsEnabled = true;
    private GameObject _arenaRoot;
    private float _nextStatePoll;
    private bool _disabledStateApplied;
    private bool _externalRuntimeControlObserved;
    private bool _externalRuntimeLightsOn;
    private float _externalRuntimeIntensity;
    private Color _externalRuntimeColour = Color.white;
    private bool _externalControlLogged;

    public void Initialize(Light[] lights, float multiplier, float maximum, Light[] nativeFloodlights, float fallbackBaseIntensity, GameObject arenaRoot)
    {
        _lights = lights;
        _nativeFloodlights = nativeFloodlights;
        _multiplier = multiplier;
        _maximum = maximum;
        _fallbackBaseIntensity = Mathf.Max(0f, fallbackBaseIntensity);
        _arenaRoot = arenaRoot;
        _baseIntensities = new float[lights.Length];
        _lastAppliedIntensities = new float[lights.Length];
        _lastAppliedEnabled = new bool[lights.Length];
        _lastAppliedColours = new Color[lights.Length];
        for (int i = 0; i < lights.Length; i++)
        {
            float actualIntensity = lights[i] == null ? 0f : lights[i].intensity;
            _baseIntensities[i] = actualIntensity;
            if (_baseIntensities[i] <= 0f && (_nativeFloodlights == null || _nativeFloodlights.Length == 0))
            {
                _baseIntensities[i] = _fallbackBaseIntensity;
            }
            _lastAppliedIntensities[i] = actualIntensity;
            _lastAppliedEnabled[i] = lights[i] != null && lights[i].enabled;
            _lastAppliedColours[i] = lights[i] == null ? Color.white : lights[i].color;
        }
    }

    public void SetRenderingLayerMask(int renderingLayerMask)
    {
        _renderingLayerMask = renderingLayerMask;
        if (_lights == null)
        {
            return;
        }

        for (int i = 0; i < _lights.Length; i++)
        {
            if (_lights[i] != null)
            {
                _lights[i].renderingLayerMask = _renderingLayerMask;
            }
        }
    }

    public void SetShadowsEnabled(bool enabled)
    {
        _shadowsEnabled = enabled;
    }

    public void ConfigurePreview(float multiplier, float maximum, float baseIntensity)
    {
        _multiplier = Mathf.Max(0f, multiplier);
        _maximum = Mathf.Max(1f, maximum);
        _shadowsEnabled = false;
        if (_lights == null)
        {
            return;
        }

        for (int i = 0; i < _lights.Length; i++)
        {
            _baseIntensities[i] = baseIntensity;
            _lastAppliedIntensities[i] = 0f;
            if (_lights[i] != null)
            {
                _lights[i].intensity = 0f;
            }
        }
    }

    private void LateUpdate()
    {
        if (_lights == null)
        {
            return;
        }

        if (Time.unscaledTime < _nextStatePoll)
        {
            return;
        }
        _nextStatePoll = Time.unscaledTime + StatePollInterval;

        if (!TlanSydneyAdapter.IsOplArenaEnabled())
        {
            if (_disabledStateApplied)
            {
                return;
            }
            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lights[i] != null)
                {
                    _lights[i].intensity = 0f;
                    _lights[i].enabled = false;
                    _lastAppliedIntensities[i] = 0f;
                    _lastAppliedEnabled[i] = false;
                    _lastAppliedColours[i] = _lights[i].color;
                }
            }
            _disabledStateApplied = true;
            return;
        }
        _disabledStateApplied = false;

        bool nativeLightsAvailable = _nativeFloodlights != null && _nativeFloodlights.Length > 0;
        bool nativeLightsOn = false;
        float nativeIntensity = 0f;
        Color nativeColor = Color.white;
        if (nativeLightsAvailable)
        {
            for (int i = 0; i < _nativeFloodlights.Length; i++)
            {
                Light nativeLight = _nativeFloodlights[i];
                if (nativeLight == null || !nativeLight.enabled || nativeLight.intensity <= 0.01f)
                {
                    continue;
                }

                nativeLightsOn = true;
                if (nativeLight.intensity > nativeIntensity)
                {
                    nativeIntensity = nativeLight.intensity;
                    nativeColor = nativeLight.color;
                }
            }
        }

        if (!nativeLightsAvailable)
        {
            bool runtimeStateChangedExternally = false;
            bool observedOn = false;
            float observedIntensity = 0f;
            Color observedColour = _externalRuntimeColour;
            for (int i = 0; i < _lights.Length; i++)
            {
                Light runtimeLight = _lights[i];
                if (runtimeLight == null)
                {
                    continue;
                }

                if (Mathf.Abs(runtimeLight.intensity - _lastAppliedIntensities[i]) > 0.01f ||
                    runtimeLight.enabled != _lastAppliedEnabled[i] ||
                    !Approximately(runtimeLight.color, _lastAppliedColours[i]))
                {
                    runtimeStateChangedExternally = true;
                }

                observedColour = runtimeLight.color;
                if (runtimeLight.enabled && runtimeLight.intensity > 0.01f)
                {
                    observedOn = true;
                    if (runtimeLight.intensity > observedIntensity)
                    {
                        observedIntensity = runtimeLight.intensity;
                        observedColour = runtimeLight.color;
                    }
                }
            }

            if (runtimeStateChangedExternally)
            {
                _externalRuntimeControlObserved = true;
                _externalRuntimeLightsOn = observedOn;
                _externalRuntimeIntensity = observedIntensity;
                _externalRuntimeColour = observedColour;
                if (!_externalControlLogged)
                {
                    Debug.Log("[SydneyTLANArena] TLAN is controlling the Companion runtime floodlights directly; native intensity, colour, and on/off state now take priority over the independent fallback.");
                    _externalControlLogged = true;
                }
            }
        }

        bool independentStateAvailable = TlanSydneyAdapter.TryGetIndependentFloodlightState(
            _arenaRoot,
            out bool independentLightsOn,
            out float independentIntensity,
            out Color independentColor);

        if (!nativeLightsAvailable && _externalRuntimeControlObserved)
        {
            nativeLightsOn = _externalRuntimeLightsOn;
            nativeIntensity = _externalRuntimeIntensity;
            nativeColor = _externalRuntimeColour;
        }
        // If neither authored Sydney lights nor direct TLAN writes are
        // available, Companion owns the equivalent day/night decision.
        else if (!nativeLightsAvailable && independentStateAvailable)
        {
            nativeLightsOn = independentLightsOn;
            nativeIntensity = independentLightsOn ? independentIntensity : 0f;
            nativeColor = independentColor;
        }

        for (int i = 0; i < _lights.Length; i++)
        {
            Light light = _lights[i];
            if (light == null)
            {
                continue;
            }

            if (nativeLightsAvailable || _externalRuntimeControlObserved || independentStateAvailable)
            {
                _baseIntensities[i] = nativeLightsOn ? nativeIntensity : 0f;
                if (!Approximately(light.color, nativeColor))
                {
                    light.color = nativeColor;
                }
            }
            else if (Mathf.Abs(light.intensity - _lastAppliedIntensities[i]) > 0.01f)
            {
                _baseIntensities[i] = light.intensity;
            }

            float scaledIntensity = Mathf.Clamp(_baseIntensities[i] * _multiplier, 0f, _maximum);
            bool shouldBeEnabled = scaledIntensity > 0.01f;
            LightShadows desiredShadows = _shadowsEnabled ? LightShadows.Soft : LightShadows.None;
            float desiredShadowStrength = _shadowsEnabled ? 0.85f : 0f;
            if (Mathf.Abs(light.intensity - scaledIntensity) > 0.01f)
            {
                light.intensity = scaledIntensity;
            }
            if (light.enabled != shouldBeEnabled)
            {
                light.enabled = shouldBeEnabled;
            }
            if (light.cullingMask != ~0)
            {
                light.cullingMask = ~0;
            }
            if (light.renderingLayerMask != _renderingLayerMask)
            {
                light.renderingLayerMask = _renderingLayerMask;
            }
            if (light.shadows != desiredShadows)
            {
                light.shadows = desiredShadows;
            }
            if (Mathf.Abs(light.shadowStrength - desiredShadowStrength) > 0.001f)
            {
                light.shadowStrength = desiredShadowStrength;
            }
            if (light.range < 220f)
            {
                light.range = 220f;
            }
            _lastAppliedIntensities[i] = scaledIntensity;
            _lastAppliedEnabled[i] = shouldBeEnabled;
            _lastAppliedColours[i] = light.color;
        }
    }

    private static bool Approximately(Color left, Color right)
    {
        return Mathf.Abs(left.r - right.r) < 0.001f &&
               Mathf.Abs(left.g - right.g) < 0.001f &&
               Mathf.Abs(left.b - right.b) < 0.001f &&
               Mathf.Abs(left.a - right.a) < 0.001f;
    }
}

internal sealed class SydneyIceReflectionContribution : MonoBehaviour
{
    private const int MaskSize = 64;
    private const int BakeRowsPerFrame = 4;
    private const float StatePollInterval = 0.1f;
    private const float IceHalfWidth = 30f;
    private const float IceHalfLength = 42f;
    private const float CookieHalfWidth = 0.82f;
    private const float CookieHalfHeight = 0.61f;
    private const float CookieFeather = 0.025f;
    private Light[] _lights;
    private float _maximumIntensity;
    private float _reflectionMultiplier;
    private Material _material;
    private Texture2D _maskTexture;
    private static Color[] _cachedMaskPixels;
    private float _nextStatePoll;
    private Color _lastAppliedColour;
    private bool _hasAppliedColour;
    private bool _hasLegacyColourProperty;

    public void Initialize(Light[] lights, float maximumIntensity, float reflectionMultiplier)
    {
        _lights = lights;
        _maximumIntensity = Mathf.Max(1f, maximumIntensity);
        _reflectionMultiplier = Mathf.Clamp(reflectionMultiplier, 0f, 5f);
        CreateReflectionLayer();
    }

    public void ConfigurePreview(float maximumIntensity)
    {
        _maximumIntensity = Mathf.Max(1f, maximumIntensity);
    }

    private void LateUpdate()
    {
        if (_material == null || _lights == null)
        {
            return;
        }

        if (Time.unscaledTime < _nextStatePoll)
        {
            return;
        }
        _nextStatePoll = Time.unscaledTime + StatePollInterval;

        if (!TlanSydneyAdapter.IsOplArenaEnabled())
        {
            ApplyReflectionColour(Color.black);
            return;
        }

        bool on = false;
        float intensity = 0f;
        Color colour = Color.white;
        for (int i = 0; i < _lights.Length; i++)
        {
            Light light = _lights[i];
            if (light == null || light.intensity <= 0f)
            {
                continue;
            }

            on = true;
            intensity = Mathf.Max(intensity, light.intensity);
            colour = light.color;
        }

        float normalizedIntensity = Mathf.Clamp01(intensity / _maximumIntensity);
        float strength = on ? Mathf.Lerp(0.08f, 1.5f, normalizedIntensity) * _reflectionMultiplier : 0f;
        ApplyReflectionColour(new Color(colour.r * strength, colour.g * strength, colour.b * strength, 1f));
    }

    private void ApplyReflectionColour(Color colour)
    {
        if (_hasAppliedColour && Approximately(_lastAppliedColour, colour))
        {
            return;
        }

        _material.SetColor("_BaseColor", colour);
        if (_hasLegacyColourProperty)
        {
            _material.SetColor("_Color", colour);
        }
        _lastAppliedColour = colour;
        _hasAppliedColour = true;
    }

    private static bool Approximately(Color left, Color right)
    {
        return Mathf.Abs(left.r - right.r) < 0.001f &&
               Mathf.Abs(left.g - right.g) < 0.001f &&
               Mathf.Abs(left.b - right.b) < 0.001f &&
               Mathf.Abs(left.a - right.a) < 0.001f;
    }

    private void CreateReflectionLayer()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
        if (shader == null)
        {
            Debug.LogWarning("[SydneyTLANArena] No unlit shader was found; custom floodlight ice reflections are unavailable.");
            return;
        }

        _maskTexture = new Texture2D(MaskSize, MaskSize, TextureFormat.RGBA32, false, true)
        {
            name = "Sydney Floodlight Ice Reflection Mask (Runtime)",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        _material = new Material(shader)
        {
            name = "Sydney Floodlight Ice Reflection Contribution (Runtime)",
            renderQueue = 3000
        };
        _material.SetTexture("_BaseMap", _maskTexture);
        _material.SetTexture("_MainTex", _maskTexture);
        _material.SetColor("_BaseColor", Color.black);
        _hasLegacyColourProperty = _material.HasProperty("_Color");
        _material.SetInt("_Cull", 0);
        if (_material.HasProperty("_Surface"))
        {
            _material.SetFloat("_Surface", 1f);
        }
        if (_material.HasProperty("_Blend"))
        {
            _material.SetFloat("_Blend", 2f);
        }
        if (_material.HasProperty("_SrcBlend"))
        {
            _material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        }
        if (_material.HasProperty("_DstBlend"))
        {
            _material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        }
        if (_material.HasProperty("_ZWrite"))
        {
            _material.SetFloat("_ZWrite", 0f);
        }

        GameObject reflectionObject = new GameObject("Sydney Floodlight Ice Reflection Contribution");
        reflectionObject.transform.SetParent(transform, false);
        MeshFilter meshFilter = reflectionObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = reflectionObject.AddComponent<MeshRenderer>();
        Mesh mesh = new Mesh { name = "Sydney Floodlight Ice Reflection Plane (Runtime)" };
        mesh.vertices = new[]
        {
            new Vector3(-IceHalfWidth, 0.02f, -IceHalfLength),
            new Vector3(-IceHalfWidth, 0.02f, IceHalfLength),
            new Vector3(IceHalfWidth, 0.02f, IceHalfLength),
            new Vector3(IceHalfWidth, 0.02f, -IceHalfLength)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        meshFilter.sharedMesh = mesh;
        meshRenderer.sharedMaterial = _material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        if (_cachedMaskPixels != null && _cachedMaskPixels.Length == MaskSize * MaskSize)
        {
            _maskTexture.SetPixels(_cachedMaskPixels);
            _maskTexture.Apply(false, true);
            Debug.Log("[SydneyTLANArena] Reused the cached rectangular floodlight reflection mask.");
        }
        else
        {
            Color[] blankPixels = new Color[MaskSize * MaskSize];
            _maskTexture.SetPixels(blankPixels);
            _maskTexture.Apply(false, false);
            StartCoroutine(BakeReflectionMask(blankPixels));
            Debug.Log("[SydneyTLANArena] Created custom rectangular floodlight reflection contribution; static occlusion mask will bake incrementally.");
        }
    }

    private IEnumerator BakeReflectionMask(Color[] pixels)
    {
        int lightCount = _lights == null ? 0 : _lights.Length;
        bool[] validLights = new bool[lightCount];
        Vector3[] sources = new Vector3[lightCount];
        Vector3[] directions = new Vector3[lightCount];
        Vector3[] rights = new Vector3[lightCount];
        float[] coneLimits = new float[lightCount];
        float[] cookieScales = new float[lightCount];
        for (int lightIndex = 0; lightIndex < lightCount; lightIndex++)
        {
            Light light = _lights[lightIndex];
            if (light == null)
            {
                continue;
            }

            validLights[lightIndex] = true;
            sources[lightIndex] = transform.InverseTransformPoint(light.transform.position);
            directions[lightIndex] = transform.InverseTransformDirection(light.transform.forward).normalized;
            rights[lightIndex] = Vector3.Cross(Vector3.up, directions[lightIndex]).normalized;
            coneLimits[lightIndex] = Mathf.Cos(light.spotAngle * 0.5f * Mathf.Deg2Rad);
            cookieScales[lightIndex] = Mathf.Tan(light.spotAngle * 0.5f * Mathf.Deg2Rad);
        }

        for (int y = 0; y < MaskSize; y++)
        {
            float z = Mathf.Lerp(-IceHalfLength, IceHalfLength, (y + 0.5f) / MaskSize);
            for (int x = 0; x < MaskSize; x++)
            {
                float xPosition = Mathf.Lerp(-IceHalfWidth, IceHalfWidth, (x + 0.5f) / MaskSize);
                float mask = 0f;
                Vector3 target = new Vector3(xPosition, 0f, z);
                for (int lightIndex = 0; lightIndex < lightCount; lightIndex++)
                {
                    if (!validLights[lightIndex])
                    {
                        continue;
                    }

                    Vector3 source = sources[lightIndex];
                    Vector3 direction = directions[lightIndex];
                    Vector3 ray = target - source;
                    float distance = ray.magnitude;
                    if (distance < 0.1f)
                    {
                        continue;
                    }

                    Vector3 rayDirection = ray / distance;
                    float coneDot = Vector3.Dot(rayDirection, direction);
                    if (coneDot < coneLimits[lightIndex])
                    {
                        continue;
                    }

                    float depth = Vector3.Dot(ray, direction);
                    if (depth <= 0.1f)
                    {
                        continue;
                    }

                    float cookieX = Vector3.Dot(ray, rights[lightIndex]) / depth / cookieScales[lightIndex];
                    float cookieY = Vector3.Dot(ray, Vector3.up) / depth / cookieScales[lightIndex];
                    float distanceToEdge = Mathf.Min(CookieHalfWidth - Mathf.Abs(cookieX), CookieHalfHeight - Mathf.Abs(cookieY));
                    float lightMask = Mathf.Clamp01(distanceToEdge / CookieFeather);
                    if (lightMask <= 0f || IsOccluded(source, target))
                    {
                        continue;
                    }

                    mask = Mathf.Clamp01(mask + lightMask);
                }

                pixels[y * MaskSize + x] = new Color(mask, mask, mask, mask);
            }

            if ((y + 1) % BakeRowsPerFrame == 0)
            {
                yield return null;
            }
        }

        _cachedMaskPixels = pixels;
        if (_maskTexture != null)
        {
            _maskTexture.SetPixels(pixels);
            _maskTexture.Apply(false, true);
        }
        Debug.Log("[SydneyTLANArena] Incremental rectangular floodlight reflection mask bake completed and cached.");
    }

    private bool IsOccluded(Vector3 source, Vector3 target)
    {
        Vector3 worldSource = transform.TransformPoint(source);
        Vector3 worldTarget = transform.TransformPoint(target);
        Vector3 ray = worldTarget - worldSource;
        float distance = ray.magnitude;
        if (distance <= 1.1f)
        {
            return false;
        }

        RaycastHit hit;
        if (!Physics.Raycast(worldSource + ray.normalized * 1f, ray.normalized, out hit, distance - 1.05f, ~0, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        string hitName = hit.collider == null ? string.Empty : hit.collider.gameObject.name;
        if (hitName.IndexOf("Flood_Lamps", StringComparison.OrdinalIgnoreCase) >= 0 && hit.distance < 2f)
        {
            return false;
        }

        return true;
    }

    private void OnDestroy()
    {
        if (_material != null)
        {
            UnityEngine.Object.Destroy(_material);
        }
        if (_maskTexture != null)
        {
            UnityEngine.Object.Destroy(_maskTexture);
        }
    }
}

internal sealed class SydneyLampGlow : MonoBehaviour
{
    private const float StatePollInterval = 0.1f;
    private Renderer _renderer;
    private Light[] _lights;
    private Material _glowMaterial;
    private float _emissionMultiplier = 1f;
    private float _maximumIntensity = 2500f;
    private bool _previewEmission;
    private float _previewIntensity;
    private Color _previewColour = new Color(1f, 0.77f, 0.56f, 1f);
    private float _nextStatePoll;
    private bool _stateDirty = true;
    private bool _lastOn;
    private float _lastIntensity = float.NaN;
    private Color _lastColour;
    private bool _lastOplEnabled = true;

    public void Initialize(Renderer renderer, Light[] lights, float emissionMultiplier, float maximumIntensity)
    {
        _renderer = renderer;
        _lights = lights;
        _emissionMultiplier = Mathf.Clamp(emissionMultiplier, 0.1f, 10f);
        _maximumIntensity = Mathf.Max(1f, maximumIntensity);

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                        Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Unlit/Color");
        if (shader == null)
        {
            Debug.LogWarning("[SydneyTLANArena] No URP lamp shader was found; Sydney lamp emission is unavailable.");
            return;
        }

        _glowMaterial = new Material(shader)
        {
            name = "Sydney Flood Lamp Mesh Emission (Runtime)",
            renderQueue = 3000
        };
        _glowMaterial.SetInt("_Cull", 0);
        if (_glowMaterial.HasProperty("_BaseColor"))
        {
            _glowMaterial.SetColor("_BaseColor", new Color(0.2f, 0.12f, 0.06f, 1f));
        }
        if (_glowMaterial.HasProperty("_Color"))
        {
            _glowMaterial.SetColor("_Color", new Color(0.2f, 0.12f, 0.06f, 1f));
        }
        if (_glowMaterial.HasProperty("_EmissionColor"))
        {
            _glowMaterial.EnableKeyword("_EMISSION");
            _glowMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        _renderer.material = _glowMaterial;
        CreatePanelOverlay();

        Debug.Log("[SydneyTLANArena] Attached visible lamp glow to '" + _renderer.name + "' using shader '" +
                  (_glowMaterial.shader == null ? "none" : _glowMaterial.shader.name) +
                  "' (emission=" + _glowMaterial.HasProperty("_EmissionColor") +
                  ", actualFloodLampMesh=true, explicitQuadPanels=true, panelEmissionSlider=linked).");
        UpdateEmission();
    }

    public void ConfigurePreview(float emissionMultiplier, float maximumIntensity, bool previewEmission)
    {
        ConfigurePreview(emissionMultiplier, maximumIntensity, previewEmission, maximumIntensity, new Color(1f, 0.77f, 0.56f, 1f));
    }

    public void ConfigurePreview(float emissionMultiplier, float maximumIntensity, bool previewEmission, float previewIntensity, Color previewColour)
    {
        _emissionMultiplier = Mathf.Clamp(emissionMultiplier, 0.1f, 10f);
        _maximumIntensity = Mathf.Max(1f, maximumIntensity);
        _previewEmission = previewEmission;
        _previewIntensity = Mathf.Clamp(previewIntensity, 0f, _maximumIntensity);
        _previewColour = previewColour;
        _stateDirty = true;
    }

    private void LateUpdate()
    {
        if (!_stateDirty && Time.unscaledTime < _nextStatePoll)
        {
            return;
        }
        _nextStatePoll = Time.unscaledTime + StatePollInterval;
        UpdateEmission();
    }

    private void UpdateEmission()
    {
        if (_glowMaterial == null || _lights == null)
        {
            return;
        }

        bool oplEnabled = TlanSydneyAdapter.IsOplArenaEnabled();
        if (!oplEnabled)
        {
            if (_lastOplEnabled || _stateDirty)
            {
                SetMaterialColour(_glowMaterial, Color.black);
                SetMaterialColour(_panelOverlayMaterial, Color.black);
            }
            _lastOplEnabled = false;
            _stateDirty = false;
            return;
        }
        if (!_lastOplEnabled)
        {
            _stateDirty = true;
        }
        _lastOplEnabled = true;

        if (!_renderer.enabled)
        {
            _renderer.enabled = true;
        }

        bool on = false;
        Color colour = Color.white;
        float intensity = 0f;
        if (_previewEmission)
        {
            on = _previewIntensity > 0.01f;
            colour = _previewColour;
            intensity = _previewIntensity;
        }
        for (int i = 0; !_previewEmission && i < _lights.Length; i++)
        {
            Light light = _lights[i];
            if (light == null || light.intensity <= 0f)
            {
                continue;
            }

            on = true;
            colour = light.color;
            intensity = Mathf.Max(intensity, light.intensity);
        }

        if (!_stateDirty && _lastOn == on && Mathf.Abs(_lastIntensity - intensity) < 0.01f && Approximately(_lastColour, colour))
        {
            return;
        }

        _lastOn = on;
        _lastIntensity = intensity;
        _lastColour = colour;
        _stateDirty = false;

        if (_glowMaterial.HasProperty("_EmissionColor"))
        {
            float normalizedIntensity = Mathf.Clamp01(intensity / _maximumIntensity);
            float scale = on ? Mathf.Lerp(3f, 18f, normalizedIntensity) * _emissionMultiplier : 0f;
            _glowMaterial.SetColor("_EmissionColor", new Color(colour.r * scale, colour.g * scale, colour.b * scale, 1f));
        }

        float baseScale = on ? Mathf.Lerp(0.25f, 0.6f, Mathf.Clamp01(intensity / _maximumIntensity)) : 0f;
        if (_glowMaterial.HasProperty("_BaseColor"))
        {
            _glowMaterial.SetColor("_BaseColor", on
                ? new Color(colour.r * baseScale, colour.g * baseScale, colour.b * baseScale, 1f)
                : Color.black);
        }
        if (_glowMaterial.HasProperty("_Color"))
        {
            _glowMaterial.SetColor("_Color", on
                ? new Color(colour.r * baseScale, colour.g * baseScale, colour.b * baseScale, 1f)
                : Color.black);
        }

        if (_panelOverlayMaterial != null)
        {
            float panelScale = on ? Mathf.Lerp(8f, 30f, Mathf.Clamp01(intensity / _maximumIntensity)) * _emissionMultiplier : 0f;
            Color panelColour = on
                ? new Color(colour.r * panelScale, colour.g * panelScale, colour.b * panelScale, 1f)
                : Color.black;
            if (_panelOverlayMaterial.HasProperty("_EmissionColor"))
            {
                _panelOverlayMaterial.SetColor("_EmissionColor", panelColour);
            }
            if (_panelOverlayMaterial.HasProperty("_BaseColor"))
            {
                _panelOverlayMaterial.SetColor("_BaseColor", panelColour);
            }
            if (_panelOverlayMaterial.HasProperty("_Color"))
            {
                _panelOverlayMaterial.SetColor("_Color", panelColour);
            }
        }
    }

    private static bool Approximately(Color left, Color right)
    {
        return Mathf.Abs(left.r - right.r) < 0.001f &&
               Mathf.Abs(left.g - right.g) < 0.001f &&
               Mathf.Abs(left.b - right.b) < 0.001f &&
               Mathf.Abs(left.a - right.a) < 0.001f;
    }

    private static void SetMaterialColour(Material material, Color colour)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_EmissionColor"))
        {
            material.SetColor("_EmissionColor", colour);
        }
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", colour);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", colour);
        }
    }

    private Renderer[] _panelOverlayRenderers;
    private Material _panelOverlayMaterial;

    private void CreatePanelOverlay()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Universal Render Pipeline/Lit") ??
                        Shader.Find("Unlit/Color");
        if (shader == null)
        {
            Debug.LogWarning("[SydneyTLANArena] No panel shader was found; quad floodlight panels are unavailable.");
            return;
        }

        _panelOverlayMaterial = new Material(shader)
        {
            name = "Sydney Flood Lamp Quad Panels (Runtime)",
            renderQueue = 3000
        };
        _panelOverlayMaterial.SetInt("_Cull", 0);
        if (_panelOverlayMaterial.HasProperty("_Surface"))
        {
            _panelOverlayMaterial.SetFloat("_Surface", 1f);
        }
        if (_panelOverlayMaterial.HasProperty("_ZWrite"))
        {
            _panelOverlayMaterial.SetFloat("_ZWrite", 0f);
        }
        if (_panelOverlayMaterial.HasProperty("_EmissionColor"))
        {
            _panelOverlayMaterial.EnableKeyword("_EMISSION");
            _panelOverlayMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        Vector3[] positions =
        {
            new Vector3(-48.22f, 42f, -71.46f),
            new Vector3(-48.22f, 42f, 71.46f),
            new Vector3(48.22f, 42f, -71.46f),
            new Vector3(48.22f, 42f, 71.46f)
        };
        Vector3[] directions =
        {
            new Vector3(0.7071f, 0f, 0.7071f),
            new Vector3(0.7071f, 0f, -0.7071f),
            new Vector3(-0.7071f, 0f, 0.7071f),
            new Vector3(-0.7071f, 0f, -0.7071f)
        };

        _panelOverlayRenderers = new Renderer[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            GameObject panelObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panelObject.name = "Flood_Lamp_QuadPanel_Runtime_" + i;
            panelObject.transform.SetParent(transform, false);
            panelObject.transform.localPosition = positions[i] + directions[i] * 0.12f;
            panelObject.transform.localRotation = Quaternion.LookRotation(directions[i], Vector3.up);
            panelObject.transform.localScale = new Vector3(4.31f, 3.20f, 1f);

            Component collider = panelObject.GetComponent("MeshCollider");
            if (collider != null)
            {
                UnityEngine.Object.Destroy(collider);
            }

            MeshRenderer meshRenderer = panelObject.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _panelOverlayMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            _panelOverlayRenderers[i] = meshRenderer;
        }

        Debug.Log("[SydneyTLANArena] Created 4 Sydney floodlight quad panels using measured lamp dimensions and normals.");
    }
}

internal sealed class SydneyGoalSirenController : MonoBehaviour
{
    private const string GoalScoredEvent = "Event_Everyone_OnGoalScored";
    private const string GameStateChangedEvent = "Event_Everyone_OnGameStateChanged";
    private const string ServerChatCommandEvent = "Event_Server_OnChatCommand";
    private const float SydneyReplayGoalLine = 40f;
    private const float PhasePollInterval = 0.1f;
    private static SydneyGoalSirenController _activeController;
    private readonly List<SydneyGoalSirenUnit> _units = new List<SydneyGoalSirenUnit>();
    private bool _subscribed;
    private bool _gameStateSubscribed;
    private bool _chatSubscribed;
    private bool _manualOverride;
    private bool _manualPeriodLightState;
    private bool _periodLightStateApplied;
    private GamePhase _lastPhase = GamePhase.None;
    private int _lastScoredEnd;
    private int _replayScoredEnd;
    private float _replayGoalProgress;
    private bool _replayGoalTriggered;
    private float _nextPhasePoll;
    private bool _disabledStateApplied;

    public void Initialize(GameObject arenaRoot, float intensityMultiplier)
    {
        if (_subscribed || arenaRoot == null)
        {
            return;
        }

        Renderer positiveDome = FindRenderer(arenaRoot, "GoalDome_0");
        Renderer negativeDome = FindRenderer(arenaRoot, "GoalDome_1");
        if (positiveDome != null)
        {
            _units.Add(new SydneyGoalSirenUnit(positiveDome, 1, intensityMultiplier));
        }
        if (negativeDome != null)
        {
            _units.Add(new SydneyGoalSirenUnit(negativeDome, -1, intensityMultiplier));
        }

        if (_units.Count == 0)
        {
            Debug.LogWarning("[SydneyTLANArena] Sydney GoalDome siren meshes were not found; goal sirens are unavailable.");
            return;
        }

        if (_activeController != null && !ReferenceEquals(_activeController, this))
        {
            SydneyGoalSirenController previousController = _activeController;
            previousController.enabled = false;
            previousController.Release();
            UnityEngine.Object.Destroy(previousController);
            Debug.Log("[SydneyTLANArena] Replaced the previous goal-light controller after TLAN respawned the arena.");
        }

        EventManager.AddEventListener(GoalScoredEvent, OnGoalScored);
        EventManager.AddEventListener(GameStateChangedEvent, OnGameStateChanged);
        EventManager.AddEventListener(ServerChatCommandEvent, OnServerChatCommand);
        _subscribed = true;
        _gameStateSubscribed = true;
        _chatSubscribed = true;
        _activeController = this;
        UpdatePhaseState();
        Debug.Log("[SydneyTLANArena] Goal siren trigger attached to " + _units.Count + " Sydney GoalDome mesh(es). Only the scored-at end will flash.");
        Debug.Log("[SydneyTLANArena] Stationary green intermission dome light installed at " + _units.Count + " goal end(s); the dome material turns green while active.");
        Debug.Log("[SydneyTLANArena] Warmup test commands: /sirens on|off for red sirens; /periodlight on|off for green lights.");
    }

    private void Update()
    {
        if (!TlanSydneyAdapter.IsOplArenaEnabled())
        {
            if (!_disabledStateApplied)
            {
                for (int i = 0; i < _units.Count; i++)
                {
                    _units[i].SetManualActive(false);
                    _units[i].SetPeriodLightActive(false);
                    _units[i].ClearGoalActive();
                }
                _disabledStateApplied = true;
            }
            return;
        }
        _disabledStateApplied = false;

        if (Time.unscaledTime >= _nextPhasePoll)
        {
            UpdatePhaseState();
            _nextPhasePoll = Time.unscaledTime + PhasePollInterval;
        }

        // Only compare bindings each frame; writes occur solely if another
        // renderer/LOD system replaced them. This retains the proximity fix
        // without reapplying all siren light and material state every frame.
        for (int i = 0; i < _units.Count; i++)
        {
            _units[i].MaintainMaterialBindings();
        }

        if (_lastPhase == GamePhase.Replay)
        {
            UpdateReplayGoalTrigger();
        }

        float deltaTime = Time.unscaledDeltaTime;
        for (int i = 0; i < _units.Count; i++)
        {
            if (_units[i].NeedsTick)
            {
                _units[i].Tick(deltaTime);
            }
        }
    }

    private void UpdatePhaseState()
    {
        GameManager gameManager = NetworkBehaviourSingleton<GameManager>.Instance;
        if (gameManager == null)
        {
            return;
        }

        GamePhase currentPhase = gameManager.Phase;
        if (_lastPhase == GamePhase.Warmup && currentPhase != GamePhase.Warmup &&
            (_manualOverride || _manualPeriodLightState))
        {
            _manualOverride = false;
            _manualPeriodLightState = false;
            for (int i = 0; i < _units.Count; i++)
            {
                _units[i].SetManualActive(false);
            }
            Debug.Log("[SydneyTLANArena] Warmup siren and period-light overrides cleared after leaving warmup.");
        }

        bool periodLightActive = currentPhase == GamePhase.Intermission ||
                                 (currentPhase == GamePhase.Warmup && _manualPeriodLightState);
        if (currentPhase != _lastPhase)
        {
            if (currentPhase == GamePhase.Replay)
            {
                _replayScoredEnd = _lastScoredEnd;
                _replayGoalProgress = float.MinValue;
                _replayGoalTriggered = false;
                for (int i = 0; i < _units.Count; i++)
                {
                    _units[i].ClearGoalActive();
                }
                Debug.Log("[SydneyTLANArena] Replay started; goal siren cleared until the replay puck reaches the goal.");
            }
            else if (_lastPhase == GamePhase.Replay)
            {
                for (int i = 0; i < _units.Count; i++)
                {
                    _units[i].ClearGoalActive();
                }
                _replayScoredEnd = 0;
                _replayGoalProgress = float.MinValue;
                _replayGoalTriggered = false;
            }
        }
        if (periodLightActive != _periodLightStateApplied)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                _units[i].SetPeriodLightActive(periodLightActive);
            }
            _periodLightStateApplied = periodLightActive;
            if (currentPhase == GamePhase.Intermission || _lastPhase == GamePhase.Intermission)
            {
                Debug.Log("[SydneyTLANArena] End-of-period green goal lights " + (periodLightActive ? "enabled for intermission." : "disabled after intermission."));
            }
        }

        _lastPhase = currentPhase;
    }

    private void OnGameStateChanged(Dictionary<string, object> payload)
    {
        if (payload == null || !payload.TryGetValue("newGameState", out object stateValue) || !(stateValue is GameState))
        {
            return;
        }

        GameState state = (GameState)stateValue;
        if (state.Phase == GamePhase.Replay && _lastPhase != GamePhase.Replay)
        {
            UpdatePhaseState();
        }
    }

    private void UpdateReplayGoalTrigger()
    {
        if (_replayGoalTriggered || _replayScoredEnd == 0)
        {
            return;
        }

        GameManager gameManager = NetworkBehaviourSingleton<GameManager>.Instance;
        if (gameManager == null || gameManager.Phase != GamePhase.Replay)
        {
            return;
        }

        PuckManager puckManager = MonoBehaviourSingleton<PuckManager>.Instance;
        if (puckManager == null)
        {
            return;
        }

        List<Puck> replayPucks = puckManager.GetReplayPucks();
        for (int i = 0; i < replayPucks.Count; i++)
        {
            Puck replayPuck = replayPucks[i];
            if (replayPuck == null)
            {
                continue;
            }

            float progress = _replayScoredEnd > 0
                ? replayPuck.transform.position.z
                : -replayPuck.transform.position.z;
            if (progress > _replayGoalProgress)
            {
                _replayGoalProgress = progress;
            }

            // The Sydney goal line is approximately 40.3m from centre ice.
            // Requiring the puck to reach this line prevents the siren from
            // starting at the beginning of the replay.
            if (progress < SydneyReplayGoalLine)
            {
                continue;
            }

            for (int unitIndex = 0; unitIndex < _units.Count; unitIndex++)
            {
                if (_units[unitIndex].Side == _replayScoredEnd)
                {
                    _units[unitIndex].Trigger();
                    _replayGoalTriggered = true;
                    Debug.Log("[SydneyTLANArena] Replay puck reached the goal; replay siren activated at the scored end.");
                    return;
                }
            }
        }
    }

    public static void TryHandleWarmupCommand(string content)
    {
        if (_activeController != null)
        {
            _activeController.HandleWarmupCommand(content);
        }
    }

    private void OnServerChatCommand(Dictionary<string, object> payload)
    {
        if (payload != null && payload.TryGetValue("command", out object commandValue))
        {
            HandleWarmupCommand(commandValue as string);
        }
    }

    private void HandleWarmupCommand(string content)
    {
        if (string.IsNullOrWhiteSpace(content) ||
            NetworkBehaviourSingleton<GameManager>.Instance == null ||
            NetworkBehaviourSingleton<GameManager>.Instance.Phase != GamePhase.Warmup)
        {
            return;
        }

        string[] parts = content.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return;
        }

        string command = parts[0].ToLowerInvariant();
        bool isSirenCommand = command == "/sydneysirens" || command == "/tlanlights" || command == "/sirens";
        bool isPeriodCommand = command == "/periodlight" || command == "/sydneyperiod" || command == "/sydneygreen" || command == "/periodlights" || command == "/greenlights";
        if (!isSirenCommand && !isPeriodCommand)
        {
            return;
        }

        string action = parts[1].ToLowerInvariant();
        if (isSirenCommand && action == "on")
        {
            SetManualOverride(true);
        }
        else if (isSirenCommand && action == "off")
        {
            SetManualOverride(false);
        }
        else if (isPeriodCommand && action == "on")
        {
            SetManualPeriodLightState(true);
        }
        else if (isPeriodCommand && action == "off")
        {
            SetManualPeriodLightState(false);
        }
    }

    private void SetManualOverride(bool enabled)
    {
        _manualOverride = enabled;
        for (int i = 0; i < _units.Count; i++)
        {
            _units[i].SetManualActive(enabled);
        }
        Debug.Log("[SydneyTLANArena] Warmup goal siren override " + (enabled ? "enabled on both ends." : "disabled on both ends."));
    }

    private void SetManualPeriodLightState(bool enabled)
    {
        _manualPeriodLightState = enabled;
        UpdatePhaseState();
        Debug.Log("[SydneyTLANArena] Warmup green end-of-period light override " + (enabled ? "enabled on both ends." : "disabled on both ends."));
    }

    private void OnGoalScored(Dictionary<string, object> payload)
    {
        int scoredEnd = DetermineScoredEnd(payload);
        if (scoredEnd == 0)
        {
            Debug.LogWarning("[SydneyTLANArena] Goal scored event did not expose a usable end position or scoring team; siren not triggered.");
            return;
        }

        for (int i = 0; i < _units.Count; i++)
        {
            if (_units[i].Side == scoredEnd)
            {
                if (_manualOverride)
                {
                    return;
                }
                _lastScoredEnd = scoredEnd;
                _units[i].Trigger();
                Debug.Log("[SydneyTLANArena] Triggered Sydney emergency siren at the scored goal end (z side " + scoredEnd + ").");
                return;
            }
        }
    }

    private static int DetermineScoredEnd(Dictionary<string, object> payload)
    {
        if (payload != null && payload.TryGetValue("puck", out object puckValue))
        {
            Puck puck = puckValue as Puck;
            if (!ReferenceEquals(puck, null))
            {
                PropertyInfo transformProperty = typeof(Puck).GetProperty("transform", BindingFlags.Instance | BindingFlags.Public);
                Transform puckTransform = transformProperty == null ? null : transformProperty.GetValue(puck, null) as Transform;
                if (puckTransform != null && Mathf.Abs(puckTransform.position.z) > 5f)
                {
                    return puckTransform.position.z > 0f ? 1 : -1;
                }
            }
        }

        // The game places Blue's goal at +Z and Red's goal at -Z. A goal scored
        // by Red therefore enters the +Z goal, and vice versa.
        if (payload != null && payload.TryGetValue("byTeam", out object teamValue))
        {
            string teamName = teamValue == null ? string.Empty : teamValue.ToString();
            if (teamName.IndexOf("Red", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 1;
            }
            if (teamName.IndexOf("Blue", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return -1;
            }
        }

        return 0;
    }

    private static Renderer FindRenderer(GameObject root, string objectName)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && string.Equals(renderers[i].gameObject.name, objectName, StringComparison.OrdinalIgnoreCase))
            {
                return renderers[i];
            }
        }

        return null;
    }

    private void OnDestroy()
    {
        Release();
    }

    private void Release()
    {
        if (_subscribed)
        {
            EventManager.RemoveEventListener(GoalScoredEvent, OnGoalScored);
            _subscribed = false;
        }
        if (_gameStateSubscribed)
        {
            EventManager.RemoveEventListener(GameStateChangedEvent, OnGameStateChanged);
            _gameStateSubscribed = false;
        }
        if (_chatSubscribed)
        {
            EventManager.RemoveEventListener(ServerChatCommandEvent, OnServerChatCommand);
            _chatSubscribed = false;
        }

        if (ReferenceEquals(_activeController, this))
        {
            _activeController = null;
        }

        for (int i = 0; i < _units.Count; i++)
        {
            _units[i].Dispose();
        }
        _units.Clear();
    }

    private sealed class SydneyGoalSirenUnit
    {
        private const int RuntimeLightLayer = 2;
        private const float SirenConeRange = 9f;
        private const float SirenEmitterRadius = 0.16f;
        private const float SirenAssemblyScale = 0.84f;
        private const float PeriodAssemblyScale = 0.84f;
        private const float PeriodAmbientRange = 12f;
        private const float RotationSpeed = 720f;
        private const float PeriodRotationSpeed = 120f;
        private const float DomeGlassAlpha = 0.09f;
        private const float PeriodPointIntensity = 1.8f;
        private const float PeriodDomeIntensity = 0.42f;
        private readonly Renderer _sourceDomeRenderer;
        private readonly Renderer _domeRenderer;
        private readonly int _side;
        private readonly float _intensityMultiplier;
        private readonly Material _domeMaterial;
        private readonly Material _rotorMaterial;
        private readonly Material _periodEmitterMaterial;
        private readonly Material _periodRotorMaterial;
        private readonly GameObject _rotorRoot;
        private readonly GameObject _periodRotorRoot;
        private readonly GameObject _glassDomeObject;
        private readonly Renderer[] _blades;
        private readonly Light[] _beamLights;
        private readonly Light _periodLight;
        private readonly Renderer _periodEmitterRenderer;
        private readonly Renderer[] _periodBlades;
        private readonly Light[] _periodBeamLights;
        private readonly Color _sirenColor = new Color(1f, 0.012f, 0.006f, 1f);
        private readonly Color _periodColor = new Color(0.02f, 1f, 0.04f, 1f);
        private float _angle;
        private float _periodAngle;
        private bool _goalActive;
        private bool _manualActive;
        private bool _periodLightActive;

        public int Side => _side;
        public bool NeedsTick => _manualActive || _goalActive || _periodLightActive;

        public SydneyGoalSirenUnit(Renderer domeRenderer, int side, float intensityMultiplier)
        {
            _sourceDomeRenderer = domeRenderer;
            _side = side;
            _intensityMultiplier = Mathf.Clamp(intensityMultiplier, 0.1f, 10f);
            Material domeGlassMaterial = CreateGlassMaterial("Sydney Goal Dome Glass (Runtime)");
            MeshFilter sourceMeshFilter = domeRenderer.GetComponent<MeshFilter>();
            if (domeGlassMaterial != null && sourceMeshFilter != null && sourceMeshFilter.sharedMesh != null)
            {
                _glassDomeObject = new GameObject("SydneyGoalDomeGlassRuntime");
                _glassDomeObject.transform.SetParent(domeRenderer.transform.parent, false);
                _glassDomeObject.transform.localPosition = domeRenderer.transform.localPosition;
                _glassDomeObject.transform.localRotation = domeRenderer.transform.localRotation;
                _glassDomeObject.transform.localScale = domeRenderer.transform.localScale;
                MeshFilter glassMeshFilter = _glassDomeObject.AddComponent<MeshFilter>();
                glassMeshFilter.sharedMesh = sourceMeshFilter.sharedMesh;
                MeshRenderer glassRenderer = _glassDomeObject.AddComponent<MeshRenderer>();
                glassRenderer.sharedMaterial = domeGlassMaterial;
                glassRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                glassRenderer.receiveShadows = false;
                _glassDomeObject.layer = RuntimeLightLayer;
                _domeRenderer = glassRenderer;
                _domeMaterial = domeGlassMaterial;
                domeRenderer.enabled = false;
            }
            else
            {
                _glassDomeObject = null;
                _domeMaterial = domeRenderer.material;
                _domeRenderer = domeRenderer;
                domeRenderer.gameObject.layer = RuntimeLightLayer;
                ConfigureDomeAsGlass();
            }

            _rotorMaterial = CreateGlassMaterial("Sydney Goal Siren Glass Blades (Runtime)");
            if (_rotorMaterial != null)
            {
                _rotorMaterial.EnableKeyword("_EMISSION");
            }

            _rotorRoot = new GameObject("SydneyGoalSirenRotor");
            _rotorRoot.transform.SetParent(domeRenderer.transform, false);
            _rotorRoot.transform.localPosition = Vector3.zero;
            _rotorRoot.transform.localScale = Vector3.one * SirenAssemblyScale;
            _rotorRoot.layer = RuntimeLightLayer;

            _blades = new Renderer[2];
            _beamLights = new Light[2];
            for (int i = 0; i < 2; i++)
            {
                GameObject blade = CreateDomeBlade("SydneyGoalSirenBlade_" + i, i == 0 ? 0f : Mathf.PI);
                blade.transform.SetParent(_rotorRoot.transform, false);
                blade.layer = RuntimeLightLayer;
                MeshRenderer bladeRenderer = blade.GetComponent<MeshRenderer>();
                bladeRenderer.sharedMaterial = _rotorMaterial;
                bladeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bladeRenderer.receiveShadows = false;
                bladeRenderer.enabled = false;
                _blades[i] = bladeRenderer;

                GameObject beamObject = new GameObject("SydneyGoalSirenBeam_" + i);
                beamObject.transform.SetParent(_rotorRoot.transform, false);
                // The lamp is mounted at the leading edge of each rotating bar.
                // Its spot direction points radially away from the rotor centre,
                // so the illumination sweeps with the beacon instead of pulsing
                // from a fixed point at the middle of the dome.
                beamObject.transform.localPosition = i == 0
                    ? Vector3.forward * SirenEmitterRadius
                    : Vector3.back * SirenEmitterRadius;
                beamObject.transform.localRotation = i == 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
                beamObject.layer = RuntimeLightLayer;
                Light beam = beamObject.AddComponent<Light>();
                beam.type = LightType.Spot;
                beam.range = SirenConeRange;
                beam.spotAngle = 68f;
                beam.innerSpotAngle = 30f;
                beam.color = _sirenColor;
                beam.intensity = 0f;
                beam.shadows = LightShadows.None;
                beam.renderMode = LightRenderMode.ForcePixel;
                // Illuminate the actual arena in the rotating cone. The cone
                // is deliberately short and narrow so this is not the old
                // goal-wide spotlight effect.
                beam.cullingMask = ~0;
                beam.enabled = false;
                _beamLights[i] = beam;
            }

            GameObject periodLightObject = new GameObject("SydneyGoalEndOfPeriodGreenLight");
            periodLightObject.transform.SetParent(domeRenderer.transform, false);
            periodLightObject.transform.localPosition = Vector3.zero;
            periodLightObject.layer = RuntimeLightLayer;
            _periodLight = periodLightObject.AddComponent<Light>();
            _periodLight.type = LightType.Point;
            _periodLight.range = PeriodAmbientRange;
            _periodLight.color = _periodColor;
            _periodLight.intensity = 0f;
            _periodLight.shadows = LightShadows.None;
            _periodLight.renderMode = LightRenderMode.ForcePixel;
            _periodLight.cullingMask = ~0;
            _periodLight.enabled = false;

            GameObject periodEmitterObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            periodEmitterObject.name = "SydneyGoalEndOfPeriodGreenEmitter";
            periodEmitterObject.transform.SetParent(domeRenderer.transform, false);
            periodEmitterObject.transform.localPosition = Vector3.zero;
            periodEmitterObject.transform.localScale = Vector3.one * 0.18f;
            periodEmitterObject.layer = RuntimeLightLayer;
            Component periodCollider = periodEmitterObject.GetComponent("SphereCollider");
            if (periodCollider != null)
            {
                UnityEngine.Object.Destroy(periodCollider);
            }
            _periodEmitterMaterial = CreateGlassMaterial("Sydney Goal Green Glass LED (Runtime)");
            if (_periodEmitterMaterial != null)
            {
                _periodEmitterMaterial.EnableKeyword("_EMISSION");
                periodEmitterObject.GetComponent<MeshRenderer>().sharedMaterial = _periodEmitterMaterial;
            }
            _periodEmitterRenderer = periodEmitterObject.GetComponent<MeshRenderer>();
            _periodEmitterRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _periodEmitterRenderer.receiveShadows = false;
            _periodEmitterRenderer.enabled = false;

            _periodRotorMaterial = CreateGlassMaterial("Sydney Goal Period Light Rotating Green Bar (Runtime)");
            if (_periodRotorMaterial != null)
            {
                _periodRotorMaterial.EnableKeyword("_EMISSION");
            }

            _periodRotorRoot = new GameObject("SydneyGoalPeriodLightRotor");
            _periodRotorRoot.transform.SetParent(domeRenderer.transform, false);
            _periodRotorRoot.transform.localPosition = Vector3.zero;
            _periodRotorRoot.transform.localScale = Vector3.one * PeriodAssemblyScale;
            _periodRotorRoot.layer = RuntimeLightLayer;
            _periodBlades = new Renderer[2];
            _periodBeamLights = new Light[2];
            for (int i = 0; i < 2; i++)
            {
                GameObject periodBladeObject = CreateDomeBlade("SydneyGoalPeriodLightBlade_" + i, i == 0 ? 0f : Mathf.PI);
                periodBladeObject.transform.SetParent(_periodRotorRoot.transform, false);
                periodBladeObject.layer = RuntimeLightLayer;
                MeshRenderer periodBladeRenderer = periodBladeObject.GetComponent<MeshRenderer>();
                periodBladeRenderer.sharedMaterial = _periodRotorMaterial;
                periodBladeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                periodBladeRenderer.receiveShadows = false;
                periodBladeRenderer.enabled = false;
                _periodBlades[i] = periodBladeRenderer;

                GameObject periodBeamObject = new GameObject("SydneyGoalPeriodLightBeam_" + i);
                periodBeamObject.transform.SetParent(_periodRotorRoot.transform, false);
                periodBeamObject.transform.localPosition = Vector3.zero;
                periodBeamObject.transform.localRotation = i == 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
                periodBeamObject.layer = RuntimeLightLayer;
                Light periodBeam = periodBeamObject.AddComponent<Light>();
                periodBeam.type = LightType.Spot;
                periodBeam.range = 22f;
                periodBeam.spotAngle = 42f;
                periodBeam.innerSpotAngle = 16f;
                periodBeam.color = _periodColor;
                periodBeam.intensity = 0f;
                periodBeam.shadows = LightShadows.None;
                periodBeam.renderMode = LightRenderMode.ForcePixel;
                periodBeam.cullingMask = 1 << RuntimeLightLayer;
                periodBeam.enabled = false;
                _periodBeamLights[i] = periodBeam;
            }

            SetActive(false, 0f);
        }

        private static GameObject CreateDomeBlade(string name, float centreAngle)
        {
            const int rows = 5;
            const int columns = 3;
            const float domeRadius = 0.192f;
            const float verticalExtent = 0.135f;
            const float angularExtent = 0.18f;
            Mesh mesh = new Mesh
            {
                name = name + "Mesh"
            };
            Vector3[] vertices = new Vector3[rows * columns];
            for (int row = 0; row < rows; row++)
            {
                float y = Mathf.Lerp(-verticalExtent, verticalExtent, row / (float)(rows - 1));
                float horizontalRadius = Mathf.Sqrt(Mathf.Max(0.001f, domeRadius * domeRadius - y * y));
                for (int column = 0; column < columns; column++)
                {
                    float angle = centreAngle + Mathf.Lerp(-angularExtent, angularExtent, column / (float)(columns - 1));
                    vertices[row * columns + column] = new Vector3(
                        Mathf.Sin(angle) * horizontalRadius,
                        y,
                        Mathf.Cos(angle) * horizontalRadius);
                }
            }

            int[] triangles = new int[(rows - 1) * (columns - 1) * 6];
            int triangleIndex = 0;
            for (int row = 0; row < rows - 1; row++)
            {
                for (int column = 0; column < columns - 1; column++)
                {
                    int topLeft = row * columns + column;
                    int topRight = topLeft + 1;
                    int bottomLeft = topLeft + columns;
                    int bottomRight = bottomLeft + 1;
                    triangles[triangleIndex++] = topLeft;
                    triangles[triangleIndex++] = bottomLeft;
                    triangles[triangleIndex++] = topRight;
                    triangles[triangleIndex++] = topRight;
                    triangles[triangleIndex++] = bottomLeft;
                    triangles[triangleIndex++] = bottomRight;
                }
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.MarkDynamic();

            GameObject bladeObject = new GameObject(name);
            MeshFilter meshFilter = bladeObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;
            bladeObject.AddComponent<MeshRenderer>();
            return bladeObject;
        }

        public void Trigger()
        {
            if (_manualActive)
            {
                return;
            }

            _goalActive = true;
            _angle = 0f;
            SetActive(true, 1f);
        }

        public void ClearGoalActive()
        {
            _goalActive = false;
            if (!_manualActive)
            {
                SetActive(false, 0f);
            }
        }

        public void SetManualActive(bool active)
        {
            _manualActive = active;
            if (active)
            {
                _goalActive = false;
                _angle = 0f;
                SetActive(true, 1f);
            }
            else
            {
                _goalActive = false;
                SetActive(false, 0f);
            }
        }

        public void SetPeriodLightActive(bool active)
        {
            if (_periodLightActive == active)
            {
                return;
            }
            _periodLightActive = active;
            if (!_manualActive && !_goalActive)
            {
                SetActive(false, 0f);
            }
        }

        public void Tick(float deltaTime)
        {
            // Keep the green period beacon rotating independently of the goal
            // siren state. This also preserves its slower, previously chosen
            // rotation rate if a warmup siren override is active at the same
            // time.
            if (_periodLightActive)
            {
                _periodAngle = (_periodAngle + PeriodRotationSpeed * deltaTime) % 360f;
                _periodRotorRoot.transform.localRotation = Quaternion.Euler(0f, _periodAngle, 0f);
            }

            if (_manualActive)
            {
                AdvanceRotation(deltaTime);
                float manualPulse = 0.5f + 0.5f * Mathf.Sin(_angle * Mathf.Deg2Rad * 2f);
                SetActive(true, manualPulse);
                return;
            }

            if (!_goalActive)
            {
                return;
            }

            AdvanceRotation(deltaTime);
            float pulse = 0.5f + 0.5f * Mathf.Sin(_angle * Mathf.Deg2Rad * 2f);
            SetActive(true, pulse);
        }

        private void AdvanceRotation(float deltaTime)
        {
            _angle = (_angle + RotationSpeed * deltaTime) % 360f;
            _rotorRoot.transform.localRotation = Quaternion.Euler(0f, _angle, 0f);
        }

        public void MaintainMaterialBindings()
        {
            if (_domeRenderer != null && _domeRenderer.sharedMaterial != _domeMaterial)
            {
                _domeRenderer.sharedMaterial = _domeMaterial;
            }
            if (_sourceDomeRenderer != null && _sourceDomeRenderer.enabled)
            {
                _sourceDomeRenderer.enabled = false;
            }
            for (int i = 0; i < _blades.Length; i++)
            {
                if (_rotorMaterial != null && _blades[i] != null && _blades[i].sharedMaterial != _rotorMaterial)
                {
                    _blades[i].sharedMaterial = _rotorMaterial;
                }
            }
            if (_periodEmitterMaterial != null && _periodEmitterRenderer != null &&
                _periodEmitterRenderer.sharedMaterial != _periodEmitterMaterial)
            {
                _periodEmitterRenderer.sharedMaterial = _periodEmitterMaterial;
            }
            for (int i = 0; i < _periodBlades.Length; i++)
            {
                if (_periodRotorMaterial != null && _periodBlades[i] != null && _periodBlades[i].sharedMaterial != _periodRotorMaterial)
                {
                    _periodBlades[i].sharedMaterial = _periodRotorMaterial;
                }
            }
        }

        public void Dispose()
        {
            if (_rotorRoot != null)
            {
                UnityEngine.Object.Destroy(_rotorRoot);
            }
            if (_glassDomeObject != null)
            {
                UnityEngine.Object.Destroy(_glassDomeObject);
            }
            if (_periodLight != null)
            {
                UnityEngine.Object.Destroy(_periodLight.gameObject);
            }
            if (_periodEmitterRenderer != null)
            {
                UnityEngine.Object.Destroy(_periodEmitterRenderer.gameObject);
            }
            if (_periodRotorRoot != null)
            {
                UnityEngine.Object.Destroy(_periodRotorRoot);
            }
            if (_rotorMaterial != null)
            {
                UnityEngine.Object.Destroy(_rotorMaterial);
            }
            if (_periodEmitterMaterial != null)
            {
                UnityEngine.Object.Destroy(_periodEmitterMaterial);
            }
            if (_periodRotorMaterial != null)
            {
                UnityEngine.Object.Destroy(_periodRotorMaterial);
            }
        }

        private void SetActive(bool active, float pulse)
        {
            if (_domeRenderer != null && _domeRenderer.sharedMaterial != _domeMaterial)
            {
                _domeRenderer.sharedMaterial = _domeMaterial;
            }
            if (_sourceDomeRenderer != null)
            {
                _sourceDomeRenderer.enabled = false;
            }

            for (int i = 0; i < _blades.Length; i++)
            {
                if (_rotorMaterial != null && _blades[i].sharedMaterial != _rotorMaterial)
                {
                    _blades[i].sharedMaterial = _rotorMaterial;
                }
                _blades[i].enabled = active;
                _beamLights[i].enabled = active;
                _beamLights[i].intensity = active ? (12f + pulse * 30f) * _intensityMultiplier : 0f;
            }

            _periodLight.enabled = !active && _periodLightActive;
            _periodLight.intensity = _periodLight.enabled ? PeriodPointIntensity * _intensityMultiplier : 0f;
            _periodEmitterRenderer.enabled = !active && _periodLightActive;
            if (_periodEmitterMaterial != null && _periodEmitterRenderer.sharedMaterial != _periodEmitterMaterial)
            {
                _periodEmitterRenderer.sharedMaterial = _periodEmitterMaterial;
            }
            bool periodActive = !active && _periodLightActive;
            for (int i = 0; i < _periodBlades.Length; i++)
            {
                if (_periodRotorMaterial != null && _periodBlades[i].sharedMaterial != _periodRotorMaterial)
                {
                    _periodBlades[i].sharedMaterial = _periodRotorMaterial;
                }
                _periodBlades[i].enabled = periodActive;
                _periodBeamLights[i].enabled = periodActive;
                _periodBeamLights[i].intensity = periodActive ? 8f * _intensityMultiplier : 0f;
            }
            if (_periodRotorMaterial != null)
            {
                Color periodRotorColor = periodActive
                    ? new Color(_periodColor.r * 18f * _intensityMultiplier, _periodColor.g * 18f * _intensityMultiplier, _periodColor.b * 18f * _intensityMultiplier, 1f)
                    : Color.black;
                SetColor(_periodRotorMaterial, "_BaseColor", periodRotorColor);
                SetColor(_periodRotorMaterial, "_Color", periodRotorColor);
                if (_periodRotorMaterial.HasProperty("_EmissionColor"))
                {
                    _periodRotorMaterial.SetColor("_EmissionColor", periodRotorColor);
                }
            }

            Color domeColor = active
                ? new Color(_sirenColor.r * (10f + pulse * 35f) * _intensityMultiplier, _sirenColor.g * (10f + pulse * 35f) * _intensityMultiplier, _sirenColor.b * (10f + pulse * 35f) * _intensityMultiplier, DomeGlassAlpha)
                : (_periodLightActive
                    ? new Color(_periodColor.r * 0.65f * _intensityMultiplier, _periodColor.g * 0.65f * _intensityMultiplier, _periodColor.b * 0.65f * _intensityMultiplier, DomeGlassAlpha)
                    : new Color(1f, 1f, 1f, DomeGlassAlpha));
            SetColor(_domeMaterial, "_BaseColor", domeColor);
            SetColor(_domeMaterial, "_Color", domeColor);

            if (_domeMaterial.HasProperty("_EmissionColor"))
            {
                _domeMaterial.EnableKeyword("_EMISSION");
                _domeMaterial.SetColor("_EmissionColor", active ? domeColor : Color.black);
            }
            if (_rotorMaterial != null)
            {
                Color rotorColor = active
                    ? new Color(_sirenColor.r * (35f + pulse * 75f) * _intensityMultiplier, _sirenColor.g * (35f + pulse * 75f) * _intensityMultiplier, _sirenColor.b * (35f + pulse * 75f) * _intensityMultiplier, 1f)
                    : Color.black;
                SetColor(_rotorMaterial, "_BaseColor", rotorColor);
                SetColor(_rotorMaterial, "_Color", rotorColor);
                if (_rotorMaterial.HasProperty("_EmissionColor"))
                {
                    _rotorMaterial.SetColor("_EmissionColor", rotorColor);
                }
            }
            if (_periodEmitterMaterial != null)
            {
                Color emitterColor = _periodLightActive && !active
                    ? new Color(_periodColor.r * PeriodDomeIntensity * 4.5f * _intensityMultiplier, _periodColor.g * PeriodDomeIntensity * 4.5f * _intensityMultiplier, _periodColor.b * PeriodDomeIntensity * 4.5f * _intensityMultiplier, 1f)
                    : Color.black;
                SetColor(_periodEmitterMaterial, "_BaseColor", emitterColor);
                SetColor(_periodEmitterMaterial, "_Color", emitterColor);
                if (_periodEmitterMaterial.HasProperty("_EmissionColor"))
                {
                    _periodEmitterMaterial.SetColor("_EmissionColor", emitterColor);
                }
            }
        }

        private void ConfigureDomeAsGlass()
        {
            if (_domeMaterial == null)
            {
                return;
            }

            ConfigureGlassMaterial(_domeMaterial);
        }

        private static Material CreateGlassMaterial(string name)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader)
            {
                name = name,
                renderQueue = 3000
            };
            ConfigureGlassMaterial(material);
            return material;
        }

        private static void ConfigureGlassMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }
            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 0f);
            }
            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 0f);
            }
            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }
            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }
            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0f);
            }
            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            }
            material.renderQueue = 3000;
            material.SetOverrideTag("RenderType", "Transparent");
        }

        private static void SetColor(Material material, string property, Color color)
        {
            if (material != null && material.HasProperty(property))
            {
                material.SetColor(property, color);
            }
        }
    }
}
