using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>Plays only after the teleport provider completes its queued locomotion.</summary>
public sealed class TeleportCompletionAudio : MonoBehaviour
{
    [SerializeField] private TeleportationProvider provider;
    [SerializeField] private AudioSource source;
    [SerializeField] private AudioClip clip;
    [SerializeField, Range(0f, 1f)] private float volume = 0.075f;

    private void OnEnable()
    {
        if (provider != null) provider.afterStepLocomotion += OnTeleportCompleted;
        else Debug.LogWarning("[TeleportCompletionAudio] Teleportation provider is missing.", this);
    }

    private void OnDisable()
    {
        if (provider != null) provider.afterStepLocomotion -= OnTeleportCompleted;
    }

    private void OnTeleportCompleted(LocomotionProvider completedProvider)
    {
        if (completedProvider == provider && source != null && clip != null)
            source.PlayOneShot(clip, volume);
    }
}
