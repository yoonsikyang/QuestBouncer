using UnityEngine;
using UnityEngine.Events;
using System;
using System.Reflection;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Generic wrapper for MRTK PinchSlider prefab.
/// - Spawns/uses the PinchSlider prefab.
/// - Follows an anchor with offset (rotation optionally locked).
/// - Exposes GetValue/SetValue and events.
/// - Can drive SliceController per-axis.
/// - Polls value as a fallback when MRTK events are not firing.
/// </summary>
public enum ControlMode
{
    SlicePosition,
    VelocityPlayback,
    WssPlayback,
    StreamlinePlayback,
    DensityX,
    DensityY,
    DensityZ,
    FrameControl,
    VesselSize,
    Rotation
}

public class GlobalSliderController : MonoBehaviour
{
    [Header("Anchor & Placement")]
    public bool useHeadAnchor = true; // New: If true, uses Camera.main as anchor
    public Transform anchor; // e.g., Object Parent (ignored if useHeadAnchor is true)
    public Vector3 offsetFromAnchor = new Vector3(-0.1f, 0f, 0f); // Used for Object Anchor
    public Vector3 headOffset = new Vector3(-0.2f, -0.1f, 0.5f); // Used for Head Anchor (Left, Down, Forward)
    public bool followAnchor = true;
    public bool verticalOnY = true; // rotate so the slider runs along local Y (adds Z 90°)
    public bool lockRotation = true;

    [Header("Slider Prefab")]
    [Header("Slider Object")]
    public GameObject sliderObject; // Assign the pre-existing slider GameObject here

    [Header("Target Selection")]
    public ControlMode mode = ControlMode.SlicePosition;
    public bool pollSliderValue = true; // fallback polling if MRTK events don't arrive

    [Header("Optional Links")]
    public bool linkSliceController = true; // only applies when mode == SlicePosition
    public SliceController SliceController;
    public SliceController.SliceAxis sliceAxisLink = SliceController.SliceAxis.None; // None = use SliceController.currentAxis
    public Manager manager;
    public VelocityLoader velocityLoader;

    [Header("Activation")]
    public bool startActive = true;

    [HideInInspector] public float sliderValue = 0.5f;

    private GameObject sliderRoot;
    private Component pinchSliderComp;
    private Type pinchSliderType;
    private bool isDragging = false;
    private bool isActive = true;
    private float lastSyncedTarget = -1f;
    private bool suppressNetworkBroadcast = false;

    private Vector3 initialOffset;
    private bool hasStoredInitialPosition = false;
    private bool hasFollowedAnchorOnce = false; // Track if we've already positioned using followAnchor
    private int followAnchorEnabledFrame = -1; // Track which frame followAnchor was enabled

    void Start()
    {
        Debug.Log("<color=magenta>===== GlobalSliderController.Start() called =====</color>");
        initialization();
    }

    public void initialization()
    {
        Debug.Log("<color=cyan>===== GlobalSliderController.initialization() START =====</color>");
        suppressNetworkBroadcast = true;
        
        // Re-acquire references after scene reload
        if (!useHeadAnchor)
        {
            if (anchor == null && Manager.Instance != null && Manager.Instance.ObjectParent != null)
            {
                anchor = Manager.Instance.ObjectParent.transform;
                Debug.Log("<color=green>Anchor acquired from Manager.Instance.ObjectParent</color>");
            }
            else
            {
                Debug.LogWarning($"<color=yellow>Anchor not acquired: anchor={(anchor != null ? "EXISTS" : "NULL")}, Manager.Instance={(Manager.Instance != null ? "EXISTS" : "NULL")}</color>");
            }
        }
        else
        {
             Debug.Log("<color=cyan>Using Head Anchor (Camera.main)</color>");
        }
        
        // Try to get manager reference
        if (manager == null)
        {
            // First try Manager.Instance
            if (Manager.Instance != null)
            {
                manager = Manager.Instance;
                Debug.Log("<color=green>Manager reference acquired from Manager.Instance: SUCCESS</color>");
            }
            else
            {
                // Fallback to FindObjectOfType
                manager = FindObjectOfType<Manager>();
                if (manager != null)
                {
                    Debug.Log("<color=yellow>Manager reference acquired via FindObjectOfType: SUCCESS</color>");
                }
                else
                {
                    Debug.LogError("<color=red>Manager reference acquisition FAILED! Manager.Instance is NULL and FindObjectOfType found nothing!</color>");
                }
            }
        }
        else
        {
            Debug.Log($"<color=yellow>Manager reference already exists: {manager.name}</color>");
        }
        
        if (velocityLoader == null)
        {
            if (manager != null && manager.velocityLoader != null)
            {
                velocityLoader = manager.velocityLoader;
                Debug.Log("<color=green>VelocityLoader acquired from manager</color>");
            }
            else
            {
                velocityLoader = FindObjectOfType<VelocityLoader>();
                Debug.Log($"<color=yellow>VelocityLoader found via FindObjectOfType: {(velocityLoader != null ? "SUCCESS" : "FAILED")}</color>");
            }
        }
        else
        {
            Debug.Log("<color=yellow>VelocityLoader reference already exists</color>");
        }
        
        if (SliceController == null)
        {
            SliceController = FindObjectOfType<SliceController>();
            Debug.Log($"<color=green>SliceController found: {(SliceController != null ? "SUCCESS" : "FAILED")}</color>");
        }
        else
        {
            Debug.Log("<color=yellow>SliceController reference already exists</color>");
        }

        pinchSliderType = FindType(
            "Microsoft.MixedReality.Toolkit.UI.PinchSlider, Microsoft.MixedReality.Toolkit.SDK",
            "Microsoft.MixedReality.Toolkit.UI.PinchSlider, Microsoft.MixedReality.Toolkit"
        );

#if UNITY_EDITOR
        if (sliderObject == null)
        {
            // const string pkgPath = "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Prefabs/Sliders/PinchSlider.prefab";
            // sliderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(pkgPath);
        }
#endif

        CreateSlider();
        HookSliderEvents();

        // Store initial offset for repositioning on activation
        initialOffset = useHeadAnchor ? headOffset : offsetFromAnchor;
        hasStoredInitialPosition = true;

        sliderValue = GetTargetValueNormalized();

        lastSyncedTarget = sliderValue;
        ApplySliderValue(sliderValue);

        SetSliderActive(startActive);
        
        Debug.Log("<color=cyan>===== GlobalSliderController.initialization() END =====</color>");
        suppressNetworkBroadcast = false;
    }

    void Update()
    {
        if (followAnchor && sliderRoot != null && isActive)
        {
            Transform targetAnchor = anchor;
            Vector3 targetOffset = offsetFromAnchor;
            Quaternion targetRotation = (anchor != null) ? anchor.rotation : Quaternion.identity;

            if (useHeadAnchor && Camera.main != null)
            {
                targetAnchor = Camera.main.transform;
                targetOffset = headOffset;
                // targetRotation = targetAnchor.rotation; // Old: Inherit all axes
                
                // New: Use only Yaw from Camera, keep Up world-aligned
                Vector3 projectedForward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up);
                if (projectedForward.sqrMagnitude > 0.001f)
                {
                    targetRotation = Quaternion.LookRotation(projectedForward, Vector3.up);
                }
                else
                {
                    targetRotation = Quaternion.identity;
                }
            }
            
            if (targetAnchor != null)
            {
                // Position follows Head Gaze (so it stays in view)
                // Note: We use targetAnchor.rotation for position to keep it locked to view frustum
                // But we use targetRotation for the actual object orientation
                Vector3 basePos = targetAnchor.position + targetAnchor.rotation * targetOffset;
                sliderRoot.transform.position = basePos;
                
                // Rotation Logic
                Quaternion rot = lockRotation ? Quaternion.identity : targetRotation;
                
                if (useHeadAnchor)
                {
                   rot = targetRotation; 
                }

                if (verticalOnY) rot *= Quaternion.Euler(0f, 0f, 90f);
                sliderRoot.transform.rotation = rot;
            }
        }

        // Poll slider value as fallback when events are not firing
        if (pollSliderValue && pinchSliderComp != null && !isDragging)
        {
            float compVal = ReadSliderValueFromComponent();
            if (compVal >= 0f && Mathf.Abs(compVal - sliderValue) > 0.0001f)
            {
                sliderValue = compVal;
                ApplyToTarget(sliderValue, invokeEvent: true);
            }
        }
    }

    void LateUpdate()
    {
        if (isDragging) return;

        // Disable followAnchor after one full frame of activation
        if (followAnchor && isActive && followAnchorEnabledFrame >= 0)
        {
            // Check if at least one frame has passed since followAnchor was enabled
            if (Time.frameCount > followAnchorEnabledFrame)
            {
                followAnchor = false;
                followAnchorEnabledFrame = -1;
                Debug.Log("<color=yellow>followAnchor disabled after position update</color>");
            }
        }

        // float current = GetTargetValueNormalized();
        // if (current >= 0f && Mathf.Abs(current - lastSyncedTarget) > 0.0001f)
        // {
        //     lastSyncedTarget = current;
        //     SetValue(current, invokeEvent: false);
        // }
    }

    /// <summary>
    /// 외부(UI)에서 모드/축을 전환할 때 호출.
    /// </summary>
    public void SetMode(ControlMode newMode, SliceController.SliceAxis axis = SliceController.SliceAxis.None, bool syncFromTarget = true)
    {
        // Ensure manager reference is set (in case SetMode is called before initialization)
        if (manager == null)
        {
            manager = Manager.Instance ?? FindObjectOfType<Manager>();
            Debug.Log($"<color=orange>SetMode: Manager was null, re-acquired: {(manager != null ? "SUCCESS" : "FAILED")}</color>");
        }
        
        Debug.Log($"<color=cyan>SetMode called: mode={newMode}, axis={axis}, syncFromTarget={syncFromTarget}</color>");
        Debug.Log($"<color=cyan>Current references: manager={(manager != null ? "OK" : "NULL")}, velocityLoader={(velocityLoader != null ? "OK" : "NULL")}, SliceController={(SliceController != null ? "OK" : "NULL")}</color>");
        
        mode = newMode;
        sliceAxisLink = axis;

        if (syncFromTarget)
        {
            float current = GetTargetValueNormalized();
            Debug.Log($"<color=yellow>GetTargetValueNormalized returned: {current}</color>");
            
            if (current >= 0f)
            {
                sliderValue = current;
                lastSyncedTarget = current;
                ApplySliderValue(current);
                Debug.Log($"<color=green>Slider value set to: {current}</color>");
            }
            else
            {
                Debug.LogWarning($"<color=red>GetTargetValueNormalized returned negative value: {current}</color>");
            }
        }
    }

    void CreateSlider()
    {
        if (sliderObject != null)
        {
            sliderRoot = sliderObject;
            // Optionally reparent if needed, or just keep it where it is but control via script
            Transform parent = anchor != null && anchor.parent != null ? anchor.parent : transform;
            sliderRoot.transform.SetParent(parent, true); // worldPositionStays=true
        }
        else
        {
            Debug.LogError("GlobalSliderController: sliderObject is NOT assigned in Inspector!");
            return;
        }

        Vector3 basePos = anchor != null ? anchor.position + anchor.rotation * offsetFromAnchor : offsetFromAnchor;
        sliderRoot.transform.position = basePos;
        Quaternion rot = lockRotation ? Quaternion.identity : (anchor != null ? anchor.rotation : Quaternion.identity);
        if (verticalOnY) rot *= Quaternion.Euler(0f, 0f, 90f);
        sliderRoot.transform.rotation = rot;

        if (pinchSliderType != null)
        {
            pinchSliderComp = sliderRoot.GetComponentInChildren(pinchSliderType);
            if (pinchSliderComp == null)
            {
                pinchSliderComp = sliderRoot.AddComponent(pinchSliderType);
            }

            Transform thumb = FindThumb(sliderRoot.transform);
            SetThumbRoot(pinchSliderComp, thumb);
        }
        else
        {
            Debug.LogWarning("PinchSlider 타입을 찾지 못했습니다. MRTK 패키지를 확인하세요.");
        }
    }

    void HookSliderEvents()
    {
        if (pinchSliderComp == null || pinchSliderType == null) return;
        TryAddUnityEventGeneric(pinchSliderComp, "OnValueUpdated", nameof(OnSliderValueUpdatedHandler));
        TryAddUnityEventGeneric(pinchSliderComp, "OnInteractionStarted", nameof(OnSliderInteractionStartedHandler));
        TryAddUnityEventGeneric(pinchSliderComp, "OnInteractionEnded", nameof(OnSliderInteractionEndedHandler));
    }

    void OnSliderValueUpdatedHandler<T>(T eventData)
    {
        if (eventData == null) return;
        float? val = null;

        if (eventData is float f)
        {
            val = f;
        }
        else
        {
            var t = eventData.GetType();
            var prop = t.GetProperty("NewValue");
            if (prop != null && prop.PropertyType == typeof(float))
            {
                val = (float)prop.GetValue(eventData);
            }
            else
            {
                var field = t.GetField("NewValue");
                if (field != null && field.FieldType == typeof(float))
                {
                    val = (float)field.GetValue(eventData);
                }
            }
        }

        if (!val.HasValue) return;

        sliderValue = Mathf.Clamp01(val.Value);
        ApplyToTarget(sliderValue, invokeEvent: true);
    }

    void OnSliderInteractionStartedHandler<T>(T _)
    {
        isDragging = true;
    }

    void OnSliderInteractionEndedHandler<T>(T _)
    {
        isDragging = false;
    }

    public bool IsActive => isActive && sliderRoot != null && sliderRoot.activeSelf;
    public bool IsDragging => isDragging;
    public float GetValue() => sliderValue;

    public void EnsureInitialized()
    {
        if (manager == null)
        {
            manager = Manager.Instance ?? FindObjectOfType<Manager>();
        }

        if (velocityLoader == null && manager != null)
        {
            velocityLoader = manager.velocityLoader;
        }

        if (SliceController == null)
        {
            SliceController = FindObjectOfType<SliceController>();
        }

        if (sliderRoot == null || pinchSliderType == null)
        {
            initialization();
        }
    }

    public void SetValue(float value, bool invokeEvent = false, bool networkCall = false)
    {
        sliderValue = Mathf.Clamp01(value);
        lastSyncedTarget = sliderValue;
        ApplySliderValue(sliderValue);
        if (invokeEvent)
        {
            ApplyToTarget(sliderValue, invokeEvent: true, networkCall: networkCall);
        }
    }

    public void ApplyNetworkSlider(float value, ControlMode remoteMode, SliceController.SliceAxis axis)
    {
        suppressNetworkBroadcast = true;
        SetMode(remoteMode, axis, syncFromTarget: false);
        SetValue(value, invokeEvent: true, networkCall: true);
        suppressNetworkBroadcast = false;
    }

    void ApplySliderValue(float value)
    {
        if (pinchSliderComp == null || pinchSliderType == null) return;
        var valueProp = pinchSliderType.GetProperty("SliderValue");
        if (valueProp != null && valueProp.CanWrite) valueProp.SetValue(pinchSliderComp, Mathf.Clamp01(value));
        var field = pinchSliderType.GetField("SliderValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null) field.SetValue(pinchSliderComp, Mathf.Clamp01(value));
    }

    float ReadSliderValueFromComponent()
    {
        if (pinchSliderComp == null || pinchSliderType == null) return -1f;

        var valueProp = pinchSliderType.GetProperty("SliderValue");
        if (valueProp != null && valueProp.CanRead)
        {
            object value = valueProp.GetValue(pinchSliderComp);
            if (value is float floatValue) return Mathf.Clamp01(floatValue);
        }

        var field = pinchSliderType.GetField("SliderValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            object value = field.GetValue(pinchSliderComp);
            if (value is float floatValue) return Mathf.Clamp01(floatValue);
        }

        return -1f;
    }

    public void SetSliderActive(bool active)
    {
        isActive = active;
        if (sliderRoot != null)
        {
            if (active && hasStoredInitialPosition)
            {
                // Reset to the current placement preset when activating.
                if (useHeadAnchor)
                {
                    headOffset = initialOffset;
                }
                else
                {
                    offsetFromAnchor = initialOffset;
                }
                
                // Enable followAnchor on every activation to update position
                // It will be disabled after one frame in LateUpdate()
                followAnchor = true;
                followAnchorEnabledFrame = Time.frameCount;
                Debug.Log($"<color=cyan>followAnchor enabled at frame {Time.frameCount}</color>");
            }
            sliderRoot.SetActive(active);
        }
    }

    public void ConfigureExhibitionPlacement(Transform newAnchor, Vector3 localOffset)
    {
        anchor = newAnchor;
        useHeadAnchor = false;
        offsetFromAnchor = localOffset;
        initialOffset = localOffset;
        hasStoredInitialPosition = true;
        followAnchor = true;
        followAnchorEnabledFrame = Time.frameCount;
    }

    SliceController.SliceAxis ResolveTargetAxis()
    {
        if (mode != ControlMode.SlicePosition) return SliceController.SliceAxis.None;
        if (!linkSliceController || SliceController == null) return SliceController.SliceAxis.None;
        if (sliceAxisLink == SliceController.SliceAxis.X_Axis) return SliceController.SliceAxis.X_Axis;
        if (sliceAxisLink == SliceController.SliceAxis.Y_Axis) return SliceController.SliceAxis.Y_Axis;
        return SliceController.currentAxis;
    }

    /// <summary>
    /// 현재 모드/축에 따라 슬라이더 값(0-1)을 대상 컴포넌트에 적용.
    /// </summary>
    public void ApplyToTarget(float value, bool invokeEvent = false, bool networkCall = false)
    {
        // Playback speed: 0 = Fast (small interval), 1 = Slow (large interval)
        // Slider 0 -> fastest (0.05s interval), Slider 1 -> slowest (0.5s interval)
        float playbackInterval = Mathf.Lerp(0.05f, 0.5f, value);

        switch (mode)
        {
            case ControlMode.VelocityPlayback:
                if (manager != null)
                    manager.SetVelocityPlaybackSpeed(playbackInterval);
                break;

            case ControlMode.WssPlayback:
                if (manager != null)
                    manager.SetWssPlaybackSpeed(playbackInterval);
                break;

            case ControlMode.StreamlinePlayback:
                if (manager != null)
                    manager.SetStreamlinePlaybackSpeed(playbackInterval);
                break;

            case ControlMode.SlicePosition:
                if (linkSliceController && SliceController != null)
                {
                    var axis = ResolveTargetAxis();
                    if (axis == SliceController.SliceAxis.None) axis = SliceController.currentAxis;
                    SliceController.SetSlicePositionForAxis(axis, value);
                }
                break;

            case ControlMode.DensityX:
                if (velocityLoader != null)
                    velocityLoader.displayStepX = Mathf.RoundToInt(Mathf.Lerp(1, 10, value));
                break;

            case ControlMode.DensityY:
                if (velocityLoader != null)
                    velocityLoader.displayStepY = Mathf.RoundToInt(Mathf.Lerp(1, 10, value));
                break;

            case ControlMode.DensityZ:
                if (velocityLoader != null)
                    velocityLoader.displayStepZ = Mathf.RoundToInt(Mathf.Lerp(1, 10, value));
                break;

            case ControlMode.FrameControl:
                var buttonController = ButtonControllerManager.Instance ?? FindObjectOfType<ButtonControllerManager>();
                if (buttonController != null)
                    buttonController.SetAllLoadersFrameBySlider(value);
                break;

            case ControlMode.VesselSize:
                if (manager != null)
                    manager.SetObjectScale(value);
                break;

            case ControlMode.Rotation:
                if (manager != null)
                    manager.SetObjectRotation(value);
                break;
        }

        // Broadcast to network if needed
        if (!suppressNetworkBroadcast && invokeEvent && !networkCall)
        {
            var photon = FindObjectOfType<PhotonSyncService>();
            if (photon != null)
                photon.BroadcastSliderValue(value, mode, ResolveTargetAxis(), isActive);
        }
    }

    /// <summary>
    /// 현재 모드/축의 대상 값을 0-1로 읽어 반환. 매핑 불가시 -1 반환.
    /// </summary>
    public float GetTargetValueNormalized()
    {
        switch (mode)
        {
            case ControlMode.VelocityPlayback:
                if (manager != null)
                    return Mathf.InverseLerp(0.05f, 0.5f, manager.velocityPlaybackSpeed);
                break;

            case ControlMode.WssPlayback:
                if (manager != null)
                    return Mathf.InverseLerp(0.05f, 0.5f, manager.wssPlaybackSpeed);
                break;

            case ControlMode.StreamlinePlayback:
                if (manager != null)
                    return Mathf.InverseLerp(0.05f, 0.5f, manager.streamlinePlaybackSpeed);
                break;

            case ControlMode.SlicePosition:
                if (linkSliceController && SliceController != null)
                    return SliceController.GetSlicePosition(ResolveTargetAxis());
                break;

            case ControlMode.DensityX:
                if (velocityLoader != null)
                    return Mathf.InverseLerp(1, 10, velocityLoader.displayStepX);
                break;

            case ControlMode.DensityY:
                if (velocityLoader != null)
                    return Mathf.InverseLerp(1, 10, velocityLoader.displayStepY);
                break;

            case ControlMode.DensityZ:
                if (velocityLoader != null)
                    return Mathf.InverseLerp(1, 10, velocityLoader.displayStepZ);
                break;

            case ControlMode.VesselSize:
                // Default mid-point (1.0x scale = 0.5)
                return 0.5f;

            case ControlMode.Rotation:
                // Default center (0 degrees = 0.5)
                return 0.5f;
        }
        return 0.5f;
    }

    public SliceController.SliceAxis GetResolvedAxis()
    {
        return ResolveTargetAxis();
    }

    Transform FindThumb(Transform root)
    {
        if (root == null) return null;
        var t = root.Find("Thumb");
        if (t != null) return t;
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.Contains("Thumb")) return child;
        }
        return null;
    }

    void SetThumbRoot(Component slider, Transform thumb)
    {
        if (slider == null || thumb == null || pinchSliderType == null) return;

        void TryAssign(MemberInfo member)
        {
            if (member == null) return;
            if (member is PropertyInfo prop && prop.CanWrite)
            {
                var t = prop.PropertyType;
                if (t.IsAssignableFrom(typeof(Transform))) prop.SetValue(slider, thumb);
                else if (t.IsAssignableFrom(typeof(GameObject))) prop.SetValue(slider, thumb.gameObject);
            }
            else if (member is FieldInfo field)
            {
                var t = field.FieldType;
                if (t.IsAssignableFrom(typeof(Transform))) field.SetValue(slider, thumb);
                else if (t.IsAssignableFrom(typeof(GameObject))) field.SetValue(slider, thumb.gameObject);
            }
        }

        TryAssign(pinchSliderType.GetProperty("ThumbRoot"));
        TryAssign(pinchSliderType.GetField("ThumbRoot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
    }

    void TryAddUnityEventGeneric(object target, string eventFieldName, string handlerName)
    {
        if (target == null) return;
        var type = target.GetType();
        var prop = type.GetProperty(eventFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null)
        {
            var propEvt = prop.GetValue(target);
            if (AddListenerToUnityEvent(propEvt, handlerName)) return;
        }
        var field = type.GetField(eventFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null) return;
        AddListenerToUnityEvent(field.GetValue(target), handlerName);
    }

    bool AddListenerToUnityEvent(object evtObj, string handlerName)
    {
        if (evtObj == null) return false;
        var evtType = evtObj.GetType();
        var addMethod = evtType.GetMethod("AddListener");
        if (addMethod == null) return false;
        var parameters = addMethod.GetParameters();
        if (parameters.Length != 1) return false;
        var unityActionType = parameters[0].ParameterType;
        var genericArgs = unityActionType.IsGenericType ? unityActionType.GetGenericArguments() : null;
        var argType = (genericArgs != null && genericArgs.Length == 1) ? genericArgs[0] : typeof(object);

        var handlerMethod = GetType().GetMethod(handlerName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (handlerMethod == null) return false;
        var constructedHandler = handlerMethod.IsGenericMethodDefinition ? handlerMethod.MakeGenericMethod(argType) : handlerMethod;
        var del = Delegate.CreateDelegate(unityActionType, this, constructedHandler, false);
        if (del == null) return false;

        addMethod.Invoke(evtObj, new object[] { del });
        return true;
    }

    Type FindType(params string[] typeNames)
    {
        foreach (var name in typeNames)
        {
            var t = Type.GetType(name);
            if (t != null) return t;
        }
        return null;
    }
}
