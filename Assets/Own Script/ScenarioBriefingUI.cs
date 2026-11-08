using UnityEngine;
using TMPro;

public class ScenarioBriefingUI : MonoBehaviour
{
    [Header("UI Text References")]
    [SerializeField] private TMP_Text scenarioTitleText;
    [SerializeField] private TMP_Text roleText;
    [SerializeField] private TMP_Text difficultyText;
    [SerializeField] private TMP_Text objectiveText;
    [SerializeField] private TMP_Text personaText;
    [SerializeField] private TMP_Text topicText;
    [SerializeField] private TMP_Text turnLimitText;

    public void SetupBriefing(ScenarioConfig config)
    {
        if (config == null) return;

        if (scenarioTitleText != null)
            scenarioTitleText.text = CleanScenarioName(config.scenario);

        if (roleText != null)
            roleText.text = config.participantRole;

        if (difficultyText != null)
            difficultyText.text = config.difficulty;

        if (objectiveText != null)
            objectiveText.text = config.sessionObjective;

        if (personaText != null)
            personaText.text = config.persona;

        if (topicText != null)
            topicText.text = config.topic;

        if (turnLimitText != null)
            turnLimitText.text = $"{config.maxTurnLimit} Turns";
    }

    private string CleanScenarioName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName)) return "Scenario Briefing";
        return rawName.Replace("Scene", "").Trim();
    }
}