using Unity.Netcode;
using UnityEngine;

public class ConnectionUIHandler : MonoBehaviour
{
    [Header("Scene Configuration")]
    [Tooltip("Nombre de la escena principal a la que el servidor/host cambiará automáticamente al iniciar.")]
    [SerializeField] private string mainSceneName = "MainScene";

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

        NetworkManager.Singleton.StartHost();
        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(mainSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
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

        NetworkManager.Singleton.StartClient();
    }

    public void StartServer()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[ConnectionUIHandler] NetworkManager.Singleton no encontrado en la escena.");
            return;
        }

        NetworkManager.Singleton.StartServer();
        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(mainSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }
}
