using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace OPLServerBanners;

internal static class TrophyAssets
{
    public const string WbwCupId = "wbw_cup";
    public const string BiCupId = "bi_cup";

    private const string BundleFileName = "s4_trophies";
    private const float MinimumMetallic = 0.70f;
    private const float MinimumSmoothness = 0.62f;
    private const float BrightnessMultiplier = 1.12f;
    // Keep the silver readable even on arenas with dark or disabled reflection probes.
    // These values deliberately avoid the near-mirror finish that made the cup appear black.
    private const float SilverBodyMetallic = 0.65f;
    private const float SilverBodySmoothness = 0.65f;
    private const float SilverBodyEmissionMultiplier = 0.60f;
    private static readonly Dictionary<string, string> AssetPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        [WbwCupId] = "Assets/Trophys/Prefabs/S4 WBW CUP.prefab",
        [BiCupId] = "Assets/Trophys/Prefabs/S4 BI CUP.prefab"
    };

    private static readonly List<Renderer> BoundsRenderers = new();
    private static readonly List<Collider> PuckColliderBuffer = new();
    private static readonly Dictionary<int, Material> CompatibleMaterials = new();
    private static AssetBundle? bundle;
    private static bool hasCachedPuckColliderTemplate;
    private static int cachedPuckColliderLayer;
    private static PhysicsMaterial? cachedPuckColliderMaterial;

    public static string GetLabel(string trophyId)
    {
        return string.Equals(trophyId, WbwCupId, StringComparison.OrdinalIgnoreCase) ? "WBW Cup" : "BI Cup";
    }

    public static GameObject? CreateInstance(string trophyId, string name, bool serverPhysical)
    {
        if (!AssetPaths.TryGetValue(trophyId, out string? assetPath))
        {
            return null;
        }

        AssetBundle? loadedBundle = GetBundle();
        GameObject? source = loadedBundle ? loadedBundle.LoadAsset<GameObject>(assetPath) : null;
        if (!source)
        {
            Plugin.LogError($"Trophy prefab '{assetPath}' was not found in {BundleFileName}.");
            return null;
        }

        GameObject instance = Object.Instantiate(source);
        instance.name = name;
        instance.SetActive(true);
        PrepareInstance(instance, serverPhysical);
        return instance;
    }

    public static void ClampBottomToIce(Transform transform)
    {
        BoundsRenderers.Clear();
        transform.GetComponentsInChildren(true, BoundsRenderers);
        bool hasBounds = false;
        Bounds bounds = default;
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

        if (hasBounds && bounds.min.y < 0f)
        {
            transform.position += Vector3.up * -bounds.min.y;
        }
    }

    public static void Teardown()
    {
        if (bundle)
        {
            bundle.Unload(unloadAllLoadedObjects: false);
        }

        bundle = null;
        foreach (Material material in CompatibleMaterials.Values)
        {
            if (material)
            {
                Object.Destroy(material);
            }
        }

        CompatibleMaterials.Clear();
        hasCachedPuckColliderTemplate = false;
        cachedPuckColliderMaterial = null;
    }

    private static AssetBundle? GetBundle()
    {
        if (bundle)
        {
            return bundle;
        }

        string bundlePath = Path.Combine(ServerFileSystemPaths.GetPluginDirectoryPath(), "Assets", "Trophies", BundleFileName);
        if (!File.Exists(bundlePath))
        {
            Plugin.LogError($"Trophy asset bundle was not found at '{bundlePath}'.");
            return null;
        }

        bundle = AssetBundle.LoadFromFile(bundlePath);
        if (!bundle)
        {
            Plugin.LogError($"Failed to load trophy asset bundle '{bundlePath}'.");
        }

        return bundle;
    }

    private static void PrepareInstance(GameObject instance, bool serverPhysical)
    {
        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.hideFlags = HideFlags.None;
        }

        foreach (Rigidbody rigidbody in instance.GetComponentsInChildren<Rigidbody>(true))
        {
            Object.Destroy(rigidbody);
        }

        NormalizeMaterials(instance);

        // Trophies are display-only. Remove any collider left in an older bundle so
        // they cannot block players, pucks, or shots on either client or server.
        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
        {
            if (!collider)
            {
                continue;
            }

            collider.enabled = false;
            Object.Destroy(collider);
        }
    }

    private static void NormalizeMaterials(GameObject root)
    {
        Shader? shader = ResolveCompatibleShader();
        if (!shader)
        {
            Plugin.LogError("No compatible shader was available for the trophy materials.");
            return;
        }

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            // TLAN can disable the rink probe. Keep a skybox fallback for metallic shine,
            // while the controlled emission below prevents that fallback from going black.
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
            renderer.allowOcclusionWhenDynamic = true;

            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int index = 0; index < materials.Length; index++)
            {
                Material? source = materials[index];
                if (!source)
                {
                    continue;
                }

                int sourceId = source.GetInstanceID();
                if (!CompatibleMaterials.TryGetValue(sourceId, out Material compatible) || !compatible)
                {
                    compatible = new Material(shader)
                    {
                        name = $"{source.name}_PuckCompatible"
                    };
                    CopyMaterialPresentation(source, compatible);
                    CompatibleMaterials[sourceId] = compatible;
                }

                materials[index] = compatible;
                changed = true;
            }

            if (changed)
            {
                renderer.sharedMaterials = materials;
            }
        }
    }

    private static Shader? ResolveCompatibleShader()
    {
        return Shader.Find("Universal Render Pipeline/Lit")
               ?? Shader.Find("Universal Render Pipeline/Simple Lit")
               ?? Shader.Find("Standard")
               ?? Shader.Find("Unlit/Color");
    }

    private static void CopyMaterialPresentation(Material source, Material target)
    {
        Color color = source.HasProperty("_BaseColor")
            ? source.GetColor("_BaseColor")
            : source.HasProperty("_Color")
                ? source.GetColor("_Color")
                : source.color;
        // Material.003 is the trophy body in both supplied prefabs. Name matching keeps
        // the light silver treatment off the deliberately dark/gold trim material.
        bool isLightBodyMaterial = string.Equals(source.name, "Material.003", StringComparison.OrdinalIgnoreCase)
                                   || color.grayscale >= 0.35f;
        color = Brighten(color);
        if (isLightBodyMaterial)
        {
            color = Color.Lerp(color, new Color(0.96f, 0.97f, 1f, color.a), 0.45f);
        }

        SetColor(target, "_BaseColor", color);
        SetColor(target, "_Color", color);
        target.color = color;

        float metallic = Mathf.Max(MinimumMetallic, GetFloat(source, "_Metallic", 0f));
        float smoothness = Mathf.Max(MinimumSmoothness, GetFloat(source, "_Smoothness", GetFloat(source, "_Glossiness", 0f)));
        SetFloat(target, "_Metallic", isLightBodyMaterial ? SilverBodyMetallic : metallic);
        SetFloat(target, "_Smoothness", isLightBodyMaterial ? SilverBodySmoothness : smoothness);
        CopyFloat(source, target, "_Glossiness");
        SetFloat(target, "_EnvironmentReflections", 1f);
        SetFloat(target, "_SpecularHighlights", 1f);
        if (isLightBodyMaterial)
        {
            SetEmission(target, color * SilverBodyEmissionMultiplier);
        }

        CopyBaseTexture(source, target);
    }

    private static Color Brighten(Color color)
    {
        return new Color(
            Mathf.Min(1f, color.r * BrightnessMultiplier),
            Mathf.Min(1f, color.g * BrightnessMultiplier),
            Mathf.Min(1f, color.b * BrightnessMultiplier),
            color.a);
    }

    private static float GetFloat(Material material, string property, float fallback)
    {
        return material.HasProperty(property) ? material.GetFloat(property) : fallback;
    }

    private static void CopyFloat(Material source, Material target, string property)
    {
        if (source.HasProperty(property) && target.HasProperty(property))
        {
            target.SetFloat(property, source.GetFloat(property));
        }
    }

    private static void SetFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property))
        {
            material.SetFloat(property, value);
        }
    }

    private static void SetEmission(Material material, Color color)
    {
        if (!material.HasProperty("_EmissionColor"))
        {
            return;
        }

        material.SetColor("_EmissionColor", color);
        material.EnableKeyword("_EMISSION");
    }

    private static void CopyBaseTexture(Material source, Material target)
    {
        string[] sourceProperties = { "_BaseMap", "_MainTex" };
        foreach (string sourceProperty in sourceProperties)
        {
            if (!source.HasProperty(sourceProperty))
            {
                continue;
            }

            Texture? texture = source.GetTexture(sourceProperty);
            if (!texture)
            {
                continue;
            }

            if (target.HasProperty("_BaseMap"))
            {
                target.SetTexture("_BaseMap", texture);
                target.SetTextureOffset("_BaseMap", source.GetTextureOffset(sourceProperty));
                target.SetTextureScale("_BaseMap", source.GetTextureScale(sourceProperty));
            }

            if (target.HasProperty("_MainTex"))
            {
                target.SetTexture("_MainTex", texture);
            }

            return;
        }
    }

    private static void SetColor(Material material, string property, Color color)
    {
        if (material.HasProperty(property))
        {
            material.SetColor(property, color);
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
            Collider? collider = PuckColliderBuffer.FirstOrDefault(candidate => candidate && candidate.enabled && !candidate.isTrigger);
            if (!collider)
            {
                continue;
            }

            cachedPuckColliderLayer = collider.gameObject.layer;
            cachedPuckColliderMaterial = collider.sharedMaterial;
            hasCachedPuckColliderTemplate = true;
            colliderLayer = cachedPuckColliderLayer;
            colliderMaterial = cachedPuckColliderMaterial;
            return true;
        }

        colliderLayer = 0;
        colliderMaterial = null;
        return false;
    }
}
