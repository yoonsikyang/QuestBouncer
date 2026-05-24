using UnityEngine;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Renders 2D heatmap and 3D arrow visualizations for slice data
/// Works alongside SliceDataManager
/// </summary>
public class SliceViewRenderer : MonoBehaviour
{
    [Header("References")]
    public SliceDataManager dataManager;
    public VelocityLoader velocityLoader;
    
    [Header("2D Heatmap")]
    public bool show2DHeatmap = false;
    public GameObject heatmapParent;
    [Range(16, 256)] public int heatmapResolution = 256;
    [Range(0f, 3f)] public float heatmapIntensity = 1.35f;
    [Range(0f, 1f)] public float heatmapAlpha = 0.9f;
    [Range(0.001f, 0.2f)] public float heatmapSpotSize = 0.04f;
    public Material heatmapBaseMaterial;
    public float targetPhysicalSize = 0.5f; // Target max dimension in meters
    public float arrowPlaneScale = 1.0f; // Removed Range constraint to allow values < 0.1 from JSON

    [Header("Interaction Collider")]
    [Tooltip("Multiplier applied so the interaction collider fully covers the visible blue panel")]
    public float interactionColliderPadding = 1.2f;
    [Tooltip("Depth of the interaction collider in local space")]
    public float interactionColliderDepth = 0.12f;
    
    [Header("3D Arrows")]
    public bool show3DArrows = false;
    public GameObject arrowSliceParent;
    public GameObject arrowPrefab;
    public float arrowScale = 0.005f;
    public float velocityScaleFactor = 0.005f;
    
    [Header("GPU Instancing")]
    [Tooltip("Enable GPU instancing for arrow rendering (recommended)")]
    public bool useGPUInstancing = true;
    [Tooltip("Arrow mesh for GPU instancing")]
    public Mesh arrowMesh;
    [Tooltip("Instanced material for GPU rendering")]
    public Material instancedMaterial;
    
    [Header("ComputeBuffer Mode (Advanced)")]
    [Tooltip("Use ComputeShader for maximum performance")]
    public bool useComputeBuffer = false;
    public ComputeShader arrowComputeShader;
    public Material indirectMaterial;
    
    [Header("Colormap")]
    public Texture2D jetColormap;
    
    [Header("Rotation")]
    [Tooltip("Additional rotation applied to velocity vectors from JSON")]
    public Vector3 additionalRotation = Vector3.zero;
    [Tooltip("Manual correction for arrow facing direction (for debugging)")]
    public Vector3 arrowFacingCorrection = Vector3.zero;
    
    // Internal
    private GameObject heatmapQuad; 
    private Texture2D heatmapTexture;
    private Material heatmapMaterial;
    private List<GameObject> arrowObjects = new List<GameObject>();
    
    // Bounds tracking for 2D/3D synchronization
    private float lastMinX, lastMaxX, lastMinY, lastMaxY;
    private bool dataBoundsCalculated = false;
    
    // GPU Instancing (matching VelocityLoader)
    private const int MAX_INSTANCES_PER_BATCH = 1023;
    private Matrix4x4[] instanceMatrices;
    private Vector4[] instanceColors;
    private MaterialPropertyBlock instanceMPB;
    private Quaternion arrowBaseCorrection = Quaternion.identity;
    
    // ComputeBuffer resources
    private ComputeBuffer positionsBuffer;
    private ComputeBuffer velocitiesBuffer;
    private ComputeBuffer magnitudesBuffer;
    private ComputeBuffer colorsBuffer;
    private ComputeBuffer matricesBuffer;
    private ComputeBuffer outputColorsBuffer;
    private ComputeBuffer argsBuffer;
    private uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
    private int computeKernel;
    private bool computeBuffersInitialized = false;
    
    // Cached GPU arrow data (for continuous rendering every frame)
    private struct GPUArrowData
    {
        public Vector3 localPosition;
        public Quaternion rotation;
        public Vector3 scale;
        public Color color;
    }
    private List<GPUArrowData> cachedGPUArrowData = new List<GPUArrowData>();
    
    // JSON loading
    [System.Serializable]
    private class RotationVector
    {
        public float x;
        public float y;
        public float z;
    }
    
    [System.Serializable]
    private class VisualizationSettings
    {
        public RotationVector additionalRotation;
        public int heatmapResolution;
        public float heatmapIntensity;
        public float heatmapAlpha;
        public float heatmapSpotSize;
        public float arrowPlaneScale;
        public float sliceArrowScale;
        public float sliceVelocityScaleFactor;
        public float targetPhysicalSize;
        public float globalVisualizationScale;
    }
    
    void Awake()
    {
        if (heatmapParent == null)
        {
            heatmapParent = new GameObject("HeatmapParent");
            heatmapParent.transform.SetParent(transform, false);
        }
        
        // Set initial scale for 2D view
        if (heatmapParent != null)
        {
            heatmapParent.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);
        }
        
        if (arrowSliceParent == null)
        {
            arrowSliceParent = new GameObject("ArrowSliceParent");
            arrowSliceParent.transform.SetParent(transform, false);
        }
        
        // Set initial scale for 3D arrows to 1.0 (same as heatmap)
        if (arrowSliceParent != null)
        {
            arrowSliceParent.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);
        }
        
        if (jetColormap == null)
        {
            jetColormap = ColorMapUtility.GenerateJetColormap(256);
        }
        
        CreateHeatmapQuad();
        
        Debug.LogWarning($"<color=green>[SliceViewRenderer] Awake() Complete - heatmapQuad={heatmapQuad?.name}</color>");
        
        // Load additional rotation from JSON
        LoadRotationFromJSON();
    }
    
    void Start()
    {
        if (dataManager == null)
        {
            dataManager = GetComponent<SliceDataManager>();
        }
        
        if (velocityLoader == null)
        {
            velocityLoader = FindObjectOfType<VelocityLoader>();
        }
    }
    
    void Update()
    {
        // GPU instanced rendering must be called every frame to prevent flickering
        if (show3DArrows && useGPUInstancing && instancedMaterial != null && arrowMesh != null)
        {
            if (cachedGPUArrowData != null && cachedGPUArrowData.Count > 0)
            {
                RenderCachedGPUArrows();
            }
        }
    }
    
    void CreateHeatmapQuad()
    {
        if (heatmapParent == null) return;
        
        heatmapQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        heatmapQuad.name = "HeatmapQuad";
        heatmapQuad.transform.SetParent(heatmapParent.transform, false);
        heatmapQuad.transform.localPosition = Vector3.zero;
        
        if (heatmapBaseMaterial != null)
        {
            heatmapMaterial = new Material(heatmapBaseMaterial);
        }
        else
        {
            heatmapMaterial = new Material(Shader.Find("Unlit/Transparent"));
        }
        
        heatmapMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        heatmapQuad.GetComponent<Renderer>().material = heatmapMaterial;
        heatmapQuad.SetActive(false);
    }
    
    /// <summary>
    /// Updates the visualization based on current data
    /// </summary>
    public void UpdateVisualization()
    {
        if (dataManager == null) return;
        
        var sliceData = dataManager.GetSliceData();
        
        if (show2DHeatmap)
        {
            RenderHeatmap(sliceData);
        }
        
        if (show3DArrows)
        {
            RenderArrows(sliceData);
        }
        
        // Update visibility
        if (heatmapQuad != null)
        {
            heatmapQuad.SetActive(show2DHeatmap && sliceData.Count > 0);
        }
        
        foreach (var arrow in arrowObjects)
        {
            if (arrow != null)
            {
                arrow.SetActive(show3DArrows && sliceData.Count > 0);
            }
        }
    }
    
    void RenderHeatmap(List<SliceDataManager.SliceDataPoint> sliceData)
    {
        if (heatmapQuad == null || velocityLoader == null)
        {
            return;
        }
        
        if (sliceData.Count == 0)
        {
            heatmapQuad.SetActive(false);
            return;
        }
        
        var dataBounds = dataManager.GetDataBounds();
        var axis = dataManager.currentAxis;
        
        // Calculate texture dimensions based on aspect ratio
        float width, height;
        if (axis == SliceController.SliceAxis.X_Axis)
        {
            width = dataBounds.size.z;
            height = dataBounds.size.y;
        }
        else // Y_Axis
        {
            width = dataBounds.size.z;
            height = dataBounds.size.x;
        }
        
        float aspect = width / Mathf.Max(0.0001f, height);
        int texWidth, texHeight;
        
        if (aspect >= 1f)
        {
            texWidth = heatmapResolution;
            texHeight = Mathf.Max(16, Mathf.RoundToInt(heatmapResolution / aspect));
        }
        else
        {
            texHeight = heatmapResolution;
            texWidth = Mathf.Max(16, Mathf.RoundToInt(heatmapResolution * aspect));
        }
        
        texWidth = Mathf.Clamp(texWidth, 16, 1024);
        texHeight = Mathf.Clamp(texHeight, 16, 1024);
        
        // Create or resize texture
        if (heatmapTexture == null || heatmapTexture.width != texWidth || heatmapTexture.height != texHeight)
        {
            if (heatmapTexture != null) Destroy(heatmapTexture);
            heatmapTexture = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false);
            heatmapTexture.filterMode = FilterMode.Bilinear;
            heatmapTexture.wrapMode = TextureWrapMode.Clamp;
        }
        
        // Ensure texture is assigned to material instance
        if (heatmapQuad != null)
        {
            Renderer quadRenderer = heatmapQuad.GetComponent<Renderer>();
            Material mat = quadRenderer.material;
            if (mat.mainTexture != heatmapTexture) mat.mainTexture = heatmapTexture;
            
            // Explicitly set transparency modes for Unlit
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
        }
        
        // Render to texture
        RenderDataToTexture(sliceData, texWidth, texHeight, dataBounds);
        
        // Position quad (scale is set in RenderPlaneHeatmap to avoid conflict)
        // float scaleMultiplier = this.arrowPlaneScale > 0 ? this.arrowPlaneScale : 1.0f;
        // float heatmapScaleFactor = 100.0f;//(targetPhysicalSize / Mathf.Max(0.01f, Mathf.Max(width, height))) * scaleMultiplier;
        // heatmapQuad.transform.localScale = new Vector3(width * heatmapScaleFactor, height * heatmapScaleFactor, 1f);
        
        // CENTER: Set localPosition to zero (centered on parent)
        heatmapQuad.transform.localPosition = Vector3.zero;
        heatmapQuad.transform.localRotation = Quaternion.identity;
        
        heatmapQuad.SetActive(true);
        
        // Update BoxCollider to match actual display size (will be set by RenderPlaneHeatmap)
        // UpdateColliderSize(width * heatmapScaleFactor, height * heatmapScaleFactor);
    }
    
    /// <summary>
    /// Updates the BoxCollider on the parent to match the visualization size
    /// </summary>
    void UpdateColliderSize(float displayWidth, float displayHeight)
    {
        // Get or add BoxCollider on the parent SliceController
        BoxCollider collider = GetComponentInParent<BoxCollider>();
        if (collider == null)
        {
            var parent = transform.parent;
            if (parent != null)
            {
                collider = parent.GetComponent<BoxCollider>();
                if (collider == null)
                {
                    collider = parent.gameObject.AddComponent<BoxCollider>();
                }
            }
        }
        
        if (collider != null)
        {
            // displayWidth and displayHeight are already in final display coordinates
            // No need to multiply by SliceController scale since we removed it
            float zDepth = Mathf.Max(displayWidth, displayHeight) * 0.1f;
            
            collider.size = new Vector3(displayWidth, displayHeight, zDepth);
            collider.center = Vector3.zero;
        }
    }
    
    void RenderDataToTexture(List<SliceDataManager.SliceDataPoint> sliceData, int texWidth, int texHeight, Bounds dataBounds)
    {
        System.Diagnostics.Stopwatch totalTimer = System.Diagnostics.Stopwatch.StartNew();
        System.Diagnostics.Stopwatch stepTimer = new System.Diagnostics.Stopwatch();
        
        // Step 1: Initialize arrays
        stepTimer.Start();
        Color[] pixels = new Color[texWidth * texHeight];
        float[] magAccum = new float[texWidth * texHeight];
        float[] weightAccum = new float[texWidth * texHeight];
        
        // Clear
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color(0, 0, 0, 0);
        }
        stepTimer.Stop();
        long initTime = stepTimer.ElapsedMilliseconds;
        
        if (velocityLoader == null) return;

        // --- Optimization: Dynamic Downsampling ---
        // If we have too many points, processing them all on CPU every frame causes stutter.
        // For a heatmap texture (e.g. 216x216), ~8000 points is more than enough.
        int maxPoints = 8000;
        int step = 1;
        if (sliceData.Count > maxPoints)
        {
            step = Mathf.Max(1, Mathf.RoundToInt((float)sliceData.Count / maxPoints));
            Debug.Log($"<color=orange>[Heatmap] Downsampling: {sliceData.Count} -> {sliceData.Count / step} points (Step: {step})</color>");
        }

        float minMag = velocityLoader.minVelocity;
        float maxMag = velocityLoader.maxVelocity;
        float magRange = Mathf.Max(0.0001f, maxMag - minMag);
        
        float splatRadius = Mathf.Max(1, heatmapSpotSize * Mathf.Max(texWidth, texHeight) * 0.5f);
        int iRadius = Mathf.CeilToInt(splatRadius);
        float sigma = splatRadius * 0.8f;
        float invTwoSigma2 = 1f / Mathf.Max(0.0001f, 2f * sigma * sigma);
        
        var axis = dataManager.currentAxis;

        // Pre-calculate Gaussian weights for the radius to avoid Mathf.Exp in nested loops
        int weightDim = iRadius * 2 + 1;
        float[,] weights = new float[weightDim, weightDim];
        for (int du = -iRadius; du <= iRadius; du++)
        {
            for (int dv = -iRadius; dv <= iRadius; dv++)
            {
                float dist2 = du * du + dv * dv;
                weights[du + iRadius, dv + iRadius] = Mathf.Exp(-dist2 * invTwoSigma2);
            }
        }
        
        // Step 2: Gaussian splatting (main bottleneck)
        stepTimer.Restart();
        for (int i = 0; i < sliceData.Count; i += step)
        {
            var point = sliceData[i];
            float uFloat, vFloat;
            
            if (axis == SliceController.SliceAxis.X_Axis)
            {
                float yu = (point.position.y - dataBounds.min.y) / dataBounds.size.y;
                float zv = (point.position.z - dataBounds.min.z) / dataBounds.size.z;
                uFloat = Mathf.Clamp01(zv) * (texWidth - 1);
                vFloat = Mathf.Clamp01(yu) * (texHeight - 1);
            }
            else // Y_Axis
            {
                float xu = (point.position.x - dataBounds.min.x) / dataBounds.size.x;
                float zv = (point.position.z - dataBounds.min.z) / dataBounds.size.z;
                uFloat = Mathf.Clamp01(zv) * (texWidth - 1);
                vFloat = Mathf.Clamp01(xu) * (texHeight - 1);
            }
            
            int uCenter = Mathf.RoundToInt(uFloat);
            int vCenter = Mathf.RoundToInt(vFloat);
            
            // Gaussian splat using pre-calculated weights
            for (int du = -iRadius; du <= iRadius; du++)
            {
                int uu = uCenter + du;
                if (uu < 0 || uu >= texWidth) continue;
                
                for (int dv = -iRadius; dv <= iRadius; dv++)
                {
                    int vv = vCenter + dv;
                    if (vv < 0 || vv >= texHeight) continue;
                    
                    float w = weights[du + iRadius, dv + iRadius];
                    int idx = vv * texWidth + uu;
                    magAccum[idx] += point.magnitude * w;
                    weightAccum[idx] += w;
                }
            }
        }
        stepTimer.Stop();
        long splatTime = stepTimer.ElapsedMilliseconds;
        
        // Step 3: Convert to colors
        stepTimer.Restart();
        for (int i = 0; i < pixels.Length; i++)
        {
            if (weightAccum[i] > 0.0001f)
            {
                float mag = magAccum[i] / weightAccum[i];
                float t = Mathf.Clamp01((mag - minMag) / magRange);
                
                Color c;
                if (jetColormap != null)
                {
                    c = jetColormap.GetPixelBilinear(t, 0.5f);
                }
                else
                {
                    c = Color.Lerp(Color.blue, Color.red, t);
                }
                
                // Apply intensity and alpha
                Color.RGBToHSV(c, out float h, out float s, out float v);
                v = Mathf.Clamp01(v * heatmapIntensity);
                c = Color.HSVToRGB(h, s, v);
                c.a = heatmapAlpha;
                pixels[i] = c;
            }
        }
        stepTimer.Stop();
        long colorTime = stepTimer.ElapsedMilliseconds;
        
        // Step 4: Apply to texture
        stepTimer.Restart();
        heatmapTexture.SetPixels(pixels);
        heatmapTexture.Apply();
        stepTimer.Stop();
        long applyTime = stepTimer.ElapsedMilliseconds;
        
        totalTimer.Stop();
        
        if (totalTimer.ElapsedMilliseconds > 0)
        {
             Debug.Log($"<color=magenta>[Heatmap] Total: {totalTimer.ElapsedMilliseconds}ms (Init: {initTime}, Splat: {splatTime}, Color: {colorTime}, Upload: {applyTime})</color>");
        }
    }
    
    void RenderArrows(List<SliceDataManager.SliceDataPoint> sliceData)
    {
        if (sliceData.Count == 0)
        {
            return;
        }
        
        // Use GPU instancing if enabled
        if (useGPUInstancing && instancedMaterial != null && arrowMesh != null)
        {
            RenderArrowsGPU(sliceData);
            return;
        }
        
        // Fallback: GameObject-based rendering
        RenderArrowsGameObject(sliceData);
    }
    
    /// <summary>
    /// GPU Instanced rendering - much faster for large numbers of arrows
    /// </summary>
    void RenderArrowsGPU(List<SliceDataManager.SliceDataPoint> sliceData)
    {
        var axis = dataManager.currentAxis;
        
        // Clear GameObject arrows if any exist (switching from GameObject to GPU mode)
        if (arrowObjects.Count > 0)
        {
            foreach (var arrow in arrowObjects)
            {
                if (arrow != null) Destroy(arrow);
            }
            arrowObjects.Clear();
        }
        
        // Clear and rebuild cache
        cachedGPUArrowData.Clear();
        
        // Track dataBounds for collider
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        
        // Calculate normalization factor to match heatmap size (with multiplier)
        var dataBounds = dataManager.GetDataBounds();
        float dataMaxDim = (axis == SliceController.SliceAxis.X_Axis) ? Mathf.Max(dataBounds.size.z, dataBounds.size.y) : Mathf.Max(dataBounds.size.z, dataBounds.size.x);
        float scaleMultiplierFactor = this.arrowPlaneScale > 0 ? this.arrowPlaneScale : 1.0f;
        float gpuArrowScaleFactor = (targetPhysicalSize / Mathf.Max(0.0001f, dataMaxDim)) * scaleMultiplierFactor;
        
        foreach (var point in sliceData)
        {
            // Map 3D position to 2D display position (normalized and CENTERED)
            Vector3 displayPos;
            float centerX = dataBounds.center.x;
            float centerY = dataBounds.center.y;
            float centerZ = dataBounds.center.z;
            
            if (axis == SliceController.SliceAxis.X_Axis)
            {
                displayPos = new Vector3((point.position.z - centerZ) * gpuArrowScaleFactor, (point.position.y - centerY) * gpuArrowScaleFactor, 0f);
            }
            else // Y_Axis
            {
                displayPos = new Vector3((point.position.z - centerZ) * gpuArrowScaleFactor, (point.position.x - centerX) * gpuArrowScaleFactor, 0f);
            }
            
            // Track dataBounds
            minX = Mathf.Min(minX, displayPos.x);
            maxX = Mathf.Max(maxX, displayPos.x);
            minY = Mathf.Min(minY, displayPos.y);
            maxY = Mathf.Max(maxY, displayPos.y);
            
            // Use LookRotation (matching ArrowCompute.compute shader)
            Quaternion rotation = Quaternion.identity;
            if (point.velocity.sqrMagnitude > 0.0001f)
            {
                // Base rotation from velocity direction
                // CRITICAL: ArrowCompute shader uses (0,0,1) as up! (line 74)
                Quaternion baseRot = Quaternion.LookRotation(
                    point.velocity.normalized, 
                    Vector3.forward  // up = (0, 0, 1)
                );
                
                // Apply rotations: additionalRotation FIRST, then base, then correction
                // (matching ArrowCompute.compute lines 161-162)
                Quaternion additionalRot = Quaternion.Euler(additionalRotation);
                rotation = additionalRot * baseRot * arrowBaseCorrection;
            }
            
            // Apply facing correction if needed
            if (arrowFacingCorrection != Vector3.zero)
            {
                Quaternion facingCorrection = Quaternion.Euler(arrowFacingCorrection);
                rotation = rotation * facingCorrection;
            }
            
            // Calculate scale (EXACTLY like VelocityLoader)
            float length = point.magnitude * velocityScaleFactor;
            length = Mathf.Max(length, 0.01f);
            Vector3 parentScale = arrowSliceParent != null ? arrowSliceParent.transform.lossyScale : Vector3.one;
            Vector3 scale = Vector3.Scale(new Vector3(arrowScale, arrowScale, length * 0.1f), parentScale);
            
            // Store as LOCAL position relative to THIS renderer (the panel)
            Vector3 visualLocalPos = displayPos;
            
            // Cache arrow data
            cachedGPUArrowData.Add(new GPUArrowData
            {
                localPosition = visualLocalPos,
                rotation = rotation,
                scale = scale,
                color = point.color
            });
        }
        
        // Update collider
        if (sliceData.Count > 0)
        {
            float width = (maxX - minX);
            float height = (maxY - minY);
            UpdateColliderSize(width, height);
        }
        
        // Render once
        RenderCachedGPUArrows();
    }
    
    /// <summary>
    /// Render cached GPU arrow data (called every frame in Update)
    /// </summary>
    void RenderCachedGPUArrows()
    {
        if (cachedGPUArrowData.Count == 0) return;
        
        // Initialize arrays once if needed
        if (instanceMatrices == null || instanceMatrices.Length != MAX_INSTANCES_PER_BATCH)
        {
            instanceMatrices = new Matrix4x4[MAX_INSTANCES_PER_BATCH];
            instanceColors = new Vector4[MAX_INSTANCES_PER_BATCH];
        }
        
        // Initialize property block once
        if (instanceMPB == null) 
        {
            instanceMPB = new MaterialPropertyBlock();
        }
        
        // Create transformation matrix for THIS renderer's local space to world
        Matrix4x4 localToWorld = transform.localToWorldMatrix;
        
        int batchIndex = 0;
        
        foreach (var arrow in cachedGPUArrowData)
        {
            // Add to batch
            // Matrix4x4.TRS takes world-relative TRS if localToWorld is identity, 
            // but since we multiply by localToWorld, we treat these as LOCAL TRS.
            Matrix4x4 localTRS = Matrix4x4.TRS(arrow.localPosition, arrow.rotation, arrow.scale);
            instanceMatrices[batchIndex] = localToWorld * localTRS;
            instanceColors[batchIndex] = new Vector4(arrow.color.r, arrow.color.g, arrow.color.b, arrow.color.a);
            batchIndex++;
            
            // Draw when batch is full
            if (batchIndex >= MAX_INSTANCES_PER_BATCH)
            {
                instanceMPB.SetVectorArray("_Color", instanceColors);
                Graphics.DrawMeshInstanced(arrowMesh, 0, instancedMaterial, instanceMatrices, batchIndex, instanceMPB);
                batchIndex = 0;
            }
        }
        
        // Draw remaining batch
        if (batchIndex > 0)
        {
            instanceMPB.SetVectorArray("_Color", instanceColors);
            Graphics.DrawMeshInstanced(arrowMesh, 0, instancedMaterial, instanceMatrices, batchIndex, instanceMPB);
        }
    }
    
    /// <summary>
    /// Fallback GameObject-based arrow rendering
    /// </summary>
    void RenderArrowsGameObject(List<SliceDataManager.SliceDataPoint> sliceData)
    {
        // Clear existing arrows
        foreach (var arrow in arrowObjects)
        {
            if (arrow != null) Destroy(arrow);
        }
        arrowObjects.Clear();
        
        if (arrowPrefab == null)
        {
            return;
        }
        
        var axis = dataManager.currentAxis;
        var dataBounds = dataManager.GetDataBounds();
        
        // Calculate dataBounds for collider update
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        
        Quaternion additionalRot = Quaternion.Euler(additionalRotation);
        
        // Calculate normalization factor to match heatmap size
        float dataMaxDim = (axis == SliceController.SliceAxis.X_Axis) ? Mathf.Max(dataBounds.size.z, dataBounds.size.y) : Mathf.Max(dataBounds.size.z, dataBounds.size.x);
        float goArrowScaleFactor = targetPhysicalSize / Mathf.Max(0.0001f, dataMaxDim);

        foreach (var point in sliceData)
        {
            GameObject arrow = Instantiate(arrowPrefab, arrowSliceParent.transform);
            
            // Map 3D position to 2D display position (normalized)
            Vector3 displayPos;
            if (axis == SliceController.SliceAxis.X_Axis)
            {
                displayPos = new Vector3(point.position.z * goArrowScaleFactor, point.position.y * goArrowScaleFactor, 0f);
            }
            else // Y_Axis
            {
                displayPos = new Vector3(point.position.z * goArrowScaleFactor, point.position.x * goArrowScaleFactor, 0f);
            }
            
            arrow.transform.localPosition = displayPos;
            
            // Track dataBounds
            minX = Mathf.Min(minX, displayPos.x);
            maxX = Mathf.Max(maxX, displayPos.x);
            minY = Mathf.Min(minY, displayPos.y);
            maxY = Mathf.Max(maxY, displayPos.y);
            
            // Use original 3D velocity for arrow direction (matching VelocityLoader behavior)
            // Position is projected to 2D slice plane, but velocity direction stays in 3D
            if (point.velocity != Vector3.zero)
            {
                Quaternion baseRot = Quaternion.LookRotation(point.velocity.normalized);
                // Apply additional rotation from JSON
                arrow.transform.localRotation = additionalRot * baseRot * arrowBaseCorrection;
            }
            
            float length = Mathf.Max(point.magnitude * velocityScaleFactor, 0.01f);
            arrow.transform.localScale = new Vector3(arrowScale, arrowScale, length * 0.1f);
            
            Renderer renderer = arrow.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = point.color;
            }
            
            arrowObjects.Add(arrow);
        }
        
        // Update collider to match arrow dataBounds (fixed size calculation)
        if (sliceData.Count > 0)
        {
            float width = (maxX - minX);
            float height = (maxY - minY);
            
            // Display size = dataBounds (no scaling needed)
            UpdateColliderSize(width, height);
        }
    }
    
    /// <summary>
    /// Load additionalRotation from visualization_settings.json (matching VelocityLoader behavior)
    /// Called on Awake and when data folder changes
    /// </summary>
    public void LoadRotationFromJSON()
    {
        // Don't check velocityLoader here - it may not be assigned yet during Awake()
        
        string dataFolderPath = Application.streamingAssetsPath + "/" + (Manager.Instance != null ? Manager.Instance.currentDataFolder : "");
        string settingsPath = Path.Combine(dataFolderPath, "visualization_settings.json");
        
        Debug.Log($"<color=cyan>[SliceViewRenderer] Attempting to load JSON from: {settingsPath}</color>");
        
        if (!File.Exists(settingsPath))
        {
            Debug.LogWarning($"[SliceViewRenderer] visualization_settings.json not found at {settingsPath}");
            return;
        }
        
        try
        {
            string jsonText = File.ReadAllText(settingsPath);
            var settings = JsonUtility.FromJson<VisualizationSettings>(jsonText);
            
            if (settings != null)
            {
                if (settings.additionalRotation != null)
                {
                    additionalRotation = new Vector3(
                        settings.additionalRotation.x,
                        settings.additionalRotation.y,
                        settings.additionalRotation.z
                    );
                }
                
                // Load updated visualization settings if strictly positive
                if (settings.heatmapResolution >= 16) heatmapResolution = settings.heatmapResolution;
                if (settings.heatmapIntensity > 0) heatmapIntensity = settings.heatmapIntensity;
                if (settings.heatmapAlpha > 0) heatmapAlpha = settings.heatmapAlpha;
                if (settings.heatmapSpotSize > 0) heatmapSpotSize = settings.heatmapSpotSize;
                if (settings.arrowPlaneScale > 0) this.arrowPlaneScale = settings.arrowPlaneScale;
                if (settings.sliceVelocityScaleFactor > 0) velocityScaleFactor = settings.sliceVelocityScaleFactor;
                if (settings.targetPhysicalSize > 0) targetPhysicalSize = settings.targetPhysicalSize;
                if (settings.sliceArrowScale > 0) arrowScale = settings.sliceArrowScale;
                
                // Load global visualization scale
                if (settings.globalVisualizationScale > 0)
                {
                    transform.localScale = Vector3.one * settings.globalVisualizationScale;
                    // Sync with SliceController if present
                    var controller = GetComponent<SliceController>();
                    if (controller != null) controller.globalVisualizationScale = settings.globalVisualizationScale;
                }
                
                Debug.Log($"<color=green>[SliceViewRenderer] ✓ JSON Loaded! arrowPlaneScale={this.arrowPlaneScale}, arrowScale={arrowScale}, velocityScaleFactor={velocityScaleFactor}, targetPhysicalSize={targetPhysicalSize}</color>");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SliceViewRenderer] Failed to load rotation from JSON: {e.Message}");
        }
    }

    /// <summary>
    /// Render slice data from arbitrary plane (for SlicePlaneController)
    /// </summary>
    public void RenderPlaneSlice(List<SliceDataManager.SliceDataPoint> sliceData, Transform planeTransform)
    {
        
        if (sliceData == null || sliceData.Count == 0)
        {
            // Hide visualization when no data
            if (heatmapQuad != null) heatmapQuad.SetActive(false);
            if (arrowSliceParent != null) arrowSliceParent.SetActive(false);
            cachedGPUArrowData.Clear(); // Clear GPU data to remove arrows
            return;
        }
        
        // Ensure arrow parent is active when we have data
        if (arrowSliceParent != null) arrowSliceParent.SetActive(true);
        cachedGPUArrowData.Clear(); // Clear previous frame's data
        
        // 0. Coordinate Space Correction: Use Data Anchor's space for consistency
        Transform dataAnchor = (velocityLoader != null && velocityLoader.velocityParent != null) 
            ? velocityLoader.velocityParent.transform 
            : null;
            
        Vector3 localOrigin, localRight, localUp;
        if (dataAnchor != null)
        {
            localOrigin = dataAnchor.InverseTransformPoint(planeTransform.position);
            localRight = dataAnchor.InverseTransformDirection(planeTransform.right).normalized;
            localUp = dataAnchor.InverseTransformDirection(planeTransform.up).normalized;
        }
        else
        {
            localOrigin = planeTransform.position;
            localRight = planeTransform.right;
            localUp = planeTransform.up;
        }

        // 1. Calculate Bounds first to ensure scale normalization matches heatmap
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        
        // Cache local points to avoid double InverseTransform
        List<Vector3> localPoints = new List<Vector3>(sliceData.Count);
        
        foreach (var p in sliceData)
        {
            Vector3 posInAnchor = (dataAnchor != null) ? dataAnchor.InverseTransformPoint(p.position) : p.position;
            localPoints.Add(posInAnchor);
            
            Vector3 offset = posInAnchor - localOrigin;
            float px = Vector3.Dot(offset, localRight);
            float py = Vector3.Dot(offset, localUp);
            if (px < minX) minX = px; if (px > maxX) maxX = px;
            if (py < minY) minY = py; if (py > maxY) maxY = py;
        }
        

        
        // Add padding to bounds for better framing
        float width = maxX - minX;
        float height = maxY - minY;
        if (width < 0.001f) width = 0.1f;
        if (height < 0.001f) height = 0.1f;
        
        float centerX = (minX + maxX) * 0.5f;
        float centerY = (minY + maxY) * 0.5f;
        
        float dataW = width;
        float dataH = height;
        
        // Auto-scale to fit within BoxCollider bounds
        float planeSliceScaleFactor;
        
        // Try to get BoxCollider size from SliceController
        var sliceController = GetComponent<SliceController>();
        BoxCollider boxCollider = sliceController != null ? sliceController.GetComponent<BoxCollider>() : null;
        
        if (boxCollider != null)
        {
            // Use actual box size for auto-fitting
            // BoxCollider.size gives the local size, we need to account for scale
            Vector3 boxSize = boxCollider.size;
            float boxWidth = boxSize.x;
            float boxHeight = boxSize.y;
            
            // Calculate scale factor to fit data within box (with small margin)
            float margin = 0.9f; // 90% of box size to leave some padding
            float scaleByWidth = (boxWidth * margin) / dataW;
            float scaleByHeight = (boxHeight * margin) / dataH;
            
            // Use smaller scale to ensure it fits both dimensions
            planeSliceScaleFactor = Mathf.Min(scaleByWidth, scaleByHeight);
            
            //Debug.Log($"<color=cyan>[SliceViewRenderer] Auto-scaled to fit box: boxSize={boxSize}, dataSize=({dataW}, {dataH}), scaleFactor={planeSliceScaleFactor}</color>");
        }
        else
        {
            // Fallback: Use original calculation
            planeSliceScaleFactor = (targetPhysicalSize / Mathf.Max(dataW, dataH)) * this.arrowPlaneScale;
            Debug.LogWarning("[SliceViewRenderer] BoxCollider not found, using fallback scale calculation");
        }

        for (int i = 0; i < sliceData.Count; i++)
        {
            var point = sliceData[i];
            Vector3 posInAnchor = localPoints[i];
            
            Vector3 localPosOnPlane = posInAnchor - localOrigin;
            float x = Vector3.Dot(localPosOnPlane, localRight);
            float y = Vector3.Dot(localPosOnPlane, localUp);
            
            // CENTERING: Subtract bounds center to keep it fixed in the UI panel
            float targetX = (x - centerX);
            float targetY = (y - centerY);
            
            // Calculate plane's local orientation from localRight and localUp
            // Create a rotation that aligns the plane's coordinate system
            Vector3 planeNormal = Vector3.Cross(localRight, localUp).normalized;
            Quaternion planeRotation = Quaternion.LookRotation(planeNormal, localUp);
            
            Quaternion rotation = Quaternion.identity;
            if (point.velocity.sqrMagnitude > 0.0001f)
            {
                // Transform velocity to plane's local space
                Vector3 velocityInPlane = Quaternion.Inverse(planeRotation) * point.velocity;
                
                Quaternion baseRot = Quaternion.LookRotation(velocityInPlane.normalized, Vector3.forward);
                Quaternion additionalRot = Quaternion.Euler(additionalRotation);
                rotation = additionalRot * baseRot * arrowBaseCorrection;
            }
            
            float length = Mathf.Max(point.magnitude * velocityScaleFactor, 0.01f);
            
            // PIVOT CENTERING: Use centered x and y
            Vector3 visualLocalPos = new Vector3(targetX * planeSliceScaleFactor, targetY * planeSliceScaleFactor, 0f);
            
            // Scale
            Vector3 parentScale = arrowSliceParent != null ? arrowSliceParent.transform.lossyScale : Vector3.one;
            Vector3 scale = Vector3.Scale(new Vector3(arrowScale, arrowScale, length * 0.1f), parentScale);
            
            // Store as LOCAL position relative to THIS renderer (the panel)
            // This is already in "visualLocalPos" space which is relative to the panel's origin
            
            cachedGPUArrowData.Add(new GPUArrowData
            {
                localPosition = visualLocalPos,
                rotation = rotation,
                scale = scale,
                color = point.color
            });
        }
        
        // Also render 2D heatmap for the plane (Pass all bounds and spaces for alignment)
        RenderPlaneHeatmap(sliceData, localPoints, localOrigin, localRight, localUp, minX, maxX, minY, maxY);
    }
    
    void RenderPlaneHeatmap(List<SliceDataManager.SliceDataPoint> sliceData, List<Vector3> localPoints,
        Vector3 localOrigin, Vector3 localRight, Vector3 localUp, 
        float minX, float maxX, float minY, float maxY)
    {
        
        if (heatmapQuad == null || velocityLoader == null) return;
        
        if (sliceData == null || sliceData.Count == 0)
        {
            heatmapQuad.SetActive(false);
            return;
        }
        
        // 1. Calculate Bounds of data on the plane (Local X, Y)
        // Match centering used in arrows
        float centerX = (minX + maxX) * 0.5f;
        float centerY = (minY + maxY) * 0.5f;
        
        // Cache local positions relative to CENTER to avoid recalculating dot products
        Vector2[] localPositions = new Vector2[sliceData.Count];
        
        for (int i = 0; i < sliceData.Count; i++)
        {
            Vector3 worldOffset = localPoints[i] - localOrigin;
            float x = Vector3.Dot(worldOffset, localRight);
            float y = Vector3.Dot(worldOffset, localUp);
            
            localPositions[i] = new Vector2(x, y);
        }
        
        // Cache for RenderPlaneSlice (arrows)
        lastMinX = minX; lastMaxX = maxX;
        lastMinY = minY; lastMaxY = maxY;
        dataBoundsCalculated = true;
        
        // Add padding (5%)
        float width = maxX - minX;
        float height = maxY - minY;
        
        // Prevent zero size
        if (width < 0.001f) width = 0.1f;
        if (height < 0.001f) height = 0.1f;
        
        float paddingX = width * 0.05f;
        float paddingY = height * 0.05f;
        
        minX -= paddingX; maxX += paddingX;
        minY -= paddingY; maxY += paddingY;
        width = maxX - minX;
        
        // 2. Adjust Aspect Ratio of Texture and Quad
        int resW = heatmapResolution;
        int resH = heatmapResolution;
        
        float aspect = width / height;
        if (aspect >= 1f) resH = Mathf.Max(16, Mathf.RoundToInt(heatmapResolution / aspect));
        else resW = Mathf.Max(16, Mathf.RoundToInt(heatmapResolution * aspect));
        
        resW = Mathf.Clamp(resW, 16, 1024);
        resH = Mathf.Clamp(resH, 16, 1024);
        
        // Create or resize texture
        if (heatmapTexture == null || heatmapTexture.width != resW || heatmapTexture.height != resH)
        {
            if (heatmapTexture != null) Destroy(heatmapTexture);
            heatmapTexture = new Texture2D(resW, resH, TextureFormat.RGBA32, false);
            heatmapTexture.filterMode = FilterMode.Bilinear;
            heatmapTexture.wrapMode = TextureWrapMode.Clamp;
        }
        
        // 3. Splatting
        Color[] pixels = new Color[resW * resH];
        float[] magAccum = new float[resW * resH];
        float[] weightAccum = new float[resW * resH];
        
        System.Array.Clear(pixels, 0, pixels.Length);
        
        float minMag = velocityLoader.minVelocity;
        float maxMag = velocityLoader.maxVelocity;
        float magRange = Mathf.Max(0.0001f, maxMag - minMag);
        
        float splatRadius = heatmapSpotSize * Mathf.Max(resW, resH) * 0.8f;
        if (splatRadius < 0.5f) splatRadius = 0.5f; // Minimum 0.5 for some visibility
        float sigma = splatRadius * 0.5f;
        float invTwoSigma2 = 1f / Mathf.Max(0.0001f, 2f * sigma * sigma);
        
        for (int i = 0; i < sliceData.Count; i++)
        {
            Vector2 localPos = localPositions[i];
            
            float uFloat = (localPos.x - minX) / width; 
            float vFloat = (localPos.y - minY) / height; 
            
            float uPixel = uFloat * (resW - 1);
            float vPixel = vFloat * (resH - 1);
            
            int uCenter = Mathf.FloorToInt(uPixel);
            int vCenter = Mathf.FloorToInt(vPixel);
            int rad = Mathf.CeilToInt(splatRadius);
            
            for (int du = -rad; du <= rad; du++)
            {
                int uu = uCenter + du;
                if (uu < 0 || uu >= resW) continue;
                for (int dv = -rad; dv <= rad; dv++)
                {
                    int vv = vCenter + dv;
                    if (vv < 0 || vv >= resH) continue;
                    
                    float dist2 = du * du + dv * dv;
                    if (dist2 > splatRadius * splatRadius) continue;
                    
                    float w = Mathf.Exp(-dist2 * invTwoSigma2);
                    int idx = vv * resW + uu;
                    magAccum[idx] += sliceData[i].magnitude * w;
                    weightAccum[idx] += w;
                }
            }
        }
        
        // Convert to colors
        for (int i = 0; i < pixels.Length; i++)
        {
            if (weightAccum[i] > 0.0001f)
            {
                float mag = magAccum[i] / weightAccum[i];
                float t = Mathf.Clamp01((mag - minMag) / magRange);
                
                Color c;
                if (jetColormap != null) c = jetColormap.GetPixelBilinear(t, 0.5f);
                else c = Color.Lerp(Color.blue, Color.red, t);
                
                Color.RGBToHSV(c, out float h, out float s, out float v);
                v = Mathf.Clamp01(v * heatmapIntensity);
                c = Color.HSVToRGB(h, s, v);
                c.a = heatmapAlpha;
                pixels[i] = c;
            }
            else
            {
                pixels[i] = new Color(0, 0, 0, 0); // Transparent
            }
        }
        
        heatmapTexture.SetPixels(pixels);
        heatmapTexture.Apply();

        // 4. Update Quad Transform and BoxCollider
        if (heatmapQuad != null)
        {
            // Honors visibility flag
            heatmapQuad.SetActive(show2DHeatmap && sliceData.Count > 0);
            
            // USE sharedMaterial or a single instance to avoid leaking/misassignment
            Renderer quadRenderer = heatmapQuad.GetComponent<Renderer>();
            Material mat = quadRenderer.material; // Gets instance
            
            if (mat.mainTexture != heatmapTexture) mat.mainTexture = heatmapTexture;
            mat.SetFloat("_Alpha", heatmapAlpha);
            mat.SetFloat("_Intensity", heatmapIntensity);

            // Explicitly set transparency modes for Unlit
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;

            heatmapQuad.transform.localPosition = Vector3.zero;
            heatmapQuad.transform.localRotation = Quaternion.identity;

            // Heatmap uses unified scale: targetPhysicalSize * arrowPlaneScale (consistent with RenderHeatmapView)
            float scaleMultiplier = this.arrowPlaneScale > 0 ? this.arrowPlaneScale : 1.0f;
            float heatmapScaleFactor = 0.1f;//(targetPhysicalSize / Mathf.Max(0.01f, Mathf.Max(width, height))) * scaleMultiplier;
            float finalW = width * heatmapScaleFactor;
            float finalH = height * heatmapScaleFactor;
            
            heatmapQuad.transform.localScale = new Vector3(finalW, finalH, 1f);

            var sliceController = GetComponent<SliceController>();
            BoxCollider manipCollider = sliceController != null ? sliceController.GetComponent<BoxCollider>() : null;
            if (manipCollider == null && sliceController != null) 
                manipCollider = sliceController.gameObject.AddComponent<BoxCollider>();

            if (manipCollider != null)
            {
                // Make the collider slightly larger than the visible panel so the whole blue area is grabbable.
                manipCollider.size = new Vector3(
                    finalW * interactionColliderPadding,
                    finalH * interactionColliderPadding,
                    interactionColliderDepth
                );
                manipCollider.center = Vector3.zero;
            }
        }
    }

    void OnDestroy()
    {
        if (heatmapTexture != null)
        {
            Destroy(heatmapTexture);
        }
        
        if (heatmapMaterial != null)
        {
            Destroy(heatmapMaterial);
        }
        
        // Clean up GPU instancing resources
        cachedGPUArrowData?.Clear();
        instanceMatrices = null;
        instanceColors = null;
        instanceMPB = null;
        
        // Clean up GameObject arrows
        foreach (var arrow in arrowObjects)
        {
            if (arrow != null) Destroy(arrow);
        }
        arrowObjects.Clear();
    }

}
