using Microsoft.MixedReality.Toolkit.Audio;
using TMPro;
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using UnityEngine.Networking;

/// <summary>
/// Reuses the blue guide panel both for menu intro and manipulation guidance.
/// When the exhibition menu is shown, the panel anchors above the menu.
/// </summary>
public class ExhibitionGuideManager : MonoBehaviour
{
    private enum GuideMode
    {
        Hidden,
        MenuIntro,
        Manipulation,
        Velocity,
        Slice,
        Streamline,
        Wss,
    }

    private enum GuideStep
    {
        ExplainManipulation,
        MoveToTarget,
        ReturnToOrigin,
        RotateModel,
        ZoomIn,
        Complete,
    }

    private enum VelocityGuideStep
    {
        ObserveFlow,
        AdjustSpeed,
        AdjustSpacing,
        RotateVessel,
        Complete,
    }

    private enum SliceGuideStep
    {
        ExplainCapabilities,
        Observe2DView,
        MoveSliceInside,
        FreeExplore,
        Complete,
    }

    private enum StreamlineGuideStep
    {
        ExplainCapabilities,
        ObserveFlow,
        AdjustSpeed,
        FreeExplore,
        Complete,
    }

    private enum WssGuideStep
    {
        ExplainCapabilities,
        ObserveDistribution,
        AdjustSpeed,
        FreeExplore,
        Complete,
    }

    private static ExhibitionGuideManager instance;

    [SerializeField] private Vector3 worldPromptOffset = new Vector3(-0.28f, -0.08f, 0.0f);
    [SerializeField] private Vector3 menuPromptOffset = new Vector3(0.0f, 0.12f, 0.0f);
    [SerializeField] private bool lockMenuPromptRotation = true;
    [SerializeField] private Vector3 panelScale = new Vector3(0.42f, 0.17f, 0.008f);
    [SerializeField] private string logoResourcePath = "Exhibition/logo";
    [SerializeField] private float logoHeight = 0.025f;
    [SerializeField] private float logoMaxWidth = 0.093f;
    [SerializeField] private Vector2 logoPadding = new Vector2(0.018f, 0.014f);
    [SerializeField] private Vector3 titleLocalPosition = new Vector3(0.0f, 0.035f, -0.006f);
    [SerializeField] private Vector3 bodyLocalPosition = new Vector3(0.0f, -0.03f, -0.006f);
    [SerializeField] private float titleFontSize = 9.5f;
    [SerializeField] private float bodyFontSize = 6.2f;
    [SerializeField] private float textScale = 0.022f;
    [SerializeField] private float targetDistance = 0.12f;
    [SerializeField] private Vector3 targetOffset = new Vector3(-0.06f, -0.01f, 0.0f);
    [SerializeField] private float promptTargetForwardOffset = -0.2f;
    [SerializeField] private float moveCompletionDistance = 0.08f;
    [SerializeField] private float manipulationExplainSeconds = 5f;
    [SerializeField] private float manipulationExplainFallbackSeconds = 12f;
    [SerializeField] private float rotationRequiredSeconds = 5f;
    [SerializeField] private float rotationMotionDelta = 1f;
    [SerializeField] private float zoomScaleMultiplier = 1.15f;
    [SerializeField] private float zoomCompletionDelaySeconds = 5f;
    [SerializeField] private float velocityObserveAfterSpeechSeconds = 0f;
    [SerializeField] private float velocityObserveFallbackSeconds = 10f;
    [SerializeField] private float velocityAdjustmentSeconds = 5f;
    [SerializeField] private float velocityFreeExploreAfterSpeechSeconds = 20f;
    [SerializeField] private float velocityFreeExploreFallbackSeconds = 25f;
    [SerializeField] private float sliceExplainSeconds = 5f;
    [SerializeField] private float sliceObserveSeconds = 5f;
    [SerializeField] private float sliceMoveCompletionDelaySeconds = 3f;
    [SerializeField] private float sliceFreeExploreSeconds = 20f;
    [SerializeField] private float sliceMoveDistanceThreshold = 0.01f;
    [SerializeField] private float sliceMoveRotationThreshold = 3f;
    [SerializeField] private float sliceInsideMin = 0.2f;
    [SerializeField] private float sliceInsideMax = 0.8f;
    [SerializeField] private float streamlineExplainFallbackSeconds = 8f;
    [SerializeField] private float streamlineObserveAfterSpeechSeconds = 5f;
    [SerializeField] private float streamlineObserveFallbackSeconds = 10f;
    [SerializeField] private float streamlineAdjustmentSeconds = 5f;
    [SerializeField] private float streamlineFreeExploreAfterSpeechSeconds = 20f;
    [SerializeField] private float streamlineFreeExploreFallbackSeconds = 25f;
    [SerializeField] private float wssExplainFallbackSeconds = 8f;
    [SerializeField] private float wssObserveAfterSpeechSeconds = 5f;
    [SerializeField] private float wssObserveFallbackSeconds = 10f;
    [SerializeField] private float wssAdjustmentSeconds = 5f;
    [SerializeField] private float wssFreeExploreAfterSpeechSeconds = 20f;
    [SerializeField] private float wssFreeExploreFallbackSeconds = 25f;
    [SerializeField] private float sliderStepThreshold = 0.05f;
    [SerializeField] private Color panelColor = new Color(0.02f, 0.08f, 0.16f, 0.82f);
    [SerializeField] private Color markerColor = new Color(1.0f, 0.18f, 0.18f, 1.0f);
    [SerializeField] private float markerScale = 0.04f;
    [SerializeField] private float markerPulseScale = 0.35f;
    [SerializeField] private float markerPulseSpeed = 2.6f;
    [SerializeField] private bool enableTts = true;
    [SerializeField] private string ttsVoiceName = string.Empty;
    [SerializeField] private string audioResourceRoot = "audio";

    private Camera mainCamera;
    private Manager manager;
    private ButtonControllerManager buttonController;
    private Transform objectParent;
    private TMP_FontAsset guideFontAsset;
    private GameObject promptRoot;
    private TextMeshPro titleText;
    private TextMeshPro bodyText;
    private GameObject logoObject;
    private MeshRenderer logoRenderer;
    private Material logoMaterial;
    private GameObject targetMarker;
    private AudioSource ttsAudioSource;
    private readonly Dictionary<string, AudioClip> promptAudioCache = new Dictionary<string, AudioClip>();
    private Coroutine pendingPromptPlaybackCoroutine;
    private GuideMode currentMode;
    private GuideStep currentStep;
    private VelocityGuideStep currentVelocityStep;
    private SliceGuideStep currentSliceStep;
    private StreamlineGuideStep currentStreamlineStep;
    private WssGuideStep currentWssStep;
    private bool guideStarted;
    private Quaternion lastRotateRotation;
    private bool rotateTrackingInitialized;
    private float rotateAccumulatedSeconds;
    private float zoomCompletionTimer;
    private bool zoomThresholdReached;
    private float zoomBaselineScaleMagnitude;
    private Vector3 moveTargetWorldPosition;
    private Vector3 originWorldPosition;
    private Vector3 initialLocalScale;
    private bool moveTargetInitialized;
    private Vector3 promptWorldPosition;
    private bool promptPositionInitialized;
    private Transform menuAnchor;
    private float manipulationExplainTimer;
    private bool manipulationExplainSpeechStarted;
    private bool manipulationExplainSpeechObserved;
    private float velocityObserveTimer;
    private bool velocityObserveSpeechStarted;
    private bool velocityObserveSpeechObserved;
    private float velocitySliderBaseline;
    private bool velocitySliderBaselineCaptured;
    private bool velocityAdjustmentTriggered;
    private float velocityAdjustmentTimer;
    private float velocityFreeExploreTimer;
    private bool velocityFreeExploreSpeechStarted;
    private bool velocityFreeExploreSpeechObserved;
    private float sliceStepTimer;
    private bool sliceExplainSpeechStarted;
    private bool sliceExplainSpeechObserved;
    private bool sliceObserveSpeechStarted;
    private bool sliceObserveSpeechObserved;
    private bool sliceInteractionDetected;
    private bool sliceMoveCompleted;
    private Vector3 sliceMoveStartPosition;
    private Quaternion sliceMoveStartRotation;
    private bool sliceMoveStartPositionCaptured;
    private float streamlineStepTimer;
    private bool streamlineExplainSpeechStarted;
    private bool streamlineExplainSpeechObserved;
    private bool streamlineObserveSpeechStarted;
    private bool streamlineObserveSpeechObserved;
    private float streamlineSliderBaseline;
    private bool streamlineSliderBaselineCaptured;
    private bool streamlineAdjustmentTriggered;
    private float streamlineAdjustmentTimer;
    private bool streamlineFreeExploreSpeechStarted;
    private bool streamlineFreeExploreSpeechObserved;
    private float wssStepTimer;
    private float wssSliderBaseline;
    private bool wssSliderBaselineCaptured;
    private bool wssAdjustmentTriggered;
    private bool wssExplainSpeechStarted;
    private bool wssExplainSpeechObserved;
    private bool wssObserveSpeechStarted;
    private bool wssObserveSpeechObserved;
    private float wssAdjustmentTimer;
    private bool wssFreeExploreSpeechStarted;
    private bool wssFreeExploreSpeechObserved;
    private string lastSpokenPromptKey = string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    public static ExhibitionGuideManager EnsureInstance()
    {
        if (instance != null)
        {
            return instance;
        }

        ExhibitionGuideManager existing = FindObjectOfType<ExhibitionGuideManager>();
        if (existing != null)
        {
            instance = existing;
            return instance;
        }

        GameObject root = new GameObject("ExhibitionGuideManager");
        instance = root.AddComponent<ExhibitionGuideManager>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        CreatePromptUI();
        CreateTargetMarker();
        CreateTtsComponents();
        SetGuideVisible(false);
    }

    private void Update()
    {
        EnsureReferences();

        if (!guideStarted || mainCamera == null)
        {
            SetGuideVisible(false);
            return;
        }

        if ((currentMode == GuideMode.Manipulation || currentMode == GuideMode.Velocity || currentMode == GuideMode.Slice || currentMode == GuideMode.Streamline || currentMode == GuideMode.Wss) && objectParent == null)
        {
            SetGuideVisible(false);
            return;
        }

        SetGuideVisible(true);
        UpdatePromptTransform();
        UpdateTargetMarker();

        if (currentMode == GuideMode.Manipulation)
        {
            AdvanceIfNeeded();
        }
        else if (currentMode == GuideMode.Velocity)
        {
            AdvanceVelocityGuideIfNeeded();
        }
        else if (currentMode == GuideMode.Slice)
        {
            AdvanceSliceGuideIfNeeded();
        }
        else if (currentMode == GuideMode.Streamline)
        {
            AdvanceStreamlineGuideIfNeeded();
        }
        else if (currentMode == GuideMode.Wss)
        {
            AdvanceWssGuideIfNeeded();
        }

        RefreshPromptText();
        SpeakPromptIfChanged();
    }

    public void ShowMenuIntro(Transform anchor)
    {
        EnsureReferences();
        menuAnchor = anchor;
        currentMode = GuideMode.MenuIntro;
        guideStarted = true;
        promptPositionInitialized = false;
        moveTargetInitialized = false;
        RefreshPromptText();
        SetGuideVisible(true);
    }

    public void StartManipulationGuide()
    {
        EnsureReferences();
        if (objectParent == null)
        {
            return;
        }

        if (buttonController != null &&
            buttonController.exhibitionMenu != null &&
            buttonController.exhibitionMenu.activeInHierarchy)
        {
            menuAnchor = buttonController.exhibitionMenu.transform;
        }

        currentMode = GuideMode.Manipulation;
        guideStarted = true;
        currentStep = GuideStep.ExplainManipulation;
        manipulationExplainTimer = 0f;
        manipulationExplainSpeechStarted = false;
        manipulationExplainSpeechObserved = false;
        lastSpokenPromptKey = string.Empty;
        rotateTrackingInitialized = false;
        rotateAccumulatedSeconds = 0f;
        zoomCompletionTimer = 0f;
        zoomThresholdReached = false;
        zoomBaselineScaleMagnitude = 0f;
        moveTargetInitialized = false;
        promptPositionInitialized = false;
        originWorldPosition = objectParent.position;
        initialLocalScale = objectParent.localScale;
        PreloadPromptAudio(
            "exp01_00",
            "exp01_01",
            "exp01_02",
            "exp01_03",
            "exp01_04",
            "exp01_done");
        InitializeMoveTarget();
        StopGuidePromptSpeech();
        RefreshPromptText();
        SetGuideVisible(true);
    }

    public void StartVelocityGuide()
    {
        EnsureReferences();
        if (objectParent == null)
        {
            return;
        }

        if (buttonController != null &&
            buttonController.exhibitionMenu != null &&
            buttonController.exhibitionMenu.activeInHierarchy)
        {
            menuAnchor = buttonController.exhibitionMenu.transform;
        }

        currentMode = GuideMode.Velocity;
        guideStarted = true;
        currentVelocityStep = VelocityGuideStep.ObserveFlow;
        velocityObserveTimer = 0f;
        velocityObserveSpeechStarted = false;
        velocityObserveSpeechObserved = false;
        velocitySliderBaseline = 0f;
        velocitySliderBaselineCaptured = false;
        velocityAdjustmentTriggered = false;
        velocityAdjustmentTimer = 0f;
        velocityFreeExploreTimer = 0f;
        velocityFreeExploreSpeechStarted = false;
        velocityFreeExploreSpeechObserved = false;
        promptPositionInitialized = false;
        lastSpokenPromptKey = string.Empty;
        StopGuidePromptSpeech();
        if (buttonController != null)
        {
            buttonController.HideExhibitionSlider();
        }

        RefreshPromptText();
        SetGuideVisible(true);
    }

    public void StartSliceGuide()
    {
        EnsureReferences();
        if (objectParent == null)
        {
            return;
        }

        if (buttonController != null &&
            buttonController.exhibitionMenu != null &&
            buttonController.exhibitionMenu.activeInHierarchy)
        {
            menuAnchor = buttonController.exhibitionMenu.transform;
        }

        currentMode = GuideMode.Slice;
        guideStarted = true;
        currentSliceStep = SliceGuideStep.ExplainCapabilities;
        sliceStepTimer = 0f;
        sliceExplainSpeechStarted = false;
        sliceExplainSpeechObserved = false;
        sliceObserveSpeechStarted = false;
        sliceObserveSpeechObserved = false;
        sliceInteractionDetected = false;
        sliceMoveCompleted = false;
        sliceMoveStartPositionCaptured = false;
        promptPositionInitialized = false;
        lastSpokenPromptKey = string.Empty;
        StopGuidePromptSpeech();

        if (buttonController != null)
        {
            buttonController.HideExhibitionSlider();
            buttonController.PrepareExhibitionSliceVisualization();
            buttonController.ShowExhibitionSliceToggleMenu();
        }

        RefreshPromptText();
        SetGuideVisible(true);
    }

    public void StartWssGuide()
    {
        EnsureReferences();
        if (objectParent == null)
        {
            return;
        }

        if (buttonController != null &&
            buttonController.exhibitionMenu != null &&
            buttonController.exhibitionMenu.activeInHierarchy)
        {
            menuAnchor = buttonController.exhibitionMenu.transform;
        }

        currentMode = GuideMode.Wss;
        guideStarted = true;
        currentWssStep = WssGuideStep.ExplainCapabilities;
        wssStepTimer = 0f;
        wssSliderBaseline = 0f;
        wssSliderBaselineCaptured = false;
        wssAdjustmentTriggered = false;
        wssAdjustmentTimer = 0f;
        wssExplainSpeechStarted = false;
        wssExplainSpeechObserved = false;
        wssObserveSpeechStarted = false;
        wssObserveSpeechObserved = false;
        wssFreeExploreSpeechStarted = false;
        wssFreeExploreSpeechObserved = false;
        promptPositionInitialized = false;
        lastSpokenPromptKey = string.Empty;
        StopGuidePromptSpeech();

        if (buttonController != null)
        {
            buttonController.HideExhibitionSlider();
            buttonController.HideExhibitionSliceToggleMenu();
        }

        RefreshPromptText();
        SetGuideVisible(true);
    }

    public void StartStreamlineGuide()
    {
        EnsureReferences();
        if (objectParent == null)
        {
            return;
        }

        if (buttonController != null &&
            buttonController.exhibitionMenu != null &&
            buttonController.exhibitionMenu.activeInHierarchy)
        {
            menuAnchor = buttonController.exhibitionMenu.transform;
        }

        currentMode = GuideMode.Streamline;
        guideStarted = true;
        currentStreamlineStep = StreamlineGuideStep.ExplainCapabilities;
        streamlineStepTimer = 0f;
        streamlineExplainSpeechStarted = false;
        streamlineExplainSpeechObserved = false;
        streamlineObserveSpeechStarted = false;
        streamlineObserveSpeechObserved = false;
        streamlineSliderBaseline = 0f;
        streamlineSliderBaselineCaptured = false;
        streamlineAdjustmentTriggered = false;
        streamlineAdjustmentTimer = 0f;
        streamlineFreeExploreSpeechStarted = false;
        streamlineFreeExploreSpeechObserved = false;
        promptPositionInitialized = false;
        lastSpokenPromptKey = string.Empty;
        StopGuidePromptSpeech();

        if (buttonController != null)
        {
            buttonController.HideExhibitionSlider();
        }

        RefreshPromptText();
        SetGuideVisible(true);
    }

    public void StopGuide()
    {
        guideStarted = false;
        currentMode = GuideMode.Hidden;
        menuAnchor = null;
        SetGuideVisible(false);
    }

    public void ShowCompletionPrompt(ButtonControllerManager.ExhibitionExperience experience, Transform anchor)
    {
        EnsureReferences();
        menuAnchor = anchor;
        promptPositionInitialized = false;
        moveTargetInitialized = false;
        lastSpokenPromptKey = string.Empty;
        StopGuidePromptSpeech();

        switch (experience)
        {
            case ButtonControllerManager.ExhibitionExperience.Manipulation:
                currentMode = GuideMode.Manipulation;
                currentStep = GuideStep.Complete;
                break;
            case ButtonControllerManager.ExhibitionExperience.Velocity:
                currentMode = GuideMode.Velocity;
                currentVelocityStep = VelocityGuideStep.Complete;
                break;
            case ButtonControllerManager.ExhibitionExperience.Slice:
                currentMode = GuideMode.Slice;
                currentSliceStep = SliceGuideStep.Complete;
                break;
            case ButtonControllerManager.ExhibitionExperience.Streamline:
                currentMode = GuideMode.Streamline;
                currentStreamlineStep = StreamlineGuideStep.Complete;
                break;
            case ButtonControllerManager.ExhibitionExperience.Wss:
                currentMode = GuideMode.Wss;
                currentWssStep = WssGuideStep.Complete;
                break;
            default:
                StopGuide();
                return;
        }

        guideStarted = true;
        RefreshPromptText();
        SetGuideVisible(true);
    }

    public bool IsManualFinishAvailable(ButtonControllerManager.ExhibitionExperience experience)
    {
        switch (experience)
        {
            case ButtonControllerManager.ExhibitionExperience.Velocity:
                return currentMode == GuideMode.Velocity &&
                    currentVelocityStep == VelocityGuideStep.RotateVessel;
            case ButtonControllerManager.ExhibitionExperience.Slice:
                return currentMode == GuideMode.Slice &&
                    currentSliceStep == SliceGuideStep.FreeExplore;
            case ButtonControllerManager.ExhibitionExperience.Streamline:
                return currentMode == GuideMode.Streamline &&
                    currentStreamlineStep == StreamlineGuideStep.FreeExplore;
            case ButtonControllerManager.ExhibitionExperience.Wss:
                return currentMode == GuideMode.Wss &&
                    currentWssStep == WssGuideStep.FreeExplore;
            default:
                return false;
        }
    }

    public bool ShouldShowFinishButton(ButtonControllerManager.ExhibitionExperience experience)
    {
        switch (experience)
        {
            case ButtonControllerManager.ExhibitionExperience.Velocity:
                return currentMode == GuideMode.Velocity &&
                    (currentVelocityStep == VelocityGuideStep.RotateVessel || currentVelocityStep == VelocityGuideStep.Complete);
            case ButtonControllerManager.ExhibitionExperience.Slice:
                return currentMode == GuideMode.Slice &&
                    (currentSliceStep == SliceGuideStep.FreeExplore || currentSliceStep == SliceGuideStep.Complete);
            case ButtonControllerManager.ExhibitionExperience.Streamline:
                return currentMode == GuideMode.Streamline &&
                    (currentStreamlineStep == StreamlineGuideStep.FreeExplore || currentStreamlineStep == StreamlineGuideStep.Complete);
            case ButtonControllerManager.ExhibitionExperience.Wss:
                return currentMode == GuideMode.Wss &&
                    (currentWssStep == WssGuideStep.FreeExplore || currentWssStep == WssGuideStep.Complete);
            default:
                return false;
        }
    }

    public bool IsCompletionStep(ButtonControllerManager.ExhibitionExperience experience)
    {
        switch (experience)
        {
            case ButtonControllerManager.ExhibitionExperience.Velocity:
                return currentMode == GuideMode.Velocity && currentVelocityStep == VelocityGuideStep.Complete;
            case ButtonControllerManager.ExhibitionExperience.Slice:
                return currentMode == GuideMode.Slice && currentSliceStep == SliceGuideStep.Complete;
            case ButtonControllerManager.ExhibitionExperience.Streamline:
                return currentMode == GuideMode.Streamline && currentStreamlineStep == StreamlineGuideStep.Complete;
            case ButtonControllerManager.ExhibitionExperience.Wss:
                return currentMode == GuideMode.Wss && currentWssStep == WssGuideStep.Complete;
            default:
                return false;
        }
    }

    public void ResetOverlayState(Transform anchor, bool showMenuIntro)
    {
        EnsureReferences();
        menuAnchor = anchor;
        promptPositionInitialized = false;
        moveTargetInitialized = false;
        lastSpokenPromptKey = string.Empty;

        if (showMenuIntro)
        {
            currentMode = GuideMode.MenuIntro;
            guideStarted = true;
            RefreshPromptText();
            SetGuideVisible(true);
            return;
        }

        if (guideStarted)
        {
            RefreshPromptText();
            SetGuideVisible(true);
        }
    }

    private void EnsureReferences()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (manager == null)
        {
            manager = Manager.Instance ?? FindObjectOfType<Manager>();
        }

        if (buttonController == null)
        {
            buttonController = FindObjectOfType<ButtonControllerManager>();
        }

        if (manager != null && objectParent == null && manager.ObjectParent != null)
        {
            objectParent = manager.ObjectParent.transform;
        }

        if (guideFontAsset == null)
        {
            guideFontAsset = ResolveGuideFont();
            ApplyGuideFont();
        }

        EnsureGuideAudioSource();
    }

    private TMP_FontAsset ResolveGuideFont()
    {
        if (buttonController != null && buttonController.exhibitionGuideFont != null)
        {
            return buttonController.exhibitionGuideFont;
        }

        TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        foreach (TMP_FontAsset font in fonts)
        {
            if (font != null && font.name.Contains("NotoSansKR"))
            {
                return font;
            }
        }

        return TMP_Settings.defaultFontAsset;
    }

    private void ApplyGuideFont()
    {
        if (guideFontAsset == null)
        {
            return;
        }

        if (titleText != null)
        {
            titleText.font = guideFontAsset;
        }

        if (bodyText != null)
        {
            bodyText.font = guideFontAsset;
        }

    }

    private void AdvanceIfNeeded()
    {
        switch (currentStep)
        {
            case GuideStep.ExplainManipulation:
                manipulationExplainTimer += Time.deltaTime;
                if (!enableTts)
                {
                    if (manipulationExplainTimer >= manipulationExplainSeconds)
                    {
                        currentStep = GuideStep.MoveToTarget;
                        InitializeMoveTarget();
                    }
                    break;
                }

                if (manipulationExplainSpeechStarted && IsGuidePromptSpeaking())
                {
                    manipulationExplainSpeechObserved = true;
                    break;
                }

                if (manipulationExplainSpeechStarted && manipulationExplainSpeechObserved)
                {
                    currentStep = GuideStep.MoveToTarget;
                    InitializeMoveTarget();
                    break;
                }

                if (manipulationExplainTimer >= manipulationExplainFallbackSeconds)
                {
                    currentStep = GuideStep.MoveToTarget;
                    InitializeMoveTarget();
                }
                break;

            case GuideStep.MoveToTarget:
                if (!moveTargetInitialized)
                {
                    InitializeMoveTarget();
                }

                if (Vector3.Distance(objectParent.position, moveTargetWorldPosition) <= moveCompletionDistance)
                {
                    currentStep = GuideStep.ReturnToOrigin;
                }
                break;

            case GuideStep.ReturnToOrigin:
                if (Vector3.Distance(objectParent.position, originWorldPosition) <= moveCompletionDistance)
                {
                    currentStep = GuideStep.RotateModel;
                    lastRotateRotation = objectParent.rotation;
                    rotateTrackingInitialized = true;
                    rotateAccumulatedSeconds = 0f;
                }
                break;

            case GuideStep.RotateModel:
                if (!rotateTrackingInitialized)
                {
                    lastRotateRotation = objectParent.rotation;
                    rotateTrackingInitialized = true;
                }

                float rotationDelta = Quaternion.Angle(lastRotateRotation, objectParent.rotation);
                if (rotationDelta >= rotationMotionDelta)
                {
                    rotateAccumulatedSeconds += Time.deltaTime;
                }

                lastRotateRotation = objectParent.rotation;

                if (rotateAccumulatedSeconds >= rotationRequiredSeconds)
                {
                    currentStep = GuideStep.ZoomIn;
                    zoomBaselineScaleMagnitude = Mathf.Max(objectParent.localScale.magnitude, 0.0001f);
                    zoomThresholdReached = false;
                    zoomCompletionTimer = 0f;
                }
                break;

            case GuideStep.ZoomIn:
                if (zoomBaselineScaleMagnitude <= 0f)
                {
                    zoomBaselineScaleMagnitude = Mathf.Max(objectParent.localScale.magnitude, 0.0001f);
                }

                float currentScaleRatio = objectParent.localScale.magnitude / zoomBaselineScaleMagnitude;
                if (!zoomThresholdReached && currentScaleRatio >= zoomScaleMultiplier)
                {
                    zoomThresholdReached = true;
                    zoomCompletionTimer = 0f;
                }

                if (zoomThresholdReached)
                {
                    zoomCompletionTimer += Time.deltaTime;
                    if (zoomCompletionTimer >= zoomCompletionDelaySeconds)
                    {
                        currentStep = GuideStep.Complete;
                    }
                }
                break;
        }
    }

    private void AdvanceVelocityGuideIfNeeded()
    {
        if (buttonController == null)
        {
            return;
        }

        switch (currentVelocityStep)
        {
            case VelocityGuideStep.ObserveFlow:
                if (!enableTts)
                {
                    velocityObserveTimer += Time.deltaTime;
                    if (velocityObserveTimer >= velocityObserveAfterSpeechSeconds)
                    {
                        currentVelocityStep = VelocityGuideStep.AdjustSpeed;
                        buttonController.ShowExhibitionVelocitySpeedSlider();
                        velocitySliderBaseline = buttonController.GetVelocityPlaybackNormalized();
                        velocitySliderBaselineCaptured = true;
                        velocityAdjustmentTriggered = false;
                        velocityAdjustmentTimer = 0f;
                    }
                    break;
                }

                if (velocityObserveSpeechStarted && IsGuidePromptSpeaking())
                {
                    velocityObserveSpeechObserved = true;
                    break;
                }

                if (velocityObserveSpeechStarted && velocityObserveSpeechObserved)
                {
                    velocityObserveTimer += Time.deltaTime;
                }
                else if (velocityObserveTimer < velocityObserveFallbackSeconds)
                {
                    velocityObserveTimer += Time.deltaTime;
                }

                bool shouldAdvanceObserveStep =
                    (velocityObserveSpeechStarted && velocityObserveSpeechObserved && velocityObserveTimer >= velocityObserveAfterSpeechSeconds) ||
                    (!velocityObserveSpeechObserved && velocityObserveTimer >= velocityObserveFallbackSeconds);

                if (shouldAdvanceObserveStep)
                {
                    currentVelocityStep = VelocityGuideStep.AdjustSpeed;
                    buttonController.ShowExhibitionVelocitySpeedSlider();
                    velocitySliderBaseline = buttonController.GetVelocityPlaybackNormalized();
                    velocitySliderBaselineCaptured = true;
                    velocityAdjustmentTriggered = false;
                    velocityAdjustmentTimer = 0f;
                }
                break;

            case VelocityGuideStep.AdjustSpeed:
                if (!velocitySliderBaselineCaptured)
                {
                    velocitySliderBaseline = buttonController.GetVelocityPlaybackNormalized();
                    velocitySliderBaselineCaptured = true;
                }

                if (Mathf.Abs(buttonController.GetVelocityPlaybackNormalized() - velocitySliderBaseline) >= sliderStepThreshold)
                {
                    velocityAdjustmentTriggered = true;
                    velocitySliderBaseline = buttonController.GetVelocityPlaybackNormalized();
                    velocityAdjustmentTimer = 0f;
                }

                if (velocityAdjustmentTriggered && !buttonController.IsExhibitionInteractionInProgress())
                {
                    velocityAdjustmentTimer += Time.deltaTime;
                }

                if (velocityAdjustmentTriggered && velocityAdjustmentTimer >= velocityAdjustmentSeconds)
                {
                    currentVelocityStep = VelocityGuideStep.AdjustSpacing;
                    buttonController.ShowExhibitionVelocitySpacingSlider();
                    velocitySliderBaseline = buttonController.GetVelocitySpacingNormalized();
                    velocityAdjustmentTriggered = false;
                    velocityAdjustmentTimer = 0f;
                }
                break;

            case VelocityGuideStep.AdjustSpacing:
                if (Mathf.Abs(buttonController.GetVelocitySpacingNormalized() - velocitySliderBaseline) >= sliderStepThreshold)
                {
                    velocityAdjustmentTriggered = true;
                    velocitySliderBaseline = buttonController.GetVelocitySpacingNormalized();
                    velocityAdjustmentTimer = 0f;
                }

                if (velocityAdjustmentTriggered && !buttonController.IsExhibitionInteractionInProgress())
                {
                    velocityAdjustmentTimer += Time.deltaTime;
                }

                if (velocityAdjustmentTriggered && velocityAdjustmentTimer >= velocityAdjustmentSeconds)
                {
                    currentVelocityStep = VelocityGuideStep.RotateVessel;
                    buttonController.HideExhibitionSlider();
                    velocityFreeExploreTimer = 0f;
                    velocityFreeExploreSpeechStarted = false;
                    velocityFreeExploreSpeechObserved = false;
                }
                break;

            case VelocityGuideStep.RotateVessel:
                break;
        }
    }

    private void AdvanceSliceGuideIfNeeded()
    {
        if (buttonController == null)
        {
            return;
        }

        switch (currentSliceStep)
        {
            case SliceGuideStep.ExplainCapabilities:
                if (!enableTts)
                {
                    sliceStepTimer += Time.deltaTime;
                    if (sliceStepTimer >= sliceExplainSeconds)
                    {
                        currentSliceStep = SliceGuideStep.Observe2DView;
                        sliceStepTimer = 0f;
                        sliceObserveSpeechStarted = false;
                        sliceObserveSpeechObserved = false;
                    }
                    break;
                }

                if (sliceExplainSpeechStarted && IsGuidePromptSpeaking())
                {
                    sliceExplainSpeechObserved = true;
                    break;
                }

                if (sliceExplainSpeechStarted && sliceExplainSpeechObserved)
                {
                    currentSliceStep = SliceGuideStep.Observe2DView;
                    sliceStepTimer = 0f;
                    sliceObserveSpeechStarted = false;
                    sliceObserveSpeechObserved = false;
                    buttonController.ShowExhibitionSliceToggleMenu();
                    break;
                }

                sliceStepTimer += Time.deltaTime;
                if (sliceStepTimer >= sliceExplainSeconds)
                {
                    currentSliceStep = SliceGuideStep.Observe2DView;
                    sliceStepTimer = 0f;
                    sliceObserveSpeechStarted = false;
                    sliceObserveSpeechObserved = false;
                    buttonController.ShowExhibitionSliceToggleMenu();
                }
                break;

            case SliceGuideStep.Observe2DView:
                if (!enableTts)
                {
                    sliceStepTimer += Time.deltaTime;
                    if (sliceStepTimer >= sliceObserveSeconds)
                    {
                        currentSliceStep = SliceGuideStep.MoveSliceInside;
                        sliceInteractionDetected = false;
                        sliceMoveCompleted = false;
                        sliceMoveStartPositionCaptured = false;
                        sliceStepTimer = 0f;
                        buttonController.ShowExhibitionSliceToggleMenu();
                    }
                    break;
                }

                if (sliceObserveSpeechStarted && IsGuidePromptSpeaking())
                {
                    sliceObserveSpeechObserved = true;
                    break;
                }

                if (sliceObserveSpeechStarted && sliceObserveSpeechObserved)
                {
                    sliceStepTimer += Time.deltaTime;
                    if (sliceStepTimer >= sliceObserveSeconds)
                    {
                        currentSliceStep = SliceGuideStep.MoveSliceInside;
                        sliceInteractionDetected = false;
                        sliceMoveCompleted = false;
                        sliceMoveStartPositionCaptured = false;
                        sliceStepTimer = 0f;
                        buttonController.ShowExhibitionSliceToggleMenu();
                    }
                    break;
                }

                sliceStepTimer += Time.deltaTime;
                if (sliceStepTimer >= sliceObserveSeconds)
                {
                    currentSliceStep = SliceGuideStep.MoveSliceInside;
                    sliceInteractionDetected = false;
                    sliceMoveCompleted = false;
                    sliceMoveStartPositionCaptured = false;
                    sliceStepTimer = 0f;
                    buttonController.ShowExhibitionSliceToggleMenu();
                }
                break;

            case SliceGuideStep.MoveSliceInside:
                if (!sliceMoveStartPositionCaptured)
                {
                    sliceMoveStartPosition = GetCurrentSliceHandlePosition();
                    sliceMoveStartRotation = GetCurrentSliceHandleRotation();
                    sliceMoveStartPositionCaptured = true;
                }

                Vector3 currentSlicePosition = GetCurrentSliceHandlePosition();
                Quaternion currentSliceRotation = GetCurrentSliceHandleRotation();
                float movedDistance = Vector3.Distance(sliceMoveStartPosition, currentSlicePosition);
                float rotatedAngle = Quaternion.Angle(sliceMoveStartRotation, currentSliceRotation);

                if (movedDistance >= sliceMoveDistanceThreshold || rotatedAngle >= sliceMoveRotationThreshold)
                {
                    sliceInteractionDetected = true;
                }

                if (!sliceMoveCompleted &&
                    sliceInteractionDetected)
                {
                    sliceMoveCompleted = true;
                    sliceStepTimer = 0f;
                }

                if (sliceMoveCompleted)
                {
                    sliceStepTimer += Time.deltaTime;
                    if (sliceStepTimer >= sliceMoveCompletionDelaySeconds)
                    {
                        currentSliceStep = SliceGuideStep.FreeExplore;
                        sliceStepTimer = 0f;
                        buttonController.ShowExhibitionSliceToggleMenu();
                    }
                }
                break;

            case SliceGuideStep.FreeExplore:
                break;
        }
    }

    private void AdvanceStreamlineGuideIfNeeded()
    {
        if (buttonController == null)
        {
            return;
        }

        switch (currentStreamlineStep)
        {
            case StreamlineGuideStep.ExplainCapabilities:
                if (!enableTts)
                {
                    streamlineStepTimer += Time.deltaTime;
                    if (streamlineStepTimer >= streamlineExplainFallbackSeconds)
                    {
                        currentStreamlineStep = StreamlineGuideStep.ObserveFlow;
                        streamlineStepTimer = 0f;
                        streamlineObserveSpeechStarted = false;
                        streamlineObserveSpeechObserved = false;
                    }
                    break;
                }

                if (streamlineExplainSpeechStarted && IsGuidePromptSpeaking())
                {
                    streamlineExplainSpeechObserved = true;
                    break;
                }

                if (streamlineExplainSpeechStarted && streamlineExplainSpeechObserved)
                {
                    currentStreamlineStep = StreamlineGuideStep.ObserveFlow;
                    streamlineStepTimer = 0f;
                    streamlineObserveSpeechStarted = false;
                    streamlineObserveSpeechObserved = false;
                    break;
                }

                streamlineStepTimer += Time.deltaTime;
                if (streamlineStepTimer >= streamlineExplainFallbackSeconds)
                {
                    currentStreamlineStep = StreamlineGuideStep.ObserveFlow;
                    streamlineStepTimer = 0f;
                    streamlineObserveSpeechStarted = false;
                    streamlineObserveSpeechObserved = false;
                }
                break;

            case StreamlineGuideStep.ObserveFlow:
                if (!enableTts)
                {
                    streamlineStepTimer += Time.deltaTime;
                    if (streamlineStepTimer >= streamlineObserveAfterSpeechSeconds)
                    {
                        currentStreamlineStep = StreamlineGuideStep.AdjustSpeed;
                        buttonController.ShowExhibitionStreamlineSpeedSlider();
                        streamlineSliderBaseline = buttonController.GetStreamlinePlaybackNormalized();
                        streamlineSliderBaselineCaptured = true;
                        streamlineAdjustmentTriggered = false;
                        streamlineAdjustmentTimer = 0f;
                    }
                    break;
                }

                if (streamlineObserveSpeechStarted && IsGuidePromptSpeaking())
                {
                    streamlineObserveSpeechObserved = true;
                    break;
                }

                if (streamlineObserveSpeechStarted && streamlineObserveSpeechObserved)
                {
                    streamlineStepTimer += Time.deltaTime;
                }
                else if (streamlineStepTimer < streamlineObserveFallbackSeconds)
                {
                    streamlineStepTimer += Time.deltaTime;
                }

                bool shouldAdvanceObserveStep =
                    (streamlineObserveSpeechStarted && streamlineObserveSpeechObserved && streamlineStepTimer >= streamlineObserveAfterSpeechSeconds) ||
                    (!streamlineObserveSpeechObserved && streamlineStepTimer >= streamlineObserveFallbackSeconds);

                if (shouldAdvanceObserveStep)
                {
                    currentStreamlineStep = StreamlineGuideStep.AdjustSpeed;
                    buttonController.ShowExhibitionStreamlineSpeedSlider();
                    streamlineSliderBaseline = buttonController.GetStreamlinePlaybackNormalized();
                    streamlineSliderBaselineCaptured = true;
                    streamlineAdjustmentTriggered = false;
                    streamlineAdjustmentTimer = 0f;
                }
                break;

            case StreamlineGuideStep.AdjustSpeed:
                if (!streamlineSliderBaselineCaptured)
                {
                    streamlineSliderBaseline = buttonController.GetStreamlinePlaybackNormalized();
                    streamlineSliderBaselineCaptured = true;
                }

                if (Mathf.Abs(buttonController.GetStreamlinePlaybackNormalized() - streamlineSliderBaseline) >= sliderStepThreshold)
                {
                    streamlineAdjustmentTriggered = true;
                    streamlineSliderBaseline = buttonController.GetStreamlinePlaybackNormalized();
                    streamlineAdjustmentTimer = 0f;
                }

                if (streamlineAdjustmentTriggered && !buttonController.IsExhibitionInteractionInProgress())
                {
                    streamlineAdjustmentTimer += Time.deltaTime;
                }

                if (streamlineAdjustmentTriggered && streamlineAdjustmentTimer >= streamlineAdjustmentSeconds)
                {
                    currentStreamlineStep = StreamlineGuideStep.FreeExplore;
                    buttonController.HideExhibitionSlider();
                    streamlineStepTimer = 0f;
                    streamlineFreeExploreSpeechStarted = false;
                    streamlineFreeExploreSpeechObserved = false;
                }
                break;

            case StreamlineGuideStep.FreeExplore:
                break;
        }
    }

    private void AdvanceWssGuideIfNeeded()
    {
        if (buttonController == null)
        {
            return;
        }

        switch (currentWssStep)
        {
            case WssGuideStep.ExplainCapabilities:
                if (!enableTts)
                {
                    wssStepTimer += Time.deltaTime;
                    if (wssStepTimer >= wssExplainFallbackSeconds)
                    {
                        currentWssStep = WssGuideStep.AdjustSpeed;
                        wssStepTimer = 0f;
                        wssSliderBaseline = buttonController.GetWssPlaybackNormalized();
                        wssSliderBaselineCaptured = true;
                        wssAdjustmentTriggered = false;
                        wssAdjustmentTimer = 0f;
                        buttonController.ShowExhibitionWssSpeedSlider();
                    }
                    break;
                }

                if (wssExplainSpeechStarted && IsGuidePromptSpeaking())
                {
                    wssExplainSpeechObserved = true;
                    break;
                }

                if (wssExplainSpeechStarted && wssExplainSpeechObserved)
                {
                    currentWssStep = WssGuideStep.AdjustSpeed;
                    wssStepTimer = 0f;
                    wssSliderBaseline = buttonController.GetWssPlaybackNormalized();
                    wssSliderBaselineCaptured = true;
                    wssAdjustmentTriggered = false;
                    wssAdjustmentTimer = 0f;
                    buttonController.ShowExhibitionWssSpeedSlider();
                    break;
                }

                wssStepTimer += Time.deltaTime;
                if (wssStepTimer >= wssExplainFallbackSeconds)
                {
                    currentWssStep = WssGuideStep.AdjustSpeed;
                    wssStepTimer = 0f;
                    buttonController.ShowExhibitionWssSpeedSlider();
                    wssSliderBaseline = buttonController.GetWssPlaybackNormalized();
                    wssSliderBaselineCaptured = true;
                    wssAdjustmentTriggered = false;
                    wssAdjustmentTimer = 0f;
                }
                break;

            case WssGuideStep.AdjustSpeed:
                if (!wssSliderBaselineCaptured)
                {
                    wssSliderBaseline = buttonController.GetWssPlaybackNormalized();
                    wssSliderBaselineCaptured = true;
                }

                if (Mathf.Abs(buttonController.GetWssPlaybackNormalized() - wssSliderBaseline) >= sliderStepThreshold)
                {
                    wssAdjustmentTriggered = true;
                    wssSliderBaseline = buttonController.GetWssPlaybackNormalized();
                    wssAdjustmentTimer = 0f;
                }

                if (wssAdjustmentTriggered && !buttonController.IsExhibitionInteractionInProgress())
                {
                    wssAdjustmentTimer += Time.deltaTime;
                }

                if (wssAdjustmentTriggered && wssAdjustmentTimer >= wssAdjustmentSeconds)
                {
                    currentWssStep = WssGuideStep.FreeExplore;
                    buttonController.HideExhibitionSlider();
                    wssStepTimer = 0f;
                    wssFreeExploreSpeechStarted = false;
                    wssFreeExploreSpeechObserved = false;
                }
                break;

            case WssGuideStep.FreeExplore:
                break;
        }
    }

    private void RefreshPromptText()
    {
        if (currentMode == GuideMode.MenuIntro)
        {
            titleText.text = "심혈관 질환 진단 XR";
            bodyText.text = "의사가 되어 첨단 의료기술을 직접 체험해 보세요.\n혈관의 형태와 다양한 혈류 정보를 직접 조작하며 살펴볼 수 있습니다.\n아래 메뉴에서 원하는 체험을 선택해 보세요.";
            return;
        }

        if (currentMode == GuideMode.Velocity)
        {
            switch (currentVelocityStep)
            {
                case VelocityGuideStep.ObserveFlow:
                    titleText.text = "혈류 속도 체험";
                    bodyText.text = "화살표는 혈류가 흐르는 방향을 보여주고, 길이와 색은 속도를 나타내요.\n의사는 혈류가 어디로, 얼마나 빠르게 흐르는지 살펴볼 수 있어요.\n안내에 따라 하나씩 체험해 보세요.";
                    break;
                case VelocityGuideStep.AdjustSpeed:
                    titleText.text = "관찰 속도 조절하기";
                    bodyText.text = "슬라이더를 움직여 관찰하기 편한 속도로 바꿔 보세요.";
                    break;
                case VelocityGuideStep.AdjustSpacing:
                    titleText.text = "혈류 간격 조절하기";
                    bodyText.text = "슬라이더를 움직여 관찰하기 편한 혈류 간격으로 바꿔 보세요.";
                    break;
                case VelocityGuideStep.RotateVessel:
                    titleText.text = "혈류 속도 자유 체험";
                    bodyText.text = "자유롭게 여러 각도에서 혈류를 살펴보세요.";
                    break;
                case VelocityGuideStep.Complete:
                    titleText.text = "혈류 속도 체험 완료";
                    bodyText.text = "혈류의 흐름과 속도 변화를 직접 확인해 보았습니다.\n혈류 속도 정보는 혈관 상태를 더 자세히 이해하는 데 도움을 줄 수 있어요.";
                    break;
            }
            return;
        }

        if (currentMode == GuideMode.Slice)
        {
            switch (currentSliceStep)
            {
                case SliceGuideStep.ExplainCapabilities:
                    titleText.text = "혈류 속도 단면 체험";
                    bodyText.text = "의사는 단면 정보를 활용해 혈류 속도를 더 자세히 살펴볼 수 있어요.\n단면을 움직이며 혈관 내부의 혈류 속도를 살펴볼 거예요.\n안내에 따라 하나씩 체험해 보세요.";
                    break;
                case SliceGuideStep.Observe2DView:
                    titleText.text = "단면 보기";
                    bodyText.text = "혈관 단면을 보여주는 방법은 여러 가지가 있어요.\n2D와 3D 화면을 바꿔 보며 혈관 내부를 비교해 보세요.\n혈관 안쪽의 혈류 속도를 더 자세히 확인할 수 있어요.";
                    break;
                case SliceGuideStep.MoveSliceInside:
                    titleText.text = "단면 위치 바꾸기";
                    bodyText.text = "혈관 내부의 파란색 판을 잡고 움직여 보세요.\n위치에 따라 혈관 안쪽 모습이 달라지는 것을 볼 수 있어요.";
                    break;
                case SliceGuideStep.FreeExplore:
                    titleText.text = "혈류 속도 단면 자유 체험";
                    bodyText.text = "자유롭게 단면을 움직이며 혈관 내부를 살펴보세요.\n원하는 위치에서 혈류 속도를 더 자세히 확인해 보세요.";
                    break;
                case SliceGuideStep.Complete:
                    titleText.text = "혈류 속도 단면 체험 완료";
                    bodyText.text = "단면을 움직이며 혈관 내부의 혈류 속도를 직접 확인해 보았습니다.\n단면 정보는 혈관 내부 상태를 더 세밀하게 살펴보는 데 도움을 줄 수 있어요.";
                    break;
            }
            return;
        }

        if (currentMode == GuideMode.Streamline)
        {
            switch (currentStreamlineStep)
            {
                case StreamlineGuideStep.ExplainCapabilities:
                    titleText.text = "혈액 흐름 분석 체험";
                    bodyText.text = "의사는 혈액이 어떤 방향으로 흐르는지 분석할 수 있어요.\n혈관 안에서 혈액의 흐름을 살펴볼 거예요.\n안내에 따라 하나씩 체험해 보세요.";
                    break;
                case StreamlineGuideStep.ObserveFlow:
                    titleText.text = "혈액 흐름 보기";
                    bodyText.text = "유선은 혈액이 이동하는 방향을 이어서 보여줘요.\n혈관 안에서 혈액이 어떻게 흐르는지 살펴보세요.";
                    break;
                case StreamlineGuideStep.AdjustSpeed:
                    titleText.text = "관찰 속도 조절하기";
                    bodyText.text = "슬라이더를 움직여 관찰하기 편한 속도로 바꿔 보세요.";
                    break;
                case StreamlineGuideStep.FreeExplore:
                    titleText.text = "혈액 흐름 자유 체험";
                    bodyText.text = "자유롭게 여러 각도에서 혈액 흐름을 살펴보세요.";
                    break;
                case StreamlineGuideStep.Complete:
                    titleText.text = "혈액 흐름 분석 체험 완료";
                    bodyText.text = "혈액이 흐르는 방향을 직접 확인해 보았습니다.\n\n혈액 흐름 방향 정보는 혈류가 어떻게 이어지는지\n이해하는 데 도움을 줄 수 있어요.";
                    break;
            }
            return;
        }

        if (currentMode == GuideMode.Wss)
        {
            switch (currentWssStep)
            {
                case WssGuideStep.ExplainCapabilities:
                    titleText.text = "벽면 전단 응력 분석 체험";
                    bodyText.text = "벽면 전단 응력은 혈액이 흐르면서 혈관 벽에 가하는 마찰력이에요.\n의사는 이 분포를 통해 혈관에 이상이 있는 부분을 살펴볼 수 있어요.\n안내에 따라 하나씩 체험해 보세요.";
                    break;
                case WssGuideStep.ObserveDistribution:
                    titleText.text = "벽면 전단 응력 보기";
                    bodyText.text = "색은 벽면 전단 응력의 차이를 보여줘요.\n어느 부위에서 더 크게 나타나는지 살펴보세요.";
                    break;
                case WssGuideStep.AdjustSpeed:
                    titleText.text = "관찰 속도 조절하기";
                    bodyText.text = "슬라이더를 움직여 관찰하기 편한 속도로 바꿔 보세요.";
                    break;
                case WssGuideStep.FreeExplore:
                    titleText.text = "벽면 전단 응력 자유 체험";
                    bodyText.text = "색은 벽면 전단 응력의 차이를 보여줘요.\n자유롭게 여러 각도에서 응력 분포를 살펴보세요.";
                    break;
                case WssGuideStep.Complete:
                    titleText.text = "벽면 전단 응력 분석 체험 완료";
                    bodyText.text = "혈관 벽에 작용하는 벽면 전단 응력 분포를 직접 확인해 보았습니다.\n벽면 전단 응력 분포는 혈관 벽에 부담이 큰 부위를 \n이해하는 데 도움을 줄 수 있어요.";
                    break;
            }
            return;
        }

        switch (currentStep)
        {
            case GuideStep.ExplainManipulation:
                titleText.text = "혈관 조작 방법 체험";
                bodyText.text = "심혈관 질환 진단 XR의 조작 방법을 알아볼 거예요.\n안내에 따라 순서대로 체험해 보세요.";
                break;
            case GuideStep.MoveToTarget:
                titleText.text = "혈관 이동하기";
                bodyText.text = "혈관 모델을 직접 두 손가락으로 잡고\n표시된 빨간 원의 위치로 옮겨 보세요.";
                break;
            case GuideStep.ReturnToOrigin:
                titleText.text = "혈관 원위치로 돌아가기";
                bodyText.text = "이제 혈관 모델을 다시 처음 위치로 옮겨 보세요.\n혈관이 놓인 기준 위치를 다시 확인해 보세요.";
                break;
            case GuideStep.RotateModel:
                titleText.text = "혈관 돌려보기";
                bodyText.text = "혈관 모델을 한손으로 잡고 돌리거나 외곽 구를 잡아 돌려보세요.\n앞, 옆, 위에서 보이는 형태 차이를 살펴보세요.";
                break;
            case GuideStep.ZoomIn:
                titleText.text = "혈관 확대하기";
                bodyText.text = "혈관 모델을 확대해 더 자세히 살펴보세요.\n두 손으로 잡고 확대하거나 외곽 사각형 핸들을 잡아 확대해 보세요.\n가까이에서 혈관의 세부 구조를 확인할 수 있습니다.";
                break;
            case GuideStep.Complete:
                titleText.text = "혈관 조작 체험 성공!";
                bodyText.text = "혈관 조작 체험이 완료되었습니다.\n혈관 이동, 복귀, 돌려보기, 확대를 모두 마쳤습니다.";
                break;
        }
    }

    private void SpeakPromptIfChanged()
    {
        if (!enableTts || titleText == null || bodyText == null)
        {
            return;
        }

        string promptKey = $"{titleText.text}\n{bodyText.text}";
        if (string.IsNullOrWhiteSpace(promptKey) || promptKey == lastSpokenPromptKey)
        {
            return;
        }

        lastSpokenPromptKey = promptKey;
        ResetPromptSpeechFlagsForCurrentState();
        StopGuidePromptSpeech();

        string audioKey = GetCurrentPromptAudioKey();
        if (string.IsNullOrWhiteSpace(audioKey) || ttsAudioSource == null)
        {
            return;
        }

        StartPromptAudioPlayback(audioKey);
    }

    private void StopGuidePromptSpeech()
    {
        if (ttsAudioSource == null)
        {
            return;
        }

        if (ttsAudioSource.isPlaying)
        {
            ttsAudioSource.Stop();
        }

        ttsAudioSource.clip = null;

        if (pendingPromptPlaybackCoroutine != null)
        {
            StopCoroutine(pendingPromptPlaybackCoroutine);
            pendingPromptPlaybackCoroutine = null;
        }
    }

    private bool IsGuidePromptSpeaking()
    {
        if (!enableTts)
        {
            return false;
        }

        return ttsAudioSource != null && ttsAudioSource.isPlaying;
    }

    public bool IsPromptSpeechActive()
    {
        return IsGuidePromptSpeaking();
    }

    private Vector3 GetCurrentSliceHandlePosition()
    {
        if (buttonController != null &&
            buttonController.sliceVisualization != null &&
            buttonController.sliceVisualization.indicatorController != null)
        {
            return buttonController.sliceVisualization.indicatorController.transform.position;
        }

        if (buttonController != null && buttonController.sliceVisualization != null)
        {
            return buttonController.sliceVisualization.transform.position;
        }

        return objectParent != null ? objectParent.position : Vector3.zero;
    }

    private Quaternion GetCurrentSliceHandleRotation()
    {
        if (buttonController != null &&
            buttonController.sliceVisualization != null &&
            buttonController.sliceVisualization.indicatorController != null)
        {
            return buttonController.sliceVisualization.indicatorController.transform.rotation;
        }

        if (buttonController != null && buttonController.sliceVisualization != null)
        {
            return buttonController.sliceVisualization.transform.rotation;
        }

        return Quaternion.identity;
    }

    private void InitializeMoveTarget()
    {
        if (mainCamera == null)
        {
            return;
        }

        if (promptRoot != null && menuAnchor != null)
        {
            Transform cam = mainCamera.transform;
            moveTargetWorldPosition =
                promptRoot.transform.position +
                (cam.right * -0.10f) +
                (cam.up * -0.08f) +
                (cam.forward * promptTargetForwardOffset);
            moveTargetInitialized = true;
            return;
        }

        Transform fallbackCam = mainCamera.transform;
        moveTargetWorldPosition = fallbackCam.position
            + fallbackCam.forward * targetDistance
            + fallbackCam.right * targetOffset.x
            + fallbackCam.up * targetOffset.y;
        moveTargetInitialized = true;
    }

    private void UpdatePromptTransform()
    {
        if (promptRoot == null || mainCamera == null)
        {
            return;
        }

        if (menuAnchor != null)
        {
            Transform menuCam = mainCamera.transform;
            promptRoot.transform.position =
                menuAnchor.position +
                (menuCam.right * menuPromptOffset.x) +
                (menuCam.up * menuPromptOffset.y) +
                (menuCam.forward * menuPromptOffset.z);

            if (lockMenuPromptRotation)
            {
                promptRoot.transform.rotation = menuAnchor.rotation;
            }
            else
            {
                promptRoot.transform.rotation = Quaternion.LookRotation(promptRoot.transform.position - menuCam.position, Vector3.up);
            }
            return;
        }

        if (objectParent == null)
        {
            return;
        }

        Transform cam = mainCamera.transform;
        if (!promptPositionInitialized)
        {
            promptWorldPosition =
                objectParent.position +
                (cam.right * worldPromptOffset.x) +
                (cam.up * worldPromptOffset.y) +
                (cam.forward * worldPromptOffset.z);
            promptPositionInitialized = true;
        }

        promptRoot.transform.position = promptWorldPosition;
        promptRoot.transform.rotation = Quaternion.LookRotation(promptRoot.transform.position - cam.position, Vector3.up);
    }

    private void UpdateTargetMarker()
    {
        if (targetMarker == null)
        {
            return;
        }

        if (currentMode != GuideMode.Manipulation)
        {
            targetMarker.SetActive(false);
            return;
        }

        bool shouldShow =
            currentStep == GuideStep.MoveToTarget ||
            currentStep == GuideStep.ReturnToOrigin;
        targetMarker.SetActive(shouldShow);
        if (!shouldShow)
        {
            return;
        }

        targetMarker.transform.position =
            currentStep == GuideStep.ReturnToOrigin
                ? originWorldPosition
                : moveTargetWorldPosition;
        float pulse = 1f + (((Mathf.Sin(Time.time * markerPulseSpeed) + 1f) * 0.5f) * markerPulseScale);
        targetMarker.transform.localScale = Vector3.one * markerScale * pulse;
    }

    private void SetGuideVisible(bool visible)
    {
        if (promptRoot != null)
        {
            promptRoot.SetActive(visible);
        }

        if (targetMarker != null)
        {
            bool shouldShowMarker =
                visible &&
                currentMode == GuideMode.Manipulation &&
                (currentStep == GuideStep.MoveToTarget || currentStep == GuideStep.ReturnToOrigin);
            targetMarker.SetActive(shouldShowMarker);
        }
    }

    private void CreatePromptUI()
    {
        promptRoot = new GameObject("ExhibitionGuidePanel");
        promptRoot.transform.SetParent(transform, false);

        GameObject panelObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panelObject.name = "Panel";
        panelObject.transform.SetParent(promptRoot.transform, false);
        panelObject.transform.localScale = panelScale;

        Collider panelCollider = panelObject.GetComponent<Collider>();
        if (panelCollider != null)
        {
            Destroy(panelCollider);
        }

        MeshRenderer renderer = panelObject.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.material = CreateRuntimeMaterial();
            renderer.material.color = panelColor;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        titleText = CreateTextElement("Title", promptRoot.transform, titleFontSize, FontStyles.Bold, titleLocalPosition);
        bodyText = CreateTextElement("Body", promptRoot.transform, bodyFontSize, FontStyles.Normal, bodyLocalPosition);
        bodyText.alignment = TextAlignmentOptions.Center;
        CreateLogoElement();
        ApplyGuideFont();
    }

    private void CreateTtsComponents()
    {
        EnsureGuideAudioSource();
    }

    private static GameObject FindSceneObjectByPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        GameObject activeObject = GameObject.Find(path);
        if (activeObject != null)
        {
            return activeObject;
        }

        Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (Transform transform in transforms)
        {
            if (transform == null || !transform.gameObject.scene.IsValid())
            {
                continue;
            }

            if (GetHierarchyPath(transform) == path)
            {
                return transform.gameObject;
            }
        }

        return null;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
        {
            return string.Empty;
        }

        string current = transform.name;
        Transform parent = transform.parent;
        while (parent != null)
        {
            current = parent.name + "/" + current;
            parent = parent.parent;
        }

        return current;
    }


    private void ResetPromptSpeechFlagsForCurrentState()
    {
        if (currentMode == GuideMode.Manipulation && currentStep == GuideStep.ExplainManipulation)
        {
            manipulationExplainSpeechObserved = false;
        }
        if (currentMode == GuideMode.Velocity && currentVelocityStep == VelocityGuideStep.ObserveFlow)
        {
            velocityObserveSpeechObserved = false;
        }
        if (currentMode == GuideMode.Velocity && currentVelocityStep == VelocityGuideStep.RotateVessel)
        {
            velocityFreeExploreSpeechObserved = false;
        }
        if (currentMode == GuideMode.Slice && currentSliceStep == SliceGuideStep.ExplainCapabilities)
        {
            sliceExplainSpeechObserved = false;
        }
        if (currentMode == GuideMode.Slice && currentSliceStep == SliceGuideStep.Observe2DView)
        {
            sliceObserveSpeechObserved = false;
        }
        if (currentMode == GuideMode.Streamline && currentStreamlineStep == StreamlineGuideStep.ExplainCapabilities)
        {
            streamlineExplainSpeechObserved = false;
        }
        if (currentMode == GuideMode.Streamline && currentStreamlineStep == StreamlineGuideStep.ObserveFlow)
        {
            streamlineObserveSpeechObserved = false;
        }
        if (currentMode == GuideMode.Streamline && currentStreamlineStep == StreamlineGuideStep.FreeExplore)
        {
            streamlineFreeExploreSpeechObserved = false;
        }
        if (currentMode == GuideMode.Wss && currentWssStep == WssGuideStep.ExplainCapabilities)
        {
            wssExplainSpeechObserved = false;
        }
        if (currentMode == GuideMode.Wss && currentWssStep == WssGuideStep.ObserveDistribution)
        {
            wssObserveSpeechObserved = false;
        }
        if (currentMode == GuideMode.Wss && currentWssStep == WssGuideStep.FreeExplore)
        {
            wssFreeExploreSpeechObserved = false;
        }
    }

    private void MarkPromptSpeechStartedForCurrentState()
    {
        if (currentMode == GuideMode.Manipulation && currentStep == GuideStep.ExplainManipulation)
        {
            manipulationExplainSpeechStarted = true;
        }
        if (currentMode == GuideMode.Velocity && currentVelocityStep == VelocityGuideStep.ObserveFlow)
        {
            velocityObserveSpeechStarted = true;
            velocityObserveTimer = 0f;
        }
        if (currentMode == GuideMode.Velocity && currentVelocityStep == VelocityGuideStep.RotateVessel)
        {
            velocityFreeExploreSpeechStarted = true;
            velocityFreeExploreTimer = 0f;
        }
        if (currentMode == GuideMode.Slice && currentSliceStep == SliceGuideStep.ExplainCapabilities)
        {
            sliceExplainSpeechStarted = true;
            sliceStepTimer = 0f;
        }
        if (currentMode == GuideMode.Slice && currentSliceStep == SliceGuideStep.Observe2DView)
        {
            sliceObserveSpeechStarted = true;
            sliceStepTimer = 0f;
        }
        if (currentMode == GuideMode.Streamline && currentStreamlineStep == StreamlineGuideStep.ExplainCapabilities)
        {
            streamlineExplainSpeechStarted = true;
            streamlineStepTimer = 0f;
        }
        if (currentMode == GuideMode.Streamline && currentStreamlineStep == StreamlineGuideStep.ObserveFlow)
        {
            streamlineObserveSpeechStarted = true;
            streamlineStepTimer = 0f;
        }
        if (currentMode == GuideMode.Streamline && currentStreamlineStep == StreamlineGuideStep.FreeExplore)
        {
            streamlineFreeExploreSpeechStarted = true;
            streamlineStepTimer = 0f;
        }
        if (currentMode == GuideMode.Wss && currentWssStep == WssGuideStep.ExplainCapabilities)
        {
            wssExplainSpeechStarted = true;
            wssStepTimer = 0f;
        }
        if (currentMode == GuideMode.Wss && currentWssStep == WssGuideStep.ObserveDistribution)
        {
            wssObserveSpeechStarted = true;
            wssStepTimer = 0f;
        }
        if (currentMode == GuideMode.Wss && currentWssStep == WssGuideStep.FreeExplore)
        {
            wssFreeExploreSpeechStarted = true;
            wssStepTimer = 0f;
        }
    }

    private string GetCurrentPromptAudioKey()
    {
        if (currentMode == GuideMode.MenuIntro) return "intro_01";

        if (currentMode == GuideMode.Manipulation)
        {
            switch (currentStep)
            {
                case GuideStep.ExplainManipulation: return "exp01_00";
                case GuideStep.MoveToTarget: return "exp01_01";
                case GuideStep.ReturnToOrigin: return "exp01_02";
                case GuideStep.RotateModel: return "exp01_03";
                case GuideStep.ZoomIn: return "exp01_04";
                case GuideStep.Complete: return "exp01_done";
            }
        }

        if (currentMode == GuideMode.Velocity)
        {
            switch (currentVelocityStep)
            {
                case VelocityGuideStep.ObserveFlow: return "exp02_00";
                case VelocityGuideStep.AdjustSpeed: return "exp02_01";
                case VelocityGuideStep.AdjustSpacing: return "exp02_02";
                case VelocityGuideStep.RotateVessel: return "exp02_03";
                case VelocityGuideStep.Complete: return "exp02_done";
            }
        }

        if (currentMode == GuideMode.Slice)
        {
            switch (currentSliceStep)
            {
                case SliceGuideStep.ExplainCapabilities: return "exp03_00";
                case SliceGuideStep.Observe2DView: return "exp03_01";
                case SliceGuideStep.MoveSliceInside: return "exp03_02";
                case SliceGuideStep.FreeExplore: return "exp03_03";
                case SliceGuideStep.Complete: return "exp03_done";
            }
        }

        if (currentMode == GuideMode.Streamline)
        {
            switch (currentStreamlineStep)
            {
                case StreamlineGuideStep.ExplainCapabilities: return "exp04_00";
                case StreamlineGuideStep.ObserveFlow: return "exp04_01";
                case StreamlineGuideStep.AdjustSpeed: return "exp04_02";
                case StreamlineGuideStep.FreeExplore: return "exp04_03";
                case StreamlineGuideStep.Complete: return "exp04_done";
            }
        }

        if (currentMode == GuideMode.Wss)
        {
            switch (currentWssStep)
            {
                case WssGuideStep.ExplainCapabilities: return "exp05_00";
                case WssGuideStep.ObserveDistribution: return "exp05_01";
                case WssGuideStep.AdjustSpeed: return "exp05_02";
                case WssGuideStep.FreeExplore: return "exp05_03";
                case WssGuideStep.Complete: return "exp05_done";
            }
        }

        return null;
    }

    private void StartPromptAudioPlayback(string audioKey)
    {
        if (string.IsNullOrWhiteSpace(audioKey) || ttsAudioSource == null)
        {
            return;
        }

        if (pendingPromptPlaybackCoroutine != null)
        {
            StopCoroutine(pendingPromptPlaybackCoroutine);
            pendingPromptPlaybackCoroutine = null;
        }

        if (promptAudioCache.TryGetValue(audioKey, out AudioClip cachedClip) && cachedClip != null)
        {
            EnsureGuideAudioSource();
            ttsAudioSource.clip = cachedClip;
            ttsAudioSource.Play();
            MarkPromptSpeechStartedForCurrentState();
            Debug.Log($"[ExhibitionGuide] Playing cached streaming audio: {audioKey} on {(ttsAudioSource != null ? ttsAudioSource.gameObject.name : "null")}");
            return;
        }

        pendingPromptPlaybackCoroutine = StartCoroutine(LoadAndPlayPromptAudio(audioKey));
    }

    private void PreloadPromptAudio(params string[] audioKeys)
    {
        if (audioKeys == null)
        {
            return;
        }

        foreach (string audioKey in audioKeys)
        {
            if (string.IsNullOrWhiteSpace(audioKey) || promptAudioCache.ContainsKey(audioKey))
            {
                continue;
            }

            StartCoroutine(LoadPromptAudioToCache(audioKey));
        }
    }

    private IEnumerator LoadPromptAudioToCache(string audioKey)
    {
        string filePath = Path.Combine(Application.streamingAssetsPath, "audio", audioKey + ".mp3");
        if (!File.Exists(filePath))
        {
            yield break;
        }

        string uri = new System.Uri(filePath).AbsoluteUri;
        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG))
        {
            request.disposeDownloadHandlerOnDispose = false;
            yield return request.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (request.result != UnityWebRequest.Result.Success)
#else
            if (request.isNetworkError || request.isHttpError)
#endif
            {
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip != null && !promptAudioCache.ContainsKey(audioKey))
            {
                promptAudioCache[audioKey] = clip;
            }
        }
    }

    private IEnumerator LoadAndPlayPromptAudio(string audioKey)
    {
        string filePath = Path.Combine(Application.streamingAssetsPath, "audio", audioKey + ".mp3");
        if (!File.Exists(filePath))
        {
            pendingPromptPlaybackCoroutine = null;
            Debug.LogWarning($"[ExhibitionGuide] Missing streaming audio file: {filePath}");
            yield break;
        }

        string uri = new System.Uri(filePath).AbsoluteUri;
        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG))
        {
            request.disposeDownloadHandlerOnDispose = false;
            yield return request.SendWebRequest();

            pendingPromptPlaybackCoroutine = null;

#if UNITY_2020_1_OR_NEWER
            if (request.result != UnityWebRequest.Result.Success)
#else
            if (request.isNetworkError || request.isHttpError)
#endif
            {
                Debug.LogWarning($"[ExhibitionGuide] Failed to load streaming audio '{audioKey}': {request.error}");
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip == null)
            {
                Debug.LogWarning($"[ExhibitionGuide] Loaded streaming audio clip is null: {audioKey}");
                yield break;
            }

            promptAudioCache[audioKey] = clip;
            EnsureGuideAudioSource();
            ttsAudioSource.clip = clip;
            ttsAudioSource.Play();
            MarkPromptSpeechStartedForCurrentState();
            Debug.Log($"[ExhibitionGuide] Playing streaming audio: {audioKey} from {filePath}");
        }
    }

    private void EnsureGuideAudioSource()
    {
        GameObject cameraHost = mainCamera != null ? mainCamera.gameObject : Camera.main != null ? Camera.main.gameObject : null;
        GameObject audioHost = cameraHost;

        if (cameraHost != null)
        {
            Transform existingChild = cameraHost.transform.Find("GuideAudioSource");
            if (existingChild == null)
            {
                GameObject child = new GameObject("GuideAudioSource");
                child.transform.SetParent(cameraHost.transform, false);
                audioHost = child;
            }
            else
            {
                audioHost = existingChild.gameObject;
            }
        }
        else if (promptRoot != null)
        {
            audioHost = promptRoot;
        }

        if (audioHost == null)
        {
            return;
        }

        if (ttsAudioSource == null || ttsAudioSource.gameObject != audioHost)
        {
            ttsAudioSource = audioHost.GetComponent<AudioSource>();
            if (ttsAudioSource == null)
            {
                ttsAudioSource = audioHost.AddComponent<AudioSource>();
            }
        }

        ttsAudioSource.playOnAwake = false;
        ttsAudioSource.spatialBlend = 0f;
        ttsAudioSource.loop = false;
        ttsAudioSource.volume = 1f;
        ttsAudioSource.priority = 0;
        ttsAudioSource.ignoreListenerPause = true;
    }

    private TextMeshPro CreateTextElement(string name, Transform parent, float fontSize, FontStyles style, Vector3 localPosition)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        textObject.transform.localPosition = localPosition;
        textObject.transform.localRotation = Quaternion.identity;
        textObject.transform.localScale = Vector3.one * textScale;

        TextMeshPro text = textObject.AddComponent<TextMeshPro>();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.outlineWidth = 0.15f;
        text.outlineColor = new Color(0f, 0f, 0f, 0.5f);
        return text;
    }

    private void CreateTargetMarker()
    {
        targetMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        targetMarker.name = "ExhibitionGuideTarget";
        targetMarker.transform.SetParent(transform, false);
        targetMarker.transform.localScale = Vector3.one * markerScale;

        Collider markerCollider = targetMarker.GetComponent<Collider>();
        if (markerCollider != null)
        {
            Destroy(markerCollider);
        }

        Renderer renderer = targetMarker.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = CreateRuntimeMaterial();
            renderer.material.color = markerColor;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    private void CreateLogoElement()
    {
        logoObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        logoObject.name = "Logo";
        logoObject.transform.SetParent(promptRoot.transform, false);
        logoObject.transform.localRotation = Quaternion.identity;

        Collider logoCollider = logoObject.GetComponent<Collider>();
        if (logoCollider != null)
        {
            Destroy(logoCollider);
        }

        logoRenderer = logoObject.GetComponent<MeshRenderer>();
        if (logoRenderer == null)
        {
            return;
        }

        logoRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        logoRenderer.receiveShadows = false;

        Texture2D logoTexture = Resources.Load<Texture2D>(logoResourcePath);
        if (logoTexture == null)
        {
            logoObject.SetActive(false);
            return;
        }

        logoMaterial = CreateRuntimeMaterial();
        if (logoMaterial.HasProperty("_BaseColor"))
        {
            logoMaterial.SetColor("_BaseColor", Color.white);
        }
        else
        {
            logoMaterial.color = Color.white;
        }

        if (logoMaterial.HasProperty("_BaseMap"))
        {
            logoMaterial.SetTexture("_BaseMap", logoTexture);
        }
        else if (logoMaterial.HasProperty("_MainTex"))
        {
            logoMaterial.SetTexture("_MainTex", logoTexture);
        }

        logoRenderer.material = logoMaterial;

        float aspect = Mathf.Max(logoTexture.width, 1) / (float)Mathf.Max(logoTexture.height, 1);
        float width = Mathf.Min(logoHeight * aspect, logoMaxWidth);
        float height = width / aspect;

        logoObject.transform.localScale = new Vector3(width, height, 1f);
        logoObject.transform.localPosition = new Vector3(
            (-panelScale.x * 0.5f) + (width * 0.5f) + logoPadding.x,
            (panelScale.y * 0.5f) - (height * 0.5f) - logoPadding.y,
            titleLocalPosition.z);
    }

    private Material CreateRuntimeMaterial()
    {
        Shader shader =
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Universal Render Pipeline/Lit") ??
            Shader.Find("Standard");

        return new Material(shader);
    }
}
