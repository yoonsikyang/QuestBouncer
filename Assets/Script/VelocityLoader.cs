using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using UnityEngine;

/// <summary>
/// Data structure for velocity information
/// </summary>
[System.Serializable]
public class VelocityData
{
    public List<Vector3> positions = new List<Vector3>();
    public List<Vector3> originalPositions = new List<Vector3>();
    public List<Vector3Int> gridIndices = new List<Vector3Int>();
    public List<Vector3> velocities = new List<Vector3>();
    public List<Vector3> originalVelocities = new List<Vector3>(); // Store original velocities for real-time inversion
    public List<float> velocityMagnitudes = new List<float>();
    public List<Color> colors = new List<Color>();
}

/// <summary>
/// Loads velocity data files sequentially with time intervals
/// Optimized with Parallel Loading and 3D Downsampling
/// </summary>
public class VelocityLoader : MonoBehaviour
{
    [Header("Coordinate Transform Testing")]
    [Tooltip("Test different velocity transformations. Position uses (-y, x, z)")]
    public VelocityTransformMode velocityTransformMode = VelocityTransformMode.NoTransform;
    
    public enum VelocityTransformMode
    {
        NoTransform,        // (vx, vy, vz)
        MatchPosition,      // (-vy, vx, vz) - same transform as position
        NegateX,            // (-vx, vy, vz)
        NegateY,            // (vx, -vy, vz)
        NegateZ,            // (vx, vy, -vz)
        NegateXY,           // (-vx, -vy, vz)
        NegateXZ,           // (-vx, vy, -vz)
        NegateYZ,           // (vx, -vy, -vz)
        NegateAll,          // (-vx, -vy, -vz)
        SwapXY,             // (vy, vx, vz)
        SwapXYNegateX,      // (-vy, vx, vz) - SAME AS MatchPosition
        SwapXYNegateY,      // (vy, -vx, vz)
        SwapXYNegateBoth    // (-vy, -vx, vz)
    }
    
    [Header("Data Settings")]
    public string dataSubFolder = "velocity";
    public string filePrefix = "vel_norm_";
    public string fileExtension = ".txt";
    
    [Header("Normalization")]
    public bool fitToMeshBounds = true;
    public bool keepAspectRatio = true;
    public Vector3 userScale = Vector3.one;

    private Vector3 dataMin;
    private Vector3 dataMax;
    private Vector3 targetMin;
    private Vector3 targetMax;

    
    [Header("Optimization (Downsampling)")]
    [Tooltip("Step size for X axis (Inner loop)")]
    [Range(1, 20)] public int stepX = 2;
    [Tooltip("Step size for Y axis (Middle loop)")]
    [Range(1, 20)] public int stepY = 2;
    [Tooltip("Step size for Z axis (Outer loop)")]
    [Range(1, 20)] public int stepZ = 1;

    [Header("Visualization Settings")]
    public float arrowScale = 0.02f;
    public float velocityScaleFactor = 0.05f; // Reduced from 0.1 to make arrows shorter
    public GameObject arrowPrefab;

    public enum ArrowBaseDirection
    {
        Forward,
        Back,
        Up,
        Down,
        Right,
        Left
    }
    
    [Header("Arrow Direction Settings")]
    public ArrowBaseDirection arrowBaseDirection = ArrowBaseDirection.Forward;
    
    [Tooltip("Additional rotation to apply to all arrows (e.g., -90 on X axis)")]
    public Vector3 additionalRotation = new Vector3(-90, 0, 0);
    
    [Header("Debug")]
    [Tooltip("Show raw velocity data for first 10 arrows")]
    public bool debugShowRawData = false;

    private ArrowBaseDirection prevArrowBaseDirection = ArrowBaseDirection.Forward;
    
    [System.Serializable]
    private class VisualizationSettings
    {
        public RotationVector additionalRotation;
    }
    
    [System.Serializable]
    private class RotationVector
    {
        public float x;
        public float y;
        public float z;
    }
    private Quaternion arrowBaseCorrection = Quaternion.identity;

    [Header("Playback Settings")]
    public float frameInterval = 0.1f;
    public bool loop = true;
    public bool autoPlay = true;

    [Header("Coordinate Mapping")]
    public bool autoDetectBounds = true;
    // (Duplicates removed: dataMin, dataMax, targetMin, targetMax, fitToMeshBounds)
    public bool matchMeshTransform = false; // keep parent transform fixed; rely on bounds mapping
    [Header("Data Rotation (applied at parse time)")]
    public Vector3 dataParseRotation = Vector3.zero; // no rotation by default

    [Header("Density Control")]
    [Range(1, 10)] public int displayStepX = 1;
    [Range(1, 10)] public int displayStepY = 1;
    [Range(1, 10)] public int displayStepZ = 1;
    
    [Header("GPU Instancing (Performance)")]
    [Tooltip("Use GPU Instancing for arrow rendering (faster, but no individual object interaction)")]
    public bool useGPUInstancing = false;
    public Material instancedMaterial; // Must have GPU Instancing enabled
    public Mesh arrowMesh; // Arrow mesh for instancing
    private Matrix4x4[] instanceMatrices;
    private Vector4[] instanceColors;
    private MaterialPropertyBlock instanceMPB;
    private const int MAX_INSTANCES_PER_BATCH = 1023; // Unity limit

    [Header("visionOS RealityKit Budget")]
    [Tooltip("Caps pooled arrow GameObjects on visionOS RealityKit to avoid PolySpatial SynchronizationComponent limits.")]
    public int visionOSMaxArrowObjects = 1500;
    
    [Header("ComputeBuffer Mode (Advanced)")]
    [Tooltip("Use ComputeShader + DrawMeshInstancedIndirect for maximum performance (requires GPU Instancing enabled)")]
    public bool useComputeBuffer = false;
    public ComputeShader arrowComputeShader;
    public Material indirectMaterial; // InstancedColorIndirect shader
    
    // Cached buffers per frame
    private class FrameBufferData
    {
        public ComputeBuffer positions;
        public ComputeBuffer velocities;
        public ComputeBuffer magnitudes;
        public ComputeBuffer colors;
        public ComputeBuffer gridIndices;
        public int count;
        
        public void Release()
        {
            positions?.Release();
            velocities?.Release();
            magnitudes?.Release();
            colors?.Release();
            gridIndices?.Release();
        }
    }
    private Dictionary<int, FrameBufferData> frameBuffersCache = new Dictionary<int, FrameBufferData>();

    private static bool IsVisionOSRealityKitRuntime
    {
        get
        {
#if UNITY_VISIONOS && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }
    
    // Shared output buffers (reused across frames)
    private ComputeBuffer matricesBuffer;
    private ComputeBuffer outputColorsBuffer;
    private ComputeBuffer argsBuffer;
    private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    private int computeKernel = -1;
    private int maxBufferSize = 0;
    private bool computeBuffersInitialized = false;
    private int lastComputeBufferFrame = -1;

    [HideInInspector]
    [System.NonSerialized] // Prevents massive serialization overhead in Inspector
    public List<VelocityData> loadedFrames = new List<VelocityData>();
    [HideInInspector]
    public GameObject velocityParent;
    private bool isPlaying = false;
    public int currentFrameIndex = 0; // Current frame being displayed
    private int totalFrames = 0;
    
    public Texture2D jetColormap;
    [HideInInspector] private List<GameObject> objectPool = new List<GameObject>();
    [HideInInspector] private List<List<Renderer>> arrowRenderers = new List<List<Renderer>>();
    private MaterialPropertyBlock mpb;
    private bool isPoolInitialized = false;
    private bool hasSeededExistingPool = false;
    
    public float minVelocity = float.MaxValue;
    public float maxVelocity = float.MinValue;

    private bool isLoading = false;
    public bool IsDataLoaded { get; private set; } = false;
    public float loadProgress = 0f; // 0~1 for UI
    public string loadStage = "idle";

    private bool prevKeepAspectRatio;
    private Vector3 prevUserScale;
    private int prevDisplayStepX;
    private int prevDisplayStepY;
    private int prevDisplayStepZ;
    private float prevArrowScale;
    private float prevVelocityScaleFactor;
    private Coroutine playbackRoutine;
    private bool wasVelocityActive = true;
    private SliceController sliceController;

    // Frame Control 지원
    public int TotalFrameCount => loadedFrames.Count;

    /// <summary>
    /// 애니메이션 일시정지 (프레임 컨트롤 모드용)
    /// </summary>
    public void PauseAnimation()
    {
        if (isPlaying)
        {
            StopPlayback();
            Debug.Log("<color=yellow>[VelocityLoader] Animation paused</color>");
        }
    }

    /// <summary>
    /// 애니메이션 재개
    /// </summary>
    public void ResumeAnimation()
    {
        if (!isPlaying && IsVelocityActive() && loadedFrames.Count > 0)
        {
            StartPlayback();
            Debug.Log("<color=green>[VelocityLoader] Animation resumed</color>");
        }
    }

    // --- UI helpers for density control ---
    public void SetDisplayStepX(int value)
    {
        displayStepX = Mathf.Clamp(value, 1, 10);
        prevDisplayStepX = displayStepX;
    }

    public void SetDisplayStepY(int value)
    {
        displayStepY = Mathf.Clamp(value, 1, 10);
        prevDisplayStepY = displayStepY;
    }

    public void SetDisplayStepZ(int value)
    {
        displayStepZ = Mathf.Clamp(value, 1, 10);
        prevDisplayStepZ = displayStepZ;
    }

    private void LoadRotationFromJSON()
    {
        string dataFolderPath = GetCurrentDataFolderPath();
        // Go up one level from velocity folder to get to data folder root
        string parentFolder = Directory.GetParent(dataFolderPath).FullName;
        string settingsPath = Path.Combine(parentFolder, "visualization_settings.json");
        
        Debug.Log($"<color=cyan>[VelocityLoader] Attempting to load rotation from: {settingsPath}</color>");
        
        if (!File.Exists(settingsPath))
        {
            Debug.LogWarning($"[VelocityLoader] visualization_settings.json not found at {settingsPath}. Using default rotation.");
            return;
        }
        
        try
        {
            string jsonText = File.ReadAllText(settingsPath);
            Debug.Log($"<color=cyan>[VelocityLoader] JSON content length: {jsonText.Length} bytes</color>");
            
            var settings = JsonUtility.FromJson<VisualizationSettings>(jsonText);
            
            if (settings != null && settings.additionalRotation != null)
            {
                additionalRotation = new Vector3(
                    settings.additionalRotation.x,
                    settings.additionalRotation.y,
                    settings.additionalRotation.z
                );
                Debug.Log($"<color=green>[VelocityLoader] Successfully loaded additionalRotation from JSON: {additionalRotation}</color>");
            }
            else
            {
                Debug.LogWarning($"[VelocityLoader] Settings or additionalRotation is null. settings={settings}, additionalRotation={settings?.additionalRotation}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VelocityLoader] Failed to load rotation from JSON: {e.Message}\n{e.StackTrace}");
        }
    }
    
    public void SetDisplaySteps(int x, int y, int z)
    {
        SetDisplayStepX(x);
        SetDisplayStepY(y);
        SetDisplayStepZ(z);
    }

    void Start()
    {
        // Manager handles initialization usually.
        // Fallback for independent testing:
        if (Manager.Instance == null)
        {
             if (velocityParent == null)
             {
                 StartCoroutine(initialization());
             }
        }
    }

    public IEnumerator initialization()
    {
        // Force reset loading state to allow re-initialization
        isLoading = false; 
        IsDataLoaded = false;
        loadStage = "init";
        
        if (Manager.Instance != null)
        {
            Manager.Instance.velocityLoader = this;
        }

        LoadJetColormap();
        
        if (Manager.Instance != null && Manager.Instance.ObjectParent != null)
        {
            Transform existingVelocity = Manager.Instance.ObjectParent.transform.Find("Velocity");
            
            if (existingVelocity != null)
            {
                velocityParent = existingVelocity.gameObject;
                Debug.Log("<color=green>Found existing Velocity object</color>");
                SeedExistingPoolFromChildren();
            }
            else
            {
                velocityParent = new GameObject("Velocity");
                velocityParent.transform.SetParent(Manager.Instance.ObjectParent.transform, false);
                Debug.Log("<color=cyan>Created new Velocity object</color>");
            }

            // Fix: Apply standard offset so data is centered in ObjectParent
            velocityParent.transform.localPosition = Manager.Instance.CurrentDataOffset;
            velocityParent.transform.localRotation = Manager.Instance.CurrentDataRotation;
            velocityParent.transform.localScale = Vector3.one * Manager.Instance.CurrentDataScale;
            
            // IMPORTANT: Manager controls the transform (Scale/Rot/Pos). 
            // Disable 'FitToMeshBounds' (AABB fitting) to prevent double-scaling and rotation misalignment.
            // We assume binary data coordinates match the mesh coordinates (Raw space).
            fitToMeshBounds = false;
            Debug.Log($"[VelocityLoader] Manager detected. Forced fitToMeshBounds=FALSE. Applied Transform: Scale={Manager.Instance.CurrentDataScale}, Rot={Manager.Instance.CurrentDataRotation.eulerAngles}");
        }
        else
        {
            Debug.LogWarning("Manager or ObjectParent not found. Creating local velocity parent.");
            if (velocityParent == null)
            {
                velocityParent = new GameObject("Velocity");
                velocityParent.transform.SetParent(transform);
            }
        }
        // Initialize previous values for change detection
        prevKeepAspectRatio = keepAspectRatio;
        prevUserScale = userScale;
        prevDisplayStepX = displayStepX;
        prevDisplayStepY = displayStepY;
        prevDisplayStepZ = displayStepZ;
        prevArrowScale = arrowScale;
        prevVelocityScaleFactor = velocityScaleFactor;
        wasVelocityActive = IsVelocityActive();

        Debug.Log("[VelocityLoader] Calling LoadAllVelocityDataAsync...");
        LoadAllVelocityDataAsync();
        
        // Wait for data loading to complete (or error/timeout)
        float startTime = Time.realtimeSinceStartup;
        float timeout = 30f; // safety to avoid infinite wait
        yield return new WaitUntil(() =>
        {
            if (IsDataLoaded) return true;
            if (loadStage == "error") return true;
            // if (!isLoading && loadedFrames.Count == 0) return true; // Removed this check as it might trigger prematurely
            if (Time.realtimeSinceStartup - startTime > timeout)
            {
                Debug.LogWarning("<color=yellow>VelocityLoader initialization timed out waiting for data.</color>");
                return true;
            }
            return false;
        });

        if (!IsDataLoaded && loadedFrames.Count == 0)
        {
            Debug.LogError("Velocity Data failed to load or timed out.");
            loadStage = "error";
        }
        velocityParent.SetActive(false);
        Debug.Log("<color=green>Velocity initialization complete</color>");
    }

    void Update()
    {
        RefreshArrowBaseCorrection();
        // REMOVED: velocityParent transform override. Manager controls this now.
        /*
        if (velocityParent != null)
        {
            velocityParent.transform.localRotation = Quaternion.Euler(velocityRotationOffset);
            velocityParent.transform.localScale = Vector3.one;
            velocityParent.transform.localPosition = Vector3.zero;
        }
        */

        if (HanyangKeyInput.GetKeyDown(KeyCode.Space))
        {
            TogglePlayback();
        }

        bool scaleChanged = keepAspectRatio != prevKeepAspectRatio || userScale != prevUserScale;
        bool densityChanged = displayStepX != prevDisplayStepX || displayStepY != prevDisplayStepY || displayStepZ != prevDisplayStepZ;
        bool visualScaleChanged = arrowScale != prevArrowScale || velocityScaleFactor != prevVelocityScaleFactor;

        if (scaleChanged || densityChanged || visualScaleChanged)
        {
            prevDisplayStepX = displayStepX;
            prevDisplayStepY = displayStepY;
            prevDisplayStepZ = displayStepZ;
            prevArrowScale = arrowScale;
            prevVelocityScaleFactor = velocityScaleFactor;
        }

        bool velocityActive = IsVelocityActive();
        
        // Don't auto-restart playback in frame control mode
        var buttonController = FindObjectOfType<ButtonControllerManager>();
        bool isFrameControlMode = buttonController != null && buttonController.isFrameControlMode;

        if (!velocityActive && isPlaying)
        {
            StopPlayback();
        }
        else if (velocityActive && !wasVelocityActive && autoPlay && loadedFrames.Count > 0 && !isPlaying && !isFrameControlMode)
        {
            StartPlayback();
        }

        wasVelocityActive = velocityActive;
        
        // GPU Instancing: render every frame (DrawMeshInstanced only lasts one frame)
        if (useGPUInstancing && velocityActive && IsDataLoaded && loadedFrames.Count > 0 && arrowMesh != null)
        {
            // ComputeBuffer mode (maximum performance)
            if (useComputeBuffer && arrowComputeShader != null && indirectMaterial != null)
            {
                // Initialize buffers if needed or frame changed
                if (!computeBuffersInitialized)
                {
                    InitializeAllFrameBuffers();
                }
                DisplayFrameComputeBuffer(currentFrameIndex);
            }
            // Regular GPU Instancing mode
            else if (instancedMaterial != null)
            {
                DisplayFrameInstanced(currentFrameIndex);
            }
        }
    }

    void LoadJetColormap()
    {
        if (jetColormap == null)
        {
            jetColormap = ColorMapUtility.GenerateJetColormap(256);
            Debug.Log($"<color=green>Generated Jet colormap: {jetColormap.width}x{jetColormap.height}</color>");
        }
    }

    void NormalizePositions(List<Vector3> positions)
    {
        if (positions == null || positions.Count == 0) return;

        // 1. Calculate Data Bounds
        dataMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        dataMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        foreach (var p in positions)
        {
            dataMin = Vector3.Min(dataMin, p);
            dataMax = Vector3.Max(dataMax, p);
        }

        if (fitToMeshBounds && Manager.Instance != null && Manager.Instance.ObjectParent != null && Manager.Instance.bloodVesselMesh != null)
        {
            var renderer = Manager.Instance.bloodVesselMesh.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Bounds worldBounds = renderer.bounds;
                Transform parent = Manager.Instance.ObjectParent.transform;
                targetMin = parent.InverseTransformPoint(worldBounds.min);
                targetMax = parent.InverseTransformPoint(worldBounds.max);
                Debug.Log($"<color=cyan>[Normalize] Mesh Found. WorldBounds: {worldBounds}, TargetMin: {targetMin}, TargetMax: {targetMax}</color>");
            }
            else
            {
                Debug.LogWarning("[Normalize] Renderer not found on BloodVesselMesh!");
                fitToMeshBounds = false; // Fallback
            }
        }
        else
        {
             Debug.LogWarning($"[Normalize] Skipping Mesh Fit. Fit={fitToMeshBounds}, Mgr={Manager.Instance!=null}, Par={Manager.Instance?.ObjectParent!=null}, Mesh={Manager.Instance?.bloodVesselMesh!=null}");
             targetMin = dataMin;
             targetMax = dataMax;
        }

        Debug.Log($"[Normalize] Data Range: {dataMin} to {dataMax}. Target Range: {targetMin} to {targetMax}");

        Vector3 dataSize = dataMax - dataMin;
        if (dataSize.x == 0) dataSize.x = 1f;
        if (dataSize.y == 0) dataSize.y = 1f;
        if (dataSize.z == 0) dataSize.z = 1f;

        Vector3 targetSize = targetMax - targetMin;
        
        Vector3 aspectScale = Vector3.one;
        if (keepAspectRatio)
        {
            float maxDimension = Mathf.Max(dataSize.x, Mathf.Max(dataSize.y, dataSize.z));
            if (maxDimension > 0)
            {
                aspectScale = dataSize / maxDimension;
            }
        }

        Vector3 finalScale = targetSize;
        if (keepAspectRatio)
        {
            finalScale = Vector3.Scale(targetSize, aspectScale);
        }
        finalScale = Vector3.Scale(finalScale, userScale);

        // Apply
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 pos = positions[i];
            
            // 0..1
            Vector3 normalized = new Vector3(
                (pos.x - dataMin.x) / dataSize.x,
                (pos.y - dataMin.y) / dataSize.y,
                (pos.z - dataMin.z) / dataSize.z
            );

            // Target
            Vector3 transformed = new Vector3(
                targetMin.x + normalized.x * finalScale.x,
                targetMin.y + normalized.y * finalScale.y,
                targetMin.z + normalized.z * finalScale.z
            );

            positions[i] = transformed;
        }
        
        Debug.Log($"<color=cyan>Normalized {positions.Count} positions. FitToMesh: {fitToMeshBounds}</color>");
    }

    public async void LoadAllVelocityDataAsync()
    {
        if (isLoading) 
        {
            Debug.LogWarning("[VelocityLoader] Already loading. Resetting state for new load.");
            // Don't return, just proceed to overwrite the load
        }
        
        isLoading = true;
        IsDataLoaded = false;
        loadProgress = 0f;
        loadStage = "scanning";

        // IMPORTANT: Clear previous data to prevent appending
        ClearAllData();

        // Ensure pool is reset if reloading
        isPoolInitialized = false;

        string dataFolderPath = GetCurrentDataFolderPath();
        Debug.Log($"<color=cyan>[VelocityLoader] Target Data Path: {dataFolderPath}</color>");
        
        if (!Directory.Exists(dataFolderPath))
        {
            Debug.LogError($"Velocity data folder not found: {dataFolderPath}");
            isLoading = false;
            loadStage = "error";
            return;
        }

        // 1. Load Static Positions
        string posFilePath = Path.Combine(dataFolderPath, "velocity_pos.bin");
        List<Vector3> sharedPositions = null;
        List<int> validIndices = null;
        
        if (File.Exists(posFilePath))
        {
            try 
            {
                // Load and Filter positions
                var result = LoadBinaryPositionsAndFilter(posFilePath);
                sharedPositions = result.positions;
                validIndices = result.validIndices;
                
                Debug.Log($"<color=green>Loaded {sharedPositions.Count} static positions (Filtered from raw). Step: {stepX}, {stepY}, {stepZ}</color>");
                
                // NORMALIZE HERE
                NormalizePositions(sharedPositions);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to load positions: {e.Message}");
                isLoading = false;
                loadStage = "error";
                return;
            }
        }
        else
        {
            Debug.LogError($"velocity_pos.bin not found in {dataFolderPath}");
             isLoading = false;
             loadStage = "error";
             return;
        }

        // Load rotation settings from JSON after confirming data folder path
        LoadRotationFromJSON();

        // 2. Scan Frame Files
        string[] files = Directory.GetFiles(dataFolderPath, "velocity_frame_*.bin");
        
        if (files.Length == 0)
        {
            Debug.LogError($"No velocity frame files found in: {dataFolderPath}");
            isLoading = false;
            loadStage = "error";
            return;
        }

        System.Array.Sort(files, (a, b) => 
        {
            int numA = ExtractFrameNumber(Path.GetFileNameWithoutExtension(a));
            int numB = ExtractFrameNumber(Path.GetFileNameWithoutExtension(b));
            return numA.CompareTo(numB);
        });

        totalFrames = files.Length;
        Debug.Log($"<color=cyan>Starting Parallel Binary Load for {totalFrames} frames...</color>");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var tempResults = new ConcurrentDictionary<int, VelocityData>();
        
        // Capture indices for parallel usage
        List<int> currentValidIndices = validIndices;  

        // Instead of blocking await, use coroutine for better UI responsiveness
        loadStage = "loading-frames";
        StartCoroutine(LoadFramesInBatches(files, sharedPositions, currentValidIndices, tempResults, stopwatch));
    }

    // Load frames in batches to keep UI responsive
    IEnumerator LoadFramesInBatches(string[] files, List<Vector3> sharedPositions, List<int> validIndices, 
                                     ConcurrentDictionary<int, VelocityData> tempResults, System.Diagnostics.Stopwatch stopwatch)
    {
        int batchSize = 5; // Process 5 files at a time
        int totalBatches = Mathf.CeilToInt((float)files.Length / batchSize);
        
        for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
        {
            int startIndex = batchIndex * batchSize;
            int endIndex = Mathf.Min(startIndex + batchSize, files.Length);
            
            // Process this batch synchronously (on main thread)
            for (int i = startIndex; i < endIndex; i++)
            {
                VelocityData data = LoadBinaryFrame(files[i], sharedPositions, validIndices);
                if (data != null)
                {
                    tempResults.TryAdd(i, data);
                }
            }
            
            // Update progress
            loadProgress = 0.2f + (0.5f * (float)(batchIndex + 1) / totalBatches);
            
            // Yield to allow UI to update (this keeps the loading spinner animating)
            yield return null;
        }
        
        stopwatch.Stop();
        Debug.Log($"<color=green>Binary Parsing Completed in {stopwatch.ElapsedMilliseconds} ms</color>");
        
        // Continue heavy steps on main thread but amortized through coroutine
        yield return StartCoroutine(FinalizeVelocityLoad(files, tempResults));
    }

    // Load Static Positions and Filter by Step
    (List<Vector3> positions, List<int> validIndices) LoadBinaryPositionsAndFilter(string filePath)
    {
        List<Vector3> positions = new List<Vector3>();
        List<int> validIndices = new List<int>();
        
        using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open)))
        {
            int count = reader.ReadInt32();
            
            // Adjust steps to be at least 1
            int sX = Mathf.Max(1, stepX);
            int sY = Mathf.Max(1, stepY);
            int sZ = Mathf.Max(1, stepZ);

            float probability = 1.0f / (sX * sY * sZ);
            Debug.Log($"[VelocityLoader] Loading {filePath}. Count: {count}. Step: {sX},{sY},{sZ}. Prob: {probability:F4}");
            
            
            byte[] buffer = reader.ReadBytes(count * 12);

            Quaternion parseRotation = Quaternion.Euler(dataParseRotation);
            bool applyParseRotation = dataParseRotation != Vector3.zero;
            int keptCount = 0;
            
            for (int i = 0; i < count; i++)
            {
                int offset = i * 12;
                float x = System.BitConverter.ToSingle(buffer, offset);
                float y = System.BitConverter.ToSingle(buffer, offset + 4);
                float z = System.BitConverter.ToSingle(buffer, offset + 8);
                
                // Coordinate Transformation (same as before)
                Vector3 finalPos = new Vector3(-y, x, z);

                if (applyParseRotation)
                {
                    finalPos = parseRotation * finalPos;
                }

                // Filter Logic:
                // Previous Voxel Grid logic (Steps 141-142) caused quantization artifacts ("Two lines" / layers) because the grid size (5mm) was too large for the vessel.
                // New Logic: "Probabilistic Random Sampling".
                // Deterministic Stride Filter (No Random)
                // "Remove random". We simply keep every Nth point based on volume stride.
                int stride = sX * sY * sZ;
                
                if (i % stride == 0)
                {
                    positions.Add(finalPos);
                    validIndices.Add(i);
                    keptCount++;
                }
            }
            Debug.Log($"[VelocityLoader] Kept {keptCount} out of {count} points ({(float)keptCount/count*100:F2}%)");
        }
        return (positions, validIndices);
    }

    // Load Frame Velocities (Filtered)
    VelocityData LoadBinaryFrame(string filePath, List<Vector3> sharedPositions, List<int> validIndices)
    {
        VelocityData data = new VelocityData();
        data.positions = sharedPositions; // Reference to filtered positions
        
        using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open)))
        {
            // The file contains raw float data for ALL points (not filtered).
            // We must skip data for filtered-out points.
            
            // To do this efficiently, we read the whole file into buffer (if memory allows) 
            // OR seek. Since we read whole file for positions anyway, reading whole buffer is likely fastest unless filtering is huge (>90%).
            // Actually, if we filter 90%, reading 100% and picking 10% is faster than 10% separate seeks if file is small.
            // Expected file size: 50k points * 12 bytes = 600KB. Tiny.
            // Just read all.
            
            // Read all bytes
            
            byte[] allBytes = reader.ReadBytes((int)reader.BaseStream.Length);

            Quaternion parseRotation = Quaternion.Euler(dataParseRotation);
            bool applyParseRotation = dataParseRotation != Vector3.zero;
            
            data.velocities = new List<Vector3>(sharedPositions.Count);
            data.velocityMagnitudes = new List<float>(sharedPositions.Count);
            
            // Iterate only through valid indices
            foreach(int originalIndex in validIndices)
            {
                int offset = originalIndex * 12;
                if (offset + 12 > allBytes.Length) break; // safety
                
                float vx = System.BitConverter.ToSingle(allBytes, offset);
                float vy = System.BitConverter.ToSingle(allBytes, offset + 4);
                float vz = System.BitConverter.ToSingle(allBytes, offset + 8);
                
                // Apply velocity transformation based on selected mode
                Vector3 vel;
                switch (velocityTransformMode)
                {
                    case VelocityTransformMode.NoTransform:
                        vel = new Vector3(vx, vy, vz);
                        break;
                    case VelocityTransformMode.MatchPosition:
                    case VelocityTransformMode.SwapXYNegateX:
                        vel = new Vector3(-vy, vx, vz);
                        break;
                    case VelocityTransformMode.NegateX:
                        vel = new Vector3(-vx, vy, vz);
                        break;
                    case VelocityTransformMode.NegateY:
                        vel = new Vector3(vx, -vy, vz);
                        break;
                    case VelocityTransformMode.NegateZ:
                        vel = new Vector3(vx, vy, -vz);
                        break;
                    case VelocityTransformMode.NegateXY:
                        vel = new Vector3(-vx, -vy, vz);
                        break;
                    case VelocityTransformMode.NegateXZ:
                        vel = new Vector3(-vx, vy, -vz);
                        break;
                    case VelocityTransformMode.NegateYZ:
                        vel = new Vector3(vx, -vy, -vz);
                        break;
                    case VelocityTransformMode.NegateAll:
                        vel = new Vector3(-vx, -vy, -vz);
                        break;
                    case VelocityTransformMode.SwapXY:
                        vel = new Vector3(-vy, vx, vz);
                        break;
                    case VelocityTransformMode.SwapXYNegateY:
                        vel = new Vector3(vy, -vx, vz);
                        break;
                    case VelocityTransformMode.SwapXYNegateBoth:
                        vel = new Vector3(-vy, -vx, vz);
                        break;
                    default:
                        vel = new Vector3(vx, vy, vz);
                        break;
                }
                
                
                if (applyParseRotation)
                {
                    vel = parseRotation * vel;
                }

                // Debug: Show raw data for first few points
                if (debugShowRawData && data.velocities.Count < 10)
                {
                    Debug.Log($"<color=cyan>Point {data.velocities.Count}: Raw({vx:F3}, {vy:F3}, {vz:F3}) → Transformed({vel.x:F3}, {vel.y:F3}, {vel.z:F3}) | Mag: {vel.magnitude:F3}</color>");
                }

                data.velocities.Add(vel);
                data.velocityMagnitudes.Add(vel.magnitude);
            }
        }
        return data;
    }                      



    IEnumerator FinalizeVelocityLoad(string[] files, ConcurrentDictionary<int, VelocityData> tempResults)
    {
        loadedFrames.Clear();
        
        // Assemble in order
        for (int i = 0; i < files.Length; i++)
        {
            if (tempResults.TryGetValue(i, out VelocityData data))
            {
                loadedFrames.Add(data);
            }
        }

        // Calculate Global Min/Max Velocity for coloring across all frames
        float globalMin = float.MaxValue;
        float globalMax = float.MinValue;

        // Sampling for speed if too many points
        foreach(var frame in loadedFrames)
        {
            foreach(float m in frame.velocityMagnitudes)
            {
                if(m < globalMin) globalMin = m;
                if(m > globalMax) globalMax = m;
            }
            if (Time.realtimeSinceStartup - Time.unscaledTime > 0.03f) yield return null;
        }

        minVelocity = globalMin;
        maxVelocity = globalMax;

        // Generate Colors
        foreach(var frame in loadedFrames)
        {
            frame.colors = new List<Color>(frame.velocities.Count);
            for(int j=0; j<frame.velocities.Count; j++)
            {
                float t = Mathf.InverseLerp(minVelocity, maxVelocity, frame.velocityMagnitudes[j]);
                if (jetColormap != null)
                    frame.colors.Add(jetColormap.GetPixelBilinear(t, 0.5f));
                else
                    frame.colors.Add(Color.Lerp(Color.blue, Color.red, t));
            }
            yield return null;
        }

        // Generate Grid Indices for Downsampling
        if (loadedFrames.Count > 0)
        {
             // Assume all frames share positions (or mostly similar bounds)
             // We compute grid indices based on the FIRST frame's positions (since they are shared)
             var firstFrame = loadedFrames[0];
             // Find bounds
             Vector3 minP = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
             Vector3 maxP = new Vector3(float.MinValue, float.MinValue, float.MinValue);
             
             foreach(var p in firstFrame.positions)
             {
                 minP = Vector3.Min(minP, p);
                 maxP = Vector3.Max(maxP, p);
             }
             
             // Auto-Detect Native Resolution
             // "Banding" happens if gridSize >> native point spacing (Clumping).
             Vector3 size = maxP - minP;
             float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
             // We must find the smallest non-zero delta between points to set the grid size correctly.
             
             float minDelta = float.MaxValue;
             var positions = loadedFrames[0].positions;
             int sampleCount = Mathf.Min(positions.Count - 1, 2000);
             
             for(int k=0; k<sampleCount; k++)
             {
                 Vector3 d = positions[k+1] - positions[k];
                 float dx = Mathf.Abs(d.x);
                 float dy = Mathf.Abs(d.y);
                 float dz = Mathf.Abs(d.z);
                 
                 // Ignore super small noise < 1e-7
                 if (dx > 1e-7f && dx < minDelta) minDelta = dx;
                 if (dy > 1e-7f && dy < minDelta) minDelta = dy;
                 if (dz > 1e-7f && dz < minDelta) minDelta = dz;
             }
             
             float gridSize = 0.001f; // Default fallback
             if (minDelta < 1.0f) // Sanity check found reasonable resolution
             {
                 gridSize = minDelta;
             }
             else if (maxDim >= 10.0f) // Fallback for large mm data
             {
                 gridSize = 1.0f;
             }
             
             Debug.Log($"[VelocityLoader] Auto-Detected Native Grid Size: {gridSize:F6} (MinDelta: {minDelta:F6})");
             
             foreach(var frame in loadedFrames)
             {
                 frame.gridIndices = new List<Vector3Int>(frame.positions.Count);
                 for(int k=0; k<frame.positions.Count; k++)
                 {
                     Vector3 p = frame.positions[k];
                     int gx = Mathf.FloorToInt((p.x - minP.x) / gridSize);
                     int gy = Mathf.FloorToInt((p.y - minP.y) / gridSize);
                     int gz = Mathf.FloorToInt((p.z - minP.z) / gridSize);
                     frame.gridIndices.Add(new Vector3Int(gx, gy, gz));
                 }
                 yield return null;
             }
        }

        Debug.Log($"<color=green>Velocity Frames Finalized: {loadedFrames.Count}</color>");
        
        if (loadedFrames.Count > 0)
        {
            InitializeObjectPool(loadedFrames[0].positions.Count);
        }
        
        IsDataLoaded = true;
        isLoading = false;
        loadStage = "done";
        loadProgress = 1.0f;
        
        if (autoPlay && IsVelocityActive())
        {
            StartPlayback();
        }
    }

    void InitializeObjectPool(int count)
    {
        if (arrowPrefab == null || velocityParent == null) return;

        int requestedCount = count;
        if (IsVisionOSRealityKitRuntime)
        {
            count = Mathf.Min(count, Mathf.Max(0, visionOSMaxArrowObjects));
            if (count < requestedCount)
            {
                Debug.Log($"[VelocityLoader] visionOS RealityKit arrow pool capped from {requestedCount} to {count} objects.");
            }
        }

        // Cleanup existing
        foreach (var obj in objectPool)
        {
            if (obj != null) Destroy(obj);
        }
        objectPool.Clear();
        arrowRenderers.Clear();

        objectPool.Capacity = count;
        arrowRenderers.Capacity = count;

        for (int i = 0; i < count; i++)
        {
            GameObject arrow = Instantiate(arrowPrefab, velocityParent.transform);
            arrow.name = $"Arrow_{i}";
            arrow.SetActive(false);
            objectPool.Add(arrow);
            
            // Cache renderers
            var renderers = new List<Renderer>();
            arrow.GetComponentsInChildren<Renderer>(true, renderers);
            arrowRenderers.Add(renderers);
        }
        
        isPoolInitialized = true;
    }



    int ExtractFrameNumber(string filename)
    {
        string numberStr = filename.Replace(filePrefix, "");
        int number;
        if (int.TryParse(numberStr, out number))
            return number;
        return 0;
    }

    public string GetCurrentDataFolderPath()
    {
        if (Manager.Instance != null)
        {
            return Path.Combine(Manager.Instance.GetCurrentDataFolderPath(), dataSubFolder);
        }
        else
        {
            return Path.Combine(Application.streamingAssetsPath, "data1", dataSubFolder);
        }
    }

    bool IsVelocityActive()
    {
        bool selfActive = isActiveAndEnabled;
        bool parentActive = velocityParent == null || velocityParent.activeInHierarchy;
        return selfActive && parentActive;
    }

    public void StartPlayback()
    {
        if (!IsVelocityActive())
        {
            Debug.LogWarning("Velocity is disabled; playback not started.");
            return;
        }

        if (loadedFrames.Count == 0)
        {
            Debug.LogWarning("No frames loaded!");
            return;
        }

        // Check if pool needs reinitialization (e.g., after scene reload)
        if (!isPoolInitialized || objectPool.Count == 0 || objectPool[0] == null)
        {
            Debug.Log("<color=cyan>Reinitializing object pool...</color>");
            InitializeObjectPool(loadedFrames[0].positions.Count);
        }

        if (isPlaying && playbackRoutine != null)
        {
            return;
        }

        isPlaying = true;
        playbackRoutine = StartCoroutine(PlaybackCoroutine());
        Debug.Log("<color=green>Playback started</color>");
    }

    public void StopPlayback()
    {
        isPlaying = false;

        if (playbackRoutine != null)
        {
            StopCoroutine(playbackRoutine);
            playbackRoutine = null;
        }
        Debug.Log("<color=yellow>Playback stopped</color>");
    }

    public void TogglePlayback()
    {
        if (isPlaying)
            StopPlayback();
        else
            StartPlayback();
    }

    void OnDisable()
    {
        if (isPlaying)
        {
            StopPlayback();
        }
        
        // Release ComputeBuffers when disabled to free GPU memory
        ReleaseComputeBuffers();
    }

    IEnumerator PlaybackCoroutine()
    {
        while (isPlaying)
        {
            if (!IsVelocityActive())
            {
                isPlaying = false;
                break;
            }

            DisplayFrame(currentFrameIndex);
            
            yield return new WaitForSeconds(frameInterval);
            
            currentFrameIndex++;
            
            if (currentFrameIndex >= loadedFrames.Count)
            {
                if (loop)
                {
                    currentFrameIndex = 0;
                }
                else
                {
                    isPlaying = false;
                }
            }
        }

        playbackRoutine = null;
    }

    void DisplayFrame(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= loadedFrames.Count)
            return;

        currentFrameIndex = frameIndex; // Update for SliceVisualization sync
        NotifySliceControllerFrameChanged();
        
        // GPU Instancing mode - just update frame index, rendering happens in Update()
        if (useGPUInstancing && arrowMesh != null)
        {
            // Don't render here - Update() handles GPU Instancing rendering
            return;
        }

        // Original GameObject Pool mode
        if (!isPoolInitialized) return;

        VelocityData frame = loadedFrames[frameIndex];
        int dataCount = frame.positions.Count;
        int poolCount = objectPool.Count;
        int count = Mathf.Min(dataCount, poolCount);

        int dX = Mathf.Max(1, displayStepX);
        int dY = Mathf.Max(1, displayStepY);
        int dZ = Mathf.Max(1, displayStepZ);

        for (int i = 0; i < count; i++)
        {
            GameObject arrow = objectPool[i];
            
            // Check if arrow is destroyed (happens on scene reload)
            if (arrow == null)
            {
                Debug.LogWarning("VelocityLoader: Arrow object is null, reinitializing pool...");
                isPoolInitialized = false;
                return;
            }
            
            bool isVisible = true;
            int sourceIndex = ResolveSourceIndex(i, dataCount, poolCount);
            
            if (frame.gridIndices.Count > sourceIndex)
            {
                // Revert to Grid Modulo as requested by user.
                // "If X is 2, print skipping one..." -> Modulo logic.
                Vector3Int idx = frame.gridIndices[sourceIndex];
                if (idx.x % dX != 0 || 
                    idx.y % dY != 0 || 
                    idx.z % dZ != 0)
                {
                    isVisible = false;
                }
            }

            if (isVisible)
            {
                if (!arrow.activeSelf) arrow.SetActive(true);
                UpdateArrow(i, arrow, frame.positions[sourceIndex], frame.velocities[sourceIndex], frame.velocityMagnitudes[sourceIndex], frame.colors[sourceIndex]);
            }
            else
            {
                if (arrow.activeSelf) arrow.SetActive(false);
            }
        }

        for (int i = count; i < objectPool.Count; i++)
        {
            if (objectPool[i] != null && objectPool[i].activeSelf) objectPool[i].SetActive(false);
        }
    }

    private static int ResolveSourceIndex(int poolIndex, int dataCount, int poolCount)
    {
        if (dataCount <= 0 || poolCount <= 0)
        {
            return 0;
        }

        if (poolCount >= dataCount)
        {
            return poolIndex;
        }

        float normalized = (poolIndex + 0.5f) / poolCount;
        return Mathf.Min(dataCount - 1, Mathf.FloorToInt(normalized * dataCount));
    }
    
    /// <summary>
    /// GPU Instanced rendering - much faster for large numbers of arrows
    /// </summary>
    void DisplayFrameInstanced(int frameIndex)
    {
        VelocityData frame = loadedFrames[frameIndex];
        
        int dX = Mathf.Max(1, displayStepX);
        int dY = Mathf.Max(1, displayStepY);
        int dZ = Mathf.Max(1, displayStepZ);
        
        // Hide pooled objects only once (not every frame)
        if (objectPool.Count > 0 && objectPool[0] != null && objectPool[0].activeSelf)
        {
            foreach (var obj in objectPool)
            {
                if (obj != null && obj.activeSelf) obj.SetActive(false);
            }
        }
        
        // Ensure batch arrays are allocated (reuse each frame)
        if (instanceMatrices == null || instanceMatrices.Length != MAX_INSTANCES_PER_BATCH)
        {
            instanceMatrices = new Matrix4x4[MAX_INSTANCES_PER_BATCH];
            instanceColors = new Vector4[MAX_INSTANCES_PER_BATCH];
        }
        
        // Initialize property block if needed
        if (instanceMPB == null) instanceMPB = new MaterialPropertyBlock();
        
        // Direct rendering without List allocation
        int visibleCount = 0;
        int batchIndex = 0;
        
        Vector3 parentScale = velocityParent != null ? velocityParent.transform.lossyScale : Vector3.one;
        
        for (int i = 0; i < frame.positions.Count; i++)
        {
            bool isVisible = true;
            
            // Fixed Logic: Use DETERMINISTIC HASH Stride to avoid Moiré patterns on grid data.
            int stride = dX * dY * dZ;
            
            if (frame.gridIndices.Count > i)
            {
                Vector3Int idx = frame.gridIndices[i];
                if (idx.x % dX != 0 || idx.y % dY != 0 || idx.z % dZ != 0)
                {
                    isVisible = false;
                }
            }
            
            if (!isVisible) continue;
            
            Vector3 pos = frame.positions[i];
            Vector3 vel = frame.velocities[i];
            float mag = frame.velocityMagnitudes[i];
            Color col = frame.colors[i];
            
            // Convert velocity vector to Euler angles directly
            Vector3 eulerAngles = VelocityToEuler(vel);
            
            // Apply rotations: additional rotation FIRST, then base rotation, then arrow correction
            Quaternion additionalRot = Quaternion.Euler(additionalRotation);
            Quaternion baseRot = Quaternion.Euler(eulerAngles);
            Quaternion rot = additionalRot * baseRot * arrowBaseCorrection;
            
            // Calculate scale
            float length = mag * velocityScaleFactor;
            length = Mathf.Max(length, 0.01f);
            Vector3 scale = Vector3.Scale(new Vector3(arrowScale, arrowScale, length * 0.1f), parentScale);
            
            // Apply parent transform
            Vector3 worldPos = velocityParent != null 
                ? velocityParent.transform.TransformPoint(pos) 
                : pos;
            Quaternion worldRot = velocityParent != null 
                ? velocityParent.transform.rotation * rot 
                : rot;
            
            // Add to batch array
            instanceMatrices[batchIndex] = Matrix4x4.TRS(worldPos, worldRot, scale);
            instanceColors[batchIndex] = new Vector4(col.r, col.g, col.b, col.a);
            batchIndex++;
            visibleCount++;
            
            // Draw when batch is full
            if (batchIndex >= MAX_INSTANCES_PER_BATCH)
            {
                instanceMPB.SetVectorArray("_Color", instanceColors);
                Graphics.DrawMeshInstanced(arrowMesh, 0, instancedMaterial, instanceMatrices, batchIndex, instanceMPB);
                batchIndex = 0;
            }
        }
        
        if (batchIndex > 0)
        {
            instanceMPB.SetVectorArray("_Color", instanceColors);
            Graphics.DrawMeshInstanced(arrowMesh, 0, instancedMaterial, instanceMatrices, batchIndex, instanceMPB);
        }
    }
    
    /// <summary>
    /// Network sync: Set the current frame index and display it
    /// </summary>
    public void SetFrameIndex(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= loadedFrames.Count) return;
        currentFrameIndex = frameIndex;
        DisplayFrame(currentFrameIndex);
    }

    private void NotifySliceControllerFrameChanged()
    {
        if (sliceController == null)
        {
            sliceController = FindObjectOfType<SliceController>();
        }

        if (sliceController != null)
        {
            sliceController.RefreshForVelocityFrameChange();
        }
    }

    GameObject CreateArrowObject()
    {
        GameObject arrow;
        
        // Additional safety check
        if (velocityParent == null)
        {
            Debug.LogError("velocityParent is null in CreateArrowObject!");
            return null;
        }
        
        if (arrowPrefab != null)
        {
            arrow = Instantiate(arrowPrefab, velocityParent.transform);
        }
        else
        {
            arrow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arrow.transform.SetParent(velocityParent.transform);
        }
        
        return arrow;
    }

    void SeedExistingPoolFromChildren()
    {
        if (velocityParent == null || hasSeededExistingPool) return;

        var children = velocityParent.GetComponentsInChildren<Transform>(false);
        foreach (var t in children)
        {
            if (t == velocityParent.transform) continue;
            var go = t.gameObject;
            if (objectPool.Contains(go)) continue;
            objectPool.Add(go);

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            arrowRenderers.Add(new List<Renderer>(renderers));
        }

        if (objectPool.Count > 0)
        {
            Debug.Log($"<color=green>Seeded velocity arrow pool from existing children: {objectPool.Count}</color>");
        }
        hasSeededExistingPool = true;
    }

    private void RefreshArrowBaseCorrection()
    {
        if (arrowBaseDirection == prevArrowBaseDirection)
        {
            return;
        }

        prevArrowBaseDirection = arrowBaseDirection;
        Vector3 baseDir = GetArrowBaseDirectionVector(arrowBaseDirection);
        arrowBaseCorrection = Quaternion.FromToRotation(baseDir, Vector3.forward);
    }

    private static Vector3 GetArrowBaseDirectionVector(ArrowBaseDirection direction)
    {
        switch (direction)
        {
            case ArrowBaseDirection.Back:
                return Vector3.back;
            case ArrowBaseDirection.Up:
                return Vector3.up;
            case ArrowBaseDirection.Down:
                return Vector3.down;
            case ArrowBaseDirection.Right:
                return Vector3.right;
            case ArrowBaseDirection.Left:
                return Vector3.left;
            case ArrowBaseDirection.Forward:
            default:
                return Vector3.forward;
        }
    }
    
    /// <summary>
    /// Public method to get rotation for a velocity vector (for SliceViewRenderer)
    /// </summary>
    public Quaternion GetRotationForVelocity(Vector3 velocity)
    {
        Vector3 eulerAngles = VelocityToEuler(velocity);
        Quaternion additionalRot = Quaternion.Euler(additionalRotation);
        Quaternion baseRot = Quaternion.Euler(eulerAngles);
        return additionalRot * baseRot * arrowBaseCorrection;
    }

    /// <summary>
    /// Convert velocity vector to Euler angles directly
    /// Assumes arrow prefab points along Y-axis (up) by default
    /// </summary>
    Vector3 VelocityToEuler(Vector3 velocity)
    {
        if (velocity.sqrMagnitude < 0.0001f)
            return Vector3.zero;
        
        velocity.Normalize();
        
        // Calculate pitch (rotation around X axis)
        // Angle from XZ plane
        float pitch = Mathf.Asin(velocity.y) * Mathf.Rad2Deg;
        
        // Calculate yaw (rotation around Y axis)
        // Angle in XZ plane
        float yaw = Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg;
        
        // Roll is 0 by default (can be adjusted with additionalRotation)
        float roll = 0f;
        
        return new Vector3(-pitch, yaw, roll);
    }

    void UpdateArrow(int poolIndex, GameObject arrow, Vector3 position, Vector3 direction, float magnitude, Color color)
    {
        arrow.transform.localPosition = position;
        
        if (direction != Vector3.zero)
        {
            // Convert velocity vector to Euler angles directly
            Vector3 eulerAngles = VelocityToEuler(direction);
            
            // Apply rotations: additional rotation FIRST, then base rotation, then arrow correction
            Quaternion additionalRot = Quaternion.Euler(additionalRotation);
            Quaternion baseRot = Quaternion.Euler(eulerAngles);
            arrow.transform.localRotation = additionalRot * baseRot * arrowBaseCorrection;
        }
            
        float length = magnitude * velocityScaleFactor;
        length = Mathf.Max(length, 0.01f);
        
        arrow.transform.localScale = new Vector3(arrowScale, arrowScale, length * 0.1f);

        if (mpb == null) mpb = new MaterialPropertyBlock();

        List<Renderer> renderers = (poolIndex >= 0 && poolIndex < arrowRenderers.Count) ? arrowRenderers[poolIndex] : null;
        if (renderers == null || renderers.Count == 0)
        {
            var fallback = arrow.GetComponentsInChildren<Renderer>(true);
            renderers = new List<Renderer>(fallback);
        }

        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(mpb);
            mpb.SetColor("_Color", color);
            mpb.SetColor("_BaseColor", color);
            mpb.SetColor("_TintColor", color);
            renderer.SetPropertyBlock(mpb);
        }
    }

    #region ComputeBuffer Mode Methods
    
    /// <summary>
    /// Pre-allocate ComputeBuffers for ALL frames at once
    /// </summary>
    void InitializeAllFrameBuffers()
    {
        if (!useComputeBuffer || arrowComputeShader == null || indirectMaterial == null) return;
        if (loadedFrames.Count == 0) return;
        
        // Release any existing buffers
        ReleaseComputeBuffers();
        
        // Find kernel
        computeKernel = arrowComputeShader.FindKernel("CSMain");
        
        // Pre-allocate input buffers for each frame
        maxBufferSize = 0;
        for (int f = 0; f < loadedFrames.Count; f++)
        {
            VelocityData frame = loadedFrames[f];
            int count = frame.positions.Count;
            if (count == 0) continue;
            
            if (count > maxBufferSize) maxBufferSize = count;
            
            FrameBufferData bufferData = new FrameBufferData();
            bufferData.count = count;
            
            // Create input buffers
            bufferData.positions = new ComputeBuffer(count, sizeof(float) * 3);
            bufferData.velocities = new ComputeBuffer(count, sizeof(float) * 3);
            bufferData.magnitudes = new ComputeBuffer(count, sizeof(float));
            bufferData.colors = new ComputeBuffer(count, sizeof(float) * 4);
            
            // Upload data
            bufferData.positions.SetData(frame.positions.ToArray());
            bufferData.velocities.SetData(frame.velocities.ToArray());
            bufferData.magnitudes.SetData(frame.velocityMagnitudes.ToArray());
            
            Vector4[] colors = new Vector4[count];
            for (int i = 0; i < count; i++)
            {
                Color c = frame.colors[i];
                colors[i] = new Vector4(c.r, c.g, c.b, c.a);
            }
            bufferData.colors.SetData(colors);
            
            // Grid indices for density filtering
            if (frame.gridIndices.Count == count)
            {
                bufferData.gridIndices = new ComputeBuffer(count, sizeof(int) * 3);
                int[] gridData = new int[count * 3];
                for (int i = 0; i < count; i++)
                {
                    gridData[i * 3] = frame.gridIndices[i].x;
                    gridData[i * 3 + 1] = frame.gridIndices[i].y;
                    gridData[i * 3 + 2] = frame.gridIndices[i].z;
                }
                bufferData.gridIndices.SetData(gridData);
            }
            
            frameBuffersCache[f] = bufferData;
        }
        
        // Create shared output buffers (sized for largest frame)
        if (maxBufferSize > 0)
        {
            matricesBuffer = new ComputeBuffer(maxBufferSize, sizeof(float) * 16);
            outputColorsBuffer = new ComputeBuffer(maxBufferSize, sizeof(float) * 4);
            
            // Args buffer for DrawMeshInstancedIndirect
            argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            args[0] = arrowMesh != null ? arrowMesh.GetIndexCount(0) : 0;
            args[1] = 0; // Will be updated per frame
            args[2] = arrowMesh != null ? arrowMesh.GetIndexStart(0) : 0;
            args[3] = arrowMesh != null ? arrowMesh.GetBaseVertex(0) : 0;
            args[4] = 0;
            argsBuffer.SetData(args);
        }
        
        computeBuffersInitialized = true;
        Debug.Log($"<color=green>[ComputeBuffer] Pre-allocated {loadedFrames.Count} frames, max {maxBufferSize} arrows</color>");
    }
    
    /// <summary>
    /// Render arrows using pre-allocated ComputeBuffers
    /// </summary>
    void DisplayFrameComputeBuffer(int frameIndex)
    {
        
        RefreshArrowBaseCorrection();
if (!computeBuffersInitialized || matricesBuffer == null) return;
        if (!frameBuffersCache.TryGetValue(frameIndex, out FrameBufferData bufferData)) return;
        
        int count = bufferData.count;
        
        // Set compute shader input buffers (from cache)
        arrowComputeShader.SetBuffer(computeKernel, "positions", bufferData.positions);
        arrowComputeShader.SetBuffer(computeKernel, "velocities", bufferData.velocities);
        arrowComputeShader.SetBuffer(computeKernel, "magnitudes", bufferData.magnitudes);
        arrowComputeShader.SetBuffer(computeKernel, "colors", bufferData.colors);
        
        // Set shared output buffers
        arrowComputeShader.SetBuffer(computeKernel, "outputMatrices", matricesBuffer);
        arrowComputeShader.SetBuffer(computeKernel, "outputColors", outputColorsBuffer);
        
        if (bufferData.gridIndices != null)
        {
            arrowComputeShader.SetBuffer(computeKernel, "gridIndices", bufferData.gridIndices);
            arrowComputeShader.SetInt("useGridFiltering", 1);
        }
        else
        {
            arrowComputeShader.SetInt("useGridFiltering", 0);
        }
        
        
        arrowComputeShader.SetVector("arrowBaseCorrection", new Vector4(arrowBaseCorrection.x, arrowBaseCorrection.y, arrowBaseCorrection.z, arrowBaseCorrection.w));
        
        // Convert additionalRotation Euler to Quaternion and pass to shader
        Quaternion additionalRot = Quaternion.Euler(additionalRotation);
        arrowComputeShader.SetVector("additionalRotation", new Vector4(additionalRot.x, additionalRot.y, additionalRot.z, additionalRot.w));
        
        arrowComputeShader.SetFloat("velocityScaleFactor", velocityScaleFactor);
        arrowComputeShader.SetFloat("arrowScale", arrowScale);
        
        Vector3 parentScale = velocityParent != null ? velocityParent.transform.lossyScale : Vector3.one;
        arrowComputeShader.SetVector("parentScale", parentScale);
        
        Matrix4x4 parentMatrix = velocityParent != null ? velocityParent.transform.localToWorldMatrix : Matrix4x4.identity;
        arrowComputeShader.SetMatrix("parentMatrix", parentMatrix);
        
        arrowComputeShader.SetInt("displayStepX", Mathf.Max(1, displayStepX));
        arrowComputeShader.SetInt("displayStepY", Mathf.Max(1, displayStepY));
        arrowComputeShader.SetInt("displayStepZ", Mathf.Max(1, displayStepZ));
        arrowComputeShader.SetInt("totalCount", count);
        
        // Dispatch compute shader
        int threadGroups = Mathf.CeilToInt(count / 256f);
        arrowComputeShader.Dispatch(computeKernel, threadGroups, 1, 1);
        
        // Update instance count in args buffer
        args[1] = (uint)count;
        argsBuffer.SetData(args);
        
        // Set material buffers
        indirectMaterial.SetBuffer("_Matrices", matricesBuffer);
        indirectMaterial.SetBuffer("_Colors", outputColorsBuffer);
        
        // Draw
        Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        Graphics.DrawMeshInstancedIndirect(arrowMesh, 0, indirectMaterial, bounds, argsBuffer);
    }
    
    /// <summary>
    /// Release all ComputeBuffers (cached and shared)
    /// </summary>
    void ReleaseComputeBuffers()
    {
        // Release cached frame buffers
        foreach (var kvp in frameBuffersCache)
        {
            kvp.Value?.Release();
        }
        frameBuffersCache.Clear();
        
        // Release shared output buffers
        matricesBuffer?.Release();
        outputColorsBuffer?.Release();
        argsBuffer?.Release();
        
        matricesBuffer = null;
        outputColorsBuffer = null;
        argsBuffer = null;
        
        maxBufferSize = 0;
        computeBuffersInitialized = false;
        lastComputeBufferFrame = -1;
    }
    
    #endregion

    private void ClearAllData()
    {
        Debug.Log("[VelocityLoader] Clearing all data...");
        
        // Stop any running playback
        StopPlayback();
        
        // CRITICAL: Clear Lists inside each VelocityData to prevent memory leak
        foreach (var frame in loadedFrames)
        {
            if (frame != null)
            {
                frame.positions?.Clear();
                frame.velocities?.Clear();
                frame.velocityMagnitudes?.Clear();
                frame.colors?.Clear();
            }
        }
        
        // Clear Frames
        loadedFrames.Clear();
        
        // Clear Compute Buffers
        ReleaseComputeBuffers();
        
        // Reset min/max
        minVelocity = float.MaxValue;
        maxVelocity = float.MinValue;
        
        // Hide Visuals
        if (velocityParent != null)
        {
            velocityParent.SetActive(false);
        }
        
        if (objectPool != null)
        {
            foreach (var arrow in objectPool)
            {
                if (arrow != null) Destroy(arrow);
            }
            objectPool.Clear();
            isPoolInitialized = false;
        }
    }

    void OnDestroy()
    {
        ClearAllData();
        if (objectPool != null) objectPool.Clear();
        if (arrowRenderers != null) arrowRenderers.Clear();
        mpb = null;
    }
}
