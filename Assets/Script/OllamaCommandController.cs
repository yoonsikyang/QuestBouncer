using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class OllamaChatRequest
{
    public string model;
    public OllamaMessage[] messages;
    public bool stream;
}

[Serializable]
public class OllamaMessage
{
    public string role;
    public string content;
}

[Serializable]
public class OllamaChatResponse
{
    public string model;
    public OllamaResponseMessage message;
}

[Serializable]
public class OllamaResponseMessage
{
    public string role;
    public string content;
    public OllamaResponseToolCall[] tool_calls;
}

[Serializable]
public class OllamaResponseToolCall
{
    public OllamaResponseFunction function;
}

[Serializable]
public class OllamaResponseFunction
{
    public string name;
    public OllamaResponseArguments arguments;
}

[Serializable]
public class OllamaResponseArguments
{
    public string action_name = "";
    public string mode = "";
    public float value = -999f;
    public string axis = "";

    public float bloodAlpha = -999f;
    public float heatmapIntensity = -999f;
    public float velocityArrowScale = -999f;
    public float wssArrowScale = -999f;
    public float streamlineLineWidth = -999f;
}

public class OllamaCommandController : MonoBehaviour
{
    private const string ToolSchemaJson = @"[
        {
            ""type"": ""function"",
            ""function"": {
                ""name"": ""execute_app_action"",
                ""description"": ""Navigate menus, toggle app states, or run reset actions."",
                ""parameters"": {
                    ""type"": ""object"",
                    ""properties"": {
                        ""action_name"": {
                            ""type"": ""string"",
                            ""enum"": [
                                ""ShowMain"", ""ShowStreamline"", ""ShowVelocity"", ""ShowWss"", ""ShowWssPlayback"",
                                ""ReturnWssMenu"", ""ShowVisSetting"", ""ShowVelocityPlayback"", ""ShowVelocityInterval"",
                                ""ShowVelocityVisSetting"", ""ReturnVisSetting"", ""ReturnVelocityHome"",
                                ""ShowStreamlineSpeed"", ""ReturnStreamlineHome"", ""ReturnStreamlineMenu"",
                                ""ReturnStreamlineMain"", ""ResetVessel"", ""ResetApp"", ""ShowVelocityArrows"",
                                ""ShowVelocityHeatmap"", ""ShowVelocityMenuRoot"", ""ToggleVelocityPlayback"",
                                ""ToggleStreamlinePlayback"", ""ToggleWSSPlayback"", ""ToggleSliceAxis"",
                                ""ShowSettings"", ""ShowFolderSelector"", ""ShowMeasurement"",
                                ""ToggleEnableMeasurement"", ""ToggleObjectMoveMode"", ""ShowPlaySetting"", ""Toggle2D3D""
                            ]
                        }
                    },
                    ""required"": [""action_name""]
                }
            }
        },
        {
            ""type"": ""function"",
            ""function"": {
                ""name"": ""set_slider_control"",
                ""description"": ""Set a numeric control value. For playback speed, 0 is fastest and 100 is slowest."",
                ""parameters"": {
                    ""type"": ""object"",
                    ""properties"": {
                        ""mode"": {
                            ""type"": ""string"",
                            ""enum"": [
                                ""SlicePosition"", ""VelocityPlayback"", ""WssPlayback"", ""StreamlinePlayback"",
                                ""DensityX"", ""DensityY"", ""DensityZ"", ""FrameControl"", ""VesselSize"", ""Rotation""
                            ]
                        },
                        ""value"": {
                            ""type"": ""number"",
                            ""minimum"": 0,
                            ""maximum"": 100,
                            ""description"": ""Target value from 0 to 100.""
                        },
                        ""axis"": {
                            ""type"": ""string"",
                            ""enum"": [""None"", ""X_Axis"", ""Y_Axis""],
                            ""description"": ""Use only for SlicePosition. Use None for other modes.""
                        }
                    },
                    ""required"": [""mode"", ""value"", ""axis""]
                }
            }
        },
        {
            ""type"": ""function"",
            ""function"": {
                ""name"": ""update_visualization_settings"",
                ""description"": ""Update visualization settings such as vessel alpha, heatmap intensity, arrow scale, and streamline width."",
                ""parameters"": {
                    ""type"": ""object"",
                    ""properties"": {
                        ""bloodAlpha"": { ""type"": ""number"" },
                        ""heatmapIntensity"": { ""type"": ""number"" },
                        ""velocityArrowScale"": { ""type"": ""number"" },
                        ""wssArrowScale"": { ""type"": ""number"" },
                        ""streamlineLineWidth"": { ""type"": ""number"" }
                    }
                }
            }
        },
        {
            ""type"": ""function"",
            ""function"": {
                ""name"": ""get_current_state"",
                ""description"": ""Get current visualization and slider state."",
                ""parameters"": {
                    ""type"": ""object"",
                    ""properties"": {}
                }
            }
        }
    ]";

    [Header("Ollama Settings")]
    [Tooltip("IP of the PC running Ollama. Use localhost in the Unity Editor.")]
    public string ollamaIP = "localhost";
    public string ollamaPort = "11434";
    public string modelName = "llama3.2:3b";

    [Header("References")]
    public ButtonControllerManager buttonManager;

    private string apiUrl;

    private void Start()
    {
        apiUrl = $"http://{ollamaIP}:{ollamaPort}/api/chat";

        if (buttonManager == null)
        {
            buttonManager = FindObjectOfType<ButtonControllerManager>();
            if (buttonManager == null)
            {
                Debug.LogWarning("[OllamaCommandController] ButtonControllerManager not found in scene.");
            }
        }
    }

    public void ProcessVoiceCommand(string recognizedText)
    {
        if (string.IsNullOrWhiteSpace(recognizedText)) return;

        if (TryHandleRuleBasedCommand(recognizedText))
        {
            return;
        }

        Debug.Log($"[OllamaCommandController] Sending command to Ollama: {recognizedText}");
        StartCoroutine(SendToOllamaRoutine(recognizedText));
    }

    private bool TryHandleRuleBasedCommand(string recognizedText)
    {
        string text = NormalizeCommand(recognizedText);
        bool hasNumber = ContainsDigit(text);
        bool isMenuRequest = ContainsAny(text, "보여", "보고", "싶어", "띄워", "열어", "들어", "선택", "눌러", "메뉴", "show", "open", "view", "menu");

        if (ContainsAny(text, "혈관위치초기화", "혈관초기화", "resetvessel"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ResetVessel));
            return true;
        }

        if (ContainsAny(text, "앱초기화", "어플초기화", "resetapp"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ResetApp));
            return true;
        }

        if (!hasNumber && ContainsAny(text, "스트림라인재생속도설정", "스트림라인재생속도메뉴", "streamlineplayback"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ShowStreamlineSpeed));
            return true;
        }

        if (!hasNumber && ContainsAny(text, "wss재생속도설정", "wss재생속도메뉴", "wssplayback"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ShowWssPlayback));
            return true;
        }

        if (!hasNumber && (ContainsAny(text, "재생속도설정", "재생속도메뉴", "velocityplayback") || (text.Contains("재생") && text.Contains("속도") && isMenuRequest)))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ShowVelocityPlayback));
            return true;
        }

        if (ContainsAny(text, "재생설정", "플레이설정", "playsetting"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ShowPlaySetting));
            return true;
        }

        if (!hasNumber && (ContainsAny(text, "단면간격설정", "단면간격메뉴") || (text.Contains("단면") && text.Contains("간격") && isMenuRequest)))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ShowVelocityInterval));
            return true;
        }

        if (!hasNumber && (ContainsAny(text, "단면시각화설정", "단면시각화메뉴") || (text.Contains("단면") && text.Contains("시각화") && isMenuRequest)))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ShowVelocityVisSetting));
            return true;
        }

        if (ContainsAny(text, "단면속도장", "속도장화살표", "velocityarrows"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ShowVelocityArrows));
            return true;
        }

        if (ContainsAny(text, "히트맵", "heatmap"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ShowVelocityHeatmap));
            return true;
        }

        if (ContainsAny(text, "축변경", "축바꿔", "toggleaxis"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ToggleSliceAxis));
            return true;
        }

        if (ContainsAny(text, "폴더", "데이터변경", "folder"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ShowFolderSelector));
            return true;
        }

        if (ContainsAny(text, "길이측정버튼", "측정켜", "측정꺼", "enablemeasurement"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ToggleEnableMeasurement));
            return true;
        }

        if (ContainsAny(text, "측정", "measurement"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ShowMeasurement));
            return true;
        }

        if (ContainsAny(text, "혈관조작", "오브젝트이동", "objectmove"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ToggleObjectMoveMode));
            return true;
        }

        if (ContainsAny(text, "2d3d", "2d/3d", "3d2d"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.Toggle2D3D));
            return true;
        }

        if (ContainsAny(text, "스트림라인재생", "streamlineplay"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ToggleStreamlinePlayback));
            return true;
        }

        if (ContainsAny(text, "wss재생", "wssplay"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ToggleWSSPlayback));
            return true;
        }

        if (ContainsAny(text, "속도장재생", "velocityplay"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ToggleVelocityPlayback));
            return true;
        }

        if (ContainsAny(text, "메인", "홈", "main", "home"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ShowMain));
            return true;
        }

        if (ContainsAny(text, "시각화설정", "visualizationsettings"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ShowVisSetting));
            return true;
        }

        if (ContainsAny(text, "전체설정", "총설정", "settings") && isMenuRequest)
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ShowSettings));
            return true;
        }

        if (isMenuRequest && ContainsAny(text, "스트림라인", "streamline"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ShowStreamline));
            return true;
        }

        if (isMenuRequest && ContainsAny(text, "혈관속도", "속도장", "벨로시티", "velocity"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ShowVelocity));
            return true;
        }

        if (isMenuRequest && ContainsAny(text, "wss", "전단응력", "벽면전단"))
        {
            ExecuteAppAction(nameof(ButtonControllerManager.ButtonAction.ShowWss));
            return true;
        }

        return false;
    }

    private static string NormalizeCommand(string text)
    {
        return text.Replace(" ", "").Replace("\t", "").Trim().ToLowerInvariant();
    }

    private static bool ContainsDigit(string text)
    {
        foreach (char c in text)
        {
            if (char.IsDigit(c))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAny(string text, params string[] tokens)
    {
        foreach (string token in tokens)
        {
            if (text.Contains(token))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerator SendToOllamaRoutine(string userText)
    {
        OllamaChatRequest requestData = new OllamaChatRequest
        {
            model = modelName,
            stream = false,
            messages = new[]
            {
                new OllamaMessage
                {
                    role = "system",
                    content = "You control a HoloLens medical visualization app. The user may speak Korean. Use tool calls when an app action is needed. Use execute_app_action for menu navigation and toggles. Use set_slider_control for speed, slice, density, vessel size, and rotation. Slider values use 0 to 100. For playback speed, 0 is fastest and 100 is slowest."
                },
                new OllamaMessage
                {
                    role = "user",
                    content = userText
                }
            }
        };

        string jsonPayload = JsonUtility.ToJson(requestData);
        jsonPayload = jsonPayload.Substring(0, jsonPayload.Length - 1) + ",\"tools\":" + ToolSchemaJson + "}";

        using (UnityWebRequest request = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"[OllamaCommandController] Error connecting to Ollama: {request.error}");
                Debug.LogError($"[OllamaCommandController] Response: {request.downloadHandler.text}");
                yield break;
            }

            ParseOllamaResponse(request.downloadHandler.text);
        }
    }

    private void ParseOllamaResponse(string responseJson)
    {
        try
        {
            OllamaChatResponse response = JsonUtility.FromJson<OllamaChatResponse>(responseJson);

            if (response == null || response.message == null)
            {
                Debug.LogWarning("[OllamaCommandController] Empty Ollama response.");
                return;
            }

            if (response.message.tool_calls == null || response.message.tool_calls.Length == 0)
            {
                Debug.Log($"[OllamaCommandController] LLM response contained no tool calls. Response: {response.message.content}");
                return;
            }

            foreach (var toolCall in response.message.tool_calls)
            {
                if (toolCall?.function == null)
                {
                    continue;
                }

                string functionName = toolCall.function.name;
                OllamaResponseArguments args = toolCall.function.arguments ?? new OllamaResponseArguments();
                Debug.Log($"[OllamaCommandController] LLM triggered tool: {functionName}");

                switch (functionName)
                {
                    case "execute_app_action":
                        ExecuteAppAction(args.action_name);
                        break;
                    case "set_slider_control":
                        SetSliderControl(args.mode, args.value, args.axis);
                        break;
                    case "update_visualization_settings":
                        UpdateVisualizationSettings(args);
                        break;
                    case "get_current_state":
                        GetCurrentState();
                        break;
                    default:
                        Debug.LogWarning($"[OllamaCommandController] Unknown tool requested by LLM: {functionName}");
                        break;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[OllamaCommandController] Error parsing Ollama JSON: {e.Message}");
            Debug.LogError($"[OllamaCommandController] Raw response: {responseJson}");
        }
    }

    private void ExecuteAppAction(string actionName)
    {
        if (buttonManager == null)
        {
            buttonManager = ButtonControllerManager.Instance ?? FindObjectOfType<ButtonControllerManager>();
            if (buttonManager == null)
            {
                Debug.LogWarning("[OllamaCommandController] ButtonControllerManager is not assigned.");
                return;
            }

            if (ButtonControllerManager.Instance == null)
            {
                ButtonControllerManager.Instance = buttonManager;
            }
        }

        if (string.IsNullOrEmpty(actionName))
        {
            Debug.LogWarning("[OllamaCommandController] Missing action_name parameter.");
            return;
        }

        if (Enum.TryParse(actionName, out ButtonControllerManager.ButtonAction action))
        {
            buttonManager.RunAction(action);
            Debug.Log($"[OllamaCommandController] Action {actionName} executed successfully.");
            return;
        }

        Debug.LogWarning($"[OllamaCommandController] Unknown action {actionName}.");
    }

    private void SetSliderControl(string modeStr, float value, string axisStr)
    {
        if (value < -900f)
        {
            Debug.LogWarning("[OllamaCommandController] Missing value parameter for slider.");
            return;
        }

        GlobalSliderController sliderController = FindObjectOfType<GlobalSliderController>();
        if (sliderController == null)
        {
            Debug.LogWarning("[OllamaCommandController] GlobalSliderController not found.");
            return;
        }

        if (!Enum.TryParse(modeStr, out ControlMode mode))
        {
            Debug.LogWarning($"[OllamaCommandController] Unknown slider mode {modeStr}.");
            return;
        }

        SliceController.SliceAxis axis = SliceController.SliceAxis.None;
        if (!string.IsNullOrEmpty(axisStr))
        {
            Enum.TryParse(axisStr, out axis);
        }

        float normalizedValue = value > 1f ? value / 100f : value;
        normalizedValue = Mathf.Clamp01(normalizedValue);

        sliderController.SetMode(mode, axis);
        sliderController.SetValue(normalizedValue, true, false);

        Debug.Log($"[OllamaCommandController] Slider {modeStr} set to {value} (normalized: {normalizedValue}).");
    }

    private void UpdateVisualizationSettings(OllamaResponseArguments args)
    {
        VisualizationSettings store = VisualizationSettingsStore.LoadSettings();

        if (args.bloodAlpha > -900f) store.bloodAlpha = args.bloodAlpha;
        if (args.heatmapIntensity > -900f) store.heatmapIntensity = args.heatmapIntensity;
        if (args.velocityArrowScale > -900f) store.velocityArrowScale = args.velocityArrowScale;
        if (args.wssArrowScale > -900f) store.wssArrowScale = args.wssArrowScale;
        if (args.streamlineLineWidth > -900f) store.streamlineLineWidth = args.streamlineLineWidth;

        VisualizationSettingsStore.SaveSettings(store);

        if (Manager.Instance != null)
        {
            Manager.Instance.LoadAndApplySettings(false);
        }

        Debug.Log("[OllamaCommandController] Visualization settings updated.");
    }

    private void GetCurrentState()
    {
        VisualizationSettings store = VisualizationSettingsStore.LoadSettings();
        Debug.Log($"[OllamaCommandController] Current State: bloodAlpha={store.bloodAlpha}, heatmapIntensity={store.heatmapIntensity}, velocityArrowScale={store.velocityArrowScale}, wssArrowScale={store.wssArrowScale}, streamlineLineWidth={store.streamlineLineWidth}");
    }
}
