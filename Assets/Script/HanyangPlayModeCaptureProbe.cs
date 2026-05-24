using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed class HanyangPlayModeCaptureProbe : MonoBehaviour
{
    private const string CaptureArgument = "-hanyangPlayCapturePath";
    private const float InitialDelaySeconds = 5f;
    private const float MaxReadyWaitSeconds = 120f;
    private const float ReadySettleSeconds = 2f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartIfRequested()
    {
        var capturePath = GetArgument(CaptureArgument);
        if (string.IsNullOrWhiteSpace(capturePath))
            return;

        Application.runInBackground = true;
        var probe = new GameObject(nameof(HanyangPlayModeCaptureProbe));
        DontDestroyOnLoad(probe);
        probe.hideFlags = HideFlags.HideAndDontSave;
        probe.AddComponent<HanyangPlayModeCaptureProbe>().StartCoroutine(CaptureAfterDelay(capturePath));
        Debug.Log($"Hanyang parity runtime Play Mode capture armed: {capturePath}");
    }

    private static IEnumerator CaptureAfterDelay(string capturePath)
    {
        yield return new WaitForSecondsRealtime(InitialDelaySeconds);

        var deadline = Time.realtimeSinceStartup + MaxReadyWaitSeconds;
        var nextReadinessLogAt = Time.realtimeSinceStartup;
        string readinessReason;
        while (!IsSceneReadyForParityCapture(out readinessReason) && Time.realtimeSinceStartup < deadline)
        {
            if (Time.realtimeSinceStartup >= nextReadinessLogAt)
            {
                Debug.Log($"Hanyang parity runtime capture waiting for ready scene: {readinessReason}");
                nextReadinessLogAt = Time.realtimeSinceStartup + 5f;
            }

            yield return null;
        }

        if (IsSceneReadyForParityCapture(out readinessReason))
        {
            Debug.Log("Hanyang parity runtime capture readiness satisfied.");
        }
        else
        {
            Debug.LogWarning($"Hanyang parity runtime capture readiness timed out; capturing current scene anyway. Last reason: {readinessReason}");
        }

        yield return new WaitForSecondsRealtime(ReadySettleSeconds);

        Directory.CreateDirectory(Path.GetDirectoryName(capturePath) ?? ".");
        LogRuntimeSurface();
        CaptureMainCamera(GetSiblingCapturePath(capturePath, "-camera-main"));
        ScreenCapture.CaptureScreenshot(capturePath);
        Debug.Log($"Hanyang parity runtime Play Mode screenshot requested: {capturePath}");
    }

    private static void LogRuntimeSurface()
    {
        var cameras = FindObjectsOfType<Camera>(includeInactive: true)
            .Where(camera => camera.gameObject.scene.IsValid())
            .OrderBy(camera => GetPath(camera.transform))
            .ToArray();
        foreach (var camera in cameras)
        {
            Debug.Log($"Hanyang parity runtime camera report: path={GetPath(camera.transform)} tag={camera.tag} enabled={camera.enabled} active={camera.gameObject.activeInHierarchy} depth={camera.depth} clear={camera.clearFlags} bg={FormatColor(camera.backgroundColor)} near={camera.nearClipPlane} far={camera.farClipPlane} fov={camera.fieldOfView} cullingMask=0x{camera.cullingMask:X8} main={camera == Camera.main}");
        }

        var renderers = FindObjectsOfType<Renderer>(includeInactive: true)
            .Where(renderer => renderer.gameObject.scene.IsValid())
            .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)
            .OrderBy(renderer => GetPath(renderer.transform))
            .ToArray();
        Debug.Log($"Hanyang parity runtime active renderer count: {renderers.Length} bounds={FormatBounds(CalculateBounds(renderers))}");
        foreach (var renderer in renderers)
        {
            var path = GetPath(renderer.transform);
            if (!ShouldReportRenderer(path, renderer))
                continue;

            Debug.Log($"Hanyang parity runtime renderer report: path={path} type={renderer.GetType().Name} layer={LayerMask.LayerToName(renderer.gameObject.layer)} bounds={FormatBounds(renderer.bounds)} materials={DescribeMaterials(renderer)}");
        }

        var canvasRenderers = FindObjectsOfType<CanvasRenderer>(includeInactive: true)
            .Where(renderer => renderer.gameObject.scene.IsValid())
            .Where(renderer => renderer.gameObject.activeInHierarchy)
            .OrderBy(renderer => GetPath(renderer.transform))
            .ToArray();
        Debug.Log($"Hanyang parity runtime active canvas renderer count: {canvasRenderers.Length}");
        foreach (var canvasRenderer in canvasRenderers)
        {
            var path = GetPath(canvasRenderer.transform);
            if (path.IndexOf("progress", StringComparison.OrdinalIgnoreCase) < 0 &&
                path.IndexOf("Loading", StringComparison.OrdinalIgnoreCase) < 0 &&
                path.IndexOf("Folder", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var material = canvasRenderer.GetMaterial();
            Debug.Log($"Hanyang parity runtime canvas renderer report: path={path} material={DescribeMaterial(material)}");
        }
    }

    private static bool IsSceneReadyForParityCapture(out string reason)
    {
        var camera = Camera.main;
        if (camera == null || !camera.enabled || !camera.gameObject.activeInHierarchy)
        {
            reason = "Main Camera is not active yet.";
            return false;
        }

        var manager = Manager.Instance ?? FindObjectOfType<Manager>();
        if (manager == null)
        {
            reason = "Manager instance is not available yet.";
            return false;
        }

        if (manager.progress != null && manager.progress.activeInHierarchy)
        {
            reason = "Manager progress UI is still active.";
            return false;
        }

        if (manager.mainUI != null && !manager.mainUI.activeInHierarchy)
        {
            reason = "Manager main UI is not active yet.";
            return false;
        }

        if (manager.bloodVesselMesh == null || !manager.bloodVesselMesh.activeInHierarchy)
        {
            reason = "Blood Vessel obj is not active yet.";
            return false;
        }

        var bloodMeshFilter = manager.bloodVesselMesh.GetComponent<MeshFilter>();
        if (bloodMeshFilter == null || bloodMeshFilter.sharedMesh == null || bloodMeshFilter.sharedMesh.vertexCount == 0)
        {
            reason = "Blood Vessel mesh is not loaded yet.";
            return false;
        }

        var bloodRenderer = manager.bloodVesselMesh.GetComponent<MeshRenderer>();
        if (bloodRenderer == null)
        {
            reason = "Blood Vessel renderer component is not available yet.";
            return false;
        }

        var bloodMaterial = bloodRenderer.sharedMaterial;
        if (bloodMaterial == null || bloodMaterial.shader == null || !bloodMaterial.shader.isSupported)
        {
            reason = $"Blood Vessel material is not renderable yet: {DescribeMaterial(bloodMaterial)}.";
            return false;
        }

        var renderers = FindObjectsOfType<Renderer>(includeInactive: true)
            .Where(renderer => renderer.gameObject.scene.IsValid())
            .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)
            .Select(renderer => GetPath(renderer.transform))
            .ToArray();

        if (!renderers.Any(path => path.IndexOf("Main Menu", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            reason = "Main Menu renderers are not active yet.";
            return false;
        }

        if (!renderers.Any(IsBoundsHandlePath))
        {
            reason = "Bounding box handle renderers are not active yet.";
            return false;
        }

        reason = "ready";
        return true;
    }

    private static bool ShouldReportRenderer(string path, Renderer renderer)
    {
        if (path.IndexOf("Main Menu", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("Object Parent", StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf("Blood Vessel", StringComparison.OrdinalIgnoreCase) >= 0 ||
            IsBoundsHandlePath(path))
        {
            return true;
        }

        return renderer.sharedMaterials.Any(material =>
            material != null &&
            (material.name.IndexOf("Holographic", StringComparison.OrdinalIgnoreCase) >= 0 ||
             material.name.IndexOf("MRTK_Grabbable", StringComparison.OrdinalIgnoreCase) >= 0 ||
             material.name.IndexOf("New Material", StringComparison.OrdinalIgnoreCase) >= 0));
    }

    private static bool IsBoundsHandlePath(string path)
    {
        return path.IndexOf("midpoint_", StringComparison.OrdinalIgnoreCase) >= 0 ||
               path.IndexOf("corner_", StringComparison.OrdinalIgnoreCase) >= 0 ||
               path.IndexOf("ScaleHandle", StringComparison.OrdinalIgnoreCase) >= 0 ||
               path.IndexOf("RotateHandle", StringComparison.OrdinalIgnoreCase) >= 0 ||
               path.IndexOf("BoundsControl", StringComparison.OrdinalIgnoreCase) >= 0 ||
               path.IndexOf("BoundingBoxWith", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void CaptureMainCamera(string cameraCapturePath)
    {
        var camera = Camera.main;
        if (camera == null)
        {
            Debug.LogWarning("Hanyang parity runtime Main Camera direct capture skipped: Camera.main is null.");
            return;
        }

        var previousTargetTexture = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var renderTexture = new RenderTexture(1024, 768, 24, RenderTextureFormat.ARGB32)
        {
            name = "HanyangParityRuntimeMainCameraCapture"
        };
        var texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);

        try
        {
            camera.targetTexture = renderTexture;
            RenderTexture.active = renderTexture;
            camera.Render();
            texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(cameraCapturePath, texture.EncodeToPNG());
            Debug.Log($"Hanyang parity runtime Main Camera direct screenshot written: {cameraCapturePath}");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Hanyang parity runtime Main Camera direct screenshot failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            camera.targetTexture = previousTargetTexture;
            RenderTexture.active = previousActive;
            Destroy(texture);
            Destroy(renderTexture);
        }
    }

    private static string GetArgument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
                return args[i + 1];
        }

        return null;
    }

    private static string GetSiblingCapturePath(string originalPath, string suffix)
    {
        var directory = Path.GetDirectoryName(originalPath);
        var fileName = Path.GetFileNameWithoutExtension(originalPath);
        var extension = Path.GetExtension(originalPath);
        return Path.Combine(string.IsNullOrWhiteSpace(directory) ? "." : directory, $"{fileName}{suffix}{extension}");
    }

    private static Bounds CalculateBounds(Renderer[] renderers)
    {
        if (renderers.Length == 0)
            return new Bounds(Vector3.zero, Vector3.zero);

        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1))
        {
            bounds.Encapsulate(renderer.bounds);
        }

        return bounds;
    }

    private static string DescribeMaterials(Renderer renderer)
    {
        return string.Join("; ", renderer.sharedMaterials.Select(DescribeMaterial));
    }

    private static string DescribeMaterial(Material material)
    {
        if (material == null)
            return "(none)";

        var shader = material.shader;
        return $"{material.name}|shader={shader?.name ?? "(none)"}|supported={(shader != null && shader.isSupported)}|queue={material.renderQueue}|instancing={material.enableInstancing}";
    }

    private static string FormatBounds(Bounds bounds)
    {
        return $"center={FormatVector(bounds.center)} size={FormatVector(bounds.size)} min={FormatVector(bounds.min)} max={FormatVector(bounds.max)}";
    }

    private static string FormatVector(Vector3 vector)
    {
        return $"({vector.x:0.###}, {vector.y:0.###}, {vector.z:0.###})";
    }

    private static string FormatColor(Color color)
    {
        return $"({color.r:0.###}, {color.g:0.###}, {color.b:0.###}, {color.a:0.###})";
    }

    private static string GetPath(Transform transform)
    {
        var path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = $"{transform.name}/{path}";
        }

        return path;
    }
}
