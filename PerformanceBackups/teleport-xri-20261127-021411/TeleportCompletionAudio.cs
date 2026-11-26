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
    private bool subscribed;

    private void OnEnable()
    {
        Bind();
    }

    private void Start()
    {
        if (!subscribed) Bind();
        if (!subscribed) Debug.LogWarning("[TeleportCompletionAudio] A unique teleportation provider was not found in this scene.", this);
    }

    private void Bind()
    {
        if (subscribed) return;
        if (provider == null)
        {
            foreach (var candidate in FindObjectsByType<TeleportationProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene != gameObject.scene) continue;
                if (provider != null) { provider = null; break; }
                provider = candidate;
            }
        }
        if (provider == null) return;
        if (source == null)
        {
            source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }
        provider.afterStepLocomotion += OnTeleportCompleted;
        subscribed = true;
    }

    private void OnDisable()
    {
        if (subscribed && provider != null) provider.afterStepLocomotion -= OnTeleportCompleted;
        subscribed = false;
    }

    private void OnTeleportCompleted(LocomotionProvider completedProvider)
    {
        if (completedProvider == provider && source != null && clip != null)
            source.PlayOneShot(clip, volume);
    }
}
