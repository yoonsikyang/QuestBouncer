using System.Linq;
using Microsoft.MixedReality.Toolkit.UI;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using Unity.PolySpatial.InputDevices;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

[DefaultExecutionOrder(1200)]
public sealed class HanyangVisionOSTouchBridge : MonoBehaviour
{
    private const float MaxPressDurationSeconds = 1.5f;

    private Interactable pressedInteractable;
    private int pressedClickCount;
    private float pressedAt;
    private bool pointerWasActive;
    private SpatialPointerPhase lastSpatialPhase;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var runner = new GameObject(nameof(HanyangVisionOSTouchBridge));
        DontDestroyOnLoad(runner);
        runner.hideFlags = HideFlags.HideAndDontSave;
        runner.AddComponent<HanyangVisionOSTouchBridge>();
    }

    private void Update()
    {
        if (!ShouldRun())
            return;

        UpdateSpatialPointer();
        UpdateMouseOrTouchFallback();
    }

    private static bool ShouldRun()
    {
#if UNITY_VISIONOS && !UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }

    private void UpdateSpatialPointer()
    {
#if ENABLE_INPUT_SYSTEM
        var device = InputSystem.devices.OfType<SpatialPointerDevice>().FirstOrDefault();
        if (device == null || device.primaryInput == null)
            return;

        var state = device.primaryInput.ReadValue();
        var phase = state.phase;
        var isActive = phase == SpatialPointerPhase.Began || phase == SpatialPointerPhase.Moved;
        var ended = pointerWasActive && (phase == SpatialPointerPhase.Ended || phase == SpatialPointerPhase.Cancelled || phase == SpatialPointerPhase.None);

        if (!pointerWasActive && isActive)
            BeginPress(FindInteractable(state.targetObject) ?? FindFocusedInteractable(), $"spatial {phase}");

        if (isActive)
        {
            var currentTarget = FindInteractable(state.targetObject);
            if (currentTarget != null)
                pressedInteractable = currentTarget;
        }

        if (ended)
            CompletePress(FindInteractable(state.targetObject) ?? FindFocusedInteractable(), $"spatial {phase}");

        pointerWasActive = isActive;
        lastSpatialPhase = phase;
#endif
    }

    private void UpdateMouseOrTouchFallback()
    {
#if ENABLE_INPUT_SYSTEM
        if (WasPrimaryPressStarted())
            BeginPress(FindFocusedInteractable(), "pointer");

        if (WasPrimaryPressCompleted())
            CompletePress(FindFocusedInteractable(), "pointer");
#endif
    }

    private void BeginPress(Interactable target, string source)
    {
        if (target == null)
            return;

        pressedInteractable = target;
        pressedClickCount = target.ClickCount;
        pressedAt = Time.unscaledTime;
        Debug.Log($"[HanyangVisionOSTouchBridge] press began on {GetPath(target.transform)} via {source}.");
    }

    private void CompletePress(Interactable currentTarget, string source)
    {
        var target = currentTarget != null ? currentTarget : pressedInteractable;
        if (target == null)
            return;

        var elapsed = Time.unscaledTime - pressedAt;
        var shouldTrigger = target.ClickCount == pressedClickCount && elapsed <= MaxPressDurationSeconds;
        if (shouldTrigger)
        {
            target.TriggerOnClick(true);
            Debug.Log($"[HanyangVisionOSTouchBridge] TriggerOnClick invoked for {GetPath(target.transform)} via {source}.");
        }

        pressedInteractable = null;
    }

    private static Interactable FindInteractable(GameObject targetObject)
    {
        if (targetObject == null || !targetObject.activeInHierarchy)
            return null;

        var interactable = targetObject.GetComponentInParent<Interactable>();
        return IsUsableButton(interactable) ? interactable : null;
    }

    private static Interactable FindFocusedInteractable()
    {
        return Resources.FindObjectsOfTypeAll<Interactable>()
            .Where(IsUsableButton)
            .Where(interactable => interactable.HasFocus)
            .OrderByDescending(interactable => interactable.transform.position.z)
            .FirstOrDefault();
    }

    private static bool IsUsableButton(Interactable interactable)
    {
        if (interactable == null || !interactable.gameObject.scene.IsValid() || !interactable.gameObject.activeInHierarchy)
            return false;

        var path = GetPath(interactable.transform);
        return path.Contains("Button Parent") || interactable.GetComponent<ButtonConfigHelper>() != null;
    }

#if ENABLE_INPUT_SYSTEM
    private static bool WasPrimaryPressStarted()
    {
        return Mouse.current?.leftButton.wasPressedThisFrame == true ||
               Touchscreen.current?.primaryTouch.press.wasPressedThisFrame == true ||
               Pointer.current?.press.wasPressedThisFrame == true;
    }

    private static bool WasPrimaryPressCompleted()
    {
        return Mouse.current?.leftButton.wasReleasedThisFrame == true ||
               Touchscreen.current?.primaryTouch.press.wasReleasedThisFrame == true ||
               Pointer.current?.press.wasReleasedThisFrame == true;
    }
#endif

    private static string GetPath(Transform transform)
    {
        var path = transform.name;
        var current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
