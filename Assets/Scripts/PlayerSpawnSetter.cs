using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerSpawnSetter : NetworkBehaviour
{
    [SerializeField] private string spawnPointObjectName = "SpawnPoint";

    [Tooltip("Altura extra sobre el SpawnPoint para evitar que el CharacterController se entierre en el suelo.")]
    [SerializeField] private float spawnHeightOffset = 0.15f;

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
            if (!TryApplySpawnPosition())
            {
                SubscribeToSceneManager();
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

    public bool TryApplySpawnPosition()
    {
        GameObject spawnPointObj = GameObject.Find(spawnPointObjectName);
        if (spawnPointObj == null)
        {
            return false;
        }

        Vector3 spawnOffset = new Vector3((OwnerClientId % 4) * 1.5f, spawnHeightOffset, 0f);
        Vector3 spawnPos = spawnPointObj.transform.position + spawnOffset;
        Quaternion spawnRot = spawnPointObj.transform.rotation;

        // Aplicar en el servidor
        ApplySpawnPosition(spawnPos, spawnRot);

        // Si el owner es un cliente remoto (no el host), enviarle la posición
        // porque ClientNetworkTransform da autoridad al owner.
        if (!IsOwner)
        {
            TeleportOwnerToSpawnRpc(spawnPos, spawnRot);
        }

        Debug.Log($"[PlayerSpawnSetter] Jugador {OwnerClientId} posicionado en SpawnPoint {spawnPos} en la escena {SceneManager.GetActiveScene().name}");
        return true;
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
