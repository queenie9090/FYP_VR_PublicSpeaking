using System.Collections;
using UnityEngine;

// Hall-only event reactions. All three animators keep their existing seated base layer.
public sealed partial class HallAudienceCelebration : MonoBehaviour
{
    [SerializeField] private Animator judge, audienceA, audienceB;
    [SerializeField] private AnimationClip clapClip, cheerClip;
    [SerializeField] private AudioClip applauseSound, cheerSound;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 0.7f;
    [SerializeField] private string layerName = "HallCelebration";
    [SerializeField, Range(0.05f, 1f)] private float blendSeconds = 0.3f;
    [SerializeField, Range(0f, 1f)] private float layerWeight = 1f;
    private Coroutine activeJudge, activeA, activeB;
    private Coroutine groupRoutine;
    private bool groupClapping, pendingHappy;
    private AudioSource applauseSource, cheerSource;
    public bool IsCelebrationAudible => (applauseSource && applauseSource.isPlaying && applauseSource.volume > 0f)
        || (cheerSource && cheerSource.isPlaying && cheerSource.volume > 0f);
    [System.Serializable]
    private class ClapPose
    {
        public Vector3 chestOffset = new Vector3(0, -0.04f, 0.26f);
        [Range(0.015f, 0.06f)] public float contactSpacing = 0.032f;
        [Range(0.10f, 0.35f)] public float openSpacing = 0.22f;
        public Vector3 leftPalmTrim, rightPalmTrim;
    }
    [SerializeField] private ClapPose poseJudge = new ClapPose(), poseA = new ClapPose(), poseB = new ClapPose();
    [SerializeField, Range(1.5f, 2.5f)] private float clapsPerSecond = 2f;
    private bool clapJudge, clapA, clapB;
    private float clapStart;

    private void LateUpdate()
    {
        UpdateAudiencePoses();
        if (clapJudge) ApplyClap(judge, poseJudge);
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
        PoseWrist(upper, elbow, hand, wrist, wristRotation, body, left);
    }

    internal static void PoseWrist(Transform upper, Transform elbow, Transform hand, Vector3 wrist,
        Quaternion wristRotation, Transform body, bool left)
    {
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
    public void PreviewAudienceClap() { PreviewSingleClap(true); clapB = true; activeB = StartCoroutine(Play(audienceB, clapClip, "Clap")); }
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
        // Solo editor previews are silent. Audible applause belongs to the group.
    }
#endif

    public void OnSessionStarted() => StartGroupClap(false);
    public void OnSessionCompleted() => StartGroupClap(false);
    public void OnJudgeHappy() => StartGroupClap(true);

    private void StartGroupClap(bool followWithHappy)
    {
        if (!isActiveAndEnabled) return;
        if (groupClapping) { pendingHappy = followWithHappy; return; }
        StopReactions();
        if (!CanPlay(judge, clapClip, "Clap") || !CanPlay(audienceA, clapClip, "Clap") ||
            !CanPlay(audienceB, clapClip, "Clap"))
        {
            Debug.LogWarning("[Hall Celebration] All three clap bindings are required for group applause.", this);
            return;
        }
        groupClapping = true;
        pendingHappy = followWithHappy;
        groupRoutine = StartCoroutine(GroupClap());
    }

    private IEnumerator GroupClap()
    {
        clapStart = Time.time;
        clapJudge = clapA = clapB = true;
        activeJudge = StartCoroutine(Play(judge, clapClip, "Clap", group: true));
        activeA = StartCoroutine(Play(audienceA, clapClip, "Clap", group: true));
        activeB = StartCoroutine(Play(audienceB, clapClip, "Clap", group: true));
        // Establish hand motion before starting the sound.
        yield return new WaitForSeconds(blendSeconds);
        if (applauseSound && soundVolume > 0)
        {
            if (!applauseSource) applauseSource = CreateSoundSource();
            applauseSource.clip = applauseSound;
            applauseSource.volume = soundVolume;
            applauseSource.Play();
            while (applauseSource.isPlaying) yield return null;
        }
        else yield return new WaitForSeconds(clapClip.length);
        groupClapping = false;
        while (clapJudge || clapA || clapB) yield return null;
        groupRoutine = null;
        if (pendingHappy)
        {
            pendingHappy = false;
            clapStart = Time.time;
            clapA = true;
            activeA = StartCoroutine(Play(audienceA, clapClip, "Clap"));
            if (CanPlay(audienceB, cheerClip, "Cheer"))
            {
                activeB = StartCoroutine(Play(audienceB, cheerClip, "Cheer"));
                if (cheerSound)
                {
                    if (!cheerSource) cheerSource = CreateSoundSource();
                    cheerSource.clip = cheerSound;
                    cheerSource.volume = soundVolume;
                    cheerSource.Play();
                }
            }
        }
    }

    public void StopReactions()
    {
        if (groupRoutine != null) StopCoroutine(groupRoutine);
        groupRoutine = null;
        groupClapping = pendingHappy = false;
        clapJudge = clapA = clapB = false;
        if (activeJudge != null) StopCoroutine(activeJudge);
        if (activeA != null) StopCoroutine(activeA);
        if (activeB != null) StopCoroutine(activeB);
        activeJudge = activeA = activeB = null;
        StopSounds();
        ResetLayer(judge); ResetLayer(audienceA); ResetLayer(audienceB);
    }
    private void OnDisable() { RestoreAudiencePoses(); StopReactions(); }
    private bool CanPlay(Animator animator, AnimationClip clip, string state)
    {
        if (animator == null || !animator.isActiveAndEnabled || !animator.isHuman || clip == null) return false;
        int layer = animator.GetLayerIndex(layerName);
        return layer >= 0 && animator.HasState(layer, Animator.StringToHash(layerName + "." + state));
    }
    private AudioSource CreateSoundSource()
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        SavedAudioLevels.Route(source, "SFX");
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        return source;
    }
    private void StopSounds()
    {
        if (applauseSource != null) applauseSource.Stop();
        if (cheerSource != null) cheerSource.Stop();
    }
    private IEnumerator Play(Animator animator, AnimationClip clip, string state, float maxDuration = float.PositiveInfinity, bool group = false)
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
        if (group) { while (groupClapping) yield return null; }
        else yield return new WaitForSeconds(Mathf.Max(0, Mathf.Min(clip.length, maxDuration) - blend * 2));
        elapsed = 0;
        while (elapsed < blend) { elapsed += Time.deltaTime; animator.SetLayerWeight(layer, Mathf.Lerp(layerWeight, 0, Mathf.Clamp01(elapsed / blend))); yield return null; }
        animator.SetLayerWeight(layer, 0);
        if (animator == judge) clapJudge = false;
        if (animator == audienceA) clapA = false;
        if (animator == audienceB)
        {
            clapB = false;
            if (state == "Cheer" && cheerSource) cheerSource.Stop();
        }
    }
    private void ResetLayer(Animator animator)
    {
        if (animator == null) return;
        int layer = animator.GetLayerIndex(layerName);
        if (layer >= 0) animator.SetLayerWeight(layer, 0);
    }
}
