using UnityEngine;
using B = UnityEngine.HumanBodyBones;

// Classroom scene instances only. Each student observes their own voice and emotion.
[DefaultExecutionOrder(60), DisallowMultipleComponent]
public sealed class ClassroomStudentGestures : MonoBehaviour
{
    [SerializeField] private InteractionManager session;
    [SerializeField] private NPCPersona classmate;
    [SerializeField] private bool studentA;
    private NPCPersona persona;
    private NPCEmotionController face;
    private Animator rig;
    private readonly Transform[] bones = new Transform[(int)B.LastBone];
    private readonly Quaternion[] original = new Quaternion[(int)B.LastBone], posed = new Quaternion[(int)B.LastBone];
    private string emotion = "NEUTRAL";
    private float weight, age, listeningTime, listeningWeight, speechTime, speechWeight, settle;
    private bool applied;

    private void Start()
    {
        persona = GetComponent<NPCPersona>();
        face = GetComponent<NPCEmotionController>();
        rig = GetComponent<Animator>();
        if (!rig || !rig.isHuman || !face || !persona) { enabled = false; return; }
        foreach (B id in System.Enum.GetValues(typeof(B)))
            if (id != B.LastBone) bones[(int)id] = rig.GetBoneTransform(id);
    }

    private Transform Bone(B id) => bones[(int)id];

    private void Update() => Restore();
    private void OnDisable() => Restore();

    private void Restore()
    {
        if (!applied) return;
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] && original[i] != posed[i] && bones[i].localRotation == posed[i])
                bones[i].localRotation = original[i];
        applied = false;
    }

    private void LateUpdate()
    {
        if (!rig.isActiveAndEnabled || !Bone(B.Head)) return;
        bool speaking = persona.npcAudioSource && persona.npcAudioSource.isPlaying;
        bool playerTalking = session && session.IsSessionActive && session.IsPlayerCapturing;
        bool listening = !speaking && (playerTalking || (classmate && classmate.npcAudioSource && classmate.npcAudioSource.isPlaying));
        float dt = Time.deltaTime;
        speechWeight = Mathf.MoveTowards(speechWeight, speaking ? 1 : 0, 5f*dt);
        listeningWeight = Mathf.MoveTowards(listeningWeight, listening ? 1 : 0, 5f*dt);
        speechTime = speechWeight > 0 ? speechTime + dt : 0;
        listeningTime = listeningWeight > 0 ? listeningTime + dt : 0;
        if (emotion != face.currentEmotion && weight <= .001f) { emotion = face.currentEmotion; age = 0; }
        age += dt;
        float target = emotion == face.currentEmotion && emotion != "NEUTRAL" && age < 3.2f
            ? Mathf.Sqrt(Mathf.Clamp01(face.emotionIntensity / 100f)) : 0f;
        weight = Mathf.MoveTowards(weight, target, 4f*dt);
        settle = Mathf.MoveTowards(settle, 1f, 3f*dt);
        for (int i = 0; i < bones.Length; i++)
            if (bones[i]) original[i] = bones[i].localRotation;
        Pose(Mathf.SmoothStep(0, 1, weight));
        for (int i = 0; i < bones.Length; i++)
            if (bones[i]) posed[i] = bones[i].localRotation;
        applied = true;
    }

    private void Pose(float w)
    {
        Transform chest = Bone(B.Chest) ? Bone(B.Chest) : Bone(B.Spine);
        Transform head = Bone(B.Head);
        Vector3 up = transform.up, forward = transform.forward, right = transform.right;
        float size = rig.humanScale;
        Vector3 left = Lap(true), hand = Lap(false);
        float pitch = studentA ? 2 : -3, headPitch = 0, roll = 0, yaw = 0;
        float beat = Pulse(speechTime, studentA ? 2.5f : 3.3f, .95f)*speechWeight;
        float nod = Pulse(listeningTime - (studentA ? .35f : 1.1f), studentA ? 4.1f : 5.3f, studentA ? 1.2f : .95f, studentA ? 2 : 1);
        Vector3 front = chest.position + forward*(.25f*size);
        hand = Vector3.Lerp(hand, front + (right*.12f-up*.045f)*size, beat*(studentA ? .8f : .35f));
        switch (emotion)
        {
            case "HAPPY":
                pitch += 5*w;
                if (studentA) hand = Vector3.Lerp(hand, head.position + (right*.14f + forward*.17f - up*.13f)*size, w);
                else headPitch += 12*w*Pulse(age, 4, 1.3f, 2);
                break;
            case "SAD":
                pitch += (studentA ? 4 : 8)*w;
                headPitch += (studentA ? 16 : 13)*w;
                roll += (studentA ? 0 : 10)*w;
                Turn(B.LeftShoulder, forward, 7*w);
                Turn(B.RightShoulder, forward, -7*w);
                break;
            case "ANGRY":
                pitch += (studentA ? 8 : -6)*w;
                if (studentA) hand = Vector3.Lerp(hand, head.position + (right*.16f + forward*.27f - up*.19f)*size, w);
                else yaw += 17*w*Mathf.Sin(age*6f)*Mathf.Clamp01(2-age);
                break;
            case "NERVOUS":
                headPitch += (studentA ? 9 : 4)*w;
                if (studentA)
                {
                    Vector3 fidget = head.position + (forward*.20f - up*.21f)*size;
                    left = Vector3.Lerp(left, fidget - right*.048f*size, w);
                    hand = Vector3.Lerp(hand, fidget + (right*.048f+up*.008f*Mathf.Sin(age*12))*size, w);
                    Turn(B.RightThumbProximal, forward, 12*w*Mathf.Sin(age*12));
                }
                else
                {
                    yaw += 18*w*Mathf.Clamp01(2-age);
                    Turn(B.LeftShoulder, forward, -8*w);
                    Turn(B.RightShoulder, forward, 8*w);
                }
                break;
            case "THINKING":
                roll += (studentA ? 7 : -14)*w;
                if (studentA) hand = Vector3.Lerp(hand, head.position + (right*.055f+forward*.12f-up*.14f)*size, w);
                else headPitch -= 8*w;
                break;
        }
        Turn(chest == Bone(B.Chest) ? B.Chest : B.Spine, right, pitch*settle);
        Turn(B.Head, right, headPitch + nod*listeningWeight*(studentA ? 7 : 9));
        Turn(B.Head, forward, roll);
        Turn(B.Head, up, yaw);
        Reach(true, left);
        Reach(false, hand);
        if (studentA && emotion == "ANGRY") AimPalm(up, forward, w);
        if (studentA && emotion == "THINKING") AimPalm(-right, (up-right*.45f).normalized, w);
        if (studentA && emotion == "HAPPY") ThumbsUp(w);
    }

    private static float Pulse(float time, float period, float duration, int cycles = 1)
    {
        if (time < 0) return 0;
        float phase = Mathf.Repeat(time, period)/duration;
        return phase < 1 ? Mathf.Pow(Mathf.Sin(phase*Mathf.PI*cycles), 2) : 0;
    }

    private void Turn(B bone, Vector3 axis, float degrees)
    {
        Transform t = Bone(bone);
        if (t) t.rotation = Quaternion.AngleAxis(degrees, axis)*t.rotation;
    }

    private Vector3 Lap(bool left)
    {
        var thigh = Bone(left ? B.LeftUpperLeg : B.RightUpperLeg);
        var knee = Bone(left ? B.LeftLowerLeg : B.RightLowerLeg);
        return Vector3.Lerp(thigh.position, knee.position, .56f) + transform.up*(.10f*rig.humanScale);
    }

    private void Reach(bool left, Vector3 target)
    {
        var hand = Bone(left ? B.LeftHand : B.RightHand);
        HallAudienceCelebration.PoseWrist(Bone(left ? B.LeftUpperArm : B.RightUpperArm),
            Bone(left ? B.LeftLowerArm : B.RightLowerArm), hand, Vector3.Lerp(hand.position,target,settle),
            hand.rotation, transform, left);
    }

    private void AimPalm(Vector3 normal, Vector3 along, float w)
    {
        var index = Bone(B.RightIndexProximal);
        var little = Bone(B.RightLittleProximal);
        if (!index || !little) return;
        var hand = Bone(B.RightHand);
        Vector3 fingers = (index.position + little.position)*.5f - hand.position;
        Vector3 palm = Vector3.Cross(fingers, index.position-little.position);
        if (palm.sqrMagnitude < 1e-10f) return;
        Quaternion delta = Quaternion.LookRotation(along,normal)*Quaternion.Inverse(Quaternion.LookRotation(fingers,palm));
        hand.rotation = Quaternion.Slerp(Quaternion.identity,delta,w)*hand.rotation;
    }

    private void ThumbsUp(float w)
    {
        var thumb = Bone(B.RightThumbProximal);
        var tip = Bone(B.RightThumbDistal);
        var index = Bone(B.RightIndexProximal);
        var little = Bone(B.RightLittleProximal);
        if (!thumb || !tip || !index || !little) return;
        var hand = Bone(B.RightHand);
        Vector3 across = (index.position - little.position).normalized;
        Vector3 palm = Vector3.Cross((index.position + little.position)*.5f - hand.position, across);
        Vector3 thumbAxis = tip.position - thumb.position;
        if (Vector3.Cross(thumbAxis.normalized,palm.normalized).sqrMagnitude < .001f) return;
        Quaternion frame = Quaternion.LookRotation(thumbAxis,palm);
        // Present the thumb in profile, with the pad of the fist facing the torso.
        Quaternion desired = Quaternion.LookRotation(transform.up, -transform.right);
        for (int i = (int)B.RightIndexProximal; i <= (int)B.RightLittleDistal; i++)
            Turn((B)i, -across, 60*w);
        hand.rotation = Quaternion.Slerp(Quaternion.identity,desired*Quaternion.Inverse(frame),w)*hand.rotation;
    }
}
