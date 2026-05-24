using UnityEditor;
using UnityEngine.InputSystem.Editor;

[InitializeOnLoad]
public static class HanyangVisionOSInputSystemRegistration
{
    static HanyangVisionOSInputSystemRegistration()
    {
        InputSystemPluginControl.RegisterPlatform(BuildTarget.VisionOS);
    }
}
