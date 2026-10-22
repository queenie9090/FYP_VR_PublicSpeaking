using UnityEngine;
using UnityEngine.InputSystem;

public class CursorParallax : MonoBehaviour
{
    [Header("Target Settings")]
    [Tooltip("Assign your Main Camera or UI Panel Transform.")]
    public Transform targetTransform;

    [Header("Rotation Intensity")]
    [Tooltip("Maximum tilt rotation degrees (X and Y).")]
    public float maxTiltAngle = 6f;

    [Header("Smoothing")]
    [Tooltip("Higher values make the movement snappier; lower values make it smoother.")]
    public float smoothSpeed = 6f;

    private Quaternion initialRotation;

    private void Start()
    {
        if (targetTransform == null)
            targetTransform = transform;

        initialRotation = targetTransform.localRotation;
    }

    private void Update()
    {
        if (Mouse.current == null) return;

        // 1. Get normalized cursor position from -1 to 1 (Center screen = 0,0)
        Vector2 mousePosition = Mouse.current.position.ReadValue();
        float normalizedX = (mousePosition.x / Screen.width) * 2f - 1f;
        float normalizedY = (mousePosition.y / Screen.height) * 2f - 1f;

        normalizedX = Mathf.Clamp(normalizedX, -1f, 1f);
        normalizedY = Mathf.Clamp(normalizedY, -1f, 1f);

        // 2. Map screen coordinates directly to 3D tilt angles:
        // - Cursor right/left rotates around Y axis (Yaw)
        // - Cursor up/down rotates around X axis (Pitch)
        float tiltY = normalizedX * maxTiltAngle;
        float tiltX = -normalizedY * maxTiltAngle;

        // 3. Combine with initial orientation
        Quaternion targetRotation = initialRotation * Quaternion.Euler(tiltX, tiltY, 0f);

        // 4. Smoothly rotate towards target angle
        targetTransform.localRotation = Quaternion.Slerp(
            targetTransform.localRotation,
            targetRotation,
            Time.deltaTime * smoothSpeed
        );
    }
}