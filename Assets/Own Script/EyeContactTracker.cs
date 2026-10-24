using System.Collections;
using UnityEngine;

public class EyeContactTracker : MonoBehaviour
{
    [Header("VR Tracking References")]
    [Tooltip("Transform of the VR Headset / Main Camera")]
    [SerializeField] private Transform headsetTransform;

    [Tooltip("Facial Collider attached to the target NPC head/face")]
    [SerializeField] private Collider avatarFacialCollider;

    [Header("Algorithm Parameters")]
    [Tooltip("Sampling interval in seconds (Default: 0.1s)")]
    [SerializeField] private float samplingInterval = 0.1f;

    [Tooltip("Maximum raycast distance in meters (Default: 5.0m)")]
    [SerializeField] private float maxDistance = 5.0f;

    private int totalSamples = 0;
    private int hitSamples = 0;
    private bool isTracking = false;
    private Coroutine trackingCoroutine;

    // Call when starting a VR session
    public void StartTracking()
    {
        totalSamples = 0;
        hitSamples = 0;
        isTracking = true;

        if (trackingCoroutine != null)
        {
            StopCoroutine(trackingCoroutine);
        }
        trackingCoroutine = StartCoroutine(TrackGazeRoutine());
        Debug.Log("[EyeContactTracker] Gaze sampling started.");
    }


    // Call when ending a VR session
    public void StopTracking()
    {
        isTracking = false;
        if (trackingCoroutine != null)
        {
            StopCoroutine(trackingCoroutine);
            trackingCoroutine = null;
        }
        Debug.Log($"[EyeContactTracker] Sampling stopped. Total: {totalSamples}, Hits: {hitSamples}");
    }

    // ALGORITHM 1: Eye Contact Calculation Sampling Routine
    private IEnumerator TrackGazeRoutine()
    {
        while (isTracking)
        {
            if (headsetTransform != null && avatarFacialCollider != null)
            {
                // 1. Ray gazeRay = Ray(HeadsetPose.Position, HeadsetPose.Forward)
                Ray gazeRay = new Ray(headsetTransform.position, headsetTransform.forward);

                // 2. Raycast with maxDistance = 5.0m
                if (Physics.Raycast(gazeRay, out RaycastHit hitInfo, maxDistance))
                {
                    // 3. IF hitInfo.collider == AvatarFacialCollider THEN hitSamples++
                    if (hitInfo.collider == avatarFacialCollider)
                    {
                        hitSamples++;
                    }
                }

                totalSamples++;
            }

            // 4. Wait for SamplingInterval (0.1s)
            yield return new WaitForSeconds(samplingInterval);
        }
    }

    // Calculates and returns the final score (0.0 to 100.0)
    public float CalculateEyeContactScore()
    {
        if (totalSamples > 0)
        {
            float score = ((float)hitSamples / totalSamples) * 100.0f;
            return Mathf.Clamp(score, 0.0f, 100.0f);
        }
        return 0.0f;
    }

    // Allows dynamic assignment of NPC collider if switching characters
    public void SetTargetFacialCollider(Collider newFacialCollider)
    {
        avatarFacialCollider = newFacialCollider;
    }
}