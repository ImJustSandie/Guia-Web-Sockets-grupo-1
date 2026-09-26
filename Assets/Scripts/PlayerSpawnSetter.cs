using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerSpawnSetter : NetworkBehaviour
{
    [Header("Spawn Configuration")]
    [Tooltip("Nombre del objeto padre en la escena que contiene los SpawnPoints individuales de cada jugador.")]
    [SerializeField] private string spawnPointsContainerName = "SpawnPoints";

    [Tooltip("Nombre base de un SpawnPoint único (o prefijo para SpawnPoint_0, SpawnPoint_1, etc.).")]
    [SerializeField] private string spawnPointObjectName = "SpawnPoint";

    [Tooltip("Altura extra sobre el SpawnPoint para evitar que el CharacterController se entierre en el suelo.")]
    [SerializeField] private float spawnHeightOffset = 0.15f;

    [Tooltip("Offset horizontal por defecto si solo existe un único SpawnPoint.")]
    [SerializeField] private float fallbackSpawnOffset = 1.5f;

    [Header("Ground Alignment")]
    [Tooltip("Nombre de la escena del Lobby donde PlayerSpawnSetter debe omitir la reubicación.")]
    [SerializeField] private string lobbySceneName = "Lobby";

    [Tooltip("Si es true, realiza un Raycast hacia abajo para ajustar la altura exacta al nivel del suelo.")]
    [SerializeField] private bool snapToGround = true;

    [Tooltip("Máscara de capas considerada como suelo para el Raycast de alineación.")]
    [SerializeField] private LayerMask groundLayerMask = ~0;

    private void Awake()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnsubscribeFromSceneManager();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            if (SceneManager.GetActiveScene().name != lobbySceneName)
            {
                if (!TryApplySpawnPosition())
                {
                    SubscribeToSceneManager();
                }
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeFromSceneManager();
        base.OnNetworkDespawn();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsServer) return;
        if (scene.name == lobbySceneName) return;

        // Cada vez que se carga una escena (ej. MainScene de nuevo), intentar re-posicionar
        if (!TryApplySpawnPosition())
        {
            SubscribeToSceneManager();
        }
    }

    private void SubscribeToSceneManager()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnSceneLoadComplete;
            NetworkManager.Singleton.SceneManager.OnLoadComplete += OnSceneLoadComplete;
        }
    }

    private void UnsubscribeFromSceneManager()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnSceneLoadComplete;
        }
    }

    private void OnSceneLoadComplete(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
    {
        if (!IsServer) return;

        if (TryApplySpawnPosition())
        {
            UnsubscribeFromSceneManager();
        }
    }

    private int GetAssignedSlotIndex()
    {
        LobbyPlayerDisplay display = GetComponent<LobbyPlayerDisplay>();
        if (display != null)
        {
            return display.PlayerSlotIndex;
        }

        if (NetworkGameManager.Instance != null)
        {
            return NetworkGameManager.Instance.GetPlayerSlot(OwnerClientId);
        }

        return (int)(OwnerClientId % 4);
    }

    public bool TryApplySpawnPosition()
    {
        if (SceneManager.GetActiveScene().name == lobbySceneName) return false;

        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;
        bool pointFound = false;

        int assignedSlot = GetAssignedSlotIndex();

        // 1. Intentar buscar dentro del contenedor "SpawnPoints" (ej. SpawnPoints -> Slot 0, Slot 1, etc.)
        GameObject container = GameObject.Find(spawnPointsContainerName);
        if (container != null && container.transform.childCount > 0)
        {
            int slotIndex = (int)(assignedSlot % container.transform.childCount);
            Transform slotTransform = container.transform.GetChild(slotIndex);
            spawnPos = slotTransform.position;
            spawnRot = slotTransform.rotation;
            pointFound = true;
        }
        else
        {
            // 2. Intentar buscar por objeto con nombre específico (ej. SpawnPoint_0, SpawnPoint_1)
            string specificName = $"{spawnPointObjectName}_{assignedSlot}";
            GameObject specificObj = GameObject.Find(specificName);
            if (specificObj != null)
            {
                spawnPos = specificObj.transform.position;
                spawnRot = specificObj.transform.rotation;
                pointFound = true;
            }
            else
            {
                // 3. Fallback: buscar "SpawnPoint" único y aplicar offset horizontal de resguardo
                GameObject singleObj = GameObject.Find(spawnPointObjectName);
                if (singleObj != null)
                {
                    Vector3 offset = new Vector3((assignedSlot % 4) * fallbackSpawnOffset, 0f, 0f);
                    spawnPos = singleObj.transform.position + offset;
                    spawnRot = singleObj.transform.rotation;
                    pointFound = true;
                }
            }
        }

        if (!pointFound)
        {
            return false;
        }

        // Ajustar altura por Raycast si está activado
        if (snapToGround)
        {
            spawnPos = GetGroundedPosition(spawnPos);
        }
        else
        {
            spawnPos += Vector3.up * spawnHeightOffset;
        }

        // Aplicar en el servidor
        ApplySpawnPosition(spawnPos, spawnRot);

        // Si el owner es un cliente remoto (no el host), enviarle la posición
        // porque ClientNetworkTransform da autoridad al owner.
        if (!IsOwner)
        {
            TeleportOwnerToSpawnRpc(spawnPos, spawnRot);
        }

        Debug.Log($"[PlayerSpawnSetter] Jugador {OwnerClientId} (Slot {assignedSlot}) posicionado en {spawnPos} en la escena {SceneManager.GetActiveScene().name}");
        return true;
    }

    private Vector3 GetGroundedPosition(Vector3 originalPos)
    {
        // Empezar Raycast 5 metros por encima del punto original para detectar el suelo
        Vector3 rayStart = originalPos + Vector3.up * 5.0f;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 50.0f, groundLayerMask))
        {
            return hit.point + Vector3.up * spawnHeightOffset;
        }
        return originalPos + Vector3.up * spawnHeightOffset;
    }

    [Rpc(SendTo.Owner)]
    private void TeleportOwnerToSpawnRpc(Vector3 position, Quaternion rotation)
    {
        ApplySpawnPosition(position, rotation);
    }

    private void ApplySpawnPosition(Vector3 position, Quaternion rotation)
    {
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        transform.position = position;
        transform.rotation = rotation;

        if (cc != null && IsOwner) cc.enabled = true;
    }
}
