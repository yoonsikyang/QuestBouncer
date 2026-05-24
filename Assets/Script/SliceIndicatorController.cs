// Force recompile - updated debug logs
using UnityEngine;
using Microsoft.MixedReality.Toolkit.UI;
using Microsoft.MixedReality.Toolkit.Input;

/// <summary>
/// Controls the visual indicator cube that shows the current slice position on the 3D vessel
/// </summary>
public class SliceIndicatorController : MonoBehaviour
{
    [Header("Indicator Settings")]
    [Tooltip("Show/hide the slice indicator")]
    public bool showIndicator = false;
    
    [Tooltip("Size of the indicator cube")]
    public Vector3 indicatorSize = new Vector3(0.02f, 0.02f, 0.02f);
    
    [Tooltip("Color of the indicator")]
    public Color indicatorColor = Color.cyan;
    
    [Range(0f, 1f)]
    [Tooltip("Transparency of the indicator")]
    public float indicatorAlpha = 0.5f;
    
    [Tooltip("Material for the indicator (optional)")]
    public Material indicatorMaterial;
    
    [Header("Knife Settings")]
    [Tooltip("Enable Knife Mode (Interactive Plane)")]
    public bool enableKnifeMode = true; // Default to true

    [Header("Knife Interaction")]
    [Tooltip("World-space width/height of the invisible grab area for the knife")]
    public float knifeGrabSize = 0.45f;
    [Tooltip("World-space thickness of the invisible grab area for the knife")]
    public float knifeGrabDepth = 0.12f;
    
    [Tooltip("Thickness of the slice (captures data within this range)")]
    [Range(0.001f, 0.3f)]
    public float sliceThickness = 0.15f; // 15cm thickness to capture more data

    [Tooltip("How many times per second to update the slice data and heatmap")]
    public float updateFrequency = 3f;
    private float lastUpdateTime = 0f;
    private int lastKnifeUpdateFrame = -1; // Track last frame UpdateKnifeSlice was called
    
    [Header("Highlight Settings")]
    [Tooltip("Enable red intersection highlight on the vessel")]
    public bool enableIntersectionHighlight = true;
    public float highlightThickness = 0.005f;
    public Color highlightColor = Color.red;
    public Shader intersectionShader; // Exposed for building
    public Shader stencilWriterShader; // Exposed for building

    [Header("References")]
    [Tooltip("Parent object to attach the indicator to (usually ObjectParent)")]
    public Transform parentTransform;
    
    [Tooltip("Reference to SliceController (set by ButtonControllerManager)")]
    public SliceController sliceControllerRef;
    
    // Internal
    public GameObject indicatorCube;
    private Renderer indicatorRenderer;
    private bool needsDataCentering = false;
    
    private Renderer cachedVesselRenderer;
    private Material cachedVesselMaterial;
    
    void OnEnable()
    {
        // Safety: Try to auto-find parent logic from SliceController if available
        if (parentTransform == null || parentTransform.GetComponent<Collider>() == null && parentTransform.GetComponentInChildren<Collider>() == null)
        {
             var sc = GetComponent<SliceController>();
             if (sc == null) sc = FindObjectOfType<SliceController>(); // Fallback search
             
             if (sc != null && sc.visualsParent != null)
             {
                 parentTransform = sc.visualsParent.transform;
                 Debug.Log($"<color=cyan>[SliceIndicatorController] Auto-assigned Parent Transform to: {parentTransform.name}</color>");
             }
        }

        if (showIndicator && indicatorCube == null)
        {
            CreateIndicator();
        }
        else if (indicatorCube != null)
        {
            indicatorCube.SetActive(showIndicator);
        }
    }
    
    void OnDisable()
    {
        if (indicatorCube != null)
        {
            indicatorCube.SetActive(false);
        }
    }
    
    /// <summary>
    /// Creates the indicator GameObject
    /// </summary>
    /// <summary>
    /// Creates or configures the indicator
    /// </summary>
    void CreateIndicator()
    {
        if (parentTransform == null)
        {
            // Optional: Auto-search parent logic is already in OnEnable, but for safety:
            var sc = sliceControllerRef != null ? sliceControllerRef : GetComponent<SliceController>();
            if (sc == null) sc = FindObjectOfType<SliceController>();
            if (sc != null && sc.visualsParent != null) parentTransform = sc.visualsParent.transform;
        }
        
        // --- CASE 1: KNIFE MODE ---
        if (enableKnifeMode)
        {
            indicatorCube = this.gameObject;
            
            // 1. Position and Scale
            var sliceController = sliceControllerRef != null ? sliceControllerRef : GetComponent<SliceController>();
            if (sliceController == null) sliceController = FindObjectOfType<SliceController>();
            
            GameObject targetObj = null;
            if (sliceController != null && sliceController.velocityLoader != null)
            {
                // Use velocityParent as the visual target for centering, as it contains the data
                targetObj = (sliceController.velocityLoader.velocityParent != null) ? sliceController.velocityLoader.velocityParent : sliceController.velocityLoader.gameObject;
            }
            if (targetObj == null)
            {
                targetObj = GameObject.Find("Object Parent");
            }

            if (targetObj != null)
            {
                // Force scale update immediately
                UpdateKnifeScale();
                // Set initial position to target center
                var dm = sliceController != null ? sliceController.dataManager : null;
                if (dm != null)
                {
                    // CRITICAL: GetDataBounds already returns bounds in VelocityParent's local space
                    // So we must TransformPoint to world space for the SliceIndicatorController's world position
                    var bounds = dm.GetDataBounds();
                    Vector3 worldCenter = targetObj.transform.TransformPoint(bounds.center);
                    transform.position = worldCenter;

                    ButtonControllerManager exhibitionButtonController = FindObjectOfType<ButtonControllerManager>();
                    bool keepCenteredForExhibition =
                        exhibitionButtonController != null &&
                        exhibitionButtonController.IsExhibitionModeActive();

                    if (!keepCenteredForExhibition && parentTransform != null)
                    {
                        transform.position = parentTransform.position;
                    }
                    
                    // Mark if we successfully centered (bounds.size > 0 means data is loaded)
                    if (bounds.size.magnitude > 0.5f)
                    {
                        needsDataCentering = false;
                        Debug.Log($"<color=cyan>[Knife] Initialized at center: {worldCenter} (Local: {bounds.center}) on {targetObj.name}</color>");
                    }
                    else
                    {
                        needsDataCentering = true;
                        Debug.LogWarning("<color=yellow>[Knife] Data not loaded yet, will re-center when available</color>");
                    }
                }
                else
                {
                    transform.position = targetObj.transform.position;
                    needsDataCentering = true;
                }
                transform.rotation = targetObj.transform.rotation;
                
            }
            
            // 2. Interaction
            if (GetComponent<ObjectManipulator>() == null)
            {
                var manipulator = gameObject.AddComponent<ObjectManipulator>();
                manipulator.SmoothingActive = true;
                manipulator.HostTransform = transform;
                
                // Add listeners for network sync
                manipulator.OnManipulationStarted.AddListener(OnManipulationStarted);
                manipulator.OnManipulationEnded.AddListener(OnManipulationEnded);
            }
            if (GetComponent<NearInteractionGrabbable>() == null) gameObject.AddComponent<NearInteractionGrabbable>();
            
            // 3. Collider
            var collider = GetComponent<BoxCollider>();
            if (collider == null) collider = gameObject.AddComponent<BoxCollider>();
            UpdateKnifeCollider(collider);
            collider.center = Vector3.zero;
            collider.isTrigger = false;
            
            // 4. Renderer and Mesh
            indicatorRenderer = GetComponent<Renderer>();
            if (indicatorRenderer == null)
            {
                 var filter = gameObject.GetComponent<MeshFilter>();
                 if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
                 if (filter.sharedMesh == null)
                 {
                     GameObject tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                     filter.sharedMesh = tempCube.GetComponent<MeshFilter>().sharedMesh;
                     Destroy(tempCube);
                 }
                 indicatorRenderer = gameObject.AddComponent<MeshRenderer>();
                 Material mat;
                 if (indicatorMaterial != null) mat = new Material(indicatorMaterial);
                 else {
                    Shader shader = Shader.Find("GUI/3D Text Shader");
                    if (shader == null) shader = Shader.Find("Unlit/Transparent");
                    mat = new Material(shader);
                 }
                 indicatorRenderer.material = mat;
            }
            
            UpdateIndicatorColor();
            indicatorCube.SetActive(showIndicator);
            Debug.Log($"<color=green>[Knife] CreateIndicator: Setup as Knife on {targetObj?.name}</color>");
            return;
        }

        // --- CASE 2: LEGACY/AXIS MODE ---
        if (parentTransform == null) return;
        
        indicatorCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        indicatorCube.name = "SliceIndicator";
        indicatorCube.transform.SetParent(parentTransform, false);
        indicatorCube.transform.localScale = indicatorSize;
        
        indicatorRenderer = indicatorCube.GetComponent<Renderer>();
        if (indicatorRenderer != null)
        {
            Material mat;
            if (indicatorMaterial != null) mat = new Material(indicatorMaterial);
            else {
                Shader shader = Shader.Find("GUI/3D Text Shader");
                if (shader == null) shader = Shader.Find("Unlit/Transparent");
                mat = new Material(shader);
            }
            indicatorRenderer.material = mat;
            UpdateIndicatorColor();
        }
        indicatorCube.SetActive(showIndicator);
        Debug.Log("<color=green>[SliceIndicatorController] CreateIndicator: Setup Legacy</color>");
    }
    
    /// <summary>
    /// Updates the indicator position based on slice axis and position
    /// </summary>
    public void UpdateIndicatorPosition(SliceController.SliceAxis axis, float sliceCoord, Bounds bounds, bool networkCall = false)
    {
        if (networkCall) return;
        
        if (indicatorCube == null && showIndicator)
        { 
            CreateIndicator();
        }
        
        if (indicatorCube == null || !showIndicator)
        {
            return;
        }
        
        indicatorCube.SetActive(true);
        
        Vector3 center;
        Vector3 scale;
        float thickness = 0.001f;
        
        if (axis == SliceController.SliceAxis.X_Axis)
        {
            center = new Vector3(sliceCoord, bounds.center.y, bounds.center.z);
            scale = new Vector3(thickness, bounds.size.y, bounds.size.z);
        }
        else if (axis == SliceController.SliceAxis.Y_Axis)
        {
            center = new Vector3(bounds.center.x, sliceCoord, bounds.center.z);
            scale = new Vector3(bounds.size.x, thickness, bounds.size.z);
        }
        else // None
        {
            indicatorCube.SetActive(false);
            return;
        }
        
        indicatorCube.transform.localPosition = center;
        indicatorCube.transform.localScale = scale;
        indicatorCube.transform.localRotation = Quaternion.identity;
    }
    
    /// <summary>
    /// Updates the indicator color
    /// </summary>
    void UpdateIndicatorColor()
    {
        if (indicatorRenderer != null && indicatorRenderer.material != null)
        {
            Color c = indicatorColor;
            c.a = indicatorAlpha;
            indicatorRenderer.material.color = c;
        }
    }
    
    /// <summary>
    /// Shows or hides the indicator
    /// </summary>
    public void SetVisible(bool visible)
    {
        enableIntersectionHighlight = visible;
        showIndicator = visible;

        if (enableKnifeMode && indicatorCube == gameObject)
        {
            if (indicatorRenderer != null)
            {
                indicatorRenderer.enabled = visible;
            }

            Collider ownCollider = GetComponent<Collider>();
            if (ownCollider != null)
            {
                ownCollider.enabled = visible;
            }

            ObjectManipulator manipulator = GetComponent<ObjectManipulator>();
            if (manipulator != null)
            {
                manipulator.enabled = visible;
            }

            NearInteractionGrabbable grabbable = GetComponent<NearInteractionGrabbable>();
            if (grabbable != null)
            {
                grabbable.enabled = visible;
            }
        }

        // 2. Handle Feature Off (either disabled or indicator hidden)
        if (!enableIntersectionHighlight || !showIndicator)
        {
            // Revert to single material
            if (cachedVesselRenderer != null && cachedVesselRenderer.sharedMaterials.Length > 1)
            {
                cachedVesselRenderer.materials = new Material[] { cachedVesselRenderer.sharedMaterials[0] };
            }
            return;
        }

        // CRITICAL FIX: If making visible but indicator doesn't exist, create it
        if (visible && indicatorCube == null)
        {
            CreateIndicator();
        }
        
        if (indicatorCube != null)
        {
            indicatorCube.SetActive(visible);
        }
    }
    
    void OnDestroy()
    {
        if (indicatorCube != null)
        {
            Destroy(indicatorCube);
        }
    }

    public void ForceHideKnifeVisuals()
    {
        if (indicatorCube == null)
        {
            return;
        }

        if (indicatorCube == gameObject)
        {
            if (indicatorRenderer != null)
            {
                indicatorRenderer.enabled = false;
            }

            Collider ownCollider = GetComponent<Collider>();
            if (ownCollider != null)
            {
                ownCollider.enabled = false;
            }

            ObjectManipulator manipulator = GetComponent<ObjectManipulator>();
            if (manipulator != null)
            {
                manipulator.enabled = false;
            }

            NearInteractionGrabbable grabbable = GetComponent<NearInteractionGrabbable>();
            if (grabbable != null)
            {
                grabbable.enabled = false;
            }
        }
        else
        {
            indicatorCube.SetActive(false);
        }
    }
    
    void Update()
    {
        // Re-center knife if data wasn't available during initialization
        if (needsDataCentering && enableKnifeMode)
        {
            var sliceController = sliceControllerRef != null ? sliceControllerRef : GetComponent<SliceController>();
            if (sliceController == null) sliceController = FindObjectOfType<SliceController>();
            
            if (sliceController != null && sliceController.dataManager != null && sliceController.velocityLoader != null)
            {
                var bounds = sliceController.dataManager.GetDataBounds();
                if (bounds.size.magnitude > 0.5f) // Data is now loaded
                {
                    GameObject targetObj = sliceController.velocityLoader.velocityParent != null 
                        ? sliceController.velocityLoader.velocityParent 
                        : sliceController.velocityLoader.gameObject;
                    
                    if (targetObj != null)
                    {
                        //Vector3 worldCenter = targetObj.transform.TransformPoint(bounds.center);
                        //transform.position = worldCenter;
                        needsDataCentering = false;
                    }
                }
            }
        }
        
        if (enableKnifeMode)
        {
            // User requested NOT to be under Object Parent
            // So we remove the parent mismatch check that forces it back
            
            // Periodically ensure scale is correct (as constraints might fight it)
            //if (Time.frameCount % 60 == 0) UpdateKnifeScale();

            // Slicing is driven by this component in Knife Mode
            // Throttle update to reduce CPU load as per user request
            if (updateFrequency <= 0 || Time.time - lastUpdateTime >= (1f / updateFrequency))
            {
                UpdateKnifeSlice();
                lastUpdateTime = Time.time;
            }
        }

        // Always sync highlight to vessel mesh if enabled
        UpdateIntersectionHighlight();

        // Update color in real-time if inspector values change
        if (indicatorRenderer != null)
        {
            Color currentColor = indicatorRenderer.material.color;
            Color targetColor = indicatorColor;
            targetColor.a = indicatorAlpha;
            
            if (currentColor != targetColor)
            {
                UpdateIndicatorColor();
            }
        }
        
        // Update size in real-time if inspector values change
        if (indicatorCube != null && indicatorCube.transform.localScale != indicatorSize)
        {
            // Only update if not actively positioned by UpdateIndicatorPosition
            // This is a fallback for when indicator is not being used for slicing
        }
    }
    
    /// <summary>
    /// Update slice data based on the knife (indicator) plane position
    /// </summary>
    void UpdateKnifeSlice()
    {

        
        // Get SliceController reference
        var sliceController = sliceControllerRef != null ? sliceControllerRef : GetComponent<SliceController>();
        if (sliceController == null)
        {
            sliceController = FindObjectOfType<SliceController>();
        }
        
        // Debug: Check what's null
        if (sliceController == null)
        {
            Debug.LogError("<color=red>[Knife] sliceController is NULL!</color>");
            return;
        }
        if (sliceController.dataManager == null)
        {
            Debug.LogError("<color=red>[Knife] dataManager is NULL!</color>");
            return;
        }
        if (sliceController.viewRenderer == null)
        {
            Debug.LogError("<color=red>[Knife] viewRenderer is NULL!</color>");
            return;
        }
        
        // 1. Get Knife Plane Info (World Space)
        // Use indicatorCube if available, otherwise transform
        Transform knifeTransform = indicatorCube != null ? indicatorCube.transform : transform;
        
        Vector3 planeNormal = knifeTransform.forward; // Assuming Z-forward is normal (standard for Quad/Plane)
        // Note: If using Cube, any face could be normal. Assuming local Z.
        
        Vector3 planePoint = knifeTransform.position;
        
        // 2. Convert to Local Space for Data Slicing (relative to Vessel's Data Anchor)
        // CRITICAL: Must use velocityParent if available, otherwise data won't move with vessel!
        Transform dataTransform = (sliceController.velocityLoader != null && sliceController.velocityLoader.velocityParent != null) 
            ? sliceController.velocityLoader.velocityParent.transform 
            : null;
            
        if (dataTransform == null) dataTransform = GameObject.Find("Object Parent")?.transform;

        Vector3 localNormal = planeNormal;
        Vector3 localPoint = planePoint;

        if (dataTransform != null)
        {
            localNormal = dataTransform.InverseTransformDirection(planeNormal);
            localPoint = dataTransform.InverseTransformPoint(planePoint);
        }
        
        // Debug: Check if values change
        if (Time.frameCount % 60 == 0)
        {
             Debug.Log($"<color=cyan>[Knife Update] LocalPos: {localPoint}, Normal: {localNormal}, Parent: {dataTransform?.name}</color>");
        }
        
        // 3. Get Slice Data with Thickness
        // Fix: Convert World Thickness to Local Thickness
        // Use the actual data reference transform for scale
        Transform dataRefTransform = (sliceController.velocityLoader != null && sliceController.velocityLoader.velocityParent != null)
            ? sliceController.velocityLoader.velocityParent.transform
            : null;
        if (dataRefTransform == null) dataRefTransform = GameObject.Find("Object Parent")?.transform;

        float localThickness = sliceThickness;
        if (dataRefTransform != null)
        {
             // Assuming uniform scale for simplicity, or use average
             float scale = (dataRefTransform.lossyScale.x + dataRefTransform.lossyScale.y + dataRefTransform.lossyScale.z) / 3f;
             if (scale > 0.0001f)
             {
                 localThickness = sliceThickness / scale;
             }
        }
        
        // Use dataRefTransform for coordinate conversion too IF parentTransform was just visual
        // But GetSliceDataByPlane logic assumes we pass localPoint relative to the object space.
        // If localNormal/localPoint were calculated relative to parentTransform, and parentTransform != dataRefTransform... mismatch.
        // We should recalculate localNormal/localPoint relative to dataRefTransform if available.
        
        if (dataRefTransform != null && dataRefTransform != parentTransform)
        {
            localNormal = dataRefTransform.InverseTransformDirection(planeNormal);
            localPoint = dataRefTransform.InverseTransformPoint(planePoint);
        }

        // Note: GetSliceDataByPlane must support thickness param (added in SliceDataManager)
        var sliceData = sliceController.dataManager.GetSliceDataByPlane(localNormal, localPoint, localThickness);
        
        // 4. Transform Data back to World for Rendering
        // RenderPlaneSlice expects World Space data
        if (dataRefTransform != null)
        {
             for(int i=0; i<sliceData.Count; i++)
             {
                 var p = sliceData[i];
                 p.position = dataRefTransform.TransformPoint(p.position);
                 p.velocity = dataRefTransform.TransformDirection(p.velocity); 
                 sliceData[i] = p;
             }
        }
        
        // 5. Render
        sliceController.viewRenderer.RenderPlaneSlice(sliceData, knifeTransform);
        
        // Debug: Confirm rendering
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"<color=green>[Knife Render] DataPoints: {sliceData.Count}, KnifePos: {knifeTransform.position}</color>");
        }
    }

    public void RefreshKnifeSliceForFrameChange()
    {
        if (!isActiveAndEnabled || !enableKnifeMode)
        {
            return;
        }

        UpdateKnifeSlice();
    }

    /// <summary>
     /// Forces the knife scale to match world size of 10cm regardless of parent scale
     /// </summary>
    void UpdateKnifeScale()
    {
        float worldSize = 0.3f; // Increased 3x (from 0.1 to 0.3)

        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        
        float px = Mathf.Abs(parentScale.x) > 0.0001f ? parentScale.x : 1f;
        float py = Mathf.Abs(parentScale.y) > 0.0001f ? parentScale.y : 1f;
        float pz = Mathf.Abs(parentScale.z) > 0.0001f ? parentScale.z : 1f;
        
        Vector3 oldScale = transform.localScale;
        Vector3 newScale = new Vector3(worldSize / px, worldSize / py, 0.01f / pz);
        transform.localScale = newScale;
        
        if (Vector3.Distance(oldScale, newScale) > 0.01f)
        {
            Debug.LogWarning($"<color=magenta>[UpdateKnifeScale] Scale CHANGED: {oldScale} -> {newScale} (parentLossyScale={parentScale})</color>");
        }

        BoxCollider collider = GetComponent<BoxCollider>();
        if (collider != null)
        {
            UpdateKnifeCollider(collider);
        }
    }

    private void UpdateKnifeCollider(BoxCollider collider)
    {
        if (collider == null)
        {
            return;
        }

        collider.size = Vector3.one;
        collider.center = Vector3.zero;
    }

    private Material highlightMaterial;

    private void UpdateIntersectionHighlight()
    {
        // 1. Find Vessel Renderer
        if (cachedVesselRenderer == null)
        {
            if (Manager.Instance != null && Manager.Instance.bloodVesselMesh != null)
                cachedVesselRenderer = Manager.Instance.bloodVesselMesh.GetComponent<Renderer>();
            
            if (cachedVesselRenderer == null)
            {
                string[] possibleNames = { "Blood Vessel obj", "WSS", "Aorta", "Vessel", "Mesh" };
                foreach (var name in possibleNames)
                {
                    GameObject obj = GameObject.Find(name);
                    if (obj != null)
                    {
                        cachedVesselRenderer = obj.GetComponentInChildren<Renderer>();
                        if (cachedVesselRenderer != null)
                        {
                            Debug.Log($"<color=cyan>[Highlight] Found vessel: {obj.name}</color>");
                            break;
                        }
                    }
                }
            }
        }

        // 2. Handle Feature Off (either disabled or indicator hidden)
        if (!enableIntersectionHighlight || !showIndicator)
        {
            // Revert to single material
            if (cachedVesselRenderer != null && cachedVesselRenderer.sharedMaterials.Length > 1)
            {
                cachedVesselRenderer.materials = new Material[] { cachedVesselRenderer.sharedMaterials[0] };
            }
            return;
        }

        // 3. Handle Feature On
        if (cachedVesselRenderer == null) return;

        // Add VertexColorHighlight as second material layer
        Material[] mats = cachedVesselRenderer.sharedMaterials;
        bool hasHighlight = false;
        foreach (var m in mats)
        {
            if (m != null && m.shader.name == "Custom/VertexColorHighlight")
            {
                hasHighlight = true;
                break;
            }
        }

        if (!hasHighlight)
        {
            if (highlightMaterial == null)
            {
                // Try direct shader reference first (prevents shader stripping in build)
                Shader highlightShader = intersectionShader != null ? intersectionShader : Shader.Find("Custom/VertexColorHighlight");
                if (highlightShader != null)
                {
                    highlightMaterial = new Material(highlightShader);
                    Debug.Log("<color=green>[Highlight] Created VertexColorHighlight material</color>");
                }
                else
                {
                    Debug.LogError("<color=red>[Highlight] VertexColorHighlight shader not found! Assign 'intersectionShader' in Inspector.</color>");
                    return;
                }
            }

            Material[] newMats = new Material[mats.Length + 1];
            for (int i = 0; i < mats.Length; i++) newMats[i] = mats[i];
            newMats[mats.Length] = highlightMaterial;
            cachedVesselRenderer.materials = newMats;
            Debug.Log("<color=green>[Highlight] Added VertexColorHighlight to vessel</color>");
        }

        // Update highlight material parameters
        if (highlightMaterial != null)
        {
            Vector3 planePoint = indicatorCube.transform.position;
            Vector3 planeNormal = indicatorCube.transform.forward;

            highlightMaterial.SetVector("_SlicePlanePoint", planePoint);
            highlightMaterial.SetVector("_SlicePlaneNormal", planeNormal);
            highlightMaterial.SetColor("_SliceHighlightColor", highlightColor);
            highlightMaterial.SetFloat("_SliceHighlightThickness", 0.01f);
            highlightMaterial.SetFloat("_EnableHighlight", 1.0f);
        }
    }

    private void OnManipulationStarted(Microsoft.MixedReality.Toolkit.UI.ManipulationEventData data)
    {
        if (PhotonSyncService.Instance != null)
        {
            PhotonSyncService.Instance.RequestGlobalLock(PhotonSyncService.LockType.ObjectManipulation);
        }
    }

    private void OnManipulationEnded(Microsoft.MixedReality.Toolkit.UI.ManipulationEventData data)
    {
        if (PhotonSyncService.Instance != null)
        {
            PhotonSyncService.Instance.ReleaseGlobalLock();
        }
    }
}
 
