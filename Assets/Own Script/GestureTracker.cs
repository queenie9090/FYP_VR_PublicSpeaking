using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GestureTracker : MonoBehaviour
{
    private List<Tuple<string, float>> validExpressiveGestures = new List<Tuple<string, float>>();
    private float activeTrackingDurationSeconds = 0.0f;
    private bool isTracking = false;

    private void Update()
    {
        if (isTracking)
        {
            activeTrackingDurationSeconds += Time.deltaTime;
        }
    }

    // Called when the VR interaction session starts or resets
    public void StartTracking()
    {
        ResetTracker();
    }

    public void ResetTracker()
    {
        validExpressiveGestures.Clear();
        activeTrackingDurationSeconds = 0.0f;
        isTracking = true;
        Debug.Log("[GestureTracker] Gesture tracking started/reset.");
    }

    // Called when the VR session ends
    public void StopTracking()
    {
        isTracking = false;
        Debug.Log($"[GestureTracker] Tracking stopped. Expressive Gestures recorded: {validExpressiveGestures.Count}");
    }

    /// <summary>
    /// Processes gesture events detected by the Meta Quest hand tracking SDK.
    /// ALGORITHM 6 & ALGORITHM 7 Implementation.
    /// </summary>
    public void ProcessGestureEvent(string gestureType, float gestureDuration, Action onTriggerHintCallback)
    {
        if (!isTracking) return;

        // 1. Exclude VR interaction gestures (Algorithm 6: Lines 4-5)
        if (gestureType == "Pinch" || gestureType == "Point + Pinch" || gestureType == "Grab" || gestureType == "Handshake")
        {
            return; // Excluded from movement analysis
        }

        // 2. Open Palm classification rule (Algorithm 6: Lines 6-13 / Algorithm 7: Lines 3-12)
        if (gestureType == "Open Palm")
        {
            if (gestureDuration > 1.0f)
            {
                // ALGORITHM 7: Classified as Hint Activation (> 1.0s)
                Debug.Log("[GestureTracker] Open Palm > 1.0s detected -> Triggering Hint Activation.");
                onTriggerHintCallback?.Invoke();
                return; // Excluded from expressive movement count
            }
            else
            {
                // Classified as Expressive Communication Gesture (<= 1.0s)
                validExpressiveGestures.Add(new Tuple<string, float>(gestureType, gestureDuration));
            }
        }
        else
        {
            // 3. Other recognized expressive communication gestures
            validExpressiveGestures.Add(new Tuple<string, float>(gestureType, gestureDuration));
        }
    }

    /// <summary>
    /// ALGORITHM 6: Computes total expressive count and Gestures Per Minute (GPM).
    /// </summary>
    public (int expressiveCount, float gpm) CalculateGestureMetrics(float overrideDurationSeconds = -1.0f)
    {
        int expressiveCount = validExpressiveGestures.Count;

        // Use override session duration if provided, otherwise use tracked active duration
        float durationSec = overrideDurationSeconds > 0.0f ? overrideDurationSeconds : activeTrackingDurationSeconds;
        float simulationDurationMinutes = durationSec / 60.0f;

        float gpm = 0.0f;
        if (simulationDurationMinutes > 0.0f)
        {
            gpm = expressiveCount / simulationDurationMinutes;
        }

        return (expressiveCount, gpm);
    }
}