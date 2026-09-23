using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Gestiona la visualización del número de jugador y su posición en la escena Lobby.
/// Este componente debe adjuntarse al Prefab del Jugador.
/// </summary>
public class LobbyPlayerDisplay : NetworkBehaviour
{
    [Header("UI & Display")]
    [Tooltip("Referencia al componente TextMeshPro que muestra el nombre/número sobre el jugador.")]
    [SerializeField] private TMP_Text playerLabelText;

    [Tooltip("Formato del texto. {0} será reemplazado por el número de jugador (OwnerClientId + 1).")]
    [SerializeField] private string labelFormat = "Jugador {0}";

    [Header("Lobby Positioning")]
    [Tooltip("Nombre del objeto padre en el Lobby que contiene las posiciones de los slots.")]
    [SerializeField] private string lobbySlotsObjectName = "LobbySlots";

    [Tooltip("Nombre de la escena de Lobby para aplicar la lógica visual.")]
    [SerializeField] private string lobbySceneName = "Lobby";

    [Tooltip("Offset horizontal si no se encuentran los LobbySlots en la escena.")]
    [SerializeField] private float fallbackSlotOffset = 2.0f;

    private Camera mainCamera;

    private bool positionApplied = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Actualizar la etiqueta del jugador (número de jugador: OwnerClientId + 1)
        UpdatePlayerLabel();

        // Suscribirse a eventos de carga de escena por si la escena actual aún está cargando
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete += OnLoadComplete;
        }
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnUnitySceneLoaded;

        // Intentar aplicar posición inicial en el Lobby
        TryUpdateLobbyPosition();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnLoadComplete;
        }
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnUnitySceneLoaded;
    }

    private void OnLoadComplete(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
    {
        if (sceneName == lobbySceneName)
        {
            TryUpdateLobbyPosition();
        }
    }

    private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == lobbySceneName)
        {
            TryUpdateLobbyPosition();
        }
    }

    private void LateUpdate()
    {
        if (playerLabelText == null) return;
        Camera cam = ResolveCamera();
        if (cam == null) return;

        // Solo en Podio el texto debe mirar siempre al frente de la cámara (paralelo al plano de vista)
        // En Lobby/MainScene mantiene el billboard clásico hacia la posición de la cámara
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        bool isPodio = sceneName == "Podio" || sceneName == "Podium";

        if (isPodio)
        {
            // Frente de cámara, independiente de la dirección final del jugador tras saltos
            playerLabelText.transform.rotation = Quaternion.LookRotation(-cam.transform.forward, cam.transform.up);
        }
        else
        {
            Vector3 dir = playerLabelText.transform.position - cam.transform.position;
            if (dir.sqrMagnitude > 0.0001f)
                playerLabelText.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
    }

    private void Update()
    {
        if (playerLabelText != null && ResolveCamera() == null)
        {
            ResolveCamera();
        }
    }

    private Camera ResolveCamera()
    {
        if (mainCamera != null) return mainCamera;

        // 1) MainCamera tag
        if (Camera.main != null)
        {
            mainCamera = Camera.main;
            return mainCamera;
        }

        // 2) Cualquier cámara activa (PlayerCamera del owner en MainScene no tiene tag MainCamera)
        Camera anyCam = FindFirstObjectByType<Camera>();
        // Preferir la cámara del jugador local (owner) si existe, para que los labels remotos miren al observador
        PlayerMovementManager localPlayer = null;
        foreach (PlayerMovementManager pm in FindObjectsByType<PlayerMovementManager>(FindObjectsSortMode.None))
        {
            if (pm.IsOwner)
            {
                localPlayer = pm;
                break;
            }
        }
        if (localPlayer != null)
        {
            Camera localCam = localPlayer.GetComponentInChildren<Camera>(true);
            if (localCam != null && localCam.enabled)
            {
                mainCamera = localCam;
                return mainCamera;
            }
        }

        if (anyCam != null)
        {
            // Si hay varias, preferir la habilitada
            if (anyCam.enabled)
            {
                mainCamera = anyCam;
                return mainCamera;
            }
            // Fallback a la primera encontrada
            mainCamera = anyCam;
            return mainCamera;
        }

        return null;
    }

    /// <summary>
    /// Establece el texto del identificador del jugador (ej. "Jugador 1", "Jugador 2").
    /// </summary>
    private void UpdatePlayerLabel()
    {
        if (playerLabelText == null) return;

        // El número de jugador es 1-indexed (OwnerClientId + 1)
        int playerNumber = (int)OwnerClientId + 1;
        playerLabelText.text = string.Format(labelFormat, playerNumber);
    }

    /// <summary>
    /// Intenta posicionar al jugador en su slot correspondiente dentro del Lobby.
    /// </summary>
    public bool TryUpdateLobbyPosition()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene != lobbySceneName) return false;

        GameObject slotsContainer = GameObject.Find(lobbySlotsObjectName);
        Vector3 targetPosition = Vector3.zero;
        Quaternion targetRotation = Quaternion.identity;

        int slotIndex = (int)(OwnerClientId % 4);

        if (slotsContainer != null && slotsContainer.transform.childCount > slotIndex)
        {
            Transform slot = slotsContainer.transform.GetChild(slotIndex);
            targetPosition = slot.position;
            targetRotation = slot.rotation;
        }
        else if (slotsContainer == null)
        {
            // Si los slots aún no existen en la escena, reintentar más tarde
            targetPosition = new Vector3(slotIndex * fallbackSlotOffset, 0f, 0f);
        }

        ApplyPosition(targetPosition, targetRotation);

        // Si el objeto tiene ClientNetworkTransform y es ejecutado en el servidor para un cliente remoto,
        // sincronizar la posición enviando un RPC al dueño.
        if (IsServer && !IsOwner)
        {
            TeleportLobbyOwnerRpc(targetPosition, targetRotation);
        }

        positionApplied = true;
        return true;
    }

    [Rpc(SendTo.Owner)]
    private void TeleportLobbyOwnerRpc(Vector3 position, Quaternion rotation)
    {
        ApplyPosition(position, rotation);
    }

    private void ApplyPosition(Vector3 position, Quaternion rotation)
    {
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        transform.position = position;
        transform.rotation = rotation;

        if (cc != null) cc.enabled = true;
    }
}
