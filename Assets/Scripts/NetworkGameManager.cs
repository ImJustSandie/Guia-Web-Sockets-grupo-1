using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Administrador del estado de la partida y aprobacion de conexiones de red.
/// Evita que se unan mas clientes una vez iniciada la ronda.
/// </summary>
public class NetworkGameManager : MonoBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    [Header("Scene Names")]
    [SerializeField] private string connectionSceneName = "ConnectionScene";
    [SerializeField] private string lobbySceneName = "Lobby";
    [SerializeField] private string mainSceneName = "MainScene";

    [Header("Game State")]
    [SerializeField] private bool gameStarted = false;

    public bool IsGameStarted => gameStarted;
    public string ConnectionSceneName => connectionSceneName;
    public string LobbySceneName => lobbySceneName;
    public string MainSceneName => mainSceneName;

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
        ConfigureConnectionApproval();
        SubscribeToNetworkEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeFromNetworkEvents();
    }

    private void SubscribeToNetworkEvents()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    private void UnsubscribeFromNetworkEvents()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        // Si el cliente desconectado es el cliente local (y no es el host cerrando el servidor)
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId && !NetworkManager.Singleton.IsHost)
        {
            Debug.Log("[NetworkGameManager] El cliente local se ha desconectado del host. Regresando a ConnectionScene...");
            ResetGame();
            if (SceneManager.GetActiveScene().name != connectionSceneName)
            {
                SceneManager.LoadScene(connectionSceneName);
            }
        }
    }

    /// <summary>
    /// Configura la aprobacion de conexiones en el NetworkManager.
    /// </summary>
    public void ConfigureConnectionApproval()
    {
        if (NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        NetworkManager.Singleton.ConnectionApprovalCallback = ConnectionApprovalCheck;
        SubscribeToNetworkEvents();
    }

    /// <summary>
    /// Callback invocado cuando un cliente intenta conectarse al servidor/host.
    /// </summary>
    private void ConnectionApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        // Si el juego ya inicio, rechazar la conexion
        if (gameStarted)
        {
            response.Approved = false;
            response.Reason = "La partida ya ha comenzado. No puedes unirte en este momento.";
            response.CreatePlayerObject = false;
            response.Pending = false;
            Debug.LogWarning($"[NetworkGameManager] Conexion rechazada para el cliente {request.ClientNetworkId}: La partida ya esta en curso.");
            return;
        }

        // Si esta en el Lobby o antes del inicio, aprobar conexion
        response.Approved = true;
        response.CreatePlayerObject = true;
        response.Pending = false;
        Debug.Log($"[NetworkGameManager] Conexion aprobada para el cliente {request.ClientNetworkId}.");
    }

    /// <summary>
    /// Marca el juego como iniciado e inhabilita nuevas conexiones.
    /// </summary>
    public void StartGame()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            gameStarted = true;
            Debug.Log("[NetworkGameManager] La partida ha comenzado. Se han bloqueado nuevas conexiones de clientes.");
        }
    }

    /// <summary>
    /// Restablece el estado del juego para permitir nuevas conexiones en el lobby.
    /// </summary>
    public void ResetGame()
    {
        gameStarted = false;
        Debug.Log("[NetworkGameManager] Estado del juego restablecido. Nuevas conexiones permitidas.");
    }
}
