using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GestureTracker : MonoBehaviour
{
    [Header("Manager References")]
    [SerializeField] private InteractionManager interactionManager;
    [SerializeField] private HandTrackingTutorialManager tutorialManager;
    [SerializeField] private HintManager hintManager; // Reference to Central Hint Manager

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
            tutorialManager = FindObjectOfType<HandTrackingTutorialManager>();

        if (hintManager == null)
            hintManager = FindObjectOfType<HintManager>();
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
    public void TriggerOpenPalmGesture() => ProcessGestureEvent("Open Palm", 1.2f);
    public void TriggerPinchGesture() => ProcessGestureEvent("Pinch", 0.5f);
    public void TriggerPointPinchGesture() => ProcessGestureEvent("Point + Pinch", 0.5f);
    public void TriggerGrabGesture() => ProcessGestureEvent("Grab", 0.5f);
    public void TriggerSocialGreetingGesture() => ProcessGestureEvent("Wave", 1.0f);

    public void ProcessGestureEvent(string gestureType, float gestureDuration)
    {
        if (!isTracking) return;

        // 1. Tutorial Progress
        if (tutorialManager != null)
        {
            switch (gestureType)
            {
                case "Open Palm":
                    if (gestureDuration > 1.0f) tutorialManager.OnOpenPalmDetected();
                    break;
                case "Pinch": tutorialManager.OnPinchConfirmed(); break;
                case "Point + Pinch": tutorialManager.OnPointPinchTeleportCompleted(); break;
                case "Grab": tutorialManager.OnObjectGrabbed(); break;
                case "Wave":
                case "Handshake": tutorialManager.OnSocialGreetingCompleted(); break;
            }
        }

        // 2. Ignore VR Navigation Gestures for Expressive Metrics
        if (gestureType == "Pinch" || gestureType == "Point + Pinch" || gestureType == "Grab" || gestureType == "Handshake")
        {
            return;
        }

        // 3. Process Open Palm Hint Request
        if (gestureType == "Open Palm" && gestureDuration > 1.0f)
        {
            if (interactionManager != null) interactionManager.TriggerHintActivation();

            // Delegate to Central Manager (which enforces HandTracking mode check)
            if (hintManager != null) hintManager.OnOpenPalmGestureTriggered();
            return;
        }

        validExpressiveGestures.Add(new Tuple<string, float>(gestureType, gestureDuration));
    }

    public void ProcessUnprofessionalGesture(string gestureType)
    {
        if (!isTracking) return;
        unprofessionalGestureCount++;
        Debug.LogWarning($"[GestureTracker] Flagged unprofessional gesture: {gestureType}");

        if (hintManager != null)
        {
            hintManager.ShowDirectMessage("Please maintain appropriate hand posture.");
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