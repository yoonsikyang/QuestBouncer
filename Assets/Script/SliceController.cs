using UnityEngine;

/// <summary>
/// Simple controller that coordinates the 3 modular slice components
/// Replaces the monolithic SliceController.cs
/// </summary>
public class SliceController : MonoBehaviour
{
    [Header("Modular Components")]
    public SliceDataManager dataManager;
    public SliceViewRenderer viewRenderer;
    public SliceIndicatorController indicatorController;
        
    public enum SliceAxis
    {
        None,
        X_Axis,
        Y_Axis
    }
    
    [Header("Parent and Anchor")]
    public GameObject visualsParent;
    public Transform customAnchorTransform;
    
    [Header("References")]
    public VelocityLoader velocityLoader;
    
    [Header("Slice Settings")]
    public SliceController.SliceAxis currentAxis = SliceController.SliceAxis.X_Axis;
    
    [Range(0, 1)]
    public float slicePosition = 0.5f;
    
    public float slicePositionX = 0.5f;
    public float slicePositionY = 0.5f;
    
    [Header("Visualization Toggles")]
    public bool show2DHeatmap = false;
    public bool show3DArrows = false;
    public bool showSliceIndicator = false;
    
    [Header("Global Visualization Scale")]
    [Tooltip("Overall scale of the entire SliceVisualization GameObject")]
    public float globalVisualizationScale = 0.1f;
    
    // Cache previous values for change detection
    private SliceController.SliceAxis prevAxis = SliceController.SliceAxis.None;
    private float prevSlicePosition = -1f;
    private bool prevShow2D = true;
    private bool prevShow3D = false;
    private int prevFrameIndex = -1;
    
    
    void Start()
    {
        // Apply global visualization scale
        transform.localScale = Vector3.one * globalVisualizationScale;
        
        Debug.Log("<color=cyan>[SliceController] Start() - Finding components...</color>");

        // Auto-find components if not assigned
        if (dataManager == null)
        {
            dataManager = GetComponent<SliceDataManager>();
            Debug.LogWarning($"<color=cyan>[SliceController] dataManager={(dataManager != null ? "FOUND" : "NOT FOUND")}</color>");
        }
        
        if (viewRenderer == null)
        {
            viewRenderer = GetComponent<SliceViewRenderer>();
            Debug.LogWarning($"<color=cyan>[SliceController] viewRenderer={(viewRenderer != null ? "FOUND" : "NOT FOUND")}</color>");
        }
        
        if (indicatorController == null)
        {
            indicatorController = GetComponent<SliceIndicatorController>();
            Debug.LogWarning($"<color=cyan>[SliceController] indicatorController={(indicatorController != null ? "FOUND" : "NOT FOUND")}</color>");
        }
        
        // Auto-find velocityLoader if not assigned
        if (velocityLoader == null)
        {
            velocityLoader = FindObjectOfType<VelocityLoader>();
            Debug.LogWarning($"<color=cyan>[SliceController] velocityLoader={(velocityLoader != null ? "FOUND" : "NOT FOUND")}</color>");
        }
        
        // Initialize visuals container if none assigned
        if (visualsParent == null || visualsParent == this.gameObject)
        {
            Transform visualsChild = transform.Find("Visuals");
            if (visualsChild != null)
            {
                visualsParent = visualsChild.gameObject;
                Debug.Log("<color=cyan>[SliceController] Found existing 'Visuals' child for visualsParent</color>");
            }
            else
            {
                // Create a dedicated container for visualizations to avoid hiding the whole object
                GameObject newVisuals = new GameObject("Visuals Container");
                newVisuals.transform.SetParent(this.transform, false);
                visualsParent = newVisuals;
                Debug.Log("<color=cyan>[SliceController] Created dedicated Visuals Container for visualsParent</color>");
            }
        }
        else
        {
            Debug.Log($"<color=cyan>[SliceController] visualsParent already assigned: {visualsParent.name}</color>");
        }
        
        // Set initial rotation: 180 degrees around Z-axis (only if not already rotated)
        if (Mathf.Abs(transform.localRotation.eulerAngles.z - 180f) > 1f)
        {
            transform.localRotation = Quaternion.Euler(0, 0, 180);
        }
        
        // Initialize
        UpdateSlicePosition();
        UpdateAllComponents();
        
        Debug.Log("<color=cyan>[SliceController] Initialized</color>");
    }
    
    void Update()
    {
        // 1. Update visibility flags (Always valid)
        if (viewRenderer != null)
        {
            viewRenderer.show2DHeatmap = show2DHeatmap;
            viewRenderer.show3DArrows = show3DArrows;
        }
        
        if (indicatorController != null)
        {
            indicatorController.showIndicator = showSliceIndicator;
            
            // CRITICAL: If Knife Mode is active, skip Axis-based logic to avoid conflict
            // But still allow visibility flags to sync
            if (indicatorController.enableKnifeMode)
            {
                // Knife Mode handles its own slicing via UpdateKnifeSlice()
                // Just ensure visibility flags are synced, then skip axis logic
                return;
            }
        }

        // 2. Axis-based Logic (Only if NOT Knife Mode)
        // Update slice position based on current axis
        UpdateSlicePosition();
        
        // Check for changes
        bool axisChanged = currentAxis != prevAxis;
        bool positionChanged = Mathf.Abs(slicePosition - prevSlicePosition) > 0.001f;
        bool visibilityChanged = show2DHeatmap != prevShow2D || show3DArrows != prevShow3D;
        
        // Check if velocity frame changed
        bool frameChanged = false;
        if (velocityLoader != null && velocityLoader.loadedFrames != null && velocityLoader.loadedFrames.Count > 0)
        {
            frameChanged = velocityLoader.currentFrameIndex != prevFrameIndex;
            if (frameChanged)
            {
                // Debug.Log($"<color=cyan>[SliceController] Frame changed: {prevFrameIndex} -> {velocityLoader.currentFrameIndex}</color>");
            }
        }
        
        if (axisChanged || positionChanged || visibilityChanged || frameChanged)
        {
            UpdateAllComponents(false);
        }
    }
    
    /// <summary>
    /// Updates the slice position based on current axis
    /// </summary>
    void UpdateSlicePosition() 
    {
        if (currentAxis == SliceController.SliceAxis.X_Axis)
        {
            slicePosition = slicePositionX;
        }
        else if (currentAxis == SliceController.SliceAxis.Y_Axis)
        {
            slicePosition = slicePositionY;
        }
    }
    
    /// <summary>
    /// Updates all modular components
    /// </summary>
    public void UpdateAllComponents(bool networkCall = false)
    {
        // Update SliceDataManager
        if (dataManager != null)
        {
            dataManager.currentAxis = this.currentAxis;
            dataManager.slicePosition = this.slicePosition;
            dataManager.ForceRefresh();
        }
        
        // Update SliceViewRenderer
        if (viewRenderer != null)
        {
            viewRenderer.UpdateVisualization();
        }
        
        // Update SliceIndicatorController
        if (indicatorController != null && dataManager != null)
        {
            var bounds = dataManager.GetDataBounds();
            float sliceCoord = dataManager.GetSliceCoordinate();
            
            // Only update indicator position in Axis mode, not in Knife mode
            if (!indicatorController.enableKnifeMode)
            {
                indicatorController.UpdateIndicatorPosition(this.currentAxis, sliceCoord, bounds, networkCall);
            }
        }

        // Sync prev state to avoid double-update in Update() loop
        prevAxis = currentAxis;
        prevSlicePosition = slicePosition;
        prevShow2D = show2DHeatmap;
        prevShow3D = show3DArrows;
        if (velocityLoader != null)
        {
            prevFrameIndex = velocityLoader.currentFrameIndex;
        }
    }

    public void RefreshForVelocityFrameChange()
    {
        if (!isActiveAndEnabled || velocityLoader == null)
        {
            return;
        }

        if (prevFrameIndex == velocityLoader.currentFrameIndex)
        {
            return;
        }

        if (viewRenderer != null)
        {
            viewRenderer.show2DHeatmap = show2DHeatmap;
            viewRenderer.show3DArrows = show3DArrows;
        }

        if (indicatorController != null)
        {
            indicatorController.showIndicator = showSliceIndicator;

            if (indicatorController.enableKnifeMode)
            {
                indicatorController.RefreshKnifeSliceForFrameChange();
                prevFrameIndex = velocityLoader.currentFrameIndex;
                return;
            }
        }

        UpdateSlicePosition();
        UpdateAllComponents(false);
    }
    
    /// <summary>
    /// Sets the slice position for a specific axis
    /// Called from GlobalSliderController
    /// </summary>
    public void SetSlicePositionForAxis(SliceController.SliceAxis axis, float value, bool updateVisualization = true, bool networkCall = false)
    {
        float clamped = Mathf.Clamp01(value);
        
        if (axis == SliceController.SliceAxis.X_Axis)
        {
            slicePositionX = clamped;
            if (currentAxis == axis) slicePosition = clamped;
        }
        else if (axis == SliceController.SliceAxis.Y_Axis)
        {
            slicePositionY = clamped;
            if (currentAxis == axis) slicePosition = clamped;
        }
        
        if (updateVisualization && currentAxis == axis)
        {
            UpdateAllComponents(networkCall);
        }
    }
    
    /// <summary>
    /// Gets the slice position for a specific axis
    /// Called from GlobalSliderController
    /// </summary>
    public float GetSlicePosition(SliceController.SliceAxis axis)
    {
        switch (axis)
        {
            case SliceController.SliceAxis.X_Axis: return slicePositionX;
            case SliceController.SliceAxis.Y_Axis: return slicePositionY;
            default: return slicePosition;
        }
    }
    
    /// <summary>
    /// Activates the slice visualization
    /// </summary>
    public void ActivateVisualization()
    {
        this.enabled = true;
        UpdateAllComponents();
        Debug.Log("<color=green>[SliceController] Activated</color>");
    }
    
    /// <summary>
    /// Deactivates the slice visualization
    /// </summary>
    public void DeactivateVisualization()
    {
        this.enabled = false;
        if (indicatorController != null)
        {
            indicatorController.SetVisible(false);
        }
    }
    
    /// <summary>
    /// Sets the slice indicator visibility
    /// </summary>
    public void SetSliceIndicatorVisible(bool visible)
    {
        showSliceIndicator = visible;
        if (indicatorController != null)
        {
            indicatorController.SetVisible(visible);
        }
    }
}
