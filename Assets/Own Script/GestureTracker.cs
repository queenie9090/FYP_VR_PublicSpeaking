using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GestureTracker : MonoBehaviour
{
    [Header("Manager References")]
    [SerializeField] private InteractionManager interactionManager;
    [SerializeField] private TutorialPanelManager tutorialManager;
    [SerializeField] private HintManager hintManager;
    [SerializeField] private WarningPanelManager warningPanelManager;

    [Header("Unprofessional Gesture Tracking")]
    private int unprofessionalGestureCount = 0;

    private List<Tuple<string, float>> validExpressiveGestures = new List<Tuple<string, float>>();
    private float activeTrackingDurationSeconds = 0.0f;
    private bool isTracking = false;

    private void Awake()
    {
        StartTracking();
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
    }

    private void Update()
    {
        if (isTracking)
        {
            activeTrackingDurationSeconds += Time.deltaTime;
        }
    }

    public void StartTracking() => ResetTracker();
    public void StopTracking() => isTracking = false;

    public void ResetTracker()
    {
        validExpressiveGestures.Clear();
        unprofessionalGestureCount = 0;
        activeTrackingDurationSeconds = 0.0f;
        isTracking = true;
    }

    // -------------------------------------------------------------
    // UnityEvent Entry Points (for XR Hand Tracking Events)
    // -------------------------------------------------------------

    /// <summary>
    /// Triggered via "Have a Question" gesture (Index extended up). Opens/toggles hint panel.
    /// </summary>
    public void TriggerHaveAQuestionGesture() => ProcessGestureEvent("HaveAQuestion", 0.5f);

    /// <summary>
    /// Triggered via "Thumbs Down" gesture to explicitly close the hint panel.
    /// </summary>
    public void TriggerThumbsDownGesture() => ProcessGestureEvent("ThumbsDown", 0.3f);

    /// <summary>
    /// Triggered via Inappropriate Gesture Detectors (Middle Finger).
    /// </summary>
    public void TriggerUnprofessionalGesture() => ProcessUnprofessionalGesture("Middle Finger");

    public void TriggerPinchGesture() => ProcessGestureEvent("Pinch", 0.5f);
    public void TriggerPointPinchGesture() => ProcessGestureEvent("Point + Pinch", 0.5f);
    public void TriggerGrabGesture() => ProcessGestureEvent("Grab", 0.5f);
    public void TriggerSocialGreetingGesture() => ProcessGestureEvent("Wave", 1.0f);

    public void ProcessGestureEvent(string gestureType, float gestureDuration)
    {
        if (!isTracking)
        {
            Debug.LogWarning("[GestureTracker Debug] ProcessGestureEvent ignored because isTracking == false!");
            return;
        }

        // 1. Log raw gesture input received from Meta Quest / XRI events
        Debug.Log($"<color=cyan>[GestureTracker RAW]</color> Detected Gesture: '{gestureType}' | Duration: {gestureDuration}s");

        // 2. Forward gesture to TutorialPanelManager if active
        if (tutorialManager == null)
            tutorialManager = FindObjectOfType<TutorialPanelManager>();

        if (tutorialManager != null)
        {
            Debug.Log($"<color=yellow>[GestureTracker -> Tutorial]</color> Forwarding '{gestureType}' to TutorialPanelManager...");
            tutorialManager.OnGesturePerformed(gestureType);
        }

        // 3. Route "Have a Question" Gesture to HintManager (Open/Toggle Panel)
        if (gestureType == "HaveAQuestion")
        {
            Debug.Log("<color=green>[GestureTracker -> HintManager]</color> Have A Question Pose Detected! Triggering 3D Hint Panel...");

            if (hintManager == null)
                hintManager = FindObjectOfType<HintManager>();

            if (hintManager != null)
            {
                hintManager.OnHaveAQuestionGestureTriggered();
            }
            else
            {
                Debug.LogError("<color=red>[GestureTracker Error]</color> HintManager reference is MISSING in scene!");
            }
            return;
        }

        // 4. Route "Thumbs Down" Gesture to HintManager (Close Panel)
        if (gestureType == "ThumbsDown")
        {
            Debug.Log("<color=orange>[GestureTracker -> HintManager]</color> Thumbs Down Detected! Hiding Hint Panel...");

            if (hintManager == null)
                hintManager = FindObjectOfType<HintManager>();

            if (hintManager != null)
            {
                hintManager.OnThumbsDownGestureTriggered();
            }
            return;
        }

        // 5. Ignore standard system gestures for expressive scoring
        if (gestureType == "Pinch" || gestureType == "Point + Pinch" || gestureType == "Grab" || gestureType == "Handshake")
        {
            Debug.Log($"[GestureTracker Debug] '{gestureType}' processed for tutorial, ignored for GPM score.");
            return;
        }

        validExpressiveGestures.Add(new Tuple<string, float>(gestureType, gestureDuration));
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