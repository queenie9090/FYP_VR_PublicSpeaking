using UnityEngine;
using TMPro;

public class ScenarioBriefingUI : MonoBehaviour
{
    [Header("UI Reference")]
    [Tooltip("Assign the single TMP_Text component placed inside your Scroll View's Content object")]
    [SerializeField] private TMP_Text fullBriefingText;

    public void SetupBriefing(ScenarioConfig config)
    {
        if (config == null || fullBriefingText == null) return;

        // Clean, readable formatting with clear visual hierarchy
        fullBriefingText.text =
            $"<b><size=120%>{CleanScenarioName(config.scenario)}</size></b>\n" +
            $"<color=#AAAAAA>Role: {config.participantRole}  |  Difficulty: {config.difficulty}</color>\n\n" +

            $"<b>TARGET OBJECTIVE</b>\n" +
            $"{config.sessionObjective}\n\n" +

            $"<b>KEY DETAILS</b>\n" +
            $"• <b>Speaking With:</b> {config.persona} ({config.topic})\n" +
            $"• <b>Turn Limit:</b> {config.maxTurnLimit} Turns";
    }

    // Helper to turn "CafeteriaScene" into "Cafeteria"
    private string CleanScenarioName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName)) return "Scenario Briefing";
        return rawName.Replace("Scene", "").Trim();
    }
}