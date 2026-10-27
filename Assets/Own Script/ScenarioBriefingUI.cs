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

        // Combine all scenario details into a single formatted text block using Rich Text formatting
        fullBriefingText.text =
            $"Scenario: {config.scenario}\n" +
            $"Your Role: {config.participantRole}\n" +
            $"Persona: {config.persona}\n" +
            $"Topic: {config.topic}\n" +
            $"Difficulty: {config.difficulty}\n\n" +
            $"<size=110%><b>OBJECTIVE</b></size>\n" +
            $"{config.sessionObjective}\n\n" +
            $"<size=110%><b>SESSION RULES</b></size>\n" +
            $"• <b>Maximum Turns:</b> {config.maxTurnLimit} turns total.\n" +
            $"• <b>Speaking Limit:</b> Up to 60 seconds per response.\n" +
            $"• Focus on concrete examples and detailed explanations when answering.";
    }
}