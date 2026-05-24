using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using UnityEngine.XR;
using Photon.Pun;

public enum VisualizationMode
{
    Mesh,           // Mesh only
    Velocity,       // Mesh + Velocity
    WSS,            // WSS only (separate mesh)
    Streamline      // Mesh + Streamline
}

public enum WSSSubMode
{
    WSSOnly,            // 1. WSS 모델만 표시
    BloodVessel_Vector, // 2. BloodVesselMesh + WSS_Vector
    WSS_Vector          // 3. WSS 모델 + WSS_Vector
}

public class Manager : MonoBehaviour
{
    public static Manager Instance;

    [Header("Settings")]
    public string objectParentName = "Object Parent";

    [Header("Data Folder Settings")]
    public string currentDataFolder = "Aorta_251224";
    public List<string> availableDataFolders = new List<string> { "data1", "data2", "Aorta_251224" };
    
    private int currentDataFolderIndex = 0;

    [Header("Visualization Mode")]
    public VisualizationMode visualizationMode = VisualizationMode.Mesh;

    [Header("WSS Sub-Mode")]
    public WSSSubMode wssSubMode = WSSSubMode.WSSOnly;

    [Header("Playback Speed")]
    [Range(0.05f, 0.5f)] public float velocityPlaybackSpeed = 0.1f;
    [Range(0.05f, 0.5f)] public float wssPlaybackSpeed = 0.1f;
    [Range(0.05f, 0.5f)] public float streamlinePlaybackSpeed = 0.1f; 

    [Header("References")]
    public GameObject progress;
    public GameObject mainUI;
    public GameObject bloodVesselMesh;
    public GameObject wssMesh;  // Separate WSS mesh
    public Material bloodMaterial; // Material for blood vessel
    public Material wssMaterial;   // Material for WSS
    public VelocityLoader velocityLoader;
    public LoadWSS wssLoader;
    public LoadStreamline streamlineLoader;
    
    [Header("Color Bar")]
    public ColorBarUI velocityColorBar; // ColorBar for Velocity visualization
    public ColorBarUI wssColorBar;      // ColorBar for WSS visualization
    [Range(0.01f, 0.2f)] public float colorBarWidth = 0.01f;
    [Range(0.1f, 0.5f)] public float colorBarHeight = 0.3f;
    [Range(0.005f, 0.05f)] public float colorBarFontSize = 0.02f;
    
    [Header("Folder Selector UI")]
    public FolderSelectorUI folderSelectorUI;
    
    [Header("Measurement Tool")]
    public VesselMeasurementTool measurementTool;
    [Tooltip("Voxel Spacing (mm 단위) - 측정 도구에 전달됨")]
    public Vector3 voxelSpacing = new Vector3(1f, 1f, 1f);
    
    [Header("Loading UI (optional)")]
    public Slider progressSlider;
    public Text progressText;
    
    [Header("Heatmap Settings")]
    public float heatmapIntensity = 1.0f;

    [Header("Transparency")]
    [Range(0f, 1f)] public float bloodAlpha = 0.35f;

    public GameObject ObjectParent { get; private set; }

    private VisualizationMode prevVisualizationMode;
    private WSSSubMode prevWSSSubMode;
    private float prevBloodAlpha = -1f;

    private bool isDataLoaded = false;
    private bool loadersInitialized = false;
    private bool velocityReadyOverride = false;
    private bool wssReadyOverride = false;

    void Awake()
    {
        Debug.Log("<color=cyan>===== Manager Awake() =====</color>");
        
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("<color=green>Manager Instance created</color>");
        }
        else
        {
            Debug.LogWarning("<color=red>Duplicate Manager found! Destroying this instance.</color>");
            Destroy(gameObject);
            return;
        }
    }

    IEnumerator Start()
    {
        Debug.Log("<color=cyan>===== Manager Start() (Coroutine) =====</color>");
        Debug.Log($"Initial visualization mode: {visualizationMode}");

        yield return null; // Wait one frame for UI to update (show loading)
        LoadAndApplySettings();

        // Initialize scene only if missing, but always initialize loaders at startup
        if (ObjectParent == null) 
        {
            yield return null; // Wait one frame for UI to update (show loading)
            InitializeScene();

            yield return null; // Wait one frame for UI to update (show loading)
            ValidateDataFolders();
            
            // Sequential Initialization to prevent freeze and ensure dependencies
            yield return null; // Wait one frame for UI to update (show loading)
            
            LoadBloodVesselMesh();
            yield return null;
            
            LoadWSSMesh();
            yield return null;
        }

        if (!loadersInitialized)
        {
            // Initialize Loaders sequentially
            yield return StartCoroutine(InitializeLoadersCoroutine());
        }

        // Initialize ColorBarUI
        InitializeColorBar(false);

        prevBloodAlpha = bloodAlpha;
        prevVisualizationMode = visualizationMode;
        ApplyVisualizationMode();
        
        // Measurement Tool 마커 부모 설정 (ObjectParent 초기화 완료 후)
        if (measurementTool != null && ObjectParent != null)
        {
            measurementTool.SetObjectParent(ObjectParent.transform);
            measurementTool.bloodVesselMesh = this.bloodVesselMesh;
        }
        
        // Finalize loading
        isDataLoaded = true;
        if (progress != null) progress.SetActive(false);
        if (mainUI != null) mainUI.SetActive(true);
        if (bloodVesselMesh != null) bloodVesselMesh.SetActive(true);

        Debug.Log("<color=green>===== Manager Initialization Complete =====</color>");
    }

    private void InitializeColorBar(bool networkCall = false)
    {
        if (ObjectParent == null) return;
        
        // Initialize Velocity ColorBar
        if (velocityColorBar == null)
        {
            GameObject velColorBarObj = new GameObject("VelocityColorBar");
            velocityColorBar = velColorBarObj.AddComponent<ColorBarUI>();
            velocityColorBar.followTarget = ObjectParent.transform;
            velocityColorBar.offsetFromTarget = new Vector3(0.15f, 0, 0);
            velocityColorBar.barWidth = colorBarWidth;
            velocityColorBar.barHeight = colorBarHeight;
            velocityColorBar.fontSize = colorBarFontSize;
            Debug.Log("<color=green>[ColorBar] Created Velocity ColorBar</color>");
        }
        
        // Initialize WSS ColorBar
        if (wssColorBar == null)
        {
            GameObject wssColorBarObj = new GameObject("WSSColorBar");
            wssColorBar = wssColorBarObj.AddComponent<ColorBarUI>();
            wssColorBar.followTarget = ObjectParent.transform;
            wssColorBar.offsetFromTarget = new Vector3(0.15f, 0, 0);
            wssColorBar.barWidth = colorBarWidth;
            wssColorBar.barHeight = colorBarHeight;
            wssColorBar.fontSize = colorBarFontSize;
            Debug.Log("<color=green>[ColorBar] Created WSS ColorBar</color>");
        }
    }
    
    /// <summary>
    /// 모든 데이터 로더가 완료되었는지 확인 (PhotonSyncService에서 호출)
    /// </summary>
    public bool AreAllDataLoadersReady()
    {
        if (!loadersInitialized) return false;
        if (velocityLoader == null || !(velocityLoader.IsDataLoaded || velocityReadyOverride)) return false;
        if (wssLoader == null || !(wssLoader.IsDataLoaded || wssReadyOverride)) return false;
        if (streamlineLoader == null || !streamlineLoader.IsDataLoaded) return false;
        return true;
    }

    public void LoadAndApplySettings(bool applyCurrentDataFolder = true)
    {
        // 0. Refresh available folders first to ensure validation is possible
        RefreshAvailableDataFolders();

        // 1. If initializing, determine which Data Folder to use
        if (applyCurrentDataFolder)
        {
            string targetFolder = null;

            // A. Check Network Property First (Priority: Network > Saved)
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("CurrentDataFolder", out object netFolder))
            {
                string networkFolder = (string)netFolder;
                if (availableDataFolders.Contains(networkFolder))
                {
                    targetFolder = networkFolder;
                    Debug.Log($"<color=green>[Manager] Using Network Synced Folder: {targetFolder}</color>");
                }
            }

            // B. If no network folder, use Global Settings
            if (string.IsNullOrEmpty(targetFolder))
            {
                var globalSettings = VisualizationSettingsStore.LoadSettings();
                targetFolder = globalSettings.currentDataFolder;
            }

            // C. Validate and Fallback
            if (!availableDataFolders.Contains(targetFolder))
            {
                 Debug.LogWarning($"<color=yellow>[Manager] Target folder '{targetFolder}' not found. Falling back to default.</color>");
                 if (availableDataFolders.Count > 0) 
                 {
                     targetFolder = availableDataFolders[0];
                     Debug.Log($"<color=green>[Manager] Fallback to first available folder: {targetFolder}</color>");
                 }
                 else
                 {
                     Debug.LogError("[Manager] No data folders found in StreamingAssets!");
                 }
            }
            
            this.currentDataFolder = targetFolder;
        }
        
        // 2. Load Per-Dataset Settings (prioritize folder-specific json)
        string folderPath = GetCurrentDataFolderPath();
        VisualizationSettings settings = VisualizationSettingsStore.LoadSettings(folderPath); // Falls back to global if missing

        // Apply Manager settings
        // Note: We do NOT overwrite currentDataFolder here if applyCurrentDataFolder is false
        this.bloodAlpha = settings.bloodAlpha;
        this.heatmapIntensity = settings.heatmapIntensity; // Apply heatmap intensity to Manager

        // Apply VelocityLoader settings
        if (velocityLoader != null)
        {
            Debug.Log($"<color=cyan>[Manager] Applying Velocity Settings for {currentDataFolder}: Step({settings.velocityStepX}, {settings.velocityStepY}, {settings.velocityStepZ}) Scale({settings.velocityScaleFactor})</color>");
            velocityLoader.stepX = settings.velocityStepX;
            velocityLoader.stepY = settings.velocityStepY;
            velocityLoader.stepZ = settings.velocityStepZ;
            velocityLoader.velocityScaleFactor = settings.velocityScaleFactor;
            velocityLoader.arrowScale = settings.velocityArrowScale;
            velocityLoader.displayStepX = settings.velocityDisplayStepX;
            velocityLoader.displayStepY = settings.velocityDisplayStepY;
            velocityLoader.displayStepZ = settings.velocityDisplayStepZ;
        }
        else
        {
             Debug.LogWarning("[Manager] velocityLoader is null when applying settings (First Pass?)");
        }

        // Apply LoadWSS settings
        if (wssLoader != null)
        {
            wssLoader.arrowScale = settings.wssArrowScale;
            wssLoader.arrowLengthMultiplier = settings.wssArrowLengthMultiplier;
            wssLoader.stepX = settings.wssStepX;
            wssLoader.stepY = settings.wssStepY;
            wssLoader.stepZ = settings.wssStepZ;
        }

        // Apply LoadStreamline settings
        if (streamlineLoader != null)
        {
            streamlineLoader.lineWidth = settings.streamlineLineWidth;
        }

        
        // Apply Measurement Tool settings
        if (measurementTool != null)
        {
            measurementTool.voxelSpacing = settings.voxelSpacing;
            measurementTool.calibrationFactor = settings.calibrationFactor;
            measurementTool.enableSnapToSurface = settings.defaultSnapToSurface;
            measurementTool.bloodVesselMesh = this.bloodVesselMesh;
            
            // ObjectParent 설정 및 마커 부모 재설정
            if (ObjectParent != null)
            {
                measurementTool.SetObjectParent(ObjectParent.transform);
            }
            
            Debug.Log($"<color=cyan>[Manager] Calibration factor set to: {settings.calibrationFactor}</color>");
        }
        
        Debug.Log($"Visualization settings loaded and applied for: {currentDataFolder}");
    }

    void Update()
    {
        // 1. Check for Global Lock Debugging (Added)
        if (Application.isPlaying)
        {
            if (isGlobalLocked != _internalLockedState)
            {
                SetGlobalInputLock(isGlobalLocked);
            }
        }

        // Check if all data is loaded
        if (!isDataLoaded)
        {
            bool allLoaded = true;
            if (!loadersInitialized) allLoaded = false;
            if (velocityLoader == null || !(velocityLoader.IsDataLoaded || velocityReadyOverride)) allLoaded = false;
            if (wssLoader == null || !(wssLoader.IsDataLoaded || wssReadyOverride)) allLoaded = false;
            if (streamlineLoader == null || !streamlineLoader.IsDataLoaded) allLoaded = false;

            if (allLoaded)
            {
                isDataLoaded = true;
                Debug.Log("<color=green>All data loaded successfully!</color>");
                if (progress != null) progress.SetActive(false);

                if (mainUI != null) mainUI.SetActive(true);
                if (bloodVesselMesh != null) bloodVesselMesh.SetActive(true);
            }
            else
            {
                // Ensure progress is visible while loading
                if (progress != null && !progress.activeSelf) progress.SetActive(true);
                if (mainUI != null && mainUI.activeSelf) mainUI.SetActive(false);
                if (bloodVesselMesh != null && bloodVesselMesh.activeSelf) bloodVesselMesh.SetActive(false);
            }
        }

        // Press '1' key to toggle folder selector UI
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            if (folderSelectorUI != null)
            {
                folderSelectorUI.ToggleFolderSelector();
            }
            else
            {
                // Fallback: 기존 동작 (순차 폴더 변경)
                ToggleDataFolder();
            }
        }

        // Check for visualization mode changes
        if (visualizationMode != prevVisualizationMode)
        {
            Debug.Log($"<color=magenta>Visualization mode changed: {prevVisualizationMode} -> {visualizationMode}</color>");
            prevVisualizationMode = visualizationMode;
            ApplyVisualizationMode();
        }

        // Check for WSS sub-mode changes (when in WSS mode)
        if (visualizationMode == VisualizationMode.WSS && wssSubMode != prevWSSSubMode)
        {
            Debug.Log($"<color=magenta>WSS SubMode changed: {prevWSSSubMode} -> {wssSubMode}</color>");
            ApplyWSSSubMode();
        }

        // Press '2' key to cycle WSS sub-modes
        if (Input.GetKeyDown(KeyCode.Alpha2) && visualizationMode == VisualizationMode.WSS)
        {
            CycleWSSSubMode();
        }
        
        // Press '3' key to recenter camera to current head position
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            RecenterCamera();
        }

        // Live-update blood alpha if tweaked via slider
        if (Mathf.Abs(prevBloodAlpha - bloodAlpha) > 0.0001f && bloodMaterial != null)
        {
            prevBloodAlpha = bloodAlpha;
            var renderer = bloodVesselMesh != null ? bloodVesselMesh.GetComponent<MeshRenderer>() : null;
            if (renderer != null)
            {
                MakeMaterialTransparent(renderer.material, bloodAlpha);
            }
            else
            {
                MakeMaterialTransparent(bloodMaterial, bloodAlpha);
            }
        }

        // Update playback speeds
        UpdatePlaybackSpeeds();
    }

    // --- UI helpers for playback interval sliders (seconds) ---
    public void SetVelocityPlaybackSpeed(float value)
    {
        velocityPlaybackSpeed = Mathf.Clamp(value, 0.05f, 0.5f);
        UpdatePlaybackSpeeds();
    }

    public void SetWssPlaybackSpeed(float value)
    {
        wssPlaybackSpeed = Mathf.Clamp(value, 0.05f, 0.5f);
        UpdatePlaybackSpeeds();
    }

    public void SetStreamlinePlaybackSpeed(float value)
    {
        streamlinePlaybackSpeed = Mathf.Clamp(value, 0.05f, 0.5f);
        UpdatePlaybackSpeeds();
    }

    public void SetObjectScale(float normalizedValue)
    {
        // Map 0.0 -> 0.1, 0.5 -> 1.0, 1.0 -> 5.0
        float multiplier = 1.0f;
        if (normalizedValue < 0.5f)
            multiplier = Mathf.Lerp(0.1f, 1.0f, normalizedValue * 2f);
        else
            multiplier = Mathf.Lerp(1.0f, 5.0f, (normalizedValue - 0.5f) * 2f);

        if (ObjectParent != null)
        {
            ObjectParent.transform.localScale = Vector3.one * (0.1f * multiplier);
        }

        // Save to settings
        var store = VisualizationSettingsStore.LoadSettings();
        store.objectScaleMultiplier = multiplier;
        VisualizationSettingsStore.SaveSettings(store);
    }

    public void SetObjectRotation(float normalizedValue)
    {
        // Map 0.0 -> -180, 0.5 -> 0, 1.0 -> 180
        float angle = Mathf.Lerp(-180f, 180f, normalizedValue);

        if (ObjectParent != null)
        {
            ObjectParent.transform.localRotation = Quaternion.Euler(0, angle, 0);
        }

        // Save to settings
        var store = VisualizationSettingsStore.LoadSettings();
        store.objectRotationY = angle;
        VisualizationSettingsStore.SaveSettings(store);
    }

    // Called when values are changed in Inspector (Editor mode)
    void OnValidate()
    {
        // Only run in Play mode and after initialization
        if (!Application.isPlaying || Instance == null) return;

        Debug.Log("<color=orange>OnValidate called - Inspector value changed</color>");

        // Apply mode change immediately
        if (visualizationMode != prevVisualizationMode)
        {
            Debug.Log($"<color=magenta>[OnValidate] Mode change: {prevVisualizationMode} -> {visualizationMode}</color>");
            prevVisualizationMode = visualizationMode;
            ApplyVisualizationMode();
        }
    }

    void InitializeScene()
    {                
        if (progress != null) progress.SetActive(true);
        // 1. Find or Create MixedRealitySceneContent
        GameObject sceneContent = GameObject.Find("MixedRealitySceneContent");
        if (sceneContent == null)
        {
            sceneContent = new GameObject("MixedRealitySceneContent");
            Debug.Log("Created MixedRealitySceneContent");
        }

        // 2. Find or Create ObjectParent under SceneContent
        Transform objectParentTrans = sceneContent.transform.Find(objectParentName);
        if (objectParentTrans == null)
        {
            ObjectParent = new GameObject(objectParentName);
            ObjectParent.transform.SetParent(sceneContent.transform, false);
        }
        else
        {
            ObjectParent = objectParentTrans.gameObject;
        }

        Debug.Log($"Hierarchy setup: {sceneContent.name} -> {ObjectParent.name}");
    }

    void ValidateDataFolders()
    {
        string streamingAssetsPath = Application.streamingAssetsPath;
        
        foreach (string folder in availableDataFolders)
        {
            string folderPath = Path.Combine(streamingAssetsPath, folder);
            if (Directory.Exists(folderPath))
            {
                Debug.Log($"Data folder found: {folder} at {folderPath}");
            }
            else
            {
                Debug.LogWarning($"Data folder not found: {folder} at {folderPath}");
            }
        }
        
        Debug.Log($"Current data folder: {currentDataFolder}");
    }

    // Auto-Scaling Variables
    public Vector3 CurrentDataOffset { get; private set; } = Vector3.zero;
    public float CurrentDataScale { get; private set; } = 1.0f;
    public Quaternion CurrentDataRotation { get; private set; } = Quaternion.Euler(0, 0, 180); // Fix: User requested 180 Z rotation

    public void LoadBloodVesselMesh(bool networkCall = false)
    {
        string folderPath = GetCurrentDataFolderPath();
        string objPath = Path.Combine(folderPath, "wall.obj");
        string stlPath = Path.Combine(folderPath, "wall.stl");
        
        Mesh mesh = null;

        if (File.Exists(objPath))
        {
            Debug.Log($"<color=green>Found OBJ file: {objPath} (Loading for Vertex Colors)</color>");
            string objContent = File.ReadAllText(objPath);
            mesh = ParseOBJ(objContent);
        }
        else if (File.Exists(stlPath))
        {
            Debug.Log($"<color=green>Found STL file: {stlPath} (No Vertex Colors)</color>");
            mesh = ParseSTL(stlPath);
        }
        else
        {
            Debug.LogError($"Blood vessel mesh not found (checked wall.obj and wall.stl) in {folderPath}");
            return;
        }

        // Find or create Blood Vessel obj
        Transform bloodVesselTrans = ObjectParent.transform.Find("Blood Vessel obj");
        if (bloodVesselTrans == null)
        {
            bloodVesselMesh = new GameObject("Blood Vessel obj");
            bloodVesselMesh.transform.SetParent(ObjectParent.transform, false);
        }
        else
        {
            bloodVesselMesh = bloodVesselTrans.gameObject;
        }
        
        if (mesh != null)
        {
            MeshFilter meshFilter = bloodVesselMesh.GetComponent<MeshFilter>();
            if (meshFilter == null) meshFilter = bloodVesselMesh.AddComponent<MeshFilter>();
            meshFilter.mesh = mesh;

            MeshRenderer meshRenderer = bloodVesselMesh.GetComponent<MeshRenderer>();
            if (meshRenderer == null) meshRenderer = bloodVesselMesh.AddComponent<MeshRenderer>();
            
            Debug.Log($"[LoadBloodVesselMesh] Checking material... BloodMaterial field is {(bloodMaterial == null ? "NULL" : "ASSIGNED")}");

            // Apply material
            if (bloodMaterial != null)
            {
                meshRenderer.material = bloodMaterial;
                MakeMaterialTransparent(meshRenderer.material, bloodAlpha);
                Debug.Log($"<color=green>Applied Blood Material: {bloodMaterial.name}</color>");
            }
            else
            {
                if (meshRenderer.material == null)
                {
                    meshRenderer.material = new Material(Shader.Find("Standard"));
                }
                Debug.LogWarning("<color=yellow>Blood Material is missing! Using default/existing material.</color>");
            }

            Debug.Log($"<color=green>Loaded blood vessel mesh from {currentDataFolder}: {mesh.vertexCount} vertices</color>");

            // --- Auto-Scaling & Centering Logic (Fit to BoxCollider) ---
            mesh.RecalculateBounds();
            Bounds meshBounds = mesh.bounds;
            
            // 1. Determine Target Size from ObjectParent's BoxCollider
            float targetSize = 0.25f; // Default fallback
            
            BoxCollider parentCollider = ObjectParent.GetComponent<BoxCollider>();
            if (parentCollider != null)
            {
                // Use the largest dimension of the collider as the target bounds
                targetSize = Mathf.Max(parentCollider.size.x, parentCollider.size.y, parentCollider.size.z);
                Debug.Log($"[Auto-Scale] Found BoxCollider on ObjectParent. Target Size: {targetSize}");
            }
            else
            {
                Debug.LogWarning("[Auto-Scale] ObjectParent has no BoxCollider. Using default size 0.25m");
            }
            
            // 2. Calculate Scale Factor
            float meshMaxDim = Mathf.Max(meshBounds.size.x, meshBounds.size.y, meshBounds.size.z);
            if (meshMaxDim < 0.0001f) meshMaxDim = 1.0f; 
            
            CurrentDataScale = targetSize / meshMaxDim;
            
            // 3. Calculate Center Offset (Scaled & Rotated)
            // We want (Center + Offset) * Scale = 0  => Offset = -Center
            // Transformed Point P' = T * R * S * P
            // We want Origin at P_center' = 0
            // Rotation is applied to (Scale * Center)
            // Pos = -(Rotation * (Center * Scale))
            CurrentDataRotation = Quaternion.Euler(0, 0, 180);
            CurrentDataOffset = -(CurrentDataRotation * (meshBounds.center * CurrentDataScale));
            
            Debug.Log($"[Auto-Scale] Mesh Size: {meshMaxDim}, Target: {targetSize} -> Scale: {CurrentDataScale}, Offset: {CurrentDataOffset}, Rot: {CurrentDataRotation.eulerAngles}");
            
            // 4. Apply to ObjectParent (Scale) -> Fix: User requested ObjectParent scale to be fixed at 0.1
            if (ObjectParent != null)
            {
                // Always reset scale during loading to ensure a consistent baseline across all clients
                ObjectParent.transform.localScale = Vector3.one * 0.1f; 
            }
            
            // Always apply child transform - this is local to each client and not network-synced
            bloodVesselMesh.transform.localPosition = CurrentDataOffset;
            bloodVesselMesh.transform.localRotation = CurrentDataRotation;
            bloodVesselMesh.transform.localScale = Vector3.one * CurrentDataScale;
            
            // 5. Review Metadata and Calibrate Measurement Tool
            // Fix: Pass Global Scale (Local * Parent) to calibration
            // ObjectParent scale is 0.1, so effective scale is CurrentDataScale * 0.1f
            ReviewMetadata(folderPath, CurrentDataScale);

        }
        else
        {
            Debug.LogError("Failed to parse blood vessel mesh");
        }
    }

    [System.Serializable]
    private class Metadata
    {
        public string unit;
        public string description;
    }

    void ReviewMetadata(string folderPath, float currentScaleFactor)
    {
        string metaPath = Path.Combine(folderPath, "metadata.json");
        float unitMultiplier = 10.0f; // Default: cm -> mm (1cm = 10mm)
        
        if (File.Exists(metaPath))
        {
            try
            {
                string json = File.ReadAllText(metaPath);
                Metadata meta = JsonUtility.FromJson<Metadata>(json);
                if (meta != null && !string.IsNullOrEmpty(meta.unit))
                {
                    string u = meta.unit.ToLower().Trim();
                    if (u == "cm") unitMultiplier = 10.0f;
                    else if (u == "mm") unitMultiplier = 1.0f;
                    else if (u == "m") unitMultiplier = 1000.0f;
                    
                    Debug.Log($"[Metadata] Loaded unit: {meta.unit} -> Multiplier: {unitMultiplier}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Metadata] Failed to parse metadata.json: {e.Message}");
            }
        }
        else
        {
            Debug.Log("[Metadata] No metadata.json found. Assuming 'cm' unit (10x multiplier).");
        }
        
        // Push to Calibration Tool
        if (MeasurementCalibration.Instance != null)
        {
            MeasurementCalibration.Instance.SetAutoCalibration(currentScaleFactor, unitMultiplier);
        }
        else
        {
            // Try simple find if Instance is null
            var cal = FindObjectOfType<MeasurementCalibration>();
            if (cal != null)
            {
                cal.SetAutoCalibration(currentScaleFactor, unitMultiplier);
            }
        }
    }

    public void LoadWSSMesh(bool networkCall = false)
    {
        string folderPath = GetCurrentDataFolderPath();
        string objPath = Path.Combine(folderPath, "wall.obj");
        string stlPath = Path.Combine(folderPath, "wall.stl");
        
        Mesh mesh = null;

        if (File.Exists(objPath))
        {
            Debug.Log($"<color=green>Found OBJ file: {objPath} (Loading for Vertex Colors)</color>");
            string objContent = File.ReadAllText(objPath);
            mesh = ParseOBJ(objContent);
        }
        else if (File.Exists(stlPath))
        {
            Debug.Log($"<color=green>Found STL file: {stlPath} (No Vertex Colors)</color>");
            mesh = ParseSTL(stlPath);
        }
        else
        {
            Debug.LogError($"WSS mesh not found (checked wall.obj and wall.stl) in {folderPath}");
            return;
        }

        // Find or create WSS Mesh obj
        Transform wssMeshTrans = ObjectParent.transform.Find("WSS");
        if (wssMeshTrans == null)
        {
            wssMesh = new GameObject("WSS");
            wssMesh.transform.SetParent(ObjectParent.transform, false);
        }
        else
        {
            wssMesh = wssMeshTrans.gameObject;
        }

        if (mesh != null)
        {
            MeshFilter meshFilter = wssMesh.GetComponent<MeshFilter>();
            if (meshFilter == null) meshFilter = wssMesh.AddComponent<MeshFilter>();
            meshFilter.mesh = mesh;

            MeshRenderer meshRenderer = wssMesh.GetComponent<MeshRenderer>();
            if (meshRenderer == null) meshRenderer = wssMesh.AddComponent<MeshRenderer>();
            
            // Apply material
            if (wssMaterial != null)
            {
                meshRenderer.material = wssMaterial;
                Debug.Log($"<color=green>Applied WSS Material: {wssMaterial.name}</color>");
            }
            else
            {
                if (meshRenderer.material == null)
                {
                    // Use VertexColor shader by default for WSS to show heatmap
                    Shader shader = Shader.Find("Custom/VertexColor");
                    if (shader == null) 
                    {
                        Debug.LogError("<color=red>Shader 'Custom/VertexColor' NOT FOUND! Check if the file exists in Assets/Shader/</color>");
                        shader = Shader.Find("Standard");
                    }
                    meshRenderer.material = new Material(shader);
                }
                Debug.LogWarning($"<color=yellow>WSS Material is missing! Using shader: {meshRenderer.sharedMaterial.shader.name}</color>");
            }

            Debug.Log($"<color=green>Loaded WSS mesh from {currentDataFolder}: {mesh.vertexCount} vertices</color>");
        }
        else
        {
            Debug.LogError("Failed to parse WSS mesh");
        }
    }

    public void ApplyVisualizationMode(bool networkCall = false)
    {
        Debug.Log($"<color=cyan>Applying visualization mode: {visualizationMode}</color>");

        switch (visualizationMode)
        {
            case VisualizationMode.Mesh:
                // Mesh only
                SetMeshVisibility(true);
                SetWSSMeshVisibility(false);
                SetWSSVisibility(false);
                SetVelocityVisibility(false);
                SetStreamlineVisibility(false);
                
                // Hide both color bars
                if (velocityColorBar != null) velocityColorBar.Hide();
                if (wssColorBar != null) wssColorBar.Hide();
                break;

            case VisualizationMode.Velocity:
                // Mesh + Velocity
                SetMeshVisibility(true);
                SetWSSMeshVisibility(false);
                SetWSSVisibility(false);
                SetVelocityVisibility(true);
                SetStreamlineVisibility(false);
                
                // Show color bar for velocity
                UpdateColorBarForVelocity(networkCall);
                break;

            case VisualizationMode.WSS:
                // WSS mode with sub-modes
                SetVelocityVisibility(false);
                SetStreamlineVisibility(false);
                ApplyWSSSubMode();
                
                // Show color bar for WSS
                UpdateColorBarForWSS(networkCall);
                break;

            case VisualizationMode.Streamline:
                // Mesh + Streamline
                SetMeshVisibility(true);
                SetWSSMeshVisibility(false);
                SetWSSVisibility(false);
                SetVelocityVisibility(false);
                SetStreamlineVisibility(true);
                
                // Hide both color bars
                if (velocityColorBar != null) velocityColorBar.Hide();
                if (wssColorBar != null) wssColorBar.Hide();
                break;
        }
    }

    private void UpdateColorBarForVelocity(bool networkCall = false)
    {
        // Hide WSS ColorBar
        if (wssColorBar != null) wssColorBar.Hide();
        
        if (velocityColorBar == null) return;
        
        // Get velocity data range from VelocityLoader
        if (velocityLoader != null)
        {
            // Use actual computed min/max from loaded data
            float minVelocity = velocityLoader.minVelocity;
            float maxVelocity = velocityLoader.maxVelocity;
            
            // Fallback to defaults if data not loaded yet
            if (minVelocity == float.MaxValue || maxVelocity == float.MinValue)
            {
                minVelocity = 0f;
                maxVelocity = 2.5f;
            }
            
            // Set range and unit
            velocityColorBar.SetRange(minVelocity, maxVelocity, "cm/s");
            
            // Use VelocityLoader's jet colormap, or generate if not available
            if (velocityLoader.jetColormap != null)
            {
                velocityColorBar.SetColormap(velocityLoader.jetColormap);
            }
            else
            {
                // Fallback: Generate colormap if loader hasn't created it yet
                Texture2D tempColormap = ColorMapUtility.GenerateJetColormap(256);
                velocityColorBar.SetColormap(tempColormap);
                Debug.LogWarning("[ColorBar] VelocityLoader jetColormap not ready, using temporary colormap");
            }
            
            velocityColorBar.Show(networkCall);
            Debug.Log($"<color=green>[ColorBar] Displayed Velocity ColorBar: {minVelocity:F2} - {maxVelocity:F2} cm/s</color>");
        }
    }

    private void UpdateColorBarForWSS(bool networkCall = false)
    {
        // Hide Velocity ColorBar
        if (velocityColorBar != null) velocityColorBar.Hide();
        
        if (wssColorBar == null) return;
        
        // Get WSS data range from LoadWSS
        if (wssLoader != null)
        {
            // Use actual computed min/max from loaded data
            float minWSS = wssLoader.minWss;
            float maxWSS = wssLoader.maxWss;
            
            // Fallback to defaults if data not loaded yet
            if (minWSS == float.MaxValue || maxWSS == float.MinValue)
            {
                minWSS = 0f;
                maxWSS = 50f;
            }
            
            // Set range and unit
            wssColorBar.SetRange(minWSS, maxWSS, "Pa");
            
            // Use LoadWSS's jet colormap, or generate if not available
            if (wssLoader.jetColormap != null)
            {
                wssColorBar.SetColormap(wssLoader.jetColormap);
            }
            else
            {
                // Fallback: Generate colormap if loader hasn't created it yet
                Texture2D tempColormap = ColorMapUtility.GenerateJetColormap(256);
                wssColorBar.SetColormap(tempColormap);
                Debug.LogWarning("[ColorBar] LoadWSS jetColormap not ready, using temporary colormap");
            }
            
            wssColorBar.Show(networkCall);
            Debug.Log($"<color=green>[ColorBar] Displayed WSS ColorBar: {minWSS:F2} - {maxWSS:F2} Pa</color>");
        }
    }

    /// <summary>
    /// WSS 서브모드를 순환합니다: WSSOnly → BloodVessel_Vector → WSS_Vector → WSSOnly
    /// </summary>
    public void CycleWSSSubMode()
    {
        // Only cycle if currently in WSS mode
        if (visualizationMode != VisualizationMode.WSS)
        {
            Debug.LogWarning("<color=yellow>CycleWSSSubMode: Not in WSS mode!</color>");
            return;
        }
        
        wssSubMode = (WSSSubMode)(((int)wssSubMode + 1) % 3);
        ApplyWSSSubMode();
        Debug.Log($"<color=magenta>WSS SubMode changed to: {wssSubMode}</color>");
        
        // 네트워크 동기화
        if (PhotonSyncService.Instance != null)
        {
            PhotonSyncService.Instance.BroadcastWSSSubMode((int)wssSubMode);
        }
    }

    /// <summary>
    /// 현재 WSS 서브모드에 따라 가시성을 설정합니다.
    /// </summary>
    public void ApplyWSSSubMode()
    {
        switch (wssSubMode)
        {
            case WSSSubMode.WSSOnly:
                // WSS 모델만 (기존 동작)
                SetMeshVisibility(false);
                SetWSSMeshVisibility(true);
                SetWSSVisibility(true);  // WSS 애니메이션 활성화
                SetWSSVectorVisibility(false);
                break;

            case WSSSubMode.BloodVessel_Vector:
                // BloodVesselMesh + WSS_Vector
                SetMeshVisibility(true);
                SetWSSMeshVisibility(false);
                SetWSSVisibility(true);  // WSS 애니메이션 활성화 (벡터 동기화 위해)
                // displayObject만 숨기기 (애니메이션은 유지)
                if (wssLoader != null && wssLoader.displayObject != null)
                {
                    wssLoader.displayObject.SetActive(false);
                }
                SetWSSVectorVisibility(true);
                break;

            case WSSSubMode.WSS_Vector:
                // WSS 모델 + WSS_Vector
                SetMeshVisibility(false);
                SetWSSMeshVisibility(true);
                SetWSSVisibility(true);  // WSS 애니메이션 활성화
                SetWSSVectorVisibility(true);
                break;
        }
        
        prevWSSSubMode = wssSubMode;
    }

    Mesh ParseOBJ(string content)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Color> colors = new List<Color>();
        List<int> triangles = new List<int>();
        
        using (System.IO.StringReader reader = new System.IO.StringReader(content))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string[] parts = line.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                if (parts[0] == "v")
                {
                    float x = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                    float y = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                    float z = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                    vertices.Add(new Vector3(-x, y, z));

                    if (parts.Length >= 7)
                    {
                        float r = float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture);
                        float g = float.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture);
                        float b = float.Parse(parts[6], System.Globalization.CultureInfo.InvariantCulture);
                        
                        // Auto-detect 0-255 range
                        if (r > 1.0f || g > 1.0f || b > 1.0f)
                        {
                            r /= 255.0f;
                            g /= 255.0f;
                            b /= 255.0f;
                        }
                        
                        colors.Add(new Color(r, g, b));
                    }
                }
                else if (parts[0] == "f")
                {
                    // Handle both triangles and quads
                    int vertexCount = parts.Length - 1;
                    if (vertexCount >= 3)
                    {
                        int[] faceIndices = new int[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                        {
                            faceIndices[i] = int.Parse(parts[i + 1].Split('/')[0]) - 1;
                        }

                        // Triangulate: first vertex + each edge
                        for (int i = 1; i < vertexCount - 1; i++)
                        {
                            triangles.Add(faceIndices[0]);
                            triangles.Add(faceIndices[i]);
                            triangles.Add(faceIndices[i + 1]);
                        }
                    }
                }
            }
        }

        if (vertices.Count == 0) return null;

        Mesh mesh = new Mesh();
        
        // Use 32-bit indices for large meshes
        if (vertices.Count > 65000)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }
        
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        
        if (colors.Count == vertices.Count)
        {
            mesh.colors = colors.ToArray();
        }
        
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        Debug.Log($"[ParseOBJ] Parsed {vertices.Count} vertices and {colors.Count} colors.");
        
        return mesh;
    }

    Mesh ParseSTL(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open)))
        {
            // Check if ASCII or Binary
            long length = reader.BaseStream.Length;
            if (length < 84) return null; // Too small

            reader.BaseStream.Seek(80, SeekOrigin.Begin);
            uint triangleCount = reader.ReadUInt32();
            
            // Expected size for binary: 80 header + 4 count + 50 * count
            if (length == 84 + triangleCount * 50)
            {
                // Binary STL
                Debug.Log($"<color=cyan>Parsing Binary STL: {triangleCount} triangles</color>");
                
                for (int i = 0; i < triangleCount; i++)
                {
                    // Skip normal (12 bytes)
                    reader.ReadBytes(12);
                    
                    // Read vertices
                    for (int j = 0; j < 3; j++)
                    {
                        float x = reader.ReadSingle();
                        float y = reader.ReadSingle();
                        float z = reader.ReadSingle();
                        // Coordinate conversion: -x, y, z to match OBJ loader
                        vertices.Add(new Vector3(-x, y, z));
                    }
                    
                    // Skip attribute (2 bytes)
                    reader.ReadUInt16();
                    
                    // Add triangle indices
                    triangles.Add(i * 3);
                    triangles.Add(i * 3 + 1);
                    triangles.Add(i * 3 + 2);
                }
            }
            else
            {
                // ASCII STL
                Debug.Log("<color=cyan>Parsing ASCII STL</color>");
                reader.BaseStream.Seek(0, SeekOrigin.Begin);
                string content = File.ReadAllText(filePath);
                
                string[] lines = content.Split(new char[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
                int vertIndex = 0;
                
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("vertex"))
                    {
                        string[] parts = trimmed.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            float x = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                            float y = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                            float z = float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                            vertices.Add(new Vector3(-x, y, z));
                            triangles.Add(vertIndex++);
                        }
                    }
                }
            }
        }

        if (vertices.Count == 0) return null;

        Mesh mesh = new Mesh();
        if (vertices.Count > 65000)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        return mesh;
    }

    IEnumerator InitializeLoadersCoroutine()
    {
        Debug.Log("<color=cyan>[Init] InitializeLoadersCoroutine start</color>");
        // Ensure loading UI visible
        if (progress != null) progress.SetActive(true);
        UpdateProgressUI(0f, "Starting");

        // Check for NetworkManager
        NetworkManager netManager = FindObjectOfType<NetworkManager>();
        if (netManager == null)
        {
            GameObject netObj = new GameObject("NetworkManager");
            netManager = netObj.AddComponent<NetworkManager>();
            Debug.Log("<color=cyan>Auto-created NetworkManager</color>");
        }
        else
        {
            Debug.Log("<color=cyan>Found existing NetworkManager</color>");
        }

        yield return null;

        // Check for PhotonSyncService
        if (PhotonSyncService.Instance == null)
        {
            PhotonSyncService syncService = FindObjectOfType<PhotonSyncService>();
            if (syncService == null)
            {
                GameObject syncObj = new GameObject("PhotonSyncService");
                syncService = syncObj.AddComponent<PhotonSyncService>();
                Debug.Log("<color=cyan>[Init] Auto-created PhotonSyncService</color>");
            }
        }


        yield return null;
        // Find or create loaders
        if (velocityLoader == null)
        {
            velocityLoader = FindObjectOfType<VelocityLoader>();
            if (velocityLoader == null)
            {
                GameObject loaderObj = new GameObject("VelocityLoader");
                velocityLoader = loaderObj.AddComponent<VelocityLoader>();
            }
        }

        yield return null;
        
        if (wssLoader == null)
        {
            wssLoader = FindObjectOfType<LoadWSS>();
        }

        if (streamlineLoader == null)
        {
            streamlineLoader = FindObjectOfType<LoadStreamline>();
        }
        
        yield return null;

        LoadAndApplySettings();
        
        // Enforce initial visibility state based on current mode (defaults to Mesh, so others hidden)
        ApplyVisualizationMode();

        if (progress != null) progress.SetActive(true);
        if (mainUI != null) mainUI.SetActive(false);
        if (bloodVesselMesh != null) bloodVesselMesh.SetActive(false);

        // Start all loaders in parallel
        Coroutine velocityCoroutine = null;
        Coroutine wssCoroutine = null;
        Coroutine streamlineCoroutine = null;
        
        if (velocityLoader != null)
        {
            Debug.Log("Initializing VelocityLoader... (parallel)");
            velocityCoroutine = StartCoroutine(InitializeVelocityLoaderWithProgress());
        }

        if (wssLoader != null)
        {
            Debug.Log("Initializing WSSLoader... (parallel)");
            wssCoroutine = StartCoroutine(InitializeWssLoaderWithProgress());
        }

        if (streamlineLoader != null)
        {
            Debug.Log("Initializing StreamlineLoader... (parallel)");
            streamlineCoroutine = StartCoroutine(streamlineLoader.initialization());
        }
        
        // Wait for all loaders to complete
        if (velocityCoroutine != null) yield return velocityCoroutine;
        if (wssCoroutine != null) yield return wssCoroutine;
        if (streamlineCoroutine != null) yield return streamlineCoroutine;
        
        Debug.Log("<color=cyan>All Loaders Initialized (parallel)</color>");
        loadersInitialized = true;
    }

    IEnumerator InitializeVelocityLoaderWithProgress()
    {
        yield return StartCoroutine(velocityLoader.initialization());

        // Track load progress until done
        float start = Time.realtimeSinceStartup;
        float timeout = 30f;
        while (!velocityLoader.IsDataLoaded)
        {
            UpdateProgressUI(velocityLoader.loadProgress, $"Velocity {velocityLoader.loadStage}");
            if (Time.realtimeSinceStartup - start > timeout)
            {
                Debug.LogWarning("<color=yellow>VelocityLoader init wait timed out; proceeding.</color>");
                velocityReadyOverride = true;
                break;
            }
            yield return null;
        }

        UpdateProgressUI(1f, "Velocity done");

    }

    IEnumerator InitializeWssLoaderWithProgress()
    {
        yield return StartCoroutine(wssLoader.initialization());

        float start = Time.realtimeSinceStartup;
        float timeout = 30f;
        while (!wssLoader.IsDataLoaded)
        {
            UpdateProgressUI(wssLoader.loadProgress, $"WSS {wssLoader.loadStage}");
            if (Time.realtimeSinceStartup - start > timeout)
            {
                Debug.LogWarning("<color=yellow>WSSLoader init wait timed out; proceeding.</color>");
                wssReadyOverride = true;
                break;
            }
            yield return null;
        }

        UpdateProgressUI(1f, "WSS done");

    }

    void UpdateProgressUI(float value, string stage)
    {
        if (progressSlider != null)
        {
            progressSlider.value = Mathf.Clamp01(value);
        }

        if (progressText != null)
        {
            progressText.text = $"{Mathf.RoundToInt(Mathf.Clamp01(value) * 100f)}% - {stage}";
        }
    }

    void UpdatePlaybackSpeeds()
    {
        if (velocityLoader != null)
        {
            velocityLoader.frameInterval = velocityPlaybackSpeed;
        }

        if (wssLoader != null)
        {
            wssLoader.animationInterval = wssPlaybackSpeed;
        }

        if (streamlineLoader != null)
        {
            streamlineLoader.animationInterval = streamlinePlaybackSpeed;
        }

        // VisualizationSettingsStore.SavePlaybackSpeeds(velocityPlaybackSpeed, wssPlaybackSpeed, streamlinePlaybackSpeed); // Removed in refactor
    }

    // ===== Visibility Control Methods =====

    public void SetMeshVisibility(bool visible)
    {
        if (bloodVesselMesh != null)
        {
            bloodVesselMesh.SetActive(visible);
            Debug.Log($"<color=yellow>Mesh visibility set to: {visible}</color>");
        }
        else
        {
            Debug.LogWarning("<color=red>Blood vessel mesh is null! Cannot set visibility.</color>");
        }
    }

    public void SetVelocityVisibility(bool visible)
    {
        if (velocityLoader != null)
        {
            // Control the velocity parent object instead of the loader GameObject
            if (velocityLoader.velocityParent != null)
            {
                velocityLoader.velocityParent.SetActive(visible);
                Debug.Log($"<color=yellow>Velocity visibility set to: {visible}</color>");
            }
            else
            {
                Debug.LogWarning("<color=red>Velocity parent is null!</color>");
            }
        }
        else
        {
            Debug.LogWarning("<color=red>VelocityLoader is null! Cannot set visibility.</color>");
        }
    }

    public void SetWSSMeshVisibility(bool visible)
    {
        if (wssMesh != null)
        {
            wssMesh.SetActive(visible);
            Debug.Log($"<color=yellow>WSS Mesh visibility set to: {visible}</color>");
        }
        else
        {
            Debug.LogWarning("<color=red>WSS mesh is null! Cannot set visibility.</color>");
        }
    }

    public void SetWSSVisibility(bool visible)
    {
        if (wssLoader != null)
        {
            wssLoader.isActivated = visible;
            
            // Force update the activation state
            if (wssLoader.displayObject != null)
            {
                wssLoader.displayObject.SetActive(visible);
                Debug.Log($"<color=yellow>WSS visibility set to: {visible}</color>");
            }
            else
            {
                Debug.LogWarning("<color=red>WSS displayObject is null!</color>");
            }
        }
        else
        {
            Debug.LogWarning("<color=red>WSSLoader is null! Cannot set visibility.</color>");
        }
    }

    public void SetStreamlineVisibility(bool visible)
    {
        if (streamlineLoader != null)
        {
            streamlineLoader.isActivated = visible;
            
            // Force update the activation state
            if (streamlineLoader.displayObject != null)
            {
                streamlineLoader.displayObject.SetActive(visible);
                Debug.Log($"<color=yellow>Streamline visibility set to: {visible}</color>");
            }
            else
            {
                Debug.LogWarning("<color=red>Streamline displayObject is null!</color>");
            }
        }
        else
        {
            Debug.LogWarning("<color=red>StreamlineLoader is null! Cannot set visibility.</color>");
        }
    }

    // ===== Playback Control Methods =====

    public void PlayVelocity()
    {
        if (velocityLoader != null)
            velocityLoader.StartPlayback();
    }

    public void PauseVelocity()
    {
        if (velocityLoader != null)
            velocityLoader.StopPlayback();
    }

    public void ToggleVelocityPlayback()
    {
        if (velocityLoader != null)
            velocityLoader.TogglePlayback();
    }

    public void ToggleWSSPlayback()
    {
        if (wssLoader != null)
            wssLoader.ToggleAnimation();
    }

    public void ToggleStreamlinePlayback()
    {
        if (streamlineLoader != null)
            streamlineLoader.ToggleAnimation();
    }

    // ===== Data Folder Management =====

    public void ToggleDataFolder()
    {
        RefreshAvailableDataFolders();

        if (availableDataFolders.Count == 0)
        {
            Debug.LogError("No data folders available!");
            return;
        }

        currentDataFolderIndex = (currentDataFolderIndex + 1) % availableDataFolders.Count;
        string newFolder = availableDataFolders[currentDataFolderIndex];
        
        // Photon 브로드캐스트 - 다른 클라이언트에게 데이터 폴더 변경 알림
        if (PhotonSyncService.Instance != null)
        {
            PhotonSyncService.Instance.BroadcastDataFolder(newFolder);
        }
        
        StartCoroutine(ChangeDataFolderCoroutine(newFolder));
    }

    void RefreshAvailableDataFolders()
    {
        availableDataFolders.Clear();
        string streamingAssetsPath = Application.streamingAssetsPath;

        if (Directory.Exists(streamingAssetsPath))
        {
            string[] directories = Directory.GetDirectories(streamingAssetsPath);
            foreach (string dir in directories)
            {
                string folderName = new DirectoryInfo(dir).Name;
                // You can add filtering here if needed, e.g., only folders starting with "data"
                if (!folderName.StartsWith(".")) // Skip hidden folders to be safe
                {
                    availableDataFolders.Add(folderName);
                }
            }
        }
        else
        {
            Debug.LogError($"StreamingAssets path not found: {streamingAssetsPath}");
        }

        Debug.Log($"Reloaded available data folders: {string.Join(", ", availableDataFolders)}");
        
        // Ensure index is valid after refresh
        if (availableDataFolders.Count > 0)
        {
            // Try to find current folder in new list to keep index valid
            int index = availableDataFolders.IndexOf(currentDataFolder);
            if (index != -1)
            {
                currentDataFolderIndex = index;
            }
            else
            {
                currentDataFolderIndex = 0;
            }
        }
    }

    

    public IEnumerator ChangeDataFolderCoroutine(string folderName, bool networkCall = false)
    {
        VisualizationMode previousVisualizationMode = visualizationMode;
        WSSSubMode previousWSSSubMode = wssSubMode;
        bool restoreVisualizationAfterLoad = ShouldRestoreVisualizationAfterDataFolderChange();

        // Suppress network sync during loading to prevent clashing with initialization transforms
        if (PhotonSyncService.Instance != null)
        {
            PhotonSyncService.Instance.SuppressSyncForDuration(10.0f);
        }

        // 데이터 폴더 변경 시 상태 초기화 (애니메이션, 측정 도구, 버튼 상태 등)
        if (ButtonControllerManager.Instance != null)
        {
            ButtonControllerManager.Instance.ResetAllStatesForNewData();
        }
        else
        {
            // Fallback if Instance is not set (e.g., during early init)
            var bm = FindObjectOfType<ButtonControllerManager>();
            if (bm != null) bm.ResetAllStatesForNewData();
        }
        
        // Ensure the folder list is up-to-date before validation
        RefreshAvailableDataFolders();
        
        if (!availableDataFolders.Contains(folderName))
        {
            Debug.LogError($"Data folder '{folderName}' is not in the available folders list!");
            yield break;
        }

        string streamingAssetsPath = Application.streamingAssetsPath;
        string folderPath = Path.Combine(streamingAssetsPath, folderName);

        if (!Directory.Exists(folderPath))
        {
            Debug.LogError($"Data folder does not exist: {folderPath}");
            yield break;
        }

        currentDataFolder = folderName;
        currentDataFolderIndex = availableDataFolders.IndexOf(folderName);
        
        Debug.Log($"<color=green>Data folder changed to: {currentDataFolder}</color>");

        yield return StartCoroutine(OnDataFolderChangedCoroutine(
            networkCall,
            restoreVisualizationAfterLoad,
            previousVisualizationMode,
            previousWSSSubMode));
    }

    public string GetCurrentDataFolderPath()
    {
        return Path.Combine(Application.streamingAssetsPath, currentDataFolder);
    }

    private bool ShouldRestoreVisualizationAfterDataFolderChange()
    {
        ButtonControllerManager buttonManager = ButtonControllerManager.Instance ?? FindObjectOfType<ButtonControllerManager>();
        return buttonManager == null || !buttonManager.IsExhibitionModeActive();
    }

    protected virtual IEnumerator OnDataFolderChangedCoroutine(
        bool networkCall = false,
        bool restoreVisualizationAfterLoad = false,
        VisualizationMode restoreVisualizationMode = VisualizationMode.Mesh,
        WSSSubMode restoreWSSSubMode = WSSSubMode.WSSOnly)
    {
        Debug.Log($"<color=cyan>OnDataFolderChanged: Reloading visualizations for mode {visualizationMode}...</color>");


        loadersInitialized = false;
        yield return null; // Allow UI to update

        visualizationMode = VisualizationMode.Mesh;
        ApplyVisualizationMode(networkCall);


        if (progress != null) progress.SetActive(true);
        if (mainUI != null) mainUI.SetActive(false);
        if (bloodVesselMesh != null) bloodVesselMesh.SetActive(false);

        yield return null; // Allow UI to update

        loadersInitialized = false;
        
        // Reload settings for the new data folder
        LoadAndApplySettings(false);

        // 1. Reload Meshes FIRST (Calculates Bounds, Scale, and Offset)
        LoadBloodVesselMesh(networkCall);
        LoadWSSMesh(networkCall);
        
        yield return null;

        // 2. Initialize Loaders (They will use the calculated Offset)
        // Initialize VelocityLoader
        if (velocityLoader != null)
        {
            Debug.Log("Initializing VelocityLoader...");
            yield return StartCoroutine(InitializeVelocityLoaderWithProgress());
        }

        // Initialize LoadWSS
        if (wssLoader != null)
        {
            Debug.Log("Initializing WSSLoader...");
            yield return StartCoroutine(InitializeWssLoaderWithProgress());
        }

        // Initialize LoadStreamline
        if (streamlineLoader != null)
        {
            Debug.Log("Initializing StreamlineLoader...");
            yield return StartCoroutine(streamlineLoader.initialization());
        }
        
        Debug.Log("<color=cyan>All Loaders Initialized</color>");
        loadersInitialized = true;

        if (wssLoader != null)
        {
            wssLoader.rootFolder = currentDataFolder;
        }

        if (streamlineLoader != null)
        {
            streamlineLoader.rootFolder = currentDataFolder;
        }
        
        
        // Reload additionalRotation for SliceViewRenderer when data folder changes
        SliceViewRenderer sliceRenderer = null;
        
        // Try to get SliceViewRenderer through ButtonControllerManager first
        var buttonManager = ButtonControllerManager.Instance ?? FindObjectOfType<ButtonControllerManager>();
        if (buttonManager != null && buttonManager.sliceVisualization != null)
        {
            sliceRenderer = buttonManager.sliceVisualization.viewRenderer;
            Debug.Log($"<color=cyan>[Manager] Found SliceViewRenderer via ButtonControllerManager</color>");
        }
        
        // Fallback to FindObjectOfType if not found
        if (sliceRenderer == null)
        {
            sliceRenderer = FindObjectOfType<SliceViewRenderer>();
            if (sliceRenderer != null)
            {
                Debug.Log($"<color=cyan>[Manager] Found SliceViewRenderer via FindObjectOfType</color>");
            }
        }
        
        if (sliceRenderer != null)
        {
            Debug.Log($"<color=yellow>[Manager] Calling LoadRotationFromJSON() for folder: {currentDataFolder}</color>");
            sliceRenderer.LoadRotationFromJSON();
            Debug.Log("<color=green>[Manager] ✓ Reloaded SliceViewRenderer settings for new data folder</color>");
        }
        else
        {
            Debug.LogWarning("<color=red>[Manager] SliceViewRenderer NOT FOUND! JSON settings will not be reloaded.</color>");
        }

        if (progress != null) progress.SetActive(false);
        if (mainUI != null) mainUI.SetActive(true);
        if (bloodVesselMesh != null) bloodVesselMesh.SetActive(true);

        if (restoreVisualizationAfterLoad)
        {
            visualizationMode = restoreVisualizationMode;
            wssSubMode = restoreWSSSubMode;
            ApplyVisualizationMode(networkCall);
        }
    } // End of OnDataFolderChangedCoroutine

    // Keep legacy method for backwards compatibility (if called directly)
    protected virtual void OnDataFolderChanged()
    {
        StartCoroutine(OnDataFolderChangedCoroutine());
    }

    // ===== Utility Methods =====

    public GameObject CreatePrimitive(string type, Vector3 position)
    {
        PrimitiveType primitiveType;
        switch (type.ToLower())
        {
            case "cube": primitiveType = PrimitiveType.Cube; break;
            case "sphere": primitiveType = PrimitiveType.Sphere; break;
            case "capsule": primitiveType = PrimitiveType.Capsule; break;
            case "cylinder": primitiveType = PrimitiveType.Cylinder; break;
            case "plane": primitiveType = PrimitiveType.Plane; break;
            default:
                Debug.LogWarning($"Unknown primitive type: {type}");
                return null;
        }

        GameObject obj = GameObject.CreatePrimitive(primitiveType);
        obj.transform.position = position;
        obj.name = $"{type}_{Time.frameCount}";
        
        return obj;
    }

    public void SetObjectColor(string name, Color color)
    {
        GameObject obj = GameObject.Find(name);
        if (obj != null)
        {
            var renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
            }
        }
        else
        {
            Debug.LogWarning($"Object not found: {name}");
        }
    }

    public void DestroyObject(string name)
    {
        GameObject obj = GameObject.Find(name);
        if (obj != null)
        {
            Destroy(obj);
        }
        else
        {
            Debug.LogWarning($"Object not found: {name}");
        }
    }

    void MakeMaterialTransparent(Material mat, float alpha = 0.35f)
    {
        if (mat == null) return;

        // Ensure URP Lit shader
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit != null)
        {
            mat.shader = urpLit;
        }
        else
        {
            Debug.LogWarning("[Material] Universal Render Pipeline/Lit shader not found; keeping current material shader.");
        }

        // Set to transparent surface if supported
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // 1 = Transparent
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);

        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_SURFACE_TYPE_OPAQUE");
        mat.renderQueue = (int)RenderQueue.Transparent;

        // Apply alpha to common color properties
        if (mat.HasProperty("_BaseColor"))
        {
            Color c = mat.GetColor("_BaseColor");
            c.a = alpha;
            mat.SetColor("_BaseColor", c);
        }
        else if (mat.HasProperty("_Color"))
        {
            Color c = mat.GetColor("_Color");
            c.a = alpha;
            mat.SetColor("_Color", c);
        }

        Debug.Log($"[Material] Transparent material configured: {mat.name}, shader={mat.shader?.name ?? "(none)"}, supported={(mat.shader != null && mat.shader.isSupported)}, alpha={alpha}");
    }

    // WSS Vector Control
    public void SetWSSVectorVisibility(bool visible)
    {
        if (wssLoader != null)
        {
            wssLoader.SetWSSVectorVisibility(visible);
            Debug.Log($"<color=cyan>WSS Vectors: {(visible ? "ON" : "OFF")}</color>");
        }
    }


    // ==================== Global Interaction Events ====================
    // Unity Inspector에서 ObjectManipulator 이벤트(OnManipulationStarted/Ended 등)에 연결

    public void OnInteractionStarted()
    {
        Debug.Log("<color=cyan>[Manager] OnInteractionStarted CALLED</color>");

        if (PhotonSyncService.Instance != null)
        {
            // Lock 요청
            Debug.Log("[Manager] Calling RequestGlobalLock...");
            // LockType.ObjectManipulation 등의 상수가 없다면 기본값이나 정수로 처리 (여기서는 기존 코드 참고)
            bool lockAcquired = PhotonSyncService.Instance.RequestGlobalLock(PhotonSyncService.LockType.ObjectManipulation);
            Debug.Log($"[Manager] RequestGlobalLock returned: {lockAcquired}");
        }
    }

    public void OnInteractionEnded()
    {
        Debug.Log("<color=cyan>[Manager] OnInteractionEnded CALLED</color>");

        if (PhotonSyncService.Instance != null)
        {
            // Lock 해제 요청
            PhotonSyncService.Instance.ReleaseGlobalLock();
            Debug.Log("<color=green>[Manager] Interaction End - Lock Released</color>");

        }
    }

    // ==================== Global Input Lock ====================
    
    [Header("Global Lock Debug")]
    public bool isGlobalLocked = false; // Inspector에서 확인 및 제어 가능
    private bool _internalLockedState = false; // 내부 실제 상태 추적용

    /// <summary>
    /// 모든 인터랙션(오브젝트 조작, UI 버튼, 슬라이더 등)을 잠그거나 풉니다.
    /// 멀티플레이어 환경에서 다른 사용자가 조작 중일 때 사용합니다.
    /// </summary>
    /// <param name="locked">true면 조작 불가(잠금), false면 조작 가능(해제)</param>
    public void SetGlobalInputLock(bool locked)
    {
        // 내부 상태 업데이트
        _internalLockedState = locked;
        isGlobalLocked = locked; // Inspector 변수도 동기화

        Debug.Log($"<color=magenta>[Manager] SetGlobalInputLock: {locked.ToString().ToUpper()}</color>");

        // 0. MRTK Hand Interaction 막기 (직접 Pointer 비활성화 - 컴파일 에러 회피 및 안전한 방식)
        ToggleHandPointers(!locked);
        
    }

    public void ToggleHandPointers(bool isActive)
    {
        // isActive가 true면 기본 동작(Default), false면 항상 끔(AlwaysOff)
        PointerBehavior behavior = isActive ? PointerBehavior.Default : PointerBehavior.AlwaysOff;

        // 1. Hand Ray (원거리 포인터)
        PointerUtils.SetHandRayPointerBehavior(behavior);
        
        // 2. Hand Poke (근거리 터치)
        PointerUtils.SetHandPokePointerBehavior(behavior);
        
        // 3. Hand Grab (근거리 잡기)
        PointerUtils.SetHandGrabPointerBehavior(behavior);

        Debug.Log($"<color=cyan>[Manager] Hand Pointers set to: {behavior}</color>");

        if (isActive){
            progress.SetActive(false);
        }else{
            progress.SetActive(true);
        }
    }
    
    /// <summary>
    /// 카메라/월드 좌표를 현재 머리 위치에 맞게 재설정합니다.
    /// 키보드 '3'번 키로 호출됩니다.
    /// </summary>
    public void RecenterCamera()
    {
        Debug.Log("<color=cyan>[Manager] RecenterCamera called</color>");
        
        // 방법 2: MRTK Playspace 이동 (XR Subsystem 없는 경우 폴백)
        Transform playspace = MixedRealityPlayspace.Transform;
        Camera mainCam = Camera.main;
        if (playspace != null && mainCam != null)
        {
            // 카메라의 현재 XZ 위치를 원점으로 이동 (Y축은 유지)
            Vector3 offset = mainCam.transform.position;
            //offset.y = 0;
            playspace.position -= offset;
            
            // 카메라가 바라보는 방향을 정면(Z+)으로 설정
            float yRotation = mainCam.transform.eulerAngles.y;
            playspace.Rotate(0, -yRotation, 0, Space.World);
        }

        playspace = MixedRealityPlayspace.Transform;
        mainCam = Camera.main;
        if (playspace != null && mainCam != null)
        {
            // 카메라의 현재 XZ 위치를 원점으로 이동 (Y축은 유지)
            Vector3 offset = mainCam.transform.position;
            //offset.y = 0;
            playspace.position -= offset;
            
            // 카메라가 바라보는 방향을 정면(Z+)으로 설정
            float yRotation = mainCam.transform.eulerAngles.y;
            playspace.Rotate(0, -yRotation, 0, Space.World);
            
            Debug.Log($"<color=green>[Manager] Camera recentered via MRTK Playspace. Offset: {offset}, YRotation: {yRotation}</color>");
        }
        else
        {
            Debug.LogWarning("<color=yellow>[Manager] RecenterCamera failed: No XR subsystem or MRTK Playspace found</color>");
        }
    }
}
