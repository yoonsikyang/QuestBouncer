using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HanyangVisionOSMaterialCompatibility
{
#if UNITY_VISIONOS && !UNITY_EDITOR
    private const string UrpLitShaderName = "Universal Render Pipeline/Lit";
    private const string UrpUnlitShaderName = "Universal Render Pipeline/Unlit";
    private const string TmpMobileShaderName = "TextMeshPro/Mobile/Distance Field";
    private const string TmpShaderName = "TextMeshPro/Distance Field";

    private static readonly HashSet<int> ProcessedMaterials = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        Apply("before scene load");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyAfterSceneLoad()
    {
        Apply("after scene load");
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Apply($"scene loaded: {scene.name}");
    }

    private static void Apply(string phase)
    {
        var litShader = Shader.Find(UrpLitShaderName);
        var unlitShader = Shader.Find(UrpUnlitShaderName);
        var textMeshProShader = Shader.Find(TmpMobileShaderName) ?? Shader.Find(TmpShaderName);
        if (litShader == null || unlitShader == null)
        {
            Debug.LogWarning($"Hanyang visionOS material compatibility skipped in {phase}: URP shaders unavailable.");
            return;
        }

        var convertedCount = 0;
        foreach (var material in Resources.FindObjectsOfTypeAll<Material>())
        {
            if (TryConvertMaterial(material, litShader, unlitShader, textMeshProShader))
                convertedCount++;
        }

        foreach (var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
        {
            var materials = renderer.sharedMaterials;
            var changed = false;
            for (var i = 0; i < materials.Length; i++)
            {
                if (TryConvertMaterial(materials[i], litShader, unlitShader, textMeshProShader))
                {
                    convertedCount++;
                    changed = true;
                }
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }

        if (convertedCount > 0)
            Debug.Log($"Hanyang visionOS material compatibility converted {convertedCount} visionOS-incompatible materials in {phase}.");
    }

    private static bool TryConvertMaterial(Material material, Shader litShader, Shader unlitShader, Shader textMeshProShader)
    {
        if (material == null)
            return false;

        var shaderName = material.shader != null ? material.shader.name : string.Empty;
        if (!ShouldConvertShader(shaderName))
            return false;

        if (!ProcessedMaterials.Add(material.GetInstanceID()))
            return false;

        var materialColor = GetMaterialColor(material, Color.white);
        var materialTexture = GetMaterialTexture(material);

        if (IsTextMeshProShader(shaderName) && textMeshProShader != null)
        {
            material.shader = textMeshProShader;
            SetColor(material, "_FaceColor", materialColor);
            SetColor(material, "_Color", materialColor);
            return true;
        }

        if (IsButtonIconMaterial(material))
        {
            material.shader = unlitShader;
            ConfigureUrpAlphaClippedIcon(material, materialColor);
            SetMainTexture(material, materialTexture);
        }
        else if (IsTransparentProxy(material, shaderName))
        {
            material.shader = unlitShader;
            ConfigureUrpTransparent(material, materialColor.a <= 0.01f ? new Color(0f, 0f, 0f, 0f) : materialColor);
            SetMainTexture(material, materialTexture);
        }
        else if (IsBlueHandle(material))
        {
            material.shader = litShader;
            ConfigureUrpOpaque(material, new Color(0.10784314f, 0.5647059f, 1f, 1f));
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_Smoothness", 0.35f);
            SetMainTexture(material, materialTexture);
        }
        else if (IsBackplate(material, shaderName))
        {
            material.shader = unlitShader;
            ConfigureUrpOpaque(material, new Color(0.006f, 0.04f, 0.30f, 1f));
            SetMainTexture(material, materialTexture);
        }
        else if (IsTransparentShader(shaderName, materialColor))
        {
            material.shader = unlitShader;
            ConfigureUrpTransparent(material, materialColor);
            SetMainTexture(material, materialTexture);
        }
        else
        {
            material.shader = unlitShader;
            ConfigureUrpOpaque(material, materialColor);
            SetMainTexture(material, materialTexture);
        }

        return true;
    }

    private static bool ShouldConvertShader(string shaderName)
    {
        return shaderName.StartsWith("Graphics Tools/", StringComparison.Ordinal) ||
               shaderName.StartsWith("Mixed Reality Toolkit/", StringComparison.Ordinal) ||
               shaderName.StartsWith("Custom/", StringComparison.Ordinal) ||
               shaderName.StartsWith("Legacy Shaders/", StringComparison.Ordinal) ||
               shaderName.Equals("Shell_Rounded_Bound", StringComparison.Ordinal);
    }

    private static bool IsTextMeshProShader(string shaderName)
    {
        return shaderName.IndexOf("TextMeshPro", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsTransparentShader(string shaderName, Color materialColor)
    {
        return materialColor.a < 0.99f ||
               shaderName.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0 ||
               shaderName.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0 ||
               shaderName.IndexOf("Alpha", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsTransparentProxy(Material material, string shaderName)
    {
        return shaderName.IndexOf("Frontplate", StringComparison.OrdinalIgnoreCase) >= 0 ||
               material.name.IndexOf("Cage", StringComparison.OrdinalIgnoreCase) >= 0 ||
               material.name.IndexOf("BoundingBox", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsBlueHandle(Material material)
    {
        return material.name.IndexOf("Handle", StringComparison.OrdinalIgnoreCase) >= 0 ||
               material.name.IndexOf("BoundsControl", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsBackplate(Material material, string shaderName)
    {
        return shaderName.IndexOf("Backplate", StringComparison.OrdinalIgnoreCase) >= 0 ||
               material.name.IndexOf("BackPlate", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsButtonIconMaterial(Material material)
    {
        return material.name.StartsWith("HolographicButtonIcon", StringComparison.OrdinalIgnoreCase);
    }

    private static void ConfigureUrpOpaque(Material material, Color color)
    {
        material.renderQueue = 2000;
        material.SetOverrideTag("RenderType", "Opaque");
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_SURFACE_TYPE_OPAQUE");
        SetFloat(material, "_Surface", 0f);
        SetFloat(material, "_Blend", 0f);
        SetFloat(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        SetFloat(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
        SetFloat(material, "_ZWrite", 1f);
        SetFloat(material, "_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        SetFloat(material, "_AlphaClip", 0f);
        SetFloat(material, "_QueueOffset", 0f);
        SetColor(material, "_BaseColor", color);
        SetColor(material, "_Color", color);
    }

    private static void ConfigureUrpTransparent(Material material, Color color)
    {
        material.renderQueue = 3000;
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        SetFloat(material, "_Surface", 1f);
        SetFloat(material, "_Blend", 0f);
        SetFloat(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        SetFloat(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        SetFloat(material, "_ZWrite", 0f);
        SetFloat(material, "_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        SetFloat(material, "_AlphaClip", 0f);
        SetFloat(material, "_QueueOffset", 0f);
        SetColor(material, "_BaseColor", color);
        SetColor(material, "_Color", color);
    }

    private static void ConfigureUrpAlphaClippedIcon(Material material, Color color)
    {
        var iconColor = color.a <= 0.01f ? Color.white : color;
        iconColor.a = 1f;

        material.renderQueue = 2450;
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.EnableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_SURFACE_TYPE_OPAQUE");
        SetFloat(material, "_Surface", 0f);
        SetFloat(material, "_Blend", 0f);
        SetFloat(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        SetFloat(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
        SetFloat(material, "_ZWrite", 1f);
        SetFloat(material, "_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        SetFloat(material, "_AlphaClip", 1f);
        SetFloat(material, "_Cutoff", 0.5f);
        SetFloat(material, "_QueueOffset", 0f);
        SetColor(material, "_BaseColor", iconColor);
        SetColor(material, "_Color", iconColor);
    }

    private static void SetFloat(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
            material.SetFloat(propertyName, value);
    }

    private static void SetColor(Material material, string propertyName, Color value)
    {
        if (material.HasProperty(propertyName))
            material.SetColor(propertyName, value);
    }

    private static Color GetMaterialColor(Material material, Color fallback)
    {
        if (material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color"))
            return material.GetColor("_Color");
        if (material.HasProperty("_FaceColor"))
            return material.GetColor("_FaceColor");
        return fallback;
    }

    private static Texture GetMaterialTexture(Material material)
    {
        if (material.HasProperty("_BaseMap"))
        {
            var baseMap = material.GetTexture("_BaseMap");
            if (baseMap != null)
                return baseMap;
        }

        if (material.HasProperty("_MainTex"))
        {
            var mainTex = material.GetTexture("_MainTex");
            if (mainTex != null)
                return mainTex;
        }

        return null;
    }

    private static void SetMainTexture(Material material, Texture texture)
    {
        if (texture == null)
            return;

        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", texture);
    }
#endif
}
