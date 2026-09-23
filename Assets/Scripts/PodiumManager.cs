using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;

/// <summary>
/// Gestor automatizado de la escena Podio.
/// - Ordena jugadores 1º→4º por puntuación (deliveredScore).
/// - Los coloca en PodiumSlots.
/// - Cada jugador salta y debajo se va formando una torre de bloques igual a su puntuación total.
/// Colocar este componente en un GameObject de la escena Podium (ej. PodiumManager).
/// Toda la lógica es local y determinista: cada cliente calcula el mismo ranking a partir de NetworkVariables sincronizadas.
/// </summary>
public class PodiumManager : MonoBehaviour
{
    [Header("Slots")]
    [Tooltip("4 Transforms para 1º,2º,3º,4º. Si está vacío, busca objetos PodiumSlot1..4 o genera posiciones por defecto.")]
    [SerializeField] private Transform[] podiumSlots = new Transform[4];
    [Tooltip("Altura base sobre el slot donde aparece el jugador.")]
    [SerializeField] private float playerHeightOffset = 2f;
    [Tooltip("Si no hay 4 slots, separación horizontal por defecto.")]
    [SerializeField] private float fallbackSpacing = 3f;

    [Header("Torre")]
    [Tooltip("Prefab del bloque de la torre. Si es null, se usa un Cube primitivo con material del collectible.")]
    [SerializeField] private GameObject towerBlockPrefab;
    [Tooltip("Altura de cada bloque.")]
    [SerializeField] private float blockHeight = 0.5f;
    [Tooltip("Tamaño del bloque (x,z).")]
    [SerializeField] private Vector3 blockSize = new Vector3(0.9f, 0.5f, 0.9f);
    [Tooltip("Intervalo entre bloques.")]
    [SerializeField] private float blockSpawnInterval = 0.22f;
    [Tooltip("Bloques por segundo en modo rápido (si puntuación es alta).")]
    [SerializeField] private float maxBuildDuration = 8f;

    [Header("Salto")]
    [Tooltip("Altura del salto.")]
    [SerializeField] private float jumpHeight = 1.1f;
    [Tooltip("Duración de cada salto.")]
    [SerializeField] private float jumpDuration = 0.45f;
    [Tooltip("Salto continuo mientras se construye, luego cada X seg.")]
    [SerializeField] private float idleJumpInterval = 1.2f;

    [Header("Ranking UI (opcional)")]
    [Tooltip("Textos opcionales para mostrar nombre/puntos en cada podio. Index 0=1º")]
    [SerializeField] private TMP_Text[] rankLabels;

    [Header("Debug")]
    [SerializeField] private bool logRanking = true;

    private readonly List<GameObject> spawnedBlocks = new List<GameObject>();
    private List<PlayerMovementManager> rankedPlayers = new List<PlayerMovementManager>();

    private void Start()
    {
        StartCoroutine(SetupPodiumRoutine());
    }

    private IEnumerator SetupPodiumRoutine()
    {
        // Esperar a que los NetworkObjects de los jugadores se spawneen en la nueva escena
        yield return new WaitForSeconds(0.5f);

        // Reintentar hasta encontrar al menos 1 jugador (máx 5s)
        float timeout = 5f;
        while (timeout > 0f)
        {
            rankedPlayers = FindObjectsByType<PlayerMovementManager>(FindObjectsSortMode.None).ToList();
            if (rankedPlayers.Count > 0) break;
            timeout -= 0.2f;
            yield return new WaitForSeconds(0.2f);
        }

        if (rankedPlayers.Count == 0)
        {
            Debug.LogWarning("[PodiumManager] No se encontraron jugadores en Podio.");
            yield break;
        }

        ResolveSlots();
        RankPlayers();

        // Colocar jugadores en sus slots
        for (int i = 0; i < rankedPlayers.Count && i < podiumSlots.Length; i++)
        {
            PlayerMovementManager pm = rankedPlayers[i];
            Transform slot = podiumSlots[i];
            TeleportPlayerToSlot(pm, slot);
            if (rankLabels != null && i < rankLabels.Length && rankLabels[i] != null)
            {
                rankLabels[i].text = $"#{i + 1} {pm.name.Replace("(Clone)", "")}\nPuntos: {pm.Score}";
            }
        }

        if (logRanking)
        {
            string log = "[PodiumManager] Ranking:\n";
            for (int i = 0; i < rankedPlayers.Count; i++)
                log += $"{i + 1}º {rankedPlayers[i].name} - {rankedPlayers[i].Score} pts (lleva {rankedPlayers[i].CollectedCount})\n";
            Debug.Log(log);
        }

        // Iniciar animación de salto + torres para cada jugador (local, determinista)
        for (int i = 0; i < rankedPlayers.Count && i < podiumSlots.Length; i++)
        {
            StartCoroutine(BuildTowerAndJumpRoutine(rankedPlayers[i], podiumSlots[i]));
        }
    }

    private void ResolveSlots()
    {
        // Si hay slots asignados, ok. Si no, buscar por nombre PodiumSlot1..4
        bool hasAny = podiumSlots != null && podiumSlots.Any(t => t != null);
        if (hasAny) return;

        List<Transform> found = new List<Transform>();
        for (int i = 1; i <= 4; i++)
        {
            GameObject go = GameObject.Find($"PodiumSlot{i}");
            if (go == null) go = GameObject.Find($"PodiumSlot {i}");
            if (go == null) go = GameObject.Find($"Slot{i}");
            found.Add(go != null ? go.transform : null);
        }

        if (found.Any(t => t != null))
        {
            podiumSlots = found.ToArray();
            // Rellenar nulos con fallback
            for (int i = 0; i < podiumSlots.Length; i++)
                if (podiumSlots[i] == null)
                    podiumSlots[i] = CreateFallbackSlot(i);
            return;
        }

        // Generar 4 slots por defecto en línea, 1º al centro más alto
        podiumSlots = new Transform[4];
        for (int i = 0; i < 4; i++)
            podiumSlots[i] = CreateFallbackSlot(i);
    }

    private Transform CreateFallbackSlot(int index)
    {
        // Orden visual: 1º centro alto, 2º izq, 3º der, 4º fondo
        // Si el manager está muy lejos (ej. 2000+ unidades por error de escena), usar origen en vez de su posición
        Vector3 anchor = transform.position;
        if (anchor.magnitude > 1000f)
            anchor = Vector3.zero;

        // Intentar anclar cerca de la Main Camera del podio para que sea visible
        Camera cam = Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();
        if (cam != null && Vector3.Distance(anchor, cam.transform.position) > 50f)
        {
            // Colocar slots frente a la cámara
            anchor = cam.transform.position + cam.transform.forward * 10f;
            anchor.y = 0f;
        }

        Vector3[] offsets = new Vector3[]
        {
            new Vector3(0f, 0f, 0f),          // 1º
            new Vector3(-fallbackSpacing, -0.5f, 0f), // 2º
            new Vector3(fallbackSpacing, -1f, 0f),  // 3º
            new Vector3(0f, -1.5f, -fallbackSpacing), // 4º
        };
        GameObject go = new GameObject($"PodiumSlot{index + 1}_Auto");
        go.transform.position = anchor + offsets[Mathf.Min(index, offsets.Length - 1)];
        go.transform.rotation = Quaternion.identity;
        return go.transform;
    }

    private void RankPlayers()
    {
        // Ordenar por Score descendente, desempate por OwnerClientId
        rankedPlayers = rankedPlayers
            .OrderByDescending(p => p.Score)
            .ThenBy(p => p.OwnerClientId)
            .ToList();
    }

    private void TeleportPlayerToSlot(PlayerMovementManager pm, Transform slot)
    {
        if (pm == null || slot == null) return;
        Vector3 pos = slot.position + Vector3.up * playerHeightOffset;
        Quaternion rot = slot.rotation;

        // Desactivar movimiento autónomo del jugador durante el podio para que no interfiera con la animación
        // (PlayerMovementManager.Update dejaría de responder a input)
        pm.enabled = false;

        // Desactivar CharacterController para teleport sin colisión
        CharacterController cc = pm.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        pm.transform.position = pos;
        pm.transform.rotation = rot;
        if (cc != null) cc.enabled = true;

        // Si es el owner local, asegurar que la cámara siga
        if (pm.IsOwner)
        {
            CameraYawPitchDragController camCtrl = pm.GetComponentInChildren<CameraYawPitchDragController>(true);
            if (camCtrl != null)
            {
                camCtrl.enabled = true;
                camCtrl.SetTarget(pm.transform);
            }
            // Asegurar cámara del owner activa (en MainScene se desactiva para remotos)
            Camera cam = pm.GetComponentInChildren<Camera>(true);
            if (cam != null) cam.enabled = true;
        }
    }

    private IEnumerator BuildTowerAndJumpRoutine(PlayerMovementManager player, Transform slot)
    {
        if (player == null || slot == null) yield break;
        // Total real = entregados + los que aún lleva encima (por si no alcanzó a entregar)
        int totalBlocks = player.Score + player.CollectedCount;
        if (totalBlocks <= 0)
        {
            // Sin puntos: solo salto idle
            while (true)
            {
                yield return AnimateJump(player, slot.position + Vector3.up * playerHeightOffset);
                yield return new WaitForSeconds(idleJumpInterval);
            }
        }

        // Calcular intervalo adaptado para no tardar demasiado si hay muchos puntos
        float interval = blockSpawnInterval;
        if (totalBlocks * interval > maxBuildDuration)
            interval = maxBuildDuration / totalBlocks;

        Vector3 basePos = slot.position;
        GameObject blockPrefab = ResolveBlockPrefab();

        for (int i = 0; i < totalBlocks; i++)
        {
            Vector3 blockPos = basePos + Vector3.up * (i * blockHeight);

            // Spawnear bloque local (no necesita NetworkObject, es visual de podio)
            GameObject block = Instantiate(blockPrefab, blockPos, Quaternion.identity);
            block.name = $"Tower_{player.OwnerClientId}_{i}";
            // Asegurar escala
            block.transform.localScale = blockSize;
            // Si es primitivo Cube, ajustar collider
            spawnedBlocks.Add(block);

            // Animar salto del jugador a la nueva altura
            Vector3 targetPlayerPos = basePos + Vector3.up * (playerHeightOffset + (i + 1) * blockHeight);
            yield return AnimateJump(player, targetPlayerPos);

            yield return new WaitForSeconds(interval);
        }

        // Torre terminada: salto idle periódico en la cima
        Vector3 topPos = basePos + Vector3.up * (playerHeightOffset + totalBlocks * blockHeight);
        while (true)
        {
            yield return AnimateJump(player, topPos);
            yield return new WaitForSeconds(idleJumpInterval);
        }
    }

    private GameObject ResolveBlockPrefab()
    {
        if (towerBlockPrefab != null) return towerBlockPrefab;

        // Fallback: crear Cube primitivo con material simple
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        // Quitar collider trigger innecesario si es solo visual, pero dejar BoxCollider como sólido de torre
        Renderer r = cube.GetComponent<Renderer>();
        if (r != null)
        {
            // Color por ranking o por defecto
            r.material.color = new Color(0.95f, 0.85f, 0.4f);
        }
        // Desactivar para que no interfiera como trigger de entrega
        // Lo dejamos como prefab temporal: destruir el template tras clonar, usamos Instantiate
        // Para evitar que el template quede en escena, lo desactivamos y lo usamos como prefab
        cube.SetActive(false);
        towerBlockPrefab = cube;
        return towerBlockPrefab;
    }

    private IEnumerator AnimateJump(PlayerMovementManager player, Vector3 targetPos)
    {
        if (player == null) yield break;
        Vector3 startPos = player.transform.position;
        // Si ya está en target, solo hacer pequeño salto vertical
        if (Vector3.Distance(new Vector3(startPos.x, 0, startPos.z), new Vector3(targetPos.x, 0, targetPos.z)) < 0.01f)
        {
            // Salto vertical en sitio
            float t = 0f;
            Vector3 basePos = new Vector3(startPos.x, targetPos.y, startPos.z);
            while (t < 1f)
            {
                t += Time.deltaTime / jumpDuration;
                float y = Mathf.Sin(t * Mathf.PI) * jumpHeight;
                CharacterController cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                player.transform.position = basePos + Vector3.up * y;
                if (cc != null) cc.enabled = true;
                yield return null;
            }
            // Asegurar posición final
            CharacterController cc2 = player.GetComponent<CharacterController>();
            if (cc2 != null) cc2.enabled = false;
            player.transform.position = targetPos;
            if (cc2 != null) cc2.enabled = true;
        }
        else
        {
            // Movimiento lateral + salto parabólico hacia slot
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / jumpDuration;
                Vector3 horiz = Vector3.Lerp(startPos, targetPos, t);
                float y = Mathf.Sin(t * Mathf.PI) * jumpHeight;
                horiz.y += y;
                CharacterController cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                player.transform.position = horiz;
                if (cc != null) cc.enabled = true;
                yield return null;
            }
            CharacterController cc3 = player.GetComponent<CharacterController>();
            if (cc3 != null) cc3.enabled = false;
            player.transform.position = targetPos;
            if (cc3 != null) cc3.enabled = true;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (podiumSlots == null) return;
        for (int i = 0; i < podiumSlots.Length; i++)
        {
            Transform t = podiumSlots[i];
            if (t == null) continue;
            Gizmos.color = i == 0 ? Color.yellow : (i == 1 ? Color.gray : new Color(0.8f, 0.5f, 0.2f));
            Gizmos.DrawWireCube(t.position, blockSize);
            Gizmos.DrawLine(t.position, t.position + Vector3.up * 3f);
        }
    }

    private void OnValidate()
    {
        if (blockHeight <= 0f) blockHeight = 0.5f;
        if (blockSpawnInterval <= 0f) blockSpawnInterval = 0.1f;
        if (fallbackSpacing <= 0f) fallbackSpacing = 3f;
    }
#endif
}
