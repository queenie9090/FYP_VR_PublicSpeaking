using UnityEngine;
using B = UnityEngine.HumanBodyBones;

public sealed partial class HallAudienceCelebration
{
    private AudiencePose poseForA, poseForB;
    private InteractionManager session;

    private void Start()
    {
        var roster = GetComponent<HallNPCRealismScreen>();
        if (!roster) return;
        session = GetComponent<InteractionManager>();
        poseForA = AudiencePose.Create(audienceA, roster.AudienceA, true, layerName);
        poseForB = AudiencePose.Create(audienceB, roster.AudienceB, false, layerName);
    }

    private void Update() => RestoreAudiencePoses();

    private void RestoreAudiencePoses()
    {
        poseForA?.Restore();
        poseForB?.Restore();
    }

    private void UpdateAudiencePoses()
    {
        bool listening = session && session.IsSessionActive && session.IsPlayerCapturing;
        if (poseForA != null && poseForA.Apply(listening) && CanPlay(audienceA, clapClip, "Clap"))
        {
            clapStart = Time.time;
            clapA = true;
            activeA = StartCoroutine(Play(audienceA, clapClip, "Clap", 1.6f));
        }
        if (poseForB != null && poseForB.Apply(listening) && CanPlay(audienceB, cheerClip, "Cheer"))
            activeB = StartCoroutine(Play(audienceB, cheerClip, "Cheer", 2.1f));
    }

    private sealed class AudiencePose
    {
        private readonly Animator rig;
        private readonly NPCEmotionController face;
        private readonly bool isA;
        private readonly int layer;
        private readonly Transform[] bones = new Transform[(int)B.LastBone];
        private readonly Quaternion[] before = new Quaternion[(int)B.LastBone], after = new Quaternion[(int)B.LastBone];
        private string emotion;
        private float age, weight, nodTime, restWeight;
        private bool applied;

        internal static AudiencePose Create(Animator rig, NPCEmotionController face, bool isA, string layerName)
        {
            return rig && rig.isHuman && face ? new AudiencePose(rig, face, isA, layerName) : null;
        }

        private AudiencePose(Animator rig, NPCEmotionController face, bool isA, string layerName)
        {
            this.rig = rig;
            this.face = face;
            this.isA = isA;
            layer = rig.GetLayerIndex(layerName);
            foreach (B id in new[] { B.Head, B.Chest, B.LeftShoulder, B.RightShoulder, B.LeftUpperArm,
                B.LeftLowerArm, B.LeftHand, B.RightUpperArm, B.RightLowerArm, B.RightHand, B.RightThumbProximal })
                bones[(int)id] = rig.GetBoneTransform(id);
        }

        internal void Restore()
        {
            if (!applied) return;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] && bones[i].localRotation == after[i])
                    bones[i].localRotation = before[i];
            applied = false;
        }

        // A true result requests a single HAPPY clip on this actor only.
        internal bool Apply(bool listening)
        {
            if (!rig || !rig.isActiveAndEnabled || !face || !face.isActiveAndEnabled) return false;
            bool celebrating = layer >= 0 && rig.GetLayerWeight(layer) > .0001f;
            bool changed = emotion != face.currentEmotion;
            if (celebrating)
            {
                emotion = face.currentEmotion;
                age = weight = nodTime = restWeight = 0f;
                return false;
            }
            if (changed && weight <= .001f)
            {
                emotion = face.currentEmotion;
                age = 0f;
                if (emotion == "HAPPY") return true;
            }
            age += Time.deltaTime;
            float target = emotion == face.currentEmotion && emotion != "NEUTRAL" && age < 3f
                ? Mathf.Lerp(.45f, 1f, face.emotionIntensity / 100f) : 0f;
            weight = Mathf.MoveTowards(weight, target, Time.deltaTime * 4f);
            float w = Mathf.SmoothStep(0, 1, weight);
            for (int i = 0; i < bones.Length; i++)
                if (bones[i]) before[i] = bones[i].localRotation;
            restWeight = Mathf.MoveTowards(restWeight, 1f, Time.deltaTime * 3f);
            Animate(w, listening);
            for (int i = 0; i < bones.Length; i++)
                if (bones[i]) after[i] = bones[i].localRotation;
            applied = true;
            return false;
        }

        private void Animate(float w, bool listening)
        {
            var body = rig.transform;
            var head = bones[(int)B.Head];
            var chest = bones[(int)B.Chest];
            if (!head || !chest) return;
            Vector3 up = body.up, forward = body.forward, right = body.right;
            float scale = rig.humanScale;
            Turn(chest, right, (isA ? 1f : -3f) + .65f * Mathf.Sin(Time.time * 1.5f));
            Vector3 left = Lap(true), hand = Lap(false);
            if (isA)
            {
                Vector3 middle = (left + hand) * .5f;
                left = middle - right * (.04f * scale);
                hand = middle + (right * .035f + up * .025f) * scale;
            }
            switch (emotion)
            {
                case "SAD":
                    Turn(head, right, (isA ? 16f : 7f) * w);
                    Turn(chest, right, (isA ? 4f : -6f) * w);
                    Turn(bones[(int)B.LeftShoulder], forward, 6f * w);
                    Turn(bones[(int)B.RightShoulder], forward, -6f * w);
                    break;
                case "ANGRY":
                    if (isA) Turn(head, up, 16f * Mathf.Sin(age * 5f) * Mathf.Clamp01(2f-age) * w);
                    else
                    {
                        Turn(chest, right, 7f * w);
                        hand = Vector3.Lerp(hand, chest.position + (right*.18f + forward*.32f - up*.02f)*scale, w);
                    }
                    break;
                case "THINKING":
                    Turn(head, forward, (isA ? -13f : 7f) * w);
                    if (isA) Turn(head, right, -9f * w);
                    else hand = Vector3.Lerp(hand, head.position + (right*.065f + forward*.105f - up*.075f)*scale, w);
                    break;
                case "NERVOUS":
                    Turn(head, up, (isA ? 13f : -5f) * w);
                    Turn(head, right, (isA ? 9f : 4f) * w);
                    Vector3 centre = chest.position + (forward*.20f - up*.12f)*scale;
                    left = Vector3.Lerp(left, centre - right*.055f*scale, w);
                    hand = Vector3.Lerp(hand, centre + (right*(isA ? .045f : .015f) + up*(.025f+.009f*Mathf.Sin(age*12f)))*scale, w);
                    break;
            }
            Reach(true, left);
            Reach(false, hand);
            OrientPalm(true, -up, forward, restWeight);
            OrientPalm(false, -up, forward, restWeight);
            if (!isA && emotion == "ANGRY") OrientPalm(false, up, forward, w);
            if (!isA && emotion == "THINKING") OrientPalm(false, -right, up, w);
            if (emotion == "NERVOUS") Turn(bones[(int)B.RightThumbProximal], forward, 12f*Mathf.Sin(age*12f)*w);
            if (listening || nodTime > 0) nodTime += Time.deltaTime;
            float period = isA ? 4.1f : 5.3f;
            float phase = Mathf.Repeat(nodTime, period) - (isA ? .35f : 1.1f);
            float cycle = isA ? .63f : .94f;
            bool nod = phase > 0 && phase < cycle*(isA ? 2 : 1);
            if (!listening && !nod) nodTime = 0;
            if (nod) Turn(head, right, (isA ? 7f : 9f)*Mathf.Pow(Mathf.Sin(Mathf.PI*phase/cycle),2f));
        }

        private Vector3 Lap(bool left)
        {
            var thigh = rig.GetBoneTransform(left ? B.LeftUpperLeg : B.RightUpperLeg);
            var knee = rig.GetBoneTransform(left ? B.LeftLowerLeg : B.RightLowerLeg);
            return Vector3.Lerp(thigh.position,knee.position,.58f) + rig.transform.up*(.095f*rig.humanScale);
        }

        private void Reach(bool left, Vector3 target)
        {
            var arm = bones[(int)(left ? B.LeftUpperArm : B.RightUpperArm)];
            var elbow = bones[(int)(left ? B.LeftLowerArm : B.RightLowerArm)];
            var hand = bones[(int)(left ? B.LeftHand : B.RightHand)];
            if (arm && elbow && hand) PoseWrist(arm, elbow, hand, Vector3.Lerp(hand.position, target, restWeight), hand.rotation, rig.transform, left);
        }

        private void OrientPalm(bool left, Vector3 normal, Vector3 along, float weight)
        {
            var hand = bones[(int)(left ? B.LeftHand : B.RightHand)];
            var index = rig.GetBoneTransform(left ? B.LeftIndexProximal : B.RightIndexProximal);
            var little = rig.GetBoneTransform(left ? B.LeftLittleProximal : B.RightLittleProximal);
            if (!hand || !index || !little) return;
            Vector3 fingers = (index.position + little.position)*.5f - hand.position;
            Vector3 palm = Vector3.Cross(fingers, index.position - little.position) * (left ? -1f : 1f);
            if (palm.sqrMagnitude < 1e-10f) return;
            Quaternion delta = Quaternion.LookRotation(along, normal) * Quaternion.Inverse(Quaternion.LookRotation(fingers, palm));
            hand.rotation = Quaternion.Slerp(Quaternion.identity, delta, weight) * hand.rotation;
        }

        private static void Turn(Transform bone, Vector3 axis, float degrees)
        {
            if (bone) bone.rotation = Quaternion.AngleAxis(degrees, axis)*bone.rotation;
        }
    }
}
