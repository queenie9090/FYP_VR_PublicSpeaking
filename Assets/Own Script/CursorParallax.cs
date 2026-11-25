using UnityEngine;
using UnityEngine.InputSystem;

public class CursorParallax : MonoBehaviour
{
    public enum ParallaxMode
    {
        Tilt3D,
        Offset2D,
        Both
    }

    [Header("Mode & Target Settings")]
    [Tooltip("Choose 3D rotation tilt, 2D position offset, or both at once.")]
    public ParallaxMode mode = ParallaxMode.Both;

    [Tooltip("Assign your Main Camera, 2D layer/sprite Transform, or UI RectTransform.")]
    public Transform targetTransform;

    [Header("3D Rotation Intensity")]
    [Tooltip("Maximum tilt rotation degrees (X and Y).")]
    public float maxTiltAngle = 6f;

    [Header("2D Parallax Settings")]
    [Tooltip("Maximum X and Y offset in local units (or pixels for UI).")]
    public Vector2 maxOffset = new Vector2(1.5f, 1.0f);

    [Tooltip("Optional subtle 2D roll (Z rotation in degrees) when moving horizontally.")]
    public float maxRollAngle = 0f;

    [Tooltip("Invert 2D movement direction (often feels more natural for cameras or background layers).")]
    public bool invert2D = false;

    [Header("Smoothing")]
    [Tooltip("Higher values make the movement snappier; lower values make it smoother.")]
    public float smoothSpeed = 6f;

    private Quaternion initialRotation;
    private Vector3 initialPosition;

    private void Start()
    {
        if (targetTransform == null)
            targetTransform = transform;

        initialRotation = targetTransform.localRotation;
        initialPosition = targetTransform.localPosition;
    }

    private void Update()
    {
        if (Mouse.current == null) return;

        // 1. Get normalized cursor position from -1 to 1 (Center screen = 0,0)
        Vector2 mousePosition = Mouse.current.position.ReadValue();
        float normalizedX = Mathf.Clamp((mousePosition.x / Screen.width) * 2f - 1f, -1f, 1f);
        float normalizedY = Mathf.Clamp((mousePosition.y / Screen.height) * 2f - 1f, -1f, 1f);

        float delta = Time.deltaTime * smoothSpeed;

        // 2. Apply 3D Tilt (keeps your original rotation behavior)
        if (mode == ParallaxMode.Tilt3D || mode == ParallaxMode.Both)
        {
            float tiltY = normalizedX * maxTiltAngle;
            float tiltX = -normalizedY * maxTiltAngle;
            float tiltZ = (mode == ParallaxMode.Offset2D) ? 0f : -normalizedX * maxRollAngle;

            Quaternion targetRotation = initialRotation * Quaternion.Euler(tiltX, tiltY, tiltZ);
            targetTransform.localRotation = Quaternion.Slerp(
                targetTransform.localRotation,
                targetRotation,
                delta
            );
        }

        // 3. Apply 2D Position Offset (and optional Z-roll if using Offset2D alone)
        if (mode == ParallaxMode.Offset2D || mode == ParallaxMode.Both)
        {
            float dir = invert2D ? -1f : 1f;

            Vector3 targetPosition = initialPosition + new Vector3(
                normalizedX * maxOffset.x * dir,
                normalizedY * maxOffset.y * dir,
                0f
            );

            targetTransform.localPosition = Vector3.Lerp(
                targetTransform.localPosition,
                targetPosition,
                delta
            );

            if (mode == ParallaxMode.Offset2D && Mathf.Abs(maxRollAngle) > 0.001f)
            {
                float rollZ = -normalizedX * maxRollAngle * dir;
                Quaternion target2DRotation = initialRotation * Quaternion.Euler(0f, 0f, rollZ);
                targetTransform.localRotation = Quaternion.Slerp(
                    targetTransform.localRotation,
                    target2DRotation,
                    delta
                );
            }
        }
    }
}