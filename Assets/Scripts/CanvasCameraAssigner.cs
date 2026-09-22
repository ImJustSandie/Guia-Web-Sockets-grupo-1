using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Asigna y actualiza automáticamente la render camera (worldCamera) de un Canvas 
/// configurado en modo Screen Space - Camera.
/// En el Lobby se vincula a la Main Camera de la escena del Lobby.
/// En la escena de juego se vincula a la Main Camera activa (cámara del personaje).
/// </summary>
[RequireComponent(typeof(Canvas))]
public class CanvasCameraAssigner : MonoBehaviour
{
    [Tooltip("Nombre de la escena de Lobby.")]
    [SerializeField] private string lobbySceneName = "Lobby";

    private Canvas targetCanvas;

    private void Awake()
    {
        targetCanvas = GetComponent<Canvas>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        AssignCamera();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        AssignCamera();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        AssignCamera();
    }

    private void Update()
    {
        // Si el worldCamera es nulo o fue destruido al cambiar de escena, intentar reasignarlo.
        if (targetCanvas != null && targetCanvas.renderMode == RenderMode.ScreenSpaceCamera && targetCanvas.worldCamera == null)
        {
            AssignCamera();
        }
    }

    /// <summary>
    /// Asigna la cámara principal activa al Canvas.
    /// </summary>
    public void AssignCamera()
    {
        if (targetCanvas == null)
            targetCanvas = GetComponent<Canvas>();

        if (targetCanvas == null || targetCanvas.renderMode != RenderMode.ScreenSpaceCamera)
            return;

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            targetCanvas.worldCamera = mainCam;
        }
        else
        {
            // Buscar cualquier cámara activa como fallback si Camera.main no está etiquetada adecuadamente
            Camera anyCam = FindFirstObjectByType<Camera>();
            if (anyCam != null)
            {
                targetCanvas.worldCamera = anyCam;
            }
        }
    }
}
