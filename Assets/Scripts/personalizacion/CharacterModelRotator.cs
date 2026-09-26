using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Permite rotar el modelo del personaje en la escena de personalización mediante drag/arrastre
/// con el mouse o pantalla táctil, ofreciendo suavizado e inercia opcional.
/// </summary>
public class CharacterModelRotator : MonoBehaviour
{
    [Header("Rotation Settings")]
    [Tooltip("Sensibilidad de rotación al arrastrar.")]
    [SerializeField] private float rotationSpeed = 0.4f;

    [Tooltip("Factor de suavizado / inercia de la rotación tras soltar.")]
    [SerializeField] private float damping = 5f;

    [Tooltip("Si es true, la rotación solo responde al arrastrar sobre áreas libres (ignora clics sobre botones de la UI).")]
    [SerializeField] private bool ignoreDragsOverUI = true;

    [Header("Auto Rotation (Opcional)")]
    [Tooltip("Rotación continua automática cuando no se está interactuando con el personaje.")]
    [SerializeField] private bool autoRotateWhenIdle = false;

    [Tooltip("Velocidad de la rotación automática en grados por segundo.")]
    [SerializeField] private float autoRotationSpeed = 15f;

    private float currentVelocity;
    private bool isDragging;
    private Vector2 lastPointerPosition;

    private void Update()
    {
        HandleInput();
        ApplyRotation();
    }

    private void HandleInput()
    {
        // Soporte para Mouse e Input System
        if (Mouse.current != null)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (!ignoreDragsOverUI || !EventSystem.current.IsPointerOverGameObject())
                {
                    isDragging = true;
                    lastPointerPosition = Mouse.current.position.ReadValue();
                }
            }
            else if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                isDragging = false;
            }

            if (isDragging && Mouse.current.leftButton.isPressed)
            {
                Vector2 currentPos = Mouse.current.position.ReadValue();
                Vector2 delta = currentPos - lastPointerPosition;
                currentVelocity = -delta.x * rotationSpeed;
                lastPointerPosition = currentPos;
            }
        }

        // Soporte para Touch (Dispositivos móviles / pantallas táctiles)
        if (Touchscreen.current != null && Touchscreen.current.touches.Count > 0)
        {
            var touch = Touchscreen.current.touches[0];
            int touchId = touch.touchId.ReadValue();

            if (touch.press.wasPressedThisFrame)
            {
                if (!ignoreDragsOverUI || !EventSystem.current.IsPointerOverGameObject(touchId))
                {
                    isDragging = true;
                    lastPointerPosition = touch.position.ReadValue();
                }
            }
            else if (touch.press.wasReleasedThisFrame)
            {
                isDragging = false;
            }

            if (isDragging && touch.press.isPressed)
            {
                Vector2 currentPos = touch.position.ReadValue();
                Vector2 delta = currentPos - lastPointerPosition;
                currentVelocity = -delta.x * rotationSpeed;
                lastPointerPosition = currentPos;
            }
        }
    }

    private void ApplyRotation()
    {
        if (isDragging)
        {
            transform.Rotate(Vector3.up, currentVelocity, Space.World);
        }
        else
        {
            if (autoRotateWhenIdle && Mathf.Abs(currentVelocity) < 0.01f)
            {
                transform.Rotate(Vector3.up, autoRotationSpeed * Time.deltaTime, Space.World);
            }
            else
            {
                // Aplicar amortiguación / inercia
                transform.Rotate(Vector3.up, currentVelocity, Space.World);
                currentVelocity = Mathf.Lerp(currentVelocity, 0f, Time.deltaTime * damping);
            }
        }
    }

    /// <summary>
    /// Restablece la rotación del modelo a su ángulo frontal original.
    /// </summary>
    public void ResetRotation()
    {
        transform.rotation = Quaternion.identity;
        currentVelocity = 0f;
    }
}
