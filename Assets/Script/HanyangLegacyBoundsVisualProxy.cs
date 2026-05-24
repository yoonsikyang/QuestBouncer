using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(1000)]
public sealed class HanyangLegacyBoundsVisualProxy : MonoBehaviour
{
    private const string ObjectParentName = "Object Parent";
    private const string VesselName = "Blood Vessel obj";
    private const string ProxyName = "Hanyang Legacy Bounds Visual Proxy";
    private const string ShellName = "Legacy Bounds Shell";
    private const int CornerCount = 8;
    private const int MidpointCount = 12;
    private const float ReferenceXScale = 1.04698f;
    private const float ReferenceZScale = 1.64706f;
    private const float HandleWorldSize = 0.0135f;
    private const float ShellWidthScale = 1.1f;
    private const float ShellHeightScale = 1.06f;

    private readonly List<Transform> cornerHandles = new();
    private readonly List<Transform> midpointHandles = new();
    private Transform objectParent;
    private Transform proxyRoot;
    private Transform shell;
    private Renderer vesselRenderer;
    private Material shellMaterial;
    private Material handleMaterial;
    private bool loggedReady;
    private bool meshModeVesselRendererHidden;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var runner = new GameObject(nameof(HanyangLegacyBoundsVisualProxy));
        DontDestroyOnLoad(runner);
        runner.hideFlags = HideFlags.HideAndDontSave;
        runner.AddComponent<HanyangLegacyBoundsVisualProxy>();
    }

    private IEnumerator Start()
    {
        var deadline = Time.realtimeSinceStartup + 12f;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (TryBindSceneObjects())
            {
                EnsureProxy();
                yield break;
            }

            yield return null;
        }

        Destroy(gameObject);
    }

    private void LateUpdate()
    {
        if (objectParent == null || vesselRenderer == null)
        {
            if (!TryBindSceneObjects())
                return;

            EnsureProxy();
        }

        HideConflictingBoundsVisuals();
        UpdateProxy();
        ApplyMeshModeVesselVisibility();
    }

    private void ApplyMeshModeVesselVisibility()
    {
        if (vesselRenderer == null || Manager.Instance == null)
            return;

        if (Manager.Instance.visualizationMode == VisualizationMode.Mesh)
        {
            if (vesselRenderer.enabled)
                vesselRenderer.enabled = false;

            meshModeVesselRendererHidden = true;
            return;
        }

        if (meshModeVesselRendererHidden)
        {
            vesselRenderer.enabled = true;
            meshModeVesselRendererHidden = false;
        }
    }

    private bool TryBindSceneObjects()
    {
        objectParent = FindInActiveScene(ObjectParentName);
        var vessel = objectParent == null ? null : FindChildByName(objectParent, VesselName);
        vesselRenderer = vessel == null ? null : vessel.GetComponent<Renderer>();
        return objectParent != null && vesselRenderer != null;
    }

    private void EnsureProxy()
    {
        if (objectParent == null)
            return;

        var existing = objectParent.Find(ProxyName);
        proxyRoot = existing != null ? existing : new GameObject(ProxyName).transform;
        proxyRoot.SetParent(objectParent, false);
        proxyRoot.gameObject.SetActive(true);

        EnsureMaterials();
        EnsureShell();
        EnsureHandles();
    }

    private void EnsureMaterials()
    {
        if (shellMaterial == null)
        {
            var shader = Shader.Find("Hanyang/Legacy Bounds Shell");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

            shellMaterial = new Material(shader)
            {
                name = "Hanyang Legacy Bounds Shell Runtime",
                renderQueue = 3000
            };
            SetMaterialFloat(shellMaterial, "_Alpha", 0.88f);
            SetMaterialFloat(shellMaterial, "_Intensity", 0.46f);
            SetMaterialColor(shellMaterial, new Color(0.35f, 0.35f, 0.35f, 0.92f));
        }

        if (handleMaterial == null)
        {
            var shader = Shader.Find("Hanyang/Legacy Bounds Handle") ??
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color");
            handleMaterial = new Material(shader)
            {
                name = "Hanyang Legacy Bounds Handle Runtime",
                renderQueue = 3500
            };
            SetMaterialColor(handleMaterial, new Color(0f, 0.5f, 1f, 0.74f));
        }
    }

    private void EnsureShell()
    {
        if (shell != null)
            return;

        var shellObject = new GameObject(ShellName);
        shellObject.transform.SetParent(proxyRoot, false);
        shellObject.AddComponent<MeshFilter>().sharedMesh = CreateQuadMesh();
        shellObject.AddComponent<MeshRenderer>().sharedMaterial = shellMaterial;
        shell = shellObject.transform;
    }

    private void EnsureHandles()
    {
        while (cornerHandles.Count < CornerCount)
        {
            cornerHandles.Add(CreatePrimitiveHandle($"corner_{cornerHandles.Count}", PrimitiveType.Cube));
        }

        while (midpointHandles.Count < MidpointCount)
        {
            midpointHandles.Add(CreatePrimitiveHandle($"midpoint_{midpointHandles.Count}", PrimitiveType.Sphere));
        }
    }

    private Transform CreatePrimitiveHandle(string name, PrimitiveType primitiveType)
    {
        var handle = GameObject.CreatePrimitive(primitiveType);
        handle.name = name;
        handle.transform.SetParent(proxyRoot, false);

        var collider = handle.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        var renderer = handle.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = handleMaterial;

        return handle.transform;
    }

    private void HideConflictingBoundsVisuals()
    {
        foreach (var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
        {
            if (renderer == null || !renderer.gameObject.scene.IsValid())
                continue;

            if (proxyRoot != null && renderer.transform.IsChildOf(proxyRoot))
                continue;

            var path = GetPath(renderer.transform);
            if (path.IndexOf("BoundingBoxWithTraditionalHandles", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("/rigRoot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                IsMenuTransientVisual(path))
            {
                renderer.enabled = false;
            }
        }
    }

    private static bool IsMenuTransientVisual(string path)
    {
        if (path.IndexOf("Button Parent", StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        return path.IndexOf("/GravVisualCue/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void UpdateProxy()
    {
        if (proxyRoot == null || shell == null || vesselRenderer == null)
            return;

        var bounds = CalculateReferenceBounds(vesselRenderer.bounds);
        var camera = Camera.main;
        var rotation = camera == null
            ? Quaternion.identity
            : Quaternion.LookRotation(bounds.center - camera.transform.position, Vector3.up);

        shell.position = bounds.center;
        shell.rotation = rotation;
        SetWorldScale(shell, new Vector3(bounds.size.x * ShellWidthScale, bounds.size.y * ShellHeightScale, 1f));

        var corners = GetCorners(bounds);
        for (var i = 0; i < cornerHandles.Count && i < corners.Length; i++)
        {
            cornerHandles[i].position = corners[i];
            cornerHandles[i].rotation = rotation;
            SetWorldUniformScale(cornerHandles[i], HandleWorldSize);
        }

        var midpoints = GetEdgeMidpoints(bounds);
        for (var i = 0; i < midpointHandles.Count && i < midpoints.Length; i++)
        {
            midpointHandles[i].position = midpoints[i];
            midpointHandles[i].rotation = rotation;
            SetWorldUniformScale(midpointHandles[i], HandleWorldSize);
        }

        if (!loggedReady)
        {
            Debug.Log($"Hanyang legacy bounds visual proxy ready: bounds={FormatBounds(bounds)}");
            loggedReady = true;
        }
    }

    private static Bounds CalculateReferenceBounds(Bounds vesselBounds)
    {
        var size = new Vector3(
            vesselBounds.size.x * ReferenceXScale,
            vesselBounds.size.y,
            vesselBounds.size.z * ReferenceZScale);
        return new Bounds(vesselBounds.center, size);
    }

    private static Vector3[] GetCorners(Bounds bounds)
    {
        var min = bounds.min;
        var max = bounds.max;
        return new[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, max.y, max.z)
        };
    }

    private static Vector3[] GetEdgeMidpoints(Bounds bounds)
    {
        var min = bounds.min;
        var max = bounds.max;
        var center = bounds.center;
        return new[]
        {
            new Vector3(center.x, min.y, min.z),
            new Vector3(min.x, center.y, min.z),
            new Vector3(center.x, max.y, min.z),
            new Vector3(max.x, center.y, min.z),
            new Vector3(center.x, min.y, max.z),
            new Vector3(min.x, center.y, max.z),
            new Vector3(center.x, max.y, max.z),
            new Vector3(max.x, center.y, max.z),
            new Vector3(min.x, min.y, center.z),
            new Vector3(max.x, min.y, center.z),
            new Vector3(min.x, max.y, center.z),
            new Vector3(max.x, max.y, center.z)
        };
    }

    private static Mesh CreateQuadMesh()
    {
        var mesh = new Mesh
        {
            name = "Hanyang Legacy Bounds Shell Quad"
        };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f)
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
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Transform FindInActiveScene(string objectName)
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
            return null;

        foreach (var root in scene.GetRootGameObjects())
        {
            var found = FindChildByName(root.transform, objectName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static Transform FindChildByName(Transform root, string objectName)
    {
        if (root.name == objectName)
            return root;

        for (var i = 0; i < root.childCount; i++)
        {
            var found = FindChildByName(root.GetChild(i), objectName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static void SetWorldScale(Transform target, Vector3 worldScale)
    {
        var parentScale = target.parent == null ? Vector3.one : target.parent.lossyScale;
        target.localScale = new Vector3(
            Divide(worldScale.x, parentScale.x),
            Divide(worldScale.y, parentScale.y),
            Divide(worldScale.z, parentScale.z));
    }

    private static void SetWorldUniformScale(Transform target, float worldSize)
    {
        var parentScale = target.parent == null ? Vector3.one : target.parent.lossyScale;
        var divisor = Mathf.Max(Mathf.Abs(parentScale.x), Mathf.Abs(parentScale.y), Mathf.Abs(parentScale.z), 0.0001f);
        target.localScale = Vector3.one * (worldSize / divisor);
    }

    private static float Divide(float value, float divisor)
    {
        return Mathf.Abs(divisor) < 0.0001f ? value : value / divisor;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    private static void SetMaterialFloat(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
            material.SetFloat(propertyName, value);
    }

    private static string GetPath(Transform transform)
    {
        var stack = new Stack<string>();
        while (transform != null)
        {
            stack.Push(transform.name);
            transform = transform.parent;
        }

        return string.Join("/", stack);
    }

    private static string FormatBounds(Bounds bounds)
    {
        return $"center={FormatVector(bounds.center)} size={FormatVector(bounds.size)}";
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:0.###}, {value.y:0.###}, {value.z:0.###})";
    }
}
