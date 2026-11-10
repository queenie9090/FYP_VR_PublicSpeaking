using UnityEngine;

public class ObjectiveManager : MonoBehaviour
{
    [Header("Start Objective UI (Desk/Waypoint)")]
    [SerializeField] private GameObject startObjectivePanel;

    [Header("World Space Waypoints")]
    [SerializeField] private GameObject startWaypoint;
    [SerializeField] private GameObject exitDoorWaypoint;

    private void Start()
    {
        bool isTutorialActive = UserSession.IsRunningTutorial ||
                                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "TutorialScene";

        if (startWaypoint != null)
            startWaypoint.SetActive(!isTutorialActive);

        if (exitDoorWaypoint != null)
            exitDoorWaypoint.SetActive(false);

        SetPanelActive(!isTutorialActive);
    }

    /// <summary>
    /// Toggles the objective panel canvas visibility.
    /// </summary>
    public void SetPanelActive(bool active)
    {
        if (startObjectivePanel != null)
            startObjectivePanel.SetActive(active);
    }

    public void OnConversationStarted()
    {
        if (startWaypoint != null)
            startWaypoint.SetActive(false);

        SetPanelActive(false);
    }

    /// <summary>
    /// Hides start UI/waypoint and enables the exit door waypoint at scene end.
    /// </summary>
    public void EnableExitWaypointOnly()
    {
        if (startWaypoint != null)
            startWaypoint.SetActive(false);

        SetPanelActive(false);

        if (exitDoorWaypoint != null)
            exitDoorWaypoint.SetActive(true);
    }

    public void ClearObjective()
    {
        SetPanelActive(false);
        if (startWaypoint != null) startWaypoint.SetActive(false);
        if (exitDoorWaypoint != null) exitDoorWaypoint.SetActive(false);
    }

    /// <summary>
    /// Called when the tutorial panel is closed/finished to reveal the main desk waypoint and objective panel.
    /// </summary>
    public void ShowStartWaypointAfterTutorial()
    {
        if (startWaypoint != null)
            startWaypoint.SetActive(true);

        SetPanelActive(true);
    }
}