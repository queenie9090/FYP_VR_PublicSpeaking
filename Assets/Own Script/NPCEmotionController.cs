
using UnityEngine;

public class NPCEmotionController : MonoBehaviour
{
    [Header("NPC Face")]
    [SerializeField]
    private SkinnedMeshRenderer headMesh;

    public string currentEmotion = "NEUTRAL";

    public void SetEmotion(string emotion, int intensity)
    {
        currentEmotion = emotion;

        Debug.Log(
            $"[NPC Emotion] {emotion}, Intensity: {intensity}"
        );
    }
}
