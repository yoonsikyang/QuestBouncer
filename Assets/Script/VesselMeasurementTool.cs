using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.UI;

/// <summary>
/// 혈관 크기 측정 도구
/// 두 개의 드래그 가능한 마커를 이용하여 실시간으로 거리를 측정합니다.
/// </summary>
public class VesselMeasurementTool : MonoBehaviour
{
    #region Inspector Fields
    [Header("Debug / Toggle")]
    [Tooltip("측정 마커 표시/숨김 - 활성화시 Object 조작 불가, 마커 조작 가능")]
    public bool enableMeasurement = false;
    
    [Tooltip("Object 이동 모드 - true: Object 조작 가능/마커 고정, false: Object 고정/마커 조작 가능")]
    public bool objectMoveMode = false;
    
    private bool previousEnableMeasurement = false;
    private bool previousObjectMoveMode = false;

    [Header("Measurement State")]
    [SerializeField] private bool isMeasurementComplete = false;
    [SerializeField] private float measuredDistance = 0f; 

    [Header("Settings")]
    [Tooltip("표면에 스냅 활성화 (드래그 종료 시)")]
    public bool enableSnapToSurface = true;
    [Tooltip("스냅 거리 (미터) - vertex 검색 범위")]
    public float snapDistance = 0.05f; // 5cm
    [Tooltip("초기 마커 간격 (Object Parent 기준)")]
    public float initialMarkerSpacing = 0.05f; // 5cm
    
    [Header("Coordinate Transform")]
    [Tooltip("Object Parent의 Transform (좌표 변환용)")]
    public Transform objectParentTransform;
    [Tooltip("Voxel Spacing (mm 단위)")]
    public Vector3 voxelSpacing = new Vector3(1f, 1f, 1f);
    
    [Header("Calibration")]
    [Tooltip("캘리브레이션 보정 계수")]
    public float calibrationFactor = 1.0f;

    [Header("References")]
    [Tooltip("Object Parent의 BoundingBox (측정 중 조작 차단용)")]
    public BoundingBox objectParentBoundingBox;
    [Tooltip("Object Parent의 ObjectManipulator (측정 중 조작 차단용)")]
    public ObjectManipulator objectParentManipulator;
    [Tooltip("혈관 메시 (Snap용)")]
    public GameObject bloodVesselMesh;

    [Header("Visual Markers")]
    [Tooltip("첫 번째 점 마커 색상")]
    public Color firstPointColor = Color.red;
    [Tooltip("두 번째 점 마커 색상")]
    public Color secondPointColor = Color.red;
    [Tooltip("측정 선 색상")]
    public Color lineColor = Color.yellow;
    [Tooltip("마커 기본 크기")]
    public float markerSize = 0.01f; // 1cm
    [Tooltip("Hover/조작 시 마커 크기 배율")]
    public float markerHoverScale = 1.5f; // 1.5배
    [Tooltip("마커 머티리얼 (설정 안하면 기본 Standard)")]
    public Material markerMaterial;
    [Tooltip("측정선 머티리얼 (설정 안하면 기본 Sprites/Default)")]
    public Material lineMaterial;
    
    [Header("Distance Label")]
    [Tooltip("거리 표시용 ToolTip (LabelOnlyTooltip)")]
    public Microsoft.MixedReality.Toolkit.UI.ToolTip distanceLabel;
    #endregion

    #region Private Fields
    private GameObject firstPointMarker;
    private GameObject secondPointMarker;
    private LineRenderer measurementLine;
    private bool wasBoundingBoxEnabled = true;
    private bool wasManipulatorEnabled = true;
    private bool isFirstMarkerDragging = false;
    private bool isSecondMarkerDragging = false;
    
    // 저장된 마커 위치 (재활성화 시 복원용)
    private Vector3? savedFirstMarkerPos = null;
    private Vector3? savedSecondMarkerPos = null;
    #endregion

    #region Properties
    public float MeasuredDistance => measuredDistance;
    public bool IsMeasurementComplete => isMeasurementComplete;
    public Vector3 FirstPoint => firstPointMarker != null ? firstPointMarker.transform.position : Vector3.zero;
    public Vector3 SecondPoint => secondPointMarker != null ? secondPointMarker.transform.position : Vector3.zero;
    #endregion

    #region Unity Lifecycle
    void Awake()
    {
        // objectParentTransform이 Start에서 초기화되므로 여기서는 아무것도 하지 않음
    }

    void Start()
    {
        // JSON 설정 파일에서 calibration factor 로드
        LoadCalibrationConfig();
        
        // Manager에서 참조 가져오기
        if (objectParentTransform == null && Manager.Instance != null)
        {
            objectParentTransform = Manager.Instance.ObjectParent?.transform;
        }

        if (bloodVesselMesh == null && Manager.Instance != null)
        {
            bloodVesselMesh = Manager.Instance.bloodVesselMesh;
        }

        // BoundingBox 자동 찾기
        if (objectParentBoundingBox == null && objectParentTransform != null)
        {
            objectParentBoundingBox = objectParentTransform.GetComponent<BoundingBox>();
        }
        
        // 시각적 마커 초기화 (objectParentTransform 설정 후)
        CreateDraggableMarkers();
        
        // ObjectManipulator 자동 찾기
        if (objectParentManipulator == null && objectParentTransform != null)
        {
            objectParentManipulator = objectParentTransform.GetComponent<ObjectManipulator>();
        }
        
        // 참조 상태 로그
        Debug.Log($"<color=cyan>[VesselMeasurement] References - BoundingBox: {(objectParentBoundingBox != null ? "Found" : "NULL")}, ObjectManipulator: {(objectParentManipulator != null ? "Found" : "NULL")}</color>");
        
        // 초기에는 마커 숨김
        HideMarkers();
    }
    
    /// <summary>
    /// visualization_settings.json에서 calibration 설정 로드
    /// </summary>
    /// <summary>
    /// visualization_settings.json에서 calibration 설정 로드 (Store 사용)
    /// </summary>
    private void LoadCalibrationConfig()
    {
        string folderPath = null;
        if (Manager.Instance != null)
        {
            folderPath = Manager.Instance.GetCurrentDataFolderPath();
        }

        var config = VisualizationSettingsStore.LoadSettings(folderPath);
        
        if (config.calibrationFactor > 0)
        {
            calibrationFactor = config.calibrationFactor;
            Debug.Log($"<color=green>[VesselMeasurement] Loaded calibrationFactor = {calibrationFactor} from visualization_settings.json</color>");
        }
    }
    
    /// <summary>
    /// 캘리브레이션 설정 다시 로드 (외부에서 호출 가능)
    /// </summary>
    public void ReloadCalibration()
    {
        LoadCalibrationConfig();
        Debug.Log($"<color=cyan>[VesselMeasurement] Calibration reloaded: {calibrationFactor}</color>");
    }

    void Update()
    {
        // 1. enableMeasurement 변경 감지: 마커 표시/숨김
        if (enableMeasurement != previousEnableMeasurement)
        {
            previousEnableMeasurement = enableMeasurement;
            if (enableMeasurement)
            {
                // 마커 표시
                ShowMarkers();
                
                // 현재 objectMoveMode 상태에 따라 조작 설정
                if (objectMoveMode)
                {
                    // Object 조작 가능, 마커 고정
                    SetObjectParentInteraction(true);
                    SetMarkerInteractionEnabled(false);
                }
                else
                {
                    // Object 고정, 마커 조작 가능
                    SetObjectParentInteraction(false);
                    SetMarkerInteractionEnabled(true);
                }
                previousObjectMoveMode = objectMoveMode; // 동기화
                
                // 저장된 위치가 있으면 복원, 없으면 초기 위치 설정
                if (savedFirstMarkerPos.HasValue && savedSecondMarkerPos.HasValue)
                {
                    firstPointMarker.transform.localPosition = savedFirstMarkerPos.Value;
                    secondPointMarker.transform.localPosition = savedSecondMarkerPos.Value;
                }
                else
                {
                    InitializeMarkerPositions(false); // Local call
                }
                
                // 거리 라벨 활성화
                if (distanceLabel != null)
                    distanceLabel.gameObject.SetActive(true);
                
                Debug.Log($"<color=green>[VesselMeasurement] Measurement ENABLED - objectMoveMode: {objectMoveMode}</color>");
            }
            else
            {
                // 마커 위치 저장 후 숨김, Object 조작 활성화
                if (firstPointMarker != null && secondPointMarker != null)
                {
                    savedFirstMarkerPos = firstPointMarker.transform.localPosition;
                    savedSecondMarkerPos = secondPointMarker.transform.localPosition;
                }
                
                HideMarkers();
                SetObjectParentInteraction(true);
                
                // 거리 라벨 비활성화
                if (distanceLabel != null)
                    distanceLabel.gameObject.SetActive(false);
                
                Debug.Log("<color=yellow>[VesselMeasurement] Measurement DISABLED - Markers hidden, Object unlocked</color>");
            }
            
            // Photon 브로드캐스트
            OnInteractionEnded();
        }
        
        // 2. objectMoveMode 변경 감지: Object/마커 조작 전환 (enableMeasurement가 true일 때만 유효)
        if (enableMeasurement && objectMoveMode != previousObjectMoveMode)
        {
            previousObjectMoveMode = objectMoveMode;
            if (objectMoveMode)
            {
                // Object 조작 가능, 마커 조작 불가
                SetObjectParentInteraction(true);
                SetMarkerInteractionEnabled(false);
                Debug.Log("<color=blue>[VesselMeasurement] Object Move Mode ON - Object movable, Markers fixed</color>");
            }
            else
            {
                // Object 조작 불가, 마커 조작 가능
                SetObjectParentInteraction(false);
                SetMarkerInteractionEnabled(true);
                Debug.Log("<color=magenta>[VesselMeasurement] Object Move Mode OFF - Object fixed, Markers movable</color>");
            }
        }
        
        // 3. 마커가 활성화되어 있으면 항상 라인 업데이트
        if (firstPointMarker != null && firstPointMarker.activeSelf && secondPointMarker != null)
        {
            UpdateMeasurement();
        }
    }

    void OnDestroy()
    {
        // 마커 정리
        if (firstPointMarker != null) Destroy(firstPointMarker);
        if (secondPointMarker != null) Destroy(secondPointMarker);
        if (measurementLine != null) Destroy(measurementLine.gameObject);
    }
    #endregion

    #region Public Methods (버튼 연결용)
    /// <summary>
    /// 측정 시작 - 마커 표시 및 초기 위치 설정
    /// </summary>
    public void StartMeasurement(bool networkCall = false)
    {
        Debug.Log("<color=cyan>[VesselMeasurement] StartMeasurement called</color>");
        
        enableMeasurement = true;
        previousEnableMeasurement = true;
        
        // Object Parent 조작 차단
        SetObjectParentInteraction(false);
        
        // 저장된 위치가 있으면 복원 (로컬 좌표), 없으면 초기 위치 설정
        if (savedFirstMarkerPos.HasValue && savedSecondMarkerPos.HasValue)
        {
            if (!networkCall)
            {
                firstPointMarker.transform.localPosition = savedFirstMarkerPos.Value;
                secondPointMarker.transform.localPosition = savedSecondMarkerPos.Value;
                Debug.Log("<color=cyan>[VesselMeasurement] Restored saved marker local positions</color>");
            }
        }
        else
        {
            InitializeMarkerPositions(networkCall);
        }
        
        // 마커 표시
        ShowMarkers();
        
        // 거리 라벨 활성화
        if (distanceLabel != null)
        {
            distanceLabel.gameObject.SetActive(true);
        }
        
        // Photon: 측정 상태 브로드캠스트
        OnInteractionEnded();
        
        Debug.Log("<color=green>[VesselMeasurement] Drag markers to measure distance</color>");
    }

    /// <summary>
    /// 측정 종료 - 마커 숨김
    /// </summary>
    public void StopMeasurement()
    {
        Debug.Log("<color=cyan>[VesselMeasurement] StopMeasurement called</color>");
        
        // 현재 마커 로컬 위치 저장 (재활성화 시 복원용 - Object Parent 기준)
        if (firstPointMarker != null && secondPointMarker != null)
        {
            savedFirstMarkerPos = firstPointMarker.transform.localPosition;
            savedSecondMarkerPos = secondPointMarker.transform.localPosition;
            Debug.Log("<color=cyan>[VesselMeasurement] Saved marker local positions</color>");
        }
        
        enableMeasurement = false;
        previousEnableMeasurement = false;
        
        // Object Parent 조작 복원
        SetObjectParentInteraction(true);
        
        // 마커 숨기기
        HideMarkers();
        
        // 거리 라벨 비활성화
        if (distanceLabel != null)
        {
            distanceLabel.gameObject.SetActive(false);
        }
        
        // Photon: 측정 상태 브로드캠스트
        OnInteractionEnded();
    }

    /// <summary>
    /// 측정 초기화 - 마커를 초기 위치로 복귀
    /// </summary>
    public void ResetMeasurement(bool networkCall = false)
    {
        Debug.Log("<color=cyan>[VesselMeasurement] ResetMeasurement called</color>");
        
        if (enableMeasurement)
        {
            InitializeMarkerPositions(networkCall);
        }
        
        measuredDistance = 0f;
    }

    /// <summary>
    /// Snap-to-Surface 토글
    /// </summary>
    public void ToggleSnapToSurface()
    {
        enableSnapToSurface = !enableSnapToSurface;
        Debug.Log($"<color=cyan>[VesselMeasurement] Snap-to-Surface: {enableSnapToSurface}</color>");
    }

    /// <summary>
    /// 측정 모드 토글 - Object Parent 조작만 전환 (마커는 유지)
    /// </summary>
    public void ToggleMeasurementMode()
    {
        if (enableMeasurement)
        {
            // 측정 모드 OFF: Object 조작 활성화 (마커는 유지)
            SetObjectParentInteraction(true);
            Debug.Log("<color=green>[VesselMeasurement] Switched to OBJECT MANIPULATION mode (markers remain)</color>");
        }
        else
        {
            // 측정 모드 ON: Object 조작 비활성화, 마커 표시
            SetObjectParentInteraction(false);
            if (!firstPointMarker.activeSelf)
            {
                InitializeMarkerPositions();
                ShowMarkers();
            }
            Debug.Log("<color=green>[VesselMeasurement] Switched to MEASUREMENT mode</color>");
        }
        
        enableMeasurement = !enableMeasurement;
        previousEnableMeasurement = enableMeasurement;
    }

    /// <summary>
    /// 마커와 라인을 초기 위치로 리셋
    /// 버튼에서 호출하여 마커를 처음 위치로 되돌림
    /// </summary>
    public void ResetMarkerPositions(bool networkCall = false)
    {
        Debug.Log("<color=cyan>[VesselMeasurement] ResetMarkerPositions called</color>");
        
        // 저장된 위치 초기화
        savedFirstMarkerPos = null;
        savedSecondMarkerPos = null;
        
        // 마커를 초기 위치로 이동
        InitializeMarkerPositions(networkCall);
        
        // 측정값 초기화
        measuredDistance = 0f;
        
        // 라인 업데이트
        if (measurementLine != null && firstPointMarker != null && secondPointMarker != null)
        {
            measurementLine.SetPosition(0, firstPointMarker.transform.position);
            measurementLine.SetPosition(1, secondPointMarker.transform.position);
        }
        
        // Photon 동기화
        OnInteractionEnded();
        
        Debug.Log("<color=green>[VesselMeasurement] Markers reset to initial positions</color>");
    }

    /// <summary>
    /// objectParentTransform 설정 및 마커 부모 재설정
    /// Manager에서 초기화 완료 후 호출
    /// </summary>
    public void SetObjectParent(Transform parent)
    {
        objectParentTransform = parent;
        
        if (objectParentTransform != null)
        {
            // 이미 생성된 마커들의 부모 재설정
            if (firstPointMarker != null)
            {
                firstPointMarker.transform.SetParent(objectParentTransform, false);
            }
            if (secondPointMarker != null)
            {
                secondPointMarker.transform.SetParent(objectParentTransform, false);
            }
            if (measurementLine != null)
            {
                measurementLine.transform.SetParent(objectParentTransform, false);
            }
            
            // BoundingBox, ObjectManipulator 재설정
            if (objectParentBoundingBox == null)
            {
                objectParentBoundingBox = objectParentTransform.GetComponent<BoundingBox>();
            }
            if (objectParentManipulator == null)
            {
                objectParentManipulator = objectParentTransform.GetComponent<ObjectManipulator>();
            }
            
            Debug.Log($"<color=green>[VesselMeasurement] SetObjectParent: Markers and Line reparented to {objectParentTransform.name}</color>");
        }
    }
    #endregion

    #region Private Methods
    private void CreateDraggableMarkers()
    {
        Debug.Log($"<color=cyan>[VesselMeasurement] CreateDraggableMarkers - objectParentTransform: {(objectParentTransform != null ? objectParentTransform.name : "NULL")}</color>");
        
        // 첫 번째 점 마커 생성
        firstPointMarker = CreateMarker("MeasurementMarker_First", firstPointColor);
        SetupMarkerInteraction(firstPointMarker, true);
        
        // 두 번째 점 마커 생성
        secondPointMarker = CreateMarker("MeasurementMarker_Second", secondPointColor);
        SetupMarkerInteraction(secondPointMarker, false);
        
        // 측정 선 생성
        GameObject lineObj = new GameObject("MeasurementLine");
        
        // Object Parent의 자식으로 설정 (함께 움직임)
        if (objectParentTransform != null)
        {
            firstPointMarker.transform.SetParent(objectParentTransform, false);
            secondPointMarker.transform.SetParent(objectParentTransform, false);
            lineObj.transform.SetParent(objectParentTransform, false);
            Debug.Log($"<color=green>[VesselMeasurement] Markers and Line parented to: {objectParentTransform.name}</color>");
        }
        else
        {
            Debug.LogWarning("[VesselMeasurement] objectParentTransform is NULL! Markers will be at root level.");
        }
        
        measurementLine = lineObj.AddComponent<LineRenderer>();
        measurementLine.startWidth = 0.002f;
        measurementLine.endWidth = 0.002f;
        
        // 머티리얼 설정 (Inspector에서 설정한 머티리얼 사용, 없으면 기본)
        if (lineMaterial != null)
        {
            measurementLine.material = new Material(lineMaterial);
        }
        else
        {
            measurementLine.material = new Material(Shader.Find("Sprites/Default"));
        }
        measurementLine.startColor = lineColor;
        measurementLine.endColor = lineColor;
        measurementLine.positionCount = 2;
        measurementLine.useWorldSpace = true; // 월드 좌표 사용
        measurementLine.enabled = false;
    }

    private GameObject CreateMarker(string name, Color color)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = name;
        marker.transform.localScale = Vector3.one * markerSize;
        
        // 머티리얼 설정 (Inspector에서 설정한 머티리얼 사용, 없으면 기본)
        Renderer renderer = marker.GetComponent<Renderer>();
        if (markerMaterial != null)
        {
            renderer.material = new Material(markerMaterial);
            renderer.material.color = color;
        }
        else
        {
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = color;
        }
        
        // Collider 유지 (드래그용)
        SphereCollider collider = marker.GetComponent<SphereCollider>();
        if (collider != null)
        {
            collider.isTrigger = false;
        }
        
        marker.SetActive(false);
        return marker;
    }

    private void SetupMarkerInteraction(GameObject marker, bool isFirstMarker)
    {
        // NearInteractionGrabbable 추가 (근거리 핀치 드래그용)
        var grabbable = marker.AddComponent<NearInteractionGrabbable>();
        
        // ObjectManipulator 추가 (드래그 가능하게)
        var manipulator = marker.AddComponent<ObjectManipulator>();
        manipulator.HostTransform = marker.transform;
        
        // 크기 조절 비활성화 - 이동만 가능하게
        var twoHandedProp = manipulator.GetType().GetProperty("TwoHandedManipulationType");
        if (twoHandedProp != null)
        {
            var transformFlags = System.Enum.ToObject(twoHandedProp.PropertyType, 1);
            twoHandedProp.SetValue(manipulator, transformFlags);
        }
        
        // 기본 크기 저장 (부모 스케일 보정)
        Vector3 parentScale = marker.transform.parent != null ? marker.transform.parent.lossyScale : Vector3.one;
        Vector3 normalScale = new Vector3(
            markerSize / parentScale.x,
            markerSize / parentScale.y,
            markerSize / parentScale.z
        );
        Vector3 hoverScale = normalScale * markerHoverScale;
        bool isHovering = false;
        bool isManipulating = false;
        
        // 초기 크기 설정
        marker.transform.localScale = normalScale;
        
        // Hover 이벤트 (Focus) - 손이 가까워지면 크게
        manipulator.OnHoverEntered.AddListener((eventData) => {
            isHovering = true;
            if (!isManipulating)
            {
                marker.transform.localScale = hoverScale;
            }
        });
        
        manipulator.OnHoverExited.AddListener((eventData) => {
            isHovering = false;
            if (!isManipulating)
            {
                marker.transform.localScale = normalScale;
            }
        });
        
        // 드래그 이벤트 연결
        manipulator.OnManipulationStarted.AddListener((eventData) => {
            isManipulating = true;
            marker.transform.localScale = hoverScale; // 조작 중 크게 유지
            
            if (isFirstMarker)
                isFirstMarkerDragging = true;
            else
                isSecondMarkerDragging = true;
            
            // Photon: Interaction Started 브로드캠스트
            OnInteractionStarted();
            
            Debug.Log($"<color=yellow>[VesselMeasurement] Marker drag started: {marker.name}</color>");
        });
        
        manipulator.OnManipulationEnded.AddListener((eventData) => {
            isManipulating = false;
            
            // Hover 상태가 아니면 원래 크기로
            if (!isHovering)
            {
                marker.transform.localScale = normalScale;
            }
            
            if (isFirstMarker)
                isFirstMarkerDragging = false;
            else
                isSecondMarkerDragging = false;
            
            // 드래그 종료 시 Snap-to-Surface 적용
            if (enableSnapToSurface)
            {
                Vector3 otherMarkerPos = isFirstMarker ? secondPointMarker.transform.position : firstPointMarker.transform.position;
                Vector3 snappedPos = SnapToNearestSurface(marker.transform.position, otherMarkerPos);
                marker.transform.position = snappedPos;
            }
            
            // Photon: Interaction Ended 브로드캠스트
            OnInteractionEnded();
            
            Debug.Log($"<color=yellow>[VesselMeasurement] Marker drag ended: {marker.name}</color>");
        });
    }

    private void InitializeMarkerPositions(bool networkCall = false)
    {
        if (networkCall) return; // Skip logic if triggered remotely
        
        if (objectParentTransform == null)
        {
            Debug.LogWarning("[VesselMeasurement] objectParentTransform is null, using world origin");
            firstPointMarker.transform.position = Vector3.left * initialMarkerSpacing;
            secondPointMarker.transform.position = Vector3.right * initialMarkerSpacing;
            return;
        }
        
        // Object Parent의 중심 위치와 X축 방향 사용
        Vector3 center = objectParentTransform.position;
        Vector3 rightDir = objectParentTransform.right;
        
        // 두 마커를 중심 기준으로 X축 방향에 배치
        firstPointMarker.transform.position = center - rightDir * initialMarkerSpacing;
        secondPointMarker.transform.position = center + rightDir * initialMarkerSpacing;
        
        Debug.Log($"<color=cyan>[VesselMeasurement] Markers initialized at X-axis: {firstPointMarker.transform.position} / {secondPointMarker.transform.position}</color>");
    }

    private void UpdateMeasurement()
    {
        if (firstPointMarker == null || secondPointMarker == null) return;
        
        Vector3 pos1 = firstPointMarker.transform.position;
        Vector3 pos2 = secondPointMarker.transform.position;
        
        // 측정 선 업데이트 (항상)
        if (measurementLine != null)
        {
            measurementLine.SetPosition(0, pos1);
            measurementLine.SetPosition(1, pos2);
        }
        
        // 실시간 거리 계산
        measuredDistance = CalculatePhysicalDistance(pos1, pos2);
        
        // 거리 라벨 업데이트 (소수점 2자리)
        if (distanceLabel != null)
        {
            distanceLabel.ToolTipText = $"Distance: {measuredDistance:F2}cm";
        }
    }

    /// <summary>
    /// World 좌표를 물리 단위(cm)로 변환하여 거리 계산
    /// </summary>
    private float CalculatePhysicalDistance(Vector3 worldPoint1, Vector3 worldPoint2)
    {
        Vector3 local1, local2;
        
        // 1. World → Local 변환
        if (objectParentTransform != null)
        {
            local1 = objectParentTransform.InverseTransformPoint(worldPoint1);
            local2 = objectParentTransform.InverseTransformPoint(worldPoint2);
        }
        else
        {
            local1 = worldPoint1;
            local2 = worldPoint2;
        }
        
        // 2. Local 거리 계산 (Unity 단위: 미터)
        Vector3 diff = local2 - local1;
        
        // 3. Voxel Spacing 적용 (필요시 활성화, 현재는 AutoCalibration에 의존)
        // 기존 1000f 곱셈 제거: AutoCalibration이 올바른 factor를 계산함
        // Vector3 physicalDiff = new Vector3(diff.x * voxelSpacing.x, diff.y * voxelSpacing.y, diff.z * voxelSpacing.z);
        // float rawDistance = physicalDiff.magnitude;

        // Simplified:
        float rawDistance = diff.magnitude;
        
        // 4. 거리 계산 및 캘리브레이션 보정
        return rawDistance * calibrationFactor;
    }

    /// <summary>
    /// 마커 위치에서 가장 가까운 mesh vertex로 직접 스냅
    /// snapDistance 범위 내에서만 스냅, 범위 밖이면 현재 위치 유지
    /// </summary>
    private Vector3 SnapToNearestSurface(Vector3 markerPos, Vector3 otherMarkerPos)
    {
        if (!enableSnapToSurface || bloodVesselMesh == null) return markerPos;
        
        MeshFilter meshFilter = bloodVesselMesh.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogWarning("[VesselMeasurement] Blood vessel mesh has no MeshFilter.");
            return markerPos;
        }
        
        Mesh mesh = meshFilter.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        Transform meshTransform = bloodVesselMesh.transform;
        
        // 마커 위치에서 가장 가까운 vertex 찾기
        float closestDist = float.MaxValue;
        Vector3 closestVertex = markerPos;
        bool foundVertex = false;
        
        foreach (Vector3 localVertex in vertices)
        {
            Vector3 worldVertex = meshTransform.TransformPoint(localVertex);
            float dist = Vector3.Distance(markerPos, worldVertex);
            
            if (dist < closestDist)
            {
                closestDist = dist;
                closestVertex = worldVertex;
                foundVertex = true;
            }
        }
        
        // snapDistance 범위 내에서만 스냅
        if (foundVertex && closestDist <= snapDistance)
        {
            Debug.Log($"<color=cyan>[VesselMeasurement] Snapped to nearest vertex: {closestVertex} (dist: {closestDist:F4})</color>");
            return closestVertex;
        }
        
        // 범위 밖이면 현재 위치 유지
        if (closestDist > snapDistance)
        {
            Debug.Log($"<color=yellow>[VesselMeasurement] Too far from mesh (dist: {closestDist:F4} > snapDistance: {snapDistance}). Keeping position.</color>");
        }
        return markerPos;
    }

    private void SetObjectParentInteraction(bool enabled)
    {
        // BoundingBox 비활성화/활성화
        if (objectParentBoundingBox != null)
        {
            if (!enabled)
            {
                wasBoundingBoxEnabled = objectParentBoundingBox.enabled;
            }
            
            objectParentBoundingBox.enabled = enabled ? wasBoundingBoxEnabled : false;
            Debug.Log($"<color=cyan>[VesselMeasurement] ObjectParent BoundingBox: {objectParentBoundingBox.enabled}</color>");
        }
        
        // ObjectManipulator 비활성화/활성화
        if (objectParentManipulator != null)
        {
            if (!enabled)
            {
                wasManipulatorEnabled = objectParentManipulator.enabled;
            }
            
            objectParentManipulator.enabled = enabled ? wasManipulatorEnabled : false;
            Debug.Log($"<color=cyan>[VesselMeasurement] ObjectParent ObjectManipulator: {objectParentManipulator.enabled}</color>");
        }
    }

    private void ShowMarkers()
    {
        if (firstPointMarker != null) firstPointMarker.SetActive(true);
        if (secondPointMarker != null) secondPointMarker.SetActive(true);
        if (measurementLine != null) measurementLine.enabled = true;
    }

    private void HideMarkers()
    {
        if (firstPointMarker != null) firstPointMarker.SetActive(false);
        if (secondPointMarker != null) secondPointMarker.SetActive(false);
        if (measurementLine != null) measurementLine.enabled = false;
    }

    /// <summary>
    /// 마커 조작 활성화/비활성화
    /// </summary>
    private void SetMarkerInteractionEnabled(bool enabled)
    {
        if (firstPointMarker != null)
        {
            var manipulator = firstPointMarker.GetComponent<Microsoft.MixedReality.Toolkit.UI.ObjectManipulator>();
            if (manipulator != null)
            {
                manipulator.enabled = enabled;
            }
        }
        
        if (secondPointMarker != null)
        {
            var manipulator = secondPointMarker.GetComponent<Microsoft.MixedReality.Toolkit.UI.ObjectManipulator>();
            if (manipulator != null)
            {
                manipulator.enabled = enabled;
            }
        }
        
        Debug.Log($"<color=cyan>[VesselMeasurement] Marker interaction: {enabled}</color>");
    }
    #endregion

    #region Photon Synchronization
    /// <summary>
    /// 마커 조작 시작 시 호출 - Photon 브로드캐스트 + Global Lock 요청
    /// </summary>
    private void OnInteractionStarted()
    {
        // Global Lock 요청
        if (PhotonSyncService.Instance != null)
        {
            bool lockAcquired = PhotonSyncService.Instance.RequestGlobalLock(PhotonSyncService.LockType.ObjectManipulation);
            Debug.Log($"<color=cyan>[VesselMeasurement] OnInteractionStarted - Lock acquired: {lockAcquired}</color>");
            
            Vector3 marker1Pos = firstPointMarker != null ? firstPointMarker.transform.localPosition : Vector3.zero;
            Vector3 marker2Pos = secondPointMarker != null ? secondPointMarker.transform.localPosition : Vector3.zero;
            PhotonSyncService.Instance.BroadcastMeasurementState(marker1Pos, marker2Pos, enableMeasurement, true);
        }
    }

    /// <summary>
    /// 마커 조작 종료 시 호출 - Photon 브로드캐스트 + Global Lock 해제
    /// </summary>
    private void OnInteractionEnded()
    {
        if (PhotonSyncService.Instance != null)
        {
            Vector3 marker1Pos = firstPointMarker != null ? firstPointMarker.transform.localPosition : Vector3.zero;
            Vector3 marker2Pos = secondPointMarker != null ? secondPointMarker.transform.localPosition : Vector3.zero;
            PhotonSyncService.Instance.BroadcastMeasurementState(marker1Pos, marker2Pos, enableMeasurement, false);
            
            // Global Lock 해제
            PhotonSyncService.Instance.ReleaseGlobalLock();
            Debug.Log("<color=green>[VesselMeasurement] OnInteractionEnded - Lock released</color>");
        }
    }

    /// <summary>
    /// 원격 클라이언트에서 측정 상태 적용 (PhotonSyncService에서 호출)
    /// </summary>
    public void ApplyNetworkMeasurement(Vector3 marker1LocalPos, Vector3 marker2LocalPos, bool isEnabled, bool isInteracting)
    {
        // 측정 모드 동기화
        if (isEnabled != enableMeasurement)
        {
            if (isEnabled)
                StartMeasurement();
            else
                StopMeasurement();
        }

        // 마커 위치 동기화 (로컬 좌표)
        if (firstPointMarker != null)
            firstPointMarker.transform.localPosition = marker1LocalPos;
        if (secondPointMarker != null)
            secondPointMarker.transform.localPosition = marker2LocalPos;

        Debug.Log($"<color=magenta>[VesselMeasurement] Applied network measurement: enabled={isEnabled}, interacting={isInteracting}</color>");
    }
    #endregion
}
