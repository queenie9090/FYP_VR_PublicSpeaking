
using UnityEngine;
using System.Collections.Generic;

public class NPCEmotionController : MonoBehaviour
{
    [Header("NPC Face")]
    [SerializeField] private SkinnedMeshRenderer headMesh;

    [Header("Emotion Settings")]
    public string currentEmotion = "NEUTRAL";

    [Range(0, 100)]
    public int emotionIntensity = 0;

    [Range(1f, 10f)]
    public float transitionSpeed = 4f;

    private Dictionary<string, int> shapeIndices = new();
    private Dictionary<string, float> targets = new();

    private string[] expressionShapes =
    {
        "mouthSmileLeft", "mouthSmileRight",
        "cheekSquintLeft", "cheekSquintRight",
        "mouthFrownLeft", "mouthFrownRight",
        "browInnerUp", "browDownLeft", "browDownRight",
        "eyeSquintLeft", "eyeSquintRight",
        "mouthPressLeft", "mouthPressRight",
        "eyeWideLeft", "eyeWideRight",
        "mouthPucker"
    };

    void Start()
    {
        if (headMesh == null || headMesh.sharedMesh == null)
        {
            Debug.LogError("NPC Head_Mesh is not assigned!");
            enabled = false;
            return;
        }

        foreach (string shape in expressionShapes)
        {
            int index = headMesh.sharedMesh.GetBlendShapeIndex(shape);

            if (index >= 0)
            {
                shapeIndices[shape] = index;
                targets[shape] = 0f;
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
            Debug.LogWarning("Invalid emotion: " + emotion);
            return;
        }

        currentEmotion = emotion;
        emotionIntensity = emotion == "NEUTRAL"
            ? 0
            : Mathf.Clamp(intensity, 0, 100);

        foreach (string shape in expressionShapes)
        {
            if (targets.ContainsKey(shape))
                targets[shape] = 0f;
        }

        float strength = emotionIntensity / 100f;

        switch (emotion)
        {
            case "HAPPY":
                SetTarget("mouthSmileLeft", 80 * strength);
                SetTarget("mouthSmileRight", 80 * strength);
                SetTarget("cheekSquintLeft", 30 * strength);
                SetTarget("cheekSquintRight", 30 * strength);
                break;

            case "SAD":
                SetTarget("mouthFrownLeft", 65 * strength);
                SetTarget("mouthFrownRight", 65 * strength);
                SetTarget("browInnerUp", 45 * strength);
                break;

            case "ANGRY":
                SetTarget("browDownLeft", 75 * strength);
                SetTarget("browDownRight", 75 * strength);
                SetTarget("eyeSquintLeft", 35 * strength);
                SetTarget("eyeSquintRight", 35 * strength);
                break;

            case "NERVOUS":
                SetTarget("browInnerUp", 45 * strength);
                SetTarget("mouthPressLeft", 55 * strength);
                SetTarget("mouthPressRight", 55 * strength);
                SetTarget("eyeWideLeft", 25 * strength);
                SetTarget("eyeWideRight", 25 * strength);
                break;

            case "THINKING":
                SetTarget("browInnerUp", 25 * strength);
                SetTarget("mouthPucker", 20 * strength);
                break;
        }

        Debug.Log(
            $"[NPCEmotionController] {currentEmotion}, " +
            $"Intensity: {emotionIntensity}"
        );
    }

    private void SetTarget(string name, float value)
    {
        if (targets.ContainsKey(name))
            targets[name] = value;
    }

    void LateUpdate()
    {
        foreach (var item in targets)
        {
            int index = shapeIndices[item.Key];

            float current = headMesh.GetBlendShapeWeight(index);

            float next = Mathf.MoveTowards(
                current,
                item.Value,
                transitionSpeed * 100f * Time.deltaTime
            );

            headMesh.SetBlendShapeWeight(index, next);
        }
    }
}
