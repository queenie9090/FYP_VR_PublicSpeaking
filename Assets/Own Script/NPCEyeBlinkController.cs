
using UnityEngine;

public class NPCEyeBlinkController : MonoBehaviour
{
    [Header("NPC Face Mesh")]
    [SerializeField] private SkinnedMeshRenderer faceMesh;

    [Header("Blink BlendShape Names")]
    [SerializeField] private string leftBlink = "eyeBlinkLeft";
    [SerializeField] private string rightBlink = "eyeBlinkRight";

    [Header("Blink Timer Settings")]
    [SerializeField] private float minBlinkInterval = 2.5f;
    [SerializeField] private float maxBlinkInterval = 5.5f;

    [Range(0.05f, 0.5f)]
    [SerializeField] private float blinkDuration = 0.18f;

    [Range(0f, 100f)]
    [SerializeField] private float blinkStrength = 100f;

    private float blinkTimer = 0f;
    private float blinkInterval;
    private float blinkProgress = 0f;

    private bool isBlinking = false;

    private int leftIndex = -1;
    private int rightIndex = -1;

    private float originalLeft;
    private float originalRight;

    private void Start()
    {
        if (faceMesh == null || faceMesh.sharedMesh == null)
        {
            Debug.LogError(
                $"[{name}] Eye Blink: Face Mesh not assigned."
            );
            enabled = false;
            return;
        }

        leftIndex =
            faceMesh.sharedMesh.GetBlendShapeIndex(leftBlink);

        rightIndex =
            faceMesh.sharedMesh.GetBlendShapeIndex(rightBlink);

        if (leftIndex < 0 || rightIndex < 0)
        {
            Debug.LogError(
                $"[{name}] Eye Blink: BlendShapes not found."
            );
            enabled = false;
            return;
        }

        originalLeft = faceMesh.GetBlendShapeWeight(leftIndex);
        originalRight = faceMesh.GetBlendShapeWeight(rightIndex);

        SetNextBlinkInterval();
    }

    private void SetNextBlinkInterval()
    {
        float min = Mathf.Max(0.1f, minBlinkInterval);
        float max = Mathf.Max(min, maxBlinkInterval);

        blinkInterval = Random.Range(min, max);
    }

    private void Update()
    {
        if (isBlinking)
            return;

        // Algorithm 4.3 - Step 11
        blinkTimer += Time.deltaTime;

        // Algorithm 4.3 - Step 12
        if (blinkTimer >= blinkInterval)
        {
            TriggerBlinkAnimation();

            // Algorithm 4.3 - Reset BlinkTimer
            blinkTimer = 0f;
        }
    }

    private void TriggerBlinkAnimation()
    {
        isBlinking = true;
        blinkProgress = 0f;
        NPCEmotionSessionManager.Instance?.RecordRealismEvent(
            GetComponentInParent<NPCEmotionController>(), "eye", "Blink", blink: true);

        Debug.Log($"[{name}] BlinkEvent triggered.");
    }

    private void LateUpdate()
    {
        if (!isBlinking)
            return;

        blinkProgress += Time.deltaTime;

        float duration = Mathf.Max(0.05f, blinkDuration);

        float progress = Mathf.Clamp01(
            blinkProgress / duration
        );

        // Smoothly close and reopen the eyes
        float closure = Mathf.Sin(progress * Mathf.PI);

        float leftWeight = Mathf.Lerp(
            originalLeft,
            blinkStrength,
            closure
        );

        float rightWeight = Mathf.Lerp(
            originalRight,
            blinkStrength,
            closure
        );

        faceMesh.SetBlendShapeWeight(
            leftIndex, leftWeight
        );

        faceMesh.SetBlendShapeWeight(
            rightIndex, rightWeight
        );

        if (progress >= 1f)
        {
            faceMesh.SetBlendShapeWeight(
                leftIndex, originalLeft
            );

            faceMesh.SetBlendShapeWeight(
                rightIndex, originalRight
            );

            isBlinking = false;
            SetNextBlinkInterval();
        }
    }

    private void OnDisable()
    {
        isBlinking = false;
        blinkProgress = 0f;
        blinkTimer = 0f;
        if (faceMesh == null)
            return;

        if (leftIndex >= 0)
            faceMesh.SetBlendShapeWeight(
                leftIndex, originalLeft
            );

        if (rightIndex >= 0)
            faceMesh.SetBlendShapeWeight(
                rightIndex, originalRight
            );
    }
}
