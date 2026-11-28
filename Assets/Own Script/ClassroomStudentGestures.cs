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
    private int seenReaction;
    private int recordedTurn;
    private string recordedActivity, recordedEmotion;

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
        if (seenReaction != face.ConversationReactionVersion)
        {
            seenReaction = face.ConversationReactionVersion;
            if (emotion == face.currentEmotion) age = 0f;
        }
        if (emotion != face.currentEmotion && weight <= .001f) { emotion = face.currentEmotion; age = 0; }
        age += dt;
        float intensity = Mathf.Clamp01(face.emotionIntensity / 100f);
        float hold = Mathf.Lerp(3.2f, 5f, intensity);
        float target = emotion == face.currentEmotion && emotion != "NEUTRAL" && (speaking || age < hold)
            ? Mathf.Lerp(.70f, 1f, intensity) : 0f;
        weight = Mathf.MoveTowards(weight, target, (target > weight ? 2.8f : 3.5f)*dt);
        settle = Mathf.MoveTowards(settle, 1f, 3f*dt);
        for (int i = 0; i < bones.Length; i++)
            if (bones[i]) original[i] = bones[i].localRotation;
        Pose(Mathf.SmoothStep(0, 1, weight));
        for (int i = 0; i < bones.Length; i++)
            if (bones[i]) posed[i] = bones[i].localRotation;
        applied = true;
        RecordGesture(speaking, listening);
    }

    private void RecordGesture(bool speaking, bool listening)
    {
        var history = NPCEmotionSessionManager.Instance;
        if (!session || !session.IsSessionActive || !history || !history.IsCollectingBodyEvents ||
            history.CurrentBehaviourTurn <= 0 || (!speaking && !listening && weight <= .01f))
        {
            recordedActivity = null;
            return;
        }
        string activity = speaking ? "SPEAKING" : listening ? "LISTENING" : "IDLE";
        int turn = history.CurrentBehaviourTurn;
        if (turn == recordedTurn && activity == recordedActivity && emotion == recordedEmotion) return;
        // Only log a pose after the rig actually received a visible bone offset.
        bool moved = false;
        for (int i = 0; i < bones.Length && !moved; i++)
            moved = bones[i] && Quaternion.Angle(original[i], posed[i]) > .5f;
        if (!moved) return;
        history.RecordBodyAnimation(new BodyAnimationEventRecord {
            animation_event_id = System.Guid.NewGuid().ToString(), npc_id = persona.characterName,
            npc_type = NPCBodyType.AMBIENT.ToString(), scene_type = gameObject.scene.name,
            npc_activity = activity, emotion = emotion, animation_name = "Seated student " + activity + " / " + emotion,
            timestamp = System.DateTime.UtcNow.ToString("o"), turnNumber = turn
        });
        recordedTurn = turn;
        recordedActivity = activity;
        recordedEmotion = emotion;
    }

    private void Pose(float w)
    {
        Transform chest = Bone(B.Chest) ? Bone(B.Chest) : Bone(B.Spine);
        Transform head = Bone(B.Head);
        Vector3 up = transform.up, forward = transform.forward, right = transform.right;
        float size = rig.humanScale;
        Vector3 left = Lap(true), hand = Lap(false);
        float pitch = (studentA ? 2 : -3) + .7f * Mathf.Sin(Time.time * 1.6f);
        float headPitch = 0, roll = 0, yaw = 0;
        float beat = Pulse(speechTime, studentA ? 2.5f : 2.8f, 1.35f)*speechWeight;
        if (emotion == "THINKING" || emotion == "SAD") beat *= 1f - w;
        pitch += beat * (studentA ? 3f : 5f);
        float nod = Pulse(listeningTime - (studentA ? .35f : 1.1f), studentA ? 4.1f : 5.3f, studentA ? 1.2f : .95f, studentA ? 2 : 1);
        Vector3 front = chest.position + forward*(.25f*size);
        hand = Vector3.Lerp(hand, front + (right*.12f-up*.045f)*size, beat*.85f);
        // One emphatic gesture on entry, followed by a quieter pose during longer replies.
        float accent = w * (1f - Mathf.SmoothStep(0f, 1f, (age - 2f) / 1.1f));
        switch (emotion)
        {
            case "HAPPY":
                pitch -= (studentA ? 6f : 9f)*w;
                headPitch += (studentA ? 9f : 19f)*accent*Pulse(age, 4f, 1.6f, 2);
                if (studentA) hand = Vector3.Lerp(hand, head.position + (right*.20f + forward*.21f - up*.12f)*size, accent);
                break;
            case "SAD":
                pitch += (studentA ? 11 : 16)*w;
                headPitch += (studentA ? 23 : 18)*w*Mathf.SmoothStep(0, 1, age/.8f);
                roll += (studentA ? 0 : 10)*w;
                Turn(B.LeftShoulder, forward, 13*w);
                Turn(B.RightShoulder, forward, -13*w);
                break;
            case "ANGRY":
                pitch += (studentA ? 13 : -15)*w;
                if (studentA) hand = Vector3.Lerp(hand, head.position + (right*.16f + forward*.32f - up*.19f)*size, accent);
                else {
                    yaw += 24*accent*Mathf.Sin(Mathf.Max(0, age-.35f)*6f);
                    hand = Vector3.Lerp(hand, front + (right*.17f + up*.02f)*size,
                        accent*Mathf.SmoothStep(0,1,(age-.35f)/.5f));
                }
                break;
            case "NERVOUS":
                headPitch += (studentA ? 10 : 6)*w;
                float rub = Mathf.Sin(age*8f + .6f*Mathf.Sin(age*3f));
                if (studentA)
                {
                    Vector3 fidget = head.position + (forward*.20f - up*.21f)*size;
                    left = Vector3.Lerp(left, fidget - right*.048f*size, w);
                    hand = Vector3.Lerp(hand, fidget + (right*.048f+up*.032f*rub)*size, w);
                    Turn(B.RightThumbProximal, forward, 12*w*Mathf.Sin(age*12));
                }
                else
                {
                    yaw += 23*w*Pulse(age, 3.2f, 1.6f);
                    Vector3 clasp = chest.position + (forward*.23f - up*.10f)*size;
                    left = Vector3.Lerp(left, clasp - right*.045f*size, w);
                    hand = Vector3.Lerp(hand, clasp + (right*.045f + forward*.028f*rub)*size, w);
                    Turn(B.LeftShoulder, forward, -8*w);
                    Turn(B.RightShoulder, forward, 8*w);
                }
                break;
            case "THINKING":
                roll += (studentA ? 16 : -21)*w;
                yaw += (studentA ? 11 : -16)*w;
                headPitch -= (studentA ? 9 : 14)*w;
                if (studentA) hand = Vector3.Lerp(hand, head.position + (right*.055f+forward*.12f-up*.14f)*size, w);
                break;
        }
        Turn(chest == Bone(B.Chest) ? B.Chest : B.Spine, right, pitch*settle);
        Turn(B.Head, right, headPitch + nod*listeningWeight*(studentA ? 7 : 9));
        Turn(B.Head, forward, roll);
        Turn(B.Head, up, yaw);
        Reach(true, left);
        Reach(false, hand);
        if (emotion == "ANGRY") AimPalm(up, forward, accent);
        if (studentA && emotion == "THINKING") AimPalm(-right, (up-right*.45f).normalized, w);
        if (studentA && emotion == "HAPPY") ThumbsUp(accent);
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
