using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.Physics;
using Microsoft.MixedReality.Toolkit.UI;
using Microsoft.MixedReality.Toolkit.Utilities;
using Photon.Pun;
using TMPro;
public class ButtonControllerManager : MonoBehaviour
{
    public enum ExhibitionExperience
    {
        None,
        Manipulation,
        Velocity,
        Slice,
        Streamline,
        Wss,
    }

    public static ButtonControllerManager Instance;

    [Header("References")]
    public Manager manager;
    public Transform vesselRoot;
    public Transform mainMenuRoot;
    public GameObject mainMenu;
    public GameObject streamlineMenu;
    public GameObject streamlineSpeedMenu;
    public GameObject velocityMenu;
    public GameObject wssMenu;
    public GameObject visualizationSettingMenu;
    public GameObject velocityVisualizationSettingMenu;
    public GameObject velocityGoBackMenu;
    public GameObject exhibitionMenu;
    public GlobalSliderController sliderController;
    public SliceController sliceVisualization;
    
    [Header("Measurement Settings")]
    [Tooltip("길이 측정 설정 UI - 토글할 GameObject")]
    public GameObject measurementSettingUI;
    [Tooltip("VesselMeasurementTool 참조 - 측정 기능 제어용")]
    public VesselMeasurementTool vesselMeasurementTool;
    [Tooltip("전시 가이드 텍스트에 사용할 TMP 폰트")]
    public TMP_FontAsset exhibitionGuideFont;

    [Header("Settings Menu")]
    [Tooltip("설정 메뉴 - 설정 버튼 클릭 시 토글")]
    public GameObject settingsMenu;
    
    [Tooltip("폴더 선택 메뉴")]
    public GameObject folderSelectorMenu;
    
    [Header("Play Setting Menu")]
    [Tooltip("재생 설정 메뉴 - 프레임 컨트롤 토글 등")]
    public GameObject playSettingMenu;

    [Header("Buttons (assign in Inspector)")]
    public Interactable mainMenuButton;
    public Interactable streamlineMenuButton;
    public Interactable velocityMenuButton;
    public Interactable wssMenuButton;
    public Interactable resetVesselButton;
    public Interactable streamlinePlaybackButton;
    public Interactable streamlineBackButton;
    public Interactable velocityPlaybackButton;
    public Interactable velocityIntervalButton;
    public Interactable velocityVisualizationButton;
    public Interactable velocityBackButton;
    public Interactable velocityHomeButton;
    public Interactable visualizationSettingButton;
    
    [Header("UI Control Buttons (assign in Inspector)")]
    [Tooltip("설정 버튼 - 설정 메뉴 열기")]
    public Interactable settingsButton;
    [Tooltip("폴더 선택 버튼 - 폴더 선택 메뉴 열기")]
    public Interactable folderSelectorButton;
    [Tooltip("길이 측정 버튼 - 측정 설정 UI 열기")]
    public Interactable measurementButton;
    
    public Transform buttonCollectionRoot;

    [Header("Options")]
    public bool autoFindByName = true;
    public bool autoWireByName = true;
    public bool autoWireFromPaths = true;
    public bool networkSync = true;
    public bool autoStartExhibitionModeOnDevice = false;
    public bool autoOpenFolderSelectorOnDesktop = true;

    [Header("Frame Control Mode")]
    [Tooltip("프레임 컨트롤 모드 - 활성화 시 애니메이션 정지 및 슬라이더로 프레임 조작")]
    public bool isFrameControlMode = false;

    private Vector3 initPos;
    private Quaternion initRot;
    private Vector3 initScale;
    private Vector3 menuInitPos;
    private Quaternion menuInitRot;
    private Vector3 menuInitScale;
    private Vector3 exhibitionMenuInitLocalPos;
    private Quaternion exhibitionMenuInitLocalRot;
    private Vector3 exhibitionMenuInitLocalScale;
    private bool exhibitionMenuInitCaptured = false;

    private Vector3 lastMenuPosition;
    private Quaternion lastMenuRotation;
    private Vector3 lastMenuScale = Vector3.one;
    private bool hasLastMenuTransform = false;
    private bool exhibitionOverlayEnabled = false;
    private bool exhibitionOverlayPositionInitialized = false;
    private ExhibitionExperience currentExhibitionExperience = ExhibitionExperience.None;
    private Coroutine exhibitionCompletionRoutine;
    private bool mouseInputEnabled = true;
    private Coroutine mousePointerSuppressionRoutine;
    private bool exhibitionSliceKnifeDefaultsCaptured = false;
    private float exhibitionSliceKnifeGrabSizeDefault;
    private float exhibitionSliceKnifeGrabDepthDefault;
    private bool exhibitionSliceTransformCaptured = false;
    private Vector3 exhibitionSliceInitPos;
    private Quaternion exhibitionSliceInitRot;
    private Vector3 exhibitionSliceInitScale;
    private bool exhibitionSliceIndicatorTransformCaptured = false;
    private Vector3 exhibitionSliceIndicatorInitPos;
    private Quaternion exhibitionSliceIndicatorInitRot;
    private Vector3 exhibitionSliceIndicatorInitScale;
    private bool pendingDataChangeFromFolderSelector = false;
    private bool restoreExhibitionAfterDataChange = false;

    
    private Vector3 initMenuPosition;
    private Quaternion initMenuRotation;
    private Vector3 initMenuScale = Vector3.one;
    private const float ExhibitionCompletionSpeechEndDelaySeconds = 0.75f;
    private const float ExhibitionCompletionPromptFallbackSeconds = 10f;
    private const int MousePointerSuppressionRefreshCount = 4;
    private const float MousePointerSuppressionRefreshIntervalSeconds = 0.2f;


    public enum ButtonAction
    {
        ShowMain,
        ShowStreamline,
        ShowVelocity,
        ShowWss,
        ShowWssPlayback,
        ReturnWssMenu,
        ShowVisSetting,
        ShowVelocityPlayback,
        ShowVelocityInterval,
        ShowVelocityVisSetting,
        ReturnVisSetting,
        ReturnVelocityHome,
        ShowStreamlineSpeed,
        ReturnStreamlineHome,
        ReturnStreamlineMenu,
        ReturnStreamlineMain,
        ResetVessel,
        ResetApp,
        ShowVelocityArrows,
        ShowVelocityHeatmap,
        ShowVelocityMenuRoot,
        ToggleVelocityPlayback,
        ToggleStreamlinePlayback,
        ToggleWSSPlayback,
        ToggleSliceAxis,
        ShowSettings,
        ShowFolderSelector,
        ShowMeasurement,
        ToggleEnableMeasurement,
        ToggleObjectMoveMode,
        ShowPlaySetting,  // 재생 설정 메뉴 표시
        Toggle2D3D
    }

    private struct ButtonActionBinding
    {
        public string keyword;
        public ButtonAction action;
        public ButtonActionBinding(string keyword, ButtonAction action)
        {
            this.keyword = keyword;
            this.action = action;
        }
    }

    private static readonly List<ButtonActionBinding> DefaultBindings = new List<ButtonActionBinding>
    {
        new ButtonActionBinding("show streamline", ButtonAction.ShowStreamline),
        new ButtonActionBinding("show velocity", ButtonAction.ShowVelocity),
        new ButtonActionBinding("show wss", ButtonAction.ShowWss),
        new ButtonActionBinding("main", ButtonAction.ShowMain),
        new ButtonActionBinding("home", ButtonAction.ShowMain),
        new ButtonActionBinding("visualization setting", ButtonAction.ShowVisSetting),
        new ButtonActionBinding("velocity visualization", ButtonAction.ShowVelocityVisSetting),
        new ButtonActionBinding("playback", ButtonAction.ShowVelocityPlayback),
        new ButtonActionBinding("speed setting", ButtonAction.ShowVelocityPlayback),
        new ButtonActionBinding("interval", ButtonAction.ShowVelocityInterval),
        new ButtonActionBinding("spacing", ButtonAction.ShowVelocityInterval),
        new ButtonActionBinding("back", ButtonAction.ReturnVisSetting),
        new ButtonActionBinding("previous", ButtonAction.ReturnVisSetting),
        new ButtonActionBinding("streamline", ButtonAction.ShowStreamlineSpeed),
        new ButtonActionBinding("reset vessel", ButtonAction.ResetVessel),
        new ButtonActionBinding("혈관위치 초기화", ButtonAction.ResetVessel),
        new ButtonActionBinding("reset app", ButtonAction.ResetApp),
        new ButtonActionBinding("앱 초기화", ButtonAction.ResetApp),
        new ButtonActionBinding("toggle velocity", ButtonAction.ToggleVelocityPlayback),
        new ButtonActionBinding("toggle streamline", ButtonAction.ToggleStreamlinePlayback),
        new ButtonActionBinding("toggle wss", ButtonAction.ToggleWSSPlayback),
        new ButtonActionBinding("play", ButtonAction.ToggleVelocityPlayback),
        new ButtonActionBinding("pause", ButtonAction.ToggleVelocityPlayback),
        new ButtonActionBinding("axis", ButtonAction.ToggleSliceAxis),
        new ButtonActionBinding("축", ButtonAction.ToggleSliceAxis),
        new ButtonActionBinding("total setting", ButtonAction.ShowSettings),
        new ButtonActionBinding("데이터 변경", ButtonAction.ShowFolderSelector),
        new ButtonActionBinding("length measurement", ButtonAction.ShowMeasurement),
        new ButtonActionBinding("길이 측정 버튼", ButtonAction.ToggleEnableMeasurement),
        new ButtonActionBinding("혈관 조작 버튼", ButtonAction.ToggleObjectMoveMode),
        new ButtonActionBinding("play setting", ButtonAction.ShowPlaySetting),
        new ButtonActionBinding("재생 설정", ButtonAction.ShowPlaySetting),
        new ButtonActionBinding("이전으로 돌아가기", ButtonAction.ShowMain),
        new ButtonActionBinding("toggle 2d 3d", ButtonAction.Toggle2D3D)
    };

    void Awake()
    {
        Instance = this;
        Application.runInBackground = true;
        if (manager == null) manager = Manager.Instance ?? FindObjectOfType<Manager>();
        if (vesselRoot == null && manager != null && manager.ObjectParent != null)
        {
            vesselRoot = manager.ObjectParent.transform;
        }

        if (autoFindByName)
        {
            if (mainMenu == null) mainMenu = GameObject.Find("Main Menu");
            if (streamlineMenu == null) streamlineMenu = GameObject.Find("Show Streamline Under Menu");
            if (streamlineSpeedMenu == null) streamlineSpeedMenu = GameObject.Find("Streamline Speed Menu");
            if (velocityMenu == null) velocityMenu = GameObject.Find("Show Velocity Under Menu");
            if (wssMenu == null) wssMenu = GameObject.Find("Show WSS Under Menu") ?? GameObject.Find("WSS");
            if (visualizationSettingMenu == null) visualizationSettingMenu = GameObject.Find("Visualization Setting Under Menu");
            if (velocityVisualizationSettingMenu == null) velocityVisualizationSettingMenu = GameObject.Find("Velocity Visualization Setting Under Menu");
            if (velocityGoBackMenu == null) velocityGoBackMenu = GameObject.Find("Goback Menu") ?? GameObject.Find("Go Back Menu") ?? GameObject.Find("Back Menu");
            if (exhibitionMenu == null) exhibitionMenu = GameObject.Find("Exhibition Mode Menu");
            if (sliderController == null) sliderController = FindObjectOfType<GlobalSliderController>();
            if (sliceVisualization == null) sliceVisualization = FindObjectOfType<SliceController>();
        }
        if (vesselRoot != null)
        {
            initPos = vesselRoot.localPosition;
            initRot = vesselRoot.localRotation;
            initScale = vesselRoot.localScale;
        }
        if (mainMenuRoot != null)
        {
            menuInitPos = mainMenuRoot.localPosition;
            menuInitRot = mainMenuRoot.localRotation;
            menuInitScale = mainMenuRoot.localScale;

            initMenuPosition = mainMenuRoot.position;
            initMenuRotation = mainMenuRoot.rotation;
            initMenuScale = mainMenuRoot.localScale;
        }
        CaptureExhibitionMenuInitialTransform();
        ConfigureMixedRealityInputModule();
        ResetVesselTransformAndVisualization();
    }

    void Start()
    {
        initialization();
        ConfigureMixedRealityInputModule();
        SyncMixedRealityMousePointerState();
        StartCoroutine(InitializeStartupModeCoroutine());
    }

    void Update()
    {
        if (HanyangKeyInput.GetKeyDown(KeyCode.P))
        {
            ToggleExhibitionMenu();
        }

        if (SupportsDesktopMouseToggle() && HanyangKeyInput.GetKeyDown(KeyCode.M))
        {
            ToggleMouseInput();
        }

    }

    public void initialization()
    {
        // Re-acquire references after scene reload
        if (manager == null) manager = Manager.Instance ?? FindObjectOfType<Manager>();
        if (vesselRoot == null && manager != null && manager.ObjectParent != null)
        {
            vesselRoot = manager.ObjectParent.transform;
        }
        
        if (autoFindByName)
        {
            if (sliderController == null) sliderController = FindObjectOfType<GlobalSliderController>();
            if (sliceVisualization == null) sliceVisualization = FindObjectOfType<SliceController>();
        }

        CaptureExhibitionMenuInitialTransform();
        
        WireExplicitButtons();
        if (autoWireByName) WireButtonsByName();
        if (autoWireFromPaths) WireButtonsByPaths();
        WireResetButton();
        ShowMainMenu();
        
        // Ensure Slice Visualization and Knife are OFF at start
        if (sliceVisualization != null)
        {
            sliceVisualization.enabled = false;
            // Also hide the Knife Indicator if present
            if (sliceVisualization.indicatorController != null)
            {
                sliceVisualization.indicatorController.SetVisible(false);
            }
            if (sliceVisualization.visualsParent != null)
            {
                sliceVisualization.visualsParent.SetActive(false);
            }
        }
        
        SetupMenuLocks();
    }

    private IEnumerator InitializeStartupModeCoroutine()
    {
        int waitFrames = 0;
        while (exhibitionMenu == null && waitFrames < 60)
        {
            exhibitionMenu = GameObject.Find("Exhibition Mode Menu");
            waitFrames++;
            yield return null;
        }

        if (ShouldAutoStartExhibitionMode())
        {
            OpenExhibitionExperienceMenu();
            yield break;
        }

        if (autoOpenFolderSelectorOnDesktop && SupportsDesktopMouseToggle())
        {
            ShowFolderSelectorMenu();
        }
    }

    private bool ShouldAutoStartExhibitionMode()
    {
        return autoStartExhibitionModeOnDevice && !SupportsDesktopMouseToggle();
    }

    private void ExitExhibitionModeToGeneral()
    {
        CancelExhibitionCompletionRoutine();
        exhibitionOverlayEnabled = false;
        exhibitionOverlayPositionInitialized = false;
        currentExhibitionExperience = ExhibitionExperience.None;

        if (exhibitionMenu != null)
        {
            exhibitionMenu.SetActive(false);
        }

        ExhibitionGuideManager existingGuide = FindObjectOfType<ExhibitionGuideManager>();
        if (existingGuide != null)
        {
            existingGuide.StopGuide();
        }
    }

    private void HideFolderSelectorUI()
    {
        if (folderSelectorMenu == null)
        {
            return;
        }

        FolderSelectorUI folderUI = folderSelectorMenu.GetComponent<FolderSelectorUI>();
        if (folderUI != null)
        {
            folderUI.HideFolderSelector();
        }

        folderSelectorMenu.SetActive(false);
    }
    void AddListenerSafe(Interactable button, UnityAction action)
    {
        if (button == null || action == null) return;
        // Remove all listeners first to prevent duplicates (lambda functions can't be matched)
        button.OnClick.RemoveAllListeners();
        button.OnClick.AddListener(action);
    }

    void WireExplicitButtons()
    {
        AddListenerSafe(mainMenuButton, () => RunAction(ButtonAction.ShowMain));
        AddListenerSafe(streamlineMenuButton, () => RunAction(ButtonAction.ShowStreamline));
        AddListenerSafe(velocityMenuButton, () => RunAction(ButtonAction.ShowVelocity));
        AddListenerSafe(wssMenuButton, () => RunAction(ButtonAction.ShowWss));

        AddListenerSafe(streamlinePlaybackButton, () => RunAction(ButtonAction.ShowStreamlineSpeed));
        AddListenerSafe(streamlineBackButton, () => RunAction(ButtonAction.ReturnStreamlineHome));

        AddListenerSafe(visualizationSettingButton, () => RunAction(ButtonAction.ShowVisSetting));
        AddListenerSafe(velocityPlaybackButton, () => RunAction(ButtonAction.ShowVelocityPlayback));
        AddListenerSafe(velocityIntervalButton, () => RunAction(ButtonAction.ShowVelocityInterval));
        AddListenerSafe(velocityVisualizationButton, () => RunAction(ButtonAction.ShowVelocityVisSetting));
        AddListenerSafe(velocityBackButton, () => RunAction(ButtonAction.ReturnVisSetting));
        AddListenerSafe(velocityHomeButton, () => RunAction(ButtonAction.ReturnVelocityHome));
        
        // UI Control buttons (Settings, Folder Selector, Measurement)
        if (settingsButton != null)
        {
            AddListenerSafe(settingsButton, () => ToggleSettingsMenu());
            Debug.Log("<color=green>[ButtonController] settingsButton wired successfully</color>");
        }
        else
        {
            Debug.LogWarning("<color=yellow>[ButtonController] settingsButton is NULL - not wired</color>");
        }
        
        if (folderSelectorButton != null)
        {
            AddListenerSafe(folderSelectorButton, () => ShowFolderSelectorMenu());
            Debug.Log("<color=green>[ButtonController] folderSelectorButton wired successfully</color>");
        }
        else
        {
            Debug.LogWarning("<color=yellow>[ButtonController] folderSelectorButton is NULL - not wired</color>");
        }
        
        AddListenerSafe(measurementButton, () => ToggleMeasurementSettingUI());
    }

    void WireButtonsByName()
    {
        Transform root = buttonCollectionRoot != null ? buttonCollectionRoot : transform;
        var interactables = root.GetComponentsInChildren<Interactable>(true);
        foreach (var it in interactables)
        {
            string lower = it.name.ToLowerInvariant();
            var actions = GetActionsForName(lower);
            foreach (var act in actions)
            {
                UnityAction action = ResolveAction(act);
                if (action != null)
                {
                    it.OnClick.RemoveListener(action);
                    it.OnClick.AddListener(action);
                }
            }
        }
    }

        void WireButtonsByPaths()
    {
        var bindings = new (string path, ButtonAction action)[]
        {
            ("Button Parent/Main Menu/ButtonCollection/Show Streamline", ButtonAction.ShowStreamline),
            ("Button Parent/Main Menu/ButtonCollection/Show Velocity", ButtonAction.ShowVelocity),
            ("Button Parent/Main Menu/ButtonCollection/Show WSS", ButtonAction.ShowWss),
            ("Button Parent/Main Menu/ButtonCollection/혈관위치 초기화", ButtonAction.ResetVessel),
            ("Button Parent/Main Menu/ButtonCollection/앱 초기화", ButtonAction.ResetApp),

            ("Button Parent/Show Streamline Under Menu/ButtonCollection/재생 속도 설정", ButtonAction.ShowStreamlineSpeed),
            ("Button Parent/Show Streamline Under Menu/ButtonCollection/홈으로 돌아가기", ButtonAction.ShowMain),
            ("Button Parent/Streamline Speed Menu/ButtonCollection/이전으로 돌아가기", ButtonAction.ReturnStreamlineMenu),
            ("Button Parent/Streamline Speed Menu/ButtonCollection/홈으로 돌아가기", ButtonAction.ShowMain),

            ("Button Parent/Show WSS Under Menu/ButtonCollection/재생 속도 설정", ButtonAction.ShowWssPlayback),
            ("Button Parent/Show WSS Under Menu/ButtonCollection/홈으로 돌아가기", ButtonAction.ShowMain),
            ("Button Parent/WSS Speed Menu/ButtonCollection/이전으로 돌아가기", ButtonAction.ReturnWssMenu),
            ("Button Parent/WSS Speed Menu/ButtonCollection/홈으로 돌아가기", ButtonAction.ShowMain),

            ("Button Parent/Show Velocity Under Menu/ButtonCollection/시각화 설정", ButtonAction.ShowVisSetting),
            ("Button Parent/Show Velocity Under Menu/ButtonCollection/홈으로 돌아가기", ButtonAction.ShowMain),

            ("Button Parent/Visualization Setting Under Menu/ButtonCollection/재생 속도 설정", ButtonAction.ShowVelocityPlayback),
            ("Button Parent/Visualization Setting Under Menu/ButtonCollection/단면 간격 설정", ButtonAction.ShowVelocityInterval),
            ("Button Parent/Visualization Setting Under Menu/ButtonCollection/단면 시각화 설정", ButtonAction.ShowVelocityVisSetting),
            ("Button Parent/Visualization Setting Under Menu/ButtonCollection/축 변경", ButtonAction.ToggleSliceAxis),
            ("Button Parent/Visualization Setting Under Menu/ButtonCollection/이전으로 돌아가기", ButtonAction.ShowVelocityMenuRoot),

            ("Button Parent/Velocity Visualization Setting Under Menu/ButtonCollection/단면 속도장", ButtonAction.ShowVelocityArrows),
            ("Button Parent/Velocity Visualization Setting Under Menu/ButtonCollection/Heatmap", ButtonAction.ShowVelocityHeatmap),
            ("Button Parent/Velocity Visualization Setting Under Menu/ButtonCollection/축 변경", ButtonAction.ToggleSliceAxis),
            ("Button Parent/Velocity Visualization Setting Under Menu/ButtonCollection/이전으로 돌아가기", ButtonAction.ShowVisSetting),

            ("Button Parent/Go back Menu/ButtonCollection/이전으로 돌아가기", ButtonAction.ReturnVisSetting),
            
            // Playback toggle buttons
            ("Button Parent/Show Streamline Under Menu/ButtonCollection/재생", ButtonAction.ToggleStreamlinePlayback),
            ("Button Parent/Show Streamline Under Menu/ButtonCollection/정지", ButtonAction.ToggleStreamlinePlayback),
            ("Button Parent/Show WSS Under Menu/ButtonCollection/재생", ButtonAction.ToggleWSSPlayback),
            ("Button Parent/Show WSS Under Menu/ButtonCollection/정지", ButtonAction.ToggleWSSPlayback),
            ("Button Parent/Show Velocity Under Menu/ButtonCollection/재생", ButtonAction.ToggleVelocityPlayback),
            ("Button Parent/Show Velocity Under Menu/ButtonCollection/정지", ButtonAction.ToggleVelocityPlayback),
        };

        foreach (var b in bindings)
        {
            var btn = FindInteractableByPath(b.path);
            var action = ResolveAction(b.action);
            if (btn != null && action != null)
            {
                btn.OnClick.RemoveListener(action);
                btn.OnClick.AddListener(action);
            }
        }
    }

    Interactable FindInteractableByPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var parts = path.Split('/');
        if (parts.Length == 0) return null;

        GameObject current = GameObject.Find(parts[0]);
        if (current == null) return null;
        for (int i = 1; i < parts.Length; i++)
        {
            var child = current.transform.Find(parts[i]);
            if (child == null) return null;
            current = child.gameObject;
        }
        return current.GetComponent<Interactable>();
    }

    List<ButtonAction> GetActionsForName(string lowerName)
    {
        var found = new List<ButtonAction>();
        foreach (var b in DefaultBindings)
        {
            if (lowerName.Contains(b.keyword))
            {
                if (!found.Contains(b.action)) found.Add(b.action);
            }
        }
        return found;
    }

    UnityAction ResolveAction(ButtonAction action)
    {
        return () => RunAction(action);
    }

    void PerformLocalAction(ButtonAction action, bool networkCall = false)
    {
        switch (action)
        {
            case ButtonAction.ShowMain: ShowMainMenu(networkCall); break;
            case ButtonAction.ShowStreamline: ShowStreamlineMenu(networkCall); break;
            case ButtonAction.ShowVelocity: ShowVelocityMenu(networkCall); break;
            case ButtonAction.ShowWss: ShowWssMenu(networkCall); break;
            case ButtonAction.ShowWssPlayback: ShowWssPlaybackSetting(networkCall); break;
            case ButtonAction.ReturnWssMenu: ShowWssMenu(networkCall); break;
            case ButtonAction.ShowVisSetting: ShowVisualizationSettingMenu(networkCall); break;
            case ButtonAction.ShowVelocityPlayback: ShowVelocityPlaybackSetting(networkCall); break;
            case ButtonAction.ShowVelocityInterval: ShowVelocitySliceIntervalSetting(networkCall); break;
            case ButtonAction.ShowVelocityVisSetting: ShowVelocityVisualizationSettingMenu(networkCall); break;
            case ButtonAction.ReturnVisSetting: ReturnToVisualizationSetting(networkCall); break;
            case ButtonAction.ReturnVelocityHome: ReturnHomeFromVelocityVisualization(networkCall); break;
            case ButtonAction.ShowStreamlineSpeed: ShowStreamlineSpeedSetting(networkCall); break;
            case ButtonAction.ReturnStreamlineHome: ReturnHomeFromStreamline(networkCall); break;
            case ButtonAction.ReturnStreamlineMenu: ShowStreamlineMenu(networkCall); break;
            case ButtonAction.ReturnStreamlineMain: ShowMainMenu(networkCall); break;
            case ButtonAction.ResetVessel: ResetVesselTransform(); break;
            case ButtonAction.ResetApp: ResetApp(); break;
            case ButtonAction.ShowVelocityArrows: ShowVelocitySliceArrows(networkCall); break;
            case ButtonAction.ShowVelocityHeatmap: ShowVelocityHeatmap(networkCall); break;
            case ButtonAction.ShowVelocityMenuRoot: ShowVelocityMenu(networkCall); break;
            case ButtonAction.ToggleVelocityPlayback: ToggleVelocityPlayback(); break;
            case ButtonAction.ToggleStreamlinePlayback: ToggleStreamlinePlayback(); break;
            case ButtonAction.ToggleWSSPlayback: ToggleWSSPlayback(); break;
            case ButtonAction.ToggleSliceAxis: ToggleSliceAxisInternal(false, networkCall); break;
            case ButtonAction.ShowSettings: ToggleSettingsMenu(networkCall); break;
            case ButtonAction.ShowFolderSelector: ShowFolderSelectorMenu(networkCall); break;
            case ButtonAction.ShowMeasurement: ToggleMeasurementSettingUI(networkCall); break;
            case ButtonAction.ToggleEnableMeasurement: ToggleEnableMeasurement(networkCall); break;
            case ButtonAction.ToggleObjectMoveMode: ToggleObjectMoveMode(networkCall); break;
            case ButtonAction.ShowPlaySetting: TogglePlaySettingMenu(networkCall); break;
            case ButtonAction.Toggle2D3D: ToggleVelocity2D3D(networkCall); break;
            default: break;
        }
    }

    public void RunAction(ButtonAction action, bool broadcast = true)
    {
        if (action == ButtonAction.ToggleSliceAxis)
        {
            Debug.Log("<color=cyan>[ButtonAction] ToggleSliceAxis triggered</color>");
        }

        if (broadcast && networkSync && PhotonSyncService.Instance != null)
        {
            PhotonSyncService.Instance.BroadcastButtonAction(action);
        }
        PerformLocalAction(action, networkCall: false);  // Local call
    }
    
    public void RunAction(ButtonAction action, bool broadcast, bool networkCall)
    {
        if (broadcast && networkSync && PhotonSyncService.Instance != null)
        {
            PhotonSyncService.Instance.BroadcastButtonAction(action);
        }
        PerformLocalAction(action, networkCall);
    }

    void WireResetButton()
    {
        AddListenerSafe(resetVesselButton, () => RunAction(ButtonAction.ResetVessel));
    }

    string GetPath(Transform t, Transform root)
    {
        var stack = new Stack<string>();
        var current = t;
        while (current != null && current != root)
        {
            stack.Push(current.name);
            current = current.parent;
        }
        if (current == root) stack.Push(root.name);
        return string.Join("/", stack.ToArray());
    }

    GameObject GetCurrentActiveMenu()
    {
        if (mainMenu != null && mainMenu.activeSelf) return mainMenu;
        if (streamlineMenu != null && streamlineMenu.activeSelf) return streamlineMenu;
        if (streamlineSpeedMenu != null && streamlineSpeedMenu.activeSelf) return streamlineSpeedMenu;
        if (velocityMenu != null && velocityMenu.activeSelf) return velocityMenu;
        if (wssMenu != null && wssMenu.activeSelf) return wssMenu;
        if (visualizationSettingMenu != null && visualizationSettingMenu.activeSelf) return visualizationSettingMenu;
        if (velocityVisualizationSettingMenu != null && velocityVisualizationSettingMenu.activeSelf) return velocityVisualizationSettingMenu;
        if (velocityGoBackMenu != null && velocityGoBackMenu.activeSelf) return velocityGoBackMenu;
        if (exhibitionMenu != null && exhibitionMenu.activeSelf) return exhibitionMenu;
        if (settingsMenu != null && settingsMenu.activeSelf) return settingsMenu;
        if (folderSelectorMenu != null && folderSelectorMenu.activeSelf) return folderSelectorMenu;
        if (measurementSettingUI != null && measurementSettingUI.activeSelf) return measurementSettingUI;
        if (playSettingMenu != null && playSettingMenu.activeSelf) return playSettingMenu;
        return null;
    }

    public ButtonAction? GetActiveMenuAction()
    {
        GameObject current = GetCurrentActiveMenu();
        if (current == null) return null;
        if (current == mainMenu) return ButtonAction.ShowMain;
        if (current == streamlineMenu) return ButtonAction.ShowStreamline;
        if (current == streamlineSpeedMenu) return ButtonAction.ShowStreamlineSpeed;
        if (current == velocityMenu) return ButtonAction.ShowVelocity;
        if (current == wssMenu)
        {
            if (sliderController != null && sliderController.IsActive && sliderController.mode == ControlMode.WssPlayback)
            {
                return ButtonAction.ShowWssPlayback;
            }
            return ButtonAction.ShowWss;
        }
        if (current == visualizationSettingMenu) return ButtonAction.ShowVisSetting;
        if (current == velocityVisualizationSettingMenu) return ButtonAction.ShowVelocityVisSetting;
        if (current == velocityGoBackMenu)
        {
            if (sliderController != null && sliderController.IsActive)
            {
                switch (sliderController.mode)
                {
                    case ControlMode.VelocityPlayback:
                        return ButtonAction.ShowVelocityPlayback;
                    case ControlMode.DensityX:
                    case ControlMode.DensityY:
                    case ControlMode.DensityZ:
                        return ButtonAction.ShowVelocityInterval;
                }
            }
            return ButtonAction.ShowVelocityPlayback;
        }
        if (current == settingsMenu) return ButtonAction.ShowSettings;
        if (current == folderSelectorMenu) return ButtonAction.ShowFolderSelector;
        if (current == measurementSettingUI) return ButtonAction.ShowMeasurement;
        if (current == playSettingMenu) return ButtonAction.ShowPlaySetting;
        return null;
    }

    void SetMenu(GameObject targetMenu, bool networkCall = false)
    {
        if (targetMenu != null && targetMenu != exhibitionMenu && exhibitionOverlayEnabled)
        {
            exhibitionOverlayEnabled = false;
            exhibitionOverlayPositionInitialized = false;
            currentExhibitionExperience = ExhibitionExperience.None;

            ExhibitionGuideManager existingGuide = FindObjectOfType<ExhibitionGuideManager>();
            if (existingGuide != null)
            {
                existingGuide.StopGuide();
            }
        }

        // Store current active menu transform before switching
        GameObject currentActive = GetCurrentActiveMenu();
        if (!networkCall && currentActive != null && currentActive != targetMenu)
        {
            lastMenuPosition = currentActive.transform.position;
            lastMenuRotation = currentActive.transform.rotation;
            lastMenuScale = currentActive.transform.localScale;
            hasLastMenuTransform = true;
        }
        
        // Apply last position to new menu if available
        if (!networkCall && hasLastMenuTransform && targetMenu != null)
        {
            targetMenu.transform.position = lastMenuPosition;
            targetMenu.transform.rotation = lastMenuRotation;
            targetMenu.transform.localScale = lastMenuScale;
        }

        if (mainMenu != null) mainMenu.SetActive(targetMenu == mainMenu);
        if (streamlineMenu != null) streamlineMenu.SetActive(targetMenu == streamlineMenu);
        if (streamlineSpeedMenu != null) streamlineSpeedMenu.SetActive(targetMenu == streamlineSpeedMenu);
        if (velocityMenu != null) velocityMenu.SetActive(targetMenu == velocityMenu);
        if (wssMenu != null) wssMenu.SetActive(targetMenu == wssMenu);
        if (visualizationSettingMenu != null) visualizationSettingMenu.SetActive(targetMenu == visualizationSettingMenu);
        if (velocityVisualizationSettingMenu != null) velocityVisualizationSettingMenu.SetActive(targetMenu == velocityVisualizationSettingMenu);
        if (velocityGoBackMenu != null) velocityGoBackMenu.SetActive(targetMenu == velocityGoBackMenu);
        if (exhibitionMenu != null)
        {
            bool shouldShowExhibition = targetMenu == exhibitionMenu || exhibitionOverlayEnabled;
            exhibitionMenu.SetActive(shouldShowExhibition);
            if (!shouldShowExhibition)
            {
                ExhibitionGuideManager existingGuide = FindObjectOfType<ExhibitionGuideManager>();
                if (existingGuide != null)
                {
                    existingGuide.StopGuide();
                }
            }
        }
        if (settingsMenu != null) settingsMenu.SetActive(targetMenu == settingsMenu);
        if (folderSelectorMenu != null) folderSelectorMenu.SetActive(targetMenu == folderSelectorMenu);
        if (measurementSettingUI != null) measurementSettingUI.SetActive(targetMenu == measurementSettingUI);
        if (playSettingMenu != null) playSettingMenu.SetActive(targetMenu == playSettingMenu);
    }

    void AlignMenuToMain(GameObject targetMenu, bool networkCall = false)
    {
        if (networkCall || mainMenu == null || targetMenu == null) return;
        
        targetMenu.transform.position = mainMenu.transform.position;
        targetMenu.transform.rotation = mainMenu.transform.rotation;
        targetMenu.transform.localScale = mainMenu.transform.localScale;
    }

    // ----- Menu actions -----
    public void ShowMainMenu(bool networkCall = false)
    {
        ExitExhibitionModeToGeneral();

        if (manager != null)
        {
            manager.SetGlobalInputLock(false);
        }

        SetMenu(mainMenu, networkCall);
        
        // Only deactivate WSS when coming from WSS mode
        if (manager != null)
        {
            manager.SetWSSMeshVisibility(false);
            manager.SetWSSVectorVisibility(false);
            manager.SetMeshVisibility(true);  // BloodVessel만 표시
            
            if (sliceVisualization != null && sliceVisualization.visualsParent != null)
            {
                sliceVisualization.enabled = false;
                sliceVisualization.indicatorController.SetVisible(false);
                sliceVisualization.visualsParent.SetActive(false);
            }
            if (sliceVisualization != null)
            {
                sliceVisualization.DeactivateVisualization();
            }
        }
        
        if (manager != null) manager.visualizationMode = VisualizationMode.Mesh;
        if (sliderController != null) sliderController.SetSliderActive(false);

    }

    public void ShowExhibitionMenu(bool networkCall = false)
    {
        CancelExhibitionCompletionRoutine();
        Debug.Log($"[Exhibition] ShowExhibitionMenu called. networkCall={networkCall}, menuAssigned={(exhibitionMenu != null)}, overlayEnabled={exhibitionOverlayEnabled}");
        if (exhibitionMenu == null)
        {
            Debug.LogWarning("<color=yellow>[ButtonController] exhibitionMenu is not assigned!</color>");
            return;
        }

        if (exhibitionOverlayEnabled && exhibitionMenu.activeSelf)
        {
            Debug.Log("[Exhibition] Hiding exhibition overlay.");
            ResetVesselTransformAndVisualization();
            exhibitionOverlayEnabled = false;
            exhibitionOverlayPositionInitialized = false;
            exhibitionMenu.SetActive(false);
            ExhibitionGuideManager.EnsureInstance().StopGuide();
            ShowMainMenu(networkCall);
            return;
        }

        ResetVesselTransformAndVisualization();
        exhibitionOverlayEnabled = true;
        currentExhibitionExperience = ExhibitionExperience.None;
        Debug.Log("[Exhibition] Showing exhibition overlay.");

        HideFolderSelectorUI();

        if (manager != null)
        {
            manager.SetGlobalInputLock(false);
        }

        if (!networkCall && !exhibitionOverlayPositionInitialized)
        {
            if (settingsMenu != null)
            {
                exhibitionMenu.transform.localScale = settingsMenu.transform.localScale;
            }

            exhibitionOverlayPositionInitialized = true;
        }

        if (mainMenu != null) mainMenu.SetActive(false);
        if (settingsMenu != null) settingsMenu.SetActive(false);
        exhibitionMenu.SetActive(true);
        ExhibitionGuideManager.EnsureInstance().ShowMenuIntro(exhibitionMenu.transform);
        Debug.Log($"[Exhibition] Menu active={exhibitionMenu.activeSelf}, position={exhibitionMenu.transform.position}");

        if (sliderController != null) sliderController.SetSliderActive(false);
        if (sliceVisualization != null)
        {
            sliceVisualization.DeactivateVisualization();
            if (sliceVisualization.visualsParent != null)
            {
                sliceVisualization.visualsParent.SetActive(false);
            }
        }

        if (measurementSettingUI != null) measurementSettingUI.SetActive(false);
        if (playSettingMenu != null) playSettingMenu.SetActive(false);
    }

    public void ToggleExhibitionMenu()
    {
        Debug.Log("[Exhibition] ToggleExhibitionMenu called.");
        ShowExhibitionMenu(false);
    }

    public void ToggleMouseInput()
    {
        if (!SupportsDesktopMouseToggle())
        {
            return;
        }

        if (mouseInputEnabled)
        {
            mouseInputEnabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SyncMixedRealityMousePointerState();
            StartMousePointerSuppressionRefresh();
            Debug.Log("[Input] Desktop cursor mode enabled.");
            return;
        }

        mouseInputEnabled = true;
        StopMousePointerSuppressionRefresh();
        Cursor.visible = false;
        SyncMixedRealityMousePointerState();
        Debug.Log("[Input] Unity cursor mode enabled.");
    }

    private void StartMousePointerSuppressionRefresh()
    {
        StopMousePointerSuppressionRefresh();
        mousePointerSuppressionRoutine = StartCoroutine(RefreshMousePointerSuppression());
    }

    private void StopMousePointerSuppressionRefresh()
    {
        if (mousePointerSuppressionRoutine == null)
        {
            return;
        }

        StopCoroutine(mousePointerSuppressionRoutine);
        mousePointerSuppressionRoutine = null;
    }

    private IEnumerator RefreshMousePointerSuppression()
    {
        for (int i = 0; i < MousePointerSuppressionRefreshCount && !mouseInputEnabled; i++)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SetMousePointerVisualsVisible(false);
            yield return new WaitForSecondsRealtime(MousePointerSuppressionRefreshIntervalSeconds);
        }

        mousePointerSuppressionRoutine = null;
    }

    private void SyncMixedRealityMousePointerState()
    {
        // Mirror the M-key desktop cursor toggle to MRTK pointers shown in XR.
        PointerBehavior desiredBehavior = mouseInputEnabled ? PointerBehavior.Default : PointerBehavior.AlwaysOff;

        PointerUtils.SetPointerBehavior<MousePointer>(desiredBehavior, InputSourceType.Other);
        PointerUtils.SetPointerBehavior<ScreenSpaceMousePointer>(desiredBehavior, InputSourceType.Other);
        PointerUtils.SetGazePointerBehavior(desiredBehavior);

        IMixedRealityCursor currentGazeCursor = CoreServices.InputSystem?.GazeProvider?.GazeCursor;
        if (currentGazeCursor != null)
        {
            currentGazeCursor.SetVisibility(mouseInputEnabled);
        }

        SetMousePointerVisualsVisible(mouseInputEnabled);
    }

    private void SetMousePointerVisualsVisible(bool visible)
    {
        // MouseDeviceManager enables MRTK debug rays unconditionally. Force them off.
        MixedRealityRaycaster.DebugEnabled = false;

        foreach (IMixedRealityPointer pointer in PointerUtils.GetPointers())
        {
            bool isMousePointer =
                pointer is IMixedRealityMousePointer ||
                (!string.IsNullOrEmpty(pointer.PointerName) && pointer.PointerName.Contains("MousePointer"));

            if (!isMousePointer)
            {
                continue;
            }

            if (pointer.BaseCursor != null)
            {
                pointer.BaseCursor.SetVisibility(visible);

                GameObject cursorObject = pointer.BaseCursor.GameObjectReference;
                if (cursorObject != null && cursorObject.activeSelf != visible)
                {
                    cursorObject.SetActive(visible);
                }
            }
        }

        foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (!go.name.Contains("MousePointer(Clone)_Cursor"))
            {
                continue;
            }

            if (go.activeSelf != visible)
            {
                go.SetActive(visible);
            }
        }
    }

    private void ConfigureMixedRealityInputModule()
    {
        MixedRealityInputModule inputModule =
            Camera.main != null
                ? Camera.main.GetComponent<MixedRealityInputModule>()
                : FindObjectOfType<MixedRealityInputModule>();

        if (inputModule == null)
        {
            return;
        }

        inputModule.forceModuleActive = true;
    }

    private bool SupportsDesktopMouseToggle()
    {
        return Application.isEditor || SystemInfo.deviceType == DeviceType.Desktop;
    }

    public bool IsExhibitionModeActive()
    {
        return exhibitionOverlayEnabled;
    }

    public ExhibitionExperience CurrentExhibitionExperience => currentExhibitionExperience;

    public bool HasActiveExhibitionExperience()
    {
        return currentExhibitionExperience != ExhibitionExperience.None;
    }

    public bool CanManuallyFinishCurrentExhibitionExperience()
    {
        if (currentExhibitionExperience == ExhibitionExperience.None)
        {
            return false;
        }

        ExhibitionGuideManager guideManager = ExhibitionGuideManager.EnsureInstance();
        return guideManager != null && guideManager.IsManualFinishAvailable(currentExhibitionExperience);
    }

    public bool ShowCurrentExhibitionCompletionPrompt()
    {
        if (!CanManuallyFinishCurrentExhibitionExperience())
        {
            return false;
        }

        CancelExhibitionCompletionRoutine();

        Transform anchor = exhibitionMenu != null ? exhibitionMenu.transform : vesselRoot;
        PrepareExhibitionCompletionView();
        ExhibitionGuideManager guideManager = ExhibitionGuideManager.EnsureInstance();
        guideManager.ShowCompletionPrompt(currentExhibitionExperience, anchor);
        exhibitionCompletionRoutine = StartCoroutine(ReturnToExhibitionHomeAfterCompletionPrompt(guideManager));
        return true;
    }

    public void RestartCurrentExhibitionExperience()
    {
        switch (currentExhibitionExperience)
        {
            case ExhibitionExperience.Manipulation:
                StartExhibitionManipulationMode();
                break;
            case ExhibitionExperience.Velocity:
                StartExhibitionVelocityMode();
                break;
            case ExhibitionExperience.Slice:
                StartExhibitionSliceMode();
                break;
            case ExhibitionExperience.Streamline:
                StartExhibitionStreamlineMode();
                break;
            case ExhibitionExperience.Wss:
                StartExhibitionWssMode();
                break;
            default:
                ReturnToExhibitionHome();
                break;
        }
    }

    public void ReturnToExhibitionHome()
    {
        CancelExhibitionCompletionRoutine();
        currentExhibitionExperience = ExhibitionExperience.None;
        ResetExhibitionHomeState();

        if (mainMenu != null) mainMenu.SetActive(false);
        if (streamlineMenu != null) streamlineMenu.SetActive(false);
        if (streamlineSpeedMenu != null) streamlineSpeedMenu.SetActive(false);
        if (velocityMenu != null) velocityMenu.SetActive(false);
        if (wssMenu != null) wssMenu.SetActive(false);
        if (visualizationSettingMenu != null) visualizationSettingMenu.SetActive(false);
        if (velocityVisualizationSettingMenu != null) velocityVisualizationSettingMenu.SetActive(false);
        if (velocityGoBackMenu != null) velocityGoBackMenu.SetActive(false);
        if (settingsMenu != null) settingsMenu.SetActive(false);
        if (folderSelectorMenu != null) folderSelectorMenu.SetActive(false);
        if (measurementSettingUI != null) measurementSettingUI.SetActive(false);
        if (playSettingMenu != null) playSettingMenu.SetActive(false);

        exhibitionOverlayEnabled = true;
        exhibitionOverlayPositionInitialized = false;

        if (exhibitionMenu != null)
        {
            exhibitionMenu.SetActive(true);
        }

        ExhibitionGuideManager.EnsureInstance().ShowMenuIntro(exhibitionMenu != null ? exhibitionMenu.transform : null);
    }

    private void PrepareExhibitionCompletionView()
    {
        if (mainMenu != null) mainMenu.SetActive(false);
        if (streamlineMenu != null) streamlineMenu.SetActive(false);
        if (streamlineSpeedMenu != null) streamlineSpeedMenu.SetActive(false);
        if (velocityMenu != null) velocityMenu.SetActive(false);
        if (wssMenu != null) wssMenu.SetActive(false);
        if (visualizationSettingMenu != null) visualizationSettingMenu.SetActive(false);
        if (velocityVisualizationSettingMenu != null) velocityVisualizationSettingMenu.SetActive(false);
        if (velocityGoBackMenu != null) velocityGoBackMenu.SetActive(false);
        if (settingsMenu != null) settingsMenu.SetActive(false);
        if (folderSelectorMenu != null) folderSelectorMenu.SetActive(false);
        if (measurementSettingUI != null) measurementSettingUI.SetActive(false);
        if (playSettingMenu != null) playSettingMenu.SetActive(false);

        exhibitionOverlayEnabled = true;

        HideExhibitionSlider();
        HideExhibitionSliceToggleMenu();
        HideExhibitionWssModeMenu();
        HideExhibitionSliceVisuals();

        if (sliceVisualization != null)
        {
            sliceVisualization.DeactivateVisualization();
            if (sliceVisualization.visualsParent != null)
            {
                sliceVisualization.visualsParent.SetActive(false);
            }
        }

        if (manager != null)
        {
            manager.SetGlobalInputLock(false);
        }
    }

    private void CancelExhibitionCompletionRoutine()
    {
        if (exhibitionCompletionRoutine == null)
        {
            return;
        }

        StopCoroutine(exhibitionCompletionRoutine);
        exhibitionCompletionRoutine = null;
    }

    private System.Collections.IEnumerator ReturnToExhibitionHomeAfterCompletionPrompt(ExhibitionGuideManager guideManager)
    {
        float elapsed = 0f;
        float silenceTimer = 0f;
        bool speechObserved = false;

        while (elapsed < ExhibitionCompletionPromptFallbackSeconds)
        {
            bool isSpeaking = guideManager != null && guideManager.IsPromptSpeechActive();

            if (isSpeaking)
            {
                speechObserved = true;
                silenceTimer = 0f;
            }
            else if (speechObserved)
            {
                silenceTimer += Time.deltaTime;
                if (silenceTimer >= ExhibitionCompletionSpeechEndDelaySeconds)
                {
                    break;
                }
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        exhibitionCompletionRoutine = null;
        ReturnToExhibitionHome();
    }

    private void ResetExhibitionHomeState()
    {
        hasLastMenuTransform = false;

        if (vesselRoot != null)
        {
            vesselRoot.localPosition = initPos;
            vesselRoot.localRotation = initRot;
            vesselRoot.localScale = initScale;
        }

        if (mainMenuRoot != null)
        {
            mainMenuRoot.localPosition = menuInitPos;
            mainMenuRoot.localRotation = menuInitRot;
            mainMenuRoot.localScale = menuInitScale;
        }

        if (Manager.Instance != null)
        {
            Manager.Instance.LoadAndApplySettings();
        }

        if (vesselMeasurementTool != null)
        {
            vesselMeasurementTool.ReloadCalibration();
        }

        if (sliderController != null)
        {
            sliderController.SetSliderActive(false);
        }

        if (sliceVisualization != null)
        {
            sliceVisualization.DeactivateVisualization();
            if (sliceVisualization.visualsParent != null)
            {
                sliceVisualization.visualsParent.SetActive(false);
            }

            if (sliceVisualization.viewRenderer != null)
            {
                sliceVisualization.viewRenderer.gameObject.SetActive(false);
            }

            if (sliceVisualization.indicatorController != null)
            {
                sliceVisualization.indicatorController.SetVisible(false);
                sliceVisualization.indicatorController.ForceHideKnifeVisuals();
                sliceVisualization.indicatorController.enabled = false;
                sliceVisualization.indicatorController.gameObject.SetActive(false);
            }
        }

        if (manager != null)
        {
            manager.visualizationMode = VisualizationMode.Mesh;
            manager.ApplyVisualizationMode();

            if (manager.velocityColorBar != null)
            {
                manager.velocityColorBar.Hide();
            }

            if (manager.wssColorBar != null)
            {
                manager.wssColorBar.Hide();
            }
        }

        ResetExhibitionOverlay(true);
    }

    public void OpenExhibitionExperienceMenu()
    {
        ShowExhibitionMenu();
    }

    public void StartExhibitionManipulationMode()
    {
        CancelExhibitionCompletionRoutine();
        currentExhibitionExperience = ExhibitionExperience.Manipulation;
        ResetVesselForExhibition();
        HideExhibitionSliceVisuals();

        if (manager != null)
        {
            manager.visualizationMode = VisualizationMode.Mesh;
            manager.ApplyVisualizationMode();
        }

        if (sliderController != null)
        {
            sliderController.SetSliderActive(false);
        }

        HideExhibitionSliceToggleMenu();

        if (sliceVisualization != null)
        {
            sliceVisualization.DeactivateVisualization();
            if (sliceVisualization.visualsParent != null)
            {
                sliceVisualization.visualsParent.SetActive(false);
            }
        }

        if (measurementSettingUI != null)
        {
            measurementSettingUI.SetActive(false);
        }

        if (playSettingMenu != null)
        {
            playSettingMenu.SetActive(false);
        }

        exhibitionOverlayEnabled = true;
        if (mainMenu != null) mainMenu.SetActive(false);
        if (settingsMenu != null) settingsMenu.SetActive(false);
        if (exhibitionMenu != null) exhibitionMenu.SetActive(true);
        ExhibitionGuideManager.EnsureInstance().StartManipulationGuide();
    }

    public void StartExhibitionManipulationGuide()
    {
        CancelExhibitionCompletionRoutine();
        if (settingsMenu != null) settingsMenu.SetActive(false);

        if (exhibitionMenu != null)
        {
            exhibitionMenu.SetActive(true);
        }

        ExhibitionGuideManager.EnsureInstance().StartManipulationGuide();
    }

    public void StartExhibitionVelocityMode()
    {
        CancelExhibitionCompletionRoutine();
        currentExhibitionExperience = ExhibitionExperience.Velocity;
        ResetVesselForExhibition();
        HideExhibitionSliceVisuals();
        exhibitionOverlayEnabled = true;

        if (manager != null)
        {
            manager.visualizationMode = VisualizationMode.Velocity;
            manager.ApplyVisualizationMode();
        }

        if (mainMenu != null) mainMenu.SetActive(false);
        if (streamlineMenu != null) streamlineMenu.SetActive(false);
        if (streamlineSpeedMenu != null) streamlineSpeedMenu.SetActive(false);
        if (velocityMenu != null) velocityMenu.SetActive(false);
        if (wssMenu != null) wssMenu.SetActive(false);
        if (visualizationSettingMenu != null) visualizationSettingMenu.SetActive(false);
        if (velocityVisualizationSettingMenu != null) velocityVisualizationSettingMenu.SetActive(false);
        if (velocityGoBackMenu != null) velocityGoBackMenu.SetActive(false);
        if (folderSelectorMenu != null) folderSelectorMenu.SetActive(false);
        if (measurementSettingUI != null) measurementSettingUI.SetActive(false);
        if (playSettingMenu != null) playSettingMenu.SetActive(false);

        if (settingsMenu != null)
        {
            settingsMenu.SetActive(false);
        }

        if (exhibitionMenu != null)
        {
            exhibitionMenu.SetActive(true);
        }

        HideExhibitionSlider();

        if (sliceVisualization != null)
        {
            sliceVisualization.DeactivateVisualization();
            if (sliceVisualization.visualsParent != null)
            {
                sliceVisualization.visualsParent.SetActive(false);
            }
        }

        HideExhibitionSliceToggleMenu();

        hasLastMenuTransform = false;
        lastMenuPosition = mainMenu != null ? mainMenu.transform.position : initMenuPosition;
        lastMenuRotation = mainMenu != null ? mainMenu.transform.rotation : initMenuRotation;
        lastMenuScale = mainMenu != null ? mainMenu.transform.localScale : initMenuScale;

        ExhibitionGuideManager.EnsureInstance().StartVelocityGuide();
    }

    public void StartExhibitionSliceMode()
    {
        CancelExhibitionCompletionRoutine();
        currentExhibitionExperience = ExhibitionExperience.Slice;
        ResetVesselForExhibition();
        HideExhibitionSliceVisuals();
        exhibitionOverlayEnabled = true;

        if (mainMenu != null) mainMenu.SetActive(false);
        if (streamlineMenu != null) streamlineMenu.SetActive(false);
        if (streamlineSpeedMenu != null) streamlineSpeedMenu.SetActive(false);
        if (velocityMenu != null) velocityMenu.SetActive(false);
        if (wssMenu != null) wssMenu.SetActive(false);
        if (settingsMenu != null) settingsMenu.SetActive(false);
        if (folderSelectorMenu != null) folderSelectorMenu.SetActive(false);
        if (measurementSettingUI != null) measurementSettingUI.SetActive(false);
        if (playSettingMenu != null) playSettingMenu.SetActive(false);
        if (exhibitionMenu != null) exhibitionMenu.SetActive(true);
        if (velocityVisualizationSettingMenu != null) velocityVisualizationSettingMenu.SetActive(false);

        PrepareExhibitionSliceVisualization();
        ShowExhibitionSlice2D();
        ShowExhibitionSliceToggleMenu();
        ExhibitionGuideManager.EnsureInstance().StartSliceGuide();
    }

    public void StartExhibitionStreamlineMode()
    {
        CancelExhibitionCompletionRoutine();
        currentExhibitionExperience = ExhibitionExperience.Streamline;
        ResetVesselForExhibition();
        HideExhibitionSliceVisuals();
        exhibitionOverlayEnabled = true;

        if (mainMenu != null) mainMenu.SetActive(false);
        if (streamlineMenu != null) streamlineMenu.SetActive(false);
        if (streamlineSpeedMenu != null) streamlineSpeedMenu.SetActive(false);
        if (velocityMenu != null) velocityMenu.SetActive(false);
        if (wssMenu != null) wssMenu.SetActive(false);
        if (visualizationSettingMenu != null) visualizationSettingMenu.SetActive(false);
        if (velocityVisualizationSettingMenu != null) velocityVisualizationSettingMenu.SetActive(false);
        if (velocityGoBackMenu != null) velocityGoBackMenu.SetActive(false);
        if (settingsMenu != null) settingsMenu.SetActive(false);
        if (folderSelectorMenu != null) folderSelectorMenu.SetActive(false);
        if (measurementSettingUI != null) measurementSettingUI.SetActive(false);
        if (playSettingMenu != null) playSettingMenu.SetActive(false);

        if (exhibitionMenu != null)
        {
            exhibitionMenu.SetActive(true);
        }

        if (manager != null)
        {
            manager.visualizationMode = VisualizationMode.Streamline;
            manager.ApplyVisualizationMode();
        }

        if (sliderController != null)
        {
            sliderController.SetSliderActive(false);
        }

        HideExhibitionSliceToggleMenu();

        if (sliceVisualization != null)
        {
            sliceVisualization.DeactivateVisualization();
            if (sliceVisualization.visualsParent != null)
            {
                sliceVisualization.visualsParent.SetActive(false);
            }
        }

        if (playSettingMenu != null)
        {
            playSettingMenu.SetActive(false);
        }

        if (exhibitionMenu != null && exhibitionMenu.activeInHierarchy)
        {
            ExhibitionGuideManager.EnsureInstance().StartStreamlineGuide();
        }
    }

    public void StartExhibitionWssMode()
    {
        CancelExhibitionCompletionRoutine();
        currentExhibitionExperience = ExhibitionExperience.Wss;
        ResetVesselForExhibition();
        HideExhibitionSliceVisuals();
        exhibitionOverlayEnabled = true;

        if (mainMenu != null) mainMenu.SetActive(false);
        if (streamlineMenu != null) streamlineMenu.SetActive(false);
        if (streamlineSpeedMenu != null) streamlineSpeedMenu.SetActive(false);
        if (velocityMenu != null) velocityMenu.SetActive(false);
        if (settingsMenu != null) settingsMenu.SetActive(false);
        if (folderSelectorMenu != null) folderSelectorMenu.SetActive(false);
        if (measurementSettingUI != null) measurementSettingUI.SetActive(false);
        if (playSettingMenu != null) playSettingMenu.SetActive(false);
        if (exhibitionMenu != null) exhibitionMenu.SetActive(true);

        HideExhibitionSliceToggleMenu();
        HideExhibitionWssModeMenu();

        if (wssMenu != null) wssMenu.SetActive(false);
        if (manager != null)
        {
            manager.visualizationMode = VisualizationMode.WSS;
            manager.wssSubMode = WSSSubMode.WSSOnly;
            manager.ApplyVisualizationMode();
        }

        ExhibitionGuideManager.EnsureInstance().StartWssGuide();
    }

    public void ShowStreamlineMenu(bool networkCall = false)
    {
        AlignMenuToMain(streamlineMenu, networkCall);
        SetMenu(streamlineMenu, networkCall);
        if (manager != null) manager.visualizationMode = VisualizationMode.Streamline;
        if (sliderController != null) sliderController.SetSliderActive(false);
        if (sliceVisualization != null) sliceVisualization.DeactivateVisualization();
        sliceVisualization.visualsParent.SetActive(false);
        
        playSettingMenu.SetActive(false);
                
        // 슬라이더 비활성화
        if (sliderController != null)
        {
            sliderController.SetSliderActive(false);
        }
        
    }

    public void ShowVelocityMenu(bool networkCall = false)
    {
        AlignMenuToMain(velocityMenu, networkCall);
        SetMenu(velocityMenu, networkCall);
        if (manager != null) manager.visualizationMode = VisualizationMode.Velocity;
        if (sliderController != null) sliderController.SetSliderActive(false);
        
        sliceVisualization.indicatorController.SetVisible(false);
        if (sliceVisualization != null)
        {
            if (sliceVisualization != null && sliceVisualization.visualsParent != null)
            {
                sliceVisualization.visualsParent.SetActive(false);
            }
            if (sliceVisualization != null)
            {
                sliceVisualization.DeactivateVisualization();
            }
            
            Debug.Log("<color=green>SliceVisualization forced active in ShowVelocityMenu</color>");
        }
        
        playSettingMenu.SetActive(false);
                
        // 슬라이더 비활성화
        if (sliderController != null)
        {
            sliderController.SetSliderActive(false);
        }
        
    }

    public void ShowWssMenu(bool networkCall = false)
    {
        if (wssMenu == null) return;
        SetMenu(wssMenu, networkCall);
        sliceVisualization.visualsParent.SetActive(false);
        if (manager != null) manager.visualizationMode = VisualizationMode.WSS;
        if (sliderController != null) sliderController.SetSliderActive(false);
        if (sliceVisualization != null) sliceVisualization.DeactivateVisualization();     
        
        sliceVisualization.indicatorController.SetVisible(false);  
        
        playSettingMenu.SetActive(false);
                
        // 슬라이더 비활성화
        if (sliderController != null)
        {
            sliderController.SetSliderActive(false);
        }
    }

    // ----- Velocity submenu actions -----
    public void ShowVisualizationSettingMenu(bool networkCall = false)
    {
        GameObject targetMenu = visualizationSettingMenu ?? velocityMenu ?? mainMenu;
        AlignMenuToMain(targetMenu, networkCall);
        SetMenu(targetMenu, networkCall);
        if (manager != null) manager.visualizationMode = VisualizationMode.Velocity;
        if (sliderController != null) sliderController.SetSliderActive(false);
        
        if (sliceVisualization != null)
        {
            sliceVisualization.indicatorController.SetVisible(false);
            sliceVisualization.enabled = false;
            sliceVisualization.visualsParent.SetActive(false);
            sliceVisualization.viewRenderer.gameObject.SetActive(false);
            if (sliceVisualization.indicatorController != null)
            {
                sliceVisualization.indicatorController.showIndicator = false;
            }
            sliceVisualization.indicatorController.gameObject.SetActive(false);
        }
    }

    public void ToggleVelocityPlayback()
    {
        if (manager != null) manager.ToggleVelocityPlayback();
        
        if (sliceVisualization != null)
        {
            sliceVisualization.indicatorController.SetVisible(false);
            sliceVisualization.enabled = false;
            sliceVisualization.visualsParent.SetActive(false);
            sliceVisualization.viewRenderer.gameObject.SetActive(false);
        }
    }

    public void ShowVelocityPlaybackSetting(bool networkCall = false)
    {
        GameObject targetMenu = velocityGoBackMenu ?? visualizationSettingMenu ?? velocityMenu ?? mainMenu;
        AlignMenuToMain(targetMenu, networkCall);
        SetMenu(targetMenu, networkCall);
        sliceVisualization.indicatorController.SetVisible(false);
        //if (manager != null) manager.visualizationMode = VisualizationMode.Velocity;
        if (sliderController != null)
        {
            sliderController.SetMode(ControlMode.VelocityPlayback, SliceController.SliceAxis.None, syncFromTarget: true);
            sliderController.SetSliderActive(true);
        }
        
        // Hide ColorBar in playback setting
        if (manager != null && manager.velocityColorBar != null)
        {
            manager.velocityColorBar.Hide();
        }
         
        if (sliceVisualization != null && sliceVisualization.visualsParent != null)
        {
            sliceVisualization.visualsParent.SetActive(false);
            sliceVisualization.DeactivateVisualization();
            
            sliceVisualization.viewRenderer.gameObject.SetActive(false);
        }
    }

    public void ShowVelocitySliceIntervalSetting(bool networkCall = false)
    {
        GameObject targetMenu = velocityGoBackMenu ?? visualizationSettingMenu ?? velocityMenu ?? mainMenu;
        AlignMenuToMain(targetMenu, networkCall);
        SetMenu(targetMenu, networkCall);
        sliceVisualization.indicatorController.SetVisible(false);
        //if (manager != null) manager.visualizationMode = VisualizationMode.Velocity;
        if (sliderController != null)
        {
            sliderController.SetMode(ControlMode.DensityY, SliceController.SliceAxis.None, syncFromTarget: true);
            sliderController.SetSliderActive(true);
        }
        if (sliceVisualization != null && sliceVisualization.visualsParent != null)
        {
            sliceVisualization.visualsParent.SetActive(false);
            sliceVisualization.DeactivateVisualization();
            
            sliceVisualization.viewRenderer.gameObject.SetActive(false);
        } 
    }

    public void ShowExhibitionVelocitySpeedSlider()
    {
        if (sliderController == null)
        {
            return;
        }

        sliderController.EnsureInitialized();
        ConfigureExhibitionSliderPlacement();
        sliderController.SetMode(ControlMode.VelocityPlayback, SliceController.SliceAxis.None, syncFromTarget: true);
        sliderController.SetSliderActive(true);
    }

    public void ShowExhibitionVelocitySpacingSlider()
    {
        if (sliderController == null)
        {
            return;
        }

        sliderController.EnsureInitialized();
        ConfigureExhibitionSliderPlacement();
        sliderController.SetMode(ControlMode.DensityY, SliceController.SliceAxis.None, syncFromTarget: true);
        sliderController.SetSliderActive(true);
    }

    public void ShowExhibitionStreamlineSpeedSlider()
    {
        if (sliderController == null)
        {
            return;
        }

        sliderController.EnsureInitialized();
        ConfigureExhibitionSliderPlacement();
        sliderController.SetMode(ControlMode.StreamlinePlayback, SliceController.SliceAxis.None, syncFromTarget: true);
        sliderController.SetSliderActive(true);
    }

    public void PrepareExhibitionSliceVisualization()
    {
        if (sliceVisualization != null)
        {
            sliceVisualization.gameObject.SetActive(true);
        }

        if (manager != null)
        {
            manager.visualizationMode = VisualizationMode.Velocity;
            manager.ApplyVisualizationMode();
        }

        if (sliderController != null)
        {
            sliderController.SetSliderActive(false);
        }

        if (sliceVisualization == null)
        {
            return;
        }

        sliceVisualization.enabled = true;
        sliceVisualization.currentAxis = SliceController.SliceAxis.X_Axis;
        sliceVisualization.show2DHeatmap = true;
        sliceVisualization.show3DArrows = false;
        sliceVisualization.showSliceIndicator = true;

        if (sliceVisualization.indicatorController != null)
        {
            if (!exhibitionSliceKnifeDefaultsCaptured)
            {
                exhibitionSliceKnifeGrabSizeDefault = sliceVisualization.indicatorController.knifeGrabSize;
                exhibitionSliceKnifeGrabDepthDefault = sliceVisualization.indicatorController.knifeGrabDepth;
                exhibitionSliceKnifeDefaultsCaptured = true;
            }

            sliceVisualization.indicatorController.knifeGrabDepth = 0.15f;
            sliceVisualization.indicatorController.gameObject.SetActive(true);
            sliceVisualization.indicatorController.sliceControllerRef = sliceVisualization;
            sliceVisualization.indicatorController.enableKnifeMode = true;
            sliceVisualization.indicatorController.enabled = true;
            sliceVisualization.indicatorController.SetVisible(true);

            BoxCollider exhibitionKnifeCollider = sliceVisualization.indicatorController.GetComponent<BoxCollider>();
            if (exhibitionKnifeCollider == null)
            {
                exhibitionKnifeCollider = sliceVisualization.indicatorController.gameObject.AddComponent<BoxCollider>();
            }
            exhibitionKnifeCollider.size = Vector3.one;
            exhibitionKnifeCollider.center = Vector3.zero;

        }

        if (sliceVisualization.visualsParent != null)
        {
            sliceVisualization.visualsParent.SetActive(true);
        }

        if (sliceVisualization.viewRenderer != null)
        {
            sliceVisualization.viewRenderer.gameObject.SetActive(true);
            if (sliceVisualization.viewRenderer.heatmapParent != null)
            {
                sliceVisualization.viewRenderer.heatmapParent.SetActive(sliceVisualization.show2DHeatmap);
            }
            if (sliceVisualization.viewRenderer.arrowSliceParent != null)
            {
                sliceVisualization.viewRenderer.arrowSliceParent.SetActive(sliceVisualization.show3DArrows);
            }
        }

        sliceVisualization.customAnchorTransform = vesselRoot != null ? vesselRoot : exhibitionMenu != null ? exhibitionMenu.transform : null;

        if (sliceVisualization.indicatorController != null && vesselRoot != null)
        {
            sliceVisualization.indicatorController.parentTransform = vesselRoot;
        }

        PositionExhibitionSliceVisuals();
        sliceVisualization.UpdateAllComponents(false);
        RestoreExhibitionSliceTransform();
        RefreshExhibitionSliceColliders();
    }

    public void ShowExhibitionSliceToggleMenu()
    {
        if (velocityVisualizationSettingMenu == null)
        {
            return;
        }

        ConfigureExhibitionSliceToggleMenu(true);
        PositionUIToRightOfCurrentMenu(velocityVisualizationSettingMenu, 0.22f, false);
        velocityVisualizationSettingMenu.SetActive(true);
    }

    public void HideExhibitionSliceToggleMenu()
    {
        ConfigureExhibitionSliceToggleMenu(false);
        if (velocityVisualizationSettingMenu != null)
        {
            velocityVisualizationSettingMenu.SetActive(false);
        }
    }

    public void ShowExhibitionSlice2D()
    {
        PrepareExhibitionSliceVisualization();
        ShowVelocityHeatmap();
        PositionExhibitionSliceVisuals();
        CaptureExhibitionSliceTransformIfNeeded();
        RestoreExhibitionSliceTransform();
        RefreshExhibitionSliceColliders();
        if (exhibitionMenu != null) exhibitionMenu.SetActive(true);
        if (velocityVisualizationSettingMenu != null) velocityVisualizationSettingMenu.SetActive(true);
    }

    public void ShowExhibitionSlice3D()
    {
        PrepareExhibitionSliceVisualization();
        ShowVelocitySliceArrows();
        PositionExhibitionSliceVisuals();
        CaptureExhibitionSliceTransformIfNeeded();
        RestoreExhibitionSliceTransform();
        RefreshExhibitionSliceColliders();
        if (exhibitionMenu != null) exhibitionMenu.SetActive(true);
        if (velocityVisualizationSettingMenu != null) velocityVisualizationSettingMenu.SetActive(true);
    }

    public void ShowExhibitionWssSpeedSlider()
    {
        if (sliderController == null)
        {
            return;
        }

        sliderController.EnsureInitialized();
        ConfigureExhibitionSliderPlacement();
        sliderController.SetMode(ControlMode.WssPlayback, SliceController.SliceAxis.None, syncFromTarget: true);
        sliderController.SetSliderActive(true);
    }

    public float GetWssPlaybackNormalized()
    {
        return manager != null ? Mathf.InverseLerp(0.05f, 0.5f, manager.wssPlaybackSpeed) : 0f;
    }

    public int GetCurrentWssSubModeIndex()
    {
        return manager != null ? (int)manager.wssSubMode : 0;
    }

    public void ShowExhibitionWssModeMenu()
    {
        return;
    }

    public void HideExhibitionWssModeMenu()
    {
        RestoreVelocityVisualizationMenuDefaults();

        if (velocityVisualizationSettingMenu != null)
        {
            velocityVisualizationSettingMenu.SetActive(false);
        }
    }

    public void SetExhibitionWssModeWssOnly()
    {
        if (manager == null)
        {
            return;
        }

        manager.wssSubMode = WSSSubMode.WSSOnly;
        manager.ApplyWSSSubMode();
        if (exhibitionMenu != null) exhibitionMenu.SetActive(true);
        if (velocityVisualizationSettingMenu != null) velocityVisualizationSettingMenu.SetActive(true);
    }

    public void SetExhibitionWssModeBloodVesselVector()
    {
        if (manager == null)
        {
            return;
        }

        manager.wssSubMode = WSSSubMode.BloodVessel_Vector;
        manager.ApplyWSSSubMode();
        if (exhibitionMenu != null) exhibitionMenu.SetActive(true);
        if (velocityVisualizationSettingMenu != null) velocityVisualizationSettingMenu.SetActive(true);
    }

    public void SetExhibitionWssModeWssVector()
    {
        if (manager == null)
        {
            return;
        }

        manager.wssSubMode = WSSSubMode.WSS_Vector;
        manager.ApplyWSSSubMode();
        if (exhibitionMenu != null) exhibitionMenu.SetActive(true);
        if (velocityVisualizationSettingMenu != null) velocityVisualizationSettingMenu.SetActive(true);
    }

    public float GetSlicePositionNormalized()
    {
        if (sliceVisualization == null)
        {
            return 0.5f;
        }

        return Mathf.Clamp01(sliceVisualization.slicePosition);
    }

    public bool IsSlicePositionInsideRange(float min, float max)
    {
        float value = GetSlicePositionNormalized();
        return value >= min && value <= max;
    }

    public float GetStreamlinePlaybackNormalized()
    {
        if (manager == null)
        {
            manager = Manager.Instance ?? FindObjectOfType<Manager>();
        }

        return manager != null ? Mathf.InverseLerp(0.05f, 0.5f, manager.streamlinePlaybackSpeed) : 0f;
    }

    private void ConfigureExhibitionSliderPlacement()
    {
        if (sliderController == null)
        {
            return;
        }

        Transform sliderAnchor = vesselRoot != null ? vesselRoot : exhibitionMenu != null ? exhibitionMenu.transform : null;
        Vector3 sliderOffset = vesselRoot != null
            ? new Vector3(-0.22f, 0.00f, -0.05f)
            : new Vector3(0f, -0.12f, -0.04f);

        if (sliderAnchor != null)
        {
            sliderController.ConfigureExhibitionPlacement(sliderAnchor, sliderOffset);
        }
    }

    private void ConfigureExhibitionSliceToggleMenu(bool exhibitionMode)
    {
        if (velocityVisualizationSettingMenu == null)
        {
            return;
        }

        Transform buttonCollection = velocityVisualizationSettingMenu.transform.Find("ButtonCollection");
        if (buttonCollection == null)
        {
            return;
        }

        GameObject arrowsButton = buttonCollection.Find("단면 속도장")?.gameObject;
        GameObject heatmapButton = buttonCollection.Find("Heatmap")?.gameObject;
        GameObject axisButton = buttonCollection.Find("축 변경")?.gameObject;
        GameObject backButton = buttonCollection.Find("이전으로 돌아가기")?.gameObject;

        if (arrowsButton != null) arrowsButton.SetActive(true);
        if (heatmapButton != null) heatmapButton.SetActive(true);
        if (axisButton != null) axisButton.SetActive(!exhibitionMode);
        if (backButton != null) backButton.SetActive(!exhibitionMode);

        ApplyButtonLabel(arrowsButton, exhibitionMode ? "3D 속도장" : "단면 속도장");
        ApplyButtonLabel(heatmapButton, exhibitionMode ? "2D 속도장" : "Heatmap");
    }

    private void ConfigureExhibitionWssModeMenu(bool exhibitionMode)
    {
        if (velocityVisualizationSettingMenu == null)
        {
            return;
        }

        Transform buttonCollection = velocityVisualizationSettingMenu.transform.Find("ButtonCollection");
        if (buttonCollection == null)
        {
            return;
        }

        GameObject buttonA = buttonCollection.Find("단면 속도장")?.gameObject;
        GameObject buttonB = buttonCollection.Find("Heatmap")?.gameObject;
        GameObject buttonC = buttonCollection.Find("축 변경")?.gameObject;
        GameObject backButton = buttonCollection.Find("이전으로 돌아가기")?.gameObject;

        if (!exhibitionMode)
        {
            RestoreVelocityVisualizationMenuDefaults();
            return;
        }

        if (buttonA != null) buttonA.SetActive(true);
        if (buttonB != null) buttonB.SetActive(true);
        if (buttonC != null) buttonC.SetActive(true);
        if (backButton != null) backButton.SetActive(false);

        ApplyButtonLabel(buttonA, "WSS");
        ApplyButtonLabel(buttonB, "혈관+벡터");
        ApplyButtonLabel(buttonC, "WSS+벡터");

        WireAuxiliaryButton(buttonA, SetExhibitionWssModeWssOnly);
        WireAuxiliaryButton(buttonB, SetExhibitionWssModeBloodVesselVector);
        WireAuxiliaryButton(buttonC, SetExhibitionWssModeWssVector);
    }

    private void RestoreVelocityVisualizationMenuDefaults()
    {
        if (velocityVisualizationSettingMenu == null)
        {
            return;
        }

        Transform buttonCollection = velocityVisualizationSettingMenu.transform.Find("ButtonCollection");
        if (buttonCollection == null)
        {
            return;
        }

        GameObject arrowsButton = buttonCollection.Find("단면 속도장")?.gameObject;
        GameObject heatmapButton = buttonCollection.Find("Heatmap")?.gameObject;
        GameObject axisButton = buttonCollection.Find("축 변경")?.gameObject;
        GameObject backButton = buttonCollection.Find("이전으로 돌아가기")?.gameObject;

        if (arrowsButton != null) arrowsButton.SetActive(true);
        if (heatmapButton != null) heatmapButton.SetActive(true);
        if (axisButton != null) axisButton.SetActive(true);
        if (backButton != null) backButton.SetActive(true);

        ApplyButtonLabel(arrowsButton, "단면 속도장");
        ApplyButtonLabel(heatmapButton, "Heatmap");
        ApplyButtonLabel(axisButton, "축 변경");
        ApplyButtonLabel(backButton, "이전으로 돌아가기");

        WireAuxiliaryButton(arrowsButton, () => ShowVelocitySliceArrows(false));
        WireAuxiliaryButton(heatmapButton, () => ShowVelocityHeatmap(false));
        WireAuxiliaryButton(axisButton, () => ToggleSliceAxis());
        WireAuxiliaryButton(backButton, () => ShowVisualizationSettingMenu(false));
    }

    private void PositionExhibitionSliceVisuals()
    {
        if (sliceVisualization == null || exhibitionMenu == null)
        {
            return;
        }

        if (exhibitionMenu.activeInHierarchy)
        {
            if (vesselRoot != null)
            {
                PositionSliceRightOfVessel(vesselRoot, false);
            }
            else
            {
                PositionSliceLeftOfMenu(exhibitionMenu.transform, false);
            }
            sliceVisualization.customAnchorTransform = exhibitionMenu.transform;
            return;
        }

        if (sliceVisualization.visualsParent == null || vesselRoot == null)
        {
            return;
        }

        Camera cam = Camera.main;
        Transform visualsTransform = sliceVisualization.visualsParent.transform;

        if (cam != null)
        {
            visualsTransform.position =
                vesselRoot.position +
                (cam.transform.right * 0.22f) +
                (cam.transform.up * 0.04f) +
                (cam.transform.forward * 0.02f);

            visualsTransform.rotation = Quaternion.LookRotation(visualsTransform.position - cam.transform.position, Vector3.up);
        }
        else
        {
            visualsTransform.position = vesselRoot.position + Vector3.right * 0.22f + Vector3.up * 0.04f;
        }
    }

    private void RefreshExhibitionSliceColliders()
    {
        if (sliceVisualization == null)
        {
            return;
        }

        if (sliceVisualization.indicatorController != null)
        {
            BoxCollider exhibitionKnifeCollider = sliceVisualization.indicatorController.GetComponent<BoxCollider>();
            if (exhibitionKnifeCollider == null)
            {
                exhibitionKnifeCollider = sliceVisualization.indicatorController.gameObject.AddComponent<BoxCollider>();
            }

            exhibitionKnifeCollider.size = Vector3.one;
            exhibitionKnifeCollider.center = Vector3.zero;

            BoundingBox knifeBoundingBox = sliceVisualization.indicatorController.GetComponent<BoundingBox>();
            if (knifeBoundingBox != null)
            {
                knifeBoundingBox.BoundsOverride = exhibitionKnifeCollider;
                knifeBoundingBox.CreateRig();
            }
        }

        BoxCollider sliceRootCollider = sliceVisualization.GetComponent<BoxCollider>();
        if (sliceRootCollider != null)
        {
            BoundingBox sliceRootBoundingBox = sliceVisualization.GetComponent<BoundingBox>();
            if (sliceRootBoundingBox != null)
            {
                sliceRootBoundingBox.BoundsOverride = sliceRootCollider;
                sliceRootBoundingBox.CreateRig();
            }
        }
    }

    private void CaptureExhibitionSliceTransformIfNeeded()
    {
        if (sliceVisualization == null)
        {
            return;
        }

        if (!exhibitionSliceTransformCaptured)
        {
            exhibitionSliceInitPos = sliceVisualization.transform.position;
            exhibitionSliceInitRot = sliceVisualization.transform.rotation;
            exhibitionSliceInitScale = sliceVisualization.transform.localScale;
            exhibitionSliceTransformCaptured = true;
        }

        if (!exhibitionSliceIndicatorTransformCaptured && sliceVisualization.indicatorController != null)
        {
            Transform indicatorTransform = sliceVisualization.indicatorController.transform;
            exhibitionSliceIndicatorInitPos = indicatorTransform.position;
            exhibitionSliceIndicatorInitRot = indicatorTransform.rotation;
            exhibitionSliceIndicatorInitScale = indicatorTransform.localScale;
            exhibitionSliceIndicatorTransformCaptured = true;
        }
    }

    private void RestoreExhibitionSliceTransform()
    {
        if (sliceVisualization == null)
        {
            return;
        }

        if (exhibitionSliceTransformCaptured)
        {
            sliceVisualization.transform.position = exhibitionSliceInitPos;
            sliceVisualization.transform.rotation = exhibitionSliceInitRot;
            sliceVisualization.transform.localScale = exhibitionSliceInitScale;
        }

        if (exhibitionSliceIndicatorTransformCaptured && sliceVisualization.indicatorController != null)
        {
            Transform indicatorTransform = sliceVisualization.indicatorController.transform;
            indicatorTransform.position = exhibitionSliceIndicatorInitPos;
            indicatorTransform.rotation = exhibitionSliceIndicatorInitRot;
            indicatorTransform.localScale = exhibitionSliceIndicatorInitScale;
        }
    }

    private void ApplyButtonLabel(GameObject buttonObject, string label)
    {
        if (buttonObject == null || string.IsNullOrEmpty(label))
        {
            return;
        }

        TMP_Text[] texts = buttonObject.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
        {
            if (text != null)
            {
                text.text = label;
            }
        }
    }

    private void WireAuxiliaryButton(GameObject buttonObject, UnityAction action)
    {
        if (buttonObject == null)
        {
            return;
        }

        Interactable interactable = buttonObject.GetComponent<Interactable>();
        if (interactable == null)
        {
            return;
        }

        interactable.OnClick.RemoveAllListeners();
        if (action != null)
        {
            interactable.OnClick.AddListener(action);
        }
    }

    public void HideExhibitionSlider()
    {
        if (sliderController != null)
        {
            sliderController.EnsureInitialized();
            sliderController.SetSliderActive(false);
        }
    }

    public float GetVelocityPlaybackNormalized()
    {
        if (manager == null)
        {
            manager = Manager.Instance ?? FindObjectOfType<Manager>();
        }

        return manager != null ? Mathf.InverseLerp(0.05f, 0.5f, manager.velocityPlaybackSpeed) : 0f;
    }

    public float GetVelocitySpacingNormalized()
    {
        if (manager == null)
        {
            manager = Manager.Instance ?? FindObjectOfType<Manager>();
        }

        VelocityLoader loader = manager != null ? manager.velocityLoader : null;
        return loader != null ? Mathf.InverseLerp(1f, 10f, loader.displayStepY) : 0f;
    }

    public bool IsExhibitionInteractionInProgress()
    {
        bool sliderDragging = sliderController != null && sliderController.IsDragging;
        bool objectManipulating = manager != null && manager.isGlobalLocked;
        return sliderDragging || objectManipulating;
    }

    public void ShowVelocityVisualizationSettingMenu(bool networkCall = false)
    {
        GameObject targetMenu = velocityVisualizationSettingMenu ?? visualizationSettingMenu ?? velocityMenu ?? mainMenu;
        AlignMenuToMain(targetMenu, networkCall);
        SetMenu(targetMenu, networkCall);
        //if (manager != null) manager.visualizationMode = VisualizationMode.Velocity;
        
        
        sliceVisualization.enabled = true;
        sliceVisualization.indicatorController.enabled = true; 
                
        // Enable SliceVisualization component with proper position
        if (sliceVisualization != null)
        {
            sliceVisualization.enabled = true;
            
            // Ensure Knife Mode is valid
            if (sliceVisualization.indicatorController != null)
            {
                // CRITICAL: Set SliceController reference
                sliceVisualization.indicatorController.sliceControllerRef = sliceVisualization;
                
                sliceVisualization.indicatorController.enableKnifeMode = true;
                sliceVisualization.indicatorController.enabled = true;
                sliceVisualization.indicatorController.SetVisible(true);
                
                // Debug: Confirm activation
                Debug.Log($"<color=magenta>[Menu] IndicatorController enabled: {sliceVisualization.indicatorController.enabled}, " +
                          $"GameObject active: {sliceVisualization.indicatorController.gameObject.activeSelf}, " +
                          $"enableKnifeMode: {sliceVisualization.indicatorController.enableKnifeMode}</color>");
            }
            
            // CRITICAL: Enable heatmap visualization
            sliceVisualization.show2DHeatmap = true;
            sliceVisualization.show3DArrows = false; // Optional: enable if you want arrows too
            
            //if (sliceVisualization.currentAxis == SliceVisualization.SliceAxis.None)
            //    sliceVisualization.currentAxis = SliceVisualization.SliceAxis.X_Axis;
            
            // KNIFE MODE: Position next to menu so visuals are visible to user
            if (velocityVisualizationSettingMenu != null && !networkCall && !exhibitionOverlayEnabled)
            {
                PositionSliceNextToMenu(velocityVisualizationSettingMenu.transform, networkCall);
                sliceVisualization.customAnchorTransform = velocityVisualizationSettingMenu.transform;
            }
            else if (exhibitionOverlayEnabled)
            {
                sliceVisualization.customAnchorTransform = vesselRoot != null ? vesselRoot : sliceVisualization.customAnchorTransform;
                if (sliceVisualization.indicatorController != null && vesselRoot != null)
                {
                    sliceVisualization.indicatorController.parentTransform = vesselRoot;
                }
            }
            
            // Ensure parent object is active if managed
            if (sliceVisualization.visualsParent != null)
            {
                sliceVisualization.visualsParent.SetActive(true);
            }
            
            // Activate
            if (sliceVisualization.viewRenderer != null)
            {
                 sliceVisualization.viewRenderer.gameObject.SetActive(true);
            }
            
            sliceVisualization.UpdateAllComponents(networkCall);
            Debug.Log("<color=green>SliceController activated (Knife Mode) and positioned next to menu</color>");
        }
    }

    /// <summary>
    /// 축 변경: X축 <-> Y축 토글
    /// </summary>
    public void ToggleSliceAxis()
    {
        ToggleSliceAxisInternal(true, false);
    }

    void ToggleSliceAxisInternal(bool broadcast, bool networkCall = false)
    {
        if (sliceVisualization == null)
        {
            Debug.LogWarning("<color=yellow>SliceVisualization is null, cannot toggle axis</color>");
            return;
        }

        sliceVisualization.enabled = true;
        if (sliceVisualization.visualsParent != null) sliceVisualization.visualsParent.SetActive(true);

        // Toggle between X_Axis and Y_Axis
        if (sliceVisualization.currentAxis == SliceController.SliceAxis.X_Axis)
        {
            sliceVisualization.currentAxis = SliceController.SliceAxis.Y_Axis;
            Debug.Log("<color=cyan>Slice axis changed to Y_Axis</color>");
            
            // Update slider to sync with new axis
            if (sliderController != null)
            {
                sliderController.SetMode(ControlMode.SlicePosition, SliceController.SliceAxis.Y_Axis, syncFromTarget: true);
            }
        } 
        else
        {
            sliceVisualization.currentAxis = SliceController.SliceAxis.X_Axis;
            Debug.Log("<color=cyan>Slice axis changed to X_Axis</color>");
            
            // Update slider to sync with new axis
            if (sliderController != null)
            {
                sliderController.SetMode(ControlMode.SlicePosition, SliceController.SliceAxis.X_Axis, syncFromTarget: true);
            }
        }
        
        // Position next to menu at center-right when toggling axis
        if (velocityVisualizationSettingMenu != null && !networkCall)
        {
            PositionSliceNextToMenu(velocityVisualizationSettingMenu.transform, networkCall);
        }
        
        // Force update visualization immediately
        if (sliceVisualization.enabled && sliceVisualization.velocityLoader != null)
        {
            sliceVisualization.SetSliceIndicatorVisible(true);
            sliceVisualization.UpdateAllComponents(networkCall);
            
            Debug.Log($"<color=cyan>Slice axis now {sliceVisualization.currentAxis}, position {sliceVisualization.slicePosition}</color>");
        }

        if (broadcast && networkSync && PhotonSyncService.Instance != null && !networkCall)
        {
            PhotonSyncService.Instance.BroadcastButtonAction(ButtonAction.ToggleSliceAxis);
        }
    }

    /// <summary>
    /// WSS Vector 화살표 토글
    /// </summary>
    public void ToggleWSSVectors()
    {
        if (manager != null && manager.wssLoader != null)
        {
            bool newState = !manager.wssLoader.showWSSVectors;
            manager.SetWSSVectorVisibility(newState);
            Debug.Log($"<color=cyan>WSS Vectors toggled: {(newState ? "ON" : "OFF")}</color>");
        }
        else
        {
            Debug.LogWarning("<color=yellow>Manager or WSSLoader is null, cannot toggle WSS vectors</color>");
        }
    }

    public void ShowVelocitySliceArrows(bool networkCall = false)
    {
        if (sliceVisualization != null)
        {
            
            sliceVisualization.show2DHeatmap = false;
            sliceVisualization.show3DArrows = true;
            sliceVisualization.enabled = true;
            if (sliceVisualization.viewRenderer != null)
            {
                sliceVisualization.viewRenderer.gameObject.SetActive(true);
                if (sliceVisualization.viewRenderer.heatmapParent != null)
                {
                    sliceVisualization.viewRenderer.heatmapParent.SetActive(false);
                }
                if (sliceVisualization.viewRenderer.arrowSliceParent != null)
                {
                    sliceVisualization.viewRenderer.arrowSliceParent.SetActive(true);
                }
            }
            sliceVisualization.UpdateAllComponents(networkCall);
            
            if (velocityVisualizationSettingMenu != null && !networkCall && !exhibitionOverlayEnabled)
            {
                PositionSliceNextToMenu(velocityVisualizationSettingMenu.transform, networkCall);
            }
            else if (exhibitionOverlayEnabled)
            {
                if (exhibitionMenu != null)
                {
                    if (vesselRoot != null)
                    {
                        PositionSliceRightOfVessel(vesselRoot, networkCall);
                    }
                    else
                    {
                        PositionSliceLeftOfMenu(exhibitionMenu.transform, networkCall);
                    }
                    sliceVisualization.customAnchorTransform = exhibitionMenu.transform;
                }
                else
                {
                    sliceVisualization.customAnchorTransform = vesselRoot != null ? vesselRoot : sliceVisualization.customAnchorTransform;
                }

                if (sliceVisualization.indicatorController != null)
                {
                    sliceVisualization.indicatorController.parentTransform = vesselRoot != null ? vesselRoot : exhibitionMenu != null ? exhibitionMenu.transform : sliceVisualization.indicatorController.parentTransform;
                }
            }
        }
    }

    public void ToggleVelocity2D3D(bool networkCall = false)
    {
        if (sliceVisualization != null)
        {
            if (sliceVisualization.show2DHeatmap)
            {
                ShowVelocitySliceArrows(networkCall);
            }
            else
            {
                ShowVelocityHeatmap(networkCall);
            }
        }
    }

    public void ShowVelocityHeatmap(bool networkCall = false)
    {
        if (velocityVisualizationSettingMenu != null && !networkCall && !exhibitionOverlayEnabled)
        {
            PositionSliceNextToMenu(velocityVisualizationSettingMenu.transform, networkCall);
        }
        if (sliceVisualization != null)
        {
            sliceVisualization.show2DHeatmap = true;
            sliceVisualization.show3DArrows = false;
            sliceVisualization.enabled = true;
            if (sliceVisualization.viewRenderer != null)
            {
                sliceVisualization.viewRenderer.gameObject.SetActive(true);
                if (sliceVisualization.viewRenderer.heatmapParent != null)
                {
                    sliceVisualization.viewRenderer.heatmapParent.SetActive(true);
                }
                if (sliceVisualization.viewRenderer.arrowSliceParent != null)
                {
                    sliceVisualization.viewRenderer.arrowSliceParent.SetActive(false);
                }
            }
            sliceVisualization.UpdateAllComponents(networkCall);

            if (exhibitionOverlayEnabled)
            {
                if (exhibitionMenu != null)
                {
                    if (vesselRoot != null)
                    {
                        PositionSliceRightOfVessel(vesselRoot, networkCall);
                    }
                    else
                    {
                        PositionSliceLeftOfMenu(exhibitionMenu.transform, networkCall);
                    }
                    sliceVisualization.customAnchorTransform = exhibitionMenu.transform;
                }
                else
                {
                    sliceVisualization.customAnchorTransform = vesselRoot != null ? vesselRoot : sliceVisualization.customAnchorTransform;
                }

                if (sliceVisualization.indicatorController != null)
                {
                    sliceVisualization.indicatorController.parentTransform = vesselRoot != null ? vesselRoot : exhibitionMenu != null ? exhibitionMenu.transform : sliceVisualization.indicatorController.parentTransform;
                }
            }
        }
    }

    public void ReturnToVisualizationSetting(bool networkCall = false)
    {
        GameObject targetMenu = visualizationSettingMenu ?? velocityMenu ?? mainMenu;
        AlignMenuToMain(targetMenu, networkCall);
        SetMenu(targetMenu, networkCall);
        if (manager != null) manager.visualizationMode = VisualizationMode.Velocity;

        if (sliderController != null) sliderController.SetSliderActive(false);

        if (sliceVisualization != null)
        {
            sliceVisualization.enabled = false;
            sliceVisualization.visualsParent.SetActive(false);
            sliceVisualization.viewRenderer.gameObject.SetActive(false);
        }
    }

    public void ReturnHomeFromVelocityVisualization(bool networkCall = false)
    {
        if (sliderController != null) sliderController.SetSliderActive(false);
        
        if (sliceVisualization != null && sliceVisualization.visualsParent != null)
        {
            sliceVisualization.visualsParent.SetActive(false);
            sliceVisualization.DeactivateVisualization();
        }
        ShowMainMenu(networkCall);
    }

    // ----- Streamline submenu actions -----
    public void ShowStreamlineSpeedSetting(bool networkCall = false)
    {
        Debug.Log("<color=cyan>ShowStreamlineSpeedSetting called</color>");
        
        // Switch to Streamline Speed Menu
        if (streamlineSpeedMenu != null)
        {
            Debug.Log($"<color=green>Streamline Speed Menu found: {streamlineSpeedMenu.name}</color>");
            AlignMenuToMain(streamlineSpeedMenu, networkCall);
            SetMenu(streamlineSpeedMenu, networkCall);
            Debug.Log($"<color=green>Streamline Speed Menu active: {streamlineSpeedMenu.activeSelf}</color>");
        }
        else
        {
            Debug.LogWarning("<color=red>Streamline Speed Menu is NULL! Check Inspector assignment.</color>");
        }
        
        if (sliderController != null)
        {
            sliderController.EnsureInitialized();
            if (exhibitionOverlayEnabled)
            {
                ConfigureExhibitionSliderPlacement();
            }
            sliderController.SetMode(ControlMode.StreamlinePlayback, SliceController.SliceAxis.None, syncFromTarget: true);
            sliderController.SetSliderActive(true);
            Debug.Log("<color=green>Slider activated for Streamline playback</color>");
        }
        sliceVisualization.visualsParent.SetActive(false);
    }

    public void ToggleStreamlinePlayback()
    {
        if (manager != null)
        {
            manager.ToggleStreamlinePlayback();
            
            // Show speed menu after starting playback
            if (manager.streamlineLoader != null && manager.streamlineLoader.isAnimating)
            {
                ShowStreamlineSpeedSetting(false);
            }
            sliceVisualization.visualsParent.SetActive(false);
        }
    }

    public void ShowWssPlaybackSetting(bool networkCall = false)
    {
        if (sliderController != null)
        {
            sliderController.SetMode(ControlMode.WssPlayback, SliceController.SliceAxis.None, syncFromTarget: true);
            sliderController.SetSliderActive(true);
            sliceVisualization.visualsParent.SetActive(false);
        }
        
        if (!exhibitionOverlayEnabled && manager != null && manager.visualizationMode == VisualizationMode.WSS)
        {
            manager.ApplyVisualizationMode(networkCall);
        }
    }

    public void ToggleWSSPlayback()
    {
        if (manager != null)
        {
            manager.ToggleWSSPlayback();
            
            sliceVisualization.visualsParent.SetActive(false);
            // Show speed menu after starting playback
            if (manager.wssLoader != null && manager.wssLoader.isAnimating)
            {
                ShowWssPlaybackSetting(false);
            }
            sliceVisualization.visualsParent.SetActive(false);
        }
    }

    public void ReturnHomeFromStreamline(bool networkCall = false)
    {
        ShowMainMenu(networkCall);
        if (sliderController != null)
        {
            sliceVisualization.visualsParent.SetActive(false);
            sliderController.SetSliderActive(false);
        }
        
    }

    public void ResetVesselTransform()
    {
        ResetVesselTransform(resetVisualization: false);
    }

    private void ResetVesselTransformAndVisualization()
    {
        ResetVesselTransform(resetVisualization: true);
    }

    private void ResetVesselTransform(bool resetVisualization)
    {
        hasLastMenuTransform = false;
        Manager.Instance.RecenterCamera();

        if (vesselRoot == null) return;
        vesselRoot.localPosition = initPos;
        vesselRoot.localRotation = initRot;
        vesselRoot.localScale = initScale;

        if (mainMenuRoot != null)
        {
            mainMenuRoot.localPosition = menuInitPos;
            mainMenuRoot.localRotation = menuInitRot;
            mainMenuRoot.localScale = menuInitScale;
        }
        
        // JSON 설정 파일 다시 로드하여 모든 값 재적용
        if (Manager.Instance != null)
        {
            Manager.Instance.LoadAndApplySettings();
            Debug.Log("<color=green>[ButtonController] Visualization settings reloaded from JSON</color>");
        }
        
        // VesselMeasurementTool 캘리브레이션도 다시 로드
        if (vesselMeasurementTool != null)
        {
            vesselMeasurementTool.ReloadCalibration();
        }

        if (resetVisualization && sliderController != null)
        {
            sliderController.SetSliderActive(false);
        }

        if (resetVisualization && sliceVisualization != null)
        {
            sliceVisualization.DeactivateVisualization();
            if (sliceVisualization.visualsParent != null)
            {
                sliceVisualization.visualsParent.SetActive(false);
            }

            if (sliceVisualization.viewRenderer != null)
            {
                sliceVisualization.viewRenderer.gameObject.SetActive(false);
            }
        }

        if (resetVisualization && manager != null)
        {
            manager.visualizationMode = VisualizationMode.Mesh;
            manager.ApplyVisualizationMode();

            if (manager.velocityColorBar != null)
            {
                manager.velocityColorBar.Hide();
            }

            if (manager.wssColorBar != null)
            {
                manager.wssColorBar.Hide();
            }
        }

        // Store current active menu transform before switching
        GameObject currentActive = GetCurrentActiveMenu();
        if (currentActive != null)
        {
            lastMenuPosition = initMenuPosition;
            lastMenuRotation = initMenuRotation;
            lastMenuScale = initMenuScale;
            hasLastMenuTransform = true;
            currentActive.transform.position = lastMenuPosition;
            currentActive.transform.rotation = lastMenuRotation;
            currentActive.transform.localScale = lastMenuScale;   
        }

        if (sliceVisualization != null)
        {
            if(sliceVisualization.enabled){
                if (currentExhibitionExperience == ExhibitionExperience.Slice && exhibitionOverlayEnabled && exhibitionMenu != null)
                {
                    sliceVisualization.customAnchorTransform = exhibitionMenu.transform;
                }
                else if (velocityVisualizationSettingMenu != null)
                {
                    sliceVisualization.customAnchorTransform = velocityVisualizationSettingMenu.transform;
                }
            }
        }

        ResetExhibitionOverlay(exhibitionOverlayEnabled || (exhibitionMenu != null && exhibitionMenu.activeSelf));
        RestoreExhibitionSliceLayoutIfNeeded();
    }

    private void ResetVesselForExhibition()
    {
        if (vesselRoot == null) return;

        vesselRoot.localPosition = initPos;
        vesselRoot.localRotation = initRot;
        vesselRoot.localScale = initScale;

        if (Manager.Instance != null)
        {
            Manager.Instance.LoadAndApplySettings();
            Debug.Log("<color=green>[ButtonController] Exhibition vessel reset applied</color>");
        }

        if (vesselMeasurementTool != null)
        {
            vesselMeasurementTool.ReloadCalibration();
        }

        ResetExhibitionOverlay(false);
    }

    private void CaptureExhibitionMenuInitialTransform()
    {
        if (exhibitionMenu == null || exhibitionMenuInitCaptured)
        {
            return;
        }

        exhibitionMenuInitLocalPos = exhibitionMenu.transform.localPosition;
        exhibitionMenuInitLocalRot = exhibitionMenu.transform.localRotation;
        exhibitionMenuInitLocalScale = exhibitionMenu.transform.localScale;
        exhibitionMenuInitCaptured = true;
    }

    private void ResetExhibitionOverlay(bool showMenuIntro)
    {
        CaptureExhibitionMenuInitialTransform();

        if (exhibitionMenu != null && exhibitionMenuInitCaptured)
        {
            exhibitionMenu.transform.localPosition = exhibitionMenuInitLocalPos;
            exhibitionMenu.transform.localRotation = exhibitionMenuInitLocalRot;
            exhibitionMenu.transform.localScale = exhibitionMenuInitLocalScale;
        }

        exhibitionOverlayPositionInitialized = false;

        ExhibitionGuideManager guideManager = ExhibitionGuideManager.EnsureInstance();
        if (guideManager != null)
        {
            guideManager.ResetOverlayState(exhibitionMenu != null ? exhibitionMenu.transform : null, showMenuIntro);
        }
    }

    private void RestoreExhibitionSliceLayoutIfNeeded()
    {
        if (currentExhibitionExperience != ExhibitionExperience.Slice || !exhibitionOverlayEnabled || sliceVisualization == null)
        {
            return;
        }

        PrepareExhibitionSliceVisualization();
        ShowExhibitionSlice2D();
        ShowExhibitionSliceToggleMenu();

        if (vesselRoot != null)
        {
            PositionSliceRightOfVessel(vesselRoot, false);
        }
    }

    private void HideExhibitionSliceVisuals()
    {
        HideExhibitionSliceToggleMenu();
        HideExhibitionSlider();

        if (velocityVisualizationSettingMenu != null)
        {
            velocityVisualizationSettingMenu.SetActive(false);
        }

        if (sliceVisualization == null)
        {
            return;
        }

        RestoreExhibitionSliceTransform();
        sliceVisualization.show2DHeatmap = false;
        sliceVisualization.show3DArrows = false;
        sliceVisualization.showSliceIndicator = false;
        sliceVisualization.customAnchorTransform = null;
        sliceVisualization.enabled = false;

        if (sliceVisualization.indicatorController != null)
        {
            if (exhibitionSliceKnifeDefaultsCaptured)
            {
                sliceVisualization.indicatorController.knifeGrabSize = exhibitionSliceKnifeGrabSizeDefault;
                sliceVisualization.indicatorController.knifeGrabDepth = exhibitionSliceKnifeGrabDepthDefault;
            }
            sliceVisualization.indicatorController.SetVisible(false);
            sliceVisualization.indicatorController.ForceHideKnifeVisuals();
            sliceVisualization.indicatorController.enabled = false;
            sliceVisualization.indicatorController.gameObject.SetActive(false);
        }

        sliceVisualization.DeactivateVisualization();

        if (sliceVisualization.viewRenderer != null)
        {
            if (sliceVisualization.viewRenderer.heatmapParent != null)
            {
                sliceVisualization.viewRenderer.heatmapParent.SetActive(false);
            }

            if (sliceVisualization.viewRenderer.arrowSliceParent != null)
            {
                sliceVisualization.viewRenderer.arrowSliceParent.SetActive(false);
            }
        }

        if (sliceVisualization.visualsParent != null)
        {
            sliceVisualization.visualsParent.SetActive(false);
        }

        if (sliceVisualization.viewRenderer != null)
        {
            sliceVisualization.viewRenderer.gameObject.SetActive(false);
        }

        sliceVisualization.gameObject.SetActive(false);
    }

    public void ResetApp()
    {
        Debug.Log("<color=magenta>===== ResetApp() called - About to reload scene =====</color>");
        Scene current = SceneManager.GetActiveScene();
        Debug.Log($"<color=magenta>Current scene: {current.name}</color>");
        SceneManager.LoadScene(current.name);
    }

    /// <summary>
    /// 길이 측정 설정 UI 토글
    /// mainUI에서 "길이 측정 설정" 버튼 클릭 시 호출
    /// </summary>
    private float lastMeasurementToggleTime = -1f;
    public void ToggleMeasurementSettingUI(bool networkCall = false)
    {
        // Debounce: prevent double-fire within 0.3 seconds
        if (Time.time - lastMeasurementToggleTime < 0.3f) 
        {
            Debug.Log("<color=yellow>[ButtonController] Measurement toggle debounced</color>");
            return;
        }
        lastMeasurementToggleTime = Time.time;
        
        if (measurementSettingUI != null)
        {
            bool newState = !measurementSettingUI.activeSelf;
            measurementSettingUI.SetActive(newState);
            
            // 활성화될 때 현재 메뉴 우측에 배치
            if (newState && !networkCall)
            {
                PositionUIToRightOfCurrentMenu(measurementSettingUI, 0.3f, networkCall);
            }
            
            Debug.Log($"<color=cyan>[ButtonController] Measurement Setting UI toggled: {(newState ? "ON" : "OFF")}</color>");
        }
        else
        {
            Debug.LogWarning("<color=yellow>[ButtonController] measurementSettingUI is not assigned!</color>");
        }

        playSettingMenu.SetActive(false);
                
        // 슬라이더 비활성화
        if (sliderController != null)
        {
            sliderController.SetSliderActive(false);
        }
        manager.visualizationMode = VisualizationMode.Mesh;
        manager.ApplyVisualizationMode(networkCall);
        
    }

    /// <summary>
    /// targetUI를 현재 활성화된 메뉴 우측에 배치
    /// </summary>
    /// <param name="targetUI">배치할 UI GameObject</param>
    /// <param name="offsetDistance">우측으로 이동할 거리 (미터 단위, 기본 0.15m)</param>
    public void PositionUIToRightOfCurrentMenu(GameObject targetUI, float offsetDistance = 0.15f, bool networkCall = false)
    {
        if (networkCall || targetUI == null) return;
        
        GameObject currentMenu = GetCurrentActiveMenu();
        if (currentMenu == null || currentMenu == targetUI)
        {
            // 현재 메뉴가 없으면 mainMenu 기준으로 배치
            currentMenu = mainMenu;
        }
        
        if (currentMenu != null)
        {
            // 현재 메뉴의 Transform 기준으로 우측에 배치
            Vector3 rightOffset = currentMenu.transform.right * offsetDistance;
            targetUI.transform.position = currentMenu.transform.position + rightOffset;

            Debug.Log($"<color=cyan>[ButtonController] Positioned {targetUI.name} to right of {currentMenu.name}</color>");
        }
    }

    /// <summary>
    /// 측정 마커 활성화/비활성화 토글
    /// enableMeasurement 토글 - true: 마커 표시/Object 조작 불가, false: 마커 숨김/Object 조작 가능
    /// </summary>
    public void ToggleEnableMeasurement(bool networkCall = false)
    {
        if (vesselMeasurementTool != null)
        {
            vesselMeasurementTool.enableMeasurement = !vesselMeasurementTool.enableMeasurement;
            
            // enableMeasurement가 false가 되면 objectMoveMode도 false로 리셋
            if (!vesselMeasurementTool.enableMeasurement)
            {
                vesselMeasurementTool.objectMoveMode = false;
                Debug.Log("<color=cyan>[ButtonController] objectMoveMode reset to false (measurement disabled)</color>");
                // Also sync objectMoveMode button if remote
                if (networkCall) SyncToggleButtonState("혈관 조작 버튼", false);
            }
            
            // Sync toggle button visual state only for remote calls (local button already toggled by MRTK)
            if (networkCall) SyncToggleButtonState("길이 측정 버튼", vesselMeasurementTool.enableMeasurement);
            
            Debug.Log($"<color=cyan>[ButtonController] enableMeasurement toggled: {vesselMeasurementTool.enableMeasurement}</color>");
        }
        else
        {
            Debug.LogWarning("<color=yellow>[ButtonController] vesselMeasurementTool is not assigned!</color>");
        }
    }

    /// <summary>
    /// Object 이동 모드 토글
    /// objectMoveMode 토글 - true: Object 조작 가능/마커 고정, false: Object 고정/마커 조작 가능
    /// </summary>
    public void ToggleObjectMoveMode(bool networkCall = false)
    {
        if (vesselMeasurementTool != null)
        {
            vesselMeasurementTool.objectMoveMode = !vesselMeasurementTool.objectMoveMode;
            
            // Sync toggle button visual state only for remote calls (local button already toggled by MRTK)
            if (networkCall) SyncToggleButtonState("혈관 조작 버튼", vesselMeasurementTool.objectMoveMode);
            
            Debug.Log($"<color=cyan>[ButtonController] objectMoveMode toggled: {vesselMeasurementTool.objectMoveMode}</color>");
        }
        else
        {
            Debug.LogWarning("<color=yellow>[ButtonController] vesselMeasurementTool is not assigned!</color>");
        }
    }

    /// <summary>
    /// MRTK Interactable 토글 버튼의 시각적 상태를 동기화
    /// </summary>
    public void SyncToggleButtonState(string buttonNameKeyword, bool isOn)
    {
        Transform root = buttonCollectionRoot != null ? buttonCollectionRoot : transform;
        var interactables = root.GetComponentsInChildren<Interactable>(true);
        
        foreach (var interactable in interactables)
        {
            if (interactable.name.Contains(buttonNameKeyword))
            {
                // MRTK Interactable의 IsToggled 상태 변경
                if (interactable.IsToggled != isOn)
                {
                    interactable.IsToggled = isOn;
                    Debug.Log($"<color=green>[ButtonController] Synced toggle state: {buttonNameKeyword} = {isOn}</color>");
                }
                break;
            }
        }
    }

    /// <summary>
    /// 마커와 라인을 초기 위치로 리셋
    /// </summary>
    public void ResetMarkerPositions()
    {
        if (vesselMeasurementTool != null)
        {
            vesselMeasurementTool.ResetMarkerPositions();
            Debug.Log("<color=cyan>[ButtonController] ResetMarkerPositions called</color>");
        }
        else
        {
            Debug.LogWarning("<color=yellow>[ButtonController] vesselMeasurementTool is not assigned!</color>");
        }
    }

    /// <summary>
    /// 폴더 선택 UI 토글
    /// </summary>
    public void ToggleFolderSelector()
    {
        if (manager != null && manager.folderSelectorUI != null)
        {
            manager.folderSelectorUI.ToggleFolderSelector();
            Debug.Log("<color=cyan>[ButtonController] ToggleFolderSelector called</color>");
        }
        else
        {
            Debug.LogWarning("<color=yellow>[ButtonController] folderSelectorUI is not assigned!</color>");
        }
    }

    /// <summary>
    /// 설정 메뉴 토글
    /// 설정 버튼 클릭 시 호출
    /// </summary>
    public void ToggleSettingsMenu(bool networkCall = false)
    {
        if (settingsMenu == null)
        {
            Debug.LogWarning("<color=yellow>[ButtonController] settingsMenu is not assigned!</color>");
            return;
        }
        AlignMenuToMain(settingsMenu, networkCall);
        SetMenu(settingsMenu, networkCall);
        if (sliderController != null) sliderController.SetSliderActive(false);
        if (sliceVisualization != null)
        {
            sliceVisualization.DeactivateVisualization();
            if (sliceVisualization.visualsParent != null)
                sliceVisualization.visualsParent.SetActive(false);
        }
        if (measurementSettingUI != null) measurementSettingUI.SetActive(false);
        playSettingMenu.SetActive(false);

    }

    /// <summary>
    /// 폴더 선택 메뉴 표시/숨김 토글
    /// </summary>
    private float lastFolderSelectorToggleTime = -1f;
    public void ShowFolderSelectorMenu(bool networkCall = false)
    {
        playSettingMenu.SetActive(false);
                
        // 슬라이더 비활성화
        if (sliderController != null)
        {
            sliderController.SetSliderActive(false);
        }
        if (!exhibitionOverlayEnabled && manager != null)
        {
            manager.visualizationMode = VisualizationMode.Mesh;
            manager.ApplyVisualizationMode();
        }

        // Debounce: prevent double-fire within 0.3 seconds
        if (Time.time - lastFolderSelectorToggleTime < 0.3f) 
        {
            Debug.Log("<color=yellow>[ButtonController] Folder selector toggle debounced</color>");
            return;
        }
        lastFolderSelectorToggleTime = Time.time;
        if (folderSelectorMenu != null)
        {            
            // FolderSelectorUI 컴포넌트가 있으면 사용
            var folderUI = folderSelectorMenu.GetComponent<FolderSelectorUI>();
            if (folderUI != null)
            {
                // folderSelectorPanel의 실제 상태를 확인하여 토글
                bool isCurrentlyVisible = folderUI.folderSelectorPanel != null && folderUI.folderSelectorPanel.activeSelf;
                
                if (isCurrentlyVisible)
                {
                    folderUI.HideFolderSelector();
                    folderSelectorMenu.SetActive(false); // 부모도 비활성화
                    pendingDataChangeFromFolderSelector = false;
                    restoreExhibitionAfterDataChange = false;

                }
                else
                {
                    pendingDataChangeFromFolderSelector = true;
                    restoreExhibitionAfterDataChange = exhibitionOverlayEnabled;

                    if (!restoreExhibitionAfterDataChange)
                    {
                        ShowMainMenu(networkCall);
                    }

                    // 먼저 부모 GameObject를 활성화
                    folderSelectorMenu.SetActive(true);
                    folderUI.ShowFolderSelector();
                    // 활성화된 경우 우측 배치 (folderSelectorPanel도 함께)
                    if (!networkCall)
                    {
                        if (folderUI.folderSelectorPanel != null)
                        {
                            PositionUIToRightOfCurrentMenu(folderUI.folderSelectorPanel, 0.2f, networkCall);
                        }
                        else
                        {
                            PositionUIToRightOfCurrentMenu(folderSelectorMenu, 0.2f, networkCall);
                        }
                    }
                }
            }
            else
            {
                // FolderSelectorUI가 없으면 GameObject 토글
                bool newState = !folderSelectorMenu.activeSelf;
                folderSelectorMenu.SetActive(newState);

                if (newState)
                {
                    pendingDataChangeFromFolderSelector = true;
                    restoreExhibitionAfterDataChange = exhibitionOverlayEnabled;

                    if (!restoreExhibitionAfterDataChange)
                    {
                        ShowMainMenu(networkCall);
                    }
                }
                else
                {
                    pendingDataChangeFromFolderSelector = false;
                    restoreExhibitionAfterDataChange = false;
                }
                
                if (newState && !networkCall)
                {
                    PositionUIToRightOfCurrentMenu(folderSelectorMenu, 0.2f, networkCall);
                }
            }
            
            Debug.Log($"<color=cyan>[ButtonController] Folder Selector Menu toggled: {folderSelectorMenu.activeSelf}</color>");
        }
        else
        {
            Debug.LogWarning("<color=yellow>[ButtonController] folderSelectorMenu is not assigned!</color>");
        }


    }

    public void NotifyDataFolderSelectionConfirmed()
    {
        if (!pendingDataChangeFromFolderSelector)
        {
            pendingDataChangeFromFolderSelector = true;
            restoreExhibitionAfterDataChange = exhibitionOverlayEnabled;
        }
    }

    public void NotifyDataFolderChangeFinished()
    {
        if (!pendingDataChangeFromFolderSelector)
        {
            return;
        }

        bool shouldRestoreExhibition = restoreExhibitionAfterDataChange;
        pendingDataChangeFromFolderSelector = false;
        restoreExhibitionAfterDataChange = false;

        if (shouldRestoreExhibition)
        {
            ReturnToExhibitionHome();
        }
    }

    #region Frame Control Mode
    
    /// <summary>
    /// 프레임 컨트롤 모드 토글
    /// ON: 모든 로더 애니메이션 정지, 슬라이더로 프레임 조작 가능
    /// OFF: 애니메이션 재개
    /// </summary>
    public void ToggleFrameControlMode()
    {
        isFrameControlMode = !isFrameControlMode;
        
        // Sync button visual state locally (if MRTK doesn't handle it for programmatic calls)
        SyncToggleButtonState("수동 재생", isFrameControlMode);
        
        if (isFrameControlMode)
        {
            // 모든 로더 애니메이션 정지
            PauseAllLoaders();
            Debug.Log("<color=cyan>[ButtonController] Frame Control Mode: ON</color>");
        }
        else
        {
            // 모든 로더 애니메이션 재개
            ResumeAllLoaders();
            Debug.Log("<color=cyan>[ButtonController] Frame Control Mode: OFF</color>");
        }
        
        // 네트워크 동기화
        if (networkSync && PhotonNetwork.IsConnected && PhotonSyncService.Instance != null)
        {
            PhotonSyncService.Instance.BroadcastFrameControlMode(isFrameControlMode);
        }
    }
    
    /// <summary>
    /// 네트워크에서 받은 프레임 컨트롤 상태 적용
    /// </summary>
    public void ApplyNetworkFrameControl(bool frameControlMode, int frameIndex)
    {
        isFrameControlMode = frameControlMode;
        
        if (isFrameControlMode)
        {
            PauseAllLoaders();
        }
        else
        {
            ResumeAllLoaders();
        }
        
        // Sync toggle button visual state for network calls
        SyncToggleButtonState("수동 재생", isFrameControlMode);
        
        // 프레임 인덱스가 유효하면 적용
        if (frameIndex >= 0)
        {
            SetAllLoadersFrameIndexInternal(frameIndex);
        }
        
        Debug.Log($"<color=magenta>[Network] Frame Control synced: mode={isFrameControlMode}, frame={frameIndex}</color>");
    }

    /// <summary>
    /// 새로운 데이터 로드 시 모든 관련 상태 초기화 (수동 재생, 측정 등)
    /// </summary>
    public void ResetAllStatesForNewData()
    {
        // 1. 프레임 컨트롤 모드 해제 및 애니메이션 재개
        isFrameControlMode = false;
        ResumeAllLoaders();
        SyncToggleButtonState("수동 재생", false);
        
        // 2. 측정 도구 상태 초기화
        if (vesselMeasurementTool != null)
        {
            vesselMeasurementTool.enableMeasurement = false;
            vesselMeasurementTool.objectMoveMode = false;
            vesselMeasurementTool.ResetMarkerPositions();
            vesselMeasurementTool.StopMeasurement();
            
            // 버튼 시각적 상태 동기화
            SyncToggleButtonState("길이 측정 버튼", false);
            SyncToggleButtonState("혈관 조작 버튼", false);
        }
        
        Debug.Log("<color=green>[ButtonController] All states reset for new data folder</color>");
    }

    
    /// <summary>
    /// 내부용 프레임 설정 (네트워크 발송 없음)
    /// </summary>
    private void SetAllLoadersFrameIndexInternal(int frameIndex)
    {
        if (manager == null) return;
        
        if (manager.wssLoader != null && manager.wssLoader.TotalFrameCount > 0)
        {
            manager.wssLoader.SetMeshFrameIndex(frameIndex % manager.wssLoader.TotalFrameCount);
        }
        if (manager.streamlineLoader != null && manager.streamlineLoader.TotalFrameCount > 0)
        {
            manager.streamlineLoader.SetFrameIndex(frameIndex % manager.streamlineLoader.TotalFrameCount);
        }
        if (manager.velocityLoader != null && manager.velocityLoader.TotalFrameCount > 0)
        {
            manager.velocityLoader.SetFrameIndex(frameIndex % manager.velocityLoader.TotalFrameCount);
        }
    }
    
    /// <summary>
    /// 모든 로더 애니메이션 일시정지
    /// </summary>
    public void PauseAllLoaders()
    {
        if (manager == null)
        {
            Debug.LogWarning("<color=yellow>[ButtonController] PauseAllLoaders: manager is null!</color>");
            return;
        }
        
        Debug.Log($"<color=yellow>[ButtonController] PauseAllLoaders called - WSS: {manager.wssLoader != null}, Streamline: {manager.streamlineLoader != null}, Velocity: {manager.velocityLoader != null}</color>");
        
        if (manager.wssLoader != null)
        {
            Debug.Log($"<color=yellow>[LoadWSS] isAnimating before pause: {manager.wssLoader.isAnimating}</color>");
            manager.wssLoader.PauseAnimation();
        }
        if (manager.streamlineLoader != null)
        {
            Debug.Log($"<color=yellow>[LoadStreamline] isAnimating before pause: {manager.streamlineLoader.isAnimating}</color>");
            manager.streamlineLoader.PauseAnimation();
        }
        if (manager.velocityLoader != null)
        {
            Debug.Log($"<color=yellow>[VelocityLoader] calling PauseAnimation</color>");
            manager.velocityLoader.PauseAnimation();
        }
    }
    
    /// <summary>
    /// 모든 로더 애니메이션 재개
    /// </summary>
    public void ResumeAllLoaders()
    {
        if (manager == null) return;
        
        if (manager.wssLoader != null) manager.wssLoader.ResumeAnimation();
        if (manager.streamlineLoader != null) manager.streamlineLoader.ResumeAnimation();
        if (manager.velocityLoader != null) manager.velocityLoader.ResumeAnimation();
    }
    
    /// <summary>
    /// 슬라이더 값으로 모든 로더의 프레임 인덱스 설정 (0~1)
    /// </summary>
    public void SetAllLoadersFrameBySlider(float sliderValue)
    {
        bool frameSliderActive = sliderController != null && sliderController.mode == ControlMode.FrameControl;
        if (!isFrameControlMode && !frameSliderActive) return;
        
        int maxFrames = GetMaxFrameCount();
        if (maxFrames <= 0) return;
        
        int frameIndex = Mathf.Clamp(Mathf.RoundToInt(sliderValue * (maxFrames - 1)), 0, maxFrames - 1);
        SetAllLoadersFrameIndex(frameIndex);
    }
    
    /// <summary>
    /// 모든 로더에 특정 프레임 인덱스 설정
    /// </summary>
    public void SetAllLoadersFrameIndex(int frameIndex)
    {
        if (manager == null) return;
        
        // WSS
        if (manager.wssLoader != null && manager.wssLoader.TotalFrameCount > 0)
        {
            int wssFrame = frameIndex % manager.wssLoader.TotalFrameCount;
            manager.wssLoader.SetMeshFrameIndex(wssFrame);
        }
        
        // Streamline
        if (manager.streamlineLoader != null && manager.streamlineLoader.TotalFrameCount > 0)
        {
            int streamFrame = frameIndex % manager.streamlineLoader.TotalFrameCount;
            manager.streamlineLoader.SetFrameIndex(streamFrame);
        }
        
        // Velocity
        if (manager.velocityLoader != null && manager.velocityLoader.TotalFrameCount > 0)
        {
            int velFrame = frameIndex % manager.velocityLoader.TotalFrameCount;
            manager.velocityLoader.SetFrameIndex(velFrame);
        }
        
        // 네트워크 동기화
        if (networkSync && PhotonNetwork.IsConnected && PhotonSyncService.Instance != null)
        {
            PhotonSyncService.Instance.BroadcastFrameIndex(frameIndex);
        }
    }
    
    /// <summary>
    /// 현재 활성화된 로더 중 최대 프레임 수 반환
    /// </summary>
    public int GetMaxFrameCount()
    {
        int maxFrames = 0;
        
        if (manager == null) return maxFrames;
        
        if (manager.wssLoader != null)
            maxFrames = Mathf.Max(maxFrames, manager.wssLoader.TotalFrameCount);
        if (manager.streamlineLoader != null)
            maxFrames = Mathf.Max(maxFrames, manager.streamlineLoader.TotalFrameCount);
        if (manager.velocityLoader != null)
            maxFrames = Mathf.Max(maxFrames, manager.velocityLoader.TotalFrameCount);
        
        return maxFrames;
    }
    
    /// <summary>
    /// 현재 프레임 인덱스 반환 (슬라이더 값 동기화용)
    /// </summary>
    public int GetCurrentFrameIndex()
    {
        if (manager == null) return 0;
        
        // 활성화된 첫 번째 로더의 현재 프레임 인덱스 반환
        if (manager.velocityLoader != null && manager.velocityLoader.TotalFrameCount > 0)
            return manager.velocityLoader.currentFrameIndex;
        if (manager.streamlineLoader != null && manager.streamlineLoader.TotalFrameCount > 0)
            return manager.streamlineLoader.CurrentFrameIndex;
        if (manager.wssLoader != null && manager.wssLoader.TotalFrameCount > 0)
            return manager.wssLoader.CurrentFrameIndex;
        
        return 0;
    }
    
    #endregion

    #region Play Setting Menu
    
    /// <summary>
    /// 재생 설정 메뉴 토글. 메뉴 표시 시 슬라이더를 FrameControl 모드로 설정.
    /// </summary>
    public void TogglePlaySettingMenu(bool networkCall = false)
    {
        if (playSettingMenu != null)
        {
            bool newState = !playSettingMenu.activeSelf;
            
            if (newState)
            {
                if (measurementSettingUI != null) measurementSettingUI.SetActive(false);
                
                // 먼저 위치를 설정한 후 활성화 (mainMenu 기준 우측 배치)
                if (mainMenu != null && !networkCall)
                {
                    PositionUIToRightOfCurrentMenu(playSettingMenu, 0.3f, networkCall);
                }
                
                playSettingMenu.SetActive(true);
                
                // WSS 모드로 변경
                if (manager != null)
                {
                    manager.visualizationMode = VisualizationMode.WSS;
                    manager.ApplyVisualizationMode(networkCall);
                    
                    // 네트워크 동기화
                    if (networkSync && PhotonNetwork.IsConnected && PhotonSyncService.Instance != null)
                    {
                        PhotonSyncService.Instance.BroadcastVisualizationMode((int)VisualizationMode.WSS);
                    }
                    
                    // FORCE HIDE ColorBars for Play Setting Menu
                    if (manager.velocityColorBar != null) manager.velocityColorBar.gameObject.SetActive(false);
                    if (manager.wssColorBar != null) manager.wssColorBar.gameObject.SetActive(false);
                }
                
                // 슬라이더를 FrameControl 모드로 설정
                if (sliderController != null)
                {
                    sliderController.SetMode(ControlMode.FrameControl);
                    sliderController.SetSliderActive(true);
                }
                

                
                Debug.Log("<color=cyan>[ButtonController] Play Setting Menu: ON, Mode set to WSS, Slider set to FrameControl mode</color>");
            }
            else
            {
                playSettingMenu.SetActive(false);
                
                // 슬라이더 비활성화
                if (sliderController != null)
                {
                    sliderController.SetSliderActive(false);
                }
                
                
                // Mesh 모드로 변경
                if (manager != null)
                {
                    manager.visualizationMode = VisualizationMode.Mesh;
                    manager.ApplyVisualizationMode();
                    
                    // 네트워크 동기화
                    if (networkSync && PhotonNetwork.IsConnected && PhotonSyncService.Instance != null)
                    {
                        PhotonSyncService.Instance.BroadcastVisualizationMode((int)VisualizationMode.Mesh);
                    }
                }
                Debug.Log("<color=cyan>[ButtonController] Play Setting Menu: OFF</color>");
            }
        }
        else
        {
            Debug.LogWarning("<color=yellow>[ButtonController] playSettingMenu is not assigned!</color>");
        }        
        
    }
    
    #endregion

    #region Slice Positioning
    
    /// <summary>
    /// Positions the slice visualization to the right of the menu at center height
    /// </summary>
    private void PositionSliceNextToMenu(Transform menuTransform, bool networkCall = false)
    {
        if (networkCall || sliceVisualization == null || menuTransform == null) return;
        
        // Get menu's BoxCollider to calculate bounds
        BoxCollider menuCollider = menuTransform.GetComponent<BoxCollider>();
        
        if (menuCollider != null)
        {
            // Calculate center-right position in local coordinates
            Vector3 colliderCenter = menuCollider.center;
            Vector3 colliderHalfSize = menuCollider.size * 0.5f;
            
            // Center-right point: +X (right edge), Y center, Z center
            Vector3 centerRightLocal = new Vector3(
                colliderCenter.x + colliderHalfSize.x,  // Right edge
                colliderCenter.y,                        // Vertical center (not top!)
                colliderCenter.z
            );
            
            // Add offset for spacing (adjustable)
            Vector3 offset = new Vector3(0.15f, 0f, 0f); // 15cm to the right
            Vector3 targetLocalPos = centerRightLocal + offset;
            
            // Convert to world position
            Vector3 targetWorldPos = menuTransform.TransformPoint(targetLocalPos);
            
            // Apply position and rotation (with 180 degree Z rotation)
            sliceVisualization.transform.position = targetWorldPos;
            sliceVisualization.transform.rotation = menuTransform.rotation * Quaternion.Euler(0, 0, 180);
            
            Debug.Log($"<color=cyan>[ButtonController] SliceController positioned at center-right: {targetWorldPos}</color>");
        }
        else
        {
            // Fallback: simple offset from menu transform (with 180 degree Z rotation)
            Vector3 rightOffset = menuTransform.right * 0.2f; // 20cm to the right
            sliceVisualization.transform.position = menuTransform.position + rightOffset;
            sliceVisualization.transform.rotation = menuTransform.rotation * Quaternion.Euler(0, 0, 180);
            
            Debug.LogWarning("<color=yellow>[ButtonController] Menu BoxCollider not found, using simple offset</color>");
        }
    }

    private void PositionSliceLeftOfMenu(Transform menuTransform, bool networkCall = false)
    {
        if (networkCall || sliceVisualization == null || menuTransform == null) return;

        BoxCollider menuCollider = menuTransform.GetComponent<BoxCollider>();

        if (menuCollider != null)
        {
            Vector3 colliderCenter = menuCollider.center;
            Vector3 colliderHalfSize = menuCollider.size * 0.5f;
            Vector3 centerLeftLocal = new Vector3(
                colliderCenter.x - colliderHalfSize.x,
                colliderCenter.y,
                colliderCenter.z
            );

            Vector3 offset = new Vector3(-0.15f, 0f, 0f);
            Vector3 targetLocalPos = centerLeftLocal + offset;
            Vector3 targetWorldPos = menuTransform.TransformPoint(targetLocalPos);

            sliceVisualization.transform.position = targetWorldPos;
            sliceVisualization.transform.rotation = menuTransform.rotation * Quaternion.Euler(0, 0, 180);
            Debug.Log($"<color=cyan>[ButtonController] SliceController positioned at center-left: {targetWorldPos}</color>");
        }
        else
        {
            Vector3 leftOffset = menuTransform.right * -0.38f;
            sliceVisualization.transform.position = menuTransform.position + leftOffset;
            sliceVisualization.transform.rotation = menuTransform.rotation * Quaternion.Euler(0, 0, 180);
            Debug.LogWarning("<color=yellow>[ButtonController] Menu BoxCollider not found, using left-side simple offset</color>");
        }
    }

    private void PositionSliceRightOfVessel(Transform vesselTransform, bool networkCall = false)
    {
        if (networkCall || sliceVisualization == null || vesselTransform == null) return;

        Vector3 targetWorldPos =
            vesselTransform.position +
            (vesselTransform.right * 0.28f) +
            (Vector3.up * 0.02f);

        sliceVisualization.transform.position = targetWorldPos;
        sliceVisualization.transform.rotation = vesselTransform.rotation * Quaternion.Euler(0, 0, 180);
    }
    
    #endregion

    #region Visualization Mode Toggle
    
    /// <summary>
    /// WSS, Streamline, Velocity 모드를 순환하는 토글 함수
    /// </summary>
    public void ToggleVisualizationMode()
    {
        if (manager == null) return;
        
        // 현재 모드에서 다음 모드로 순환
        switch (manager.visualizationMode)
        {
            case VisualizationMode.WSS:
                manager.visualizationMode = VisualizationMode.Streamline;
                Debug.Log("<color=green>[ButtonController] Visualization Mode: Streamline</color>");
                break;
            case VisualizationMode.Streamline:
                manager.visualizationMode = VisualizationMode.Velocity;
                Debug.Log("<color=green>[ButtonController] Visualization Mode: Velocity</color>");
                break;
            case VisualizationMode.Velocity:
                manager.visualizationMode = VisualizationMode.WSS;
                Debug.Log("<color=green>[ButtonController] Visualization Mode: WSS</color>");
                break;
            default:
                manager.visualizationMode = VisualizationMode.WSS;
                Debug.Log("<color=green>[ButtonController] Visualization Mode: WSS (default)</color>");
                break;
        }
        
        // 적용
        manager.ApplyVisualizationMode();
        // FORCE HIDE ColorBars for Play Setting Menu
        if (manager.velocityColorBar != null) manager.velocityColorBar.gameObject.SetActive(false);
        if (manager.wssColorBar != null) manager.wssColorBar.gameObject.SetActive(false);
    
        // 네트워크 동기화
        if (networkSync && PhotonNetwork.IsConnected && PhotonSyncService.Instance != null)
        {
            PhotonSyncService.Instance.BroadcastVisualizationMode((int)manager.visualizationMode);
        }
    }
    
    #endregion
    
    #region Menu Lock Handling
    
    private void SetupMenuLocks()
    {
        GameObject[] menus = { 
            mainMenu, streamlineMenu, streamlineSpeedMenu, velocityMenu, wssMenu, 
            visualizationSettingMenu, velocityVisualizationSettingMenu, 
            measurementSettingUI, settingsMenu, folderSelectorMenu, playSettingMenu 
        };

        foreach (var menu in menus)
        {
            if (menu == null) continue;
            var manipulator = menu.GetComponent<ObjectManipulator>();
            if (manipulator != null)
            {
                manipulator.OnManipulationStarted.RemoveAllListeners(); // Clear old ones if re-initializing
                manipulator.OnManipulationStarted.AddListener((_) => {
                    if (PhotonSyncService.Instance != null)
                        PhotonSyncService.Instance.RequestGlobalLock(PhotonSyncService.LockType.ObjectManipulation);
                });
                manipulator.OnManipulationEnded.AddListener((_) => {
                    if (PhotonSyncService.Instance != null)
                        PhotonSyncService.Instance.ReleaseGlobalLock();
                });
                Debug.Log($"<color=green>[ButtonController] Setup sync lock for menu: {menu.name}</color>");
            }
        }
    }
    
    #endregion
}


 
