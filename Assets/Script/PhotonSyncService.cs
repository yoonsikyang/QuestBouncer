using System;
using System.Collections;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public class PhotonSyncService : MonoBehaviourPunCallbacks, IOnEventCallback
{
    public static PhotonSyncService Instance;

    private const byte ButtonActionEvent = 1;
    private const byte SliderValueEvent = 2;
    private const byte SnapshotRequestEvent = 3;
    private const byte SnapshotEvent = 4;
    private const byte TransformSyncEvent = 5;
    private const byte LockSyncEvent = 6;
    private const byte MeasurementSyncEvent = 7;
    private const byte DataFolderSyncEvent = 8;
    private const byte WSSSubModeSyncEvent = 9;
    private const byte PlaybackIndexSyncEvent = 10;
    private const byte FrameControlSyncEvent = 11;
    private const byte VisualizationModeSyncEvent = 12;

    private ButtonControllerManager buttonManager;
    private GlobalSliderController sliderController;
    private SliceController sliceVisualization;
    private Manager manager;
    private VesselMeasurementTool measurementTool;

    private Vector3 lastParentPos;
    private Quaternion lastParentRot; 
    private Vector3 lastParentScale = Vector3.one;
    private Vector3 lastSlicePos;
    private Quaternion lastSliceRot;
    private Vector3 lastSliceScale = Vector3.one;

    private Vector3 lastVelColorBarPos;
    private Quaternion lastVelColorBarRot;
    private Vector3 lastWssColorBarPos;
    private Quaternion lastWssColorBarRot;

    private Vector3 lastSlicePlanePos;
    private Quaternion lastSlicePlaneRot;
    
    private Vector3 lastSliceIndicatorPos;
    private Quaternion lastSliceIndicatorRot;
    private Vector3 lastSliceIndicatorScale = Vector3.one;

    private float nextTransformSendTime = 0f;
    
    [Header("Playback Sync Settings")]
    [Tooltip("Interval for syncing playback indices (seconds)")]
    public float playbackSyncInterval = 0.5f;
    private float nextPlaybackSyncTime = 0;

    [Header("Network Suppression")]
    private bool isSyncSuppressed = false;
    private float suppressionEndTime = 0f;
    private System.Collections.Generic.Dictionary<TransformTarget, float> lastReceiveTimestamps = new System.Collections.Generic.Dictionary<TransformTarget, float>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        PhotonNetwork.AddCallbackTarget(this);
        PhotonNetwork.AutomaticallySyncScene = true;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            PhotonNetwork.RemoveCallbackTarget(this);
        }
    }

    private Vector3 lastPlaySettingMenuPos;
    private Quaternion lastPlaySettingMenuRot;
    private Vector3 lastMeasurementMenuPos;
    private Quaternion lastMeasurementMenuRot;
    private Vector3 lastFolderSelectorMenuPos;
    private Quaternion lastFolderSelectorMenuRot;
    
    // New Menus
    private Vector3 lastMainMenuPos;
    private Quaternion lastMainMenuRot;
    private Vector3 lastStreamlineMenuPos;
    private Quaternion lastStreamlineMenuRot;
    private Vector3 lastStreamlineSpeedMenuPos;
    private Quaternion lastStreamlineSpeedMenuRot;
    private Vector3 lastVelocityMenuPos;
    private Quaternion lastVelocityMenuRot;
    private Vector3 lastWssMenuPos;
    private Quaternion lastWssMenuRot;
    private Vector3 lastVisSettingMenuPos;
    private Quaternion lastVisSettingMenuRot;
    private Vector3 lastVelVisSettingMenuPos;
    private Quaternion lastVelVisSettingMenuRot;
    private Vector3 lastSettingsMenuPos;
    private Quaternion lastSettingsMenuRot;
    
    // Scale refs for menus
    private Vector3 lastPlaySettingMenuScale = Vector3.one;
    private Vector3 lastMeasurementMenuScale = Vector3.one;
    private Vector3 lastFolderSelectorMenuScale = Vector3.one;
    private Vector3 lastMainMenuScale = Vector3.one;
    private Vector3 lastStreamlineMenuScale = Vector3.one;
    private Vector3 lastStreamlineSpeedMenuScale = Vector3.one;
    private Vector3 lastVelocityMenuScale = Vector3.one;
    private Vector3 lastWssMenuScale = Vector3.one;
    private Vector3 lastVisSettingMenuScale = Vector3.one;
    private Vector3 lastVelVisSettingMenuScale = Vector3.one;
    private Vector3 lastSettingsMenuScale = Vector3.one;
    
    // Placeholder for additional scales if needed
    private Vector3 lastDummyScale = Vector3.one;

    
    // ... (existing helper methods)

    void Update()
    {
        if (!PhotonNetwork.InRoom) return;
        EnsureRefs();

        // Update suppression state
        if (isSyncSuppressed && Time.time >= suppressionEndTime)
        {
            isSyncSuppressed = false;
            Debug.Log("<color=green>[PhotonSyncService] Network sync suppression ENDED</color>");
        }

        // Transform sync
        if (!isSyncSuppressed && Time.time >= nextTransformSendTime)
        {
            nextTransformSendTime = Time.time + 0.05f;
            SyncTransformIfChanged(manager != null ? manager.ObjectParent?.transform : null, TransformTarget.ObjectParent, ref lastParentPos, ref lastParentRot, ref lastParentScale);
            SyncTransformIfChanged(sliceVisualization != null ? sliceVisualization.transform : null, TransformTarget.SliceVisualization, ref lastSlicePos, ref lastSliceRot, ref lastSliceScale);
            
            // Sync Color Bars
            if (manager != null)
            {
                SyncTransformIfChanged(manager.velocityColorBar?.transform, TransformTarget.VelColorBar, ref lastVelColorBarPos, ref lastVelColorBarRot, ref lastSliceScale); // Use some scale dummy
                SyncTransformIfChanged(manager.wssColorBar?.transform, TransformTarget.WssColorBar, ref lastWssColorBarPos, ref lastWssColorBarRot, ref lastSliceScale);
                
                // Sync Slice Plane (if exists)
                var slicePlane = FindObjectOfType<SlicePlaneController>();
                if (slicePlane != null && slicePlane.slicePlane != null)
                {
                    SyncTransformIfChanged(slicePlane.slicePlane.transform, TransformTarget.SlicePlane, ref lastSlicePlanePos, ref lastSlicePlaneRot, ref lastSliceScale);
                }
                
                // Sync Slice Indicator
                var sliceIndicator = FindObjectOfType<SliceIndicatorController>();
                if (sliceIndicator != null && sliceIndicator.indicatorCube != null)
                {
                    SyncTransformIfChanged(sliceIndicator.indicatorCube.transform, TransformTarget.SliceIndicator, ref lastSliceIndicatorPos, ref lastSliceIndicatorRot, ref lastSliceIndicatorScale);
                }
            }
            
            // Sync UI Menus
            if (buttonManager != null)
            {
                SyncTransformIfChanged(buttonManager.playSettingMenu?.transform, TransformTarget.PlaySettingMenu, ref lastPlaySettingMenuPos, ref lastPlaySettingMenuRot, ref lastPlaySettingMenuScale);
                SyncTransformIfChanged(buttonManager.measurementSettingUI?.transform, TransformTarget.MeasurementMenu, ref lastMeasurementMenuPos, ref lastMeasurementMenuRot, ref lastMeasurementMenuScale);
                SyncTransformIfChanged(buttonManager.folderSelectorMenu?.transform, TransformTarget.FolderSelectorMenu, ref lastFolderSelectorMenuPos, ref lastFolderSelectorMenuRot, ref lastFolderSelectorMenuScale);
                
                // Sync remaining UIs
                SyncTransformIfChanged(buttonManager.mainMenu?.transform, TransformTarget.MainMenu, ref lastMainMenuPos, ref lastMainMenuRot, ref lastMainMenuScale);
                SyncTransformIfChanged(buttonManager.streamlineMenu?.transform, TransformTarget.StreamlineMenu, ref lastStreamlineMenuPos, ref lastStreamlineMenuRot, ref lastStreamlineMenuScale);
                SyncTransformIfChanged(buttonManager.streamlineSpeedMenu?.transform, TransformTarget.StreamlineSpeedMenu, ref lastStreamlineSpeedMenuPos, ref lastStreamlineSpeedMenuRot, ref lastStreamlineSpeedMenuScale);
                SyncTransformIfChanged(buttonManager.velocityMenu?.transform, TransformTarget.VelocityMenu, ref lastVelocityMenuPos, ref lastVelocityMenuRot, ref lastVelocityMenuScale);
                SyncTransformIfChanged(buttonManager.wssMenu?.transform, TransformTarget.WssMenu, ref lastWssMenuPos, ref lastWssMenuRot, ref lastWssMenuScale);
                SyncTransformIfChanged(buttonManager.visualizationSettingMenu?.transform, TransformTarget.VisSettingMenu, ref lastVisSettingMenuPos, ref lastVisSettingMenuRot, ref lastVisSettingMenuScale);
                SyncTransformIfChanged(buttonManager.velocityVisualizationSettingMenu?.transform, TransformTarget.VelVisSettingMenu, ref lastVelVisSettingMenuPos, ref lastVelVisSettingMenuRot, ref lastVelVisSettingMenuScale);
                SyncTransformIfChanged(buttonManager.settingsMenu?.transform, TransformTarget.SettingsMenu, ref lastSettingsMenuPos, ref lastSettingsMenuRot, ref lastSettingsMenuScale);
            }
        }
        
    }

    void EnsureRefs()
    {
        if (buttonManager == null) buttonManager = FindObjectOfType<ButtonControllerManager>();
        if (sliderController == null) sliderController = FindObjectOfType<GlobalSliderController>();
        if (sliceVisualization == null) sliceVisualization = FindObjectOfType<SliceController>();
        if (manager == null) manager = Manager.Instance ?? FindObjectOfType<Manager>();
        if (measurementTool == null) measurementTool = FindObjectOfType<VesselMeasurementTool>();
    }

    public void BroadcastButtonAction(ButtonControllerManager.ButtonAction action)
    {
        if (!PhotonNetwork.InRoom) return;
        PhotonNetwork.RaiseEvent(ButtonActionEvent, (int)action, ReliableToOthers(), SendOptions.SendReliable);
    }

    public void BroadcastSliderValue(float value, ControlMode mode, SliceController.SliceAxis axis, bool isActive)
    {
        if (!PhotonNetwork.InRoom) return;
        object[] payload = new object[] { value, (int)mode, (int)axis, isActive };
        PhotonNetwork.RaiseEvent(SliderValueEvent, payload, ReliableToOthers(), SendOptions.SendUnreliable);
    }

    /// <summary>
    /// 측정 도구 상태 브로드캐스트 (마커 위치, 측정 모드)
    /// </summary>
    public void BroadcastMeasurementState(Vector3 marker1LocalPos, Vector3 marker2LocalPos, bool isEnabled, bool isInteracting)
    {
        if (!PhotonNetwork.InRoom) return;
        object[] payload = new object[] { marker1LocalPos, marker2LocalPos, isEnabled, isInteracting };
        PhotonNetwork.RaiseEvent(MeasurementSyncEvent, payload, ReliableToOthers(), isInteracting ? SendOptions.SendUnreliable : SendOptions.SendReliable);
    }

    /// <summary>
    /// 데이터 폴더 변경 브로드캐스트 + Room Property 업데이트 (Late Joiner용)
    /// </summary>
    public void BroadcastDataFolder(string folderName)
    {
        if (!PhotonNetwork.InRoom) return;

        // 1. Raise Event for immediate update to others
        PhotonNetwork.RaiseEvent(DataFolderSyncEvent, folderName, ReliableToOthers(), SendOptions.SendReliable);
        
        // 2. Update Room Property for Late Joiners
        if (PhotonNetwork.IsMasterClient)
        {
            var props = new ExitGames.Client.Photon.Hashtable { { "CurrentDataFolder", folderName } };
            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }
        
        Debug.Log($"<color=cyan>[PhotonSync] Broadcasting DataFolder: {folderName}</color>");
    }

    /// <summary>
    /// WSS SubMode 변경 브로드캐스트
    /// </summary>
    public void BroadcastWSSSubMode(int subMode)
    {
        if (!PhotonNetwork.InRoom) return;
        PhotonNetwork.RaiseEvent(WSSSubModeSyncEvent, subMode, ReliableToOthers(), SendOptions.SendReliable);
        Debug.Log($"<color=cyan>[PhotonSync] Broadcasting WSSSubMode: {(WSSSubMode)subMode}</color>");
    }
    
    /// <summary>
    /// 프레임 컨트롤 모드 브로드캐스트
    /// </summary>
    public void BroadcastFrameControlMode(bool isFrameControlMode)
    {
        if (!PhotonNetwork.InRoom) return;
        PhotonNetwork.RaiseEvent(FrameControlSyncEvent, new object[] { isFrameControlMode, -1 }, ReliableToOthers(), SendOptions.SendReliable);
        Debug.Log($"<color=cyan>[PhotonSync] Broadcasting FrameControlMode: {isFrameControlMode}</color>");
    }
    
    /// <summary>
    /// 프레임 인덱스 브로드캐스트
    /// </summary>
    public void BroadcastFrameIndex(int frameIndex)
    {
        if (!PhotonNetwork.InRoom) return;
        // 프레임 컨트롤 모드 상태 유지 + 프레임 인덱스 전송
        PhotonNetwork.RaiseEvent(FrameControlSyncEvent, new object[] { true, frameIndex }, ReliableToOthers(), SendOptions.SendUnreliable);
    }
    
    /// <summary>
    /// 시각화 모드 브로드캐스트
    /// </summary>
    public void BroadcastVisualizationMode(int visualizationMode)
    {
        if (!PhotonNetwork.InRoom) return;
        PhotonNetwork.RaiseEvent(VisualizationModeSyncEvent, visualizationMode, ReliableToOthers(), SendOptions.SendReliable);
        Debug.Log($"<color=cyan>[PhotonSync] Broadcasting VisualizationMode: {(VisualizationMode)visualizationMode}</color>");
    }

    /// <summary>
    /// 지정된 시간(초) 동안 네트워크 동기화(Transform sync)를 일시 중지합니다.
    /// 로컬 초기화 로직이 네트워크 업데이트와 충돌하는 것을 방지하기 위해 사용합니다.
    /// </summary>
    public void SuppressSyncForDuration(float duration)
    {
        isSyncSuppressed = true;
        suppressionEndTime = Time.time + duration;
        Debug.Log($"<color=orange>[PhotonSyncService] Network sync suppressed for {duration} seconds (Until {suppressionEndTime:F2})</color>");
    }
    


    public override void OnJoinedRoom()
    {
        base.OnJoinedRoom();
        
        Debug.Log($"<color=cyan>[PhotonSync] OnJoinedRoom called. Room: {PhotonNetwork.CurrentRoom.Name}</color>");
        
        // Check for existing Data Folder in Room Properties (For Late Joiners)
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("CurrentDataFolder", out object folderNameObj))
        {
            string folderName = (string)folderNameObj;
            Debug.Log($"<color=green>[PhotonSync] Late Join: Found active DataFolder in Room Properties: {folderName}</color>");
            
            // Start coroutine to wait for Manager initialization, then load folder
            StartCoroutine(LoadDataFolderWhenReady(folderName));
        }
        else
        {
            Debug.LogWarning("<color=yellow>[PhotonSync] Late Join: No DataFolder found in Room Properties</color>");
        }
        
        // 로딩 완료 후 스냅샷 요청/전송
        StartCoroutine(RequestSnapshotAfterLoadingComplete());
    }
    
    private IEnumerator LoadDataFolderWhenReady(string folderName)
    {
        Debug.Log($"<color=cyan>[PhotonSync] Waiting for Manager to initialize before loading: {folderName}</color>");
        
        // Wait for Manager to be ready
        while (Manager.Instance == null)
        {
            yield return new WaitForSeconds(0.5f);
        }
        
        manager = Manager.Instance;
        Debug.Log($"<color=green>[PhotonSync] Manager ready! Loading data folder: {folderName}</color>");
        
        // Load the data folder
        yield return manager.ChangeDataFolderCoroutine(folderName);
        
        Debug.Log($"<color=green>[PhotonSync] Data folder loaded successfully: {folderName}</color>");
    }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        base.OnRoomPropertiesUpdate(propertiesThatChanged);
        
        if (propertiesThatChanged.ContainsKey("CurrentDataFolder"))
        {
            EnsureRefs();
            string newFolder = (string)propertiesThatChanged["CurrentDataFolder"];
            Debug.Log($"<color=green>[PhotonSync] Room Property Update: CurrentDataFolder -> {newFolder}</color>");
            
            if (manager != null)
            {
                // Only change if different (ChangeDataFolderCoroutine handles checks, but good to check here)
                if (manager.currentDataFolder != newFolder)
                {
                    manager.StartCoroutine(manager.ChangeDataFolderCoroutine(newFolder));
                    
                     // Close menus (Remote Sync behavior)
                    var buttonController = FindObjectOfType<ButtonControllerManager>();
                    if (buttonController != null)
                    {
                        if (buttonController.folderSelectorMenu != null) buttonController.folderSelectorMenu.SetActive(false);
                        if (buttonController.settingsMenu != null) buttonController.settingsMenu.SetActive(false);
                        if (buttonController.mainMenu != null) buttonController.mainMenu.SetActive(false);
                    }
                    var folderUI = FindObjectOfType<FolderSelectorUI>();
                    if (folderUI != null) folderUI.HideFolderSelector();
                }
            }
        }
    }
    /// <summary>
    /// 모든 로더가 완료될 때까지 대기한 후 스냅샷 요청
    /// </summary>
    private IEnumerator RequestSnapshotAfterLoadingComplete()
    {
        Debug.Log("<color=cyan>[PhotonSync] Waiting for all loaders to complete...</color>");
        
        // Manager가 준비될 때까지 대기
        while (Manager.Instance == null)
        {
            yield return new WaitForSeconds(0.5f);
        }
        
        // 모든 로더가 완료될 때까지 대기
        float timeout = 60f;
        float elapsed = 0f;
        
        while (elapsed < timeout)
        {
            bool allLoaded = Manager.Instance.AreAllDataLoadersReady();
            if (allLoaded)
            {
                Debug.Log("<color=green>[PhotonSync] All loaders complete. Requesting/Sending snapshot...</color>");
                break;
            }
            
            yield return new WaitForSeconds(0.5f);
            elapsed += 0.5f;
        }
        
        if (elapsed >= timeout)
        {
            Debug.LogWarning("<color=yellow>[PhotonSync] Loader timeout. Proceeding with snapshot anyway.</color>");
        }
        
        // 스냅샷 요청/전송
        if (PhotonNetwork.IsMasterClient)
        {
            SendSnapshot(PhotonNetwork.LocalPlayer.ActorNumber);
        }
        else
        {
            PhotonNetwork.RaiseEvent(
                SnapshotRequestEvent,
                null,
                new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient },
                SendOptions.SendReliable
            );
        }
    }

    void SendSnapshot(int targetActor)
    {
        EnsureRefs();

        Transform parent = manager != null ? manager.ObjectParent?.transform : null;
        Transform sliceTransform = sliceVisualization != null ? sliceVisualization.transform : null;

        ButtonControllerManager.ButtonAction? activeAction = buttonManager != null ? buttonManager.GetActiveMenuAction() : null;
        int actionCode = activeAction.HasValue ? (int)activeAction.Value : -1;

        object[] snapshot = new object[]
        {
            actionCode,
            sliderController != null ? sliderController.GetValue() : 0f,
            sliderController != null ? (int)sliderController.mode : (int)ControlMode.SlicePosition,
            sliderController != null ? (int)sliderController.GetResolvedAxis() : (int)SliceController.SliceAxis.None,
            sliderController != null && sliderController.IsActive,
            sliceVisualization != null ? (int)sliceVisualization.currentAxis : (int)SliceController.SliceAxis.None,
            sliceVisualization != null ? sliceVisualization.slicePositionX : 0f,
            sliceVisualization != null ? sliceVisualization.slicePositionY : 0f,
            sliceVisualization != null && sliceVisualization.show2DHeatmap,
            sliceVisualization != null && sliceVisualization.show3DArrows,
            // ObjectParent sync (indices 10, 11, 12)
            parent != null ? parent.position : Vector3.zero,
            parent != null ? parent.rotation : Quaternion.identity,
            parent != null ? parent.localScale : Vector3.one,
            sliceTransform != null ? sliceTransform.position : Vector3.zero,
            sliceTransform != null ? sliceTransform.rotation : Quaternion.identity,
            sliceTransform != null ? sliceTransform.localScale : Vector3.one,
            // ColorBar sync (indices 16, 17, 18, 19)
            manager != null && manager.velocityColorBar != null ? manager.velocityColorBar.transform.position : Vector3.zero,
            manager != null && manager.velocityColorBar != null ? manager.velocityColorBar.transform.rotation : Quaternion.identity,
            manager != null && manager.wssColorBar != null ? manager.wssColorBar.transform.position : Vector3.zero,
            manager != null && manager.wssColorBar != null ? manager.wssColorBar.transform.rotation : Quaternion.identity,
            // SlicePlane sync (indices 20, 21)
            FindObjectOfType<SlicePlaneController>()?.slicePlane != null ? FindObjectOfType<SlicePlaneController>().slicePlane.transform.position : Vector3.zero,
            FindObjectOfType<SlicePlaneController>()?.slicePlane != null ? FindObjectOfType<SlicePlaneController>().slicePlane.transform.rotation : Quaternion.identity,
            // SliceIndicator sync (indices 22, 23, 24)
            FindObjectOfType<SliceIndicatorController>()?.indicatorCube != null ? FindObjectOfType<SliceIndicatorController>().indicatorCube.transform.position : Vector3.zero,
            FindObjectOfType<SliceIndicatorController>()?.indicatorCube != null ? FindObjectOfType<SliceIndicatorController>().indicatorCube.transform.rotation : Quaternion.identity,
            FindObjectOfType<SliceIndicatorController>()?.indicatorCube != null ? FindObjectOfType<SliceIndicatorController>().indicatorCube.transform.localScale : Vector3.one,
            // Visualization mode sync (indices 25, 26)
            manager != null ? (int)manager.visualizationMode : 0,
            manager != null ? (int)manager.wssSubMode : 0,
            // UI Menus sync (indices 27-32)
            buttonManager != null && buttonManager.playSettingMenu != null ? buttonManager.playSettingMenu.transform.position : Vector3.zero,
            buttonManager != null && buttonManager.playSettingMenu != null ? buttonManager.playSettingMenu.transform.rotation : Quaternion.identity,
            buttonManager != null && buttonManager.measurementSettingUI != null ? buttonManager.measurementSettingUI.transform.position : Vector3.zero,
            buttonManager != null && buttonManager.measurementSettingUI != null ? buttonManager.measurementSettingUI.transform.rotation : Quaternion.identity,
            buttonManager != null && buttonManager.folderSelectorMenu != null ? buttonManager.folderSelectorMenu.transform.position : Vector3.zero,
            buttonManager != null && buttonManager.folderSelectorMenu != null ? buttonManager.folderSelectorMenu.transform.rotation : Quaternion.identity,
            // Additional Menus (indices 33-48)
            buttonManager != null && buttonManager.mainMenu != null ? buttonManager.mainMenu.transform.position : Vector3.zero,
            buttonManager != null && buttonManager.mainMenu != null ? buttonManager.mainMenu.transform.rotation : Quaternion.identity,
            buttonManager != null && buttonManager.streamlineMenu != null ? buttonManager.streamlineMenu.transform.position : Vector3.zero,
            buttonManager != null && buttonManager.streamlineMenu != null ? buttonManager.streamlineMenu.transform.rotation : Quaternion.identity,
            buttonManager != null && buttonManager.streamlineSpeedMenu != null ? buttonManager.streamlineSpeedMenu.transform.position : Vector3.zero,
            buttonManager != null && buttonManager.streamlineSpeedMenu != null ? buttonManager.streamlineSpeedMenu.transform.rotation : Quaternion.identity,
            buttonManager != null && buttonManager.velocityMenu != null ? buttonManager.velocityMenu.transform.position : Vector3.zero,
            buttonManager != null && buttonManager.velocityMenu != null ? buttonManager.velocityMenu.transform.rotation : Quaternion.identity,
            buttonManager != null && buttonManager.wssMenu != null ? buttonManager.wssMenu.transform.position : Vector3.zero,
            buttonManager != null && buttonManager.wssMenu != null ? buttonManager.wssMenu.transform.rotation : Quaternion.identity,
            buttonManager != null && buttonManager.visualizationSettingMenu != null ? buttonManager.visualizationSettingMenu.transform.position : Vector3.zero,
            buttonManager != null && buttonManager.visualizationSettingMenu != null ? buttonManager.visualizationSettingMenu.transform.rotation : Quaternion.identity,
            buttonManager != null && buttonManager.velocityVisualizationSettingMenu != null ? buttonManager.velocityVisualizationSettingMenu.transform.position : Vector3.zero,
            buttonManager != null && buttonManager.velocityVisualizationSettingMenu != null ? buttonManager.velocityVisualizationSettingMenu.transform.rotation : Quaternion.identity,
            buttonManager != null && buttonManager.settingsMenu != null ? buttonManager.settingsMenu.transform.position : Vector3.zero,
            buttonManager != null && buttonManager.settingsMenu != null ? buttonManager.settingsMenu.transform.rotation : Quaternion.identity,
            
            // UI Menu Active States (indices 49-50)
            buttonManager != null && buttonManager.playSettingMenu != null && buttonManager.playSettingMenu.activeSelf,
            buttonManager != null && buttonManager.measurementSettingUI != null && buttonManager.measurementSettingUI.activeSelf,
            
            // Frame Control Mode (index 51)
            buttonManager != null && buttonManager.isFrameControlMode,
            
            // Measurement Tool State (indices 52-56)
            measurementTool != null && measurementTool.enableMeasurement,
            measurementTool != null && measurementTool.objectMoveMode,
            Vector3.zero, // marker positions not directly accessible
            Vector3.zero,
            
            // ColorBar Visibility (indices 56-57)
            manager != null && manager.velocityColorBar != null && manager.velocityColorBar.gameObject.activeSelf,
            manager != null && manager.wssColorBar != null && manager.wssColorBar.gameObject.activeSelf,
            
            // Density Control (indices 58-60)
            manager != null && manager.velocityLoader != null ? manager.velocityLoader.displayStepX : 1,
            manager != null && manager.velocityLoader != null ? manager.velocityLoader.displayStepY : 1,
            manager != null && manager.velocityLoader != null ? manager.velocityLoader.displayStepZ : 1,
            
            // JSON Visualization Settings - Manager (indices 61-63)
            manager != null ? manager.bloodAlpha : 0.35f,
            0.1f, // globalVisualizationScale - not in Manager
            1f, // calibrationFactor - not in Manager
            
            // VelocityLoader Settings (indices 64-69)
            manager != null && manager.velocityLoader != null ? manager.velocityLoader.stepX : 5,
            manager != null && manager.velocityLoader != null ? manager.velocityLoader.stepY : 5,
            manager != null && manager.velocityLoader != null ? manager.velocityLoader.stepZ : 5,
            manager != null && manager.velocityLoader != null ? manager.velocityLoader.velocityScaleFactor : 0.001f,
            manager != null && manager.velocityLoader != null ? manager.velocityLoader.arrowScale : 0.003f,
            
            // WSS Settings (indices 70-75)
            manager != null && manager.wssLoader != null ? manager.wssLoader.arrowScale : 0.031f,
            manager != null && manager.wssLoader != null ? manager.wssLoader.arrowLengthMultiplier : 0.08f,
            manager != null && manager.wssLoader != null ? manager.wssLoader.stepX : 2,
            manager != null && manager.wssLoader != null ? manager.wssLoader.stepY : 2,
            manager != null && manager.wssLoader != null ? manager.wssLoader.stepZ : 2,
            
            // Streamline Settings (index 76)
            manager != null && manager.streamlineLoader != null ? manager.streamlineLoader.lineWidth : 0.001f,
            
            // SliceVisualization Settings (indices 77-85)
            sliceVisualization != null && sliceVisualization.viewRenderer != null ? sliceVisualization.viewRenderer.heatmapResolution : 85,
            sliceVisualization != null && sliceVisualization.viewRenderer != null ? sliceVisualization.viewRenderer.heatmapIntensity : 3.0f,
            sliceVisualization != null && sliceVisualization.viewRenderer != null ? sliceVisualization.viewRenderer.heatmapAlpha : 1.0f,
            sliceVisualization != null && sliceVisualization.viewRenderer != null ? sliceVisualization.viewRenderer.heatmapSpotSize : 0.0384f,
            sliceVisualization != null && sliceVisualization.viewRenderer != null ? sliceVisualization.viewRenderer.arrowPlaneScale : 0.001f,
            sliceVisualization != null && sliceVisualization.viewRenderer != null ? sliceVisualization.viewRenderer.arrowScale : 0.001f,
            sliceVisualization != null && sliceVisualization.viewRenderer != null ? sliceVisualization.viewRenderer.velocityScaleFactor : 0.0015f,
            sliceVisualization != null && sliceVisualization.viewRenderer != null ? sliceVisualization.viewRenderer.targetPhysicalSize : 0.4f,
            sliceVisualization != null && sliceVisualization.viewRenderer != null ? sliceVisualization.viewRenderer.additionalRotation : Vector3.zero
        };

        PhotonNetwork.RaiseEvent(
            SnapshotEvent,
            snapshot,
            new RaiseEventOptions { TargetActors = new int[] { targetActor } },
            SendOptions.SendReliable
        );
    }

    void ApplySnapshot(object[] snapshot)
    {
        EnsureRefs();
        if (snapshot == null || snapshot.Length < 16) return;

        int menuAction = Convert.ToInt32(snapshot[0]);
        if (menuAction >= 0 && buttonManager != null)
        {
            buttonManager.RunAction((ButtonControllerManager.ButtonAction)menuAction, false, networkCall: true);
        }

        float sliderValue = Convert.ToSingle(snapshot[1]);
            var sliderMode = (ControlMode)Convert.ToInt32(snapshot[2]);
        var sliderAxis = (SliceController.SliceAxis)Convert.ToInt32(snapshot[3]);
        bool sliderActive = Convert.ToBoolean(snapshot[4]);

        if (sliderController != null)
        {
            sliderController.ApplyNetworkSlider(sliderValue, sliderMode, sliderAxis);
            sliderController.SetSliderActive(sliderActive);
        }

        if (sliceVisualization != null)
        {
            sliceVisualization.currentAxis = (SliceController.SliceAxis)Convert.ToInt32(snapshot[5]);
            sliceVisualization.SetSlicePositionForAxis(SliceController.SliceAxis.X_Axis, Convert.ToSingle(snapshot[6]), updateVisualization: true, networkCall: true);
            sliceVisualization.SetSlicePositionForAxis(SliceController.SliceAxis.Y_Axis, Convert.ToSingle(snapshot[7]), updateVisualization: true, networkCall: true);
            sliceVisualization.show2DHeatmap = Convert.ToBoolean(snapshot[8]);
            sliceVisualization.show3DArrows = Convert.ToBoolean(snapshot[9]);
        }

        ApplyRemoteTransform(TransformTarget.ObjectParent, (Vector3)snapshot[10], (Quaternion)snapshot[11], (Vector3)snapshot[12]);
        ApplyRemoteTransform(TransformTarget.SliceVisualization, (Vector3)snapshot[13], (Quaternion)snapshot[14], (Vector3)snapshot[15]);
        
        // Apply ColorBar transforms if present (indices 16-19)
        if (snapshot.Length >= 22)
        {
            ApplyRemoteTransform(TransformTarget.VelColorBar, (Vector3)snapshot[16], (Quaternion)snapshot[17], Vector3.one);
            ApplyRemoteTransform(TransformTarget.WssColorBar, (Vector3)snapshot[18], (Quaternion)snapshot[19], Vector3.one);
            
            if (snapshot.Length >= 24)
            {
                ApplyRemoteTransform(TransformTarget.SlicePlane, (Vector3)snapshot[20], (Quaternion)snapshot[21], Vector3.one);
                
                if (snapshot.Length >= 27)
                {
                    ApplyRemoteTransform(TransformTarget.SliceIndicator, (Vector3)snapshot[22], (Quaternion)snapshot[23], (Vector3)snapshot[24]);
                }
            }
        }

        // Apply visualization mode if present (new clients may not have these fields)
        if (snapshot.Length >= 22 && manager != null)
        {
            int vizIdx = snapshot.Length >= 27 ? 25 : (snapshot.Length >= 24 ? 22 : 20);
            int wssIdx = snapshot.Length >= 27 ? 26 : (snapshot.Length >= 24 ? 23 : 21);
            
            var vizMode = (VisualizationMode)Convert.ToInt32(snapshot[vizIdx]);
            var wssMode = (WSSSubMode)Convert.ToInt32(snapshot[wssIdx]);
            
            manager.visualizationMode = vizMode;
            manager.wssSubMode = wssMode;
            manager.ApplyVisualizationMode(networkCall: true);
            
            Debug.Log($"<color=green>[PhotonSync] Applied snapshot visualization: {vizMode}, WSS: {wssMode}</color>");
        }

        // Apply UI Menu transforms if present (indices 27-32)
        if (snapshot.Length >= 33)
        {
            ApplyRemoteTransform(TransformTarget.PlaySettingMenu, (Vector3)snapshot[27], (Quaternion)snapshot[28], Vector3.one);
            ApplyRemoteTransform(TransformTarget.MeasurementMenu, (Vector3)snapshot[29], (Quaternion)snapshot[30], Vector3.one);
            ApplyRemoteTransform(TransformTarget.FolderSelectorMenu, (Vector3)snapshot[31], (Quaternion)snapshot[32], Vector3.one);
        }
        
        // Apply remaining UI Menus (indices 33-48)
        if (snapshot.Length >= 49)
        {
            ApplyRemoteTransform(TransformTarget.MainMenu, (Vector3)snapshot[33], (Quaternion)snapshot[34], Vector3.one);
            ApplyRemoteTransform(TransformTarget.StreamlineMenu, (Vector3)snapshot[35], (Quaternion)snapshot[36], Vector3.one);
            ApplyRemoteTransform(TransformTarget.StreamlineSpeedMenu, (Vector3)snapshot[37], (Quaternion)snapshot[38], Vector3.one);
            ApplyRemoteTransform(TransformTarget.VelocityMenu, (Vector3)snapshot[39], (Quaternion)snapshot[40], Vector3.one);
            ApplyRemoteTransform(TransformTarget.WssMenu, (Vector3)snapshot[41], (Quaternion)snapshot[42], Vector3.one);
            ApplyRemoteTransform(TransformTarget.VisSettingMenu, (Vector3)snapshot[43], (Quaternion)snapshot[44], Vector3.one);
            ApplyRemoteTransform(TransformTarget.VelVisSettingMenu, (Vector3)snapshot[45], (Quaternion)snapshot[46], Vector3.one);
            ApplyRemoteTransform(TransformTarget.SettingsMenu, (Vector3)snapshot[47], (Quaternion)snapshot[48], Vector3.one);
        }
        
        // Apply UI Menu Active States (indices 49-50)
        if (snapshot.Length >= 51 && buttonManager != null)
        {
            bool playSettingActive = Convert.ToBoolean(snapshot[49]);
            bool measurementActive = Convert.ToBoolean(snapshot[50]);
            
            if (buttonManager.playSettingMenu != null)
                buttonManager.playSettingMenu.SetActive(playSettingActive);
            if (buttonManager.measurementSettingUI != null)
                buttonManager.measurementSettingUI.SetActive(measurementActive);
        }
        
        // Apply Frame Control Mode (index 51)
        if (snapshot.Length >= 52 && buttonManager != null)
        {
            bool frameControlMode = Convert.ToBoolean(snapshot[51]);
            buttonManager.ApplyNetworkFrameControl(frameControlMode, -1);
        }
        
        // Apply Measurement Tool State (indices 52-55)
        if (snapshot.Length >= 56 && measurementTool != null)
        {
            bool enableMeasurement = Convert.ToBoolean(snapshot[52]);
            bool objectMoveMode = Convert.ToBoolean(snapshot[53]);
            Vector3 marker1Pos = (Vector3)snapshot[54];
            Vector3 marker2Pos = (Vector3)snapshot[55];
            
            measurementTool.enableMeasurement = enableMeasurement;
            measurementTool.objectMoveMode = objectMoveMode;
            // Marker positions not directly settable
        }
        
        // Apply ColorBar Visibility (indices 56-57)
        if (snapshot.Length >= 58 && manager != null)
        {
            bool velColorBarActive = Convert.ToBoolean(snapshot[56]);
            bool wssColorBarActive = Convert.ToBoolean(snapshot[57]);
            
            if (manager.velocityColorBar != null)
                manager.velocityColorBar.gameObject.SetActive(velColorBarActive);
            if (manager.wssColorBar != null)
                manager.wssColorBar.gameObject.SetActive(wssColorBarActive);
        }
        
        // Apply Density Control (indices 58-60)
        if (snapshot.Length >= 61 && manager != null && manager.velocityLoader != null)
        {
            int displayStepX = Convert.ToInt32(snapshot[58]);
            int displayStepY = Convert.ToInt32(snapshot[59]);
            int displayStepZ = Convert.ToInt32(snapshot[60]);
            
            manager.velocityLoader.SetDisplayStepX(displayStepX);
            manager.velocityLoader.SetDisplayStepY(displayStepY);
            manager.velocityLoader.SetDisplayStepZ(displayStepZ);
        }
        
        // Apply Manager Settings (indices 61-63)
        if (snapshot.Length >= 64 && manager != null)
        {
            manager.bloodAlpha = Convert.ToSingle(snapshot[61]);
            // globalVisualizationScale and calibrationFactor not in Manager
        }
        
        // Apply VelocityLoader Settings (indices 64-68)
        if (snapshot.Length >= 69 && manager != null && manager.velocityLoader != null)
        {
            manager.velocityLoader.stepX = Convert.ToInt32(snapshot[64]);
            manager.velocityLoader.stepY = Convert.ToInt32(snapshot[65]);
            manager.velocityLoader.stepZ = Convert.ToInt32(snapshot[66]);
            manager.velocityLoader.velocityScaleFactor = Convert.ToSingle(snapshot[67]);
            manager.velocityLoader.arrowScale = Convert.ToSingle(snapshot[68]);
        }
        
        // Apply WSS Settings (indices 69-74)
        if (snapshot.Length >= 75 && manager != null && manager.wssLoader != null)
        {
            manager.wssLoader.arrowScale = Convert.ToSingle(snapshot[69]);
            manager.wssLoader.arrowLengthMultiplier = Convert.ToSingle(snapshot[70]);
            manager.wssLoader.stepX = Convert.ToInt32(snapshot[71]);
            manager.wssLoader.stepY = Convert.ToInt32(snapshot[72]);
            manager.wssLoader.stepZ = Convert.ToInt32(snapshot[73]);
        }
        
        // Apply Streamline Settings (index 74)
        if (snapshot.Length >= 77 && manager != null && manager.streamlineLoader != null)
        {
            manager.streamlineLoader.lineWidth = Convert.ToSingle(snapshot[76]);
        }
        
        // Apply SliceVisualization Settings (indices 77-85)
        if (snapshot.Length >= 86 && sliceVisualization != null)
        {
            if (sliceVisualization.viewRenderer != null)
            {
                sliceVisualization.viewRenderer.heatmapResolution = Convert.ToInt32(snapshot[77]);
                sliceVisualization.viewRenderer.heatmapIntensity = Convert.ToSingle(snapshot[78]);
                sliceVisualization.viewRenderer.heatmapAlpha = Convert.ToSingle(snapshot[79]);
                sliceVisualization.viewRenderer.heatmapSpotSize = Convert.ToSingle(snapshot[80]);
                sliceVisualization.viewRenderer.arrowPlaneScale = Convert.ToSingle(snapshot[81]);
                sliceVisualization.viewRenderer.arrowScale = Convert.ToSingle(snapshot[82]);
                sliceVisualization.viewRenderer.velocityScaleFactor = Convert.ToSingle(snapshot[83]);
                sliceVisualization.viewRenderer.targetPhysicalSize = Convert.ToSingle(snapshot[84]);
                sliceVisualization.viewRenderer.additionalRotation = (Vector3)snapshot[85];
            }
        }
    }

    public void OnEvent(EventData photonEvent)
    {
        switch (photonEvent.Code)
        {
            case ButtonActionEvent:
                EnsureRefs();
                if (buttonManager != null)
                {
                    buttonManager.RunAction((ButtonControllerManager.ButtonAction)Convert.ToInt32(photonEvent.CustomData), false, networkCall: true);
                }
                break;
            case SliderValueEvent:
                EnsureRefs();
                if (sliderController != null && photonEvent.CustomData is object[] sliderData && sliderData.Length >= 4)
                {
                    float value = Convert.ToSingle(sliderData[0]);
            var mode = (ControlMode)Convert.ToInt32(sliderData[1]);
                    var axis = (SliceController.SliceAxis)Convert.ToInt32(sliderData[2]);
                    bool active = Convert.ToBoolean(sliderData[3]);
                    sliderController.ApplyNetworkSlider(value, mode, axis);
                    // Only force-activate; avoid remote false turning off a local slider unexpectedly.
                    if (active)
                    {
                        sliderController.SetSliderActive(true);
                    }
                }
                break;
            case SnapshotRequestEvent:
                if (PhotonNetwork.IsMasterClient)
                {
                    SendSnapshot(photonEvent.Sender);
                }
                break;
            case SnapshotEvent:
                ApplySnapshot(photonEvent.CustomData as object[]);
                break;
            case TransformSyncEvent:
                EnsureRefs();
                if (photonEvent.CustomData is object[] transformData && transformData.Length >= 4)
                {
                    var target = (TransformTarget)(byte)transformData[0];
                    var pos = (Vector3)transformData[1];
                    var rot = (Quaternion)transformData[2];
                    var scale = (Vector3)transformData[3];
                    ApplyRemoteTransform(target, pos, rot, scale);
                }
                break;
            case LockSyncEvent:
                if (photonEvent.CustomData is object[] lockData && lockData.Length >= 2)
                {
                    int ownerActorNr = (int)lockData[0];
                    LockType type = (LockType)(int)lockData[1];
                    Debug.Log($"<color=green>[PhotonSync] Received LockSyncEvent: Owner={ownerActorNr}, Type={type}</color>");
                    
                    // Update local state
                    globalLockOwner = ownerActorNr;
                    currentLockType = type;
                    
                    // If locked by someone else -> Disable UI
                    if (ownerActorNr != 0 && ownerActorNr != PhotonNetwork.LocalPlayer.ActorNumber)
                    {
                        DisableAllUI();
                    }
                    else
                    {
                        EnableAllUI();
                    }
                }
                break;
            case MeasurementSyncEvent:
                EnsureRefs();
                if (measurementTool != null && photonEvent.CustomData is object[] measureData && measureData.Length >= 4)
                {
                    Vector3 marker1Pos = (Vector3)measureData[0];
                    Vector3 marker2Pos = (Vector3)measureData[1];
                    bool isEnabled = (bool)measureData[2];
                    bool isInteracting = (bool)measureData[3];
                    measurementTool.ApplyNetworkMeasurement(marker1Pos, marker2Pos, isEnabled, isInteracting);
                }
                break;
            case DataFolderSyncEvent:
                EnsureRefs();
                if (manager != null && photonEvent.CustomData is string folderName)
                {
                    Debug.Log($"<color=green>[PhotonSync] Received DataFolder change: {folderName}</color>");
                    manager.StartCoroutine(manager.ChangeDataFolderCoroutine(folderName, networkCall: true));
                    
                    // Close folder selector and main menu on remote clients
                    var buttonController = FindObjectOfType<ButtonControllerManager>();
                    if (buttonController != null)
                    {
                        // Close Folder Selector Menu
                        if (buttonController.folderSelectorMenu != null)
                            buttonController.folderSelectorMenu.SetActive(false);
                        
                        // Close Settings Menu
                        if (buttonController.settingsMenu != null)
                            buttonController.settingsMenu.SetActive(false);
                        
                        // Close Main Menu
                        if (buttonController.mainMenu != null)
                            buttonController.mainMenu.SetActive(false);
                        
                        Debug.Log("<color=cyan>[PhotonSync] Closed menus after folder change</color>");
                    }
                    
                    // Also hide FolderSelectorUI panel
                    var folderUI = FindObjectOfType<FolderSelectorUI>();
                    if (folderUI != null)
                    {
                        folderUI.HideFolderSelector();
                    }
                }
                break;
            case WSSSubModeSyncEvent:
                EnsureRefs();
                if (manager != null)
                {
                    int subMode = Convert.ToInt32(photonEvent.CustomData);
                    Debug.Log($"<color=green>[PhotonSync] Received WSSSubMode: {(WSSSubMode)subMode}</color>");
                    manager.wssSubMode = (WSSSubMode)subMode;
                    // 현재 WSS 모드일 때만 적용
                    if (manager.visualizationMode == VisualizationMode.WSS)
                    {
                        manager.ApplyWSSSubMode();
                    }
                }
                break;
            case PlaybackIndexSyncEvent:
                EnsureRefs();
                if (manager != null && photonEvent.CustomData is object[] playbackData && playbackData.Length >= 3)
                {
                    int velocityIdx = Convert.ToInt32(playbackData[0]);
                    int wssIdx = Convert.ToInt32(playbackData[1]);
                    int streamlineIdx = Convert.ToInt32(playbackData[2]);
                    
                    // Apply to loaders (they will display the correct frame)
                    if (manager.velocityLoader != null)
                        manager.velocityLoader.SetFrameIndex(velocityIdx);
                    if (manager.wssLoader != null)
                        manager.wssLoader.SetFrameIndex(wssIdx);
                    if (manager.streamlineLoader != null)
                        manager.streamlineLoader.SetFrameIndex(streamlineIdx);
                }
                break;
            case FrameControlSyncEvent:
                EnsureRefs();
                if (buttonManager != null && photonEvent.CustomData is object[] frameData && frameData.Length >= 2)
                {
                    bool frameControlMode = Convert.ToBoolean(frameData[0]);
                    int frameIndex = Convert.ToInt32(frameData[1]);
                    
                    // 프레임 컨트롤 모드 동기화
                    buttonManager.ApplyNetworkFrameControl(frameControlMode, frameIndex);
                }
                break;
            case VisualizationModeSyncEvent:
                EnsureRefs();
                if (manager != null)
                {
                    int vizMode = Convert.ToInt32(photonEvent.CustomData);
                    Debug.Log($"<color=green>[PhotonSync] Received VisualizationMode: {(VisualizationMode)vizMode}</color>");
                    manager.visualizationMode = (VisualizationMode)vizMode;
                    manager.ApplyVisualizationMode();
                }
                break;
        }
    }

    void SyncTransformIfChanged(Transform target, TransformTarget id, ref Vector3 lastPos, ref Quaternion lastRot, ref Vector3 lastScale)
    {
        if (target == null) return;

        // [USER_REQUEST] 2-second cooldown after receiving remote update to prevent echo
        // If we recently received an update for this target, don't send it back unless we are the authoritative mover
        if (lastReceiveTimestamps.TryGetValue(id, out float lastTime) && Time.time - lastTime < 2.0f)
        {
            // Skip sync unless I am the authoritative owner of the lock.
            // (Standard objects like ObjectParent/Slice use the GlobalLock during manipulation)
            if (globalLockOwner != PhotonNetwork.LocalPlayer.ActorNumber)
            {
                return;
            }
        }

        const float posEps = 0.0005f;
        const float rotEps = 0.1f;
        const float scaleEps = 0.0005f;

        bool changed =
            Vector3.Distance(target.position, lastPos) > posEps ||
            Quaternion.Angle(target.rotation, lastRot) > rotEps ||
            Vector3.Distance(target.localScale, lastScale) > scaleEps;

        if (!changed) return;

        lastPos = target.position;
        lastRot = target.rotation;
        lastScale = target.localScale;

        object[] payload = new object[] { (byte)id, target.position, target.rotation, target.localScale };
        PhotonNetwork.RaiseEvent(TransformSyncEvent, payload, ReliableToOthers(), SendOptions.SendUnreliable);
    }

    void ApplyRemoteTransform(TransformTarget target, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        if (isSyncSuppressed) return;
        lastReceiveTimestamps[target] = Time.time;

        switch (target)
        {
            case TransformTarget.ObjectParent:
                if (manager != null && manager.ObjectParent != null)
                {
                    var t = manager.ObjectParent.transform;
                    t.SetPositionAndRotation(position, rotation);
                    t.localScale = scale;
                    lastParentPos = position;
                    lastParentRot = rotation;
                    lastParentScale = scale;
                }
                break;
            case TransformTarget.SliceVisualization:
                if (sliceVisualization != null)
                {
                    var t = sliceVisualization.transform;
                    t.SetPositionAndRotation(position, rotation);
                    t.localScale = scale;
                    lastSlicePos = position;
                    lastSliceRot = rotation;
                    lastSliceScale = scale;
                }
                break;
            case TransformTarget.VelColorBar:
                if (manager != null && manager.velocityColorBar != null)
                {
                    var t = manager.velocityColorBar.transform;
                    t.SetPositionAndRotation(position, rotation);
                    lastVelColorBarPos = position;
                    lastVelColorBarRot = rotation;
                }
                break;
            case TransformTarget.WssColorBar:
                if (manager != null && manager.wssColorBar != null)
                {
                    var t = manager.wssColorBar.transform;
                    t.SetPositionAndRotation(position, rotation);
                    lastWssColorBarPos = position;
                    lastWssColorBarRot = rotation;
                }
                break;
            case TransformTarget.SlicePlane:
                var slicePlane = FindObjectOfType<SlicePlaneController>();
                if (slicePlane != null && slicePlane.slicePlane != null)
                {
                    var t = slicePlane.slicePlane.transform;
                    t.SetPositionAndRotation(position, rotation);
                    lastSlicePlanePos = position;
                    lastSlicePlaneRot = rotation;
                }
                break;
            case TransformTarget.SliceIndicator:
                var sliceIndicator = FindObjectOfType<SliceIndicatorController>();
                if (sliceIndicator != null && sliceIndicator.indicatorCube != null)
                {
                    var t = sliceIndicator.indicatorCube.transform;
                    t.SetPositionAndRotation(position, rotation);
                    t.localScale = scale;
                    lastSliceIndicatorPos = position;
                    lastSliceIndicatorRot = rotation;
                    lastSliceIndicatorScale = scale;
                }
                break;
            case TransformTarget.PlaySettingMenu:
                if (buttonManager != null && buttonManager.playSettingMenu != null)
                {
                    buttonManager.playSettingMenu.transform.SetPositionAndRotation(position, rotation);
                    lastPlaySettingMenuPos = position;
                    lastPlaySettingMenuRot = rotation;
                }
                break;
            case TransformTarget.MeasurementMenu:
                if (buttonManager != null && buttonManager.measurementSettingUI != null)
                {
                    buttonManager.measurementSettingUI.transform.SetPositionAndRotation(position, rotation);
                    lastMeasurementMenuPos = position;
                    lastMeasurementMenuRot = rotation;
                }
                break;
            case TransformTarget.FolderSelectorMenu:
                if (buttonManager != null && buttonManager.folderSelectorMenu != null)
                {
                    buttonManager.folderSelectorMenu.transform.SetPositionAndRotation(position, rotation);
                    lastFolderSelectorMenuPos = position;
                    lastFolderSelectorMenuRot = rotation;
                }
                break;
            case TransformTarget.MainMenu:
                if (buttonManager != null && buttonManager.mainMenu != null)
                {
                    buttonManager.mainMenu.transform.SetPositionAndRotation(position, rotation);
                    lastMainMenuPos = position;
                    lastMainMenuRot = rotation;
                }
                break;
            case TransformTarget.StreamlineMenu:
                if (buttonManager != null && buttonManager.streamlineMenu != null)
                {
                    buttonManager.streamlineMenu.transform.SetPositionAndRotation(position, rotation);
                    lastStreamlineMenuPos = position;
                    lastStreamlineMenuRot = rotation;
                }
                break;
            case TransformTarget.StreamlineSpeedMenu:
                if (buttonManager != null && buttonManager.streamlineSpeedMenu != null)
                {
                    buttonManager.streamlineSpeedMenu.transform.SetPositionAndRotation(position, rotation);
                    lastStreamlineSpeedMenuPos = position;
                    lastStreamlineSpeedMenuRot = rotation;
                }
                break;
            case TransformTarget.VelocityMenu:
                if (buttonManager != null && buttonManager.velocityMenu != null)
                {
                    buttonManager.velocityMenu.transform.SetPositionAndRotation(position, rotation);
                    lastVelocityMenuPos = position;
                    lastVelocityMenuRot = rotation;
                }
                break;
            case TransformTarget.WssMenu:
                if (buttonManager != null && buttonManager.wssMenu != null)
                {
                    buttonManager.wssMenu.transform.SetPositionAndRotation(position, rotation);
                    lastWssMenuPos = position;
                    lastWssMenuRot = rotation;
                }
                break;
            case TransformTarget.VisSettingMenu:
                if (buttonManager != null && buttonManager.visualizationSettingMenu != null)
                {
                    buttonManager.visualizationSettingMenu.transform.SetPositionAndRotation(position, rotation);
                    lastVisSettingMenuPos = position;
                    lastVisSettingMenuRot = rotation;
                }
                break;
            case TransformTarget.VelVisSettingMenu:
                if (buttonManager != null && buttonManager.velocityVisualizationSettingMenu != null)
                {
                    buttonManager.velocityVisualizationSettingMenu.transform.SetPositionAndRotation(position, rotation);
                    lastVelVisSettingMenuPos = position;
                    lastVelVisSettingMenuRot = rotation;
                }
                break;
            case TransformTarget.SettingsMenu:
                if (buttonManager != null && buttonManager.settingsMenu != null)
                {
                    buttonManager.settingsMenu.transform.SetPositionAndRotation(position, rotation);
                    lastSettingsMenuPos = position;
                    lastSettingsMenuRot = rotation;
                }
                break;
        }
    }

    private enum TransformTarget : byte
    {
        ObjectParent = 1,
        SliceVisualization = 2,
        VelColorBar = 3,
        WssColorBar = 4,
        SlicePlane = 5,
        SliceIndicator = 6,
        PlaySettingMenu = 7,
        MeasurementMenu = 8,
        FolderSelectorMenu = 9,
        MainMenu = 10,
        StreamlineMenu = 11,
        StreamlineSpeedMenu = 12,
        VelocityMenu = 13,
        WssMenu = 14,
        VisSettingMenu = 15,
        VelVisSettingMenu = 16,
        SettingsMenu = 17
    }

    RaiseEventOptions ReliableToOthers()
    {
        return new RaiseEventOptions { Receivers = ReceiverGroup.Others };
    }

    // ==================== Simple Global Lock (Event Based) ====================
    
    // 0 = Unlocked, Other = Locked
    private int globalLockOwner = 0; 
    public enum LockType : byte { None = 0, ObjectManipulation = 1, SliderControl = 2, ButtonAction = 3 }
    private LockType currentLockType = LockType.None;

    public bool IsGloballyLocked => globalLockOwner != 0;
    public bool IsLockedByMe => PhotonNetwork.InRoom && globalLockOwner == PhotonNetwork.LocalPlayer.ActorNumber;
    
    public bool RequestGlobalLock(LockType lockType)
    {
        if (!PhotonNetwork.InRoom) return true; // Single player always succeeds

        // If already locked by someone else, fail
        if (globalLockOwner != 0 && globalLockOwner != PhotonNetwork.LocalPlayer.ActorNumber)
        {
            Debug.LogWarning($"[Lock] Request Failed. Currently locked by {globalLockOwner}");
            return false;
        }

        // Optimistic Success
        globalLockOwner = PhotonNetwork.LocalPlayer.ActorNumber;
        currentLockType = lockType;

        // Broadcast Lock
        object[] payload = new object[] { globalLockOwner, (int)lockType };
        PhotonNetwork.RaiseEvent(LockSyncEvent, payload, ReliableToOthers(), SendOptions.SendReliable);

        //DisableAllUI();

        Debug.Log($"<color=cyan>[Lock] Request Success! Sent Event. Type: {lockType}</color>");
        return true;
    }

    public void ReleaseGlobalLock()
    {
        if (!PhotonNetwork.InRoom) return;
        
        // Only owner can release
        if (globalLockOwner == PhotonNetwork.LocalPlayer.ActorNumber)
        {
            globalLockOwner = 0;
            currentLockType = LockType.None;

            // Broadcast Unlock
            object[] payload = new object[] { 0, (int)LockType.None };
            
            //PhotonNetwork.RaiseEvent(LockSyncEvent, payload, ReliableToOthers(), SendOptions.SendReliable);
            PhotonNetwork.RaiseEvent(LockSyncEvent, payload, ReliableToOthers(), SendOptions.SendUnreliable);

            //EnableAllUI();

            Debug.Log("<color=green>[Lock] Release Success! Sent Event.</color>");
        }
    }

    void ShowLockNotification()
    {
        Debug.LogWarning("다른 사용자가 조작 중입니다.");
    }

    void DisableAllUI()
    {
        Debug.Log("<color=red>[Lock] DisableAllUI called (Hands Disabled)</color>");
        if (manager != null)
        {
            manager.SetGlobalInputLock(true);
        }
    }

    void EnableAllUI()
    {
        Debug.Log("<color=green>[Lock] EnableAllUI called (Hands Enabled)</color>");
        if (manager != null)
        {
            manager.SetGlobalInputLock(false);
        }
    }
}
