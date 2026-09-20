using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Gestiona la interfaz de usuario de la escena Lobby.
/// Muestra el numero de jugadores conectados y el boton de inicio de juego (exclusivo para el Host/Servidor).
/// </summary>
public class LobbyUIHandler : MonoBehaviour
{
    [Header("UI Elements")]
    [Tooltip("Texto TMP para mostrar el numero de jugadores conectados.")]
    [SerializeField] private TMP_Text playerCountText;

    [Tooltip("Boton para iniciar la partida.")]
    [SerializeField] private Button startGameButton;

    [Header("Scene Configuration")]
    [Tooltip("Nombre de la escena de juego principal a la que se cambiara al presionar Jugar.")]
    [SerializeField] private string mainSceneName = "MainScene";

    private void Start()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[LobbyUIHandler] NetworkManager.Singleton no fue encontrado.");
            return;
        }

        // Suscribirse a eventos de conexion/desconexion para actualizar la UI
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        // Configurar la UI inicial
        UpdatePlayerCountUI();
        ConfigureStartButton();
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[LobbyUIHandler] Cliente conectado con ID: {clientId}");
        UpdatePlayerCountUI();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"[LobbyUIHandler] Cliente desconectado con ID: {clientId}");
        UpdatePlayerCountUI();
    }

    /// <summary>
    /// Actualiza el texto de conteo de jugadores conectados.
    /// </summary>
    private void UpdatePlayerCountUI()
    {
        if (playerCountText == null) return;

        int count = 0;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            count = NetworkManager.Singleton.ConnectedClientsIds.Count;
        }

        playerCountText.text = count.ToString();
    }

    /// <summary>
    /// Configura el boton de inicio de juego segun si el cliente local es Servidor/Host.
    /// </summary>
    private void ConfigureStartButton()
    {
        if (startGameButton == null) return;

        bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        // El boton solo debe estar visible/activo para el Host / Servidor
        startGameButton.gameObject.SetActive(isServer);

        if (isServer)
        {
            startGameButton.onClick.RemoveAllListeners();
            startGameButton.onClick.AddListener(OnStartGameButtonClicked);
        }
    }

    /// <summary>
    /// Metodo ejecutado al pulsar el boton "Jugar".
    /// </summary>
    public void OnStartGameButtonClicked()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[LobbyUIHandler] Solo el servidor/host puede iniciar la partida.");
            return;
        }

        // Marcar el inicio de la partida en el NetworkGameManager para bloquear late-join
        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.StartGame();
        }

        // Determinar el nombre de la escena a cargar (usando el NetworkGameManager o la variable local)
        string targetScene = (NetworkGameManager.Instance != null) ? NetworkGameManager.Instance.MainSceneName : mainSceneName;

        Debug.Log($"[LobbyUIHandler] Iniciando partida... Cambiando a escena: {targetScene}");
        NetworkManager.Singleton.SceneManager.LoadScene(targetScene, UnityEngine.SceneManagement.LoadSceneMode.Single);
    }
}
