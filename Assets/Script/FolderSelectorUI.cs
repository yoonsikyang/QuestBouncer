using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using TMPro;
using Microsoft.MixedReality.Toolkit.UI;
using Microsoft.MixedReality.Toolkit.Utilities;

/// <summary>
/// StreamingAssets 폴더 목록을 동적으로 표시하는 UI
/// MRTK ScrollingObjectCollection을 사용하여 폴더 버튼 리스트 생성
/// </summary>
public class FolderSelectorUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("폴더 선택 UI 전체 패널")]
    public GameObject folderSelectorPanel;
    
    [Tooltip("ScrollingObjectCollection 컴포넌트")]
    public ScrollingObjectCollection scrollView;
    
    [Tooltip("버튼 프리팹 (PressableButton)")]
    public GameObject buttonPrefab;
    
    [Tooltip("버튼들이 배치될 컨테이너 (GridObjectCollection)")]
    public GridObjectCollection buttonContainer;
    
    [Tooltip("ClippingBox - 버튼 Renderer 등록용")]
    public ClippingBox clippingBox;
    
    [Header("Settings")]
    [Tooltip("버튼 시작 Y 위치")]
    public float buttonStartY = -0.016f;
    
    [Tooltip("생성된 버튼 목록")]
    private List<GameObject> createdButtons = new List<GameObject>();
    
    private bool isVisible = false;

    // 이전 프레임의 위치/회전 저장 (ClippingBox 업데이트용)
    private Vector3 lastPosition;
    private Quaternion lastRotation;
    
    void Start()
    {
        // 초기에는 숨김
        if (folderSelectorPanel != null)
        {
            folderSelectorPanel.SetActive(false);
            lastPosition = folderSelectorPanel.transform.position;
            lastRotation = folderSelectorPanel.transform.rotation;
        }
    }
    
    void Update()
    {
        // 패널이 활성화되어 있고 위치/회전이 변경되었을 때 ClippingBox 갱신
        if (isVisible && folderSelectorPanel != null)
        {
            bool positionChanged = Vector3.Distance(lastPosition, folderSelectorPanel.transform.position) > 0.001f;
            bool rotationChanged = Quaternion.Angle(lastRotation, folderSelectorPanel.transform.rotation) > 0.1f;
            
            if (positionChanged || rotationChanged)
            {
                lastPosition = folderSelectorPanel.transform.position;
                lastRotation = folderSelectorPanel.transform.rotation;
                
                // ClippingBox 강제 갱신
                RefreshClippingBounds();
            }
        }
    }
    
    /// <summary>
    /// ClippingBox를 강제로 갱신하여 렌더러들이 올바르게 클립되도록 함
    /// </summary>
    public void RefreshClippingBounds()
    {
        if (clippingBox != null)
        {
            // ClippingBox를 껐다 켜서 강제 갱신
            clippingBox.enabled = false;
            clippingBox.enabled = true;
            
            // ScrollingObjectCollection도 갱신
            if (scrollView != null)
            {
                scrollView.UpdateContent();
            }
        }
    }

    /// <summary>
    /// 폴더 선택 UI 토글
    /// </summary>
    public void ToggleFolderSelector()
    {
        if (isVisible)
        {
            HideFolderSelector();
        }
        else
        {
            ShowFolderSelector();
        }
    }

    /// <summary>
    /// 폴더 선택 UI 표시 및 폴더 목록 새로고침
    /// </summary>
    public void ShowFolderSelector()
    {
        if (folderSelectorPanel == null)
        {
            Debug.LogWarning("<color=yellow>[FolderSelectorUI] folderSelectorPanel is not assigned!</color>");
            return;
        }

        // 먼저 패널 활성화
        folderSelectorPanel.SetActive(true);
        isVisible = true;
        
        // 폴더 목록 새로고침 (코루틴)
        StartCoroutine(PopulateFolderListCoroutine());
        
        Debug.Log("<color=cyan>[FolderSelectorUI] Folder selector shown</color>");
    }

    /// <summary>
    /// 폴더 선택 UI 숨김
    /// </summary>
    public void HideFolderSelector()
    {
        if (folderSelectorPanel != null)
        {
            folderSelectorPanel.SetActive(false);
        }
        isVisible = false;
        
        Debug.Log("<color=cyan>[FolderSelectorUI] Folder selector hidden</color>");
    }

    /// <summary>
    /// StreamingAssets에서 폴더 목록을 스캔하고 버튼 생성 (코루틴)
    /// </summary>
    private IEnumerator PopulateFolderListCoroutine()
    {
        // 기존 버튼 정리
        ClearButtons();
        
        // Destroy()가 프레임 끝에 실행되므로 한 프레임 기다림
        yield return null;
        
        if (buttonPrefab == null)
        {
            Debug.LogError("<color=red>[FolderSelectorUI] buttonPrefab is not assigned!</color>");
            yield break;
        }
        
        if (buttonContainer == null)
        {
            Debug.LogError("<color=red>[FolderSelectorUI] buttonContainer is not assigned!</color>");
            yield break;
        }

        // StreamingAssets 폴더 스캔
        string streamingAssetsPath = Application.streamingAssetsPath;
        
        if (!Directory.Exists(streamingAssetsPath))
        {
            Debug.LogError($"<color=red>[FolderSelectorUI] StreamingAssets not found: {streamingAssetsPath}</color>");
            yield break;
        }

        string[] directories = Directory.GetDirectories(streamingAssetsPath);
        
        // ClippingBox 비활성화 (버튼 생성 중)
        if (clippingBox != null && clippingBox.enabled)
        {
            clippingBox.enabled = false;
        }
        
        foreach (string dir in directories)
        {
            string folderName = new DirectoryInfo(dir).Name;
            
            // 숨김 폴더 및 메타 파일 스킵
            if (folderName.StartsWith(".") || folderName.EndsWith(".meta"))
                continue;
            
            // 버튼 생성
            CreateFolderButton(folderName);
        }

        // 한 프레임 기다림 - Unity가 오브젝트를 완전히 초기화하도록
        yield return null;
        
        // GridObjectCollection 업데이트
        buttonContainer.UpdateCollection();
        
        // 한 프레임 더 기다림
        yield return null;
        
        // ScrollingObjectCollection 업데이트
        if (scrollView != null)
        {
            scrollView.UpdateContent();
        }
        
        // 모든 버튼 생성 후 ClippingBox 활성화
        if (clippingBox != null)
        {
            clippingBox.enabled = true;
            Debug.Log("<color=green>[FolderSelectorUI] ClippingBox enabled</color>");
        }

        Debug.Log($"<color=green>[FolderSelectorUI] Created {createdButtons.Count} folder buttons</color>");
    }

    /// <summary>
    /// 폴더 버튼 생성
    /// </summary>
    private void CreateFolderButton(string folderName)
    {
        GameObject buttonObj = Instantiate(buttonPrefab, buttonContainer.transform);
        buttonObj.name = $"FolderButton_{folderName}";
        buttonObj.SetActive(true);
        
        // 버튼 Y 위치 설정
        Vector3 localPos = buttonObj.transform.localPosition;
        localPos.y = buttonStartY;
        buttonObj.transform.localPosition = localPos;
        
        // ClippingBox에 버튼의 모든 Renderer 등록 (ClippingBox는 나중에 활성화됨)
        if (clippingBox != null)
        {
            Renderer[] renderers = buttonObj.GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                clippingBox.AddRenderer(renderer);
                
                // TextMeshPro 렌더러의 경우 clipping shader 키워드 활성화
                if (renderer.GetType().Name.Contains("TextMeshPro") || 
                    renderer.gameObject.GetComponent<TextMeshPro>() != null ||
                    renderer.gameObject.GetComponent<TextMeshProUGUI>() != null)
                {
                    // Material에 clipping 키워드 활성화
                    foreach (Material mat in renderer.materials)
                    {
                        if (mat != null)
                        {
                            mat.EnableKeyword("_CLIPPING_PLANE");
                            mat.EnableKeyword("_CLIPPING_BOX");
                        }
                    }
                }
            }
        }
        else
        {
            Debug.LogWarning("<color=red>[FolderSelectorUI] clippingBox is not assigned! Please assign in Inspector.</color>");
        }
        
        // 버튼 텍스트 설정 (TextMeshPro)
        TextMeshPro tmpText = buttonObj.GetComponentInChildren<TextMeshPro>();
        if (tmpText != null)
        {
            tmpText.text = folderName;
        }
        else
        {
            // TextMeshProUGUI 시도
            TextMeshProUGUI tmpuguiText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (tmpuguiText != null)
            {
                tmpuguiText.text = folderName;
            }
            else
            {
                Debug.LogWarning($"<color=yellow>[FolderSelectorUI] No TextMeshPro found in button prefab for {folderName}</color>");
            }
        }
        
        // 버튼 클릭 이벤트 연결
        Interactable interactable = buttonObj.GetComponent<Interactable>();
        if (interactable != null)
        {
            string capturedFolderName = folderName; // 클로저용 캡처
            interactable.OnClick.AddListener(() => OnFolderButtonClicked(capturedFolderName));
        }
        else
        {
            // PressableButtonHoloLens2 시도
            var pressableButton = buttonObj.GetComponent<PressableButtonHoloLens2>();
            if (pressableButton != null)
            {
                string capturedFolderName = folderName;
                pressableButton.ButtonPressed.AddListener(() => OnFolderButtonClicked(capturedFolderName));
            }
            else
            {
                Debug.LogWarning($"<color=yellow>[FolderSelectorUI] No Interactable found on button for {folderName}</color>");
            }
        }
        
        createdButtons.Add(buttonObj);
        
        Debug.Log($"<color=cyan>[FolderSelectorUI] Created button for folder: {folderName}</color>");
    }

    /// <summary>
    /// 폴더 버튼 클릭 시 호출
    /// </summary>
    private void OnFolderButtonClicked(string folderName)
    {
        Debug.Log($"<color=green>[FolderSelectorUI] Folder selected: {folderName}</color>");

        var buttonController = FindObjectOfType<ButtonControllerManager>();
        if (buttonController != null)
        {
            buttonController.NotifyDataFolderSelectionConfirmed();
        }
        
        // Manager를 통해 데이터 폴더 변경
        if (Manager.Instance != null)
        {
            // Photon 브로드캐스트
            if (PhotonSyncService.Instance != null)
            {
                PhotonSyncService.Instance.BroadcastDataFolder(folderName);
            }
            
            StartCoroutine(ChangeDataFolderWithContext(folderName, buttonController));
        }
        else
        {
            Debug.LogError("<color=red>[FolderSelectorUI] Manager.Instance is null!</color>");
            if (buttonController != null)
            {
                buttonController.NotifyDataFolderChangeFinished();
            }
        }
        
        // UI 숨김
        HideFolderSelector();
        
        // Settings Menu도 숨김
        if (buttonController != null && buttonController.settingsMenu != null)
        {
            buttonController.settingsMenu.SetActive(false);
            Debug.Log("<color=cyan>[FolderSelectorUI] Settings menu hidden</color>");
        }
    }

    private IEnumerator ChangeDataFolderWithContext(string folderName, ButtonControllerManager buttonController)
    {
        yield return Manager.Instance.ChangeDataFolderCoroutine(folderName);

        if (buttonController != null)
        {
            buttonController.NotifyDataFolderChangeFinished();
        }
    }

    /// <summary>
    /// 생성된 버튼들 정리 - ButtonContainer의 모든 자식 삭제
    /// </summary>
    private void ClearButtons()
    {
        // createdButtons 리스트 정리
        foreach (var button in createdButtons)
        {
            if (button != null)
            {
                Destroy(button);
            }
        }
        createdButtons.Clear();
        
        // ButtonContainer의 모든 자식도 삭제 (프리팹에 남아있는 기존 버튼 포함)
        if (buttonContainer != null)
        {
            // 역순으로 삭제 (인덱스 문제 방지)
            for (int i = buttonContainer.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = buttonContainer.transform.GetChild(i);
                Destroy(child.gameObject);
            }
            Debug.Log("<color=cyan>[FolderSelectorUI] Cleared all buttons from container</color>");
        }
    }

    void OnDestroy()
    {
        ClearButtons();
    }
}
