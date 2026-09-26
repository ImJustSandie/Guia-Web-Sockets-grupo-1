using UnityEngine;

public class CharacterCustomizationPreview : MonoBehaviour
{
    [Header("Skin Database (Opcional para Carga Automática)")]
    [Tooltip("Base de datos de skins para cargar automáticamente las preferencias guardadas en PlayerPrefs.")]
    [SerializeField] private CharacterSkinDatabase skinDatabase;

    [Header("Renderers / Mesh Components")]
    [Tooltip("Renderer responsable del sombrero/cabeza (Hat).")]
    [SerializeField] private Renderer hatRenderer;

    [Tooltip("Renderer responsable del cuerpo (Body).")]
    [SerializeField] private Renderer bodyRenderer;

    [Tooltip("Renderer responsable de la mochila (Bag).")]
    [SerializeField] private Renderer bagRenderer;

    [Header("Sub-material Indices (Opcional)")]
    [Tooltip("Índice de material dentro del Renderer si el objeto usa múltiples sub-materiales.")]
    [SerializeField] private int hatMaterialIndex = 0;
    [SerializeField] private int bodyMaterialIndex = 0;
    [SerializeField] private int bagMaterialIndex = 0;

    private void Start()
    {
        LoadSavedSkinsFromPlayerPrefs();
    }

    private void OnEnable()
    {
        LoadSavedSkinsFromPlayerPrefs();
    }

    /// <summary>
    /// Lee los índices guardados en PlayerPrefs y aplica los materiales correspondientes de la base de datos.
    /// </summary>
    public void LoadSavedSkinsFromPlayerPrefs()
    {
        if (skinDatabase == null) return;

        ApplyHatMaterial(skinDatabase.GetHatMaterial(PlayerCustomizationData.HatIndex));
        ApplyBodyMaterial(skinDatabase.GetBodyMaterial(PlayerCustomizationData.BodyIndex));
        ApplyBagMaterial(skinDatabase.GetBagMaterial(PlayerCustomizationData.BagIndex));
    }

    public void ApplyHatMaterial(Material newMat)
    {
        ApplyMaterialToRenderer(hatRenderer, hatMaterialIndex, newMat);
    }

    public void ApplyBodyMaterial(Material newMat)
    {
        ApplyMaterialToRenderer(bodyRenderer, bodyMaterialIndex, newMat);
    }

    public void ApplyBagMaterial(Material newMat)
    {
        ApplyMaterialToRenderer(bagRenderer, bagMaterialIndex, newMat);
    }

    private void ApplyMaterialToRenderer(Renderer rend, int index, Material newMat)
    {
        if (rend == null || newMat == null) return;

        Material[] materials = rend.materials;
        if (index >= 0 && index < materials.Length)
        {
            materials[index] = newMat;
            rend.materials = materials;
        }
    }
}
