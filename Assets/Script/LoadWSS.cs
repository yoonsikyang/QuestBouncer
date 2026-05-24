using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;
using System.IO;
using System;
using System.Globalization;
using Photon.Pun;
using System.Threading.Tasks;
using System.Linq;

/// <summary>
/// Data structure for WSS vector information
/// </summary>
public class WSSVectorData
{
    public List<Vector3> positions = new List<Vector3>();
    public List<Vector3> originalPositions = new List<Vector3>();
    public List<Vector3> vectors = new List<Vector3>();
    public List<Vector3> originalVectors = new List<Vector3>();
    public List<float> magnitudes = new List<float>();
    public List<Color> colors = new List<Color>();
    public List<Vector3Int> gridIndices = new List<Vector3Int>(); // For downsampling
}

public class LoadWSS : MonoBehaviour, IPunObservable
{
    [Header("WSS Mesh Settings")]
    public string rootFolder = "data1";
    public string subFolder = "wss";
    public float animationInterval = 0.1f;
    public Material meshMaterial; // Assign a material in Inspector
    public Transform contentParent;

    private List<Mesh> loadedMeshes = new List<Mesh>();
    public GameObject displayObject;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    public bool isAnimating = false;
    private Coroutine animationCoroutine;

    public bool isActivated = true;
    public bool IsDataLoaded { get; private set; } = false;
    public Texture inactiveTexture;
    public float loadProgress = 0f;
    public string loadStage = "idle";
    
    private Texture originalTexture;
    private string currentFolderPath;
    private bool lastActivatedState = true;

    [Header("WSS Vector Settings")]
    public string vectorSubFolder = "wss_vectors";
    public string vectorFilePrefix = "wss_vectors_";
    public string vectorFileExtension = ".txt";
    public bool showWSSVectors = false;
    public GameObject arrowPrefab;
    public float arrowScale = 0.1f;
    public float arrowLengthMultiplier = 1.0f;
    public Texture2D jetColormap;

    [Header("WSS Vector Downsampling")]
    [Tooltip("Step size for X axis")]
    [Range(1, 20)] public int stepX = 1;
    [Tooltip("Step size for Y axis")]
    [Range(1, 20)] public int stepY = 1;
    [Tooltip("Step size for Z axis")]
    [Range(1, 20)] public int stepZ = 1;
    
    // Min/max WSS values from loaded data (for ColorBar display)
    public float minWss = float.MaxValue;
    public float maxWss = float.MinValue;
    
    [Header("GPU Instancing (Performance)")]
    [Tooltip("Use GPU Instancing for arrow rendering (faster, but no individual object interaction)")]
    public bool useGPUInstancing = false;
    public Material instancedMaterial; // Must have GPU Instancing enabled
    public Mesh arrowMesh; // Arrow mesh for instancing
    private Matrix4x4[] instanceMatrices;
    private Vector4[] instanceColors;
    private MaterialPropertyBlock instanceMPB;
    private const int MAX_INSTANCES_PER_BATCH = 1023;

    // Vector data
    private List<WSSVectorData> loadedVectorFrames = new List<WSSVectorData>();
    [SerializeField] // Serialize to keep references across Editor/Play mode
    private List<GameObject> vectorArrowPool = new List<GameObject>();
    [SerializeField] // Serialize parent reference
    private GameObject wssVectorParent;
    private bool isVectorPoolInitialized = false;
    public int currentVectorFrameIndex = 0;
    public int minPoolSize = 0; // User can set this to 10000
    private float minMagnitude = float.MaxValue;
    private float maxMagnitude = float.MinValue;
    private bool hasSeededVectorPool = false;

    // Frame Control 지원
    public int TotalFrameCount => wssFrameMagnitudes.Count > 0 ? wssFrameMagnitudes.Count : loadedMeshes.Count;
    public int CurrentFrameIndex => currentMeshFrameIndex;
    private int currentMeshFrameIndex = 0;

    /// <summary>
    /// 애니메이션 일시정지 (프레임 컨트롤 모드용)
    /// </summary>
    public void PauseAnimation()
    {
        if (isAnimating)
        {
            isAnimating = false;
            if (animationCoroutine != null) StopCoroutine(animationCoroutine);
            Debug.Log("<color=yellow>[LoadWSS] Animation paused</color>");
        }
    }

    /// <summary>
    /// 애니메이션 재개
    /// </summary>
    public void ResumeAnimation()
    {
        if (!isAnimating && isActivated && loadedMeshes.Count > 0)
        {
            isAnimating = true;
            if (animationCoroutine != null) StopCoroutine(animationCoroutine);
            animationCoroutine = StartCoroutine(PlayAnimation());
            Debug.Log("<color=green>[LoadWSS] Animation resumed</color>");
        }
    }

    /// <summary>
    /// 특정 메시 프레임 표시 (프레임 컨트롤 모드용)
    /// </summary>
    public void SetMeshFrameIndex(int frameIndex)
    {
        // Use wssFrameMagnitudes count for validation as we now have single mesh with multiple frames of data
        int maxFrames = wssFrameMagnitudes.Count > 0 ? wssFrameMagnitudes.Count : loadedMeshes.Count;
        
        if (frameIndex < 0 || frameIndex >= maxFrames) return;
        
        // Update both indices to be safe
        currentMeshFrameIndex = frameIndex;
        currentFrameIndex = frameIndex;

        // New Logic: Apply colors to the single shared mesh
        if (meshFilter != null && meshFilter.sharedMesh != null && wssFrameColors.Count > frameIndex)
        {
             meshFilter.sharedMesh.colors = wssFrameColors[frameIndex];
        }
        else if (loadedMeshes.Count > frameIndex) 
        {
            // Fallback for old multi-mesh mode (if ever used)
            meshFilter.mesh = loadedMeshes[frameIndex];
        }

        // 벡터 프레임도 동기화
        if (showWSSVectors && loadedVectorFrames.Count > 0)
        {
            int vectorIndex = frameIndex % loadedVectorFrames.Count;
            DisplayVectorFrame(vectorIndex);
        }
    }

    // Helper property to get full path
    private string FullFolderPath;

    public void SetFrameIndex(int index)
    {
        SetMeshFrameIndex(index);
    }
    
    public void ToggleAnimation()
    {
        if (isAnimating)
            PauseAnimation();
        else
            ResumeAnimation();
    }

    void Start()
    {
        // Manager handles initialization usually.
        if (Manager.Instance == null)
        {
            StartCoroutine(initialization());
        }
    }

    public IEnumerator initialization()
    {
        loadStage = "init";
        loadProgress = 0f;
        
        // Clear previous data before loading new data
        IsDataLoaded = false;
        if (animationCoroutine != null) 
        {
            StopCoroutine(animationCoroutine);
            animationCoroutine = null;
        }
        isAnimating = false;
        
        // CRITICAL: Destroy old mesh objects to prevent memory leak
        foreach (var mesh in loadedMeshes)
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }
        loadedMeshes.Clear();
        
        loadedVectorFrames.Clear();
        if (wssFrameColors != null) wssFrameColors.Clear(); // Fix: Clear colors prevents mismatch
        
        // Reset pool seeding flag so existing arrows can be reused
        hasSeededVectorPool = false;

        rootFolder = Manager.Instance.currentDataFolder;
        FullFolderPath = Path.Combine(rootFolder, subFolder);

        currentFolderPath = FullFolderPath;
        lastActivatedState = isActivated;

        // Find or create the WSS target object under MixedRealitySceneContent/Object Parent
        GameObject root = GameObject.Find("MixedRealitySceneContent");
        if (root != null)
        {
            Transform target = root.transform.Find("Object Parent/WSS");
            if (target == null)
            {
                GameObject wssObj = new GameObject("WSS");
                wssObj.transform.SetParent(root.transform.Find("Object Parent"), false);
                target = wssObj.transform;
            }

            displayObject = target.gameObject;
            meshFilter = displayObject.GetComponent<MeshFilter>();
            meshRenderer = displayObject.GetComponent<MeshRenderer>();
            
            if (meshFilter == null) meshFilter = displayObject.AddComponent<MeshFilter>();
            if (meshRenderer == null) meshRenderer = displayObject.AddComponent<MeshRenderer>();

            // Apply inspector material if provided, otherwise use a vertex-color-capable shader
            if (meshMaterial != null)
            {
                meshRenderer.material = meshMaterial;
            }
            else
            {
                Shader vcShader = Shader.Find("Custom/VertexColor");
                if (vcShader == null) vcShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (vcShader == null) vcShader = Shader.Find("Standard");
                meshRenderer.material = new Material(vcShader);
                meshRenderer.material.mainTexture = null; // keep vertex colors visible
            }
            
            if (meshRenderer != null)
            {
                originalTexture = meshRenderer.material.mainTexture;                
                EnsureDoubleSided(meshRenderer.material);

            }

            // Fix: Apply standard offset so data is centered in ObjectParent
            // Fix: Apply standard offset so data is centered in ObjectParent
            // Fix: Apply standard offset so data is centered in ObjectParent
            if (Manager.Instance != null && Manager.Instance.ObjectParent != null)
            {
                displayObject.transform.localPosition = Manager.Instance.CurrentDataOffset;
                displayObject.transform.localScale = Vector3.one * Manager.Instance.CurrentDataScale;
            
            }
            
            // Hide displayObject initially to prevent flash during loading
            displayObject.SetActive(false);
        }
        else
        {
            Debug.LogError("Could not find 'MixedRealitySceneContent' in scene");
        }

        // Apply initial state
        UpdateActivationState();
        
        yield return StartCoroutine(LoadWSSFiles());
        //yield return StartCoroutine(LoadWSSVectorFiles());
        
        // Mark as fully loaded after both are done
        IsDataLoaded = true;
        loadProgress = 1f;
        loadStage = "done";
    }


    void Update()
    {
        // // Check for path change
        // string newPath = Path.Combine(rootFolder, subFolder);
        // if (currentFolderPath != newPath)
        // {
        //     currentFolderPath = newPath;
        //     ReloadWSSFiles();
        // }

        // Note: WSS visibility is now controlled by Manager.ApplyWSSSubMode()
        // Do NOT auto-manage displayObject activation here to avoid overriding sub-mode settings

        // Animation control - only run when activated externally
        // Don't auto-restart animation in frame control mode
        var buttonController = FindObjectOfType<ButtonControllerManager>();
        bool isFrameControlMode = buttonController != null && buttonController.isFrameControlMode;
        if (isActivated && !isFrameControlMode)
        {
            // Fix: Check IsDataLoaded to ensure colors are ready before playing
            if (!isAnimating && loadedMeshes.Count > 0 && IsDataLoaded) 
            {
                isAnimating = true;
                if (animationCoroutine != null) StopCoroutine(animationCoroutine);
                animationCoroutine = StartCoroutine(PlayAnimation());
            }
        }
        else
        {
            if (isAnimating)
            {
                isAnimating = false;
                if (animationCoroutine != null) StopCoroutine(animationCoroutine);
            }
        }
        
        // GPU Instancing: render every frame (DrawMeshInstanced only lasts one frame)
        // Only render when WSS visualization mode is active AND sub-mode includes vectors AND vectors enabled
        bool isWSSVectorMode = Manager.Instance != null 
            && Manager.Instance.visualizationMode == VisualizationMode.WSS 
            && (Manager.Instance.wssSubMode == WSSSubMode.WSS_Vector || Manager.Instance.wssSubMode == WSSSubMode.BloodVessel_Vector);
        if (useGPUInstancing && isWSSVectorMode && showWSSVectors && loadedVectorFrames.Count > 0 && instancedMaterial != null && arrowMesh != null)
        {
            DisplayVectorFrameInstanced(currentVectorFrameIndex);
        }


        if (isActivated != lastActivatedState)
        {
            lastActivatedState = isActivated;
            UpdateActivationState();
        }
    }

    void ReloadWSSFiles()
    {
        IsDataLoaded = false;
        if (animationCoroutine != null) StopCoroutine(animationCoroutine);
        isAnimating = false;
        loadedMeshes.Clear();
        Debug.Log($"Reloading WSS files from: {currentFolderPath}");
        StartCoroutine(LoadWSSFiles());
    }

    void UpdateActivationState()
    {
        if (isActivated)
        {
            if (meshRenderer != null) meshRenderer.material.mainTexture = originalTexture;
        }
        else
        {
            // Stop animation if deactivated
            if (isAnimating)
            {
                isAnimating = false;
                if (animationCoroutine != null) StopCoroutine(animationCoroutine);
            }
            
            if (meshRenderer != null && inactiveTexture != null)
            {
                meshRenderer.material.mainTexture = inactiveTexture;
            }
        }
    }

    IEnumerator LoadWSSFiles()
    {
        loadStage = "wss-binary";
        loadProgress = 0.05f;

        string dirPath = Path.Combine(Application.streamingAssetsPath, currentFolderPath);
        if (!Directory.Exists(dirPath))
        {
            Debug.LogError("Directory not found: " + dirPath);
            loadStage = "error";
            yield break;
        }

        // 1. Load Static Mesh
        string meshPath = Path.Combine(dirPath, "wss_mesh.bin");
        if (!File.Exists(meshPath))
        {
            Debug.LogError($"wss_mesh.bin not found in {dirPath}");
            loadStage = "error";
            yield break;
        }

        Mesh sharedMesh = LoadBinaryWSSMesh(meshPath);
        if (sharedMesh == null)
        {
             loadStage = "error";
             yield break;
        }
        
        // Assign mesh immediately
        if (meshFilter != null)
        {
            meshFilter.mesh = sharedMesh;
            // Also assign to collider if needed?
        }
        loadedMeshes.Clear();
        loadedMeshes.Add(sharedMesh); // Keep one mesh

        Debug.Log($"<color=green>Loaded WSS Mesh: {sharedMesh.vertexCount} verts, {sharedMesh.triangles.Length/3} tris</color>");
        loadProgress = 0.2f;

        // 2. Load Frame Data (Magnitudes)
        string[] frameFiles = Directory.GetFiles(dirPath, "wss_frame_*.bin");
        
        if (frameFiles.Length == 0)
        {
            Debug.LogWarning($"No wss frame files found in {dirPath}");
            loadStage = "wss-mesh-done"; 
            yield break;
        }

        Array.Sort(frameFiles, (a, b) => {
            int numA = ExtractNumber(Path.GetFileNameWithoutExtension(a));
            int numB = ExtractNumber(Path.GetFileNameWithoutExtension(b));
            return numA.CompareTo(numB);
        });

        // Initialize frame list
        // We will store colors per frame, or magnitudes per frame?
        // Storing magnitudes is more memory efficient (1 float vs 4 floats per vertex).
        // But converting to color every frame in Update might be slow if vertex count is high.
        // Vertex count for WSS is usually high (e.g. 50k-100k).
        // 100k verts * 4 floats * 24 frames ~ 10MB per frame. 240MB total. Acceptable.
        // Pre-calculating colors allow faster playback.
        
        List<Color[]> frameColors = new List<Color[]>();
        
        // Find global min/max for normalization?
        // Or assume data is already normalized or we Scan first.
        // The previous OBJ loader didn't normalize across frames explicitly (it just read colors or applied them).
        // But current plan says "WSS Magnitudes". We need min/max to colorize.
        
        float globalMin = float.MaxValue;
        float globalMax = float.MinValue;

        var tempMagnitudes = new List<float[]>(frameFiles.Length);
        
        // Load all magnitudes first
        for(int i=0; i<frameFiles.Length; i++)
        {
            float[] mags = LoadBinaryWSSFrame(frameFiles[i], sharedMesh.vertexCount);
            tempMagnitudes.Add(mags);
            
            // Simple min/max check (can be optimized with parallel or stepping)
            foreach(float m in mags)
            {
                if(m < globalMin) globalMin = m;
                if(m > globalMax) globalMax = m;
            }
            if (i % 5 == 0) yield return null;
        }
        
        Debug.Log($"WSS Magnitude Range: {globalMin} - {globalMax}");

        // Generate Colors
        this.wssFrameMagnitudes = tempMagnitudes; // Persist raw data
        this.dataMinMagnitude = globalMin;
        this.dataMaxMagnitude = globalMax;
        this.currentFrameIndex = 0; // Reset index on load

        // Apply initial colors
        RefreshWSSColors();

        loadStage = "wss-done";
        loadProgress = 1.0f;
        Debug.Log("WSS Data Load Complete");
        isActivated = true;
        UpdateActivationState();
    }
    
    // New storage for frames
    private List<Color[]> wssFrameColors = new List<Color[]>();
    private List<float[]> wssFrameMagnitudes = new List<float[]>(); // Raw data for dynamic coloring
    private float dataMinMagnitude = 0f;
    private float dataMaxMagnitude = 1f;
    private int currentFrameIndex = 0;

    // Call this when settings change
    public void RefreshWSSColors()
    {
        if (wssFrameMagnitudes == null || wssFrameMagnitudes.Count == 0) return;
        
        if (jetColormap == null) jetColormap = ColorMapUtility.GenerateJetColormap(256);

        float min = dataMinMagnitude;
        float max = dataMaxMagnitude;
        
        // Update public min/max for ColorBar display
        minWss = min;
        maxWss = max;

        // Apply intensity
        
        // Use Manager's range if valid, otherwise data range
        // Looking for Manager settings... assuming standard names or using data range for now
        // If Manager has Min/Max settable by UI, use them.
        // For now, defaulting to data range as base implementation.
        // float min = dataMinMagnitude;
        // float max = dataMaxMagnitude;

        // Example: if Manager has wssMin/wssMax
        // if (Manager.Instance.overrideWssRange) { min = Manager.Instance.wssMin; max = Manager.Instance.wssMax; }

        wssFrameColors.Clear();
        for(int i=0; i<wssFrameMagnitudes.Count; i++)
        {
            float[] mags = wssFrameMagnitudes[i];
            Color[] colors = new Color[mags.Length];
            for(int k=0; k<mags.Length; k++)
            {
                float t = Mathf.InverseLerp(min, max, mags[k]);
                Color c;
                if (jetColormap != null)
                    c = jetColormap.GetPixelBilinear(t, 0.5f);
                else
                    c = Color.Lerp(Color.blue, Color.red, t);
                
                colors[k] = c;
            }
            wssFrameColors.Add(colors);
        }

        // Apply immediately if mesh is ready (for pause mode updates)
        if (meshFilter != null && meshFilter.sharedMesh != null && wssFrameColors.Count > currentFrameIndex)
        {
            meshFilter.sharedMesh.colors = wssFrameColors[currentFrameIndex];
        }
    }

    Mesh LoadBinaryWSSMesh(string filePath)
    {
        using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open)))
        {
            int vertexCount = reader.ReadInt32();
            int indexCount = reader.ReadInt32(); // Although python script writes just faces? 
            // Check main_generate_data.py packing:
            // f.write(struct.pack('i', num_vertices))
            // f.write(struct.pack('i', num_faces)) (Wait, number of faces or indices? "len(faces)" usually is face count)
            // f.write(vertices.tobytes())
            // f.write(faces.tobytes())
            
            // Wait, my python script:
            // faces = fv['faces'] ...
            // num_faces = len(faces)
            // So reader.ReadInt32() is num_faces.
            // Faces in MAT are likely [v1, v2, v3] or [v1, v2, v3, v4]. Unity needs triangle indices.
            // If num_faces is number of "face tuples", I need to read that many tuples.
            // But reader just reads bytes.
            // Let's assume faces are flattened int array?
            // "faces.tobytes()" will write the whole array.
            
            // I need to read vertices first. 
            // PPos: 12 bytes * vertexCount.
            
            byte[] vertBytes = reader.ReadBytes(vertexCount * 12);
            Vector3[] vertices = new Vector3[vertexCount];
            for(int i=0; i<vertexCount; i++)
            {
                float x = System.BitConverter.ToSingle(vertBytes, i*12);
                float y = System.BitConverter.ToSingle(vertBytes, i*12+4);
                float z = System.BitConverter.ToSingle(vertBytes, i*12+8);
                // Unity: X axis flip? Previous LoadWSS did -x.
                vertices[i] = new Vector3(y, -x, z);
            }
            
            // Faces
            // How many bytes? "num_faces" * per_face_size?
            // If faces is Int32 array of shape (N, 3).
            // I should just read to end of file? Or Calculate size?
            // Assuming faces are triangles (3 ints).
            // Need to double check python script "faces" type.
            // Usually in matlab/scipy faces are 1-based indices? Python 0-based?
            // My script likely handles 1-based -> 0-based? 
            // If I just dump bytes, I need to know how many.
            
            // Hack: Read the rest of the stream.
            long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
            int numIndices = (int)(remaining / 4);
            byte[] faceBytes = reader.ReadBytes((int)remaining);
            
            int[] triangles = new int[numIndices];
            for(int i=0; i<numIndices; i++)
            {
                triangles[i] = System.BitConverter.ToInt32(faceBytes, i*4);
                // Winding order? Unity is CW?
            }
            
            Mesh mesh = new Mesh();
            // Enable 16-bit or 32-bit index buffer
            if (vertexCount > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            return mesh;
        }
    }



    float[] LoadBinaryWSSFrame(string filePath, int expectedCount)
    {
        using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open)))
        {
            // Python: magnitudes.astype(np.float32).tobytes()
            // No count header?
            // Wait, my plan said "wss_frame_{t}.bin: ... (float array)".
            // Let's read by expected count.
            
            byte[] bytes = reader.ReadBytes(expectedCount * 4);
            float[] mags = new float[expectedCount];
            for(int i=0; i<expectedCount; i++)
            {
                mags[i] = System.BitConverter.ToSingle(bytes, i*4);
            }
            return mags;
        }
    }

    IEnumerator PlayAnimation()
    {
        // Use class member currentFrameIndex
        while (isAnimating && isActivated)
        {
            if (wssFrameColors != null && wssFrameColors.Count > 0 && meshFilter != null && meshFilter.sharedMesh != null)
            {
                meshFilter.mesh.colors = wssFrameColors[currentFrameIndex];
                
                // Also update vectors if active
                if (showWSSVectors && loadedVectorFrames.Count > 0)
                {
                    int vectorIndex = currentFrameIndex % loadedVectorFrames.Count;
                    DisplayVectorFrame(vectorIndex);
                }
                
                currentFrameIndex = (currentFrameIndex + 1) % wssFrameColors.Count;
            }
            yield return new WaitForSeconds(animationInterval);
        }
    }

    // A very simple runtime OBJ parser
    // (Deprecated/Removed replacement)
    
    // Helper to get extract number
    int ExtractNumber(string filename)
    {
        string numberPart = "";
        for (int i = filename.Length - 1; i >= 0; i--)
        {
            if (char.IsDigit(filename[i]))
                numberPart = filename[i] + numberPart;
            else if (numberPart.Length > 0)
                break;
        }
        
        if (int.TryParse(numberPart, out int result))
            return result;
        return 0;
    }


    // Photon Synchronization
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(isActivated);
            stream.SendNext(isAnimating);
            stream.SendNext(rootFolder);
            stream.SendNext(subFolder);
        }
        else
        {
            isActivated = (bool)stream.ReceiveNext();
            bool networkIsAnimating = (bool)stream.ReceiveNext();
            rootFolder = (string)stream.ReceiveNext();
            subFolder = (string)stream.ReceiveNext();

            // Handle animation state sync
            if (networkIsAnimating != isAnimating)
            {
                if (networkIsAnimating)
                {
                    // Force start animation
                    if (loadedMeshes.Count > 0 && isActivated)
                    {
                        isAnimating = true;
                        if (animationCoroutine != null) StopCoroutine(animationCoroutine);
                        animationCoroutine = StartCoroutine(PlayAnimation());
                    }
                }
                else
                {
                    // Force stop animation
                    isAnimating = false;
                    if (animationCoroutine != null) StopCoroutine(animationCoroutine);
                }
            }
        }
    }

    void EnsureDoubleSided(Material mat)
    {
        if (mat == null) return;
        // Common properties for Standard/URP Unlit/Custom shaders
        if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", 0);            // 0 = Off
        if (mat.HasProperty("_CullMode")) mat.SetInt("_CullMode", 0);    // URP
        if (mat.HasProperty("_DoubleSidedEnable")) mat.SetInt("_DoubleSidedEnable", 1);
        if (mat.HasProperty("_DoubleSidedNormalMode")) mat.SetInt("_DoubleSidedNormalMode", 0);
    }

    // ========== WSS Vector Visualization Methods ==========

    IEnumerator LoadWSSVectorFiles()
    {
        loadStage = "wss-vectors";
        loadProgress = Mathf.Max(loadProgress, 0.5f);

        string vectorPath = Path.Combine(Application.streamingAssetsPath, rootFolder, vectorSubFolder);
        
        if (!Directory.Exists(vectorPath))
        {
            Debug.LogWarning($"<color=yellow>WSS vector directory not found: {vectorPath}</color>");
            loadStage = "done";
            loadProgress = 1f;
            yield break;
        }

        string[] files = Directory.GetFiles(vectorPath, $"{vectorFilePrefix}*{vectorFileExtension}");
        
        if (files.Length == 0)
        {
            Debug.LogWarning($"<color=yellow>No WSS vector files found in: {vectorPath}</color>");
            loadStage = "done";
            loadProgress = 1f;
            yield break;
        }

        // Sort files by number
        Array.Sort(files, (a, b) => {
            int numA = ExtractNumber(Path.GetFileNameWithoutExtension(a));
            int numB = ExtractNumber(Path.GetFileNameWithoutExtension(b));
            return numA.CompareTo(numB);
        });

        loadedVectorFrames.Clear();
        minMagnitude = float.MaxValue;
        maxMagnitude = float.MinValue;

        // Use Parallel.For for heavy parsing to avoid freezing main thread
        int total = files.Length;
        var tempResults = new ConcurrentDictionary<int, WSSVectorData>();
        float localMinMag = float.MaxValue;
        float localMaxMag = float.MinValue;
        object magLock = new object();
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        var loadTask = Task.Run(() =>
        {
            Parallel.For(0, files.Length, i =>
            {
                WSSVectorData data = LoadWSSVectorFile(files[i]);
                if (data != null)
                {
                    tempResults.TryAdd(i, data);
                    
                    // Thread-safe magnitude tracking
                    foreach (var mag in data.magnitudes)
                    {
                        lock (magLock)
                        {
                            if (mag < localMinMag) localMinMag = mag;
                            if (mag > localMaxMag) localMaxMag = mag;
                        }
                    }
                }
            });
        });

        yield return new WaitUntil(() => loadTask.IsCompleted);
        
        stopwatch.Stop();
        Debug.Log($"<color=green>WSS Vector Parallel Load completed in {stopwatch.ElapsedMilliseconds}ms</color>");

        if (loadTask.Exception != null)
        {
            Debug.LogError($"WSS Vector load failed: {loadTask.Exception.Flatten().Message}");
            loadStage = "error";
            yield break;
        }
        loadProgress = 0.75f;

        // Assemble results in order
        minMagnitude = localMinMag;
        maxMagnitude = localMaxMag;
        loadedVectorFrames.Clear();
        for (int i = 0; i < files.Length; i++)
        {
            if (tempResults.TryGetValue(i, out WSSVectorData data))
            {
                loadedVectorFrames.Add(data);
            }
        }

        if (loadedVectorFrames.Count > 0)
        {
            Debug.Log($"<color=green>Loaded {loadedVectorFrames.Count} WSS vector frames</color>");
            Debug.Log($"<color=green>Magnitude range: {minMagnitude:F4} - {maxMagnitude:F4}</color>");
            
            // Generate colors for all frames
            GenerateVectorColors();
            yield return null; // Yield after color gen
            loadProgress = 0.85f;
            
            // Find max count for pooling
            int maxCount = 0;
            foreach (var frame in loadedVectorFrames)
            {
                if (frame.positions.Count > maxCount) maxCount = frame.positions.Count;
            }

            // Initialize arrow pool
            int finalCount = Mathf.Max(maxCount, minPoolSize);
            if (finalCount > 0)
            {
                // Run pool init as Coroutine
                yield return StartCoroutine(InitializeVectorArrowPoolCoroutine(finalCount));
            }
        }

        loadProgress = 1f;
        loadStage = "done";
    }

    WSSVectorData LoadWSSVectorFile(string filePath)
    {
        try
        {
            WSSVectorData data = new WSSVectorData();
            string[] lines = File.ReadAllLines(filePath);

            // First pass: collect all positions
            var tempData = new List<(Vector3 pos, Vector3 vec, float mag)>();
            
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                
                // Skip header line or empty lines
                if (i == 0 || string.IsNullOrWhiteSpace(line))
                    continue;

                string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                
                if (parts.Length >= 7)
                {
                    float x = -float.Parse(parts[0], CultureInfo.InvariantCulture);
                    float y = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    float z = float.Parse(parts[2], CultureInfo.InvariantCulture);
                    float wx = float.Parse(parts[3], CultureInfo.InvariantCulture);
                    float wy = float.Parse(parts[4], CultureInfo.InvariantCulture);
                    float wz = float.Parse(parts[5], CultureInfo.InvariantCulture);
                    float mag = float.Parse(parts[6], CultureInfo.InvariantCulture);

                    Vector3 pos = new Vector3(x, y, z);
                    Vector3 vec = new Vector3(wx, wy, wz);

                    tempData.Add((pos, vec, mag));

                    // Track min/max magnitude
                    if (mag < minMagnitude) minMagnitude = mag;
                    if (mag > maxMagnitude) maxMagnitude = mag;
                }
            }

            // Second pass: calculate grid indices
            if (tempData.Count > 0)
            {
                var uniqueX = tempData.Select(p => (float)System.Math.Round(p.pos.x, 4)).Distinct().OrderBy(v => v).ToList();
                var uniqueY = tempData.Select(p => (float)System.Math.Round(p.pos.y, 4)).Distinct().OrderBy(v => v).ToList();
                var uniqueZ = tempData.Select(p => (float)System.Math.Round(p.pos.z, 4)).Distinct().OrderBy(v => v).ToList();

                foreach (var item in tempData)
                {
                    int idxX = uniqueX.BinarySearch((float)System.Math.Round(item.pos.x, 4));
                    int idxY = uniqueY.BinarySearch((float)System.Math.Round(item.pos.y, 4));
                    int idxZ = uniqueZ.BinarySearch((float)System.Math.Round(item.pos.z, 4));

                    if (idxX < 0) idxX = ~idxX;
                    if (idxY < 0) idxY = ~idxY;
                    if (idxZ < 0) idxZ = ~idxZ;

                    data.positions.Add(item.pos);
                    data.originalPositions.Add(item.pos);
                    data.vectors.Add(item.vec);
                    data.originalVectors.Add(item.vec);
                    data.magnitudes.Add(item.mag);
                    data.gridIndices.Add(new Vector3Int(idxX, idxY, idxZ));
                }
            }

            return data;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading WSS vector file {filePath}: {e.Message}");
            return null;
        }
    }

    void GenerateVectorColors()
    {
        if (jetColormap == null)
        {
            jetColormap = ColorMapUtility.GenerateJetColormap(256);
        }

        foreach (var frame in loadedVectorFrames)
        {
            frame.colors.Clear();
            foreach (float mag in frame.magnitudes)
            {
                float normalized = Mathf.InverseLerp(minMagnitude, maxMagnitude, mag);
                Color color = GetColorFromJet(normalized);
                frame.colors.Add(color);
            }
        }
    }

    Color GetColorFromJet(float t)
    {
        if (jetColormap == null) return Color.white;
        t = Mathf.Clamp01(t);
        return jetColormap.GetPixelBilinear(t, 0.5f);
    }

    void InitializeVectorArrowPool(int count)
    {
        if (Application.isPlaying)
        {
            if (!hasSeededVectorPool) SeedVectorPoolFromChildren();
            StartCoroutine(InitializeVectorArrowPoolCoroutine(count));
        }
        else
        {
            InitializeVectorArrowPoolSync(count);
        }
    }

    IEnumerator InitializeVectorArrowPoolCoroutine(int count)
    {
         if (!hasSeededVectorPool) SeedVectorPoolFromChildren();
         if (isVectorPoolInitialized && vectorArrowPool.Count >= count) yield break;

        // Create parent for WSS vectors
        EnsureWSSVectorParent();

        // Set scale to 0.1
        if (wssVectorParent != null) wssVectorParent.transform.localScale = Vector3.one * 0.1f;

        // Clean nulls first
        vectorArrowPool.RemoveAll(x => x == null);

        int currentCount = vectorArrowPool.Count;
        int needed = count - currentCount;

        if (needed > 0)
        {
            // Create arrow pool amortized
            int created = 0;
            int batchSize = 50; // Create 50 arrows per frame

            for (int i = 0; i < needed; i++)
            {
                GameObject arrow = CreateVectorArrow();
                if (arrow != null)
                {
                    arrow.SetActive(false);
                    vectorArrowPool.Add(arrow);
                }
                
                created++;
                if (created % batchSize == 0)
                {
                    yield return null;
                }
            }
            Debug.Log($"<color=green>Initialized WSS vector arrow pool with {currentCount} existing + {needed} new arrows</color>");
        }
        else
        {
            Debug.Log($"<color=green>WSS vector arrow pool already has {currentCount} arrows (needed {count})</color>");
        }

        isVectorPoolInitialized = true;
    }

    void InitializeVectorArrowPoolSync(int count)
    {
        if (!hasSeededVectorPool) SeedVectorPoolFromChildren();
        if (isVectorPoolInitialized && vectorArrowPool.Count >= count) return;

        EnsureWSSVectorParent();
        if (wssVectorParent != null) wssVectorParent.transform.localScale = Vector3.one * 0.1f;

        vectorArrowPool.RemoveAll(x => x == null);

        int currentCount = vectorArrowPool.Count;
        int needed = count - currentCount;

        if (needed > 0)
        {
            for (int i = 0; i < needed; i++)
            {
                GameObject arrow = CreateVectorArrow();
                if (arrow != null)
                {
                    arrow.SetActive(false);
                    vectorArrowPool.Add(arrow);
                }
            }
            Debug.Log($"<color=green>Initialized WSS vector arrow pool (Sync) with {currentCount} existing + {needed} new arrows</color>");
        }

        isVectorPoolInitialized = true;
    }

    void EnsureWSSVectorParent()
    {
        if (wssVectorParent == null)
        {
             if (Manager.Instance != null && Manager.Instance.ObjectParent != null)
            {
                Transform existingParent = Manager.Instance.ObjectParent.transform.Find("WSS_Vectors");
                if (existingParent != null)
                {
                    wssVectorParent = existingParent.gameObject;
                }
                else
                {
                    wssVectorParent = new GameObject("WSS_Vectors");
                    wssVectorParent.transform.SetParent(Manager.Instance.ObjectParent.transform, false);
                }
            }
            else
            {
                // In Editor time or missing Manager
                GameObject root = GameObject.Find("MixedRealitySceneContent");
                if (root != null)
                {
                    Transform op = root.transform.Find("Object Parent");
                    if (op != null)
                    {
                         Transform existingParent = op.Find("WSS_Vectors");
                         if (existingParent != null)
                         {
                             wssVectorParent = existingParent.gameObject;
                         }
                         else
                         {
                             wssVectorParent = new GameObject("WSS_Vectors");
                             wssVectorParent.transform.SetParent(op, false);
                         }
                    }
                    else
                    {
                         wssVectorParent = new GameObject("WSS_Vectors");
                    }
                }
                else
                {
                    wssVectorParent = new GameObject("WSS_Vectors");
                }
            }
        }
    }
    void SeedVectorPoolFromChildren()
    {
        if (wssVectorParent == null || hasSeededVectorPool) return;

        var children = wssVectorParent.GetComponentsInChildren<Transform>(false);
        foreach (var t in children)
        {
            if (t == wssVectorParent.transform) continue;
            var go = t.gameObject;
            if (vectorArrowPool.Contains(go)) continue;
            vectorArrowPool.Add(go);
        }

        if (vectorArrowPool.Count > 0)
        {
            Debug.Log($"<color=green>Seeded WSS vector pool from existing children: {vectorArrowPool.Count}</color>");
        }
        hasSeededVectorPool = true;
    }

    [ContextMenu("Generate Pool In Editor")]
    void GeneratePoolInEditor()
    {
        if (minPoolSize <= 0)
        {
            Debug.LogWarning("Please set Min Pool Size > 0 first.");
            return;
        }

        Debug.Log($"Generating {minPoolSize} arrows in Editor...");
        InitializeVectorArrowPool(minPoolSize);
        Debug.Log("Generation complete. Don't forget to save the scene!");
    }

    GameObject CreateVectorArrow()
    {
        GameObject arrow;
        
        if (arrowPrefab != null)
        {
            arrow = Instantiate(arrowPrefab, wssVectorParent.transform);
        }
        else
        {
            // Fallback: Create simple arrow from primitives
            arrow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arrow.transform.SetParent(wssVectorParent.transform, false);
            
            // Remove collider
            Collider col = arrow.GetComponent<Collider>();
            if (col != null) Destroy(col);
            
            // Adjust primitive cylinder (default height 2, y-up) to point forward if needed or just be a marker
            // VelocityLoader arrows usually point Z-forward. Cylinder is Y-up.
            // We might need to rotate it inside a parent or just use it as is.
            // For now, simple fallback.
        }

        return arrow;
    }

    void DisplayVectorFrame(int frameIndex)
    {
        if (!showWSSVectors) return;
        if (frameIndex < 0 || frameIndex >= loadedVectorFrames.Count) return;

        currentVectorFrameIndex = frameIndex;
        
        // GPU Instancing mode - just update frame index, rendering happens in Update()
        if (useGPUInstancing && instancedMaterial != null && arrowMesh != null)
        {
            // Don't render here - Update() handles GPU Instancing rendering
            return;
        }
        
        // Original GameObject Pool mode
        if (!isVectorPoolInitialized) return;

        WSSVectorData frame = loadedVectorFrames[frameIndex];
        int count = Mathf.Min(frame.positions.Count, vectorArrowPool.Count);

        int sX = Mathf.Max(1, stepX);
        int sY = Mathf.Max(1, stepY);
        int sZ = Mathf.Max(1, stepZ);

        for (int i = 0; i < count; i++)
        {
            GameObject arrow = vectorArrowPool[i];
            if (arrow == null) continue;

            bool isVisible = true;

            // Apply downsampling based on grid indices
            if (frame.gridIndices.Count > i)
            {
                Vector3Int idx = frame.gridIndices[i];
                if (idx.x % sX != 0 || idx.y % sY != 0 || idx.z % sZ != 0)
                {
                    isVisible = false;
                }
            }

            if (isVisible)
            {
                arrow.SetActive(true);
                UpdateVectorArrow(arrow, frame.positions[i], frame.vectors[i], frame.magnitudes[i], frame.colors[i]);
            }
            else
            {
                arrow.SetActive(false);
            }
        }

        // Hide unused arrows
        for (int i = count; i < vectorArrowPool.Count; i++)
        {
            if (vectorArrowPool[i] != null)
            {
                vectorArrowPool[i].SetActive(false);
            }
        }
    }
    
    /// <summary>
    /// GPU Instanced rendering for WSS vectors - much faster for large numbers of arrows
    /// </summary>
    void DisplayVectorFrameInstanced(int frameIndex)
    {
        WSSVectorData frame = loadedVectorFrames[frameIndex];
        
        int sX = Mathf.Max(1, stepX);
        int sY = Mathf.Max(1, stepY);
        int sZ = Mathf.Max(1, stepZ);
        
        // Hide pooled objects only once (not every frame)
        if (vectorArrowPool.Count > 0 && vectorArrowPool[0] != null && vectorArrowPool[0].activeSelf)
        {
            foreach (var obj in vectorArrowPool)
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
        int batchIndex = 0;
        
        Vector3 parentScale = wssVectorParent != null ? wssVectorParent.transform.lossyScale : Vector3.one;
        
        for (int i = 0; i < frame.positions.Count; i++)
        {
            bool isVisible = true;
            if (frame.gridIndices.Count > i)
            {
                Vector3Int idx = frame.gridIndices[i];
                if (idx.x % sX != 0 || idx.y % sY != 0 || idx.z % sZ != 0)
                {
                    isVisible = false;
                }
            }
            
            if (!isVisible) continue;
            
            Vector3 pos = frame.positions[i];
            Vector3 vec = frame.vectors[i];
            float mag = frame.magnitudes[i];
            Color col = frame.colors[i];
            
            // Calculate rotation (use Vector3.forward like original UpdateVectorArrow)
            Quaternion rot = vec.sqrMagnitude > 0.0001f 
                ? Quaternion.LookRotation(vec.normalized, Vector3.forward)
                : Quaternion.identity;
            
            // Calculate scale
            float length = mag * arrowLengthMultiplier;
            Vector3 scale = Vector3.Scale(new Vector3(arrowScale, arrowScale, length), parentScale);
            
            // Apply parent transform
            Vector3 worldPos = wssVectorParent != null 
                ? wssVectorParent.transform.TransformPoint(pos) 
                : pos;
            Quaternion worldRot = wssVectorParent != null 
                ? wssVectorParent.transform.rotation * rot 
                : rot;
            
            // Add to batch array
            instanceMatrices[batchIndex] = Matrix4x4.TRS(worldPos, worldRot, scale);
            instanceColors[batchIndex] = new Vector4(col.r, col.g, col.b, col.a);
            batchIndex++;
            
            // Draw when batch is full
            if (batchIndex >= MAX_INSTANCES_PER_BATCH)
            {
                instanceMPB.SetVectorArray("_Color", instanceColors);
                Graphics.DrawMeshInstanced(arrowMesh, 0, instancedMaterial, instanceMatrices, batchIndex, instanceMPB);
                batchIndex = 0;
            }
        }
        
        // Draw remaining instances
        if (batchIndex > 0)
        {
            instanceMPB.SetVectorArray("_Color", instanceColors);
            Graphics.DrawMeshInstanced(arrowMesh, 0, instancedMaterial, instanceMatrices, batchIndex, instanceMPB);
        }
    }

    void UpdateVectorArrow(GameObject arrow, Vector3 position, Vector3 vector, float magnitude, Color color)
    {
        arrow.transform.localPosition = position;
        
        // Orient arrow along vector direction
        if (vector.magnitude > 0.0001f)
        {
            arrow.transform.localRotation = Quaternion.LookRotation(vector.normalized, Vector3.forward);
        }
        
        // Scale arrow based on magnitude
        float length = magnitude * arrowLengthMultiplier;
        arrow.transform.localScale = new Vector3(arrowScale, arrowScale, length);
        
        // Apply color
        Renderer renderer = arrow.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = color;
        }
    }

    public void SetWSSVectorVisibility(bool visible)
    {
        showWSSVectors = visible;
        
        if (wssVectorParent != null)
        {
            wssVectorParent.SetActive(visible);
        }

        if (visible && isVectorPoolInitialized && loadedVectorFrames.Count > 0)
        {
            DisplayVectorFrame(currentVectorFrameIndex);
        }
    }

}
