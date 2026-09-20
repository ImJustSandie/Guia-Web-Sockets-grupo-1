using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PlayerMovementManager : NetworkBehaviour
{
    [SerializeField] private InputActionAsset actionsAsset;
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private UnityEvent onInteract;

    [Header("Touch UI")]
    [Tooltip("Objeto del virtual pad (joystick) creado en el Canvas. Referencia para tenerlo localizado desde el player.")]
    [SerializeField] private GameObject virtualMovePadObject;
    [Tooltip("Boton de interactuar creado en el Canvas. Al asignarlo se suscribe su onClick automaticamente.")]
    [SerializeField] private Button interactButton;

    [Header("Camera Relative")]
    [Tooltip("Camara de referencia para el movimiento. Si se deja vacio se usa Camera.main.")]
    [SerializeField] private Transform cameraTransform;
    [Tooltip("Si es true, el jugador rota para mirar hacia la direccion de movimiento.")]
    [SerializeField] private bool faceMoveDirection = true;
    [SerializeField] private float turnSpeed = 10f;

    private Vector2 virtualMoveInput;

    private InputAction moveAction;
    private InputAction interactAction;

    private InteractableCube currentInteractable;
    private InteractableCube carriedInteractable;

    public event Action<bool> IsMovingChanged;
    public event Action<bool> IsCarryingChanged;
    public event Action<PlayerNetworkState> NetworkStateChanged;

    public bool IsMoving { get; private set; }
    public bool IsCarrying => carriedInteractable != null;

    private bool lastCarrying;
    private PlayerNetworkState lastPublishedPlayerState;
    private bool hasPublishedPlayerState;

    [Header("Scene Camera Settings")]
    [Tooltip("Nombre de la escena de Lobby donde la cámara del personaje debe permanecer desactivada.")]
    [SerializeField] private string lobbySceneName = "Lobby";

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        UpdateCameraState();

        if (IsOwner)
        {
            SetupInputActions();
        }
        else
        {
            // Desactivar CharacterController en instancias remotas para que
            // ClientNetworkTransform sincronice la posición sin conflictos.
            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
        }
    }

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

        if (interactButton != null)
            interactButton.onClick.AddListener(OnInteractButtonPressed);

        if (IsSpawned)
        {
            UpdateCameraState();
            if (IsOwner)
            {
                SetupInputActions();
            }
        }
    }

    private void SetupInputActions()
    {
        if (actionsAsset == null)
        {
            Debug.LogWarning($"{name}: asignar InputSystem_Actions a actionsAsset en el Inspector.");
            return;
        }

        if (moveAction == null)
            moveAction = actionsAsset.FindAction("Player/Move");
        if (interactAction == null)
            interactAction = actionsAsset.FindAction("Player/Interact");

        moveAction?.Enable();
        if (interactAction != null)
        {
            interactAction.Enable();
            interactAction.performed -= HandleInteract;
            interactAction.performed += HandleInteract;
        }
    }

    private void Start()
    {
        PublishPlayerState(true);
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

        if (interactButton != null)
            interactButton.onClick.RemoveListener(OnInteractButtonPressed);
        if (moveAction != null)
        {
            moveAction.Disable();
            moveAction = null;
        }

        if (interactAction != null)
        {
            interactAction.performed -= HandleInteract;
            interactAction.Disable();
            interactAction = null;
        }
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        UpdateCameraState();
    }

    /// <summary>
    /// Activa o desactiva la cámara del personaje según la escena activa y la autoridad (IsOwner).
    /// En el Lobby la cámara del personaje siempre está desactivada para usar la Main Camera del Lobby.
    /// En el juego (MainScene) la cámara se activa únicamente para el jugador local (IsOwner).
    /// </summary>
    private void UpdateCameraState()
    {
        string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        bool inLobby = (currentScene == lobbySceneName);

        Camera cam = GetComponentInChildren<Camera>();
        AudioListener listener = GetComponentInChildren<AudioListener>();

        if (!IsOwner || inLobby)
        {
            if (cam != null) cam.enabled = false;
            if (listener != null) listener.enabled = false;
        }
        else
        {
            if (cam != null) cam.enabled = true;
            if (listener != null) listener.enabled = true;
        }
    }

    [SerializeField] private float gravity = -9.81f;
    private float verticalVelocity;

    private void Update()
    {
        if (!IsOwner) return;

        CharacterController cc = GetComponent<CharacterController>();

        // Aplicar Gravedad
        if (cc != null && cc.enabled)
        {
            if (cc.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f; // Mantener al jugador pegado al suelo
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }
        }

        Vector2 input = Vector2.zero;

        if (moveAction != null && moveAction.enabled)
            input += moveAction.ReadValue<Vector2>();

        input += virtualMoveInput;
        if (input.sqrMagnitude > 1f) input.Normalize();

        bool moving = input.sqrMagnitude >= 0.0001f;
        SetIsMoving(moving);
        RefreshCarryingState();

        Transform cam = cameraTransform;
        if (cam == null && Camera.main != null)
            cam = Camera.main.transform;

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        if (cam != null)
        {
            forward = cam.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            else
                forward.Normalize();

            right = cam.right;
            right.y = 0f;
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            else
                right.Normalize();
        }

        Vector3 direction = right * input.x + forward * input.y;
        if (direction.sqrMagnitude > 1f) direction.Normalize();

        Vector3 velocity = direction * moveSpeed;
        velocity.y = verticalVelocity;

        if (cc != null && cc.enabled)
        {
            cc.Move(velocity * Time.deltaTime);
        }
        else
        {
            transform.position += velocity * Time.deltaTime;
        }

        if (moving && faceMoveDirection && direction.sqrMagnitude >= 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRotation, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
        }

        RefreshCarryingState();
        PublishPlayerState();
    }

    /// <summary>Llamado por el joystick virtual (evento Vector2) para mover al jugador.</summary>
    public void SetVirtualMove(Vector2 input)
    {
        if (input.sqrMagnitude > 1f) input.Normalize();
        virtualMoveInput = input;
    }

    /// <summary>Llamado por el boton UI de interactuar (onClick).</summary>
    public void OnInteractButtonPressed()
    {
        onInteract?.Invoke();
        HandleInteraction();
    }

    private void HandleInteract(InputAction.CallbackContext context)
    {
        onInteract?.Invoke();
        HandleInteraction();
    }

    private void HandleInteraction()
    {
        InteractableCube target = currentInteractable;

        if (target != null)
        {
            if (target == carriedInteractable)
            {
                DropCarried();
                return;
            }

            if (carriedInteractable != null)
                DropCarried();

            if (target.TryPickUp(this))
                carriedInteractable = target;

            LogState();
            return;
        }

        if (carriedInteractable != null)
            DropCarried();

        LogState();
    }

    private void DropCarried()
    {
        if (carriedInteractable == null) return;
        carriedInteractable.Drop();
        carriedInteractable = null;
        LogState();
    }

    private void LogState()
    {
        Debug.Log($"[PlayerMovementManager] cargando cubo: {carriedInteractable != null}");
    }

    public PlayerNetworkState GetNetworkState()
    {
        return new PlayerNetworkState
        {
            entityId = gameObject.name,
            isMoving = IsMoving,
            isCarrying = IsCarrying,
            position = transform.position,
            rotation = transform.rotation
        };
    }

    private void SetIsMoving(bool moving)
    {
        if (IsMoving == moving) return;
        IsMoving = moving;
        IsMovingChanged?.Invoke(moving);
    }

    private void RefreshCarryingState()
    {
        bool carrying = IsCarrying;
        if (lastCarrying == carrying) return;
        lastCarrying = carrying;
        IsCarryingChanged?.Invoke(carrying);
    }

    private void PublishPlayerState(bool force = false)
    {
        PlayerNetworkState state = GetNetworkState();
        if (!force && hasPublishedPlayerState && !PlayerStateDiffers(lastPublishedPlayerState, state))
            return;

        lastPublishedPlayerState = state;
        hasPublishedPlayerState = true;
        NetworkStateChanged?.Invoke(state);
        NetworkEventBus.Publish(state);
    }

    private static bool PlayerStateDiffers(PlayerNetworkState previous, PlayerNetworkState current)
    {
        if (previous.isMoving != current.isMoving) return true;
        if (previous.isCarrying != current.isCarrying) return true;
        if (Vector3.Distance(previous.position, current.position) > 0.001f) return true;
        if (Quaternion.Angle(previous.rotation, current.rotation) > 0.5f) return true;
        return false;
    }

    public void SetInteractableInRange(InteractableCube interactable, bool inRange)
    {
        if (inRange)
            currentInteractable = interactable;
        else if (currentInteractable == interactable)
            currentInteractable = null;
    }

    public void OnInteractableExitRange(InteractableCube interactable)
    {
        SetInteractableInRange(interactable, false);
        // No soltar aqui: al llevar el cubo, su trigger se mueve con el
        // y saldria del rango del jugador, lo que provocaba un Drop
        // inmediato (el "pequeno movimiento" reportado).
        // Soltar solo ocurre al pulsar Interact de nuevo.
    }
}