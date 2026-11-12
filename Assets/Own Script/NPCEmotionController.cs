
using UnityEngine;
using System.Collections.Generic;

public class NPCEmotionController : MonoBehaviour
{
    [Header("NPC Face")]
    [SerializeField] private SkinnedMeshRenderer headMesh;
    public SkinnedMeshRenderer HeadMesh => headMesh;

    [Header("Emotion Settings")]
    public string currentEmotion = "NEUTRAL";

    [Range(0, 100)]
    public int emotionIntensity = 0;

    [Range(1f, 10f)]
    public float transitionSpeed = 4f;

    private readonly Dictionary<string, List<int>> shapeIndices = new();
    private readonly Dictionary<string, float> targets = new();
    private readonly Dictionary<int, float> baselineWeights = new();
    private bool expressionTransitionPending;

    // Logical blendshape names used by the existing Businessman.
    private readonly string[] expressionShapes =
    {
        "mouthSmileLeft", "mouthSmileRight",
        "cheekSquintLeft", "cheekSquintRight",
        "mouthFrownLeft", "mouthFrownRight",
        "browInnerUp",
        "browDownLeft", "browDownRight",
        "eyeSquintLeft", "eyeSquintRight",
        "mouthPressLeft", "mouthPressRight",
        "eyeWideLeft", "eyeWideRight",
        "mouthPucker"
    };

    // Alternative names for Business Girl (CC_Base).
    private readonly Dictionary<string, string[]> alternateNames = new()
    {
        { "mouthSmileLeft", new[] { "Mouth_Smile_L" } },
        { "mouthSmileRight", new[] { "Mouth_Smile_R" } },

        { "cheekSquintLeft", new[] { "Cheek_Raise_L" } },
        { "cheekSquintRight", new[] { "Cheek_Raise_R" } },

        { "mouthFrownLeft", new[] { "Mouth_Frown_L" } },
        { "mouthFrownRight", new[] { "Mouth_Frown_R" } },

        { "browInnerUp", new[]
            {
                "Brow_Raise_Inner_L",
                "Brow_Raise_Inner_R"
            }
        },

        { "browDownLeft", new[] { "Brow_Drop_L" } },
        { "browDownRight", new[] { "Brow_Drop_R" } },

        { "eyeSquintLeft", new[] { "Eye_Squint_L" } },
        { "eyeSquintRight", new[] { "Eye_Squint_R" } },

        { "mouthPressLeft", new[] { "Mouth_Press_L" } },
        { "mouthPressRight", new[] { "Mouth_Press_R" } },

        { "eyeWideLeft", new[] { "Eye_Wide_L" } },
        { "eyeWideRight", new[] { "Eye_Wide_R" } },

        { "mouthPucker", new[]
            {
                "Mouth_Pucker_Up_L",
                "Mouth_Pucker_Up_R",
                "Mouth_Pucker_Down_L",
                "Mouth_Pucker_Down_R"
            }
        }
    };

    private void Start()
    {
        if (headMesh == null || headMesh.sharedMesh == null)
        {
            Debug.LogError(
                $"[{name}] Facial SkinnedMeshRenderer missing!"
            );
            enabled = false;
            return;
        }

        foreach (string logicalName in expressionShapes)
        {
            List<int> indices = new();

            // First try the original Businessman name.
            int originalIndex =
                headMesh.sharedMesh.GetBlendShapeIndex(logicalName);

            if (originalIndex >= 0)
            {
                indices.Add(originalIndex);
            }
            else if (alternateNames.TryGetValue(
                logicalName, out string[] alternatives))
            {
                // Otherwise find matching Business Girl shapes.
                foreach (string alternate in alternatives)
                {
                    int index =
                        headMesh.sharedMesh.GetBlendShapeIndex(alternate);

                    if (index >= 0)
                        indices.Add(index);
                }
            }

            if (indices.Count > 0)
            {
                shapeIndices[logicalName] = indices;
                targets[logicalName] = 0f;
                foreach (int index in indices)
                    baselineWeights[index] = headMesh.GetBlendShapeWeight(index);
            }
            else
            {
                Debug.LogWarning(
                    $"[{name}] Missing BlendShape: {logicalName}"
                );
            }
        }

        SetEmotion(currentEmotion, emotionIntensity);
    }

    public void SetEmotion(string emotion, int intensity)
    {
        if (string.IsNullOrWhiteSpace(emotion))
            return;

        emotion = emotion.Trim().ToUpperInvariant();

        if (emotion != "HAPPY" &&
            emotion != "SAD" &&
            emotion != "ANGRY" &&
            emotion != "NERVOUS" &&
            emotion != "THINKING" &&
            emotion != "NEUTRAL")
        {
            Debug.LogWarning(
                $"[{name}] Invalid emotion: {emotion}"
            );
            return;
        }

        bool changed = currentEmotion != emotion || emotionIntensity !=
            (emotion == "NEUTRAL" ? 0 : Mathf.Clamp(intensity, 0, 100));
        currentEmotion = emotion;

        emotionIntensity = emotion == "NEUTRAL"
            ? 0
            : Mathf.Clamp(intensity, 0, 100);

        // Clear previous emotion targets.
        foreach (string shape in expressionShapes)
        {
            if (targets.ContainsKey(shape))
                targets[shape] = 0f;
        }

        float strength = emotionIntensity / 100f;

        switch (emotion)
        {
            case "HAPPY":
                SetTarget("mouthSmileLeft", 80f * strength);
                SetTarget("mouthSmileRight", 80f * strength);
                SetTarget("cheekSquintLeft", 30f * strength);
                SetTarget("cheekSquintRight", 30f * strength);
                break;

            case "SAD":
                SetTarget("mouthFrownLeft", 65f * strength);
                SetTarget("mouthFrownRight", 65f * strength);
                SetTarget("browInnerUp", 45f * strength);
                break;

            case "ANGRY":
                SetTarget("browDownLeft", 75f * strength);
                SetTarget("browDownRight", 75f * strength);
                SetTarget("eyeSquintLeft", 35f * strength);
                SetTarget("eyeSquintRight", 35f * strength);
                break;

            case "NERVOUS":
                SetTarget("browInnerUp", 45f * strength);
                SetTarget("mouthPressLeft", 55f * strength);
                SetTarget("mouthPressRight", 55f * strength);
                SetTarget("eyeWideLeft", 25f * strength);
                SetTarget("eyeWideRight", 25f * strength);
                break;

            case "THINKING":
                SetTarget("browInnerUp", 25f * strength);
                SetTarget("mouthPucker", 20f * strength);
                break;

                // NEUTRAL keeps all expression targets at zero.
        }

        if (changed) expressionTransitionPending = shapeIndices.Count > 0;

        Debug.Log(
            $"[{name}] Emotion: {currentEmotion}, " +
            $"Intensity: {emotionIntensity}"
        );
    }

    private void SetTarget(string shape, float value)
    {
        if (targets.ContainsKey(shape))
            targets[shape] = value;
    }

    private void LateUpdate()
    {
        if (headMesh == null)
            return;

        bool applied = false;
        foreach (var item in targets)
        {
            foreach (int index in shapeIndices[item.Key])
            {
                float current =
                    headMesh.GetBlendShapeWeight(index);

                float desired = Mathf.Clamp(baselineWeights[index] + item.Value, 0f, 100f);
                float next = Mathf.MoveTowards(
                    current,
                    desired,
                    transitionSpeed * 100f * Time.deltaTime
                );

                headMesh.SetBlendShapeWeight(index, next);
                if (Mathf.Abs(next - current) > 0.001f) applied = true;
            }
        }
        if (expressionTransitionPending && applied)
        {
            expressionTransitionPending = false;
            NPCEmotionSessionManager.Instance?.RecordRealismEvent(
                this, "face", "Expression blendshape transition started");
        }
    }
}
