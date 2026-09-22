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

    [Header("Collectibles")]
    [Tooltip("Contador sincronizado de recolectables. Solo el servidor escribe.")]
    private NetworkVariable<int> collectedCount = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public event Action<bool> IsMovingChanged;
    public event Action<bool> IsCarryingChanged;
    public event Action<int> CollectedCountChanged;
    public event Action<PlayerNetworkState> NetworkStateChanged;

    public bool IsMoving { get; private set; }
    // Nuevo: IsCarrying ahora refleja si lleva al menos 1 recolectable. Se mantiene compatibilidad con código antiguo basado en carriedInteractable.
    public bool IsCarrying => CollectedCount > 0 || carriedInteractable != null;
    public int CollectedCount => collectedCount.Value;

    private bool lastCarrying;
    private int lastCollectedCount = -1;
    private PlayerNetworkState lastPublishedPlayerState;
    private bool hasPublishedPlayerState;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        collectedCount.OnValueChanged += OnCollectedCountChanged;
        // Sincronizar estado inicial
        OnCollectedCountChanged(0, collectedCount.Value);

        if (IsOwner)
        {
            SetupInputActions();
        }
        else
        {
            // Desactivar cámara local si está como hija o asociada a este avatar no-local
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) cam.enabled = false;

            AudioListener listener = GetComponentInChildren<AudioListener>();
            if (listener != null) listener.enabled = false;

            // Desactivar CharacterController en instancias remotas para que
            // ClientNetworkTransform sincronice la posición sin conflictos.
            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
        }
    }

    public override void OnNetworkDespawn()
    {
        collectedCount.OnValueChanged -= OnCollectedCountChanged;
        base.OnNetworkDespawn();
    }

    private void OnCollectedCountChanged(int previous, int current)
    {
        CollectedCountChanged?.Invoke(current);
        RefreshCarryingState();
        PublishPlayerState(true);
        Debug.Log($"[PlayerMovementManager] {name} recolectados: {current}");
    }

    private void OnEnable()
    {
        if (interactButton != null)
            interactButton.onClick.AddListener(OnInteractButtonPressed);

        if (IsSpawned && IsOwner)
        {
            SetupInputActions();
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
        // Recolección ahora es automática por trigger (InteractableCube.TryCollect).
        // Se mantiene el hook por compatibilidad pero ya no hace pickup manual.
        // Si quieres mantener interacción legacy, descomenta el bloque TryPickUp.
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
        Debug.Log($"[PlayerMovementManager] recolectados: {CollectedCount} | cargando cubo (legacy): {carriedInteractable != null}");
    }

    /// <summary>Llamado por InteractableCube al ser pisado (hitbox trigger). Solo el servidor incrementa NetworkVariable.</summary>
    public void AddCollected(int amount = 1)
    {
        if (amount <= 0) return;
        if (IsServer)
        {
            collectedCount.Value += amount;
        }
        else
        {
            RequestAddCollectedServerRpc(amount);
        }
    }

    /// <summary>Variante directa solo-servidor usada por InteractableCube.PerformCollect.</summary>
    public void AddCollectedServerSide(int amount = 1)
    {
        if (!IsServer) return;
        collectedCount.Value += amount;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestAddCollectedServerRpc(int amount)
    {
        collectedCount.Value += amount;
    }

    public PlayerNetworkState GetNetworkState()
    {
        return new PlayerNetworkState
        {
            entityId = gameObject.name,
            isMoving = IsMoving,
            isCarrying = IsCarrying,
            collectedCount = CollectedCount,
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
        bool carryingChanged = lastCarrying != carrying;
        bool countChanged = lastCollectedCount != CollectedCount;
        if (!carryingChanged && !countChanged) return;
        lastCarrying = carrying;
        lastCollectedCount = CollectedCount;
        if (carryingChanged)
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
        if (previous.collectedCount != current.collectedCount) return true;
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