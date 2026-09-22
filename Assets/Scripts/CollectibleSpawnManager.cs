using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manager central que administra la instanciación de recolectables.
/// Cuenta cuántas instancias hay activas y respeta un límite máximo.
/// Solo el servidor puede instanciar NetworkObjects.
/// </summary>
public class CollectibleSpawnManager : MonoBehaviour
{
    public static CollectibleSpawnManager Instance { get; private set; }

    [Header("Prefab")]
    [Tooltip("Prefab del recolectable (debe tener NetworkObject + InteractableCube). Asignar Object.prefab.")]
    [SerializeField] private NetworkObject collectiblePrefab;

    [Header("Límites")]
    [Tooltip("Número máximo de instancias simultáneas en escena. No se generan más si se alcanza.")]
    [SerializeField] private int maxInstances = 5;
    [Tooltip("Si es true, al iniciar el servidor se generan instancias hasta llegar al límite (o initialSpawnCount).")]
    [SerializeField] private bool spawnOnStart = true;
    [Tooltip("Cantidad a generar al inicio. Si es 0 usa maxInstances.")]
    [SerializeField] private int initialSpawnCount = 0;

    [Header("Respawn automático")]
    [Tooltip("Intervalo en segundos para reintentar spawn automático (0 = desactivado). Solo en servidor. Si es 0 igual respawnea al recolectar si respawnOnCollected está activo.")]
    [SerializeField] private float autoSpawnInterval = 2f;
    [Tooltip("Si el auto-spawn debe elegir un spawner aleatorio como punto de aparición.")]
    [SerializeField] private bool useSpawnersAsSpawnPoints = true;
    [Tooltip("Si al recolectar/destruir un objeto se genera otro automáticamente para reponer hasta el límite.")]
    [SerializeField] private bool respawnOnCollected = true;
    [Tooltip("Retardo en segundos antes de reponer tras recolectar.")]
    [SerializeField] private float respawnDelay = 1f;

    [Header("Puntos de spawn")]
    [Tooltip("Puntos fijos donde instanciar. Si está vacío y useSpawnersAsSpawnPoints es true, usa la posición de cada CollectibleSpawner.")]
    [SerializeField] private Transform[] spawnPoints;

    [Tooltip("Offset vertical al instanciar para evitar solapamiento con el suelo.")]
    [SerializeField] private float spawnHeightOffset = 0.5f;

    private readonly HashSet<InteractableCube> trackedInstances = new HashSet<InteractableCube>();
    private readonly List<CollectibleSpawner> registeredSpawners = new List<CollectibleSpawner>();

    private float nextAutoSpawnTime;

    public int ActiveCount
    {
        get
        {
            // Limpieza de referencias nulas (objetos destruidos sin Unregister)
            trackedInstances.RemoveWhere(c => c == null);
            return trackedInstances.Count;
        }
    }

    public int MaxInstances => maxInstances;
    public bool CanSpawn => ActiveCount < maxInstances;
    public int RemainingSlots => Mathf.Max(0, maxInstances - ActiveCount);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[CollectibleSpawnManager] Ya existe una instancia en {Instance.name}, destruyendo {name}.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Migración: si quedó 0 por defecto antiguo, activar respawn periódico
        if (autoSpawnInterval == 0f && respawnOnCollected)
            autoSpawnInterval = 2f;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
            // Si ya somos servidor (host iniciado antes de cargar escena)
            if (NetworkManager.Singleton.IsServer && spawnOnStart)
            {
                // Delay un frame para que los spawners se registren
                Invoke(nameof(SpawnInitialBatch), 0.5f);
            }

            // Registrar instancias ya colocadas en escena (modo retrocompatibilidad)
            RegisterScenePlacedInstances();
        }
        else if (spawnOnStart)
        {
            Debug.LogWarning("[CollectibleSpawnManager] NetworkManager.Singleton no encontrado, el conteo funcionará sin red.");
            RegisterScenePlacedInstances();
        }
    }

    private void OnEnable()
    {
        RegisterScenePlacedInstances();
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
        }
    }

    private void HandleServerStarted()
    {
        RegisterScenePlacedInstances();
        if (spawnOnStart)
            Invoke(nameof(SpawnInitialBatch), 0.5f);
    }

    private void Update()
    {
        if (autoSpawnInterval <= 0f) return;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (Time.time < nextAutoSpawnTime) return;

        nextAutoSpawnTime = Time.time + autoSpawnInterval;
        TrySpawnRandom();
    }

    // ── Registro de instancias ──────────────────────────────────────────────

    public void Register(InteractableCube cube)
    {
        if (cube == null) return;
        if (trackedInstances.Add(cube))
        {
            // Debug.Log($"[CollectibleSpawnManager] Registrado {cube.name} -> {ActiveCount}/{maxInstances}");
        }
    }

    public void Unregister(InteractableCube cube)
    {
        if (cube == null) return;
        bool removed = trackedInstances.Remove(cube);
        if (removed)
        {
            // Debug.Log($"[CollectibleSpawnManager] Desregistrado {cube.name} -> {ActiveCount}/{maxInstances}");
            // Reponer automáticamente para mantener el límite
            if (respawnOnCollected && CanSpawn && IsServer)
            {
                if (respawnDelay <= 0f)
                    TrySpawnRandom();
                else
                    Invoke(nameof(TrySpawnRandomDelayed), respawnDelay);
            }
            // Si hay autoSpawnInterval, asegurar próximo tick
            if (autoSpawnInterval > 0f && IsServer)
                nextAutoSpawnTime = Time.time + autoSpawnInterval;
        }
    }

    private bool IsServer
    {
        get
        {
            if (NetworkManager.Singleton == null) return true; // modo offline/editor
            return NetworkManager.Singleton.IsServer;
        }
    }

    private void TrySpawnRandomDelayed()
    {
        if (!CanSpawn) return;
        TrySpawnRandom();
    }

    /// <summary>Busca cubos ya colocados manualmente en la escena para contarlos.</summary>
    private void RegisterScenePlacedInstances()
    {
        foreach (InteractableCube cube in FindObjectsByType<InteractableCube>(FindObjectsSortMode.None))
        {
            // Solo contar los que ya están spawneados / activos en escena
            // Evitar duplicar
            if (!trackedInstances.Contains(cube))
                trackedInstances.Add(cube);
        }
    }

    // ── Registro de spawners (hitbox) ──────────────────────────────────────

    public void RegisterSpawner(CollectibleSpawner spawner)
    {
        if (spawner == null || registeredSpawners.Contains(spawner)) return;
        registeredSpawners.Add(spawner);
    }

    public void UnregisterSpawner(CollectibleSpawner spawner)
    {
        registeredSpawners.Remove(spawner);
    }

    // ── API de spawn ───────────────────────────────────────────────────────

    /// <summary>
    /// Intenta instanciar en una posición/rotación concreta.
    /// Respeta el límite y solo funciona en servidor.
    /// </summary>
    public bool TrySpawnAt(Vector3 position, Quaternion rotation)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            // En modo no-network o cliente, no spawnear (authoritative server)
            // Pero permitir en editor sin NetworkManager para pruebas offline
            if (NetworkManager.Singleton != null) return false;
        }

        if (!CanSpawn) return false;
        if (collectiblePrefab == null)
        {
            Debug.LogWarning("[CollectibleSpawnManager] collectiblePrefab no asignado. Asignar Object.prefab en el Inspector.");
            return false;
        }

        position.y += spawnHeightOffset;

        NetworkObject instance = Instantiate(collectiblePrefab, position, rotation);
        // Si tiene NetworkObject, spawnear por Netcode; si no, queda como objeto local
        if (instance != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            instance.Spawn(true);
        }

        // El InteractableCube se registrará solo en OnNetworkSpawn/OnEnable;
        // por si el prefab no llama a Register (p.ej. sin red), lo registramos aquí
        InteractableCube cube = instance.GetComponent<InteractableCube>();
        if (cube != null && !trackedInstances.Contains(cube))
            trackedInstances.Add(cube);

        return true;
    }

    /// <summary>Intenta spawnear en un spawnPoint aleatorio o en un punto aleatorio dentro de una hitbox.</summary>
    public bool TrySpawnRandom()
    {
        if (!CanSpawn) return false;

        // Prioridad 1: spawnPoints fijos (posición exacta)
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            Transform chosen = spawnPoints[Random.Range(0, spawnPoints.Length)];
            Vector3 pos = chosen != null ? chosen.position : transform.position;
            Quaternion rot = chosen != null ? chosen.rotation : Quaternion.identity;
            return TrySpawnAt(pos, rot);
        }

        // Prioridad 2: punto aleatorio dentro del volumen de un spawner
        if (useSpawnersAsSpawnPoints && registeredSpawners.Count > 0)
        {
            CollectibleSpawner s = registeredSpawners[Random.Range(0, registeredSpawners.Count)];
            if (s != null)
                return RequestSpawnFromSpawner(s);
        }

        return TrySpawnAt(transform.position, Quaternion.identity);
    }

    /// <summary>Solicitado por un CollectibleSpawner (hitbox). Genera en punto aleatorio dentro del volumen.</summary>
    public bool RequestSpawnFromSpawner(CollectibleSpawner spawner)
    {
        if (spawner == null) return false;
        if (!CanSpawn) return false;
        Vector3 pos = spawner.GetRandomSpawnPosition();
        Quaternion rot = spawner.GetSpawnRotation();
        // Punto ya está dentro del volumen, no sumar spawnHeightOffset extra
        return TrySpawnAtExact(pos, rot);
    }

    private bool TrySpawnAtExact(Vector3 position, Quaternion rotation)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            if (NetworkManager.Singleton != null) return false;
        }
        if (!CanSpawn) return false;
        if (collectiblePrefab == null)
        {
            Debug.LogWarning("[CollectibleSpawnManager] collectiblePrefab no asignado.");
            return false;
        }
        NetworkObject instance = Instantiate(collectiblePrefab, position, rotation);
        if (instance != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            instance.Spawn(true);
        InteractableCube cube = instance.GetComponent<InteractableCube>();
        if (cube != null && !trackedInstances.Contains(cube))
            trackedInstances.Add(cube);
        return true;
    }

    private Transform GetRandomSpawnPoint()
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            return spawnPoints[Random.Range(0, spawnPoints.Length)];
        }

        if (useSpawnersAsSpawnPoints && registeredSpawners.Count > 0)
        {
            CollectibleSpawner s = registeredSpawners[Random.Range(0, registeredSpawners.Count)];
            return s != null ? s.transform : null;
        }

        return null;
    }

    private void SpawnInitialBatch()
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) return;

        // Reintentar si aún no hay spawners registrados pero se requieren
        if (useSpawnersAsSpawnPoints && registeredSpawners.Count == 0 && (spawnPoints == null || spawnPoints.Length == 0))
        {
            // Buscar spawners que aún no se registraron (orden de ejecución)
            foreach (CollectibleSpawner s in FindObjectsByType<CollectibleSpawner>(FindObjectsSortMode.None))
                RegisterSpawner(s);

            if (registeredSpawners.Count == 0)
            {
                Debug.LogWarning("[CollectibleSpawnManager] No hay CollectibleSpawner en escena, reintentando spawn inicial en 0.5s.");
                Invoke(nameof(SpawnInitialBatch), 0.5f);
                return;
            }
        }

        if (collectiblePrefab == null)
        {
            Debug.LogError("[CollectibleSpawnManager] collectiblePrefab no asignado. Asigna Object.prefab en el Inspector -> no se puede spawnear.");
            return;
        }

        int toSpawn = initialSpawnCount > 0 ? initialSpawnCount : maxInstances;
        toSpawn = Mathf.Min(toSpawn, RemainingSlots);
        int spawned = 0;
        for (int i = 0; i < toSpawn; i++)
        {
            if (TrySpawnRandom()) spawned++;
            else break;
        }
        Debug.Log($"[CollectibleSpawnManager] Spawn inicial: {spawned}/{toSpawn} | Activos: {ActiveCount}/{maxInstances}");

        // Si faltan por límite y no se pudo (prefab null / sin spawners), reintentar
        if (RemainingSlots > 0 && spawned < toSpawn)
        {
            Invoke(nameof(SpawnInitialBatch), 0.5f);
        }

        if (autoSpawnInterval > 0f)
            nextAutoSpawnTime = Time.time + autoSpawnInterval;
    }

    // ── Utilidades ─────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (maxInstances < 1) maxInstances = 1;
        if (initialSpawnCount < 0) initialSpawnCount = 0;
        if (autoSpawnInterval < 0f) autoSpawnInterval = 0f;
    }
#endif
}
