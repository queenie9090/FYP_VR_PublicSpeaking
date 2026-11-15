using System;
using System.Collections.Generic;
using UnityEngine;

public enum NPCBodyType { INTERVIEWER, AMBIENT }
public enum NPCBodyActivity { IDLE, LISTENING, THINKING, SPEAKING, UsePhone, Talk, Selfie, Drink, Cheer, Clap }

[Serializable]
public class BodyAnimationEventRecord
{
    public string animation_event_id;
    public string npc_id;
    public string npc_type;
    public string scene_type;
    public string npc_activity;
    public string emotion;
    public string animation_name;
    public string timestamp;
    public int turnNumber;
}

// One owner for an optional masked body layer. Facial controllers remain independent.
[DisallowMultipleComponent]
public class NPCBodyAnimation : MonoBehaviour
{
    [Serializable]
    public class ActionBinding
    {
        public NPCBodyActivity activity;
        [Tooltip("Empty matches any validated emotion.")]
        public string emotion;
        [Tooltip("Empty matches any scene. Use HALL, CLASSROOM, KOPITIAM or OTHER.")]
        public string scene;
        [Tooltip("Optional external event name. Event bindings must use non-looping states.")]
        public string environmentEvent;
        [Tooltip("Exact full Animator state path on the configured body layer.")]
        public string state;
        public bool oneShot;
    }

    [SerializeField] private string npcId;
    [SerializeField] private NPCBodyType npcType;
    [SerializeField] private Animator animator;
    [Tooltip("Optional scene-specific controller assigned before body animation initializes.")]
    [SerializeField] private RuntimeAnimatorController sceneControllerOverride;
    [SerializeField] private NPCEmotionController emotionSource;
    [Tooltip("Dedicated layer, default weight zero. Mask out head, face, eyes, jaw and root.")]
    [SerializeField] private string layerName;
    [SerializeField, Min(0.1f)] private float blendSeconds = 0.5f;
    [SerializeField, Min(3f)] private float variationInterval = 8f;
    [SerializeField, Range(0.1f, 1f)] private float layerWeight = 0.8f;
    [Tooltip("Optional seated forearm gestures. Requires IK Pass on the configured body layer.")]
    [SerializeField] private bool seatedActivityGestures;
    [SerializeField, Range(0f, 10f)] private float seatedGestureDegrees = 5f;
    [Tooltip("Optional speaking forearm motion; zero leaves existing NPCs unchanged.")]
    [SerializeField, Range(0f, 5f)] private float seatedSpeakingDegrees;
    [Header("Optional Seated Conversation Gestures")]
    [SerializeField] private bool useSeatedConversationGestures;
    [SerializeField, Range(0f, 8f)] private float listeningGestureDegrees = 3.5f;
    [SerializeField, Range(0f, 8f)] private float thinkingGestureDegrees = 5f;
    [SerializeField, Range(0f, 8f)] private float conversationSpeakingDegrees = 4.5f;
    [SerializeField, Range(0.5f, 3f)] private float gestureTransitionSeconds = 0.8f;
    [SerializeField, Range(0.5f, 2f)] private float conversationGestureSpeed = 1f;
    [Header("Interview Activity Gestures")]
    [SerializeField] private bool interviewActivityGestures;
    [Tooltip("Enable Tutorial's SittingTalking clip in real sessions only after checking desk clearance.")]
    [SerializeField] private bool enableTutorialSpeakingInInterview;
    [Tooltip("Enable the unverified seated pointing clip after checking its direction, finger pose and desk clearance.")]
    [SerializeField] private bool enableInterviewAngryPointing;
    [Tooltip("Gesture-layer strength for the Interview pointing clip; check desk clearance before use.")]
    [SerializeField, Range(0f, 1f)] private float interviewPointingLayerWeight = 1f;
    [Header("Listening Nod")]
    [SerializeField, Range(0f, 10f)] private float listeningNodDegrees = 4f;
    [Tooltip("Complete nod cycles per second during a nodding sequence.")]
    [SerializeField, Range(0.2f, 1.5f)] private float listeningNodFrequency = 0.9f;
    [Tooltip("Rest time between two-nod sequences, in seconds.")]
    [SerializeField, Range(0.1f, 3f)] private float listeningNodPause = 0.4f;
    [SerializeField] private ActionBinding[] actions = Array.Empty<ActionBinding>();
    private readonly List<ActionBinding> validActions = new();
    private readonly List<ActionBinding> candidates = new();
    private NPCBodyActivity activity;
    private int turnNumber;
    private int layer = -1;
    private ActionBinding selected;
    private ActionBinding lastAction;
    private string pendingEvent;
    private string lastContext;
    private float nextSelection;
    private float startedAt;
    private bool observedStart;
    private bool failedToStart;
    private string recordedActivity;
    private int selectedTurn;
    private float oneShotCooldownUntil;
    private float seatedGestureWeight;
    private float speakingGestureWeight;
    private float speakingStartedAt;
    private float activityStartedAt;
    private bool seatedGestureRecorded;
    private Transform leftForearm, rightForearm;
    private float conversationRightAngle, conversationLeftAngle;
    private Transform nodHead;
    private float nodWeight;
    private float nodStartTime;
    private Quaternion nodBaseRotation, nodAppliedRotation;
    private bool nodApplied;
    private const string InterviewPointingEvent = "InterviewAngryPointing";
    private ActionBinding interviewPointingBinding;
    private string lastPointingEmotion;

#if UNITY_EDITOR
    [Header("Editor Diagnostics")]
    [Tooltip("Log activity and up to two sampled bone results per activity. Editor only.")]
    [SerializeField] private bool diagnoseSeatedGestures;
    [Header("Interview Pose Preview (Editor Only)")]
    [Tooltip("Apply adjustable posture and hand targets only during manual keyboard testing.")]
    [SerializeField] private bool previewSeatedPoseCorrections;
    [Tooltip("Total forward torso pitch for Editor preview; compare candidates visually.")]
    [SerializeField, Range(0f, 10f)] private float previewUprightDegrees = 4f;
    [Tooltip("Fraction of the total correction applied to Chest; the remainder goes to Spine.")]
    [SerializeField, Range(0f, 1f)] private float previewChestShare = 0.4f;
    [Tooltip("During manual preview, sample the Speaking clip and match that torso pose in other activities.")]
    [SerializeField] private bool previewMatchSpeakingTorso = true;
    [Tooltip("Use a fixed frame of the Speaking clip as the upper-body rest pose during manual preview.")]
    [SerializeField] private bool previewSpeakingRestPose = true;
    [Tooltip("Lift animated hand targets in world-space metres; this does not detect the desk.")]
    [SerializeField, Range(0f, 0.08f)] private float previewHandLiftMetres = 0.03f;
    private float previewPoseWeight;
    private bool previewHandIkApplied;
    private bool previewSpeakingTorsoCaptured;
    private Quaternion previewSpeakingSpine, previewSpeakingChest;
    private float previewTorsoMatchWeight;
    private int diagnosticSamples;
    private float nextDiagnosticTime;
    private int diagnosticIkFrame = -1;
    private Quaternion diagnosticBefore;
    private float diagnosticRequestedAngle;
    private RuntimeAnimatorController diagnosticLastController;
    private bool manualTestActive;
    private ActionBinding manualPreviewBinding;
    private readonly List<ActionBinding> manualRestBindings = new List<ActionBinding>();
    private int lastIkCallbackFrame = -1;
    public bool ManualTestActive => manualTestActive;
    public NPCBodyActivity ManualTestActivity => activity;
    public string ManualTestController => animator != null && animator.runtimeAnimatorController != null
        ? animator.runtimeAnimatorController.name : "Not available";
    public string ManualTestPlayback
    {
        get
        {
            if (animator == null || layer < 0 || layer >= animator.layerCount) return "layer unavailable";
            var state = animator.GetCurrentAnimatorStateInfo(layer);
            string stateName = state.IsName("BodyGestures.PreviewTutorialSpeaking") ? "PreviewTutorialSpeaking" :
                state.IsName("BodyGestures.Idle") ? "Idle" : $"hash {state.fullPathHash}";
            return $"layer={layer}, state={stateName}, weight={animator.GetLayerWeight(layer):F2}, IK callback recently ran={(lastIkCallbackFrame >= 0 && Time.frameCount - lastIkCallbackFrame <= 5)}";
        }
    }

    public void ConfigureManualSpeakingPreview(bool enabled)
    {
        if (!manualTestActive) return;
        if (manualPreviewBinding != null) validActions.Remove(manualPreviewBinding);
        manualPreviewBinding = null;
        if (enabled && animator != null && layer > 0 &&
            animator.HasState(layer, Animator.StringToHash("BodyGestures.PreviewTutorialSpeaking")))
        {
            manualPreviewBinding = new ActionBinding { activity = NPCBodyActivity.SPEAKING,
                state = "BodyGestures.PreviewTutorialSpeaking", oneShot = true };
            validActions.Add(manualPreviewBinding);
        }
        else if (enabled)
            Debug.LogWarning("[Interview Animation Test] Tutorial Speaking preview state not found.", this);
        selected = null;
        lastContext = null;
        oneShotCooldownUntil = 0f;
    }

    public void BeginManualTest()
    {
        manualTestActive = true;
        if (animator != null && layer > 0 && animator.HasState(layer, Animator.StringToHash("BodyGestures.PreviewSpeakingRest")))
        {
            foreach (var restActivity in new[] { NPCBodyActivity.IDLE, NPCBodyActivity.LISTENING, NPCBodyActivity.THINKING })
            {
                var binding = new ActionBinding { activity = restActivity, state = "BodyGestures.PreviewSpeakingRest" };
                manualRestBindings.Add(binding);
                validActions.Add(binding);
            }
        }
        else Debug.LogWarning("[Interview Animation Test] Speaking rest preview state not found.", this);
        EnsurePointingBinding();
        pendingEvent = null;
        ApplyActivity(NPCBodyActivity.IDLE, 0);
    }

    public void SetManualTestActivity(NPCBodyActivity value)
    {
        if (manualTestActive)
        {
            if (pendingEvent == InterviewPointingEvent) { pendingEvent = null; selected = null; lastContext = null; }
            ApplyActivity(value, 0);
        }
    }

    public bool PreviewAngryPointing()
    {
        if (!manualTestActive) return false;
        return StartInterviewPointing();
    }

    public void EndManualTest()
    {
        if (!manualTestActive) return;
        ConfigureManualSpeakingPreview(false);
        foreach (var binding in manualRestBindings) validActions.Remove(binding);
        manualRestBindings.Clear();
        if (pendingEvent == InterviewPointingEvent) pendingEvent = null;
        if (!enableInterviewAngryPointing && interviewPointingBinding != null)
        {
            validActions.Remove(interviewPointingBinding);
            interviewPointingBinding = null;
        }
        ApplyActivity(NPCBodyActivity.IDLE, 0);
        manualTestActive = false;
        previewSpeakingTorsoCaptured = false;
        previewTorsoMatchWeight = 0f;
        selected = null;
        lastContext = null;
    }
#endif

    public bool ControlsLayer(Animator target, int index) =>
        isActiveAndEnabled && animator == target && layer == index && layer > 0;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator != null && sceneControllerOverride != null &&
            animator.runtimeAnimatorController != sceneControllerOverride)
            animator.runtimeAnimatorController = sceneControllerOverride;
    }

    private void Start()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (emotionSource == null) emotionSource = GetComponentInChildren<NPCEmotionController>();
        layer = animator != null && animator.runtimeAnimatorController != null &&
            !string.IsNullOrWhiteSpace(layerName) ? animator.GetLayerIndex(layerName) : -1;
        if (layer <= 0 || animator.runtimeAnimatorController == null ||
            animator.GetLayerWeight(layer) > 0f || animator.applyRootMotion)
        {
            Debug.LogWarning("[Body Animation] Assign a dedicated masked layer with weight zero and disable root motion.", this);
            enabled = false;
            return;
        }
        if (animator.isHuman && (animator.avatar == null || !animator.avatar.isValid))
        {
            Debug.LogWarning("[Body Animation] Invalid Humanoid avatar.", this);
            enabled = false;
            return;
        }
        if (seatedActivityGestures && animator.isHuman)
        {
            leftForearm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            rightForearm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            if (leftForearm == null || rightForearm == null)
                Debug.LogWarning("[Body Animation] Seated gestures need both Humanoid forearms.", this);
        }
        if (interviewActivityGestures && animator.isHuman)
        {
            nodHead = animator.GetBoneTransform(HumanBodyBones.Head);
            if (nodHead == null) Debug.LogWarning("[Body Animation] Listening nod requires a Humanoid head bone.", this);
        }
        foreach (var action in actions)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.state)) continue;
            if (animator.HasState(layer, Animator.StringToHash(action.state))) validActions.Add(action);
            else Debug.LogWarning("[Body Animation] Missing Animator state: " + action.state, this);
        }
        if (interviewActivityGestures && enableTutorialSpeakingInInterview)
        {
            const string speakingState = "BodyGestures.PreviewTutorialSpeaking";
            if (animator.HasState(layer, Animator.StringToHash(speakingState)))
                validActions.Add(new ActionBinding { activity = NPCBodyActivity.SPEAKING, state = speakingState, oneShot = true });
            else Debug.LogWarning("[Body Animation] Tutorial Speaking state is unavailable.", this);
        }
        if (interviewActivityGestures && enableInterviewAngryPointing) EnsurePointingBinding();
        if (validActions.Count == 0)
        {
            Debug.LogWarning("[Body Animation] No valid body states configured; existing animation remains active.", this);
            enabled = false;
            return;
        }
        if (string.IsNullOrWhiteSpace(npcId))
            Debug.LogWarning("[Body Animation] Assign a stable NPC ID to enable event recording.", this);
#if UNITY_EDITOR
        if (diagnoseSeatedGestures)
        {
            diagnosticLastController = animator.runtimeAnimatorController;
            Debug.Log($"[Body Animation Diagnostic] owner={DiagnosticPath(transform)} (id {GetInstanceID()}), animator={DiagnosticPath(animator.transform)} (id {animator.GetInstanceID()}), controller={(diagnosticLastController != null ? diagnosticLastController.name : "none")}.", this);
        }
#endif
    }

    public void SetActivity(NPCBodyActivity value, int dialogueTurn = 0)
    {
#if UNITY_EDITOR
        if (manualTestActive)
        {
            if (NPCEmotionSessionManager.Instance?.IsCollectingBodyEvents != true) return;
            EndManualTest();
        }
#endif
        ApplyActivity(value, dialogueTurn);
    }

    private void ApplyActivity(NPCBodyActivity value, int dialogueTurn)
    {
        if (activity != value)
        {
#if UNITY_EDITOR
            if (diagnoseSeatedGestures)
                Debug.Log($"[Body Animation Diagnostic] {name}: requested {value}; previous activity {activity}.", this);
            diagnosticSamples = 0;
            nextDiagnosticTime = Time.time + 0.75f;
#endif
            oneShotCooldownUntil = 0f;
            activityStartedAt = Time.time;
            if (value == NPCBodyActivity.LISTENING) nodStartTime = Time.time;
            if (value == NPCBodyActivity.SPEAKING) speakingStartedAt = Time.time;
            seatedGestureRecorded = false;
        }
        activity = value;
        turnNumber = dialogueTurn;
    }

    // Explicit context for ambient Talk/Selfie/Drink. No invented social-event AI.
    public void SetAmbientActivity(string value)
    {
        if (npcType == NPCBodyType.AMBIENT && Enum.TryParse(value, true, out NPCBodyActivity parsed))
            SetActivity(parsed);
    }

    public void ReactToEnvironment(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return;
        if (validActions.Exists(a => a.environmentEvent == eventName && MatchesScene(a)))
            pendingEvent = eventName;
    }

    private void EnsurePointingBinding()
    {
        if (interviewPointingBinding != null || animator == null || layer <= 0) return;
        const string state = "BodyGestures.PreviewAngryPointing";
        if (!animator.HasState(layer, Animator.StringToHash(state))) return;
        interviewPointingBinding = new ActionBinding { activity = NPCBodyActivity.IDLE,
            environmentEvent = InterviewPointingEvent, state = state, oneShot = true };
        validActions.Add(interviewPointingBinding);
    }

    private bool StartInterviewPointing()
    {
        EnsurePointingBinding();
        if (interviewPointingBinding == null || animator == null || !animator.isHuman ||
            animator.GetBoneTransform(HumanBodyBones.RightIndexProximal) == null)
        {
            Debug.LogWarning("[Body Animation] Pointing unavailable: state, Humanoid rig or right index bone is missing.", this);
            return false;
        }
        pendingEvent = InterviewPointingEvent;
        selected = null;
        lastContext = null;
        failedToStart = false;
        Debug.Log("[Body Animation] Seated pointing requested; direction and clearance require visual verification.", this);
        return true;
    }

    private string SceneType()
    {
        string scene = gameObject.scene.name.ToUpperInvariant();
        if (scene == "HALL") return "HALL";
        if (scene == "CLASSROOM") return "CLASSROOM";
        if (scene == "CAFETERIA" || scene == "KOPITIAM") return "KOPITIAM";
        return "OTHER";
    }

    private bool MatchesScene(ActionBinding action) =>
        string.IsNullOrEmpty(action.scene) || action.scene.Equals(SceneType(), StringComparison.OrdinalIgnoreCase);

    private bool AllowedAmbient(NPCBodyActivity value)
    {
        if (value == NPCBodyActivity.IDLE) return true;
        switch (SceneType())
        {
            // Group actions require an explicit activity/event; only phone use
            // is considered automatically during ordinary idle periods.
            case "HALL": return value == NPCBodyActivity.UsePhone ||
                value == NPCBodyActivity.Cheer || value == NPCBodyActivity.Clap;
            case "CLASSROOM": return value == NPCBodyActivity.UsePhone ||
                value == NPCBodyActivity.Talk || value == NPCBodyActivity.Selfie;
            case "KOPITIAM": return value == NPCBodyActivity.UsePhone ||
                value == NPCBodyActivity.Talk || value == NPCBodyActivity.Drink;
            default: return false;
        }
    }

    private void Update()
    {
        RestoreNodIfUnchanged();
        if (layer <= 0 || animator == null) return;
#if UNITY_EDITOR
        previewPoseWeight = Mathf.MoveTowards(previewPoseWeight,
            manualTestActive && previewSeatedPoseCorrections ? 1f : 0f,
            Time.deltaTime / Mathf.Max(0.5f, blendSeconds));
        if (!manualTestActive || (!previewSeatedPoseCorrections && previewPoseWeight <= 0f))
            previewSpeakingTorsoCaptured = false;
        float torsoTarget = previewMatchSpeakingTorso && previewSpeakingTorsoCaptured &&
            !(previewSpeakingRestPose && previewSeatedPoseCorrections) &&
            activity != NPCBodyActivity.SPEAKING && string.IsNullOrEmpty(pendingEvent) ? 1f : 0f;
        previewTorsoMatchWeight = Mathf.MoveTowards(previewTorsoMatchWeight, torsoTarget,
            Time.deltaTime / Mathf.Max(0.5f, blendSeconds));
#endif
        if (interviewActivityGestures)
            nodWeight = Mathf.MoveTowards(nodWeight, activity == NPCBodyActivity.LISTENING ? 1f : 0f,
                Time.deltaTime / Mathf.Max(0.5f, blendSeconds));
#if UNITY_EDITOR
        if (diagnoseSeatedGestures && animator.runtimeAnimatorController != diagnosticLastController)
        {
            var nextController = animator.runtimeAnimatorController;
            Debug.LogWarning($"[Body Animation Diagnostic] {DiagnosticPath(animator.transform)} controller changed from {(diagnosticLastController != null ? diagnosticLastController.name : "none")} to {(nextController != null ? nextController.name : "none")}.", this);
            diagnosticLastController = nextController;
        }
#endif
        bool seatedActivity = seatedActivityGestures && npcType == NPCBodyType.INTERVIEWER &&
            string.IsNullOrEmpty(pendingEvent) &&
            animator.isHuman && leftForearm != null && rightForearm != null &&
            (activity == NPCBodyActivity.LISTENING || activity == NPCBodyActivity.THINKING);
        seatedGestureWeight = Mathf.MoveTowards(seatedGestureWeight, seatedActivity ? 1f : 0f,
            Time.deltaTime / Mathf.Max(0.25f, blendSeconds));
        bool seatedSpeaking = seatedActivityGestures && seatedSpeakingDegrees > 0f &&
            npcType == NPCBodyType.INTERVIEWER && string.IsNullOrEmpty(pendingEvent) &&
            animator.isHuman && rightForearm != null && activity == NPCBodyActivity.SPEAKING;
        speakingGestureWeight = Mathf.MoveTowards(speakingGestureWeight, seatedSpeaking ? 1f : 0f,
            Time.deltaTime / Mathf.Max(0.25f, blendSeconds));
        if (useSeatedConversationGestures)
            UpdateConversationGestures();
        string emotion = emotionSource != null ? emotionSource.currentEmotion : "NEUTRAL";
        if (interviewActivityGestures && enableInterviewAngryPointing)
        {
            if (lastPointingEmotion != null && lastPointingEmotion != emotion && emotion == "ANGRY")
            {
#if UNITY_EDITOR
                if (!manualTestActive)
#endif
                    StartInterviewPointing();
            }
            lastPointingEmotion = emotion;
        }
        string context = activity + "|" + emotion + "|" + pendingEvent;
#if UNITY_EDITOR
        context += "|" + (manualTestActive && previewSeatedPoseCorrections && previewSpeakingRestPose);
#endif
        if (selected != null)
        {
            var state = animator.GetCurrentAnimatorStateInfo(layer);
            int selectedHash = Animator.StringToHash(selected.state);
            bool transitioning = animator.IsInTransition(layer);
            bool playing = state.fullPathHash == selectedHash;
            bool entering = transitioning && animator.GetNextAnimatorStateInfo(layer).fullPathHash == selectedHash;
            if (!observedStart && (entering || (playing && !transitioning)) &&
                animator.GetLayerWeight(layer) > 0f)
            {
                var clips = entering ? animator.GetNextAnimatorClipInfo(layer) : animator.GetCurrentAnimatorClipInfo(layer);
                if (Array.Exists(clips, clip => clip.clip != null && clip.weight > 0f))
                {
                    observedStart = true;
#if UNITY_EDITOR
                    if (manualTestActive && selected.environmentEvent == InterviewPointingEvent)
                        Debug.Log("[Interview Animation Test] Pointing clip started.", this);
#endif
                    RecordStart(emotion);
                }
            }
            if (!observedStart && Time.time - startedAt > 3f)
            {
                Debug.LogWarning("[Body Animation] State did not begin: " + selected.state, this);
                selected = null;
                failedToStart = true;
            }
            else if (observedStart &&
                (selected.oneShot || !state.loop || !string.IsNullOrEmpty(selected.environmentEvent)) &&
                !transitioning && ((playing && state.normalizedTime >= 1f) || !playing))
            {
                if (!string.IsNullOrEmpty(selected.environmentEvent))
                {
#if UNITY_EDITOR
                    if (manualTestActive && selected.environmentEvent == InterviewPointingEvent)
                        Debug.Log("[Interview Animation Test] Pointing clip finished; returning to current activity.", this);
#endif
                    pendingEvent = null;
                }
                else oneShotCooldownUntil = Time.time + Mathf.Max(3f, variationInterval);
                selected = null;
                nextSelection = Time.time + 1f;
                lastContext = null;
            }
        }
        bool reactionPlaying = selected != null && !string.IsNullOrEmpty(selected.environmentEvent) &&
            selected.environmentEvent == pendingEvent;
        if (!reactionPlaying && (context != lastContext ||
            (!failedToStart && Time.time >= nextSelection)))
        {
            failedToStart = false;
            SelectAction(emotion);
            lastContext = context;
            nextSelection = Time.time + Mathf.Max(3f, variationInterval);
        }
        float intensity = emotionSource != null ? Mathf.Clamp01(emotionSource.emotionIntensity / 100f) : 0f;
        float target = selected != null ? layerWeight * Mathf.Lerp(0.75f, 1f, intensity) : 0f;
        if (interviewActivityGestures && selected != null && selected.environmentEvent == InterviewPointingEvent)
            target = Mathf.Clamp01(interviewPointingLayerWeight);
        animator.SetLayerWeight(layer, Mathf.MoveTowards(animator.GetLayerWeight(layer),
            target, Time.deltaTime / Mathf.Max(0.1f, blendSeconds)));
    }

    // Animator IK Pass applies a small offset to the animated pose; no Transform
    // writes or face/head/root/leg bones are owned by this component.
    private void OnAnimatorIK(int layerIndex)
    {
#if UNITY_EDITOR
        if (layerIndex == layer) lastIkCallbackFrame = Time.frameCount;
        if (layerIndex == layer)
        {
            if (manualTestActive && previewPoseWeight > 0f && interviewActivityGestures)
                ApplySeatedPosePreview();
            else if (previewHandIkApplied)
                ReleasePreviewHandIK();
        }
#endif
        if (interviewActivityGestures) return;
        if (useSeatedConversationGestures)
        {
            ApplyConversationGestures(layerIndex);
            return;
        }
        if (seatedActivityGestures && layerIndex == layer && speakingGestureWeight > 0f &&
            string.IsNullOrEmpty(pendingEvent) && rightForearm != null &&
            (activity == NPCBodyActivity.SPEAKING || activity == NPCBodyActivity.IDLE))
        {
            float speakingCycle = .5f - .5f * Mathf.Cos(Mathf.Max(0f, Time.time - speakingStartedAt) * 2f);
            float speakingAngle = Mathf.Clamp(seatedSpeakingDegrees, 0f, 5f) * speakingGestureWeight * speakingCycle;
            if (speakingAngle > .01f)
            {
#if UNITY_EDITOR
                CaptureDiagnosticIk(speakingAngle);
#endif
                animator.SetBoneLocalRotation(HumanBodyBones.RightLowerArm,
                    rightForearm.localRotation * Quaternion.AngleAxis(-speakingAngle, Vector3.right));
                if (activity == NPCBodyActivity.SPEAKING && !seatedGestureRecorded)
                {
                    seatedGestureRecorded = true;
                    RecordSeatedGestureStart();
                }
            }
            return;
        }
        if (!seatedActivityGestures || layerIndex != layer || seatedGestureWeight <= 0f ||
            !string.IsNullOrEmpty(pendingEvent) ||
            leftForearm == null || rightForearm == null ||
            (activity != NPCBodyActivity.LISTENING && activity != NPCBodyActivity.THINKING)) return;

        float elapsed = Mathf.Max(0f, Time.time - activityStartedAt);
        float cycle = .5f - .5f * Mathf.Cos(elapsed * (activity == NPCBodyActivity.THINKING ? 1.4f : 1f));
        float angle = Mathf.Clamp(seatedGestureDegrees, 0f, 10f) * seatedGestureWeight * cycle;
        if (angle <= .01f) return;
#if UNITY_EDITOR
        CaptureDiagnosticIk(angle);
#endif
        animator.SetBoneLocalRotation(HumanBodyBones.RightLowerArm,
            rightForearm.localRotation * Quaternion.AngleAxis(angle, Vector3.right));
        if (activity == NPCBodyActivity.LISTENING)
            animator.SetBoneLocalRotation(HumanBodyBones.LeftLowerArm,
                leftForearm.localRotation * Quaternion.AngleAxis(angle * .45f, Vector3.right));
        if (!seatedGestureRecorded)
        {
            seatedGestureRecorded = true;
            RecordSeatedGestureStart();
        }
    }

    private void UpdateConversationGestures()
    {
        float rightTarget = 0f, leftTarget = 0f;
        bool available = seatedActivityGestures && npcType == NPCBodyType.INTERVIEWER &&
            animator.isHuman && leftForearm != null && rightForearm != null && string.IsNullOrEmpty(pendingEvent);
        float elapsed = Mathf.Max(0f, Time.time - activityStartedAt);
        if (available && activity == NPCBodyActivity.LISTENING)
        {
            // Settle into a small attentive posture instead of continuously waving.
            float settle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 1.2f));
            rightTarget = Mathf.Clamp(listeningGestureDegrees, 0f, 8f) * settle;
            leftTarget = rightTarget * 0.35f;
        }
        else if (available && activity == NPCBodyActivity.THINKING)
        {
            float settle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 1.4f));
            rightTarget = -Mathf.Clamp(thinkingGestureDegrees, 0f, 8f) * settle;
        }
        else if (available && activity == NPCBodyActivity.SPEAKING)
        {
            // A short pair of gestures followed by a pause; this is not speech analysis.
            float phase = Mathf.Repeat(elapsed * Mathf.Clamp(conversationGestureSpeed, 0.5f, 2f), 5f);
            float phrase = phase < 3f ? Mathf.Sin(Mathf.PI * phase / 3f) : 0f;
            float pulse = 0.55f + 0.45f * Mathf.Sin(phase * 3f) * Mathf.Sin(phase * 3f);
            rightTarget = -Mathf.Clamp(conversationSpeakingDegrees, 0f, 8f) * phrase * pulse;
            leftTarget = -rightTarget * 0.25f;
        }
        float step = 8f * Time.deltaTime / Mathf.Max(0.5f, gestureTransitionSeconds);
        conversationRightAngle = Mathf.MoveTowards(conversationRightAngle, rightTarget, step);
        conversationLeftAngle = Mathf.MoveTowards(conversationLeftAngle, leftTarget, step);
    }

    private void ApplyConversationGestures(int layerIndex)
    {
        if (!seatedActivityGestures || layerIndex != layer || !string.IsNullOrEmpty(pendingEvent) ||
            leftForearm == null || rightForearm == null) return;
        if (Mathf.Abs(conversationRightAngle) > 0.01f)
        {
#if UNITY_EDITOR
            CaptureDiagnosticIk(conversationRightAngle);
#endif
            animator.SetBoneLocalRotation(HumanBodyBones.RightLowerArm,
                rightForearm.localRotation * Quaternion.AngleAxis(conversationRightAngle, Vector3.right));
            if (activity != NPCBodyActivity.IDLE && !seatedGestureRecorded)
            {
                seatedGestureRecorded = true;
                RecordSeatedGestureStart();
            }
        }
        if (Mathf.Abs(conversationLeftAngle) > 0.01f)
            animator.SetBoneLocalRotation(HumanBodyBones.LeftLowerArm,
                leftForearm.localRotation * Quaternion.AngleAxis(conversationLeftAngle, Vector3.right));
    }

#if UNITY_EDITOR
    private void ApplySeatedPosePreview()
    {
        if (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid) return;
        var spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        float totalDegrees = Mathf.Clamp(previewUprightDegrees, 0f, 10f) * previewPoseWeight;
        float chestFraction = chest != null && chest.parent != null ? Mathf.Clamp01(previewChestShare) : 0f;
        Quaternion spineLocal = spine != null ? spine.localRotation : Quaternion.identity;
        Quaternion chestLocal = chest != null ? chest.localRotation : Quaternion.identity;
        if (spine != null && spine.parent != null)
        {
            Quaternion uprightWorld = Quaternion.AngleAxis(totalDegrees * (1f - chestFraction), animator.transform.right) * spine.rotation;
            spineLocal = Quaternion.Inverse(spine.parent.rotation) * uprightWorld;
        }
        if (chestFraction > 0f)
        {
            Quaternion uprightWorld = Quaternion.AngleAxis(totalDegrees * chestFraction, animator.transform.right) * chest.rotation;
            chestLocal = Quaternion.Inverse(chest.parent.rotation) * uprightWorld;
        }
        if (previewMatchSpeakingTorso && !previewSpeakingTorsoCaptured && previewPoseWeight >= 0.95f &&
            activity == NPCBodyActivity.SPEAKING && string.IsNullOrEmpty(pendingEvent) &&
            !animator.IsInTransition(layer) && spine != null && chest != null)
        {
            var speakingState = animator.GetCurrentAnimatorStateInfo(layer);
            if (speakingState.IsName("BodyGestures.PreviewTutorialSpeaking") && speakingState.normalizedTime >= 0.5f && speakingState.normalizedTime < 1f)
            {
                previewSpeakingSpine = spineLocal;
                previewSpeakingChest = chestLocal;
                previewSpeakingTorsoCaptured = true;
                Debug.Log("[Interview Animation Test] Captured Speaking torso pose for seated preview.", this);
            }
        }
        float matchWeight = previewSpeakingTorsoCaptured ? previewTorsoMatchWeight * previewPoseWeight : 0f;
        if (spine != null && spine.parent != null)
            animator.SetBoneLocalRotation(HumanBodyBones.Spine, matchWeight > 0f ? Quaternion.Slerp(spineLocal, previewSpeakingSpine, matchWeight) : spineLocal);
        if (chest != null && chest.parent != null)
            animator.SetBoneLocalRotation(HumanBodyBones.Chest, matchWeight > 0f ? Quaternion.Slerp(chestLocal, previewSpeakingChest, matchWeight) : chestLocal);
        float lift = Mathf.Clamp(previewHandLiftMetres, 0f, 0.08f);
        if (leftHand != null)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, previewPoseWeight);
            animator.SetIKPosition(AvatarIKGoal.LeftHand, leftHand.position + Vector3.up * lift);
        }
        if (rightHand != null)
        {
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, previewPoseWeight);
            animator.SetIKPosition(AvatarIKGoal.RightHand, rightHand.position + Vector3.up * lift);
        }
        previewHandIkApplied = leftHand != null || rightHand != null;
    }

    // Animator IK setters must be called from OnAnimatorIK, not OnDisable.
    private void ReleasePreviewHandIK()
    {
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
        previewHandIkApplied = false;
    }

    private static string DiagnosticPath(Transform item)
    {
        string path = item.name;
        while (item.parent != null) { item = item.parent; path = item.name + "/" + path; }
        return path;
    }

    private void CaptureDiagnosticIk(float requestedAngle)
    {
        if (!diagnoseSeatedGestures || rightForearm == null) return;
        diagnosticBefore = rightForearm.localRotation;
        diagnosticRequestedAngle = requestedAngle;
        diagnosticIkFrame = Time.frameCount;
    }

    private void EditorDiagnosticsLateUpdate()
    {
        if (!diagnoseSeatedGestures || diagnosticSamples >= 2 || Time.time < nextDiagnosticTime) return;
        diagnosticSamples++;
        nextDiagnosticTime = Time.time + 1f;
        string state = "unavailable";
        float weight = 0f;
        if (animator != null && layer >= 0 && layer < animator.layerCount)
        {
            var info = animator.GetCurrentAnimatorStateInfo(layer);
            state = info.IsName("BodyGestures.Idle") ? "BodyGestures.Idle" :
                info.IsName("BodyGestures.PreviewTutorialSpeaking") ? "BodyGestures.PreviewTutorialSpeaking" : $"hash {info.fullPathHash}";
            weight = animator.GetLayerWeight(layer);
        }
        bool observedIk = diagnosticIkFrame == Time.frameCount && rightForearm != null;
        string bone = observedIk
            ? $"requested offset {diagnosticRequestedAngle:F2}°, observed rotation delta {Quaternion.Angle(diagnosticBefore, rightForearm.localRotation):F2}°"
            : "no forearm IK sample this frame";
        Debug.Log($"[Body Animation Diagnostic] {name}: activity={activity}, controller={(animator != null && animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "none")}, layer={layer}, weight={weight:F2}, state={state}, humanoid={(animator != null && animator.isHuman)}, {bone}.", this);
    }
#endif

    private void LateUpdate()
    {
        if (interviewActivityGestures && nodHead != null && nodWeight > 0.001f)
        {
            float elapsed = Mathf.Max(0f, Time.time - nodStartTime);
            float frequency = Mathf.Clamp(listeningNodFrequency, 0.2f, 1.5f);
            float sequenceDuration = 2f / frequency;
            float phase = Mathf.Repeat(elapsed, sequenceDuration + Mathf.Clamp(listeningNodPause, 0.1f, 3f));
            float nod = phase < sequenceDuration ? 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * frequency * phase) : 0f;
            nodBaseRotation = nodHead.localRotation;
            nodAppliedRotation = nodBaseRotation * Quaternion.AngleAxis(Mathf.Clamp(listeningNodDegrees, 0f, 10f) * nodWeight * nod, Vector3.right);
            nodHead.localRotation = nodAppliedRotation;
            nodApplied = true;
        }
#if UNITY_EDITOR
        EditorDiagnosticsLateUpdate();
#endif
    }

    private void RestoreNodIfUnchanged()
    {
        if (!nodApplied || nodHead == null) { nodApplied = false; return; }
        if (Quaternion.Angle(nodHead.localRotation, nodAppliedRotation) < 0.05f)
            nodHead.localRotation = nodBaseRotation;
        nodApplied = false;
    }

    private void RecordSeatedGestureStart()
    {
#if UNITY_EDITOR
        if (manualTestActive) return;
#endif
        var session = NPCEmotionSessionManager.Instance;
        if (session == null || !session.IsCollectingBodyEvents || string.IsNullOrWhiteSpace(npcId)) return;
        session.RecordBodyAnimation(new BodyAnimationEventRecord
        {
            animation_event_id = Guid.NewGuid().ToString(), npc_id = npcId,
            npc_type = npcType.ToString(), scene_type = gameObject.scene.name,
            npc_activity = activity.ToString(), emotion = emotionSource != null ? emotionSource.currentEmotion : "NEUTRAL",
            animation_name = "Seated " + activity + " forearm gesture", timestamp = DateTime.UtcNow.ToString("o"),
            turnNumber = turnNumber
        });
    }

    private void SelectAction(string emotion)
    {
        candidates.Clear();
        int best = -1;
        foreach (var action in validActions)
        {
#if UNITY_EDITOR
            bool isPreviewRest = manualRestBindings.Contains(action);
            if (isPreviewRest && !(manualTestActive && previewSeatedPoseCorrections && previewSpeakingRestPose)) continue;
#else
            const bool isPreviewRest = false;
#endif
            if (!MatchesScene(action)) continue;
            if (!string.IsNullOrEmpty(action.emotion) && action.emotion != emotion) continue;
            if (string.IsNullOrEmpty(action.environmentEvent) &&
                !isPreviewRest && Time.time < oneShotCooldownUntil &&
                (action.activity != NPCBodyActivity.IDLE || action == lastAction))
                continue;
            int score;
            if (!string.IsNullOrEmpty(action.environmentEvent))
            {
                if (action.environmentEvent != pendingEvent) continue;
                score = 100;
            }
            else if (npcType == NPCBodyType.INTERVIEWER)
            {
                if (action.activity == activity) score = 50;
                else if (action.activity == NPCBodyActivity.IDLE) score = 10;
                else continue;
            }
            else
            {
                if (!AllowedAmbient(action.activity)) continue;
                if (action.activity == activity) score = 40;
                else if (activity == NPCBodyActivity.IDLE && action.activity == NPCBodyActivity.UsePhone) score = 40;
                else if (action.activity == NPCBodyActivity.IDLE) score = 10;
                else continue;
            }
            if (!string.IsNullOrEmpty(action.emotion)) score += 1;
            if (interviewActivityGestures && enableTutorialSpeakingInInterview &&
                action.state == "BodyGestures.PreviewTutorialSpeaking") score += 5;
#if UNITY_EDITOR
            if (action == manualPreviewBinding) score += 10;
            if (isPreviewRest) score += 20;
#endif
            if (score < best) continue;
            if (score > best) { candidates.Clear(); best = score; }
            candidates.Add(action);
        }
        if (candidates.Count > 1) candidates.Remove(lastAction);
        if (candidates.Count == 0) { selected = null; return; }
        var next = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        if (selected != null && selected.state == next.state) return;
        selected = next;
        lastAction = next;
        observedStart = false;
        startedAt = Time.time;
        selectedTurn = npcType == NPCBodyType.INTERVIEWER ? turnNumber : 0;
        recordedActivity = npcType == NPCBodyType.AMBIENT || !string.IsNullOrEmpty(next.environmentEvent)
            ? next.activity.ToString() : activity.ToString();
        animator.CrossFadeInFixedTime(Animator.StringToHash(next.state), blendSeconds, layer);
    }

    private void RecordStart(string emotion)
    {
#if UNITY_EDITOR
        if (manualTestActive) return;
#endif
        if (string.IsNullOrWhiteSpace(npcId)) return;
        var session = NPCEmotionSessionManager.Instance;
        if (session == null || !session.IsCollectingBodyEvents) return;
        session.RecordBodyAnimation(new BodyAnimationEventRecord
        {
            animation_event_id = Guid.NewGuid().ToString(), npc_id = npcId,
            npc_type = npcType.ToString(), scene_type = gameObject.scene.name,
            npc_activity = recordedActivity, emotion = emotion,
            animation_name = selected.state, timestamp = DateTime.UtcNow.ToString("o"),
            turnNumber = selectedTurn
        });
    }

    private void OnDisable()
    {
        RestoreNodIfUnchanged();
        nodWeight = 0f;
#if UNITY_EDITOR
        foreach (var binding in manualRestBindings) validActions.Remove(binding);
        manualRestBindings.Clear();
        if (manualPreviewBinding != null) validActions.Remove(manualPreviewBinding);
        manualPreviewBinding = null;
        manualTestActive = false;
        previewPoseWeight = 0f;
        previewSpeakingTorsoCaptured = false;
        previewTorsoMatchWeight = 0f;
#endif
        if (layer > 0 && animator != null) animator.SetLayerWeight(layer, 0f);
        selected = null;
        lastContext = null;
        pendingEvent = null;
        lastPointingEmotion = null;
        seatedGestureWeight = 0f;
        speakingGestureWeight = 0f;
        conversationRightAngle = 0f;
        conversationLeftAngle = 0f;
    }
}
