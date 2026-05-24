using UnityEngine;
using System.IO;
using UnityEngine.Networking;

[System.Serializable]
public class VisualizationSettings
{
    // Manager
    public string currentDataFolder = "data2";
    public float bloodAlpha = 0.35f;

    // Measurement settings
    // Voxel spacing in meters (x,y,z) to convert local coords to real-world units
    public Vector3 voxelSpacing = new Vector3(0.01f, 0.01f, 0.01f);
    // Calibration factor to correct scale per device/session
    public float calibrationFactor = 0.1f;
    // Default snap-to-surface behavior when selecting points
    public bool defaultSnapToSurface = true;

    // VelocityLoader
    public int velocityStepX = 5;
    public int velocityStepY = 5;
    public int velocityStepZ = 5;
    public float velocityScaleFactor = 0.001f;
    public float velocityArrowScale = 0.003f;
    public int velocityDisplayStepX = 1;
    public int velocityDisplayStepY = 1;
    public int velocityDisplayStepZ = 1;

    // LoadWSS
    public float wssArrowScale = 0.031f;
    public float wssArrowLengthMultiplier = 0.08f;
    public int wssStepX = 2;
    public int wssStepY = 2;
    public int wssStepZ = 2;

    // LoadStreamline
    public float streamlineLineWidth = 0.001f;

    // SliceVisualization
    public int heatmapResolution = 85;
    public float heatmapIntensity = 3.0f;
    public float heatmapAlpha = 1.0f;
    public float heatmapSpotSize = 0.0384f;
    public float arrowPlaneScale = 0.001f; // Added to match JSON
    public float sliceArrowScale = 0.001f; // Added to match JSON
    public float arrowSliceScale = 0.15f; // Legacy field, keeping for compatibility
    public float sliceVelocityScaleFactor = 0.0015f;
    public float targetPhysicalSize = 0.4f;
    public Vector3 additionalRotation = Vector3.zero;
    public float globalVisualizationScale = 0.1f;

    // Object Transform
    public float objectScaleMultiplier = 1.0f;
    public float objectRotationY = 0f;
}

public static class VisualizationSettingsStore
{
    private static string GlobalFilePath => Path.Combine(Application.streamingAssetsPath, "visualization_settings.json");
    private static string PersistentPath => Path.Combine(Application.persistentDataPath, "visualization_settings.json");

    public static VisualizationSettings LoadSettings(string folderPath = null)
    {
        string json = null;

        // 1. Try Specific Folder
        if (!string.IsNullOrEmpty(folderPath))
        {
            string specificPath = Path.Combine(folderPath, "visualization_settings.json");
            if (TryReadJson(specificPath, out json))
            {
                Debug.Log($"[Settings] Loaded from specific folder: {specificPath}");
                return ParseJson(json);
            }
        }

        // 2. Try Global StreamingAssets
        if (TryReadJson(GlobalFilePath, out json))
        {
            Debug.Log($"[Settings] Loaded from Global StreamingAssets");
            return ParseJson(json);
        }

        // 3. Try Persistent Data Path
        if (TryReadJson(PersistentPath, out json))
        {
            Debug.Log($"[Settings] Loaded from Persistent Data");
            return ParseJson(json);
        }

        // 4. Default
        Debug.LogWarning($"Settings file not found. Using defaults.");
        VisualizationSettings defaultSettings = new VisualizationSettings();
        // Optionally save default to folder if provided? No, don't auto-create files unless asked.
        return defaultSettings;
    }

    public static void SaveSettings(VisualizationSettings settings, string folderPath = null)
    {
        try
        {
            string json = JsonUtility.ToJson(settings, true);
            string path = GlobalFilePath;

            if (!string.IsNullOrEmpty(folderPath))
            {
                path = Path.Combine(folderPath, "visualization_settings.json");
            }

            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(path, json);
            Debug.Log($"Settings saved to {path}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error saving settings: {e.Message}");
        }
    }

    static VisualizationSettings ParseJson(string json)
    {
        try
        {
            return JsonUtility.FromJson<VisualizationSettings>(json) ?? new VisualizationSettings();
        }
        catch
        {
            return new VisualizationSettings();
        }
    }

    static bool TryReadJson(string path, out string json)
    {
        json = null;
        if (string.IsNullOrEmpty(path)) return false;

        if (path.Contains("://") || path.Contains("ms-appx"))
        {
            using (var req = UnityWebRequest.Get(path))
            {
                var op = req.SendWebRequest();
                while (!op.isDone) { }
                if (req.result == UnityWebRequest.Result.Success)
                {
                    json = req.downloadHandler.text;
                    return true;
                }
            }
        }
        else if (File.Exists(path))
        {
            json = File.ReadAllText(path);
            return true;
        }
        return false;
    }
}
