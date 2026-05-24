using UnityEngine;
using System.Collections.Generic;
using Microsoft.MixedReality.Toolkit.UI;
using Microsoft.MixedReality.Toolkit.Input;

/// <summary>
/// Controller for the interactive slice plane
/// </summary>
public class SlicePlaneController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Parent transform (usually ObjectParent)")]
    public Transform objectParent;
    
    [Tooltip("The actual slice plane object")]
    public GameObject slicePlane;
    
    [Tooltip("SliceDataManager for slice data calculation")]
    public SliceDataManager sliceDataManager;
    
    [Tooltip("SliceViewRenderer for visualization")]
    public SliceViewRenderer sliceViewRenderer;
    
    [Header("Plane Settings")]
    public float planeSize = 0.2f;
    public Color planeColor = new Color(0, 1, 1, 0.3f);
    public Material planeMaterial;
    
    [Header("Handle Settings")]
    [Tooltip("Radius of the handle sphere")]
    public float handleRadius = 0.03f;

    [Tooltip("Minimum world-space radius for both the visible handle and its grab area")]
    public float minimumHandleGrabRadius = 0.03f;
    
    [Tooltip("Distance from plane center")]
    public float handleDistance = 0.06f;
    
    [Tooltip("Handle color")]
    public Color handleColor = Color.cyan;
    
    // Internal
    private GameObject planeQuad;
    private GameObject handleSphere;
    private ObjectManipulator handleManipulator;
    private BoxCollider movementBoundsCollider;
    private Bounds movementBounds;
    
    void Start()
    {
        CreateSlicePlane();
        CalculateMovementBounds();
    }
    
    void Update() 
    {
        ApplyBoundsConstraint();
        
        // Continuous update while manipulating
        // MRTK2 ObjectManipulator check workaround or just update if handle exists
        if (handleSphere != null) 
        {
             UpdateSliceData();
        }
    }
    
    /// <summary> 
    /// Creates the interactive slice plane with handle
    /// </summary>
    /// 
    void CreateSlicePlane()
    {
        // 1. Create main plane object if not assigned
        if (slicePlane == null)
        {
            slicePlane = new GameObject("SlicePlane");
            if (objectParent != null)
            {
                slicePlane.transform.SetParent(objectParent, false);
            }
            slicePlane.transform.localPosition = Vector3.zero;
        }
        
        // 2. Create visual quad
        planeQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        planeQuad.name = "PlaneQuad";
        planeQuad.transform.SetParent(slicePlane.transform, false);
        planeQuad.transform.localScale = new Vector3(planeSize, planeSize, 1f);
        
        // Setup material
        Renderer planeRenderer = planeQuad.GetComponent<Renderer>();
        if (planeMaterial != null)
        {
            planeRenderer.material = new Material(planeMaterial);
        }
        else
        {
            planeRenderer.material = new Material(Shader.Find("Standard"));
            // Make transparent
            planeRenderer.material.SetFloat("_Mode", 3);
            planeRenderer.material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            planeRenderer.material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            planeRenderer.material.SetInt("_ZWrite", 0);
            planeRenderer.material.renderQueue = 3000;
        }
        ConfigureTransparentMaterial(planeRenderer.material, planeColor);
        
        // Remove collider (interaction handled by handle)
        Destroy(planeQuad.GetComponent<Collider>());
        
        // 3. Create handle sphere
        handleSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        handleSphere.name = "SliceHandle";
        handleSphere.transform.SetParent(slicePlane.transform, false);
        handleSphere.transform.localPosition = new Vector3(handleDistance, 0, 0);
        float effectiveHandleRadius = GetEffectiveHandleRadius();
        handleSphere.transform.localScale = Vector3.one * effectiveHandleRadius * 2f;
        
        // Setup handle material
        Renderer handleRenderer = handleSphere.GetComponent<Renderer>();
        if (handleRenderer == null) handleRenderer = handleSphere.AddComponent<MeshRenderer>();
        
        if (planeMaterial != null)
        {
            handleRenderer.material = new Material(planeMaterial);
        }
        else 
        {
            handleRenderer.material = new Material(Shader.Find("Standard"));
        }
        handleRenderer.material.color = handleColor;
        
        // 4. Add MRTK ObjectManipulator to handle
        handleManipulator = handleSphere.AddComponent<ObjectManipulator>();
        handleManipulator.HostTransform = slicePlane.transform;
        // MRTK2 specific settings - handle mainly in Inspector due to version diffs
        handleManipulator.SmoothingActive = true;
        
        // 5. Add MRTK NearInteractionGrabbable
        handleSphere.AddComponent<NearInteractionGrabbable>();
        
        // 6. Ensure collider exists and is sized correctly
        SphereCollider sphereCollider = handleSphere.GetComponent<SphereCollider>();
        if (sphereCollider == null) sphereCollider = handleSphere.AddComponent<SphereCollider>();
        sphereCollider.radius = 0.5f;
        
        // 7. Setup events
        handleManipulator.OnManipulationStarted.AddListener(OnManipulationStarted);
        handleManipulator.OnManipulationEnded.AddListener(OnManipulationEnded);
        
        // 8. Add constraint if needed 
        // For MRTK2, we might need ConstraintManager, but let's keep it simple for code creation
        
        Debug.Log("<color=green>[SlicePlaneController] Slice plane with handle created</color>");
    }
    
    /// <summary>
    /// Calculate movement bounds from BoxCollider
    /// </summary>
    void CalculateMovementBounds()
    {
        if (objectParent == null) return;

        movementBoundsCollider = objectParent.GetComponent<BoxCollider>();
        if (movementBoundsCollider == null)
        {
            BoxCollider[] childColliders = objectParent.GetComponentsInChildren<BoxCollider>(true);
            foreach (BoxCollider candidate in childColliders)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (slicePlane != null && candidate.transform.IsChildOf(slicePlane.transform))
                {
                    continue;
                }

                movementBoundsCollider = candidate;
                break;
            }
        }

        if (movementBoundsCollider == null) return;

        movementBounds = movementBoundsCollider.bounds;
        Debug.Log($"[SlicePlaneController] Movement bounds: {movementBounds}");
    }
    
    void OnManipulationStarted(ManipulationEventData eventData)
    {
        if (PhotonSyncService.Instance != null)
        {
            PhotonSyncService.Instance.RequestGlobalLock(PhotonSyncService.LockType.ObjectManipulation);
        }
    }
    
    void OnManipulationEnded(ManipulationEventData eventData)
    {
        if (PhotonSyncService.Instance != null)
        {
            PhotonSyncService.Instance.ReleaseGlobalLock();
        }
        UpdateSliceData();
    }
    
    /// <summary>
    /// Clamp plane position to movement bounds
    /// </summary>
    void ApplyBoundsConstraint()
    {
        if (slicePlane == null) return;
        Bounds currentBounds = GetCurrentMovementBounds();
        if (currentBounds.size == Vector3.zero) return;

        Vector3 worldPos = slicePlane.transform.position;

        worldPos.x = Mathf.Clamp(worldPos.x, currentBounds.min.x, currentBounds.max.x);
        worldPos.y = Mathf.Clamp(worldPos.y, currentBounds.min.y, currentBounds.max.y);
        worldPos.z = Mathf.Clamp(worldPos.z, currentBounds.min.z, currentBounds.max.z);
        
        slicePlane.transform.position = worldPos;
    }

    private Bounds GetCurrentMovementBounds()
    {
        if (movementBoundsCollider != null)
        {
            movementBounds = movementBoundsCollider.bounds;
        }

        return movementBounds;
    }

    private float GetEffectiveHandleRadius()
    {
        return Mathf.Max(0.001f, handleRadius, minimumHandleGrabRadius);
    }

    private void ConfigureTransparentMaterial(Material material, Color color)
    {
        if (material == null) return;

        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);
        }

        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000;
        material.color = color;
    }
    
    /// <summary>
    /// Update slice data based on current plane position
    /// </summary>
    void UpdateSliceData()
    {
        if (sliceDataManager == null || sliceViewRenderer == null || slicePlane == null) return;
        
        // 1. Get Plane Info (World Space)
        Vector3 planeNormal = slicePlane.transform.forward;
        Vector3 planePoint = slicePlane.transform.position;
        
        // 2. Convert to Local Space for Data Slicing
        // Data points in VelocityLoader are in Object Local Space (usually)
        // If they are in World Space, we skip this
        // SliceDataManager uses VelocityLoader.loadedFrames which are usually raw data (Local)
        
        Vector3 localNormal = planeNormal;
        Vector3 localPoint = planePoint;
        
        if (objectParent != null)
        {
            localNormal = objectParent.InverseTransformDirection(planeNormal);
            localPoint = objectParent.InverseTransformPoint(planePoint);
        }
        
        // 3. Get Slice Data
        var sliceData = sliceDataManager.GetSliceDataByPlane(localNormal, localPoint);
        
        // 4. Render
        // RenderPlaneSlice expects World Space data for rendering if we are rendering in World Space
        // We need to convert data points back to World Space for visualization
        
        if (objectParent != null)
        {
             for(int i=0; i<sliceData.Count; i++)
             {
                 var p = sliceData[i];
                 p.position = objectParent.TransformPoint(p.position);
                 p.velocity = objectParent.TransformDirection(p.velocity); // Rotate checks
                 sliceData[i] = p;
             }
        }
        
        sliceViewRenderer.RenderPlaneSlice(sliceData, slicePlane.transform);
    }
}
