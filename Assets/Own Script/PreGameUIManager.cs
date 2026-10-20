using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PreGameUIManager : MonoBehaviour
{
    public static PreGameUIManager Instance { get; private set; }

    [Header("Dropdown & Toggles")]
    [SerializeField] private TMP_Dropdown scenarioDropdown;
    [SerializeField] private Toggle controllerToggle;
    [SerializeField] private Toggle handTrackingToggle;

    [Header("Panels")]
    [SerializeField] private GameObject scenarioConfigPanel;
    [SerializeField] private GameObject tutorialPromptPanel;

    [Header("Modal Options")]
    [SerializeField] private Toggle dontShowAgainToggle; // Drag your 'DontShowAgainToggle' here
    [SerializeField] private TMP_Text headsetNoticeText;

    private void Awake()
    {
        Instance = this;
    }

    // --- STEP 1: Main Config "Start Session" Button ---
    public void OnClickStartSession()
    {
        if (scenarioDropdown == null)
        {
            Debug.LogError("[PreGameUI] scenarioDropdown is missing in Inspector!");
            return;
        }

        // 1. Extract dropdown value ("Stage Venue" -> "StageVenue")
        string rawText = scenarioDropdown.options[scenarioDropdown.value].text;
        string selectedScenario = System.Text.RegularExpressions.Regex.Replace(rawText, @"\s+", "");

        // 2. Extract input mode toggle choice
        string selectedMode = "Controller";
        if (handTrackingToggle != null && handTrackingToggle.isOn)
        {
            selectedMode = "HandTracking";
        }

        // 3. Immediately save configuration into persistent ScenarioManager
        ScenarioManager.Instance.SetConfiguration(selectedScenario, selectedMode);
        Debug.Log($"[PreGameUI] Locked Scenario: '{selectedScenario}' | Mode: '{selectedMode}'");

        // 4. Check if user opted out of the tutorial modal
        bool hidePrompt = PlayerPrefs.GetInt("HideTutorialPrompt", 0) == 1;

        if (!hidePrompt && tutorialPromptPanel != null)
        {
            // Show prompt modal
            if (scenarioConfigPanel != null) scenarioConfigPanel.SetActive(false);
            tutorialPromptPanel.SetActive(true);
        }
        else
        {
            // Direct launch if preference was saved or modal skipped
            LaunchVRScenario();
        }
    }

    // --- STEP 2: Modal Button "Start Tutorial" ---
    public void OnClickStartTutorial()
    {
        CheckAndSavePreference();

        if (headsetNoticeText != null) headsetNoticeText.text = "Loading Tutorial Scene...";
        ScenarioManager.Instance.LoadTutorialScene();
    }

    // --- STEP 3: Modal Button "Skip Tutorial" ---
    public void OnClickSkipTutorial()
    {
        CheckAndSavePreference();

        LaunchVRScenario();
    }

    private void CheckAndSavePreference()
    {
        // If user checked "Don't show again", save preference locally
        if (dontShowAgainToggle != null && dontShowAgainToggle.isOn)
        {
            PlayerPrefs.SetInt("HideTutorialPrompt", 1);
            PlayerPrefs.Save();
            Debug.Log("[PreGameUI] 'Don't show again' checked. Prompt hidden for future launches.");
        }
    }

    private void LaunchVRScenario()
    {
        if (headsetNoticeText != null) headsetNoticeText.text = "Loading VR World...";
        ScenarioManager.Instance.LoadSelectedVRScene();
    }

    // Utility: Right-click script title in Inspector to reset preference during testing
    [ContextMenu("Reset Tutorial Preferences")]
    public void ResetTutorialPreferences()
    {
        PlayerPrefs.DeleteKey("HideTutorialPrompt");
        PlayerPrefs.Save();
        Debug.Log("[PreGameUI] Tutorial preferences reset. Modal will show on next launch.");
    }
}