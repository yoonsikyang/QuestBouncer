using UnityEngine;
using UnityEngine.UI;
using Microsoft.MixedReality.Toolkit.Utilities;
using Microsoft.MixedReality.Toolkit.Input;

/// <summary>
/// 양손 주먹 쥐기 제스처를 감지하여 초기화를 트리거합니다.
/// 양손 모두 주먹을 쥐고 1.5초 이상 유지하면 ResetVesselTransform이 호출됩니다.
/// Progress Ring + Text 시각적 피드백 제공
/// </summary>
public class FistGestureDetector : MonoBehaviour
{
    [Header("Gesture Settings")]
    [Tooltip("주먹 쥐기 인식 임계값 (0-1). 높을수록 더 꽉 쥐어야 함")]
    [Range(0.5f, 1f)] public float grabThreshold = 0.7f;
    
    [Tooltip("리셋 트리거까지 유지해야 하는 시간 (초)")]
    [Range(0.5f, 3f)] public float holdDuration = 1.5f;
    
    [Tooltip("리셋 후 다시 트리거 가능해지기까지의 쿨다운 시간 (초)")]
    [Range(1f, 5f)] public float cooldownDuration = 2f;
    
    [Header("Visual Feedback")]
    [Tooltip("시각적 피드백 활성화 여부")]
    public bool enableVisualFeedback = true;
    
    [Tooltip("Progress Ring 색상 시작 (0%)")]
    public Color ringColorStart = new Color(0.2f, 0.6f, 1f, 0.8f);  // 하늘색
    
    [Tooltip("Progress Ring 색상 끝 (100%)")]
    public Color ringColorEnd = new Color(0f, 1f, 0.5f, 1f);  // 초록색
    
    [Tooltip("완료 시 색상")]
    public Color completedColor = new Color(0f, 1f, 0f, 1f);  // 밝은 초록
    
    [Tooltip("카메라로부터 UI 거리")]
    public float uiDistanceFromCamera = 0.5f;
    
    [Tooltip("Progress Ring 크기")]
    public float ringSize = 0.1f;
    
    [Header("References")]
    [Tooltip("ButtonControllerManager 참조 (자동 탐색됨)")]
    public ButtonControllerManager buttonController;
    
    [Header("Debug")]
    [SerializeField] private float currentHoldTime = 0f;
    [SerializeField] private bool isLeftGrabbing = false;
    [SerializeField] private bool isRightGrabbing = false;
    [SerializeField] private bool isBothHandsGrabbing = false;
    
    private float cooldownTimer = 0f;
    private bool isTriggered = false;
    
    // Visual Feedback Objects
    private GameObject feedbackCanvas;
    private Image progressRingBackground;
    private Image progressRingFill;
    private Text progressTextUI;
    private bool feedbackInitialized = false;
    
    void Start()
    {
        // ButtonControllerManager 자동 탐색
        if (buttonController == null)
        {
            buttonController = FindObjectOfType<ButtonControllerManager>();
        }
        
        if (buttonController == null)
        {
            Debug.LogWarning("<color=yellow>[FistGestureDetector] ButtonControllerManager not found!</color>");
        }
        else
        {
            Debug.Log("<color=green>[FistGestureDetector] Initialized - Hold both fists for " + holdDuration + "s to reset</color>");
        }
        
        // Visual Feedback 초기화
        if (enableVisualFeedback)
        {
            CreateVisualFeedback();
        }
    }
    
    void Update()
    {
        // 쿨다운 중이면 타이머 감소
        if (cooldownTimer > 0)
        {
            cooldownTimer -= Time.deltaTime;
            UpdateVisualFeedback(0f, false);
            return;
        }
        
        // 주먹 쥐기 상태 체크
        CheckGrabGesture();
        
        // 양손 모두 주먹을 쥐고 있으면 홀드 시간 증가
        if (isBothHandsGrabbing && !isTriggered)
        {
            currentHoldTime += Time.deltaTime;
            
            // Visual Feedback 업데이트
            float progress = Mathf.Clamp01(currentHoldTime / holdDuration);
            UpdateVisualFeedback(progress, true);
            
            // 홀드 시간 달성 시 리셋 트리거
            if (currentHoldTime >= holdDuration)
            {
                TriggerReset();
                isTriggered = true;
                cooldownTimer = cooldownDuration;
                ShowCompletedFeedback();
            }
        }
        else if (!isBothHandsGrabbing)
        {
            // 주먹을 풀면 홀드 시간 리셋
            currentHoldTime = 0f;
            isTriggered = false;
            UpdateVisualFeedback(0f, false);
        }
    }
    
    /// <summary>
    /// Visual Feedback UI 생성
    /// </summary>
    void CreateVisualFeedback()
    {
        if (feedbackInitialized) return;
        
        // World Space Canvas 생성
        feedbackCanvas = new GameObject("FistGesture_FeedbackCanvas");
        feedbackCanvas.transform.SetParent(transform);
        
        Canvas canvas = feedbackCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;
        
        CanvasScaler scaler = feedbackCanvas.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 100;
        
        feedbackCanvas.AddComponent<GraphicRaycaster>();
        
        RectTransform canvasRect = feedbackCanvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(200, 200);
        canvasRect.localScale = Vector3.one * 0.001f;  // World Space에서 적절한 크기로
        
        // 원형 스프라이트 동적 생성
        Sprite circleSprite = CreateCircleSprite(128);
        
        // Background Ring (회색 배경)
        GameObject bgObj = new GameObject("ProgressRing_BG");
        bgObj.transform.SetParent(feedbackCanvas.transform, false);
        progressRingBackground = bgObj.AddComponent<Image>();
        progressRingBackground.sprite = circleSprite;
        progressRingBackground.type = Image.Type.Filled;
        progressRingBackground.fillMethod = Image.FillMethod.Radial360;
        progressRingBackground.fillOrigin = (int)Image.Origin360.Top;
        progressRingBackground.fillClockwise = true;
        progressRingBackground.fillAmount = 1f;
        progressRingBackground.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        
        RectTransform bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.sizeDelta = new Vector2(100, 100);
        bgRect.anchoredPosition = Vector2.zero;
        
        // Progress Ring Fill (채워지는 링)
        GameObject fillObj = new GameObject("ProgressRing_Fill");
        fillObj.transform.SetParent(feedbackCanvas.transform, false);
        progressRingFill = fillObj.AddComponent<Image>();
        progressRingFill.sprite = circleSprite;
        progressRingFill.type = Image.Type.Filled;
        progressRingFill.fillMethod = Image.FillMethod.Radial360;
        progressRingFill.fillOrigin = (int)Image.Origin360.Top;
        progressRingFill.fillClockwise = true;
        progressRingFill.fillAmount = 0f;
        progressRingFill.color = ringColorStart;
        
        RectTransform fillRect = fillObj.GetComponent<RectTransform>();
        fillRect.sizeDelta = new Vector2(100, 100);
        fillRect.anchoredPosition = Vector2.zero;
        
        // Progress Text (Unity UI Text 사용)
        GameObject textObj = new GameObject("ProgressText");
        textObj.transform.SetParent(feedbackCanvas.transform, false);
        progressTextUI = textObj.AddComponent<Text>();
        progressTextUI.text = "";
        progressTextUI.fontSize = 50;
        progressTextUI.alignment = TextAnchor.MiddleCenter;
        progressTextUI.color = Color.white;
        progressTextUI.font = Font.CreateDynamicFontFromOSFont("Arial", 50);
        
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(150, 80);
        textRect.anchoredPosition = new Vector2(0, 0);  // 중앙에 배치
        
        feedbackCanvas.SetActive(false);
        feedbackInitialized = true;
        
        Debug.Log("<color=green>[FistGestureDetector] Visual Feedback UI created with circle sprite</color>");
    }
    
    /// <summary>
    /// 원형 스프라이트 동적 생성
    /// </summary>
    Sprite CreateCircleSprite(int resolution)
    {
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        
        float center = resolution / 2f;
        float radius = resolution / 2f - 1f;
        
        Color[] pixels = new Color[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                if (dist <= radius)
                {
                    // 안티앨리어싱 효과
                    float alpha = Mathf.Clamp01((radius - dist) * 2f);
                    pixels[y * resolution + x] = new Color(1, 1, 1, alpha);
                }
                else
                {
                    pixels[y * resolution + x] = Color.clear;
                }
            }
        }
        
        texture.SetPixels(pixels);
        texture.Apply();
        
        return Sprite.Create(texture, new Rect(0, 0, resolution, resolution), new Vector2(0.5f, 0.5f));
    }
    
    /// <summary>
    /// Visual Feedback 업데이트
    /// </summary>
    void UpdateVisualFeedback(float progress, bool show)
    {
        if (!enableVisualFeedback || !feedbackInitialized) return;
        
        if (show && progress > 0.01f)
        {
            feedbackCanvas.SetActive(true);
            
            // 왼손 위치에 표시
            Camera mainCam = Camera.main;
            if (HandJointUtils.TryGetJointPose(TrackedHandJoint.Palm, Handedness.Left, out MixedRealityPose palmPose))
            {
                // 왼손 손바닥 위에 표시 (약간 위로 오프셋)
                Vector3 handPos = palmPose.Position + Vector3.up * 0.08f;
                feedbackCanvas.transform.position = handPos;
                
                // 카메라를 바라보도록 회전
                if (mainCam != null)
                {
                    feedbackCanvas.transform.rotation = Quaternion.LookRotation(feedbackCanvas.transform.position - mainCam.transform.position);
                }
                feedbackCanvas.transform.localScale = Vector3.one * ringSize;
            }
            else if (mainCam != null)
            {
                // 손 추적 실패 시 카메라 앞에 표시 (폴백)
                feedbackCanvas.transform.position = mainCam.transform.position + mainCam.transform.forward * uiDistanceFromCamera;
                feedbackCanvas.transform.rotation = Quaternion.LookRotation(feedbackCanvas.transform.position - mainCam.transform.position);
                feedbackCanvas.transform.localScale = Vector3.one * ringSize;
            }
            
            // Progress Ring 업데이트
            progressRingFill.fillAmount = progress;
            progressRingFill.color = Color.Lerp(ringColorStart, ringColorEnd, progress);
            
            // Text 업데이트
            int percentage = Mathf.RoundToInt(progress * 100f);
            progressTextUI.text = $"Reset...\n{percentage}%";
        }
        else
        {
            feedbackCanvas.SetActive(false);
        }
    }
    
    /// <summary>
    /// 완료 피드백 표시
    /// </summary>
    void ShowCompletedFeedback()
    {
        if (!enableVisualFeedback || !feedbackInitialized) return;
        
        progressRingFill.fillAmount = 1f;
        progressRingFill.color = completedColor;
        progressTextUI.text = "Complete!";
        progressTextUI.color = completedColor;
        
        // 0.5초 후 자동 숨김
        Invoke(nameof(HideFeedback), 0.5f);
    }
    
    void HideFeedback()
    {
        if (feedbackCanvas != null)
        {
            feedbackCanvas.SetActive(false);
        }
        if (progressTextUI != null)
        {
            progressTextUI.color = Color.white;
        }
    }
    
    /// <summary>
    /// MRTK HandPoseUtils를 사용하여 양손의 주먹 상태 확인
    /// 핀치와 구분하기 위해 모든 손가락이 굽혀져 있는지 확인
    /// </summary>
    void CheckGrabGesture()
    {
        // 왼손: 모든 손가락이 굽혀져 있어야 주먹으로 인식 (핀치와 구분)
        bool leftIndex = HandPoseUtils.IsIndexGrabbing(Handedness.Left);
        bool leftMiddle = HandPoseUtils.IsMiddleGrabbing(Handedness.Left);
        bool leftRing = HandPoseUtils.IsMiddleGrabbing(Handedness.Left); // Ring은 Middle과 유사하게 체크
        bool leftPinky = HandPoseUtils.IsMiddleGrabbing(Handedness.Left); // Pinky도 Middle과 유사
        bool leftThumb = HandPoseUtils.IsThumbGrabbing(Handedness.Left);
        
        // 모든 손가락이 굽혀져 있어야 진짜 주먹
        isLeftGrabbing = leftIndex && leftMiddle;
         
        // 오른손: 모든 손가락이 굽혀져 있어야 주먹으로 인식
        bool rightIndex = HandPoseUtils.IsIndexGrabbing(Handedness.Right);
        bool rightMiddle = HandPoseUtils.IsMiddleGrabbing(Handedness.Right); 
        
        // 모든 손가락이 굽혀져 있어야 진짜 주먹
        isRightGrabbing = rightIndex && rightMiddle;
        
        // 양손 모두 주먹을 쥐고 있어야 함
        isBothHandsGrabbing = isLeftGrabbing && isRightGrabbing;
    }
    
    /// <summary>
    /// 리셋 동작 실행
    /// </summary>
    void TriggerReset()
    {
        Debug.Log("<color=cyan>[FistGestureDetector] Both fists held for " + holdDuration + "s - Triggering Reset!</color>");
        
        if (buttonController != null)
        {
            buttonController.ResetVesselTransform();
        }
        else if (Manager.Instance != null)
        {
            // 폴백: Manager.RecenterCamera() 직접 호출
            Manager.Instance.RecenterCamera();
        }
        
        // 홀드 시간 리셋
        currentHoldTime = 0f;
    }
    
    /// <summary>
    /// 현재 홀드 진행률 반환 (0-1) - UI 피드백용
    /// </summary>
    public float GetHoldProgress()
    {
        return Mathf.Clamp01(currentHoldTime / holdDuration);
    }
    
    /// <summary>
    /// 현재 양손 주먹 쥐기 중인지 반환
    /// </summary>
    public bool IsBothHandsGrabbing()
    {
        return isBothHandsGrabbing;
    }
    
    void OnDestroy()
    {
        if (feedbackCanvas != null)
        {
            Destroy(feedbackCanvas);
        }
    }
}

