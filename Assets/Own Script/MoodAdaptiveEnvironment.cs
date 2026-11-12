using System;
using System.Collections.Generic;
using UnityEngine;

// Observes the same controller used by facial animation. Never writes session data.
public class MoodAdaptiveEnvironment : MonoBehaviour
{
    [Serializable]
    public class Mood
    {
        public string emotion;
        public Color tint = Color.white;
        [Range(0.8f, 1.15f)] public float brightness = 1f;
        [Range(0f, 1.2f)] public float ambientVolume = 1f;
        public AudioClip ambience;
        [Tooltip("Full state path on the optional masked body layer. Leave empty without a suitable clip.")]
        public string bodyState;

        public Mood(string label, Color color, float light, float volume)
        {
            emotion = label;
            tint = color;
            brightness = light;
            ambientVolume = volume;
        }
    }

    [SerializeField] private NPCEmotionController emotionSource;
    [Tooltip("Assign only lights intended to change with mood. Baked lights are skipped.")]
    [SerializeField] private Light[] lights = Array.Empty<Light>();
    [Tooltip("Dedicated looping ambience only. Never assign the NPC speech or UI effects source.")]
    [SerializeField] private AudioSource ambientSource;
    [SerializeField, Min(0.5f)] private float transitionSeconds = 3f;
    [SerializeField, Range(0f, 1f)] private float maximumEffectStrength = 0.7f;
    [Header("Optional body animation")]
    [Tooltip("Use an additional layer with an AvatarMask excluding face, eyes, jaw and root motion.")]
    [SerializeField] private Animator bodyAnimator;
    [SerializeField] private string bodyLayerName;
    [SerializeField] private Mood[] moods =
    {
        new Mood("HAPPY", new Color(1f, 0.96f, 0.88f), 1.12f, 1.1f),
        new Mood("SAD", new Color(0.88f, 0.94f, 1f), 0.88f, 0.55f),
        new Mood("ANGRY", new Color(1f, 0.9f, 0.87f), 0.9f, 0.5f),
        new Mood("NERVOUS", new Color(0.91f, 0.95f, 1f), 0.93f, 0.65f),
        new Mood("THINKING", new Color(0.96f, 0.98f, 1f), 1f, 0.6f),
        new Mood("NEUTRAL", Color.white, 1f, 1f)
    };

    private readonly List<Light> runtimeLights = new();
    private readonly List<Color> originalColors = new();
    private readonly List<float> originalIntensities = new();
    private AudioClip originalClip;
    private float originalVolume;
    private bool originallyPlaying;
    private int bodyLayer = -1;
    private float originalLayerWeight;
    private Mood activeMood;
    private string lastEmotion;
    private string activeBodyState;
    private bool initialized;

    private void OnEnable()
    {
        if (emotionSource == null) emotionSource = GetComponent<NPCEmotionController>();
        if (emotionSource == null)
        {
            Debug.LogError("[Mood Environment] Assign the conversation NPC emotion controller.", this);
            enabled = false;
            return;
        }
        runtimeLights.Clear();
        originalColors.Clear();
        originalIntensities.Clear();
        foreach (var light in lights)
        {
            if (light == null || runtimeLights.Contains(light)) continue;
            if (light.lightmapBakeType == LightmapBakeType.Baked)
            {
                Debug.LogWarning("[Mood Environment] Skipping baked light: " + light.name, light);
                continue;
            }
            runtimeLights.Add(light);
            originalColors.Add(light.color);
            originalIntensities.Add(light.intensity);
        }
        if (ambientSource != null)
        {
            originalClip = ambientSource.clip;
            originalVolume = ambientSource.volume;
            originallyPlaying = ambientSource.isPlaying;
        }
        bodyLayer = bodyAnimator != null && !string.IsNullOrWhiteSpace(bodyLayerName)
            ? bodyAnimator.GetLayerIndex(bodyLayerName) : -1;
        if (bodyLayer == 0)
        {
            Debug.LogWarning("[Mood Environment] Base animation layer cannot be overridden.", this);
            bodyLayer = -1;
        }
        if (bodyLayer > 0)
        {
            originalLayerWeight = bodyAnimator.GetLayerWeight(bodyLayer);
            if (originalLayerWeight > 0f)
            {
                Debug.LogWarning("[Mood Environment] Use a dedicated body layer with default weight zero.", this);
                bodyLayer = -1;
            }
        }
        lastEmotion = null;
        initialized = true;
    }

    private void Update()
    {
        if (!initialized || emotionSource == null) return;
        if (bodyLayer > 0 && bodyAnimator != null)
        {
            var owner = bodyAnimator.GetComponent<NPCBodyAnimation>();
            if (owner != null && owner.ControlsLayer(bodyAnimator, bodyLayer))
            {
                Debug.Log("[Mood Environment] Body animation selector owns the configured animation layer.", this);
                bodyLayer = -1;
            }
        }
        string emotion = emotionSource.currentEmotion;
        if (emotion != lastEmotion)
        {
            lastEmotion = emotion;
            activeMood = Array.Find(moods, mood => mood != null && mood.emotion == emotion);
            activeBodyState = null;
            if (bodyLayer > 0 && activeMood != null &&
                !string.IsNullOrWhiteSpace(activeMood.bodyState))
            {
                int hash = Animator.StringToHash(activeMood.bodyState);
                if (bodyAnimator.HasState(bodyLayer, hash))
                {
                    activeBodyState = activeMood.bodyState;
                    bodyAnimator.CrossFadeInFixedTime(hash, transitionSeconds, bodyLayer);
                }
                else Debug.LogWarning("[Mood Environment] Missing body state: " + activeMood.bodyState, this);
            }
        }
        float strength = activeMood == null || emotion == "NEUTRAL" ? 0f :
            Mathf.Clamp(emotionSource.emotionIntensity, 0, 100) / 100f *
            Mathf.Clamp01(maximumEffectStrength);
        float blend = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.5f, transitionSeconds));
        for (int i = 0; i < runtimeLights.Count; i++)
        {
            var light = runtimeLights[i];
            if (light == null) continue;
            Color tint = activeMood != null ? activeMood.tint : Color.white;
            float brightness = activeMood != null ? Mathf.Clamp(activeMood.brightness, 0.8f, 1.15f) : 1f;
            light.color = Color.Lerp(light.color,
                Color.Lerp(originalColors[i], originalColors[i] * tint, strength), blend);
            light.intensity = Mathf.Lerp(light.intensity,
                originalIntensities[i] * Mathf.Lerp(1f, brightness, strength), blend);
        }
        UpdateAmbience(strength);
        if (bodyLayer > 0)
            bodyAnimator.SetLayerWeight(bodyLayer, Mathf.Lerp(bodyAnimator.GetLayerWeight(bodyLayer),
                Mathf.Lerp(originalLayerWeight, activeBodyState != null ? 1f : originalLayerWeight, strength), blend));
    }

    private void UpdateAmbience(float strength)
    {
        if (ambientSource == null) return;
        AudioClip desiredClip = strength > 0f && activeMood != null && activeMood.ambience != null
            ? activeMood.ambience : originalClip;
        float multiplier = activeMood != null ? Mathf.Clamp(activeMood.ambientVolume, 0f, 1.2f) : 1f;
        float desiredVolume = originalVolume * Mathf.Lerp(1f, multiplier, strength);
        bool changingClip = ambientSource.clip != desiredClip;
        ambientSource.volume = Mathf.MoveTowards(ambientSource.volume,
            changingClip ? 0f : desiredVolume, Time.deltaTime / Mathf.Max(0.5f, transitionSeconds));
        // Fade to silence before replacing the clip. Rapid changes update this one target.
        if (changingClip && ambientSource.volume <= 0.001f)
        {
            ambientSource.Stop();
            ambientSource.clip = desiredClip;
            if (desiredClip != null && (originallyPlaying || desiredClip != originalClip))
                ambientSource.Play();
        }
    }

    private void OnDisable()
    {
        if (!initialized) return;
        for (int i = 0; i < runtimeLights.Count; i++)
            if (runtimeLights[i] != null)
            {
                runtimeLights[i].color = originalColors[i];
                runtimeLights[i].intensity = originalIntensities[i];
            }
        if (ambientSource != null)
        {
            bool clipChanged = ambientSource.clip != originalClip;
            if (clipChanged) ambientSource.Stop();
            ambientSource.clip = originalClip;
            ambientSource.volume = originalVolume;
            if (originallyPlaying && !ambientSource.isPlaying) ambientSource.Play();
            if (!originallyPlaying) ambientSource.Stop();
        }
        if (bodyLayer > 0 && bodyAnimator != null)
            bodyAnimator.SetLayerWeight(bodyLayer, originalLayerWeight);
        initialized = false;
    }
}
