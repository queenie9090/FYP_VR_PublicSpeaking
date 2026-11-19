using System.Collections;
using UnityEngine;
using System.Collections.Generic;


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
    private GameObject activeEyeContactIcon;

    private int totalSamples = 0;
    private int hitSamples = 0;
    private bool isTracking = false;
    private Coroutine trackingCoroutine;

    private readonly List<NPCPersona> targetNPCs = new List<NPCPersona>();

    public void SetTargetNPCs(IEnumerable<NPCPersona> npcs)
    {
        HideAllIcons();
        targetNPCs.Clear();
        if (npcs == null) return;
        foreach (var npc in npcs)
        {
            if (npc != null && !targetNPCs.Contains(npc))
                targetNPCs.Add(npc);
        }
    }

    private void HideAllIcons()
    {
        if (activeEyeContactIcon != null)
            activeEyeContactIcon.SetActive(false);

        foreach (var npc in targetNPCs)
        {
            if (npc != null && npc.eyeContactIcon != null)
                npc.eyeContactIcon.SetActive(false);
        }
    }

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
    // Call when ending a VR session
    public void StopTracking()
    {
        isTracking = false;
        if (trackingCoroutine != null)
        {
            StopCoroutine(trackingCoroutine);
            trackingCoroutine = null;
        }

        HideAllIcons();

        Debug.Log($"[EyeContactTracker] Sampling stopped. Total: {totalSamples}, Hits: {hitSamples}");
    }

    // ALGORITHM 1: Eye Contact Calculation Sampling Routine
    private IEnumerator TrackGazeRoutine()
    {
        while (isTracking)
        {
            bool isLookingAtNPC = false;
            NPCPersona lookedAtNPC = null;

            if (headsetTransform != null && (targetNPCs.Count > 0 || avatarFacialCollider != null))
            {
                Ray gazeRay = new Ray(headsetTransform.position, headsetTransform.forward);
                Debug.DrawRay(gazeRay.origin, gazeRay.direction * maxDistance, Color.red, samplingInterval);

                if (Physics.Raycast(gazeRay, out RaycastHit hitInfo, maxDistance))
                {
                    Debug.Log($"[GazeDebug] targetCount={targetNPCs.Count} | hit={hitInfo.collider.name} | dist={hitInfo.distance:F2}m");
                    if (targetNPCs.Count > 0)
                    {
                        foreach (var npc in targetNPCs)
                        {
                            if (npc != null && npc.facialCollider != null && hitInfo.collider == npc.facialCollider)
                            {
                                isLookingAtNPC = true;
                                lookedAtNPC = npc;
                                break;
                            }
                        }
                    }
                    else
                    {
                        isLookingAtNPC = hitInfo.collider == avatarFacialCollider;
                    }

                    if (isLookingAtNPC) hitSamples++;
                }

                totalSamples++;
            }

            if (targetNPCs.Count > 0)
            {
                foreach (var npc in targetNPCs)
                {
                    if (npc == null || npc.eyeContactIcon == null) continue;
                    bool shouldShow = npc == lookedAtNPC;
                    if (npc.eyeContactIcon.activeSelf != shouldShow)
                        npc.eyeContactIcon.SetActive(shouldShow);
                }
            }
            else if (activeEyeContactIcon != null && activeEyeContactIcon.activeSelf != isLookingAtNPC)
            {
                activeEyeContactIcon.SetActive(isLookingAtNPC);
            }

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

    public void SetEyeContactIcon(GameObject icon)
    {
        if (activeEyeContactIcon != null && activeEyeContactIcon != icon)
            activeEyeContactIcon.SetActive(false);

        activeEyeContactIcon = icon;
        if (activeEyeContactIcon != null)
            activeEyeContactIcon.SetActive(false);
    }

}