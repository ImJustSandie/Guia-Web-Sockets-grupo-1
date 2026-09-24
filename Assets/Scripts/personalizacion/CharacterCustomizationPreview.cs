using UnityEngine;

public class CharacterCustomizationPreview : MonoBehaviour
{
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
