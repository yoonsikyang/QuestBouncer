using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.UI;

/// <summary>
/// World-space color bar UI that displays data range with gradient
/// Follows the Object Parent
/// </summary>
public class ColorBarUI : MonoBehaviour
{
    [Header("References")]
    public Transform followTarget; // Object Parent to follow
    public Vector3 offsetFromTarget = new Vector3(0.15f, 0, 0); // Offset from target
    
    [Header("UI Elements")]
    private Canvas canvas;
    private RawImage gradientImage;
    private System.Collections.Generic.List<TextMeshProUGUI> valueLabels = new System.Collections.Generic.List<TextMeshProUGUI>();
    private TextMeshProUGUI unitLabel;
    
    [Header("Settings")]
    public float barWidth = 0.05f;
    public float barHeight = 0.3f;
    public float fontSize = 0.02f;
    public int labelCount = 5; // Number of value labels to display
    
    private Texture2D colormap;
    private float currentMin = 0f;
    private float currentMax = 1f;
    private string currentUnit = "";
    private bool hasBeenInitialized = false;
    private bool initialPositionSet = false; // Track if initial position has been set
    
    void Awake()
    {
        // Create UI early to ensure it's ready when Manager calls Show()
        //CreateUI();
        hasBeenInitialized = true;
        
        // Add manipulation components for hand interaction
        SetupManipulation();
        
        // Start hidden, but ready to show immediately
        gameObject.SetActive(false);
    }
    
    private void SetupManipulation()
    {
        // Add BoxCollider for interaction
        BoxCollider boxCollider = gameObject.GetComponent<BoxCollider>();
        if (boxCollider == null)
        {
            boxCollider = gameObject.AddComponent<BoxCollider>();
            boxCollider.size = new Vector3(barWidth, barHeight, 0.01f);
        }
        
        // Add NearInteractionGrabbable (for near grab)
        var grabbable = gameObject.AddComponent<NearInteractionGrabbable>();
        
        // Add ObjectManipulator (for drag/rotation)
        var manipulator = gameObject.AddComponent<ObjectManipulator>();
        manipulator.HostTransform = transform;
        
        // Add listeners for network sync
        manipulator.OnManipulationStarted.AddListener(OnManipulationStarted);
        manipulator.OnManipulationEnded.AddListener(OnManipulationEnded);
        
        Debug.Log("<color=cyan>[ColorBarUI] Manipulation components added - ColorBar is now movable/rotatable</color>");
    }
    
    void Update()
    {
        // Follow the target only once to set initial position
        if (followTarget != null && !initialPositionSet)
        {
            transform.position = followTarget.position + followTarget.TransformDirection(offsetFromTarget);
            transform.rotation = followTarget.rotation;
            initialPositionSet = true; // Mark as set, won't follow anymore
        }
    }
    
    void CreateUI()
    {
        // Create Canvas
        GameObject canvasObj = new GameObject("ColorBar Canvas");
        canvasObj.transform.SetParent(transform, false);
        
        canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 100;
        
        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(barWidth * 1000, barHeight * 1000);
        canvasRect.localScale = new Vector3(0.001f, 0.001f, 0.001f);
        
        // Create gradient bar
        GameObject gradientObj = new GameObject("Gradient Bar");
        gradientObj.transform.SetParent(canvasObj.transform, false);
        
        gradientImage = gradientObj.AddComponent<RawImage>();
        RectTransform gradientRect = gradientObj.GetComponent<RectTransform>();
        gradientRect.anchorMin = new Vector2(0.5f, 0.5f);
        gradientRect.anchorMax = new Vector2(0.5f, 0.5f);
        gradientRect.pivot = new Vector2(0.5f, 0.5f);
        gradientRect.sizeDelta = new Vector2(barWidth * 1000, barHeight * 1000);
        gradientRect.localPosition = Vector3.zero;
        
        // Create value labels (5 labels evenly spaced)
        valueLabels.Clear();
        for (int i = 0; i < labelCount; i++)
        {
            float t = i / (float)(labelCount - 1); // 0, 0.25, 0.5, 0.75, 1.0
            float yPos = (t - 0.5f) * barHeight * 1000; // Position from bottom to top
            
            GameObject labelObj = new GameObject($"Value Label {i}");
            labelObj.transform.SetParent(canvasObj.transform, false);
            
            TextMeshProUGUI label = labelObj.AddComponent<TextMeshProUGUI>();
            label.fontSize = fontSize * 1000;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.color = Color.white;
            label.fontStyle = FontStyles.Normal;
            
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.sizeDelta = new Vector2(barWidth * 3000, fontSize * 1000);
            labelRect.localPosition = new Vector3(barWidth * 500 + 5, yPos, 0);
            
            valueLabels.Add(label);
        }
        
        // Create unit label (below the bar)
        GameObject unitLabelObj = new GameObject("Unit Label");
        unitLabelObj.transform.SetParent(canvasObj.transform, false);
        
        unitLabel = unitLabelObj.AddComponent<TextMeshProUGUI>();
        unitLabel.fontSize = fontSize * 1000;
        unitLabel.alignment = TextAlignmentOptions.Center;
        unitLabel.color = Color.gray;
        
        RectTransform unitRect = unitLabelObj.GetComponent<RectTransform>();
        unitRect.anchorMin = new Vector2(0.5f, 0f);
        unitRect.anchorMax = new Vector2(0.5f, 0f);
        unitRect.pivot = new Vector2(0.5f, 1f);
        unitRect.sizeDelta = new Vector2(barWidth * 2000, fontSize * 1000);
        unitRect.localPosition = new Vector3(0, -barHeight * 500 - fontSize * 600, 0);
        
        // Apply colormap if it was set before UI was created
        if (colormap != null && gradientImage != null)
        {
            Debug.Log($"<color=green>[ColorBarUI] Applying stored colormap after UI creation</color>");
            Texture2D rotated = RotateTexture90(colormap);
            gradientImage.texture = rotated;
            gradientImage.color = Color.white;
        }
        
        // Apply range values if they were set before UI was created
        if (valueLabels.Count > 0 && (currentMin != 0f || currentMax != 1f))
        {
            Debug.Log($"<color=green>[ColorBarUI] Applying stored range values after UI creation: {currentMin:F1}-{currentMax:F1} {currentUnit}</color>");
            for (int i = 0; i < valueLabels.Count; i++)
            {
                float t = i / (float)(labelCount - 1);
                float value = Mathf.Lerp(currentMin, currentMax, t);
                valueLabels[i].text = value.ToString("F1");
            }
            if (unitLabel != null) unitLabel.text = $"({currentUnit})";
        }
    }
    
    /// <summary>
    /// Set the data range and unit
    /// </summary>
    public void SetRange(float min, float max, string unit)
    {
        currentMin = min;
        currentMax = max;
        currentUnit = unit;
        
        // Update all value labels if they exist
        if (valueLabels != null && valueLabels.Count > 0)
        {
            for (int i = 0; i < valueLabels.Count; i++)
            {
                float t = i / (float)(labelCount - 1);
                float value = Mathf.Lerp(min, max, t);
                valueLabels[i].text = value.ToString("F1");
            }
            
            if (unitLabel != null) unitLabel.text = $"({unit})";
            Debug.Log($"<color=cyan>[ColorBarUI] Labels updated with range {min:F1}-{max:F1} {unit}</color>");
        }
        else
        {
            Debug.LogWarning($"<color=yellow>[ColorBarUI] Labels not created yet, range will be applied after UI creation</color>");
        }
    }
    
    /// <summary>
    /// Set the colormap texture
    /// </summary>
    public void SetColormap(Texture2D colormapTexture)
    {
        colormap = colormapTexture;
        Debug.Log($"<color=magenta>[ColorBarUI] SetColormap called. Colormap null? {colormapTexture == null}, GradientImage null? {gradientImage == null}</color>");
        
        if (gradientImage != null && colormap != null)
        {
            Debug.Log($"<color=magenta>[ColorBarUI] Original colormap size: {colormap.width}x{colormap.height}</color>");
            
            // Rotate texture 90 degrees for vertical display
            Texture2D rotated = RotateTexture90(colormap);
            Debug.Log($"<color=magenta>[ColorBarUI] Rotated colormap size: {rotated.width}x{rotated.height}</color>");
            
            gradientImage.texture = rotated;
            Debug.Log($"<color=magenta>[ColorBarUI] Texture assigned to gradientImage. Current texture: {gradientImage.texture != null}</color>");
            
            // Ensure RawImage color is white (for proper texture display)
            gradientImage.color = Color.white;
        }
        else if (colormap != null)
        {
            // GradientImage not created yet, will be applied in CreateUI or Show
            Debug.LogWarning($"<color=yellow>[ColorBarUI] GradientImage not ready yet, colormap will be applied after UI creation</color>");
        }
        else
        {
            Debug.LogWarning($"<color=red>[ColorBarUI] Cannot set colormap - gradientImage: {gradientImage != null}, colormap: {colormap != null}</color>");
        }
    }
    
    /// <summary>
    /// Show the color bar
    /// </summary>
    public void Show(bool networkCall = false)
    {
        // Ensure UI is created before showing
        if (canvas == null)
        {
            Debug.LogWarning("[ColorBarUI] UI not created yet, creating now...");
            CreateUI();
        }
        
        Debug.Log($"<color=cyan>[ColorBarUI] Show() called. Current state: {gameObject.activeSelf}</color>");
        
        // Reset position to followTarget + offset
        if (followTarget != null && !networkCall)
        {
            transform.position = followTarget.position + followTarget.TransformDirection(offsetFromTarget);
            transform.rotation = followTarget.rotation;
            Debug.Log($"<color=green>[ColorBarUI] Reset position to followTarget: {transform.position}</color>");
            initialPositionSet = true;
        }
        
        if (networkCall)
        {
            initialPositionSet = true; // Prevent Update from overriding remote position
        }
        
        // Activate all parents in hierarchy
        Transform current = transform;
        while (current != null)
        {
            if (!current.gameObject.activeSelf)
            {
                Debug.Log($"<color=yellow>[ColorBarUI] Activating parent: {current.name}</color>");
                current.gameObject.SetActive(true);
            }
            current = current.parent;
        }
        
        gameObject.SetActive(true);
        Debug.Log($"<color=cyan>[ColorBarUI] After SetActive(true). New state: {gameObject.activeSelf}</color>");
        
        // Force enable canvas as well
        if (canvas != null)
        {
            canvas.gameObject.SetActive(true);
            Debug.Log($"<color=cyan>[ColorBarUI] Canvas activated: {canvas.gameObject.activeSelf}</color>");
        }
    }
    
    /// <summary>
    /// Hide the color bar
    /// </summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }
    
    /// <summary>
    /// Rotate texture 90 degrees clockwise for vertical display
    /// </summary>
    private Texture2D RotateTexture90(Texture2D original)
    {
        Texture2D rotated = new Texture2D(original.height, original.width);
        
        for (int x = 0; x < original.width; x++)
        {
            for (int y = 0; y < original.height; y++)
            {
                rotated.SetPixel(original.height - 1 - y, x, original.GetPixel(x, y));
            }
        }
        
        rotated.Apply();
        return rotated;
    }

    private void OnManipulationStarted(ManipulationEventData data)
    {
        if (PhotonSyncService.Instance != null)
        {
            PhotonSyncService.Instance.RequestGlobalLock(PhotonSyncService.LockType.ObjectManipulation);
        }
    }

    private void OnManipulationEnded(ManipulationEventData data)
    {
        if (PhotonSyncService.Instance != null)
        {
            PhotonSyncService.Instance.ReleaseGlobalLock();
        }
    }
}
