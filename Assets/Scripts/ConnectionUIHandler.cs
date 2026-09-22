using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using TMPro;

public class ConnectionUIHandler : MonoBehaviour
{
    [Header("Scene Configuration")]
    [Tooltip("Nombre de la escena de Lobby a la que el servidor/host cambiará automáticamente al iniciar.")]
    [SerializeField] private string lobbySceneName = "Lobby";

    [Header("Network Connection Configuration")]
    [Tooltip("Campo de texto de TMP para ingresar la IP del servidor.")]
    [SerializeField] private TMP_InputField ipInputField;

    [Tooltip("IP por defecto si el campo está vacío.")]
    [SerializeField] private string defaultAddress = "127.0.0.1";

    [Tooltip("Puerto de conexión.")]
    [SerializeField] private ushort port = 7777;

    private void SetTargetIPAddress()
    {
        if (NetworkManager.Singleton == null) return;

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogWarning("[ConnectionUIHandler] UnityTransport no fue encontrado en el NetworkManager.");
            return;
        }

        string targetIP = defaultAddress;
        if (ipInputField != null && !string.IsNullOrWhiteSpace(ipInputField.text))
        {
            targetIP = ipInputField.text.Trim();
        }

        transport.SetConnectionData(targetIP, port);
        Debug.Log($"[ConnectionUIHandler] IP de conexión configurada a: {targetIP}:{port}");
    }

    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        // Si el cliente desconectado es el cliente local y no es el servidor/host activo
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("[ConnectionUIHandler] Desconectado del servidor. Regresando a la escena de conexión...");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "ConnectionScene")
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene("ConnectionScene");
            }
        }
    }

    public void StartHost()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[ConnectionUIHandler] NetworkManager.Singleton no encontrado en la escena.");
            return;
        }

        if (NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        SetTargetIPAddress();

        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.ConfigureConnectionApproval();
            NetworkGameManager.Instance.ResetGame();
        }

        NetworkManager.Singleton.StartHost();

        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }

    public void StartClient()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[ConnectionUIHandler] NetworkManager.Singleton no encontrado en la escena.");
            return;
        }

        if (NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        SetTargetIPAddress();
        NetworkManager.Singleton.StartClient();
    }

    public void StartServer()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[ConnectionUIHandler] NetworkManager.Singleton no encontrado en la escena.");
            return;
        }

        SetTargetIPAddress();

        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.ConfigureConnectionApproval();
            NetworkGameManager.Instance.ResetGame();
        }

        NetworkManager.Singleton.StartServer();

        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }
}

