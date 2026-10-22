using UnityEngine;

public class NPCPersona : MonoBehaviour
{
    [Header("NPC Identity")]
    public string characterName = "Interview Manager";

    [Header("Voice Configuration")]
    [Tooltip("Choose from OpenAI Voices: alloy, echo, fable, onyx, nova, shimmer")]
    public string voiceId = "nova";

    [Header("Audio Output")]
    public AudioSource npcAudioSource;

    private void Awake()
    {
        if (npcAudioSource == null)
        {
            npcAudioSource = GetComponent<AudioSource>();
        }
    }
}