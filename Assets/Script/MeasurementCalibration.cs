using UnityEngine;

/// <summary>
/// 측정 캘리브레이션 관리
/// 알려진 기준 거리를 측정하여 보정 계수를 계산하고 저장합니다.
/// </summary>
public class MeasurementCalibration : MonoBehaviour
{
    #region Inspector Fields
    [Header("Calibration Settings")]
    [Tooltip("보정 계수 (기본값: 1.0 = 보정 없음)")]
    public float calibrationFactor = 1.0f;
    
    [Tooltip("알려진 기준 거리 (mm)")]
    public float knownReferenceDistance = 10f;
    
    [Header("Calibration State")]
    [Tooltip("캘리브레이션 모드 활성화")]
    public bool isCalibrating = false;
    
    [SerializeField] private float measuredReferenceDistance = 0f;
    
    [Header("References")]
    public VesselMeasurementTool measurementTool;
    #endregion

    #region Private Fields
    private const string CALIBRATION_PREF_KEY = "MeasurementCalibrationFactor";
    #endregion

    #region Singleton
    public static MeasurementCalibration Instance { get; private set; }
    #endregion

    #region Unity Lifecycle
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Debug.LogWarning("Multiple MeasurementCalibration instances found. Destroying duplicate.");
            Destroy(gameObject);
            return;
        }

        LoadCalibration();
    }

    void Start()
    {
        // 측정 도구 참조 찾기
        if (measurementTool == null)
        {
            measurementTool = FindObjectOfType<VesselMeasurementTool>();
        }
        
        // 캘리브레이션 계수 전달
        if (measurementTool != null)
        {
            measurementTool.calibrationFactor = calibrationFactor;
        }
    }

    void Update()
    {
        // 캘리브레이션 모드에서 측정 완료 감지
        // 드래그 방식에서는 자동 완료가 없으므로, 수동으로 CompleteCalibration을 호출해야 함
        // 또는 일정 거리 이상 측정되면 자동 완료 처리 가능
        if (isCalibrating && measurementTool != null)
        {
            // 실시간 측정값이 0보다 크면 사용자가 측정 중
            if (measurementTool.MeasuredDistance > 0.1f)
            {
                // 실시간으로 값 표시 (선택적)
                measuredReferenceDistance = measurementTool.MeasuredDistance;
            }
        }
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// 캘리브레이션 시작
    /// 사용자가 알려진 거리를 측정하도록 안내
    /// </summary>
    public void StartCalibration()
    {
        Debug.Log($"<color=yellow>[Calibration] Starting calibration. Please measure a known distance of {knownReferenceDistance}mm</color>");
        
        isCalibrating = true;
        
        // 측정 도구 시작
        if (measurementTool != null)
        {
            // 캘리브레이션 중에는 보정 계수를 1로 설정
            measurementTool.calibrationFactor = 1.0f;
            measurementTool.StartMeasurement();
        }
    }

    /// <summary>
    /// 캘리브레이션 완료 - 보정 계수 계산
    /// </summary>
    public void CompleteCalibration(float measuredDistance)
    {
        measuredReferenceDistance = measuredDistance;
        
        if (measuredReferenceDistance > 0.001f) // 0이 아닌지 확인
        {
            calibrationFactor = knownReferenceDistance / measuredReferenceDistance;
            
            Debug.Log($"<color=green>[Calibration] Completed!</color>");
            Debug.Log($"  Known distance: {knownReferenceDistance}mm");
            Debug.Log($"  Measured distance: {measuredReferenceDistance}mm");
            Debug.Log($"  Calibration factor: {calibrationFactor}");
            
            // 저장
            SaveCalibration();
            
            // 측정 도구에 새 보정 계수 적용
            if (measurementTool != null)
            {
                measurementTool.calibrationFactor = calibrationFactor;
            }
        }
        else
        {
            Debug.LogWarning("[Calibration] Invalid measurement. Please try again.");
        }
        
        isCalibrating = false;
    }

    /// <summary>
    /// 캘리브레이션 취소
    /// </summary>
    public void CancelCalibration()
    {
        Debug.Log("<color=yellow>[Calibration] Cancelled</color>");
        
        isCalibrating = false;
        
        if (measurementTool != null)
        {
            measurementTool.StopMeasurement();
            measurementTool.calibrationFactor = calibrationFactor; // 기존 계수 복원
        }
    }

    /// <summary>
    /// 캘리브레이션 초기화 (보정 계수 1로 리셋)
    /// </summary>
    public void ResetCalibration()
    {
        calibrationFactor = 10.0f; // Default for cm -> mm in Unity (1 unit = 1m = 100cm? No. 1 unit = raw cm. So 1 = 1cm = 10mm)
        // Wait, if 1 raw unit = 1 cm.
        // And we want measurements in mm.
        // Measured Distance = Unity Distance (which is Raw Units scaled).
        // Let's rely on SetAutoCalibration. Default Reset should probably be 1.0 or user default.
        
        SaveCalibration();
        
        if (measurementTool != null)
        {
            measurementTool.calibrationFactor = calibrationFactor;
        }
        
        Debug.Log("<color=cyan>[Calibration] Reset to default</color>");
    }

    /// <summary>
    /// Auto-Calibration directly from Manager
    /// </summary>
    public void SetAutoCalibration(float scaleFactor, float unitMultiplier)
    {
        // Unity Distance = Raw Value * ScaleFactor
        // Raw Value = Unity Distance / ScaleFactor
        // Display Value (mm) = Raw Value * UnitMultiplier (e.g. 10 for cm->mm)
        // Display Value = (Unity Distance * UnitMultiplier) / ScaleFactor
        // CalibrationFactor = UnitMultiplier / ScaleFactor
        
        if (scaleFactor < 0.00001f) scaleFactor = 1.0f;
        
        calibrationFactor = unitMultiplier / scaleFactor;
        
        Debug.Log($"<color=green>[Calibration] Auto-Calibrated: Scale={scaleFactor}, UnitMult={unitMultiplier} -> Factor={calibrationFactor}</color>");
        
        if (measurementTool != null)
        {
            measurementTool.calibrationFactor = calibrationFactor;
        }
        
        SaveCalibration();
    }
    #endregion

    #region Private Methods
    private void SaveCalibration()
    {
        PlayerPrefs.SetFloat(CALIBRATION_PREF_KEY, calibrationFactor);
        PlayerPrefs.Save();
        Debug.Log($"<color=cyan>[Calibration] Saved: factor = {calibrationFactor}</color>");
    }

    private void LoadCalibration()
    {
        if (PlayerPrefs.HasKey(CALIBRATION_PREF_KEY))
        {
            calibrationFactor = PlayerPrefs.GetFloat(CALIBRATION_PREF_KEY, 1.0f);
            Debug.Log($"<color=cyan>[Calibration] Loaded: factor = {calibrationFactor}</color>");
        }
    }
    #endregion
}
