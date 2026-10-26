using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class GestureTracker : MonoBehaviour
{
    [Header("Hint UI References")]
    [SerializeField] private GameObject hintCanvas;
    [SerializeField] private TextMeshProUGUI hintText;
    [SerializeField] private float hintDisplayDuration = 5.0f;

    [Header("Manager Reference")]
    [SerializeField] private InteractionManager interactionManager;

    [Header("Unprofessional Gesture Tracking")]
    private int unprofessionalGestureCount = 0;

    private List<Tuple<string, float>> validExpressiveGestures = new List<Tuple<string, float>>();
    private float activeTrackingDurationSeconds = 0.0f;
    private bool isTracking = false;
    private Coroutine hideHintCoroutine;

    private void Awake()
    {
        StartTracking();
    }

    private void Start()
    {
        if (hintCanvas != null)
            hintCanvas.SetActive(false);

        // Auto-find InteractionManager if not manually assigned in Inspector
        if (interactionManager == null)
            interactionManager = FindObjectOfType<InteractionManager>();
    }

    private void Update()
    {
        if (isTracking)
        {
            activeTrackingDurationSeconds += Time.deltaTime;
        }
    }

    public void StartTracking()
    {
        ResetTracker();
    }

    public void StopTracking()
    {
        isTracking = false;
    }

    public void ResetTracker()
    {
        validExpressiveGestures.Clear();
        unprofessionalGestureCount = 0; // Reset counter
        activeTrackingDurationSeconds = 0.0f;
        isTracking = true;
        if (hintCanvas != null) hintCanvas.SetActive(false);
    }

    // -------------------------------------------------------------
    // UnityEvent Entry Point (for XR Hand Tracking Events inspector)
    // -------------------------------------------------------------
    public void TriggerOpenPalmGesture()
    {
        ProcessGestureEvent("Open Palm", 1.2f, null);
    }

    public void ProcessGestureEvent(string gestureType, float gestureDuration, Action onTriggerHintCallback = null)
    {
        if (!isTracking) return;

        // Ignore standard VR environment interactions (Algorithm 6)
        if (gestureType == "Pinch" || gestureType == "Point + Pinch" || gestureType == "Grab" || gestureType == "Handshake")
        {
            return;
        }

        if (gestureType == "Open Palm")
        {
            if (gestureDuration > 1.0f)
            {
                // Algorithm 7: Hint Activation (> 1.0s)
                Debug.Log("[GestureTracker] Open Palm > 1.0s detected -> Requesting Dynamic LLM Hint.");

                if (interactionManager != null)
                {
                    interactionManager.TriggerHintActivation();
                }
                else
                {
                    Debug.LogWarning("[GestureTracker] InteractionManager reference missing!");
                }

                onTriggerHintCallback?.Invoke();
                return;
            }
            else
            {
                validExpressiveGestures.Add(new Tuple<string, float>(gestureType, gestureDuration));
            }
        }
        else
        {
            validExpressiveGestures.Add(new Tuple<string, float>(gestureType, gestureDuration));
        }
    }

    /// <summary>
    /// Triggered via XR Hand Tracking Events when an unprofessional pose is detected.
    /// </summary>
    public void ProcessUnprofessionalGesture(string gestureType)
    {
        if (!isTracking) return;

        unprofessionalGestureCount++;
        Debug.LogWarning($"[GestureTracker] Flagged unprofessional gesture: {gestureType} (Total: {unprofessionalGestureCount})");

        // Optional: Show immediate soft guidance on hand canvas
        ShowHint("Please maintain appropriate hand posture.");
    }

    public int GetUnprofessionalCount()
    {
        return unprofessionalGestureCount;
    }

    // -------------------------------------------------------------
    // Metrics & Analytics Functions (Required by InteractionManager)
    // -------------------------------------------------------------
    public (int expressiveCount, float gpm) CalculateGestureMetrics(float sessionDurationSeconds)
    {
        int expressiveCount = validExpressiveGestures.Count;
        float durationMinutes = sessionDurationSeconds > 0 ? sessionDurationSeconds / 60.0f : 0.0f;
        float gpm = durationMinutes > 0 ? expressiveCount / durationMinutes : 0.0f;

        return (expressiveCount, gpm);
    }

    // -------------------------------------------------------------
    // VR Hand Canvas UI Control
    // -------------------------------------------------------------
    public void ShowHint(string message)
    {
        if (hintCanvas != null)
        {
            if (hintText != null) hintText.text = message;

            hintCanvas.SetActive(true);

            if (hideHintCoroutine != null) StopCoroutine(hideHintCoroutine);
            hideHintCoroutine = StartCoroutine(HideHintAfterDelay());
        }
    }

    private IEnumerator HideHintAfterDelay()
    {
        yield return new WaitForSeconds(hintDisplayDuration);
        if (hintCanvas != null)
        {
            hintCanvas.SetActive(false);
        }
    }
}