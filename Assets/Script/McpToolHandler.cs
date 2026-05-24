using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

[Serializable]
public class McpRequest
{
    public string jsonrpc;
    public string id;
    public string method;
    public McpParams @params;
}

[Serializable]
public class McpParams
{
    public string name;
    public McpArguments arguments;
}

[Serializable]
public class McpArguments
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

[Serializable]
public class McpResponse
{
    public string jsonrpc = "2.0";
    public string id;
    public McpResult result;
    public McpError error;
}

[Serializable]
public class McpResult
{
    public List<McpContent> content;
    public bool isError = false;
}

[Serializable]
public class McpContent
{
    public string type = "text";
    public string text;
}

[Serializable]
public class McpError
{
    public int code;
    public string message;
}

public class McpToolHandler : MonoBehaviour
{
    private class PendingMcpMessage
    {
        public string JsonMessage;
        public string SessionId;
    }

    public static McpToolHandler Instance { get; private set; }

    private ConcurrentQueue<PendingMcpMessage> messageQueue = new ConcurrentQueue<PendingMcpMessage>();
    private McpServerManager subscribedServer;
    private string currentResponseSessionId;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        EnsureSubscribed();
    }

    private void OnEnable()
    {
        EnsureSubscribed();
    }

    private void OnDisable()
    {
        UnsubscribeFromServer();
    }

    private void OnDestroy()
    {
        UnsubscribeFromServer();
    }

    private void SubscribeToServer()
    {
        if (subscribedServer == McpServerManager.Instance && subscribedServer != null) return;

        UnsubscribeFromServer();

        if (McpServerManager.Instance != null)
        {
            subscribedServer = McpServerManager.Instance;
            subscribedServer.OnJsonRpcMessageReceived += HandleIncomingMessage;
        }
        else
        {
            Debug.LogWarning("[McpToolHandler] McpServerManager instance not found.");
        }
    }

    public void EnsureSubscribed()
    {
        if (Instance == null)
        {
            Instance = this;
        }

        if (Instance == this)
        {
            SubscribeToServer();
        }
    }

    private void UnsubscribeFromServer()
    {
        if (subscribedServer != null)
        {
            subscribedServer.OnJsonRpcMessageReceived -= HandleIncomingMessage;
            subscribedServer = null;
        }
    }

    private void HandleIncomingMessage(string jsonMessage, string sessionId)
    {
        ReceiveMessage(jsonMessage, sessionId);
    }

    public void ReceiveMessage(string jsonMessage)
    {
        ReceiveMessage(jsonMessage, null);
    }

    public void ReceiveMessage(string jsonMessage, string sessionId)
    {
        // Enqueue to be processed on the main thread
        messageQueue.Enqueue(new PendingMcpMessage
        {
            JsonMessage = jsonMessage,
            SessionId = sessionId
        });
    }

    private void Update()
    {
        while (messageQueue.TryDequeue(out PendingMcpMessage message))
        {
            currentResponseSessionId = message.SessionId;
            try
            {
                ProcessMessage(message.JsonMessage);
            }
            finally
            {
                currentResponseSessionId = null;
            }
        }
    }

    private void SendJsonResponse(string json)
    {
        if (McpServerManager.Instance != null)
        {
            McpServerManager.Instance.SendMessageToClient(currentResponseSessionId, json);
        }
    }

    private void ProcessMessage(string jsonMessage)
    {
        McpRequest req = new McpRequest();
        req.@params = new McpParams();
        req.@params.arguments = new McpArguments();

        try
        {
            JsonUtility.FromJsonOverwrite(jsonMessage, req);
        }
        catch (Exception e)
        {
            Debug.LogError($"[McpToolHandler] Failed to parse JSON: {e.Message}");
            SendErrorResponse(req?.id, -32700, "Parse error");
            return;
        }

        if (string.IsNullOrEmpty(req.jsonrpc))
        {
            // Initial tools/list or similar might be requested
        }

        if (req.method == "initialize")
        {
            HandleInitialize(req);
        }
        else if (req.method == "notifications/initialized")
        {
            Debug.Log("[McpToolHandler] Client initialized notification received.");
        }
        else if (req.method == "tools/list")
        {
            HandleToolsList(req);
        }
        else if (req.method == "tools/call")
        {
            HandleToolsCall(req);
        }
        else
        {
            SendErrorResponse(req.id, -32601, $"Method not found: {req.method}");
        }
    }

    private void HandleInitialize(McpRequest req)
    {
        string response = "{"
            + "\"jsonrpc\":\"2.0\","
            + "\"id\":\"" + EscapeJson(req.id) + "\","
            + "\"result\":{"
            + "\"protocolVersion\":\"2024-11-05\","
            + "\"capabilities\":{\"tools\":{\"listChanged\":false}},"
            + "\"serverInfo\":{\"name\":\"hanyang-hololens-unity\",\"version\":\"0.1.0\"}"
            + "}"
            + "}";

        SendJsonResponse(response);
    }

    private void HandleToolsList(McpRequest req)
    {
        // For tools/list, we need to return the list of tools.
        // JsonUtility is very limited with nested arrays of complex objects.
        // Instead of building a complex class for tools/list, we can construct the JSON string directly.
        string toolsJson = @"
        {
            ""jsonrpc"": ""2.0"",
            ""id"": """ + req.id + @""",
            ""result"": {
                ""tools"": [
                    {
                        ""name"": ""execute_app_action"",
                        ""description"": ""Execute an app action like menu toggle or reset"",
                        ""inputSchema"": {
                            ""type"": ""object"",
                            ""properties"": {
                                ""action_name"": { ""type"": ""string"" }
                            },
                            ""required"": [""action_name""]
                        }
                    },
                    {
                        ""name"": ""set_slider_control"",
                        ""description"": ""Set a slider/control value for the visualization. mode must be one of: VelocityPlayback (blood flow speed), WssPlayback (WSS animation speed), StreamlinePlayback (streamline speed), SlicePosition (slice plane), DensityX/DensityY/DensityZ (arrow density), FrameControl (global frame), VesselSize (scale the vessel model), Rotation (rotate the vessel). value is 0-100 where for playback speeds: 0=fastest, 100=slowest. For VesselSize: 0=smallest(0.1x), 50=normal(1x), 100=largest(5x). For Rotation: 0=-180deg, 50=0deg, 100=+180deg. axis is optional: X_Axis or Y_Axis for SlicePosition."",
                        ""inputSchema"": {
                            ""type"": ""object"",
                            ""properties"": {
                                ""mode"": { ""type"": ""string"", ""enum"": [""VelocityPlayback"", ""WssPlayback"", ""StreamlinePlayback"", ""SlicePosition"", ""DensityX"", ""DensityY"", ""DensityZ"", ""FrameControl"", ""VesselSize"", ""Rotation""] },
                                ""value"": { ""type"": ""number"", ""minimum"": 0, ""maximum"": 100 },
                                ""axis"": { ""type"": ""string"", ""enum"": [""None"", ""X_Axis"", ""Y_Axis""] }
                            },
                            ""required"": [""mode"", ""value""]
                        }
                    },
                    {
                        ""name"": ""update_visualization_settings"",
                        ""description"": ""Update visualization settings"",
                        ""inputSchema"": {
                            ""type"": ""object"",
                            ""properties"": {
                                ""bloodAlpha"": { ""type"": ""number"" },
                                ""heatmapIntensity"": { ""type"": ""number"" },
                                ""velocityArrowScale"": { ""type"": ""number"" },
                                ""wssArrowScale"": { ""type"": ""number"" },
                                ""streamlineLineWidth"": { ""type"": ""number"" }
                            }
                        }
                    },
                    {
                        ""name"": ""get_current_state"",
                        ""description"": ""Get current app state"",
                        ""inputSchema"": {
                            ""type"": ""object"",
                            ""properties"": {}
                        }
                    }
                ]
            }
        }";

        SendJsonResponse(toolsJson);
    }

    private void HandleToolsCall(McpRequest req)
    {
        string toolName = req.@params?.name;
        McpArguments args = req.@params?.arguments;

        if (string.IsNullOrEmpty(toolName) || args == null)
        {
            SendErrorResponse(req.id, -32602, "Invalid params");
            return;
        }

        string responseText = "";
        bool isError = false;

        try
        {
            Debug.Log($"[McpToolHandler] Tool call received: {toolName}");

            switch (toolName)
            {
                case "execute_app_action":
                    responseText = ExecuteAppAction(args.action_name);
                    break;
                case "set_slider_control":
                    responseText = SetSliderControl(args.mode, args.value, args.axis);
                    break;
                case "update_visualization_settings":
                    responseText = UpdateVisualizationSettings(args);
                    break;
                case "get_current_state":
                    responseText = GetCurrentState();
                    break;
                default:
                    SendErrorResponse(req.id, -32601, $"Tool not found: {toolName}");
                    return;
            }
        }
        catch (Exception e)
        {
            isError = true;
            responseText = $"Exception: {e.Message}\n{e.StackTrace}";
        }

        if (isError || responseText.StartsWith("Error:"))
        {
            Debug.LogWarning($"[McpToolHandler] {responseText}");
        }
        else
        {
            Debug.Log($"[McpToolHandler] {responseText}");
        }

        SendSuccessResponse(req.id, responseText, isError);
    }

    private string ExecuteAppAction(string actionName)
    {
        if (string.IsNullOrEmpty(actionName)) return "Error: action_name is empty";
        
        ButtonControllerManager buttonManager = ButtonControllerManager.Instance ?? UnityEngine.Object.FindObjectOfType<ButtonControllerManager>();
        if (buttonManager != null && ButtonControllerManager.Instance == null)
        {
            ButtonControllerManager.Instance = buttonManager;
        }

        // Use ButtonControllerManager to execute action
        if (buttonManager != null)
        {
            if (Enum.TryParse(actionName, out ButtonControllerManager.ButtonAction action))
            {
                buttonManager.RunAction(action);
                return $"Action {actionName} executed successfully.";
            }
            else
            {
                return $"Error: Unknown action {actionName}.";
            }
        }
        return "Error: ButtonControllerManager instance not found.";
    }

    private string SetSliderControl(string modeStr, float value, string axisStr)
    {
        if (value < -900f) return "Error: value is required.";
        
        var sliderController = UnityEngine.Object.FindObjectOfType<GlobalSliderController>();
        if (sliderController != null)
        {
            if (Enum.TryParse(modeStr, out ControlMode mode))
            {
                SliceController.SliceAxis axis = SliceController.SliceAxis.None;
                if (!string.IsNullOrEmpty(axisStr))
                {
                    Enum.TryParse(axisStr, out axis);
                }

                sliderController.SetMode(mode, axis);
                
                // Standardize 0-100 to 0-1
                float normalizedValue = Mathf.Clamp01(value / 100f);
                sliderController.SetValue(normalizedValue, true, false);

                if (mode == ControlMode.FrameControl)
                {
                    ApplyFrameControl(normalizedValue);
                }

                return $"Slider {modeStr} set to {value} (normalized: {normalizedValue}){(axis != SliceController.SliceAxis.None ? $" on axis {axis}" : "")}.";
            }
            return $"Error: Unknown mode {modeStr}.";
        }
        return "Error: GlobalSliderController instance not found.";
    }

    private void ApplyFrameControl(float normalizedValue)
    {
        var buttonManager = ButtonControllerManager.Instance ?? UnityEngine.Object.FindObjectOfType<ButtonControllerManager>();
        if (buttonManager == null) return;

        buttonManager.isFrameControlMode = true;
        buttonManager.SetAllLoadersFrameBySlider(normalizedValue);
    }

    private string UpdateVisualizationSettings(McpArguments args)
    {
        var store = VisualizationSettingsStore.LoadSettings();
        string changes = "Updated settings: ";
        
        if (args.bloodAlpha > -900f) { store.bloodAlpha = args.bloodAlpha; changes += $"bloodAlpha={args.bloodAlpha} "; }
        if (args.heatmapIntensity > -900f) { store.heatmapIntensity = args.heatmapIntensity; changes += $"heatmapIntensity={args.heatmapIntensity} "; }
        if (args.velocityArrowScale > -900f) { store.velocityArrowScale = args.velocityArrowScale; changes += $"velocityArrowScale={args.velocityArrowScale} "; }
        if (args.wssArrowScale > -900f) { store.wssArrowScale = args.wssArrowScale; changes += $"wssArrowScale={args.wssArrowScale} "; }
        if (args.streamlineLineWidth > -900f) { store.streamlineLineWidth = args.streamlineLineWidth; changes += $"streamlineLineWidth={args.streamlineLineWidth} "; }
        
        VisualizationSettingsStore.SaveSettings(store);
        
        if (Manager.Instance != null)
        {
            Manager.Instance.LoadAndApplySettings(false);
        }

        if (changes == "Updated settings: ") return "No valid settings provided to update.";
        return changes;
    }

    private string GetCurrentState()
    {
        var store = VisualizationSettingsStore.LoadSettings();
        var slider = UnityEngine.Object.FindObjectOfType<GlobalSliderController>();
        var buttonManager = ButtonControllerManager.Instance ?? UnityEngine.Object.FindObjectOfType<ButtonControllerManager>();
        if (buttonManager != null && ButtonControllerManager.Instance == null)
        {
            ButtonControllerManager.Instance = buttonManager;
        }
        var measurementTool = UnityEngine.Object.FindObjectOfType<VesselMeasurementTool>();
        var manager = Manager.Instance ?? UnityEngine.Object.FindObjectOfType<Manager>();
        var slice = UnityEngine.Object.FindObjectOfType<SliceController>();

        string rawActiveMenu = ResolveRawActiveMenu(buttonManager, slider);
        string activeMenu = NormalizeActiveMenu(rawActiveMenu);
        string currentTargetContext = ResolveCurrentTargetContext(activeMenu);
        string sliderMode = slider != null ? slider.mode.ToString() : "None";
        float sliderValue = slider != null ? slider.sliderValue * 100f : 0f;
        bool sliderActive = slider != null && slider.IsActive;
        string sliceAxis = slice != null ? slice.currentAxis.ToString() : "unknown";
        float slicePosition = slice != null ? slice.slicePosition : -1f;
        bool measurementEnabled = measurementTool != null && measurementTool.enableMeasurement;
        bool objectMoveMode = measurementTool != null && measurementTool.objectMoveMode;
        bool frameControlEnabled = buttonManager != null && buttonManager.isFrameControlMode;
        string visualizationMode = manager != null ? manager.visualizationMode.ToString() : "unknown";
        string viewMode = slice == null ? "unknown" : (slice.show3DArrows ? "3d" : "2d");
        string dataFolder = manager != null ? manager.currentDataFolder : null;

        StringBuilder state = new StringBuilder();
        state.AppendLine("{");
        state.AppendLine("  \"schemaVersion\": 1,");
        state.AppendLine($"  \"bloodAlpha\": {FormatFloat(store.bloodAlpha)},");
        state.AppendLine($"  \"heatmapIntensity\": {FormatFloat(store.heatmapIntensity)},");
        state.AppendLine($"  \"velocityArrowScale\": {FormatFloat(store.velocityArrowScale)},");
        state.AppendLine($"  \"wssArrowScale\": {FormatFloat(store.wssArrowScale)},");
        state.AppendLine($"  \"streamlineLineWidth\": {FormatFloat(store.streamlineLineWidth)},");
        state.AppendLine($"  \"activeMenuRaw\": \"{EscapeJson(rawActiveMenu)}\",");
        state.AppendLine($"  \"activeMenu\": \"{EscapeJson(activeMenu)}\",");
        state.AppendLine($"  \"currentTargetContext\": {NullableJsonString(currentTargetContext)},");
        state.AppendLine($"  \"enableMeasurement\": {FormatBool(measurementEnabled)},");
        state.AppendLine($"  \"objectMoveMode\": {FormatBool(objectMoveMode)},");
        state.AppendLine("  \"playback\": {");
        state.AppendLine($"    \"velocity\": \"{EscapeJson(GetVelocityPlaybackState(manager))}\",");
        state.AppendLine($"    \"streamline\": \"{EscapeJson(GetStreamlinePlaybackState(manager))}\",");
        state.AppendLine($"    \"wss\": \"{EscapeJson(GetWssPlaybackState(manager))}\"");
        state.AppendLine("  },");
        state.AppendLine("  \"slice\": {");
        state.AppendLine($"    \"axis\": \"{EscapeJson(sliceAxis)}\",");
        state.AppendLine($"    \"position\": {(slicePosition >= 0f ? FormatFloat(slicePosition * 100f) : "null")}");
        state.AppendLine("  },");
        state.AppendLine($"  \"visualizationMode\": \"{EscapeJson(visualizationMode)}\",");
        state.AppendLine($"  \"viewMode\": \"{EscapeJson(viewMode)}\",");
        state.AppendLine("  \"frameControl\": {");
        state.AppendLine($"    \"enabled\": {FormatBool(frameControlEnabled)},");
        state.AppendLine($"    \"value\": {(sliderMode == "FrameControl" ? FormatFloat(sliderValue) : "null")}");
        state.AppendLine("  },");
        state.AppendLine("  \"slider\": {");
        state.AppendLine($"    \"mode\": \"{EscapeJson(sliderMode)}\",");
        state.AppendLine($"    \"value\": {FormatFloat(sliderValue)},");
        state.AppendLine($"    \"active\": {FormatBool(sliderActive)}");
        state.AppendLine("  },");
        state.AppendLine("  \"visualSettings\": {");
        state.AppendLine($"    \"bloodAlpha\": {FormatFloat(store.bloodAlpha)},");
        state.AppendLine($"    \"heatmapIntensity\": {FormatFloat(store.heatmapIntensity)},");
        state.AppendLine($"    \"velocityArrowScale\": {FormatFloat(store.velocityArrowScale)},");
        state.AppendLine($"    \"wssArrowScale\": {FormatFloat(store.wssArrowScale)},");
        state.AppendLine($"    \"streamlineLineWidth\": {FormatFloat(store.streamlineLineWidth)}");
        state.AppendLine("  },");
        state.AppendLine("  \"dataset\": {");
        state.AppendLine($"    \"folder\": {NullableJsonString(dataFolder)},");
        state.AppendLine("    \"caseName\": null");
        state.AppendLine("  },");
        state.AppendLine("  \"lastSuccessfulCommand\": null,");
        state.AppendLine("  \"pendingClarification\": null,");
        state.AppendLine($"  \"currentSliderMode\": \"{EscapeJson(sliderMode)}\",");
        state.AppendLine($"  \"currentSliderValue\": {FormatFloat(sliderValue)}");
        state.Append("}");
        return state.ToString();
    }

    private string ResolveRawActiveMenu(ButtonControllerManager buttonManager, GlobalSliderController slider)
    {
        string activeMenu = "None";
        if (buttonManager != null)
        {
            var activeMenuAction = buttonManager.GetActiveMenuAction();
            if (activeMenuAction.HasValue)
            {
                activeMenu = activeMenuAction.Value.ToString();
            }

            if (activeMenu == "ShowWss" && slider != null && slider.IsActive && slider.mode == ControlMode.WssPlayback)
            {
                return "ShowWssPlayback";
            }

            if (activeMenu == "ShowVelocityPlayback" && slider != null && slider.IsActive)
            {
                if (slider.mode == ControlMode.DensityX || slider.mode == ControlMode.DensityY || slider.mode == ControlMode.DensityZ)
                {
                    return "ShowVelocityInterval";
                }
            }

            if (activeMenu == "None")
            {
                if (buttonManager.settingsMenu != null && buttonManager.settingsMenu.activeSelf) return "ShowSettings";
                if (buttonManager.folderSelectorMenu != null && buttonManager.folderSelectorMenu.activeSelf) return "ShowFolderSelector";
                if (buttonManager.measurementSettingUI != null && buttonManager.measurementSettingUI.activeSelf) return "ShowMeasurement";
                if (buttonManager.playSettingMenu != null && buttonManager.playSettingMenu.activeSelf) return "ShowPlaySetting";
            }
        }
        return activeMenu;
    }

    private string NormalizeActiveMenu(string rawActiveMenu)
    {
        switch (rawActiveMenu)
        {
            case "ShowMain": return "main";
            case "ShowVelocity": return "velocity";
            case "ShowVelocityPlayback": return "velocity_playback";
            case "ShowVelocityInterval": return "velocity_interval";
            case "ShowVelocityVisSetting": return "velocity_visual_setting";
            case "ShowStreamline": return "streamline";
            case "ShowStreamlineSpeed": return "streamline_speed";
            case "ShowWss": return "wss";
            case "ShowWssPlayback": return "wss_playback";
            case "ShowVisSetting": return "visualization_setting";
            case "ShowSettings": return "settings";
            case "ShowFolderSelector": return "folder_selector";
            case "ShowMeasurement": return "measurement";
            case "ShowPlaySetting": return "play_setting";
            default: return "unknown";
        }
    }

    private string ResolveCurrentTargetContext(string activeMenu)
    {
        switch (activeMenu)
        {
            case "velocity":
            case "velocity_playback":
            case "velocity_interval":
            case "velocity_visual_setting":
                return "velocity";
            case "streamline":
            case "streamline_speed":
                return "streamline";
            case "wss":
            case "wss_playback":
                return "wss";
            case "measurement":
                return "measurement";
            case "settings":
            case "visualization_setting":
            case "folder_selector":
                return "settings";
            case "play_setting":
                return "app";
            default:
                return null;
        }
    }

    private string GetVelocityPlaybackState(Manager manager)
    {
        if (manager == null || manager.velocityLoader == null) return "unknown";
        FieldInfo field = typeof(VelocityLoader).GetField("isPlaying", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) return "unknown";
        object value = field.GetValue(manager.velocityLoader);
        if (value is bool isPlaying) return isPlaying ? "playing" : "paused";
        return "unknown";
    }

    private string GetStreamlinePlaybackState(Manager manager)
    {
        if (manager == null || manager.streamlineLoader == null) return "unknown";
        return manager.streamlineLoader.isAnimating ? "playing" : "paused";
    }

    private string GetWssPlaybackState(Manager manager)
    {
        if (manager == null || manager.wssLoader == null) return "unknown";
        return manager.wssLoader.isAnimating ? "playing" : "paused";
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string NullableJsonString(string value)
    {
        if (string.IsNullOrEmpty(value)) return "null";
        return "\"" + EscapeJson(value) + "\"";
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private void SendSuccessResponse(string id, string text, bool isError)
    {
        McpResponse response = new McpResponse
        {
            id = id,
            result = new McpResult
            {
                isError = isError,
                content = new List<McpContent> { new McpContent { text = text } }
            }
        };

        string json = JsonUtility.ToJson(response);
        SendJsonResponse(json);
    }

    private void SendErrorResponse(string id, int code, string message)
    {
        McpResponse response = new McpResponse
        {
            id = id,
            error = new McpError
            {
                code = code,
                message = message
            }
        };

        string json = JsonUtility.ToJson(response);
        SendJsonResponse(json);
    }
}

