using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerSpawnSetter : NetworkBehaviour
{
    [SerializeField] private string spawnPointObjectName = "SpawnPoint";

    [Tooltip("Altura extra sobre el SpawnPoint para evitar que el CharacterController se entierre en el suelo.")]
    [SerializeField] private float spawnHeightOffset = 0.15f;

    private bool spawnApplied;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        // Intentar posicionar ahora. Si la escena aún no cargó (el host
        // spawna al jugador ANTES de llamar a LoadScene), el SpawnPoint
        // no existirá todavía. En ese caso nos suscribimos al evento de
        // carga de escena para reintentar.
        if (!TryApplySpawnPosition())
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete += OnSceneLoadComplete;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnSceneLoadComplete;
        }
    }

    private void OnSceneLoadComplete(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
    {
        if (spawnApplied) return;

        // Solo reaccionar cuando el servidor/host termina de cargar la escena,
        // que es cuando el SpawnPoint ya existe localmente.
        if (clientId != NetworkManager.ServerClientId) return;

        if (TryApplySpawnPosition())
        {
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnSceneLoadComplete;
        }
    }

    private bool TryApplySpawnPosition()
    {
        GameObject spawnPointObj = GameObject.Find(spawnPointObjectName);
        if (spawnPointObj == null)
        {
            Debug.Log($"[PlayerSpawnSetter] SpawnPoint '{spawnPointObjectName}' no encontrado aún, se reintentará tras la carga de escena.");
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

        spawnApplied = true;
        Debug.Log($"[PlayerSpawnSetter] Jugador {OwnerClientId} posicionado en {spawnPos}");
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

        if (cc != null) cc.enabled = true;
    }
}
