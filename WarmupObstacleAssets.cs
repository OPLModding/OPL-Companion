using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace OPLServerBanners;

internal static class WarmupObstacleAssets
{
    private const string AssetFolderName = "Assets";
    private const string ObstacleAssetFolderName = "WarmupObstacles";

    private static readonly Dictionary<WarmupObstacleBundleKey, AssetBundle> LoadedBundles = new();
    private static readonly Dictionary<WarmupObstacleBundleKey, GameObject> LoadedTrainingRoots = new();
    private static readonly Dictionary<int, Material> CompatibleMaterialCache = new();
    private static readonly HashSet<int> CompatibleMaterialIds = new();
    private static readonly List<Renderer> BoundsRenderers = new();
    private static readonly List<Collider> BoundsColliders = new();
    private static readonly List<Collider> PuckColliderBuffer = new();

    private static Material? ghostMaterial;
    private static Material? highlightMaterial;
    private static Material? targetPieceMaterial;
    private static bool hasCachedPuckColliderTemplate;
    private static int cachedPuckColliderLayer;
    private static PhysicsMaterial? cachedPuckColliderMaterial;
    private static bool loggedPuckColliderFailure;

    public static GameObject? CreateInstance(string assetId, string name, bool serverPhysical)
    {
        if (!WarmupObstacleCatalog.TryGet(assetId, out WarmupObstacleDefinition definition))
        {
            return null;
        }

        GameObject? source = GetSourceObject(definition);
        if (!source)
        {
            return null;
        }

        GameObject instance = Object.Instantiate(source);
        instance.name = name;
        instance.SetActive(true);
        PrepareInstance(instance, serverPhysical);
        return instance;
    }

    public static GameObject CreateTargetPiece(string name, bool serverPhysical)
    {
        GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
        piece.name = name;
        Renderer? renderer = piece.GetComponent<Renderer>();
        if (renderer)
        {
            renderer.sharedMaterial = GetTargetPieceMaterial();
        }

        if (serverPhysical)
        {
            ConfigureServerCollider(piece);
            Rigidbody rigidbody = piece.AddComponent<Rigidbody>();
            rigidbody.mass = 0.08f;
            rigidbody.linearDamping = 0.08f;
            rigidbody.angularDamping = 0.08f;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
        else
        {
            foreach (Collider collider in piece.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
        }

        return piece;
    }

    public static void PrepareGhost(GameObject instance)
    {
        PrepareInstance(instance, serverPhysical: false);
        WarmupObstacleVisual visual = instance.GetComponent<WarmupObstacleVisual>() ?? instance.AddComponent<WarmupObstacleVisual>();
        visual.Initialize(-1, string.Empty);
        visual.SetGhost(GetGhostMaterial());
    }

    public static void ClampBottomToIce(Transform transform, float iceY = 0f)
    {
        if (!TryGetWorldBounds(transform.gameObject, out Bounds bounds))
        {
            if (transform.position.y < iceY)
            {
                transform.position = new Vector3(transform.position.x, iceY, transform.position.z);
            }

            return;
        }

        float bottom = bounds.min.y;
        if (bottom < iceY)
        {
            transform.position += Vector3.up * (iceY - bottom);
        }
    }

    public static bool TryGetWorldBounds(GameObject root, out Bounds bounds)
    {
        BoundsRenderers.Clear();
        root.GetComponentsInChildren(true, BoundsRenderers);

        bool hasBounds = false;
        bounds = default;
        foreach (Renderer renderer in BoundsRenderers)
        {
            if (!renderer || !renderer.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (hasBounds)
        {
            return true;
        }

        BoundsColliders.Clear();
        root.GetComponentsInChildren(true, BoundsColliders);
        foreach (Collider collider in BoundsColliders)
        {
            if (!collider || !collider.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds;
    }

    public static Material GetGhostMaterial()
    {
        if (!ghostMaterial)
        {
            ghostMaterial = CreateTintMaterial(new Color(0.15f, 0.85f, 1f, 0.34f), transparent: true, "WarmupObstacleGhost");
        }

        return ghostMaterial;
    }

    public static Material GetHighlightMaterial()
    {
        if (!highlightMaterial)
        {
            highlightMaterial = CreateTintMaterial(new Color(1f, 0.08f, 0.06f, 0.66f), transparent: true, "WarmupObstacleHighlightRed");
        }

        return highlightMaterial;
    }

    public static Material GetTargetPieceMaterial()
    {
        if (!targetPieceMaterial)
        {
            targetPieceMaterial = CreateTintMaterial(new Color(0.95f, 0.35f, 0.18f, 1f), transparent: false, "WarmupObstacleTargetShard");
        }

        return targetPieceMaterial;
    }

    public static void Teardown()
    {
        foreach (AssetBundle bundle in LoadedBundles.Values)
        {
            if (bundle)
            {
                bundle.Unload(false);
            }
        }

        LoadedBundles.Clear();
        LoadedTrainingRoots.Clear();

        foreach (Material material in CompatibleMaterialCache.Values)
        {
            if (material)
            {
                Object.Destroy(material);
            }
        }

        CompatibleMaterialCache.Clear();
        CompatibleMaterialIds.Clear();

        DestroyMaterial(ref ghostMaterial);
        DestroyMaterial(ref highlightMaterial);
        DestroyMaterial(ref targetPieceMaterial);
        hasCachedPuckColliderTemplate = false;
        cachedPuckColliderLayer = 0;
        cachedPuckColliderMaterial = null;
        loggedPuckColliderFailure = false;
    }

    private static GameObject? GetSourceObject(WarmupObstacleDefinition definition)
    {
        AssetBundle? bundle = GetBundle(definition.BundleKey);
        if (!bundle)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(definition.ChildName))
        {
            return bundle.LoadAsset<GameObject>(definition.AssetPath);
        }

        if (!LoadedTrainingRoots.TryGetValue(definition.BundleKey, out GameObject trainingRoot) || !trainingRoot)
        {
            trainingRoot = bundle.LoadAsset<GameObject>(definition.AssetPath);
            if (!trainingRoot)
            {
                Plugin.LogError($"Warmup obstacle training root '{definition.AssetPath}' was not found in {definition.BundleKey}.");
                return null;
            }

            LoadedTrainingRoots[definition.BundleKey] = trainingRoot;
        }

        Transform? child = FindChildByName(trainingRoot.transform, definition.ChildName!);
        if (!child)
        {
            Plugin.LogError($"Warmup obstacle child '{definition.ChildName}' was not found in {definition.BundleKey}.");
            return null;
        }

        return child.gameObject;
    }

    private static AssetBundle? GetBundle(WarmupObstacleBundleKey key)
    {
        if (LoadedBundles.TryGetValue(key, out AssetBundle loadedBundle) && loadedBundle)
        {
            return loadedBundle;
        }

        string? bundlePath = ResolveBundlePath(key);
        if (string.IsNullOrWhiteSpace(bundlePath) || !File.Exists(bundlePath))
        {
            Plugin.LogError($"Warmup obstacle asset bundle for {key} was not found.");
            return null;
        }

        AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
        if (!bundle)
        {
            Plugin.LogError($"Failed to load warmup obstacle bundle '{bundlePath}'.");
            return null;
        }

        LoadedBundles[key] = bundle;
        Plugin.Log($"Loaded warmup obstacle bundle '{bundlePath}'.");
        return bundle;
    }

    private static string? ResolveBundlePath(WarmupObstacleBundleKey key)
    {
        string pluginDirectory = ServerFileSystemPaths.GetPluginDirectoryPath();
        string bundledPath = key switch
        {
            WarmupObstacleBundleKey.PreviousTraining => Path.Combine(pluginDirectory, AssetFolderName, ObstacleAssetFolderName, "trainingprefabs_3764816187"),
            WarmupObstacleBundleKey.MultiSheetTraining => Path.Combine(pluginDirectory, AssetFolderName, ObstacleAssetFolderName, "trainingprefabs_3771130437"),
            WarmupObstacleBundleKey.PuckPrac => Path.Combine(pluginDirectory, AssetFolderName, ObstacleAssetFolderName, "puckprac_3673692112.bundle"),
            WarmupObstacleBundleKey.S4Trophies => Path.Combine(pluginDirectory, AssetFolderName, ObstacleAssetFolderName, "s4_trophies"),
            _ => string.Empty
        };

        if (File.Exists(bundledPath))
        {
            return bundledPath;
        }

        return key switch
        {
            WarmupObstacleBundleKey.PreviousTraining => @"C:\Program Files (x86)\Steam\steamapps\workshop\content\2994020\3764816187\trainingprefabs",
            WarmupObstacleBundleKey.MultiSheetTraining => @"C:\Program Files (x86)\Steam\steamapps\workshop\content\2994020\3771130437\trainingprefabs",
            WarmupObstacleBundleKey.PuckPrac => @"C:\Program Files (x86)\Steam\steamapps\workshop\content\2994020\3673692112\puckprac.bundle",
            _ => null
        };
    }

    private static void PrepareInstance(GameObject instance, bool serverPhysical)
    {
        foreach (Transform transform in instance.GetComponentsInChildren<Transform>(true))
        {
            transform.gameObject.hideFlags = HideFlags.None;
        }

        DestroyNestedRigidbodies(instance);
        NormalizeMaterials(instance);

        if (serverPhysical)
        {
            EnsureServerColliders(instance);
            ConfigureServerCollider(instance);
        }
        else
        {
            EnsureSelectionColliders(instance);
        }
    }

    private static void EnsureServerColliders(GameObject instance)
    {
        Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
        if (colliders.Any(collider => collider))
        {
            foreach (Collider collider in colliders)
            {
                if (!collider)
                {
                    continue;
                }

                collider.enabled = true;
                collider.isTrigger = false;
                if (collider is MeshCollider meshCollider)
                {
                    meshCollider.convex = false;
                }
            }

            return;
        }

        foreach (MeshFilter meshFilter in instance.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!meshFilter || !meshFilter.sharedMesh)
            {
                continue;
            }

            MeshCollider collider = meshFilter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = meshFilter.sharedMesh;
            collider.convex = false;
            collider.isTrigger = false;
        }
    }

    private static void EnsureSelectionColliders(GameObject instance)
    {
        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
        {
            if (!collider)
            {
                continue;
            }

            collider.enabled = true;
            collider.isTrigger = true;
            if (collider is MeshCollider meshCollider)
            {
                meshCollider.convex = true;
            }
        }

        if (instance.GetComponentsInChildren<Collider>(true).Any(collider => collider))
        {
            return;
        }

        foreach (MeshFilter meshFilter in instance.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!meshFilter || !meshFilter.sharedMesh)
            {
                continue;
            }

            MeshCollider collider = meshFilter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = meshFilter.sharedMesh;
            collider.convex = true;
            collider.isTrigger = true;
        }
    }

    private static void ConfigureServerCollider(GameObject root)
    {
        int colliderLayer = root.layer;
        PhysicsMaterial? colliderMaterial = null;
        if (TryGetPuckColliderTemplate(out int templateLayer, out PhysicsMaterial? templateMaterial))
        {
            colliderLayer = templateLayer;
            colliderMaterial = templateMaterial;
        }

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (!collider)
            {
                continue;
            }

            collider.gameObject.layer = colliderLayer;
            collider.sharedMaterial = colliderMaterial;
        }
    }

    private static bool TryGetPuckColliderTemplate(out int colliderLayer, out PhysicsMaterial? colliderMaterial)
    {
        if (hasCachedPuckColliderTemplate)
        {
            colliderLayer = cachedPuckColliderLayer;
            colliderMaterial = cachedPuckColliderMaterial;
            return true;
        }

        foreach (Puck puck in Object.FindObjectsByType<Puck>(FindObjectsSortMode.None))
        {
            if (!puck)
            {
                continue;
            }

            PuckColliderBuffer.Clear();
            puck.GetComponentsInChildren(true, PuckColliderBuffer);
            foreach (Collider candidate in PuckColliderBuffer)
            {
                if (!candidate || !candidate.enabled || candidate.isTrigger)
                {
                    continue;
                }

                cachedPuckColliderLayer = candidate.gameObject.layer;
                cachedPuckColliderMaterial = candidate.sharedMaterial;
                hasCachedPuckColliderTemplate = true;
                colliderLayer = cachedPuckColliderLayer;
                colliderMaterial = cachedPuckColliderMaterial;
                return true;
            }
        }

        if (!loggedPuckColliderFailure)
        {
            loggedPuckColliderFailure = true;
            Plugin.Log("No live puck collider was available yet; warmup obstacle colliders will use their current layer/material.");
        }

        colliderLayer = 0;
        colliderMaterial = null;
        return false;
    }

    private static void DestroyNestedRigidbodies(GameObject instance)
    {
        foreach (Rigidbody rigidbody in instance.GetComponentsInChildren<Rigidbody>(true))
        {
            Object.Destroy(rigidbody);
        }
    }

    private static void NormalizeMaterials(GameObject root)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] sharedMaterials = renderer.sharedMaterials;
            bool changed = false;
            for (int index = 0; index < sharedMaterials.Length; index++)
            {
                Material? compatible = GetCompatibleMaterial(sharedMaterials[index]);
                if (compatible && compatible != sharedMaterials[index])
                {
                    sharedMaterials[index] = compatible;
                    changed = true;
                }
            }

            if (changed)
            {
                renderer.sharedMaterials = sharedMaterials;
            }
        }
    }

    private static Material? GetCompatibleMaterial(Material? sourceMaterial)
    {
        if (!sourceMaterial)
        {
            return null;
        }

        int sourceId = sourceMaterial.GetInstanceID();
        if (CompatibleMaterialIds.Contains(sourceId))
        {
            return sourceMaterial;
        }

        if (CompatibleMaterialCache.TryGetValue(sourceId, out Material cached) && cached)
        {
            return cached;
        }

        Shader? shader = ResolveCompatibleShader(sourceMaterial.shader);
        if (!shader)
        {
            return sourceMaterial;
        }

        if (sourceMaterial.shader && string.Equals(sourceMaterial.shader.name, shader.name, StringComparison.Ordinal))
        {
            CompatibleMaterialIds.Add(sourceId);
            return sourceMaterial;
        }

        Material compatibleMaterial = new Material(shader)
        {
            name = $"{sourceMaterial.name}_WarmupCompat"
        };
        CopyMaterialPresentation(sourceMaterial, compatibleMaterial);
        CompatibleMaterialCache[sourceId] = compatibleMaterial;
        CompatibleMaterialIds.Add(compatibleMaterial.GetInstanceID());
        return compatibleMaterial;
    }

    private static Shader? ResolveCompatibleShader(Shader? sourceShader)
    {
        List<string> candidates = new();
        bool hasRenderPipeline = GraphicsSettings.currentRenderPipeline != null;
        if (sourceShader && IsUsableShaderName(sourceShader.name, hasRenderPipeline))
        {
            candidates.Add(sourceShader.name);
        }

        if (hasRenderPipeline)
        {
            candidates.Add("Universal Render Pipeline/Lit");
            candidates.Add("Universal Render Pipeline/Simple Lit");
            candidates.Add("Universal Render Pipeline/Unlit");
        }

        candidates.Add("Unlit/Color");
        candidates.Add("Standard");
        candidates.Add("Legacy Shaders/Diffuse");

        foreach (string shaderName in candidates.Distinct(StringComparer.Ordinal))
        {
            Shader shader = Shader.Find(shaderName);
            if (shader)
            {
                return shader;
            }
        }

        return sourceShader;
    }

    private static bool IsUsableShaderName(string? shaderName, bool allowUrp)
    {
        if (string.IsNullOrWhiteSpace(shaderName))
        {
            return false;
        }

        return !shaderName!.StartsWith("Hidden/", StringComparison.Ordinal)
               && (allowUrp || !shaderName.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal));
    }

    private static Material CreateTintMaterial(Color color, bool transparent, string name)
    {
        Shader shader = ResolveCompatibleShader(null) ?? Shader.Find("Sprites/Default");
        Material material = new Material(shader)
        {
            name = name,
            color = color
        };
        SetColorIfPresent(material, "_BaseColor", color);
        SetColorIfPresent(material, "_Color", color);

        if (transparent)
        {
            SetFloatIfPresent(material, "_Surface", 1f);
            SetFloatIfPresent(material, "_Blend", 0f);
            SetFloatIfPresent(material, "_ZWrite", 0f);
            SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        }

        return material;
    }

    private static void CopyMaterialPresentation(Material source, Material target)
    {
        if (TryGetColor(source, out Color color))
        {
            color.a = 1f;
            SetColorIfPresent(target, "_BaseColor", color);
            SetColorIfPresent(target, "_Color", color);
            target.color = color;
        }

        CopyTexture(source, target, "_BaseMap", "_BaseMap", "_MainTex");
        CopyTexture(source, target, "_MainTex", "_MainTex", "_BaseMap");
        CopyFloat(source, target, "_Smoothness");
        CopyFloat(source, target, "_Metallic");
        CopyFloat(source, target, "_Glossiness");
    }

    private static bool TryGetColor(Material material, out Color color)
    {
        if (material.HasProperty("_BaseColor"))
        {
            color = material.GetColor("_BaseColor");
            return true;
        }

        if (material.HasProperty("_Color"))
        {
            color = material.GetColor("_Color");
            return true;
        }

        color = material.color;
        return true;
    }

    private static void CopyTexture(Material source, Material target, string targetProperty, params string[] sourceProperties)
    {
        if (!target.HasProperty(targetProperty))
        {
            return;
        }

        foreach (string sourceProperty in sourceProperties)
        {
            if (!source.HasProperty(sourceProperty))
            {
                continue;
            }

            Texture texture = source.GetTexture(sourceProperty);
            if (!texture)
            {
                continue;
            }

            target.SetTexture(targetProperty, texture);
            target.SetTextureOffset(targetProperty, source.GetTextureOffset(sourceProperty));
            target.SetTextureScale(targetProperty, source.GetTextureScale(sourceProperty));
            return;
        }
    }

    private static void CopyFloat(Material source, Material target, string property)
    {
        if (source.HasProperty(property) && target.HasProperty(property))
        {
            target.SetFloat(property, source.GetFloat(property));
        }
    }

    private static void SetColorIfPresent(Material material, string property, Color color)
    {
        if (material.HasProperty(property))
        {
            material.SetColor(property, color);
        }
    }

    private static void SetFloatIfPresent(Material material, string property, float value)
    {
        if (material.HasProperty(property))
        {
            material.SetFloat(property, value);
        }
    }

    private static Transform? FindChildByName(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, childName, StringComparison.Ordinal))
            {
                return child;
            }
        }

        return null;
    }

    private static void DestroyMaterial(ref Material? material)
    {
        if (material)
        {
            Object.Destroy(material);
        }

        material = null;
    }
}

internal sealed class WarmupObstacleVisual : MonoBehaviour
{
    private readonly Dictionary<Renderer, Material[]> originalMaterials = new();
    private bool initialized;

    public int ObjectId { get; private set; }
    public string AssetId { get; private set; } = string.Empty;

    public void Initialize(int objectId, string assetId)
    {
        ObjectId = objectId;
        AssetId = assetId ?? string.Empty;
        CacheMaterials();
    }

    public void SetNormal()
    {
        CacheMaterials();
        foreach (KeyValuePair<Renderer, Material[]> entry in originalMaterials)
        {
            if (entry.Key)
            {
                entry.Key.sharedMaterials = entry.Value;
            }
        }
    }

    public void SetGhost(Material material)
    {
        ApplyMaterial(material);
    }

    public void SetHighlighted(bool highlighted)
    {
        if (highlighted)
        {
            ApplyMaterial(WarmupObstacleAssets.GetHighlightMaterial());
        }
        else
        {
            SetNormal();
        }
    }

    private void CacheMaterials()
    {
        if (initialized)
        {
            return;
        }

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer && !originalMaterials.ContainsKey(renderer))
            {
                originalMaterials[renderer] = renderer.sharedMaterials;
            }
        }

        initialized = true;
    }

    private void ApplyMaterial(Material material)
    {
        CacheMaterials();
        foreach (Renderer renderer in originalMaterials.Keys.ToArray())
        {
            if (!renderer)
            {
                continue;
            }

            Material[] replacement = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
            for (int index = 0; index < replacement.Length; index++)
            {
                replacement[index] = material;
            }

            renderer.sharedMaterials = replacement;
        }
    }
}
