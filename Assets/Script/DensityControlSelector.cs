using UnityEngine;

/// <summary>
/// 전역 슬라이더/버튼 값을 VelocityLoader의 displayStepX/Y/Z에 코드로 매핑하는 도우미.
/// - targetAxis를 바꿔가며 어떤 축을 제어할지 선택할 수 있음.
/// - float 입력(슬라이더)을 int로 반올림/클램프(1~10) 후 적용.
/// </summary>
public class DensityControlSelector : MonoBehaviour
{
    public enum DensityAxis { X, Y, Z }

    [Header("Target")]
    public VelocityLoader velocityLoader;
    public DensityAxis targetAxis = DensityAxis.X;

    [Header("Settings")]
    [Range(1, 10)] public int initialValue = 1;
    public bool applyOnStart = true;

    void Start()
    {
        if (velocityLoader == null)
        {
            velocityLoader = FindObjectOfType<VelocityLoader>();
        }

        // Do not force overwrite settings on Start. 
        // Manager loads settings from JSON which should be the source of truth.
        /*
        if (applyOnStart && velocityLoader != null)
        {
            ApplyValue(initialValue);
        }
        */
    }

    /// <summary>
    /// 슬라이더(float)나 UI 이벤트에 직접 연결할 함수.
    /// </summary>
    public void ApplyValue(float value)
    {
        ApplyValue(Mathf.RoundToInt(value));
    }

    /// <summary>
    /// 코드/버튼에서 직접 호출할 함수 (정수 입력).
    /// </summary>
    public void ApplyValue(int value)
    {
        if (velocityLoader == null) return;
        int v = Mathf.Clamp(value, 1, 10);

        switch (targetAxis)
        {
            case DensityAxis.X:
                velocityLoader.SetDisplayStepX(v);
                break;
            case DensityAxis.Y:
                velocityLoader.SetDisplayStepY(v);
                break;
            case DensityAxis.Z:
                velocityLoader.SetDisplayStepZ(v);
                break;
        }
    }

    /// <summary>
    /// UI 토글/드롭다운 등에서 축 선택을 바꿀 때 호출.
    /// index: 0=X, 1=Y, 2=Z
    /// </summary>
    public void SetTargetAxis(int index)
    {
        targetAxis = (DensityAxis)Mathf.Clamp(index, 0, 2);
    }
}
