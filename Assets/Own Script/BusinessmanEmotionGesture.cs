using System;
using UnityEngine;

// A short upper-body accent over the Businessman's current seated activity.
[DefaultExecutionOrder(50), DisallowMultipleComponent, RequireComponent(typeof(Animator), typeof(NPCEmotionController))]
public sealed class BusinessmanEmotionGesture : MonoBehaviour
{
    private Animator rig;
    private NPCBodyAnimation body;
    private float listen, think, speak, phase;
    private NPCEmotionController face;
    private int celebrationLayer;
    private Transform chest, head, leftShoulder, rightShoulder;
    private readonly Transform[] bones = new Transform[(int)HumanBodyBones.LastBone];
    private readonly Quaternion[] before = new Quaternion[(int)HumanBodyBones.LastBone];
    private readonly Quaternion[] after = new Quaternion[(int)HumanBodyBones.LastBone];
    private readonly Quaternion[] relaxedFingers = new Quaternion[15], curledFingers = new Quaternion[15];
    private Quaternion handFrame;
    private bool applied, hasHandFrame;
    private bool hallJudge;
    private int seenReaction;
    private string displayed = "NEUTRAL";
    private float elapsed, strength;
    private const float Duration = 2.4f;

    private void Awake()
    {
        rig = GetComponent<Animator>();
        hallJudge = gameObject.scene.name == "Hall";
        body = GetComponent<NPCBodyAnimation>();
        face = GetComponent<NPCEmotionController>();
        if (!rig.isHuman || !rig.runtimeAnimatorController) { enabled = false; return; }
        celebrationLayer = rig.GetLayerIndex("HallCelebration");
        foreach (HumanBodyBones bone in new[] { HumanBodyBones.Chest, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand })
            bones[(int)bone] = rig.GetBoneTransform(bone);
        chest = Bone(HumanBodyBones.Chest);
        head = Bone(HumanBodyBones.Head);
        leftShoulder = Bone(HumanBodyBones.LeftShoulder);
        rightShoulder = Bone(HumanBodyBones.RightShoulder);
        if (!chest || !head) { enabled = false; return; }
        for (int j = 0; j < 15; j++)
        {
            int id = (int)HumanBodyBones.RightThumbProximal + j;
            bones[id] = rig.GetBoneTransform((HumanBodyBones)id);
        }
        hasHandFrame = ReadRightHandBindPose();
    }

    private Transform Bone(HumanBodyBones id) => bones[(int)id];

    private bool ReadRightHandBindPose()
    {
        foreach (var mesh in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!mesh.sharedMesh) continue;
            var skin = mesh.bones;
            var bind = mesh.sharedMesh.bindposes;
            int wrist = Array.IndexOf(skin, Bone(HumanBodyBones.RightHand));
            int index = Array.IndexOf(skin, Bone(HumanBodyBones.RightIndexProximal));
            int tip = Array.IndexOf(skin, Bone(HumanBodyBones.RightIndexDistal));
            int little = Array.IndexOf(skin, Bone(HumanBodyBones.RightLittleProximal));
            if (bind.Length != skin.Length || Math.Min(Math.Min(wrist,index),Math.Min(tip,little)) < 0) continue;
            Vector3 along = (bind[tip].inverse.MultiplyPoint3x4(Vector3.zero) - bind[index].inverse.MultiplyPoint3x4(Vector3.zero)).normalized;
            Vector3 across = (bind[index].inverse.MultiplyPoint3x4(Vector3.zero) - bind[little].inverse.MultiplyPoint3x4(Vector3.zero)).normalized;
            Vector3 palm = Vector3.Cross(along, across).normalized;
            if (palm.sqrMagnitude < .9f) continue;
            handFrame = Quaternion.LookRotation(bind[wrist].MultiplyVector(along), bind[wrist].MultiplyVector(across));
            for (int j = 0; j < 15; j++)
            {
                Transform finger = bones[(int)HumanBodyBones.RightThumbProximal + j];
                int joint = finger ? Array.IndexOf(skin, finger) : -1;
                int parent = finger ? Array.IndexOf(skin, finger.parent) : -1;
                if (joint < 0 || parent < 0) return false;
                relaxedFingers[j] = (bind[parent] * bind[joint].inverse).rotation;
                Vector3 axis = j < 3 ? Vector3.Cross(across, along - across + palm) : Vector3.Cross(along, palm);
                float curl = j < 3 ? 25f : j % 3 == 2 ? 48f : 72f;
                curledFingers[j] = Quaternion.AngleAxis(curl, bind[parent].MultiplyVector(axis)) * relaxedFingers[j];
            }
            return true;
        }
        return false;
    }

    private void Update() => Restore();
    private void OnDisable() => Restore();

    private void Restore()
    {
        if (!applied) return;
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] && Quaternion.Angle(bones[i].localRotation, after[i]) < .01f)
                bones[i].localRotation = before[i];
        applied = false;
    }

    private void LateUpdate()
    {
        if (!rig.isActiveAndEnabled) return;
        float activityWeight = celebrationLayer >= 0 ? 1f - Mathf.Clamp01(rig.GetLayerWeight(celebrationLayer)) : 1f;
        if (seenReaction != face.ConversationReactionVersion)
        {
            seenReaction = face.ConversationReactionVersion;
            if (displayed == face.currentEmotion) elapsed = 0f;
        }
        // Fade the previous gesture before accepting the latest label, including a neutral reset.
        if (displayed != face.currentEmotion && strength <= .001f)
        {
            displayed = face.currentEmotion;
            elapsed = 0;
        }
        elapsed += Time.deltaTime;
        float intensity = Mathf.Clamp01(face.emotionIntensity / 100f);
        float duration = hallJudge ? Mathf.Lerp(3.4f, 5f, intensity) : Duration;
        float target = displayed != face.currentEmotion || elapsed >= duration ? 0f :
            Mathf.Lerp(hallJudge ? .74f : .45f, 1f, intensity);
        strength = Mathf.MoveTowards(strength, target, Time.deltaTime * 3.2f);
        bool breathing = displayed == "NEUTRAL";
        bool hasOverlay = body && body.isActiveAndEnabled && body.Overlay != NPCBodyAnimation.SeatedOverlay.None;
#if UNITY_EDITOR
        hasOverlay &= !body || !body.ManualTestActive;
#endif
        if (!breathing && strength <= .001f && !hasOverlay) return;
        for (int i = 0; i < bones.Length; i++)
            if (bones[i]) before[i] = bones[i].localRotation;
        float w = activityWeight * Mathf.SmoothStep(0f, 1f, strength);
        Vector3 up = transform.up, right = transform.right, forward = transform.forward;
        float scale = rig.humanScale;
        switch (displayed)
        {
            case "NEUTRAL":
                Rotate(chest, right, activityWeight * .65f * Mathf.Sin(Time.time * 1.65f));
                break;
            case "THINKING":
                Rotate(head, forward, -14f * w);
                Rotate(head, right, -8f * w);
                if (hallJudge)
                {
                    Rotate(head, up, 15f * w);
                    Rotate(chest, right, 6f * w);
                    Reach(false, chest.position + scale * (.14f * right + .23f * forward), w);
                }
                break;
            case "SAD":
                Rotate(chest, right, (hallJudge ? 13f : 7f) * w);
                Rotate(head, right, (hallJudge ? 20f : 15f) * w);
                Rotate(leftShoulder, forward, 8f * w);
                Rotate(rightShoulder, forward, -8f * w);
                RestOnThigh(true,w); RestOnThigh(false,w);
                break;
            case "NERVOUS":
                Vector3 middle = chest.position + scale * (.25f * forward - .08f * up);
                if (hallJudge)
                {
                    Rotate(leftShoulder, forward, -8f * w);
                    Rotate(rightShoulder, forward, 8f * w);
                    middle += up * (.035f * scale * Mathf.Sin(elapsed * 5f));
                }
                Reach(true, middle - right * (.044f * scale), w);
                Reach(false, middle + scale * (.043f * right + (.015f + .009f * Mathf.Sin(elapsed * 13f)) * up), w);
                if (hasHandFrame) ShapeFingers(w * .55f, -1, .55f + .35f * Mathf.Sin(elapsed * 13f));
                break;
            case "HAPPY":
            case "ANGRY":
                bool happy = displayed == "HAPPY";
                Vector3 aim = forward;
                if (!happy && Camera.main)
                    aim = Vector3.RotateTowards(forward,(Camera.main.transform.position - chest.position).normalized,.65f,0f);
                if (!happy) Rotate(chest, right, (hallJudge ? 14f : 6f) * w);
                Vector3 wrist = chest.position + scale * ((happy ? .17f : .12f) * right +
                    (happy ? .12f : .09f) * up + (happy ? .24f : .47f) * aim);
                Reach(false, wrist, w);
                if (hasHandFrame)
                {
                    var hand = Bone(HumanBodyBones.RightHand);
                    var rotation = Quaternion.LookRotation(happy ? -right : aim, up) * Quaternion.Inverse(handFrame);
                    hand.rotation = Quaternion.Slerp(hand.rotation, rotation, w);
                    ShapeFingers(w, happy ? 0 : 1, 1f);
                    if (happy)
                    {
                        Vector3 thumb = Bone(HumanBodyBones.RightThumbDistal).position -
                                        Bone(HumanBodyBones.RightThumbProximal).position;
                        if (thumb.sqrMagnitude > .000001f)
                            hand.rotation = Quaternion.Slerp(Quaternion.identity,
                                Quaternion.FromToRotation(thumb, up), w) * hand.rotation;
                    }
                }
                break;
        }
        if (hasOverlay) ApplyConversationPose(activityWeight * (breathing ? 1f : 1f - .8f * w));
        for (int i = 0; i < bones.Length; i++)
            if (bones[i]) after[i] = bones[i].localRotation;
        applied = true;
    }

    private void ApplyConversationPose(float weight)
    {
        var activity = body.Activity;
        bool all = body.Overlay == NPCBodyAnimation.SeatedOverlay.AllConversationActivities;
        float step = Time.deltaTime * 3f;
        listen = Mathf.MoveTowards(listen, all && activity == NPCBodyActivity.LISTENING ? 1f : 0f, step);
        think = Mathf.MoveTowards(think, all && activity == NPCBodyActivity.THINKING ? 1f : 0f, step);
        speak = Mathf.MoveTowards(speak, activity == NPCBodyActivity.SPEAKING ? 1f : 0f, step);
        if (listen + think + speak == 0f) { phase = 0; return; }
        phase += Time.deltaTime;
        float beat = .5f + .5f * Mathf.Sin(phase * 4.4f + .7f * Mathf.Sin(phase * 1.3f));
        float reply = .5f + .5f * Mathf.Sin(phase * 3.2f + 1.4f);
        float nod = Mathf.Repeat(phase, 2.8f);
        nod = nod < 2f ? Mathf.Pow(Mathf.Sin(Mathf.PI * nod), 2f) : 0f;
        Vector3 right = transform.right;
        Rotate(head, right, weight * (8f * listen * nod - 5f * think));
        Rotate(head, transform.forward, weight * think * 8f);
        Rotate(chest, right, weight * (listen * 2f + speak * (1f + 3f * beat)));
        // Lift relative to the animated rest pose, keeping the elbows near the body.
        Rotate(Bone(HumanBodyBones.RightLowerArm), -right, weight * (think * 8f + speak * (5f + 14f * beat)));
        Rotate(Bone(HumanBodyBones.LeftLowerArm), -right, weight * speak * (3f + 9f * reply));
        if (weight > .1f && activity != NPCBodyActivity.IDLE) body.RecordOverlayMotion();
    }

    private static void Rotate(Transform bone, Vector3 axis, float degrees)
    {
        if (bone) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
    }

    private void ShapeFingers(float weight, int extendedDigit, float thumbCurl)
    {
        for (int j = 0; j < 15; j++)
        {
            var finger = bones[(int)HumanBodyBones.RightThumbProximal + j];
            Quaternion target = j / 3 == extendedDigit ? relaxedFingers[j] :
                j < 3 ? Quaternion.Slerp(relaxedFingers[j], curledFingers[j], thumbCurl) : curledFingers[j];
            finger.localRotation = Quaternion.Slerp(finger.localRotation, target, weight);
        }
    }

    private void RestOnThigh(bool left, float weight)
    {
        var hip = rig.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
        var knee = rig.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
        if (hip && knee) Reach(left, Vector3.Lerp(hip.position, knee.position, .57f) + transform.up * (.09f * rig.humanScale), weight);
    }

    private void Reach(bool left, Vector3 position, float weight)
    {
        var upper = Bone(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        var elbow = Bone(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var hand = Bone(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        if (upper && elbow && hand)
            HallAudienceCelebration.PoseWrist(upper, elbow, hand, Vector3.Lerp(hand.position, position, weight),
                hand.rotation, transform, left);
    }
}
