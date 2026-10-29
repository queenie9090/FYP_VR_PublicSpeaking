using UnityEngine;
using TMPro;

public class ObjectiveManager : MonoBehaviour
{
    [Header("UI Text References (Hand & Controller HUD)")]
    [SerializeField] private GameObject objectivePanelHand;
    [SerializeField] private TMP_Text objectiveTextHand;

    [SerializeField] private GameObject objectivePanelController;
    [SerializeField] private TMP_Text objectiveTextController;

    [Header("World Space Waypoints")]
    [Tooltip("Waypoint active at start directing user to the NPC/Briefing area")]
    [SerializeField] private GameObject startWaypoint;

    [Tooltip("Waypoint active after conversation directing user to the Exit Door")]
    [SerializeField] private GameObject exitDoorWaypoint;

    private void Start()
    {
        // 1. Enable Start Waypoint and disable Exit Waypoint at launch
        if (startWaypoint != null) startWaypoint.SetActive(true);
        if (exitDoorWaypoint != null) exitDoorWaypoint.SetActive(false);

        SetObjective("Walk to the NPC to trigger dialogue");
    }

    /// <summary>
    /// Updates objective text on both Hand and Controller HUD panels simultaneously.
    /// </summary>
    public void SetObjective(string text)
    {
        SetPanelActive(true);

        if (objectiveTextHand != null) objectiveTextHand.text = text;
        if (objectiveTextController != null) objectiveTextController.text = text;
    }

    /// <summary>
    /// Shows or hides objective UI panels on both Hand & Controller UI.
    /// </summary>
    public void SetPanelActive(bool active)
    {
        if (objectivePanelHand != null) objectivePanelHand.SetActive(active);
        if (objectivePanelController != null) objectivePanelController.SetActive(active);
    }

    /// <summary>
    /// Call when dialogue starts to hide start waypoint & HUD banners during conversation.
    /// </summary>
    public void OnConversationStarted()
    {
        if (startWaypoint != null) startWaypoint.SetActive(false);
        SetPanelActive(false);
    }

    /// <summary>
    /// Call when the speech session ends to direct player to the exit door.
    /// </summary>
    public void ShowExitObjective(bool isQuickExitEnabled)
    {
        // Start waypoint stays off
        if (startWaypoint != null) startWaypoint.SetActive(false);

        if (isQuickExitEnabled)
        {
            SetObjective("Session Completed!\nClick 'Exit Session' on your UI.");
            if (exitDoorWaypoint != null) exitDoorWaypoint.SetActive(false);
        }
        else
        {
            SetObjective("Session Completed!\nWalk to the Exit Door waypoint to leave.");
            if (exitDoorWaypoint != null) exitDoorWaypoint.SetActive(true);
        }
    }

    /// <summary>
    /// Hide UI and waypoints completely upon scene transition.
    /// </summary>
    public void ClearObjective()
    {
        SetPanelActive(false);
        if (startWaypoint != null) startWaypoint.SetActive(false);
        if (exitDoorWaypoint != null) exitDoorWaypoint.SetActive(false);
    }
}