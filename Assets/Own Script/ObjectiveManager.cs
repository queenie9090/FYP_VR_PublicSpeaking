using UnityEngine;
using TMPro;

public class ObjectiveManager : MonoBehaviour
{
    [Header("Combined Start Objective UI (Desk/Waypoint)")]
    [SerializeField] private GameObject startObjectivePanel;
    [SerializeField] private TMP_Text startObjectiveText;

    [Header("World Space Waypoints")]
    [SerializeField] private GameObject startWaypoint;
    [SerializeField] private GameObject exitDoorWaypoint;

    private void Start()
    {
        if (startWaypoint != null) startWaypoint.SetActive(true);
        if (exitDoorWaypoint != null) exitDoorWaypoint.SetActive(false);

        SetPanelActive(false);
    }

    public void SetObjective(string text)
    {
        if (startObjectiveText != null)
            startObjectiveText.text = text;
    }

    public void SetPanelActive(bool active)
    {
        if (startObjectivePanel != null)
            startObjectivePanel.SetActive(active);
    }

    public void DisplayScenarioObjective(ScenarioConfig config)
    {
        if (startObjectiveText == null) return;

        if (config == null)
        {
            SetObjective("<color=#FFCC00><b>OBJECTIVE:</b></color> Click '<color=#55FF55>Start Conversation</color>' to begin.");
            return;
        }

        string persona = string.IsNullOrEmpty(config.persona) ? "Evaluator" : config.persona;
        string rawScenario = string.IsNullOrEmpty(config.scenario) ? "Simulation" : config.scenario.Replace("Scene", "").Trim();
        string topic = string.IsNullOrEmpty(config.topic) ? "General Discussion" : config.topic;
        string role = string.IsNullOrEmpty(config.participantRole) ? "Participant" : config.participantRole;

        bool isTutorial = UserSession.IsRunningTutorial || rawScenario == "Tutorial";

        if (isTutorial)
        {
            startObjectiveText.text =
                "<size=120%><color=#00E5FF><b>SYSTEM CALIBRATION</b></color></size>\n\n" +
                "<color=#FFCC00><b>Task:</b></color> Test mic by introducing yourself and responding to <color=#55FF55><b>" + persona + "</b></color>.\n\n" +
                "<color=#FF9900><b>QUICK CONTROLS:</b></color>\n" +
                "<color=#AAAAAA>Look at " + persona + "  •  Open Palm = AI Hint  •  Silence is OK</color>";
            return;
        }

        string dynamicTask = BuildDynamicTaskString(config.sessionObjective, role, persona, topic);

        startObjectiveText.text =
            "<size=120%><color=#00E5FF><b>" + rawScenario.ToUpper() + " MISSION</b></color></size>\n\n" +
            "<color=#FFCC00><b>Goal:</b></color> " + dynamicTask + "\n\n" +
            "<color=#FF9900><b>EVALUATION TIPS:</b></color>\n" +
            "<color=#AAAAAA>Steady Eye Contact  •  Open Palm = AI Hint  •  Natural Speaking Pace</color>";
    }

    private string BuildDynamicTaskString(string rawObjective, string role, string persona, string topic)
    {
        if (!string.IsNullOrEmpty(rawObjective) && rawObjective.Length < 90 && !rawObjective.Contains("Guide the user"))
        {
            return rawObjective;
        }

        if (!string.IsNullOrEmpty(topic) && topic != "General Discussion")
        {
            return "Act as <color=#55FF55><b>" + role + "</b></color> and discuss '<color=#FFFF55>" + topic + "</color>' with <color=#55FF55><b>" + persona + "</b></color>.";
        }

        return "Respond clearly to <color=#55FF55><b>" + persona + "</b></color> and fulfill your role as <color=#55FF55><b>" + role + "</b></color>.";
    }

    public void OnConversationStarted()
    {
        if (startWaypoint != null) startWaypoint.SetActive(false);
        SetPanelActive(false);
    }

    /// <summary>
    /// Simply hides the start UI/waypoint and enables the exit door waypoint.
    /// </summary>
    public void EnableExitWaypointOnly()
    {
        if (startWaypoint != null) startWaypoint.SetActive(false);
        SetPanelActive(false);

        if (exitDoorWaypoint != null) exitDoorWaypoint.SetActive(true);
    }

    public void ClearObjective()
    {
        SetPanelActive(false);
        if (startWaypoint != null) startWaypoint.SetActive(false);
        if (exitDoorWaypoint != null) exitDoorWaypoint.SetActive(false);
    }
}