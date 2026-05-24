using System;
using System.Text;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

/// <summary>
/// Windows editor fallback TTS using an external PowerShell process and SAPI.
/// This avoids COM activation directly inside Unity's runtime.
/// </summary>
public class EditorTtsFallback : MonoBehaviour
{
#if UNITY_EDITOR && !WINDOWS_UWP
    private Process speechProcess;
#endif

    private void Awake()
    {
#if UNITY_EDITOR && !WINDOWS_UWP
        UnityEngine.Debug.Log("[EditorTTS] PowerShell TTS fallback enabled.");
#endif
    }

    public void Speak(string text)
    {
#if UNITY_EDITOR && !WINDOWS_UWP
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            StopSpeaking();

            string script =
                "$ErrorActionPreference='Stop'; " +
                "$voice = New-Object -ComObject SAPI.SpVoice; " +
                "$voice.Rate = 0; " +
                "[void]$voice.Speak(" + ToPowerShellString(text) + ");";

            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encodedCommand,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            speechProcess = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[EditorTTS] Speak failed: {ex.Message}");
        }
#else
        UnityEngine.Debug.Log($"[EditorTTS] {text}");
#endif
    }

    public void StopSpeaking()
    {
#if UNITY_EDITOR && !WINDOWS_UWP
        if (speechProcess == null)
        {
            return;
        }

        try
        {
            if (!speechProcess.HasExited)
            {
                speechProcess.Kill();
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[EditorTTS] Stop failed: {ex.Message}");
        }
        finally
        {
            speechProcess.Dispose();
            speechProcess = null;
        }
#endif
    }

    public bool IsSpeaking()
    {
#if UNITY_EDITOR && !WINDOWS_UWP
        return speechProcess != null && !speechProcess.HasExited;
#else
        return false;
#endif
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR && !WINDOWS_UWP
        StopSpeaking();
#endif
    }

    private static string ToPowerShellString(string text)
    {
        return "'" + text.Replace("'", "''") + "'";
    }
}
