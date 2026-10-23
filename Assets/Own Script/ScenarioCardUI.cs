using UnityEngine;
using TMPro;

public class ScenarioCardUI : MonoBehaviour
{
    [Header("UI Weight Displays")]
    [SerializeField] private TMP_Text speechClarityWeightText;
    [SerializeField] private TMP_Text eyeContactWeightText;
    [SerializeField] private TMP_Text handGestureWeightText;

    [Header("UI Weight Explanation Subtext")]
    [SerializeField] private TMP_Text scenarioFocusExplanationText;

    public void UpdateScenarioWeightsDisplay(string selectedScenarioName)
    {
        switch (selectedScenarioName)
        {
            case "Stage Venue":
            case "StageVenue":
                SetWeights("40%", "20%", "40%");
                SetExplanation("Large-room scenario: Prioritizes open gestures and speech articulation over close eye tracking.");
                break;

            case "Classroom":
                SetWeights("40%", "40%", "20%");
                SetExplanation("Instructional setting: Balances direct visual attention with articulate delivery.");
                break;

            case "Interview":
                SetWeights("50%", "30%", "20%");
                SetExplanation("Professional evaluation: Heavily weights spoken clarity and structured responses.");
                break;

            case "Cafeteria":
                SetWeights("50%", "30%", "20%");
                SetExplanation("Casual exchange: Focuses on natural speaking pace and clear expression.");
                break;

            case "Tutorial":
                SetWeights("Calibration", "Calibration", "Calibration");
                SetExplanation("Calibration session: Practices basic gesture controls and microphone input.");
                break;

            default:
                SetWeights("--", "--", "--");
                SetExplanation("Select a scenario to view evaluation metrics.");
                break;
        }
    }

    private void SetWeights(string clarity, string eyeContact, string gesture)
    {
        if (speechClarityWeightText != null)
            speechClarityWeightText.text = $"Speech Clarity: {clarity}";

        if (eyeContactWeightText != null)
            eyeContactWeightText.text = $"Eye Contact: {eyeContact}";

        if (handGestureWeightText != null)
            handGestureWeightText.text = $"Hand Gestures: {gesture}";
    }

    private void SetExplanation(string explanation)
    {
        if (scenarioFocusExplanationText != null)
            scenarioFocusExplanationText.text = explanation;
    }
}