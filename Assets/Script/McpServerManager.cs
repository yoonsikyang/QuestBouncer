using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class McpServerManager : MonoBehaviour
{
    private class QueuedMcpRequest
    {
        public string Body;
        public string SessionId;
    }

    private class SseClient
    {
        public string SessionId;
        public HttpListenerContext Context;
    }

    public static McpServerManager Instance { get; private set; }

    [Header("Server Configuration")]
    public int port = 11112;
    public string endpointPrefix = "/mcp/";

    private HttpListener httpListener;
    private Thread serverThread;
    private bool isRunning = false;
    private readonly ConcurrentQueue<QueuedMcpRequest> requestQueue = new ConcurrentQueue<QueuedMcpRequest>();
    private SynchronizationContext unityContext;

    // A list to keep track of active SSE connections
    private List<SseClient> sseClients = new List<SseClient>();
    private readonly object sseLock = new object();

    public event Action<string, string> OnJsonRpcMessageReceived;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeServer()
    {
        McpServerManager server = FindObjectOfType<McpServerManager>();
        if (server == null)
        {
            GameObject serverObject = new GameObject("MCP_Manager");
            server = serverObject.AddComponent<McpServerManager>();
            serverObject.AddComponent<McpToolHandler>();
        }

        server.StartServer();

        McpToolHandler handler = server.GetComponent<McpToolHandler>();
        if (handler == null)
        {
            handler = server.gameObject.AddComponent<McpToolHandler>();
        }
        handler.EnsureSubscribed();
    }

    private void Awake()
    {
        unityContext = SynchronizationContext.Current;

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
        StartServer();
    }

    private void OnEnable()
    {
        StartServer();
    }

    private void OnDestroy()
    {
        StopServer();
    }

    private void Update()
    {
        while (requestQueue.TryDequeue(out QueuedMcpRequest request))
        {
            DispatchMessage(request.Body, request.SessionId);
        }
    }

    private void StartServer()
    {
        if (isRunning && httpListener != null && httpListener.IsListening) return;

        if (isRunning)
        {
            isRunning = false;
            try
            {
                httpListener?.Stop();
                httpListener?.Close();
            }
            catch { }
            httpListener = null;
        }

        try
        {
            httpListener = new HttpListener();
            httpListener.Prefixes.Add($"http://localhost:{port}{endpointPrefix}");
            httpListener.Prefixes.Add($"http://127.0.0.1:{port}{endpointPrefix}");
            httpListener.Start();
            isRunning = true;

            serverThread = new Thread(ServerLoop);
            serverThread.IsBackground = true;
            serverThread.Start();

            Debug.Log($"[McpServerManager] Server started on port {port}{endpointPrefix}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[McpServerManager] Failed to start server: {e.Message}");
        }
    }

    private void StopServer()
    {
        if (!isRunning) return;

        isRunning = false;

        lock (sseLock)
        {
            foreach (var client in sseClients)
            {
                try { client.Context.Response.Close(); } catch { }
            }
            sseClients.Clear();
        }

        if (httpListener != null)
        {
            try
            {
                httpListener.Stop();
                httpListener.Close();
            }
            catch { }
            httpListener = null;
        }

        if (serverThread != null && serverThread.IsAlive)
        {
            serverThread.Join(1000);
        }

        Debug.Log("[McpServerManager] Server stopped.");
    }

    private void ServerLoop()
    {
        while (isRunning)
        {
            try
            {
                // Note: GetContext blocks until a request comes in
                IAsyncResult result = httpListener.BeginGetContext(new AsyncCallback(ListenerCallback), httpListener);
                result.AsyncWaitHandle.WaitOne();
            }
            catch (Exception e)
            {
                if (isRunning)
                    Debug.LogWarning($"[McpServerManager] Server loop exception: {e.Message}");
            }
        }
    }

    private void ListenerCallback(IAsyncResult result)
    {
        if (!isRunning || httpListener == null || !httpListener.IsListening) return;

        try
        {
            HttpListenerContext context = httpListener.EndGetContext(result);
            HttpListenerRequest request = context.Request;

            string path = request.Url.AbsolutePath;

            if (path.EndsWith("/sse") && request.HttpMethod == "GET")
            {
                HandleSseConnection(context);
            }
            else if (path.EndsWith("/message") && request.HttpMethod == "POST")
            {
                HandleMessage(context);
            }
            else
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
            }
        }
        catch (Exception e)
        {
            if (isRunning)
                Debug.LogWarning($"[McpServerManager] Error handling request: {e.Message}");
        }
    }

    private void HandleSseConnection(HttpListenerContext context)
    {
        HttpListenerResponse response = context.Response;
        
        response.ContentType = "text/event-stream";
        response.Headers.Add("Cache-Control", "no-cache");
        response.Headers.Add("Connection", "keep-alive");
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.StatusCode = 200;

        string sessionId = Guid.NewGuid().ToString();

        lock (sseLock)
        {
            sseClients.Add(new SseClient
            {
                SessionId = sessionId,
                Context = context
            });
        }

        // Send the endpoint event as per MCP specification
        string endpointEvent = $"event: endpoint\ndata: {endpointPrefix}message?sessionId={sessionId}\n\n";
        byte[] buffer = Encoding.UTF8.GetBytes(endpointEvent);
        
        try
        {
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Flush();
            Debug.Log($"[McpServerManager] New SSE connection established. Session: {sessionId}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[McpServerManager] Failed to write SSE endpoint event: {e.Message}");
            RemoveSseClient(context);
        }
    }

    private void HandleMessage(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        // Allow CORS
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        
        if (request.HttpMethod == "OPTIONS")
        {
            response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
            response.StatusCode = 200;
            response.Close();
            return;
        }

        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
        {
            string requestBody = reader.ReadToEnd();
            string sessionId = request.QueryString["sessionId"];
            
            // Send back 202 Accepted
            response.StatusCode = 202;
            response.Close();

            if (unityContext != null)
            {
                Debug.Log("[McpServerManager] MCP message queued to Unity context.");
                unityContext.Post(_ => DispatchMessage(requestBody, sessionId), null);
            }
            else
            {
                Debug.Log("[McpServerManager] MCP message queued to Update fallback.");
                requestQueue.Enqueue(new QueuedMcpRequest
                {
                    Body = requestBody,
                    SessionId = sessionId
                });
            }
        }
    }

    private void DispatchMessage(string requestBody, string sessionId)
    {
        Debug.Log("[McpServerManager] Dispatching MCP message.");

        if (OnJsonRpcMessageReceived != null)
        {
            OnJsonRpcMessageReceived.Invoke(requestBody, sessionId);
        }
        else if (McpToolHandler.Instance != null)
        {
            McpToolHandler.Instance.ReceiveMessage(requestBody, sessionId);
        }
        else
        {
            Debug.LogWarning("[McpServerManager] Received MCP message, but no tool handler is available.");
        }
    }

    public void SendMessageToAllClients(string jsonrpcResponse)
    {
        SendMessageToClient(null, jsonrpcResponse);
    }

    public void SendMessageToClient(string sessionId, string jsonrpcResponse)
    {
        // Format as SSE message event
        string sseMessage = FormatSseMessage(jsonrpcResponse);
        byte[] buffer = Encoding.UTF8.GetBytes(sseMessage);
        bool sent = false;

        lock (sseLock)
        {
            for (int i = sseClients.Count - 1; i >= 0; i--)
            {
                var client = sseClients[i];
                if (!string.IsNullOrEmpty(sessionId) && client.SessionId != sessionId)
                {
                    continue;
                }

                try
                {
                    client.Context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    client.Context.Response.OutputStream.Flush();
                    sent = true;
                }
                catch
                {
                    // If write fails, the connection is likely dead
                    client.Context.Response.Close();
                    sseClients.RemoveAt(i);
                }
            }
        }

        if (!sent && !string.IsNullOrEmpty(sessionId))
        {
            Debug.LogWarning($"[McpServerManager] No active SSE client for session: {sessionId}");
        }
    }

    private string FormatSseMessage(string jsonrpcResponse)
    {
        string normalized = (jsonrpcResponse ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");
        string[] lines = normalized.Split('\n');
        StringBuilder builder = new StringBuilder();
        builder.Append("event: message\n");
        foreach (string line in lines)
        {
            builder.Append("data: ");
            builder.Append(line);
            builder.Append('\n');
        }
        builder.Append('\n');
        return builder.ToString();
    }

    private void RemoveSseClient(HttpListenerContext context)
    {
        lock (sseLock)
        {
            sseClients.RemoveAll(client => client.Context == context);
            try { context.Response.Close(); } catch { }
        }
    }
}
