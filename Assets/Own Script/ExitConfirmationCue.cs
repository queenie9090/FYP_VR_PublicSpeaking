using System.Collections;
using UnityEngine;

/// <summary>Plays once whenever the confirmation panel becomes visible.</summary>
public sealed class ExitConfirmationCue : MonoBehaviour
{
    private AudioSource source;
    private AudioClip clip;
    private bool configured;
    private bool playedThisActivation;

    internal void Configure(AudioSource audioSource, AudioClip audioClip)
    {
        source = audioSource;
        clip = audioClip;
        // Prefer the existing dedicated source, preserving the button-click source.
        if (source != null && clip != null)
        {
            foreach (var candidate in source.GetComponents<AudioSource>())
                if (candidate != null && candidate.clip == clip) { source = candidate; break; }
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        }
        configured = true;
        if (isActiveAndEnabled && !playedThisActivation) PlayForActivation();
    }

    private void OnEnable()
    {
        playedThisActivation = false;
        if (configured) PlayForActivation();
    }

    private void OnDisable() { StopAllCoroutines(); }

    private void PlayForActivation()
    {
        if (playedThisActivation || source == null || clip == null) return;
        playedThisActivation = true;
        if (clip.loadState == AudioDataLoadState.Loaded) source.PlayOneShot(clip);
        else StartCoroutine(PlayWhenLoaded());
    }

    private IEnumerator PlayWhenLoaded()
    {
        if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        while (clip != null && clip.loadState == AudioDataLoadState.Loading) yield return null;
        if (isActiveAndEnabled && source != null && clip != null && clip.loadState == AudioDataLoadState.Loaded)
            source.PlayOneShot(clip);
        else
            Debug.LogWarning("[ExitConfirmationCue] Completion sound could not be loaded or played.", this);
    }
}
