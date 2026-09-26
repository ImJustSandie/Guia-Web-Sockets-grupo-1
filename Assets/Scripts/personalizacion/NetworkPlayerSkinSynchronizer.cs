using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Sincroniza la skin seleccionada del jugador local a través de la red (NGO)
/// para que todos los demás clientes la vean en tiempo real en el Lobby y la Partida.
/// </summary>
public class NetworkPlayerSkinSynchronizer : NetworkBehaviour
{
    [Header("References")]
    [Tooltip("Base de datos de skins con los materiales.")]
    [SerializeField] private CharacterSkinDatabase skinDatabase;

    [Tooltip("Componente de preview/renderers adjunto al jugador.")]
    [SerializeField] private CharacterCustomizationPreview customizationPreview;

    // Variables de red sincronizadas (el servidor escribe, todos leen)
    private readonly NetworkVariable<int> netHatIndex = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> netBodyIndex = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> netBagIndex = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (customizationPreview == null)
        {
            customizationPreview = GetComponent<CharacterCustomizationPreview>();
        }

        // Suscribirse a cambios en las variables de red para actualizar materiales en pantalla
        netHatIndex.OnValueChanged += OnHatChanged;
        netBodyIndex.OnValueChanged += OnBodyChanged;
        netBagIndex.OnValueChanged += OnBagChanged;

        // Si soy el cliente dueño de este personaje, enviar mis elecciones guardadas al servidor
        if (IsOwner)
        {
            int myHat = PlayerCustomizationData.HatIndex;
            int myBody = PlayerCustomizationData.BodyIndex;
            int myBag = PlayerCustomizationData.BagIndex;

            SubmitSkinSelectionServerRpc(myHat, myBody, myBag);
        }
        else
        {
            // Aplicar el estado actual que el servidor ya conoce para los demás jugadores
            ApplySkin(netHatIndex.Value, netBodyIndex.Value, netBagIndex.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        netHatIndex.OnValueChanged -= OnHatChanged;
        netBodyIndex.OnValueChanged -= OnBodyChanged;
        netBagIndex.OnValueChanged -= OnBagChanged;
    }

    [ServerRpc]
    private void SubmitSkinSelectionServerRpc(int hat, int body, int bag)
    {
        netHatIndex.Value = hat;
        netBodyIndex.Value = body;
        netBagIndex.Value = bag;
    }

    private void OnHatChanged(int oldVal, int newVal) => ApplySkin(newVal, netBodyIndex.Value, netBagIndex.Value);
    private void OnBodyChanged(int oldVal, int newVal) => ApplySkin(netHatIndex.Value, newVal, netBagIndex.Value);
    private void OnBagChanged(int oldVal, int newVal) => ApplySkin(netHatIndex.Value, netBodyIndex.Value, newVal);

    private void ApplySkin(int hat, int body, int bag)
    {
        if (customizationPreview == null || skinDatabase == null) return;

        customizationPreview.ApplyHatMaterial(skinDatabase.GetHatMaterial(hat));
        customizationPreview.ApplyBodyMaterial(skinDatabase.GetBodyMaterial(body));
        customizationPreview.ApplyBagMaterial(skinDatabase.GetBagMaterial(bag));
    }
}
