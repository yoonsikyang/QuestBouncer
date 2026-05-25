using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed class HanyangFunctionalPlayModeProbe : MonoBehaviour
{
    private const string ReportArgument = "-hanyangFunctionalValidationPath";
    private const float InitialDelaySeconds = 5f;
    private const float MaxReadyWaitSeconds = 120f;
    private const float DefaultSettleSeconds = 0.75f;
    private const float AudioSettleSeconds = 8f;

    private static readonly string[] MenuNames =
    {
        "Main Menu",
        "Show Streamline Under Menu",
        "Streamline Speed Menu",
        "Show Velocity Under Menu",
        "Visualization Setting Under Menu",
        "Velocity Visualization Setting Under Menu",
        "Show WSS Under Menu",
        "WSS Speed Menu",
        "Setting Menu",
        "Exhibition Mode Menu",
        "Folder Selector Menu",
        "Measurement Setting UI",
        "Play Setting Menu"
    };

    private readonly List<StepResult> steps = new List<StepResult>();
    private readonly List<RuntimeLog> runtimeLogs = new List<RuntimeLog>();
    private readonly List<string> fatalLogs = new List<string>();

    private string reportPath;
    private ButtonControllerManager buttonController;
    private Manager manager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartIfRequested()
    {
        string path = GetArgument(ReportArgument);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Application.runInBackground = true;
        ConfigureBatchLoggingForProbe();
        GameObject probeObject = new GameObject(nameof(HanyangFunctionalPlayModeProbe));
        DontDestroyOnLoad(probeObject);
        probeObject.hideFlags = HideFlags.HideAndDontSave;

        HanyangFunctionalPlayModeProbe probe = probeObject.AddComponent<HanyangFunctionalPlayModeProbe>();
        probe.reportPath = path;
        probe.StartCoroutine(probe.Run());
        Debug.Log("Hanyang functional validation armed: " + path);
    }

    private void OnEnable()
    {
        Application.logMessageReceived += CaptureLog;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= CaptureLog;
    }

    private IEnumerator Run()
    {
        yield return new WaitForSecondsRealtime(InitialDelaySeconds);
        yield return WaitForReadyScene();

        RecordProbeState("ready-state");
        yield return RunObservationStep(
            "startup.no_autoplay",
            "No guide audio should play before a user action.",
            delegate { return !AnyAudioPlaying(); },
            DefaultSettleSeconds);

        yield return RunButtonStep(
            "main.show_main_menu",
            delegate { buttonController.ShowMainMenu(false); },
            "Main Menu",
            "Mesh",
            null,
            DefaultSettleSeconds);

        yield return RunButtonStep(
            "streamline.open_menu",
            delegate { buttonController.ShowStreamlineMenu(false); },
            "Show Streamline Under Menu",
            "Streamline",
            null,
            DefaultSettleSeconds);

        yield return RunButtonStep(
            "streamline.speed_menu",
            delegate { buttonController.ShowStreamlineSpeedSetting(false); },
            "Streamline Speed Menu",
            "Streamline",
            null,
            DefaultSettleSeconds);

        yield return RunButtonStep(
            "streamline.toggle_playback",
            delegate { buttonController.ToggleStreamlinePlayback(); },
            null,
            "Streamline",
            null,
            DefaultSettleSeconds);

        yield return RunButtonStep(
            "velocity.open_menu",
            delegate { buttonController.ShowVelocityMenu(false); },
            "Show Velocity Under Menu",
            "Velocity",
            null,
            DefaultSettleSeconds);

        yield return RunButtonStep(
            "velocity.visualization_settings",
            delegate { buttonController.ShowVisualizationSettingMenu(false); },
            "Visualization Setting Under Menu",
            "Velocity",
            null,
            DefaultSettleSeconds);

        yield return RunButtonStep(
            "velocity.playback_settings",
            delegate { buttonController.ShowVelocityPlaybackSetting(false); },
            null,
            "Velocity",
            null,
            DefaultSettleSeconds);

        yield return RunButtonStep(
            "velocity.interval_settings",
            delegate { buttonController.ShowVelocitySliceIntervalSetting(false); },
            null,
            "Velocity",
            null,
            DefaultSettleSeconds);

        yield return RunButtonStep(
            "velocity.toggle_playback",
            delegate { buttonController.ToggleVelocityPlayback(); },
            null,
            "Velocity",
            null,
            DefaultSettleSeconds);

        yield return RunButtonStep(
            "wss.open_menu",
            delegate { buttonController.ShowWssMenu(false); },
            "Show WSS Under Menu",
            "WSS",
            null,
            DefaultSettleSeconds);

        yield return RunButtonStep(
            "wss.playback_settings",
            delegate { buttonController.ShowWssPlaybackSetting(false); },
            null,
            "WSS",
            null,
            DefaultSettleSeconds);

        yield return RunButtonStep(
            "wss.toggle_playback",
            delegate { buttonController.ToggleWSSPlayback(); },
            null,
            "WSS",
            null,
            DefaultSettleSeconds);

        yield return RunButtonStep(
            "settings.open_menu",
            delegate { buttonController.ToggleSettingsMenu(false); },
            "Setting Menu",
            null,
            null,
            DefaultSettleSeconds);

        yield return RunButtonStep(
            "measurement.toggle_panel",
            delegate { buttonController.ToggleMeasurementSettingUI(false); },
            null,
            "Mesh",
            null,
            DefaultSettleSeconds);

        yield return RunButtonStep(
            "folder_selector.open",
            delegate { buttonController.ShowFolderSelectorMenu(false); },
            null,
            "Mesh",
            null,
            DefaultSettleSeconds);

        yield return RunExhibitionStep(
            "exhibition.open_menu_intro_audio",
            delegate { buttonController.ShowExhibitionMenu(false); },
            "intro_01",
            "Exhibition Mode Menu",
            null);

        yield return RunExhibitionStep(
            "exhibition.manipulation_mode_audio",
            delegate { buttonController.StartExhibitionManipulationMode(); },
            "exp01_00",
            "Exhibition Mode Menu",
            "Manipulation");

        yield return RunExhibitionStep(
            "exhibition.velocity_mode_audio",
            delegate { buttonController.StartExhibitionVelocityMode(); },
            "exp02_00",
            "Exhibition Mode Menu",
            "Velocity");

        yield return RunExhibitionStep(
            "exhibition.slice_mode_audio",
            delegate { buttonController.StartExhibitionSliceMode(); },
            "exp03_00",
            "Exhibition Mode Menu",
            "Slice");

        yield return RunExhibitionStep(
            "exhibition.streamline_mode_audio",
            delegate { buttonController.StartExhibitionStreamlineMode(); },
            "exp04_00",
            "Exhibition Mode Menu",
            "Streamline");

        yield return RunExhibitionStep(
            "exhibition.wss_mode_audio",
            delegate { buttonController.StartExhibitionWssMode(); },
            "exp05_00",
            "Exhibition Mode Menu",
            "Wss");

        int returnHomeAudioLogStart = runtimeLogs.Count;
        yield return RunButtonStep(
            "exhibition.return_home_intro_audio",
            delegate { buttonController.ReturnToExhibitionHome(); },
            "Exhibition Mode Menu",
            null,
            delegate { return HasNewAudioPlaybackLog(returnHomeAudioLogStart, "intro_01") || GuideAudioIsPlaying("intro_01"); },
            AudioSettleSeconds);

        yield return RunButtonStep(
            "final.return_main_menu",
            delegate { buttonController.ShowMainMenu(false); },
            "Main Menu",
            "Mesh",
            null,
            DefaultSettleSeconds);

        WriteReport();
        Debug.Log("Hanyang functional validation completed: " + reportPath);
    }

    private IEnumerator WaitForReadyScene()
    {
        float deadline = Time.realtimeSinceStartup + MaxReadyWaitSeconds;
        string reason = string.Empty;

        while (!TryBindAndCheckReady(out reason) && Time.realtimeSinceStartup < deadline)
        {
            Debug.Log("Hanyang functional validation waiting for ready scene: " + reason);
            yield return new WaitForSecondsRealtime(2f);
        }

        if (!TryBindAndCheckReady(out reason))
        {
            AddStep("ready.timeout", false, "Timed out waiting for ready scene: " + reason, 0, fatalLogs.Count);
        }
        else
        {
            AddStep("ready.scene", true, "Scene ready.", 0, fatalLogs.Count);
        }
    }

    private bool TryBindAndCheckReady(out string reason)
    {
        buttonController = ButtonControllerManager.Instance ?? FindObjectOfType<ButtonControllerManager>();
        manager = Manager.Instance ?? FindObjectOfType<Manager>();

        if (buttonController == null)
        {
            reason = "ButtonControllerManager is not available.";
            return false;
        }

        if (manager == null)
        {
            reason = "Manager is not available.";
            return false;
        }

        if (manager.progress != null && manager.progress.activeInHierarchy)
        {
            reason = "Manager progress UI is still active.";
            return false;
        }

        if (!IsActiveInHierarchy("Main Menu"))
        {
            reason = "Main Menu is not active.";
            return false;
        }

        reason = "ready";
        return true;
    }

    private IEnumerator RunButtonStep(
        string name,
        Action action,
        string expectedActiveMenu,
        string expectedVisualizationMode,
        Func<bool> extraPassCondition,
        float settleSeconds)
    {
        int fatalStart = fatalLogs.Count;
        int logStart = runtimeLogs.Count;
        string detail = string.Empty;
        bool passed = true;

        try
        {
            action();
        }
        catch (Exception exception)
        {
            passed = false;
            detail += "Exception: " + exception.GetType().Name + " " + exception.Message + ". ";
        }

        if (extraPassCondition == null)
        {
            yield return new WaitForSecondsRealtime(settleSeconds);
        }
        else
        {
            float deadline = Time.realtimeSinceStartup + settleSeconds;
            while (Time.realtimeSinceStartup < deadline && !extraPassCondition())
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        if (!string.IsNullOrWhiteSpace(expectedActiveMenu) && !IsActiveInHierarchy(expectedActiveMenu))
        {
            passed = false;
            detail += "Expected active menu not active: " + expectedActiveMenu + ". ";
        }

        if (!string.IsNullOrWhiteSpace(expectedVisualizationMode) && manager != null &&
            !string.Equals(manager.visualizationMode.ToString(), expectedVisualizationMode, StringComparison.OrdinalIgnoreCase))
        {
            passed = false;
            detail += "Expected visualizationMode=" + expectedVisualizationMode + " but was " + manager.visualizationMode + ". ";
        }

        if (extraPassCondition != null && !extraPassCondition())
        {
            passed = false;
            detail += "Extra condition failed. ";
        }

        if (fatalLogs.Count > fatalStart)
        {
            passed = false;
            detail += "Fatal logs captured: " + (fatalLogs.Count - fatalStart) + ". ";
        }

        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = "OK";
        }

        AddStep(name, passed, detail, logStart, fatalStart);
    }

    private IEnumerator RunObservationStep(string name, string description, Func<bool> passCondition, float settleSeconds)
    {
        int fatalStart = fatalLogs.Count;
        int logStart = runtimeLogs.Count;
        yield return new WaitForSecondsRealtime(settleSeconds);

        bool passed = passCondition();
        string detail = passed ? "OK: " + description : "Failed: " + description;
        if (fatalLogs.Count > fatalStart)
        {
            passed = false;
            detail += " Fatal logs captured: " + (fatalLogs.Count - fatalStart) + ".";
        }

        AddStep(name, passed, detail, logStart, fatalStart);
    }

    private IEnumerator RunExhibitionStep(
        string name,
        Action action,
        string expectedAudioKey,
        string expectedActiveMenu,
        string expectedExperience)
    {
        int logStart = runtimeLogs.Count;
        yield return RunButtonStep(
            name,
            action,
            expectedActiveMenu,
            null,
            delegate
            {
                bool audioOk = HasNewAudioPlaybackLog(logStart, expectedAudioKey) || GuideAudioIsPlaying(expectedAudioKey);
                bool experienceOk = string.IsNullOrWhiteSpace(expectedExperience) ||
                    (buttonController != null &&
                     string.Equals(buttonController.CurrentExhibitionExperience.ToString(), expectedExperience, StringComparison.OrdinalIgnoreCase));
                return audioOk && experienceOk;
            },
            AudioSettleSeconds);
    }

    private void AddStep(string name, bool passed, string detail, int logStart, int fatalStart)
    {
        StepResult result = new StepResult();
        result.name = name;
        result.passed = passed;
        result.detail = detail;
        result.activeMenus = GetActiveMenus();
        result.visualizationMode = manager != null ? manager.visualizationMode.ToString() : "(none)";
        result.exhibitionModeActive = buttonController != null && buttonController.IsExhibitionModeActive();
        result.currentExhibitionExperience = buttonController != null ? buttonController.CurrentExhibitionExperience.ToString() : "(none)";
        result.audioPlaying = AnyAudioPlaying();
        result.playingAudioSources = GetPlayingAudioSources();
        result.newFatalLogCount = Mathf.Max(0, fatalLogs.Count - fatalStart);
        result.newFatalLogs = string.Join(" | ", fatalLogs.Skip(fatalStart).Take(8).ToArray());
        result.newGuideAudioLogs = string.Join(" | ", runtimeLogs
            .Skip(logStart)
            .Where(log => log.message.IndexOf("[ExhibitionGuide] Playing", StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(log => log.message)
            .Take(8)
            .ToArray());
        steps.Add(result);
        Debug.Log("Hanyang functional validation step " + name + ": " + (passed ? "PASS" : "FAIL") + " - " + detail);
    }

    private void RecordProbeState(string label)
    {
        Debug.Log("Hanyang functional validation state " + label +
                  ": activeMenus=" + GetActiveMenus() +
                  ", visualizationMode=" + (manager != null ? manager.visualizationMode.ToString() : "(none)") +
                  ", audioPlaying=" + GetPlayingAudioSources());
    }

    private void WriteReport()
    {
        ValidationReport report = new ValidationReport();
        report.projectPath = Application.dataPath.Replace("/Assets", string.Empty);
        report.sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        report.unityVersion = Application.unityVersion;
        report.passed = steps.All(step => step.passed);
        report.passCount = steps.Count(step => step.passed);
        report.failCount = steps.Count(step => !step.passed);
        report.fatalLogCount = fatalLogs.Count;
        report.fatalLogs = fatalLogs.Take(24).ToList();
        report.steps = steps;

        Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? ".");
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
    }

    private void CaptureLog(string condition, string stackTrace, LogType type)
    {
        RuntimeLog log = new RuntimeLog();
        log.type = type.ToString();
        log.message = StripRichText(condition);
        runtimeLogs.Add(log);

        if (IsFatalLog(condition, stackTrace, type))
        {
            fatalLogs.Add(type + ": " + StripRichText(condition));
        }
    }

    private static bool IsFatalLog(string condition, string stackTrace, LogType type)
    {
        if (IsKnownEditorInputNoise(condition, stackTrace))
        {
            return false;
        }

        if (type == LogType.Exception)
        {
            return true;
        }

        return condition.IndexOf("NullReferenceException", StringComparison.OrdinalIgnoreCase) >= 0 ||
               condition.IndexOf("MissingReferenceException", StringComparison.OrdinalIgnoreCase) >= 0 ||
               condition.IndexOf("IndexOutOfRangeException", StringComparison.OrdinalIgnoreCase) >= 0 ||
               condition.IndexOf("ArgumentException", StringComparison.OrdinalIgnoreCase) >= 0 ||
               condition.IndexOf("error CS", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsKnownEditorInputNoise(string condition, string stackTrace)
    {
        if (condition.IndexOf("NullReferenceException", StringComparison.OrdinalIgnoreCase) >= 0 &&
            stackTrace.IndexOf("Microsoft.MixedReality.Toolkit.Input.FocusProvider.TryGetPointerData", StringComparison.OrdinalIgnoreCase) >= 0 &&
            stackTrace.IndexOf("Microsoft.MixedReality.Toolkit.Input.BaseCursor.Update", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (condition.IndexOf("ArgumentNullException", StringComparison.OrdinalIgnoreCase) >= 0 &&
            stackTrace.IndexOf("Microsoft.MixedReality.Toolkit.Utilities.ClippingPrimitive.UpdateRenderers", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }

    private bool HasNewAudioPlaybackLog(int logStart, string expectedAudioKey)
    {
        return runtimeLogs
            .Skip(Mathf.Clamp(logStart, 0, runtimeLogs.Count))
            .Any(log =>
                log.message.IndexOf("[ExhibitionGuide] Playing", StringComparison.OrdinalIgnoreCase) >= 0 &&
                log.message.IndexOf(expectedAudioKey, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool AnyAudioPlaying()
    {
        return Resources.FindObjectsOfTypeAll<AudioSource>()
            .Any(source => source != null && source.gameObject.scene.IsValid() && source.isPlaying);
    }

    private static void ConfigureBatchLoggingForProbe()
    {
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
    }

    private static bool GuideAudioIsPlaying(string expectedAudioKey)
    {
        return Resources.FindObjectsOfTypeAll<AudioSource>()
            .Any(source =>
                source != null &&
                source.gameObject.scene.IsValid() &&
                source.isPlaying &&
                source.gameObject.name.IndexOf("GuideAudioSource", StringComparison.OrdinalIgnoreCase) >= 0 &&
                source.clip != null &&
                source.clip.name.IndexOf(expectedAudioKey, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string GetPlayingAudioSources()
    {
        string[] names = Resources.FindObjectsOfTypeAll<AudioSource>()
            .Where(source => source != null && source.gameObject.scene.IsValid() && source.isPlaying)
            .Select(source =>
                GetPath(source.transform) +
                "(clip=" + (source.clip != null ? source.clip.name : "null") + ")")
            .OrderBy(name => name)
            .ToArray();
        return names.Length == 0 ? "(none)" : string.Join(", ", names);
    }

    private static bool IsActiveInHierarchy(string objectName)
    {
        GameObject go = FindSceneGameObject(objectName);
        return go != null && go.activeInHierarchy;
    }

    private static GameObject FindSceneGameObject(string objectName)
    {
        return Resources.FindObjectsOfTypeAll<GameObject>()
            .FirstOrDefault(go =>
                go != null &&
                go.scene.IsValid() &&
                string.Equals(go.name, objectName, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetActiveMenus()
    {
        List<string> active = new List<string>();
        foreach (string menuName in MenuNames)
        {
            GameObject go = FindSceneGameObject(menuName);
            if (go != null && go.activeInHierarchy)
            {
                active.Add(menuName);
            }
        }

        return active.Count == 0 ? "(none)" : string.Join(", ", active.ToArray());
    }

    private static string GetArgument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string StripRichText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("<color=cyan>", string.Empty)
            .Replace("<color=green>", string.Empty)
            .Replace("<color=yellow>", string.Empty)
            .Replace("<color=red>", string.Empty)
            .Replace("<color=magenta>", string.Empty)
            .Replace("</color>", string.Empty);
    }

    private static string GetPath(Transform transform)
    {
        if (transform == null)
        {
            return "(null)";
        }

        List<string> names = new List<string>();
        Transform current = transform;
        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names.ToArray());
    }

    [Serializable]
    private sealed class ValidationReport
    {
        public string projectPath;
        public string sceneName;
        public string unityVersion;
        public bool passed;
        public int passCount;
        public int failCount;
        public int fatalLogCount;
        public List<string> fatalLogs = new List<string>();
        public List<StepResult> steps = new List<StepResult>();
    }

    [Serializable]
    private sealed class StepResult
    {
        public string name;
        public bool passed;
        public string detail;
        public string activeMenus;
        public string visualizationMode;
        public bool exhibitionModeActive;
        public string currentExhibitionExperience;
        public bool audioPlaying;
        public string playingAudioSources;
        public int newFatalLogCount;
        public string newFatalLogs;
        public string newGuideAudioLogs;
    }

    private sealed class RuntimeLog
    {
        public string type;
        public string message;
    }
}
