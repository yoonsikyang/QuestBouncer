using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class NetworkManager : MonoBehaviourPunCallbacks
{
    public static NetworkManager Instance;

    [Tooltip("The maximum number of players per room. When a room is full, it can't be joined by new players, and so new room will be created")]
    [SerializeField]
    private byte maxPlayersPerRoom = 10;
    private float nextStateLogTime = 0f;
    private DisconnectCause lastDisconnectCause = DisconnectCause.None;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            PhotonNetwork.AutomaticallySyncScene = true;
            EnsureSyncService();
            PhotonNetwork.PhotonServerSettings.EnableSupportLogger = true;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        Connect();
    }

    void Update()
    {
        if (Time.time >= nextStateLogTime)
        {
            Debug.Log($"[PhotonState] {PhotonNetwork.NetworkClientState}, InRoom={PhotonNetwork.InRoom}, Connected={PhotonNetwork.IsConnectedAndReady}, LastCause={lastDisconnectCause}");
            nextStateLogTime = Time.time + 5f;
        }
    }

    public void Connect()
    {
        if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.JoinRandomRoom();
        }
        else
        {
            Debug.Log("NetworkManager: Connecting to Photon...");
            PhotonNetwork.ConnectUsingSettings();
            PhotonNetwork.GameVersion = Application.version;
        }
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("NetworkManager: Connected to Master");
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        Debug.Log("NetworkManager: Joined Lobby");
        PhotonNetwork.JoinOrCreateRoom("HoloRoom", new RoomOptions { MaxPlayers = maxPlayersPerRoom }, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        Debug.Log("NetworkManager: Joined Room");
        EnsureSyncService();
        // Instantiate networked objects here if needed
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        Debug.Log("NetworkManager: Join Random Failed, creating room");
        PhotonNetwork.CreateRoom(null, new RoomOptions { MaxPlayers = maxPlayersPerRoom });
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarningFormat("NetworkManager: Disconnected due to {0}", cause);
        lastDisconnectCause = cause;
        Debug.LogWarning($"[PhotonState] {PhotonNetwork.NetworkClientState}, InRoom={PhotonNetwork.InRoom}, Connected={PhotonNetwork.IsConnectedAndReady}, Cause={cause}");
    }

    void EnsureSyncService()
    {
        if (PhotonSyncService.Instance == null)
        {
            var go = new GameObject("PhotonSyncService");
            go.AddComponent<PhotonSyncService>();
        }
    }
}
