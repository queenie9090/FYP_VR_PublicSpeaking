using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks gestures for the session.
///  - Primary Engine  : Smoothed Velocity Tracking with a 0.3s Idle Grace Period.
///  - Secondary Engine: Zone Transit (ArmTransitDetector) cross-deduplicated.
///  - System Gestures : Forwarded to UI/Tutorial and retroactively scrubbed from expressive scores.
/// </summary>
public class GestureTracker : MonoBehaviour
{
    [Header("Manager References")]
    [SerializeField] private InteractionManager interactionManager;
    [SerializeField] private TutorialPanelManager tutorialManager;
    [SerializeField] private HintUIController hintUIController;
    [SerializeField] private WarningPanelManager warningPanelManager;
    [SerializeField] private ArmTransitDetector armTransitDetector;

    [Header("Fallback Motion Sources (if ArmTransitDetector has no manual probes)")]
    [SerializeField] private Transform leftHandTransform;
    [SerializeField] private Transform rightHandTransform;
    [SerializeField] private Transform leftControllerTransform;
    [SerializeField] private Transform rightControllerTransform;

    [Header("Reference Frames")]
    [SerializeField] private Transform trackingSpace;
    [SerializeField] private Transform headset;

    [Header("Primary Velocity Detection Settings")]
    [SerializeField] private bool enableVelocityTracking = true;
    [SerializeField] private float minSpeed = 0.45f;          // m/s to initiate movement
    [SerializeField] private float minDuration = 0.35f;       // minimum sustained seconds
    [SerializeField] private float minDistance = 0.16f;       // minimum meters traveled
    [SerializeField] private float idleGracePeriod = 0.30f;   // 0.3s pause buffer before ending episode
    [SerializeField] private float maxPlausibleSpeed = 6.0f;  // tracking glitch filter
    [SerializeField] private float gestureCooldown = 1.5f;    // per-hand cooldown between counts
    [SerializeField] private bool logMotionDebug = false;

    [Header("System Gesture Handling")]
    [SerializeField] private float systemGestureSuppression = 1.5f;
    [SerializeField] private float retroactiveWindow = 1.0f;

    [Header("Unprofessional Gesture Tracking")]
    private int unprofessionalGestureCount = 0;

    private class VelocityState
    {
        public Vector3 smoothedPos;
        public bool initialized;
        public bool isEpisodeActive;
        public float smoothedSpeed;
        public float episodeTime;
        public float episodeDistance;
        public float idleTimer;

        public void Reset()
        {
            initialized = false;
            isEpisodeActive = false;
            smoothedSpeed = 0f;
            episodeTime = 0f;
            episodeDistance = 0f;
            idleTimer = 0f;
        }
    }

    private class GestureRecord
    {
        public string label;
        public float duration;
        public float time;
        public int handIndex;
    }

    private readonly VelocityState leftState = new VelocityState();
    private readonly VelocityState rightState = new VelocityState();
    private readonly float[] lastHandGestureTime = { -99f, -99f };
    private readonly List<GestureRecord> validExpressiveGestures = new List<GestureRecord>();

    private Transform leftSource;
    private Transform rightSource;
    private float activeTrackingDurationSeconds = 0.0f;
    private float suppressUntil = 0f;
    private bool isTracking = false;

    public bool IsTracking => isTracking;

    private void Awake()
    {
        isTracking = false;
    }

    private void Start()
    {
        if (interactionManager == null) interactionManager = FindObjectOfType<InteractionManager>();
        if (tutorialManager == null) tutorialManager = FindObjectOfType<TutorialPanelManager>();
        if (hintUIController == null) hintUIController = FindObjectOfType<HintUIController>();
        if (warningPanelManager == null) warningPanelManager = FindObjectOfType<WarningPanelManager>();
        if (armTransitDetector == null) armTransitDetector = FindObjectOfType<ArmTransitDetector>();

        if (headset == null && Camera.main != null) headset = Camera.main.transform;
        if (trackingSpace == null)
        {
            var origin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null) trackingSpace = origin.transform;
        }

        ResolveMotionSources();
    }

    private void Update()
    {
        if (!isTracking) return;

        activeTrackingDurationSeconds += Time.deltaTime;

        if (!enableVelocityTracking) return;

        if (leftSource == null || rightSource == null) ResolveMotionSources();

        TrackVelocityMotion(leftSource, leftState, 0);
        TrackVelocityMotion(rightSource, rightState, 1);
    }

    public void StartTracking() => ResetTracker();
    public void StopTracking() => isTracking = false;

    public void ResetTracker()
    {
        validExpressiveGestures.Clear();
        unprofessionalGestureCount = 0;
        activeTrackingDurationSeconds = 0.0f;
        suppressUntil = 0f;
        lastHandGestureTime[0] = lastHandGestureTime[1] = -99f;
        leftState.Reset();
        rightState.Reset();
        ResolveMotionSources();
        isTracking = true;
        Debug.Log("[GestureTracker] Primary Velocity Tracker STARTED.");
    }

    // -------------------------------------------------------------
    // Primary Engine: Velocity Motion with 0.3s Grace Period
    // -------------------------------------------------------------
    private void TrackVelocityMotion(Transform source, VelocityState s, int handIndex)
    {
        if (source == null || headset == null || !source.gameObject.activeInHierarchy || Time.time < suppressUntil)
        {
            s.Reset();
            return;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Position relative to tracking head space
        Vector3 rawPos = trackingSpace != null
            ? trackingSpace.InverseTransformPoint(source.position) - trackingSpace.InverseTransformPoint(headset.position)
            : source.position - headset.position;

        if (!s.initialized)
        {
            s.smoothedPos = rawPos;
            s.initialized = true;
            return;
        }

        // 1. Vector Position Smoothing (Exponential Moving Average)
        s.smoothedPos = Vector3.Lerp(s.smoothedPos, rawPos, 0.35f);
        float step = Vector3.Distance(rawPos, s.smoothedPos);
        float rawSpeed = step / dt;

        if (rawSpeed > maxPlausibleSpeed)
        {
            s.Reset();
            return;
        }

        s.smoothedSpeed = Mathf.Lerp(s.smoothedSpeed, rawSpeed, 0.25f);
        bool active = s.smoothedSpeed >= minSpeed;

        if (logMotionDebug && s.smoothedSpeed > 0.2f)
            Debug.Log($"[GestureTracker Live] Hand {handIndex} Speed: {s.smoothedSpeed:F2} m/s | Active: {s.isEpisodeActive} | Time: {s.episodeTime:F2}s");

        // 2. Grace Period State Machine
        if (active)
        {
            s.isEpisodeActive = true;
            s.idleTimer = 0f;
            s.episodeTime += dt;
            s.episodeDistance += step;
        }
        else if (s.isEpisodeActive)
        {
            s.idleTimer += dt;

            // Episode finishes after grace period expires
            if (s.idleTimer >= idleGracePeriod)
            {
                if (s.episodeTime >= minDuration && s.episodeDistance >= minDistance && !IsDuplicate(handIndex))
                {
                    AddGesture(handIndex == 0 ? "Velocity (Left)" : "Velocity (Right)", s.episodeTime, handIndex);
                }
                s.Reset();
            }
        }
    }

    // -------------------------------------------------------------
    // Secondary Engine: Zone Transit Callback (ArmTransitDetector)
    // -------------------------------------------------------------
    public void RegisterExpressiveArmTransit(float transitDuration, int handIndex = -1)
    {
        if (!isTracking || Time.time < suppressUntil) return;

        if (IsDuplicate(handIndex))
        {
            Debug.Log($"[GestureTracker] Zone Transit merged with recent velocity count for hand {handIndex}.");
            return;
        }

        AddGesture("Zone Transit", transitDuration, handIndex);
    }

    private bool IsDuplicate(int handIndex)
    {
        if (handIndex < 0 || handIndex > 1) return false;
        return (Time.time - lastHandGestureTime[handIndex]) < gestureCooldown;
    }

    private void AddGesture(string label, float duration, int handIndex)
    {
        validExpressiveGestures.Add(new GestureRecord { label = label, duration = duration, time = Time.time, handIndex = handIndex });
        if (handIndex == 0 || handIndex == 1) lastHandGestureTime[handIndex] = Time.time;
        Debug.Log($"<color=cyan>[GestureTracker Log]</color> {label} COUNTED. Duration: {duration:F2}s | Total Count: {validExpressiveGestures.Count}");
    }

    private void ResolveMotionSources()
    {
        Transform l = null, r = null;
        if (armTransitDetector == null) armTransitDetector = FindObjectOfType<ArmTransitDetector>();
        if (armTransitDetector != null) (l, r) = armTransitDetector.GetActiveProbeTransforms();

        if (l == null || r == null)
        {
            var (fl, fr) = GetActiveSources();
            if (l == null) l = fl;
            if (r == null) r = fr;
        }
        leftSource = l; rightSource = r;
    }

    public (Transform left, Transform right) GetActiveSources()
    {
        string mode = UserSession.InputMode;
        bool useHands = !string.IsNullOrEmpty(mode) && mode.Contains("Hand");
        return (useHands ? leftHandTransform : leftControllerTransform,
                useHands ? rightHandTransform : rightControllerTransform);
    }

    // -------------------------------------------------------------
    // System Pose Events & Unprofessional Tracking
    // -------------------------------------------------------------
    public void TriggerHaveAQuestionGesture() => ProcessGestureEvent("HaveAQuestion", 0.5f);
    public void TriggerThumbsDownGesture() => ProcessGestureEvent("ThumbsDown", 0.3f);
    public void TriggerUnprofessionalGesture() => ProcessUnprofessionalGesture("Middle Finger");
    public void TriggerPinchGesture() => ProcessGestureEvent("Pinch", 0.5f);
    public void TriggerPointPinchGesture() => ProcessGestureEvent("Point + Pinch", 0.5f);
    public void TriggerGrabGesture() => ProcessGestureEvent("Grab", 0.5f);

    public void ProcessGestureEvent(string gestureType, float gestureDuration)
    {
        Debug.Log($"<color=cyan>[GestureTracker RAW]</color> System Gesture: '{gestureType}' | Duration: {gestureDuration}s");

        suppressUntil = Time.time + systemGestureSuppression;

        if (validExpressiveGestures.Count > 0)
        {
            float cutoff = Time.time - retroactiveWindow;
            int removed = validExpressiveGestures.RemoveAll(g => g.time >= cutoff);
            if (removed > 0)
                Debug.Log($"[GestureTracker] Retroactively removed {removed} expressive count(s) near '{gestureType}'.");
        }
        leftState.Reset(); rightState.Reset();

        if (tutorialManager == null) tutorialManager = FindObjectOfType<TutorialPanelManager>();
        if (tutorialManager != null) tutorialManager.OnGesturePerformed(gestureType);

        if (gestureType == "HaveAQuestion")
        {
            if (hintUIController == null) hintUIController = FindObjectOfType<HintUIController>();
            if (hintUIController != null) hintUIController.OnHaveAQuestionGestureTriggered();
            return;
        }

        if (gestureType == "ThumbsDown")
        {
            if (hintUIController == null) hintUIController = FindObjectOfType<HintUIController>();
            if (hintUIController != null) hintUIController.OnThumbsDownGestureTriggered();
            return;
        }
    }

    public void ProcessUnprofessionalGesture(string gestureType)
    {
        Debug.Log($"[GestureTracker] Unprofessional gesture received. isTracking = {isTracking}, warningPanelManager null? {warningPanelManager == null}");

        if (isTracking)
        {
            unprofessionalGestureCount++;
            Debug.LogWarning($"[GestureTracker] Flagged unprofessional gesture: {gestureType}");
        }

        if (warningPanelManager != null)
            warningPanelManager.ShowWarning("Please maintain appropriate hand posture.");
    }

    public int GetUnprofessionalCount() => unprofessionalGestureCount;

    public (int expressiveCount, float gpm) CalculateGestureMetrics(float sessionDurationSeconds)
    {
        int expressiveCount = validExpressiveGestures.Count;
        float durationMinutes = sessionDurationSeconds > 0 ? sessionDurationSeconds / 60.0f : 0.0f;
        float gpm = durationMinutes > 0 ? expressiveCount / durationMinutes : 0.0f;
        return (expressiveCount, gpm);
    }
}