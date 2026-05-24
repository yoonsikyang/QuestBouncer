using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;

/// <summary>
/// Editor-only external MP3 playback via PowerShell + WPF MediaPlayer.
/// Keeps Windows audio output outside Unity's virtualized audio pipeline.
/// </summary>
public class EditorAudioFilePlayer : MonoBehaviour
{
#if UNITY_EDITOR && !WINDOWS_UWP
    private Process audioProcess;
#endif

    public void PlayFile(string absolutePath)
    {
#if UNITY_EDITOR && !WINDOWS_UWP
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return;
        }

        try
        {
            StopPlayback();

            string normalizedPath = absolutePath.Replace("\\", "/");
            string uri = "file:///" + normalizedPath;
            string script =
                "$ErrorActionPreference='Stop'; " +
                "Add-Type -AssemblyName presentationCore; " +
                "$player = New-Object System.Windows.Media.MediaPlayer; " +
                "$uri = New-Object System.Uri(" + ToPowerShellString(uri) + "); " +
                "$player.Open($uri); " +
                "$wait=0; " +
                "while($player.NaturalDuration.HasTimeSpan -eq $false -and $wait -lt 5000){ Start-Sleep -Milliseconds 100; $wait += 100; } " +
                "$player.Volume = 1.0; " +
                "$player.Play(); " +
                "$duration = 0; " +
                "if($player.NaturalDuration.HasTimeSpan){ $duration = [int][Math]::Ceiling($player.NaturalDuration.TimeSpan.TotalMilliseconds) + 200; } " +
                "if($duration -gt 0){ Start-Sleep -Milliseconds $duration; } else { Start-Sleep -Seconds 5; } " +
                "$player.Stop(); " +
                "$player.Close();";

            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encodedCommand,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            audioProcess = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[EditorAudioFilePlayer] Play failed: {ex.Message}");
        }
#endif
    }

    public void StopPlayback()
    {
#if UNITY_EDITOR && !WINDOWS_UWP
        if (audioProcess == null)
        {
            return;
        }

        try
        {
            if (!audioProcess.HasExited)
            {
                audioProcess.Kill();
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[EditorAudioFilePlayer] Stop failed: {ex.Message}");
        }
        finally
        {
            audioProcess.Dispose();
            audioProcess = null;
        }
#endif
    }

    public bool IsPlaying()
    {
#if UNITY_EDITOR && !WINDOWS_UWP
        return audioProcess != null && !audioProcess.HasExited;
#else
        return false;
#endif
    }

    private void OnDestroy()
    {
        StopPlayback();
    }

    private static string ToPowerShellString(string text)
    {
        return "'" + text.Replace("'", "''") + "'";
    }
}
