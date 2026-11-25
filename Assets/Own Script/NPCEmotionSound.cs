using UnityEngine;

[DefaultExecutionOrder(100), DisallowMultipleComponent]
public sealed class NPCEmotionSound : MonoBehaviour
{
    private NPCEmotionController owner;
    private InteractionManager interaction;
    private HallAudienceCelebration celebration;
    private Transform head;
    private AudioSource output;
    private string previous;
    private float cueVolume;

    private void Awake()
    {
        owner = GetComponent<NPCEmotionController>();
        if (!owner) { enabled = false; return; }
        foreach (var root in gameObject.scene.GetRootGameObjects())
        {
            if (!interaction) interaction = root.GetComponentInChildren<InteractionManager>(true);
            if (!celebration) celebration = root.GetComponentInChildren<HallAudienceCelebration>(true);
        }
        var rig = GetComponentInParent<Animator>() ?? GetComponentInChildren<Animator>();
        if (rig && rig.isHuman) head = rig.GetBoneTransform(HumanBodyBones.Head);
        var emitter = new GameObject("Emotion sound");
        emitter.transform.SetParent(transform, false);
        output = emitter.AddComponent<AudioSource>();
        output.playOnAwake = false;
        output.loop = false;
        output.spatialBlend = 1f;
        output.dopplerLevel = 0f;
        output.minDistance = 1.5f;
        output.maxDistance = 18f;
        output.priority = 180;
    }

    private void OnEnable() => previous = owner ? owner.currentEmotion : null;
    private void OnDisable() { if (output) output.Stop(); }
    private void OnDestroy() { if (output) Destroy(output.gameObject); }

    private void LateUpdate()
    {
        if (!output) return;
        if (!owner || !owner.isActiveAndEnabled)
        {
            if (output.isPlaying) output.Stop();
            previous = null;
            return;
        }

        output.transform.position = head ? head.position : owner.HeadMesh.bounds.center;
        string emotion = owner.currentEmotion;
        bool suppressHappy = emotion == "HAPPY" && celebration && celebration.IsCelebrationAudible;
        float duck = interaction && interaction.IsAnyNpcSpeechPlaying ? .32f : 1f;
        if (previous != emotion)
        {
            bool entering = previous != null;
            previous = emotion;
            output.Stop();
            if (!entering || suppressHappy || (emotion != "NEUTRAL" && owner.emotionIntensity <= 0)) return;

            string cue = emotion == "ANGRY" && owner.emotionIntensity >= 55 ? "ANGRY_Strong" : emotion;
            var clip = Resources.Load<AudioClip>("EmotionCues/" + cue);
            if (!clip) return;
            cueVolume = emotion == "NEUTRAL" ? .14f : Mathf.Lerp(.22f, .44f, owner.emotionIntensity / 100f);
            output.clip = clip;
            output.volume = cueVolume * duck;
            output.Play();
        }
        else if (output.isPlaying)
        {
            if (suppressHappy) output.Stop();
            else output.volume = Mathf.MoveTowards(output.volume, cueVolume * duck, 4f * Time.unscaledDeltaTime);
        }
    }
}
