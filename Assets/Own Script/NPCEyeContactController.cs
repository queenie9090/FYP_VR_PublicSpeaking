
using UnityEngine;

public class NPCEyeContactController : MonoBehaviour
{
    [Header("NPC Eyes")]
    [SerializeField] private SkinnedMeshRenderer eyeMesh;

    [Tooltip("Forward (+Z) must point out of the NPC's face.")]
    [SerializeField] private Transform gazeReference;

    [Header("VR Player")]
    [SerializeField] private Transform playerHead;

    [Header("NPC Emotion")]
    [SerializeField] private NPCEmotionController emotionController;

    [Header("Eye Movement Settings")]
    [Range(5f, 45f)]
    [SerializeField] private float maxHorizontalAngle = 25f;

    [Range(5f, 30f)]
    [SerializeField] private float maxVerticalAngle = 15f;

    [Range(0f, 100f)]
    [SerializeField] private float maxBlendShapeWeight = 70f;

    [Range(1f, 20f)]
    [SerializeField] private float movementSpeed = 8f;

    [Header("Direction Calibration")]
    [SerializeField] private bool invertHorizontal = false;
    [SerializeField] private bool invertVertical = false;

    [Header("Emotion-Based Gaze Settings")]
    [SerializeField] private bool enableEmotionGaze = true;

    [Range(0f, 20f)]
    [SerializeField] private float thinkingGazeAngle = 12f;

    [Range(0f, 20f)]
    [SerializeField] private float nervousGazeAngle = 9f;

    [Range(0f, 15f)]
    [SerializeField] private float sadGazeAngle = 8f;

    private int leftIn, leftOut, rightIn, rightOut;
    private int leftUp, rightUp, leftDown, rightDown;

    private int[] indices;
    private float[] originalWeights;

    // Emotion gaze event state
    private string previousEmotion = "";
    private float nextGazeTime;
    private float gazeStartTime;
    private float gazeDuration;

    private bool isGazeShifting = false;
    private NPCBodyActivity activity = NPCBodyActivity.IDLE;
    private float thinkingStarted;
    private bool thinkingShiftRecorded;

    // Scene code can reconnect a replacement tracked headset without recreating the NPC.
    public void SetPlayerHead(Transform trackedHead) => playerHead = trackedHead;

    public void SetActivity(NPCBodyActivity value)
    {
        if (activity == value) return;
        activity = value;
        thinkingStarted = Time.time;
        thinkingShiftRecorded = false;
        isGazeShifting = false;
        gazeOffset = Vector2.zero;
    }

    // X = horizontal angle, Y = vertical angle
    private Vector2 gazeOffset = Vector2.zero;

    private void Start()
    {
        if (eyeMesh == null ||
            eyeMesh.sharedMesh == null ||
            gazeReference == null)
        {
            Debug.LogError(
                $"[{name}] Eye Contact references are missing."
            );

            enabled = false;
            return;
        }

        if (playerHead == null)
            Debug.LogWarning($"[{name}] Assign the tracked headset to Player Head for eye contact.");

        Mesh mesh = eyeMesh.sharedMesh;

        leftIn = mesh.GetBlendShapeIndex("eyeLookInLeft");
        leftOut = mesh.GetBlendShapeIndex("eyeLookOutLeft");

        rightIn = mesh.GetBlendShapeIndex("eyeLookInRight");
        rightOut = mesh.GetBlendShapeIndex("eyeLookOutRight");

        leftUp = mesh.GetBlendShapeIndex("eyeLookUpLeft");
        rightUp = mesh.GetBlendShapeIndex("eyeLookUpRight");

        leftDown = mesh.GetBlendShapeIndex("eyeLookDownLeft");
        rightDown = mesh.GetBlendShapeIndex("eyeLookDownRight");

        indices = new int[]
        {
            leftIn, leftOut, rightIn, rightOut,
            leftUp, rightUp, leftDown, rightDown
        };

        originalWeights = new float[indices.Length];

        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] < 0)
            {
                Debug.LogError(
                    $"[{name}] Missing eye movement BlendShape."
                );

                enabled = false;
                return;
            }

        }
        for (int i = 0; i < indices.Length; i++)
            originalWeights[i] = eyeMesh.GetBlendShapeWeight(indices[i]);

        if (emotionController == null)
        {
            Debug.LogWarning(
                $"[{name}] Emotion Controller not assigned. " +
                "Normal Eye Contact will still work."
            );
        }

        ScheduleNextGaze();

        Debug.Log($"[{name}] Eye Contact initialized.");
    }

    private void LateUpdate()
    {
        if (eyeMesh == null || gazeReference == null)
            return;

        if (playerHead == null || !playerHead.gameObject.activeInHierarchy)
        {
            ReturnToNeutral();
            return;
        }

        // Step 1: Calculate normal eye contact direction.
        Vector3 direction =
            playerHead.position - gazeReference.position;

        Vector3 localDirection =
            gazeReference.InverseTransformDirection(
                direction.normalized
            );

        // Do not turn eyes backwards.
        if (localDirection.z <= 0f)
        {
            ReturnToNeutral();
            return;
        }

        float horizontalAngle = Mathf.Atan2(
            localDirection.x,
            localDirection.z
        ) * Mathf.Rad2Deg;

        float verticalAngle = Mathf.Atan2(
            localDirection.y,
            Mathf.Sqrt(
                localDirection.x * localDirection.x +
                localDirection.z * localDirection.z
            )
        ) * Mathf.Rad2Deg;

        // Step 2: Apply emotion-based gaze modifier.
        Vector2 emotionOffset = UpdateEmotionGaze();
        // A slow, small idle variation avoids a fixed stare without disturbing
        // the stronger thinking or emotion-driven gaze changes.
        if (activity == NPCBodyActivity.IDLE && emotionOffset.sqrMagnitude < 0.0001f)
            emotionOffset.x += 1.0f * Mathf.Sin(Time.time * 0.45f);

        horizontalAngle += emotionOffset.x;
        verticalAngle += emotionOffset.y;

        // Preserve the original calibration.
        if (invertHorizontal)
            horizontalAngle = -horizontalAngle;

        if (invertVertical)
            verticalAngle = -verticalAngle;

        float horizontal = Mathf.Clamp(
            horizontalAngle / maxHorizontalAngle,
            -1f, 1f
        );

        float vertical = Mathf.Clamp(
            verticalAngle / maxVerticalAngle,
            -1f, 1f
        );

        float lookRight = Mathf.Max(0f, horizontal);
        float lookLeft = Mathf.Max(0f, -horizontal);

        float lookUp = Mathf.Max(0f, vertical);
        float lookDown = Mathf.Max(0f, -vertical);

        // Preserve the working eye BlendShape mapping.
        SetWeight(leftIn, lookRight);
        SetWeight(rightOut, lookRight);

        SetWeight(leftOut, lookLeft);
        SetWeight(rightIn, lookLeft);

        SetWeight(leftUp, lookUp);
        SetWeight(rightUp, lookUp);

        SetWeight(leftDown, lookDown);
        SetWeight(rightDown, lookDown);
    }

    private Vector2 UpdateEmotionGaze()
    {
        if (activity == NPCBodyActivity.THINKING)
        {
            float elapsed = Time.time - thinkingStarted;
            if (elapsed >= 0.4f && elapsed < 1.8f)
            {
                if (!thinkingShiftRecorded)
                {
                    thinkingShiftRecorded = true;
                    NPCEmotionSessionManager.Instance?.RecordRealismEvent(
                        emotionController, "eye", "Thinking gaze shift");
                }
                float phase = (elapsed - 0.4f) / 1.4f;
                return new Vector2(4f, 1f) * Mathf.Sin(phase * Mathf.PI);
            }
            return Vector2.zero;
        }
        if (!enableEmotionGaze ||
            emotionController == null)
        {
            isGazeShifting = false;
            gazeOffset = Vector2.zero;
            previousEmotion = "";
            return Vector2.zero;
        }

        string emotion = emotionController.currentEmotion;

        if (string.IsNullOrWhiteSpace(emotion))
            emotion = "NEUTRAL";

        emotion = emotion.Trim().ToUpperInvariant();

        float intensity = Mathf.Clamp01(
            emotionController.emotionIntensity / 100f
        );

        // Detect emotion changes.
        if (emotion != previousEmotion)
        {
            previousEmotion = emotion;

            isGazeShifting = false;
            gazeOffset = Vector2.zero;

            ScheduleNextGaze();
        }

        // These emotions normally maintain direct eye contact.
        if (emotion == "HAPPY" ||
            emotion == "ANGRY" ||
            emotion == "NEUTRAL" ||
            intensity <= 0f)
        {
            isGazeShifting = false;
            return Vector2.zero;
        }

        // Start a short gaze shift when its timer expires.
        if (!isGazeShifting &&
            Time.time >= nextGazeTime)
        {
            StartEmotionGaze(emotion, intensity);
        }

        if (!isGazeShifting)
            return Vector2.zero;

        float progress = Mathf.Clamp01(
            (Time.time - gazeStartTime) / gazeDuration
        );

        if (progress >= 1f)
        {
            isGazeShifting = false;
            ScheduleNextGaze();
            return Vector2.zero;
        }

        // Smoothly move away, hold briefly, then return.
        float blend;

        if (progress < 0.25f)
        {
            blend = Mathf.SmoothStep(
                0f, 1f, progress / 0.25f
            );
        }
        else if (progress > 0.75f)
        {
            blend = Mathf.SmoothStep(
                1f, 0f, (progress - 0.75f) / 0.25f
            );
        }
        else
        {
            blend = 1f;
        }

        return gazeOffset * blend;
    }

    private void StartEmotionGaze(
        string emotion,
        float intensity)
    {
        isGazeShifting = true;
        gazeStartTime = Time.time;

        float direction = Random.value < 0.5f
            ? -1f
            : 1f;

        switch (emotion)
        {
            case "THINKING":
                gazeDuration = Random.Range(0.8f, 1.2f);

                gazeOffset = new Vector2(
                    direction * thinkingGazeAngle * intensity,
                    3f * intensity
                );
                break;

            case "NERVOUS":
                gazeDuration = Random.Range(0.35f, 0.65f);

                gazeOffset = new Vector2(
                    direction * nervousGazeAngle * intensity,
                    -2f * intensity
                );
                break;

            case "SAD":
                gazeDuration = Random.Range(0.8f, 1.3f);

                gazeOffset = new Vector2(
                    0f,
                    -sadGazeAngle * intensity
                );
                break;

            default:
                isGazeShifting = false;
                gazeOffset = Vector2.zero;
                ScheduleNextGaze();
                return;
        }

        Debug.Log(
            $"[{name}] Emotion gaze event: {emotion}"
        );
        NPCEmotionSessionManager.Instance?.RecordRealismEvent(
            emotionController, "eye", "Emotion gaze shift");
    }

    private void ScheduleNextGaze()
    {
        float delay = 5f;

        switch (previousEmotion)
        {
            case "THINKING":
                delay = Random.Range(3f, 6f);
                break;

            case "NERVOUS":
                delay = Random.Range(2f, 4f);
                break;

            case "SAD":
                delay = Random.Range(5f, 8f);
                break;

            default:
                delay = Random.Range(4f, 7f);
                break;
        }

        nextGazeTime = Time.time + delay;
    }

    private void SetWeight(int index, float amount)
    {
        if (index < 0)
            return;

        float target = Mathf.Clamp(
            amount * maxBlendShapeWeight,
            0f, 100f
        );

        float current =
            eyeMesh.GetBlendShapeWeight(index);

        float next = Mathf.Lerp(
            current,
            target,
            1f - Mathf.Exp(
                -movementSpeed * Time.deltaTime
            )
        );

        eyeMesh.SetBlendShapeWeight(index, next);
    }

    private void ReturnToNeutral()
    {
        if (indices == null)
            return;

        foreach (int index in indices)
        {
            SetWeight(index, 0f);
        }
    }

    private void OnDisable()
    {
        if (eyeMesh == null ||
            indices == null ||
            originalWeights == null)
            return;

        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] >= 0)
            {
                eyeMesh.SetBlendShapeWeight(
                    indices[i],
                    originalWeights[i]
                );
            }
        }
    }
}
