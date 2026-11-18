using UnityEngine;

// This component owns the Cafeteria student's reaction layer and base-layer IK.
[RequireComponent(typeof(Animator), typeof(NPCEmotionController))]
public sealed class CafeteriaEmotionGestures : MonoBehaviour
{
    [SerializeField] private InteractionManager session;
    [Header("Chair movement")]
    [SerializeField, Range(.9f, 2.5f)] private float standUpSeconds = 1.45f;
    [SerializeField, Range(.9f, 2.5f)] private float sitDownSeconds = 1.7f;
    private float chairPhase;
    private bool feetAnchored;
    private PoseAnchor leftFoot, rightFoot;
    private Transform spine, leftThigh, rightThigh;
    private struct PoseAnchor
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    private Animator animator;
    private NPCEmotionController face;
    private Transform chest, shoulder, head, hand;
    private readonly FingerJoint[] pointingJoints = new FingerJoint[15];
    private Quaternion pointingHandFrame;
    private bool hasPointingPose;
    private struct FingerJoint
    {
        public Transform bone;
        public Quaternion target, animated, applied;
        public bool modified;
    }
    private readonly float[] weights = new float[6];
    private enum Pose { Neutral, Happy, Sad, Angry, Nervous, Thinking }
    private Pose pose;
    private float elapsed, nodTime, nodWeight;
    private bool sessionWasActive, suppressAfterExit;
    private static readonly int ChairHeight = Animator.StringToHash("ChairHeight");
    private static readonly int ChairState = Animator.StringToHash("Base Layer.Chair Posture");
    private static readonly int Cheer = Animator.StringToHash("CafeReaction.Cheer");
    private int reactionLayer;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        face = GetComponent<NPCEmotionController>();
        reactionLayer = animator.GetLayerIndex("CafeReaction");
        if (!animator.isHuman || reactionLayer < 1 || !animator.HasState(0, ChairState) || !animator.HasState(reactionLayer, Cheer))
        {
            Debug.LogWarning("Cafeteria reactions require the student Humanoid and CafeteriaStudentReactions controller.", this);
            enabled = false;
            return;
        }
        chest = Bone(HumanBodyBones.Chest) ?? Bone(HumanBodyBones.Spine);
        spine = Bone(HumanBodyBones.Spine);
        leftThigh = Bone(HumanBodyBones.LeftUpperLeg);
        rightThigh = Bone(HumanBodyBones.RightUpperLeg);
        shoulder = Bone(HumanBodyBones.RightUpperArm);
        hand = Bone(HumanBodyBones.RightHand);
        head = Bone(HumanBodyBones.Head);
        hasPointingPose = BuildPointingPose();
        if (!hasPointingPose) Debug.LogWarning("Café pointing needs a skinned right hand with mapped index and little fingers.", this);
    }

    private Transform Bone(HumanBodyBones bone) => animator.GetBoneTransform(bone);

    // Use the mesh bind pose so curls follow this hand's own joints and axes.
    private bool BuildPointingPose()
    {
        var index = Bone(HumanBodyBones.RightIndexProximal);
        var tip = Bone(HumanBodyBones.RightIndexDistal);
        var little = Bone(HumanBodyBones.RightLittleProximal);
        if (!hand || !index || !tip || !little) return false;
        foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!renderer.sharedMesh) continue;
            var bones = renderer.bones;
            var binds = renderer.sharedMesh.bindposes;
            if (binds.Length != bones.Length) continue;
            int wrist = System.Array.IndexOf(bones, hand);
            int first = System.Array.IndexOf(bones, index);
            int last = System.Array.IndexOf(bones, tip);
            int outer = System.Array.IndexOf(bones, little);
            if (wrist < 0 || first < 0 || last < 0 || outer < 0) continue;
            Vector3 baseIndex = binds[first].inverse.MultiplyPoint3x4(Vector3.zero);
            Vector3 along = (binds[last].inverse.MultiplyPoint3x4(Vector3.zero) - baseIndex).normalized;
            Vector3 across = (baseIndex - binds[outer].inverse.MultiplyPoint3x4(Vector3.zero)).normalized;
            Vector3 palm = Vector3.Cross(along, across).normalized;
            if (palm.sqrMagnitude < .9f) continue;
            pointingHandFrame = Quaternion.LookRotation(binds[wrist].MultiplyVector(along),
                binds[wrist].MultiplyVector(-palm));
            for (int j = 0; j < pointingJoints.Length; j++)
            {
                // Humanoid enum order: thumb, index, middle, ring, little; three joints each.
                var joint = Bone((HumanBodyBones)((int)HumanBodyBones.RightThumbProximal + j));
                if (!joint) continue;
                int child = System.Array.IndexOf(bones, joint);
                int parent = System.Array.IndexOf(bones, joint.parent);
                if (child < 0 || parent < 0) continue;
                Quaternion rest = (binds[parent] * binds[child].inverse).rotation;
                int digit = j / 3, segment = j % 3;
                float curl = digit == 1 ? 0f : segment == 0 ? 60f : segment == 1 ? 78f : 45f;
                Vector3 axis = Vector3.Cross(along, palm);
                if (digit == 0)
                {
                    // Sweep the thumb toward the curled fingers, leaving the index free.
                    axis = Vector3.Cross(across, along - across + palm);
                    curl = segment == 0 ? 32f : 22f;
                }
                var parentAxis = binds[parent].MultiplyVector(axis).normalized;
                pointingJoints[j] = new FingerJoint { bone = joint,
                    target = Quaternion.AngleAxis(curl, parentAxis) * rest };
            }
            return true;
        }
        return false;
    }

    private void RestoreFingers()
    {
        for (int j = 0; j < pointingJoints.Length; j++)
        {
            ref var joint = ref pointingJoints[j];
            if (!joint.modified) continue;
            if (Quaternion.Angle(joint.bone.localRotation, joint.applied) < .01f)
                joint.bone.localRotation = joint.animated;
            joint.modified = false;
        }
    }

    private void Update()
    {
        RestoreFingers();
        bool active = session != null && session.IsSessionActive;
        if (sessionWasActive && !active) suppressAfterExit = true;
        if (active || face.currentEmotion == "NEUTRAL") suppressAfterExit = false;
        sessionWasActive = active;
        Pose next = suppressAfterExit ? Pose.Neutral : ReadPose(face.currentEmotion);
        if (next != pose)
        {
            pose = next;
            elapsed = 0f;
            if (pose == Pose.Happy) animator.Play(Cheer, reactionLayer, 0f);
        }
        elapsed += Time.deltaTime;
        // Perform each reaction once on entry, including a seated recovery while the face retains its emotion.
        float duration = pose == Pose.Angry ? 3.8f : 3f;
        float strength = Mathf.Lerp(.65f, 1f, face.emotionIntensity / 100f);
        for (int i = 1; i < weights.Length; i++)
        {
            float target = i == (int)pose && elapsed < duration ? strength : 0f;
            weights[i] = Mathf.MoveTowards(weights[i], target, Time.deltaTime * 2.5f);
        }
        bool rise = pose == Pose.Angry && elapsed < duration;
        chairPhase = Mathf.MoveTowards(chairPhase, rise ? 1f : 0f,
            Time.deltaTime / Mathf.Max(.1f, rise ? standUpSeconds : sitDownSeconds));
        // Leave time to lean over the feet before extending the knees, and to settle after sitting.
        animator.SetFloat(ChairHeight, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.16f, .94f, chairPhase)));
        animator.SetLayerWeight(reactionLayer, Weight(Pose.Happy));
        bool listening = active && session.IsPlayerCapturing;
        nodWeight = Mathf.MoveTowards(nodWeight, listening ? 1f : 0f, Time.deltaTime * 4f);
        if (nodWeight > 0f) nodTime += Time.deltaTime;
        else nodTime = 0f;
    }

    private static Pose ReadPose(string label) => label switch
    {
        "HAPPY" => Pose.Happy, "SAD" => Pose.Sad, "ANGRY" => Pose.Angry,
        "NERVOUS" => Pose.Nervous, "THINKING" => Pose.Thinking, _ => Pose.Neutral
    };

    private float Weight(Pose value) => weights[(int)value];

    private float PointWeight => Weight(Pose.Angry) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, .94f, chairPhase));

    private PoseAnchor CaptureFoot(AvatarIKGoal goal) => new PoseAnchor
    {
        position = animator.GetIKPosition(goal),
        rotation = animator.GetIKRotation(goal)
    };

    private void SupportFoot(AvatarIKGoal goal, AvatarIKHint knee, Transform thigh, PoseAnchor foot, float weight)
    {
        animator.SetIKPositionWeight(goal, weight);
        animator.SetIKRotationWeight(goal, weight);
        animator.SetIKHintPositionWeight(knee, .6f * weight);
        if (weight <= 0f) return;
        animator.SetIKPosition(goal, foot.position);
        animator.SetIKRotation(goal, foot.rotation);
        animator.SetIKHintPosition(knee, (thigh.position + foot.position) * .5f
            + transform.forward * (.4f * animator.humanScale));
    }

    private void ApplyChairTransfer()
    {
        bool moving = chairPhase > 0f && chairPhase < 1f;
        if (moving && !feetAnchored)
        {
            leftFoot = CaptureFoot(AvatarIKGoal.LeftFoot);
            rightFoot = CaptureFoot(AvatarIKGoal.RightFoot);
        }
        feetAnchored = moving;
        float engage = Mathf.SmoothStep(0f, 1f, chairPhase / .16f);
        float release = Mathf.SmoothStep(0f, 1f, (1f - chairPhase) / .25f);
        float support = engage * release;
        SupportFoot(AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, leftThigh, leftFoot, support);
        SupportFoot(AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, rightThigh, rightFoot, support);
        if (!moving) return;

        float lean = engage * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.25f, .88f, chairPhase)));
        // Offset this frame's evaluated body pose. The scene root and chair stay fixed.
        animator.bodyPosition += transform.forward * (.065f * animator.humanScale * lean);
        if (spine != null)
        {
            Vector3 pitchAxis = spine.parent.InverseTransformDirection(transform.right);
            animator.SetBoneLocalRotation(HumanBodyBones.Spine,
                Quaternion.AngleAxis(13f * lean, pitchAxis) * spine.localRotation);
        }
    }

    private void OnAnimatorIK(int layer)
    {
        if (!enabled || layer != 0 || chest == null) return;
        ApplyChairTransfer();
        float sad = Weight(Pose.Sad), nervous = Weight(Pose.Nervous), thinking = Weight(Pose.Thinking);
        float angry = PointWeight;
        Vector3 forward = transform.forward, up = transform.up, right = transform.right;
        float scale = animator.humanScale;
        float cycle = nodTime % 3.4f;
        float nod = cycle < 2f ? .5f - .5f * Mathf.Cos(cycle * Mathf.PI * 2f) : 0f;
        float attention = Mathf.Clamp01(sad + nervous + nodWeight);
        animator.SetLookAtWeight(attention, .22f, .9f, 0f, .3f);
        animator.SetLookAtPosition(chest.position + scale * (forward + up *
            (.25f - .95f * sad - .32f * nod * nodWeight)
            + right * (.2f * nervous * Mathf.Sin(elapsed * 5f))));

        if (thinking > 0f && head != null)
        {
            Vector3 rollAxis = head.parent.InverseTransformDirection(forward);
            animator.SetBoneLocalRotation(HumanBodyBones.Head,
                Quaternion.AngleAxis(16f * thinking, rollAxis) * head.localRotation);
        }

        Vector3 origin = chest.position;
        Vector3 fidget = origin + scale * (forward * .24f - up * .16f);
        float shake = Mathf.Sin(elapsed * 13f) * .022f;
        Vector3 left = fidget - right * (.045f + shake) * scale;
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, nervous);
        animator.SetIKPosition(AvatarIKGoal.LeftHand, left);

        Vector3 aim = forward;
        Camera player = Camera.main;
        if (player != null) aim = Vector3.RotateTowards(forward,
            (player.transform.position - shoulder.position).normalized, 1.15f, 0f);
        Vector3 point = shoulder.position + aim * scale * .68f;
        float total = nervous + angry;
        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, Mathf.Clamp01(total));
        if (total > .001f)
            animator.SetIKPosition(AvatarIKGoal.RightHand,
                ((fidget + right * (.045f + shake) * scale) * nervous + point * angry) / total);

        if (angry > 0f && hasPointingPose)
        {
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, angry);
            animator.SetIKRotation(AvatarIKGoal.RightHand,
                Quaternion.LookRotation(aim, up) * Quaternion.Inverse(pointingHandFrame));
        }
        else animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
    }

    private void LateUpdate()
    {
        float point = PointWeight;
        if (point <= 0f || !hasPointingPose) return;
        for (int j = 0; j < pointingJoints.Length; j++)
        {
            ref var joint = ref pointingJoints[j];
            if (!joint.bone) continue;
            joint.animated = joint.bone.localRotation;
            joint.applied = Quaternion.Slerp(joint.animated, joint.target, point);
            joint.bone.localRotation = joint.applied;
            joint.modified = true;
        }
    }

    private void OnDisable()
    {
        RestoreFingers();
        if (animator == null || !animator.isActiveAndEnabled || reactionLayer < 1) return;
        animator.SetLayerWeight(reactionLayer, 0f);
        animator.SetFloat(ChairHeight, 0f);
        chairPhase = 0f;
        feetAnchored = false;
        pose = Pose.Neutral;
        System.Array.Clear(weights, 0, weights.Length);
        nodWeight = nodTime = 0f;
    }
}
