using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks gestures for the session.
///  - SYSTEM gestures (hint open/close, pinch, grab, unprofessional) still arrive as pose events
///    through the Trigger... methods (wired from Static Hand Gesture UnityEvents). They are NOT scored.
///  - EXPRESSIVE gestures are detected from hand / controller MOVEMENT (speed relative to the head)
///    and are the only thing counted for GPM. The same logic is used for both input modes.
/// </summary>
public class GestureTracker : MonoBehaviour
{
    [Header("Manager References")]
    [SerializeField] private InteractionManager interactionManager;
    [SerializeField] private TutorialPanelManager tutorialManager;
    [SerializeField] private HintManager hintManager;
    [SerializeField] private WarningPanelManager warningPanelManager;

    [Header("Motion Sources (Hand Tracking)")]
    [Tooltip("A transform that really MOVES with the tracked hand (wrist / palm joint). Check it moves in the Scene view.")]
    [SerializeField] private Transform leftHandTransform;
    [SerializeField] private Transform rightHandTransform;

    [Header("Motion Sources (Controllers)")]
    [SerializeField] private Transform leftControllerTransform;
    [SerializeField] private Transform rightControllerTransform;

    [Header("Reference Frames")]
    [Tooltip("XR Origin transform. Measuring in its space ignores teleporting, snap turning and walking.")]
    [SerializeField] private Transform trackingSpace;
    [Tooltip("Main Camera / CenterEyeAnchor")]
    [SerializeField] private Transform headset;

    [Header("Motion Thresholds (system-defined parameters)")]
    [SerializeField] private float minSpeed = 0.5f;          // m/s (smoothed) to start counting as movement
    [SerializeField] private float minDuration = 0.25f;      // seconds of sustained movement
    [SerializeField] private float minDistance = 0.15f;      // metres travelled during the movement
    [SerializeField] private float gestureCooldown = 1.0f;   // seconds between counted gestures
    [SerializeField] private float maxPlausibleSpeed = 6.0f; // m/s; faster frames are tracking glitches
    [SerializeField] private float systemGestureSuppression = 1.5f; // seconds ignored after a system gesture

    [Header("Unprofessional Gesture Tracking")]
    private int unprofessionalGestureCount = 0;

    private class MotionState
    {
        public Vector3 lastPos;
        public bool initialized;
        public bool moving;
        public float smoothedSpeed;
        public float moveTime;
        public float distance;

        public void Reset()
        {
            initialized = false;
            moving = false;
            smoothedSpeed = 0f;
            moveTime = 0f;
            distance = 0f;
        }
    }

    private readonly MotionState leftState = new MotionState();
    private readonly MotionState rightState = new MotionState();
    private Transform leftSource;
    private Transform rightSource;
    private string motionLabel = "Hand Motion";
    private float cooldownUntil = 0f;
    private float suppressUntil = 0f;

    private List<Tuple<string, float>> validExpressiveGestures = new List<Tuple<string, float>>();
    private float activeTrackingDurationSeconds = 0.0f;
    private bool isTracking = false;

    private void Awake()
    {
        isTracking = false;
    }

    private void Start()
    {
        if (interactionManager == null)
            interactionManager = FindObjectOfType<InteractionManager>();

        if (tutorialManager == null)
            tutorialManager = FindObjectOfType<TutorialPanelManager>();

        if (hintManager == null)
            hintManager = FindObjectOfType<HintManager>();

        if (warningPanelManager == null)
            warningPanelManager = FindObjectOfType<WarningPanelManager>();

        if (headset == null && Camera.main != null)
            headset = Camera.main.transform;

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

        TrackMotion(leftSource, leftState);
        TrackMotion(rightSource, rightState);
    }

    public void StartTracking() => ResetTracker();
    public void StopTracking() => isTracking = false;

    public void ResetTracker()
    {
        validExpressiveGestures.Clear();
        unprofessionalGestureCount = 0;
        activeTrackingDurationSeconds = 0.0f;
        cooldownUntil = 0f;
        suppressUntil = 0f;
        leftState.Reset();
        rightState.Reset();
        ResolveMotionSources();
        isTracking = true;
    }

    /// <summary>Choose hand or controller transforms based on the selected input mode.</summary>
    private void ResolveMotionSources()
    {
        string mode = UserSession.InputMode;
        bool useHands = !string.IsNullOrEmpty(mode) && mode.Contains("Hand");

        leftSource = useHands ? leftHandTransform : leftControllerTransform;
        rightSource = useHands ? rightHandTransform : rightControllerTransform;
        motionLabel = useHands ? "Hand Motion" : "Controller Motion";
    }

    // -------------------------------------------------------------
    // Motion-based expressive gesture detection
    // -------------------------------------------------------------
    private void TrackMotion(Transform source, MotionState s)
    {
        if (source == null || headset == null || trackingSpace == null) return;

        // Hand / controller object is switched off in the other input mode, or tracking is lost
        if (!source.gameObject.activeInHierarchy)
        {
            s.Reset();
            return;
        }

        // Ignore motion right after a system gesture (hint, pinch, grab, etc.)
        if (Time.time < suppressUntil)
        {
            s.Reset();
            return;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Position relative to the head, measured in tracking space:
        // teleport, snap turn and walking don't create fake hand movement.
        Vector3 pos = trackingSpace.InverseTransformPoint(source.position)
                    - trackingSpace.InverseTransformPoint(headset.position);

        if (!s.initialized)
        {
            s.lastPos = pos;
            s.initialized = true;
            return;
        }

        float step = (pos - s.lastPos).magnitude;
        float rawSpeed = step / dt;
        s.lastPos = pos;

        // Tracking glitch (e.g. hand re-acquired): skip this frame
        if (rawSpeed > maxPlausibleSpeed)
        {
            s.moving = false;
            s.moveTime = 0f;
            s.distance = 0f;
            return;
        }

        // Light smoothing so single-frame jitter does not count
        s.smoothedSpeed = Mathf.Lerp(s.smoothedSpeed, rawSpeed, 0.3f);

        float startSpeed = minSpeed;
        float stopSpeed = minSpeed * 0.6f; // hysteresis so one movement isn't split in pieces

        bool active = s.moving ? s.smoothedSpeed >= stopSpeed : s.smoothedSpeed >= startSpeed;

        if (active)
        {
            s.moving = true;
            s.moveTime += dt;
            s.distance += step;
        }
        else if (s.moving)
        {
            // Movement just ended: was it a real gesture?
            if (s.moveTime >= minDuration && s.distance >= minDistance && Time.time >= cooldownUntil)
            {
                RegisterExpressiveMotion(s.moveTime);
            }

            s.moving = false;
            s.moveTime = 0f;
            s.distance = 0f;
        }
    }

    private void RegisterExpressiveMotion(float duration)
    {
        validExpressiveGestures.Add(new Tuple<string, float>(motionLabel, duration));
        cooldownUntil = Time.time + gestureCooldown;
        Debug.Log($"<color=cyan>[GestureTracker Motion]</color> {motionLabel} counted | Duration: {duration:F2}s | Total: {validExpressiveGestures.Count}");
    }

    // -------------------------------------------------------------
    // UnityEvent Entry Points (pose events from XR Hand Tracking)
    // These are SYSTEM gestures: forwarded to tutorial / hint, never scored.
    // -------------------------------------------------------------

    /// <summary>"Have a Question" gesture (index up). Opens/toggles the hint panel.</summary>
    public void TriggerHaveAQuestionGesture() => ProcessGestureEvent("HaveAQuestion", 0.5f);

    /// <summary>"Thumbs Down" gesture. Closes the hint panel.</summary>
    public void TriggerThumbsDownGesture() => ProcessGestureEvent("ThumbsDown", 0.3f);

    /// <summary>Inappropriate gesture detectors (middle finger).</summary>
    public void TriggerUnprofessionalGesture() => ProcessUnprofessionalGesture("Middle Finger");

    public void TriggerPinchGesture() => ProcessGestureEvent("Pinch", 0.5f);
    public void TriggerPointPinchGesture() => ProcessGestureEvent("Point + Pinch", 0.5f);
    public void TriggerGrabGesture() => ProcessGestureEvent("Grab", 0.5f);
    //public void TriggerSocialGreetingGesture() => ProcessGestureEvent("Wave", 1.0f);

    public void ProcessGestureEvent(string gestureType, float gestureDuration)
    {
        if (!isTracking)
        {
            Debug.LogWarning("[GestureTracker Debug] ProcessGestureEvent ignored because isTracking == false!");
            return;
        }

        Debug.Log($"<color=cyan>[GestureTracker RAW]</color> Detected Gesture: '{gestureType}' | Duration: {gestureDuration}s");

        // Any system gesture briefly pauses motion counting (the hand is busy interacting)
        suppressUntil = Time.time + systemGestureSuppression;

        // 1. Forward to TutorialPanelManager if active
        if (tutorialManager == null)
            tutorialManager = FindObjectOfType<TutorialPanelManager>();

        if (tutorialManager != null)
        {
            Debug.Log($"<color=yellow>[GestureTracker -> Tutorial]</color> Forwarding '{gestureType}' to TutorialPanelManager...");
            tutorialManager.OnGesturePerformed(gestureType);
        }

        // 2. "Have a Question" -> open/toggle hint panel
        if (gestureType == "HaveAQuestion")
        {
            Debug.Log("<color=green>[GestureTracker -> HintManager]</color> Have A Question detected. Triggering hint panel...");

            if (hintManager == null)
                hintManager = FindObjectOfType<HintManager>();

            if (hintManager != null)
                hintManager.OnHaveAQuestionGestureTriggered();
            else
                Debug.LogError("<color=red>[GestureTracker Error]</color> HintManager reference is MISSING in scene!");
            return;
        }

        // 3. "Thumbs Down" -> close hint panel
        if (gestureType == "ThumbsDown")
        {
            Debug.Log("<color=orange>[GestureTracker -> HintManager]</color> Thumbs Down detected. Hiding hint panel...");

            if (hintManager == null)
                hintManager = FindObjectOfType<HintManager>();

            if (hintManager != null)
                hintManager.OnThumbsDownGestureTriggered();
            return;
        }

        // 4. Everything else is a system / pose event: not scored.
        Debug.Log($"[GestureTracker Debug] '{gestureType}' processed for tutorial, not scored (expressive gestures are motion-based).");
    }

    public void ProcessUnprofessionalGesture(string gestureType)
    {
        if (!isTracking) return;
        unprofessionalGestureCount++;
        Debug.LogWarning($"[GestureTracker] Flagged unprofessional gesture: {gestureType}");

        if (warningPanelManager != null)
        {
            warningPanelManager.ShowWarning("Please maintain appropriate hand posture.");
        }
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


//using System;
//using System.Collections;
//using System.Collections.Generic;
//using UnityEngine;

//public class GestureTracker : MonoBehaviour
//{
//    [Header("Manager References")]
//    [SerializeField] private InteractionManager interactionManager;
//    [SerializeField] private TutorialPanelManager tutorialManager;
//    [SerializeField] private HintManager hintManager;

//    [Header("Unprofessional Gesture Tracking")]
//    private int unprofessionalGestureCount = 0;

//    private List<Tuple<string, float>> validExpressiveGestures = new List<Tuple<string, float>>();
//    private float activeTrackingDurationSeconds = 0.0f;
//    private bool isTracking = false;

//    private void Awake()
//    {
//        StartTracking();
//    }

//    private void Start()
//    {
//        if (interactionManager == null)
//            interactionManager = FindObjectOfType<InteractionManager>();

//        if (tutorialManager == null)
//            tutorialManager = FindObjectOfType<TutorialPanelManager>();

//        if (hintManager == null)
//            hintManager = FindObjectOfType<HintManager>();
//    }

//    private void Update()
//    {
//        if (isTracking)
//        {
//            activeTrackingDurationSeconds += Time.deltaTime;
//        }
//    }

//    public void StartTracking() => ResetTracker();
//    public void StopTracking() => isTracking = false;

//    public void ResetTracker()
//    {
//        validExpressiveGestures.Clear();
//        unprofessionalGestureCount = 0;
//        activeTrackingDurationSeconds = 0.0f;
//        isTracking = true;
//    }

//    // -------------------------------------------------------------
//    // UnityEvent Entry Points (for XR Hand Tracking Events)
//    // -------------------------------------------------------------
//    public void TriggerOpenPalmGesture() => ProcessGestureEvent("Open Palm", 1.0f);
//    public void TriggerPinchGesture() => ProcessGestureEvent("Pinch", 0.5f);
//    public void TriggerPointPinchGesture() => ProcessGestureEvent("Point + Pinch", 0.5f);
//    public void TriggerGrabGesture() => ProcessGestureEvent("Grab", 0.5f);
//    public void TriggerSocialGreetingGesture() => ProcessGestureEvent("Wave", 1.0f);

//    public void ProcessGestureEvent(string gestureType, float gestureDuration)
//    {
//        if (!isTracking)
//        {
//            Debug.LogWarning("[GestureTracker Debug] ProcessGestureEvent ignored because isTracking == false!");
//            return;
//        }

//        // 1. Log raw gesture input received from Meta Quest / XRI events
//        Debug.Log($"<color=cyan>[GestureTracker RAW]</color> Detected Gesture: '{gestureType}' | Duration: {gestureDuration}s");

//        // 2. Ensure TutorialPanelManager reference exists and forward the event
//        if (tutorialManager == null)
//            tutorialManager = FindObjectOfType<TutorialPanelManager>();

//        if (tutorialManager != null)
//        {
//            Debug.Log($"<color=yellow>[GestureTracker -> Tutorial]</color> Forwarding '{gestureType}' to TutorialPanelManager...");
//            tutorialManager.OnGesturePerformed(gestureType);
//        }
//        else
//        {
//            Debug.LogError("<color=red>[GestureTracker Error]</color> TutorialPanelManager is NULL! Cannot advance tutorial step.");
//        }

//        // 3. Handle Open Palm Hint Request
//        if (gestureType == "Open Palm" && gestureDuration >= 1.0f)
//        {
//            if (hintManager == null)
//                hintManager = FindObjectOfType<HintManager>();

//            if (hintManager != null)
//            {
//                Debug.Log("<color=green>[GestureTracker -> Hint]</color> Triggering Open Palm Hint Activation.");
//                hintManager.OnOpenPalmGestureTriggered();
//            }
//            return;
//        }

//        // 4. Ignore navigation gestures for expressive metric scoring
//        if (gestureType == "Pinch" || gestureType == "Point + Pinch" || gestureType == "Grab" || gestureType == "Handshake")
//        {
//            Debug.Log($"[GestureTracker Debug] '{gestureType}' processed for tutorial, ignored for GPM score.");
//            return;
//        }

//        validExpressiveGestures.Add(new Tuple<string, float>(gestureType, gestureDuration));
//    }

//    public void ProcessUnprofessionalGesture(string gestureType)
//    {
//        if (!isTracking) return;
//        unprofessionalGestureCount++;
//        Debug.LogWarning($"[GestureTracker] Flagged unprofessional gesture: {gestureType}");

//        if (hintManager != null)
//        {
//            hintManager.ShowDirectMessage("Please maintain appropriate hand posture.");
//        }
//    }

//    public int GetUnprofessionalCount() => unprofessionalGestureCount;

//    public (int expressiveCount, float gpm) CalculateGestureMetrics(float sessionDurationSeconds)
//    {
//        int expressiveCount = validExpressiveGestures.Count;
//        float durationMinutes = sessionDurationSeconds > 0 ? sessionDurationSeconds / 60.0f : 0.0f;
//        float gpm = durationMinutes > 0 ? expressiveCount / durationMinutes : 0.0f;
//        return (expressiveCount, gpm);
//    }

//    // Add this method inside GestureTracker.cs if it's missing
//    public void TriggerOpenPalmEnded()
//    {
//        if (hintManager != null)
//        {
//            hintManager.HideHintCanvas();
//        }
//    }
//}