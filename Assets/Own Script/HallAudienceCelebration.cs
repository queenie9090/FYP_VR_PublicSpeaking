using System.Collections;
using UnityEngine;

// Hall-only event reactions. Both animators keep their existing seated base layer.
public sealed class HallAudienceCelebration : MonoBehaviour
{
    [SerializeField] private Animator audienceA, audienceB;
    [SerializeField] private AnimationClip clapClip, cheerClip;
    [SerializeField] private AudioClip applauseSound, cheerSound;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 0.7f;
    [SerializeField] private string layerName = "HallCelebration";
    [SerializeField, Range(0.05f, 1f)] private float blendSeconds = 0.3f;
    [SerializeField, Range(0f, 1f)] private float layerWeight = 1f;
    private Coroutine activeA, activeB;
    private Coroutine soundRoutine;
    private AudioSource applauseSource, cheerSource;
    [System.Serializable]
    private class ClapPose
    {
        public Vector3 chestOffset = new Vector3(0, -0.04f, 0.26f);
        [Range(0.015f, 0.06f)] public float contactSpacing = 0.032f;
        [Range(0.10f, 0.35f)] public float openSpacing = 0.22f;
        public Vector3 leftPalmTrim, rightPalmTrim;
    }
    [SerializeField] private ClapPose poseA = new ClapPose(), poseB = new ClapPose();
    [SerializeField, Range(1.5f, 2.5f)] private float clapsPerSecond = 2f;
    private bool clapA, clapB;
    private float clapStart;

    private void LateUpdate()
    {
        if (clapA) ApplyClap(audienceA, poseA);
        if (clapB) ApplyClap(audienceB, poseB);
    }

    private void ApplyClap(Animator rig, ClapPose pose)
    {
        if (!CanPlay(rig, clapClip, "Clap")) return;
        float weight = rig.GetLayerWeight(rig.GetLayerIndex(layerName));
        if (weight <= 0) return;
        Transform chest = rig.GetBoneTransform(HumanBodyBones.Chest) ?? rig.GetBoneTransform(HumanBodyBones.Spine);
        if (!chest) return;
        Transform body = rig.transform;
        Transform leftShoulder = rig.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rightShoulder = rig.GetBoneTransform(HumanBodyBones.RightUpperArm);
        if (!leftShoulder || !rightShoulder) return;
        Vector3 shoulders = (leftShoulder.position + rightShoulder.position) * 0.5f;
        Vector3 anchor = Vector3.Lerp(chest.position, shoulders, 0.72f);
        Vector3 centre = anchor + body.rotation * pose.chestOffset;
        // Brisk inward stroke, longer recovery. The right hand travels further.
        float phase = Mathf.Repeat((Time.time - clapStart) * clapsPerSecond, 1f);
        float stroke = phase < 0.38f ? 1f - phase / 0.38f : (phase - 0.38f) / 0.62f;
        float opening = Mathf.Sin(stroke * Mathf.PI * 0.5f);
        float travel = (pose.openSpacing - pose.contactSpacing) * opening;
        float halfContact = pose.contactSpacing * 0.5f;
        Vector3 left = centre - body.right * (halfContact + 0.36f * travel);
        Vector3 right = centre + body.right * (halfContact + 0.64f * travel)
            + body.up * (0.009f + 0.022f * opening);
        PoseArm(rig, true, left, pose.leftPalmTrim, weight);
        PoseArm(rig, false, right, pose.rightPalmTrim, weight);
    }

    private static void PoseArm(Animator rig, bool left, Vector3 palmTarget, Vector3 trim, float weight)
    {
        Transform upper = rig.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        Transform elbow = rig.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        Transform hand = rig.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        Transform index = rig.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
        Transform little = rig.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
        if (!upper || !elbow || !hand || !index || !little) return;
        Vector3 knuckles = (index.position + little.position) * 0.5f - hand.position;
        Vector3 normal = Vector3.Cross(knuckles, index.position - little.position) * (left ? -1 : 1);
        if (normal.sqrMagnitude < 1e-10f) return;
        Transform body = rig.transform;
        // Fingers point diagonally forward at different angles within the same contact plane.
        Quaternion desiredFrame = Quaternion.LookRotation(body.up + (left ? 1.35f : 0.75f) * body.forward,
            left ? body.right : -body.right);
        Quaternion turn = desiredFrame * Quaternion.Euler(trim) * Quaternion.Inverse(Quaternion.LookRotation(knuckles, normal));
        Quaternion wristRotation = Quaternion.Slerp(hand.rotation, turn * hand.rotation, weight);
        Vector3 wrist = Vector3.Lerp(hand.position, palmTarget - turn * (0.65f * knuckles), weight);
        Vector3 axis = (wrist - upper.position).normalized;
        float a = Vector3.Distance(upper.position, elbow.position), b = Vector3.Distance(elbow.position, hand.position);
        if (a < 0.001f || b < 0.001f || axis.sqrMagnitude < 0.5f) return;
        float d = Mathf.Clamp(Vector3.Distance(upper.position, wrist), Mathf.Abs(a-b) + 0.0001f, a+b-0.0001f);
        float along = (a*a - b*b + d*d) / (2*d);
        Vector3 bend = Vector3.ProjectOnPlane(elbow.position - upper.position, axis);
        if (bend.sqrMagnitude < 0.00001f) bend = Vector3.ProjectOnPlane(-body.up + (left ? -body.right : body.right), axis);
        Vector3 elbowOffset = axis * along + bend.normalized * Mathf.Sqrt(Mathf.Max(0, a*a - along*along));
        upper.rotation = Quaternion.FromToRotation(elbow.position-upper.position, elbowOffset) * upper.rotation;
        elbow.rotation = Quaternion.FromToRotation(hand.position-elbow.position, upper.position+axis*d-elbow.position) * elbow.rotation;
        hand.rotation = wristRotation;
    }

#if UNITY_EDITOR
    public void PreviewSingleClap(bool leftAudience)
    {
        StopReactions();
        clapStart = Time.time;
        clapA = leftAudience;
        clapB = !leftAudience;
        Animator rig = leftAudience ? audienceA : audienceB;
        if (!CanPlay(rig, clapClip, "Clap")) return;
        if (leftAudience) activeA = StartCoroutine(Play(rig, clapClip, "Clap"));
        else activeB = StartCoroutine(Play(rig, clapClip, "Clap"));
        soundRoutine = StartCoroutine(PlaySounds(true, false));
    }
#endif

    public void OnSessionStarted() { PlayBoth(false); }
    public void OnSessionCompleted() { PlayBoth(false); }
    public void OnJudgeHappy() { PlayBoth(true); }

    public void StopReactions()
    {
        clapA = clapB = false;
        if (activeA != null) StopCoroutine(activeA);
        if (activeB != null) StopCoroutine(activeB);
        activeA = activeB = null;
        StopSounds();
        ResetLayer(audienceA); ResetLayer(audienceB);
    }
    private void OnDisable() { StopReactions(); }
    private void PlayBoth(bool cheerForB)
    {
        if (!isActiveAndEnabled) return;
        clapStart = Time.time;
        clapA = true;
        clapB = !cheerForB;
        if (activeA != null) StopCoroutine(activeA);
        if (activeB != null) StopCoroutine(activeB);
        StopSounds();
        activeA = StartCoroutine(Play(audienceA, clapClip, "Clap"));
        activeB = StartCoroutine(Play(audienceB, cheerForB ? cheerClip : clapClip, cheerForB ? "Cheer" : "Clap"));
        bool clapPlaying = CanPlay(audienceA, clapClip, "Clap") ||
            (!cheerForB && CanPlay(audienceB, clapClip, "Clap"));
        bool cheerPlaying = cheerForB && CanPlay(audienceB, cheerClip, "Cheer");
        if (clapPlaying || cheerPlaying)
            soundRoutine = StartCoroutine(PlaySounds(clapPlaying, cheerPlaying));
    }
    private bool CanPlay(Animator animator, AnimationClip clip, string state)
    {
        if (animator == null || !animator.isActiveAndEnabled || !animator.isHuman || clip == null) return false;
        int layer = animator.GetLayerIndex(layerName);
        return layer >= 0 && animator.HasState(layer, Animator.StringToHash(layerName + "." + state));
    }
    private IEnumerator PlaySounds(bool clapPlaying, bool cheerPlaying)
    {
        if (clapPlaying && applauseSound != null)
        {
            if (applauseSource == null) applauseSource = CreateSoundSource();
            applauseSource.clip = applauseSound;
            applauseSource.volume = soundVolume;
            applauseSource.Play();
        }
        if (cheerPlaying && cheerSound != null)
        {
            if (cheerSource == null) cheerSource = CreateSoundSource();
            cheerSource.clip = cheerSound;
            cheerSource.volume = soundVolume;
            cheerSource.Play();
        }
        float clapRemaining = clapPlaying && clapClip != null ? clapClip.length : 0f;
        float cheerRemaining = cheerPlaying && cheerClip != null ? cheerClip.length : 0f;
        while (clapRemaining > 0f || cheerRemaining > 0f)
        {
            yield return null;
            clapRemaining -= Time.deltaTime;
            cheerRemaining -= Time.deltaTime;
            if (clapRemaining <= 0f && applauseSource != null && applauseSource.isPlaying) applauseSource.Stop();
            if (cheerRemaining <= 0f && cheerSource != null && cheerSource.isPlaying) cheerSource.Stop();
        }
        soundRoutine = null;
    }
    private AudioSource CreateSoundSource()
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        return source;
    }
    private void StopSounds()
    {
        if (soundRoutine != null) StopCoroutine(soundRoutine);
        soundRoutine = null;
        if (applauseSource != null) applauseSource.Stop();
        if (cheerSource != null) cheerSource.Stop();
    }
    private IEnumerator Play(Animator animator, AnimationClip clip, string state)
    {
        if (animator == null || !animator.isActiveAndEnabled || !animator.isHuman || clip == null) yield break;
        int layer = animator.GetLayerIndex(layerName);
        int hash = Animator.StringToHash(layerName + "." + state);
        if (layer < 0 || !animator.HasState(layer, hash)) { Debug.LogWarning("[Hall Celebration] Missing masked layer or state: " + state, animator); yield break; }
        float blend = Mathf.Max(0.05f, blendSeconds), elapsed = 0;
        animator.CrossFadeInFixedTime(hash, blend, layer, 0);
        float initial = animator.GetLayerWeight(layer);
        while (elapsed < blend) { elapsed += Time.deltaTime; animator.SetLayerWeight(layer, Mathf.Lerp(initial, layerWeight, Mathf.Clamp01(elapsed / blend))); yield return null; }
        animator.SetLayerWeight(layer, layerWeight);
        yield return new WaitForSeconds(Mathf.Max(0, clip.length - blend * 2));
        elapsed = 0;
        while (elapsed < blend) { elapsed += Time.deltaTime; animator.SetLayerWeight(layer, Mathf.Lerp(layerWeight, 0, Mathf.Clamp01(elapsed / blend))); yield return null; }
        animator.SetLayerWeight(layer, 0);
        if (animator == audienceA) clapA = false;
        if (animator == audienceB) clapB = false;
    }
    private void ResetLayer(Animator animator)
    {
        if (animator == null) return;
        int layer = animator.GetLayerIndex(layerName);
        if (layer >= 0) animator.SetLayerWeight(layer, 0);
    }
}
