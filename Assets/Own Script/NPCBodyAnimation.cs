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
    [SerializeField] private NPCEmotionController emotionSource;
    [Tooltip("Dedicated layer, default weight zero. Mask out head, face, eyes, jaw and root.")]
    [SerializeField] private string layerName;
    [SerializeField, Min(0.1f)] private float blendSeconds = 0.5f;
    [SerializeField, Min(3f)] private float variationInterval = 8f;
    [SerializeField, Range(0.1f, 1f)] private float layerWeight = 0.8f;
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

    public bool ControlsLayer(Animator target, int index) =>
        isActiveAndEnabled && animator == target && layer == index && layer > 0;

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
        foreach (var action in actions)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.state)) continue;
            if (animator.HasState(layer, Animator.StringToHash(action.state))) validActions.Add(action);
            else Debug.LogWarning("[Body Animation] Missing Animator state: " + action.state, this);
        }
        if (validActions.Count == 0)
        {
            Debug.LogWarning("[Body Animation] No valid body states configured; existing animation remains active.", this);
            enabled = false;
            return;
        }
        if (string.IsNullOrWhiteSpace(npcId))
            Debug.LogWarning("[Body Animation] Assign a stable NPC ID to enable event recording.", this);
    }

    public void SetActivity(NPCBodyActivity value, int dialogueTurn = 0)
    {
        if (activity != value) oneShotCooldownUntil = 0f;
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
        if (layer <= 0 || animator == null) return;
        string emotion = emotionSource != null ? emotionSource.currentEmotion : "NEUTRAL";
        string context = activity + "|" + emotion + "|" + pendingEvent;
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
                    pendingEvent = null;
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
            lastContext = activity + "|" + emotion + "|" + pendingEvent;
            nextSelection = Time.time + Mathf.Max(3f, variationInterval);
        }
        float intensity = emotionSource != null ? Mathf.Clamp01(emotionSource.emotionIntensity / 100f) : 0f;
        float target = selected != null ? layerWeight * Mathf.Lerp(0.75f, 1f, intensity) : 0f;
        animator.SetLayerWeight(layer, Mathf.MoveTowards(animator.GetLayerWeight(layer),
            target, Time.deltaTime / Mathf.Max(0.1f, blendSeconds)));
    }

    private void SelectAction(string emotion)
    {
        candidates.Clear();
        int best = -1;
        foreach (var action in validActions)
        {
            if (!MatchesScene(action)) continue;
            if (!string.IsNullOrEmpty(action.emotion) && action.emotion != emotion) continue;
            if (string.IsNullOrEmpty(action.environmentEvent) &&
                Time.time < oneShotCooldownUntil &&
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
        if (layer > 0 && animator != null) animator.SetLayerWeight(layer, 0f);
        selected = null;
        lastContext = null;
        pendingEvent = null;
    }
}
