using System;
using Unity.Netcode;
using UnityEngine;

public class InteractableCube : NetworkBehaviour
{
    [SerializeField] private Vector3 holdOffset = new Vector3(0f, 1f, 1.5f);
    [SerializeField] private float interactionRange = 1.3f;
    [SerializeField] private float carrySmoothTime = 8f;
    [SerializeField] private float maxPickupDistance = 3f;

    private Collider solidCollider;
    private Collider interactionCollider;
    private Collider playerCollider;
    private Rigidbody cubeRigidbody;

    private bool isCarried;
    private Transform carrier;

    public event Action<bool> IsBeingCarriedChanged;
    public event Action<bool> IsGroundedChanged;
    public event Action<CubeNetworkState> NetworkStateChanged;

    public bool IsBeingCarried => isCarried;
    public bool IsGrounded { get; private set; }

    private bool lastCarrying;
    private bool groundTouchThisStep;
    private CubeNetworkState lastPublishedCubeState;
    private bool hasPublishedCubeState;

    private void Awake()
    {
        cubeRigidbody = GetComponent<Rigidbody>();
        foreach (Collider c in GetComponents<Collider>())
        {
            if (!c.isTrigger && solidCollider == null)
                solidCollider = c;
            else if (c.isTrigger && interactionCollider == null)
                interactionCollider = c;
        }

        if (solidCollider == null)
            solidCollider = gameObject.AddComponent<BoxCollider>();

        if (interactionCollider == null)
        {
            BoxCollider trigger = gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            if (solidCollider is BoxCollider boxSolid)
                trigger.size = boxSolid.size * interactionRange;
            interactionCollider = trigger;
        }
    }

    private void Start()
    {
        RefreshCarriedState();
        RefreshGroundedState(groundTouchThisStep || cubeRigidbody == null);
        PublishCubeState(true);
    }

    private void FixedUpdate()
    {
        if (isCarried)
        {
            SetGrounded(false);
        }
        else if (cubeRigidbody == null)
        {
            SetGrounded(true);
        }
        else
        {
            SetGrounded(groundTouchThisStep);
        }
        groundTouchThisStep = false;

        PublishCubeState();
    }

    private void OnCollisionEnter(Collision collision)
    {
        RegisterGroundContacts(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        RegisterGroundContacts(collision);
    }

    private void RegisterGroundContacts(Collision collision)
    {
        foreach (ContactPoint contact in collision.contacts)
        {
            if (Mathf.Abs(contact.normal.y) > 0.5f)
            {
                groundTouchThisStep = true;
                break;
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        var player = other.GetComponentInParent<PlayerMovementManager>();
        if (player == null) return;

        playerCollider = other;
        player.SetInteractableInRange(this, true);
    }

    private void OnTriggerStay(Collider other)
    {
        if (isCarried) return;

        var player = other.GetComponentInParent<PlayerMovementManager>();
        if (player == null) return;

        playerCollider = other;
        player.SetInteractableInRange(this, true);
    }

    private void OnTriggerExit(Collider other)
    {
        // Mientras esta cargado el trigger se mueve con el cubo y
        // inevitablemente sale del jugador. Ignorar esa salida para
        // no perder la referencia ni provocar un Drop inmediato.
        if (isCarried) return;

        var player = other.GetComponentInParent<PlayerMovementManager>();
        if (player == null) return;

        if (other == playerCollider) playerCollider = null;
        player.OnInteractableExitRange(this);
    }

    private void LateUpdate()
    {
        if (!isCarried || carrier == null) return;

        Vector3 target = carrier.TransformPoint(holdOffset);
        transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-carrySmoothTime * Time.deltaTime));
        PublishCubeState();
    }

    public bool TryPickUp(PlayerMovementManager player)
    {
        if (isCarried || player == null) return false;
        if (Vector3.Distance(transform.position, player.transform.position) > maxPickupDistance) return false;
        if (solidCollider == null) return false;

        if (IsServer)
        {
            PerformPickUp(player.NetworkObjectId);
        }
        else
        {
            RequestPickUpServerRpc(player.NetworkObjectId);
        }
        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestPickUpServerRpc(ulong playerNetworkObjectId)
    {
        PerformPickUp(playerNetworkObjectId);
    }

    private void PerformPickUp(ulong playerNetworkObjectId)
    {
        // Si ya está siendo cargado, ignorar
        if (isCarried) return;

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerNetworkObjectId, out NetworkObject playerObj))
        {
            isCarried = true;
            carrier = playerObj.transform;

            // Ignorar colisión entre el jugador y el cubo sólido
            IgnorePlayerCollision(playerObj, true);

            if (cubeRigidbody != null)
            {
                cubeRigidbody.linearVelocity = Vector3.zero;
                cubeRigidbody.angularVelocity = Vector3.zero;
                cubeRigidbody.isKinematic = true;
                cubeRigidbody.useGravity = false;
            }
            SetGrounded(false);
            PublishCubeState(true);
            NotifyPickUpClientRpc(playerNetworkObjectId);
        }
    }

    /// <summary>Ignora o restaura colisiones entre todos los colliders del jugador y el solidCollider del cubo.</summary>
    private void IgnorePlayerCollision(NetworkObject playerObj, bool ignore)
    {
        if (solidCollider == null) return;
        foreach (Collider col in playerObj.GetComponentsInChildren<Collider>())
        {
            if (!col.isTrigger)
                Physics.IgnoreCollision(col, solidCollider, ignore);
        }
    }

    [Rpc(SendTo.NotServer)]
    private void NotifyPickUpClientRpc(ulong playerNetworkObjectId)
    {
        if (IsServer) return; // Ya procesado en el servidor

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerNetworkObjectId, out NetworkObject playerObj))
        {
            isCarried = true;
            carrier = playerObj.transform;

            IgnorePlayerCollision(playerObj, true);

            if (cubeRigidbody != null)
            {
                cubeRigidbody.isKinematic = true;
                cubeRigidbody.useGravity = false;
            }
            SetGrounded(false);
            PublishCubeState(true);
        }
    }

    public void Drop()
    {
        if (IsServer)
        {
            PerformDrop();
        }
        else
        {
            RequestDropServerRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestDropServerRpc()
    {
        PerformDrop();
    }

    private void PerformDrop()
    {
        if (!isCarried) return;

        // Restaurar colisiones con el carrier
        if (carrier != null)
        {
            NetworkObject carrierObj = carrier.GetComponent<NetworkObject>();
            if (carrierObj != null)
                IgnorePlayerCollision(carrierObj, false);
        }

        ulong carrierNetObjId = 0;
        if (carrier != null)
        {
            var nObj = carrier.GetComponent<NetworkObject>();
            if (nObj != null) carrierNetObjId = nObj.NetworkObjectId;
        }

        isCarried = false;
        carrier = null;

        if (cubeRigidbody != null)
        {
            cubeRigidbody.isKinematic = false;
            cubeRigidbody.useGravity = true;
            cubeRigidbody.linearVelocity = Vector3.zero;
            cubeRigidbody.angularVelocity = Vector3.zero;
        }

        RefreshGroundedState(cubeRigidbody == null);
        PublishCubeState(true);
        NotifyDropClientRpc(carrierNetObjId);
    }

    [Rpc(SendTo.NotServer)]
    private void NotifyDropClientRpc(ulong carrierNetworkObjectId)
    {
        if (IsServer) return;

        // Restaurar colisiones con el carrier en el cliente
        if (carrierNetworkObjectId != 0 &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(carrierNetworkObjectId, out NetworkObject carrierObj))
        {
            IgnorePlayerCollision(carrierObj, false);
        }

        isCarried = false;
        carrier = null;

        if (cubeRigidbody != null)
        {
            cubeRigidbody.isKinematic = false;
            cubeRigidbody.useGravity = true;
        }

        RefreshGroundedState(cubeRigidbody == null);
        PublishCubeState(true);
    }

    public CubeNetworkState GetNetworkState()
    {
        return new CubeNetworkState
        {
            entityId = gameObject.name,
            isBeingCarried = isCarried,
            isGrounded = IsGrounded,
            position = transform.position
        };
    }

    private void SetGrounded(bool grounded)
    {
        if (IsGrounded == grounded) return;
        IsGrounded = grounded;
        IsGroundedChanged?.Invoke(grounded);
    }

    private void RefreshGroundedState(bool grounded)
    {
        SetGrounded(grounded);
    }

    private void RefreshCarriedState()
    {
        if (lastCarrying == isCarried) return;
        lastCarrying = isCarried;
        IsBeingCarriedChanged?.Invoke(isCarried);
    }

    private void PublishCubeState(bool force = false)
    {
        RefreshCarriedState();
        CubeNetworkState state = GetNetworkState();
        if (!force && hasPublishedCubeState && !CubeStateDiffers(lastPublishedCubeState, state))
            return;

        lastPublishedCubeState = state;
        hasPublishedCubeState = true;
        NetworkStateChanged?.Invoke(state);
        NetworkEventBus.Publish(state);
    }

    private static bool CubeStateDiffers(CubeNetworkState previous, CubeNetworkState current)
    {
        if (previous.isBeingCarried != current.isBeingCarried) return true;
        if (previous.isGrounded != current.isGrounded) return true;
        if (Vector3.Distance(previous.position, current.position) > 0.001f) return true;
        return false;
    }
}