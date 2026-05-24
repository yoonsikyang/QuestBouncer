// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;

#if UNITY_EDITOR || UNITY_WSA || UNITY_STANDALONE_WIN || UNITY_ANDROID
using Microsoft.MixedReality.OpenXR.Remoting;
#endif

public class HolographicRemoteConnect : MonoBehaviour
{
    private const string DefaultIp = "192.168.60.29";

    [SerializeField]
    private string IP = DefaultIp;

    private bool connected = false;

#if UNITY_EDITOR || UNITY_WSA || UNITY_STANDALONE_WIN || UNITY_ANDROID
    [SerializeField, Tooltip("The configuration information for the remote connection.")]
    private RemotingConnectConfiguration remotingConfiguration = new RemotingConnectConfiguration
    {
        RemotePort = 8265,
        MaxBitrateKbps = 20000
    };
#endif

    private void Awake()
    {
        if (string.IsNullOrWhiteSpace(IP))
        {
            IP = DefaultIp;
        }
    }

    public void Connect()
    {
#if UNITY_EDITOR || UNITY_WSA || UNITY_STANDALONE_WIN || UNITY_ANDROID
        connected = true;

        remotingConfiguration.RemoteHostName = IP;

        AppRemoting.StartConnectingToPlayer(remotingConfiguration);
#else
        Debug.LogWarning("Holographic remoting is not available for this build target.");
#endif
    }

    private void OnGUI()
    {
        IP = GUI.TextField(new Rect(10, 10, 200, 30), IP, 25);

        string buttonText = connected ? "Disconnect" : "Connect";

        Event currentEvent = Event.current;
        bool enterPressed =
            currentEvent.type == EventType.KeyDown &&
            (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter);

        if (GUI.Button(new Rect(220, 10, 100, 30), buttonText) || enterPressed)
        {
            if (connected)
            {
#if UNITY_EDITOR || UNITY_WSA || UNITY_STANDALONE_WIN || UNITY_ANDROID
                AppRemoting.Disconnect();
#endif
                connected = false;
            }
            else
            {
                Connect();
            }

            Debug.Log(buttonText);
        }
    }
}
