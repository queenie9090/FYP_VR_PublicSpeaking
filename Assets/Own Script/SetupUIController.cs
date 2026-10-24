using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class SetupUIController : MonoBehaviour
{
    [Header("UI Panel References")]
    [SerializeField] private GameObject configurationPanel;
    [SerializeField] private GameObject tutorialModalPanel;

    [Header("Informational Card Display")]
    [SerializeField] private ScenarioCardUI scenarioCardUI;

    [Header("Scenario UI Input Components")]
    [SerializeField] private TMP_Dropdown scenarioDropdown;      // Hall, Classroom, Interview, Normal Conversation
    [SerializeField] private TMP_InputField activityInput;        // Topic / Activity name
    [SerializeField] private TMP_Dropdown difficultyDropdown;    // Easy, Medium, Hard
    [SerializeField] private TMP_InputField participantRoleInput;// e.g., Presenter, Student, Candidate
    [SerializeField] private TMP_InputField npcPersonaInput;      // e.g., Audience, Evaluator, Interviewer
    [SerializeField] private Toggle inputModeToggle;              // ON = Hands-Free, OFF = Controller

    [Header("Tutorial Modal Preferences")]
    [SerializeField] private Toggle dontShowTutorialAgainToggle;  // "Don't show this message again" Toggle

    private const string NeverShowTutorialKey = "Tutorial_NeverShowAgain";

    private void Start()
    {
        // 1. Ensure UserSession config is initialized to avoid NullReferenceException
        if (UserSession.SelectedScenarioConfig == null)
        {
            UserSession.SelectedScenarioConfig = new ScenarioConfig();
        }

        // Initialize toggle position based on current UserSession preference
        if (inputModeToggle != null)
        {
            inputModeToggle.isOn = (UserSession.InputMode == "Hands-Free");
        }

        // 2. Add dropdown listener to auto-populate defaults and update scenario weights
        if (scenarioDropdown != null)
        {
            scenarioDropdown.onValueChanged.AddListener(OnScenarioDropdownChanged);

            // Initialize displays for initial selected option
            OnScenarioDropdownChanged(scenarioDropdown.value);
        }

        // 3. Hide tutorial modal by default
        if (tutorialModalPanel != null)
        {
            tutorialModalPanel.SetActive(false);
        }
    }

    // Handles both default preset populating AND updating the ScenarioCardUI weights display
    private void OnScenarioDropdownChanged(int selectedIndex)
    {
        if (scenarioDropdown == null) return;

        string selectedScenario = scenarioDropdown.options[selectedIndex].text;

        // Store selected scenario name in global session
        if (UserSession.SelectedScenarioConfig != null)
        {
            UserSession.SelectedScenarioConfig.scenario = selectedScenario;
        }

        // Auto-populate default topic/role presets
        switch (selectedScenario)
        {
            case "Stage Venue":
            case "StageVenue":
                SetPresetIfEmpty("Keynote Presentation", "Presenter", "Audience Member");
                break;
            case "Interview":
                SetPresetIfEmpty("Job Interview Evaluation", "Candidate", "Interviewer");
                break;
            case "Classroom":
                SetPresetIfEmpty("Lecture QA Session", "Student", "Teacher");
                break;
            case "Cafeteria":
                SetPresetIfEmpty("Social Networking", "Participant", "Peer");
                break;
        }

        // Refresh informational scenario card weights
        if (scenarioCardUI != null)
        {
            scenarioCardUI.UpdateScenarioWeightsDisplay(selectedScenario);
        }
    }

    private void SetPresetIfEmpty(string defaultTopic, string defaultRole, string defaultPersona)
    {
        if (activityInput != null) activityInput.text = defaultTopic;
        if (participantRoleInput != null) participantRoleInput.text = defaultRole;
        if (npcPersonaInput != null) npcPersonaInput.text = defaultPersona;
    }

    // Called when the user clicks "Confirm Scenario" / "Start Session"
    public void OnConfirmConfiguration()
    {
        // Package setup selections into UserSession
        UserSession.SelectedScenarioConfig = new ScenarioConfig
        {
            scenario = scenarioDropdown != null ? scenarioDropdown.options[scenarioDropdown.value].text : "Classroom",
            topic = activityInput != null && !string.IsNullOrEmpty(activityInput.text) ? activityInput.text : "General Presentation",
            difficulty = difficultyDropdown != null ? difficultyDropdown.options[difficultyDropdown.value].text : "Medium",
            participantRole = participantRoleInput != null && !string.IsNullOrEmpty(participantRoleInput.text) ? participantRoleInput.text : "Student",
            persona = npcPersonaInput != null && !string.IsNullOrEmpty(npcPersonaInput.text) ? npcPersonaInput.text : "Evaluator",
            sessionObjective = "Practice scenario-specific communication skills effectively.",
            maxTurnLimit = 5,
            sessionStartSignal = "Session Start"
        };

        // Reads the actual state of the UI toggle
        UserSession.InputMode = (inputModeToggle != null && inputModeToggle.isOn) ? "Hands-Free" : "Controller";

        Debug.Log($"[SetupUIController] Configured: {UserSession.SelectedScenarioConfig.scenario} | Topic: {UserSession.SelectedScenarioConfig.topic} | Input: {UserSession.InputMode}");

        // Check if user previously checked "Don't show this message again"
        bool shouldSkipModal = PlayerPrefs.GetInt(NeverShowTutorialKey, 0) == 1;

        if (shouldSkipModal || tutorialModalPanel == null)
        {
            StartMainScenario();
        }
        else
        {
            tutorialModalPanel.SetActive(true);
        }
    }

    // Called when user clicks "YES" in Tutorial Modal
    public void OnSelectStartTutorial()
    {
        SaveTutorialPreference();

        UserSession.IsRunningTutorial = true;
        if (tutorialModalPanel != null) tutorialModalPanel.SetActive(false);

        Debug.Log("[Flow] Starting Tutorial Session...");
        SceneManager.LoadScene("TutorialScene");
    }

    // Called when user clicks "NO / SKIP" in Tutorial Modal
    public void StartMainScenario()
    {
        SaveTutorialPreference();

        UserSession.IsRunningTutorial = false;
        if (tutorialModalPanel != null) tutorialModalPanel.SetActive(false);

        string targetScene = UserSession.SelectedScenarioConfig.scenario;
        Debug.Log($"[Flow] Skipping Tutorial. Loading target scene: {targetScene}");
        SceneManager.LoadScene(targetScene);
    }

    // Save toggle preference into PlayerPrefs if checked
    private void SaveTutorialPreference()
    {
        if (dontShowTutorialAgainToggle != null && dontShowTutorialAgainToggle.isOn)
        {
            PlayerPrefs.SetInt(NeverShowTutorialKey, 1);
            PlayerPrefs.Save();
            Debug.Log("[SetupUIController] Preference saved: Never show tutorial modal again.");
        }
    }

    // Utility method to reset tutorial preference from Settings
    public void ResetTutorialPreference()
    {
        PlayerPrefs.DeleteKey(NeverShowTutorialKey);
        PlayerPrefs.Save();
        if (dontShowTutorialAgainToggle != null) dontShowTutorialAgainToggle.isOn = false;
        Debug.Log("[SetupUIController] Tutorial preference reset.");
    }

    // Cancel / Back Button Logic
    public void OnClickBack()
    {
        if (tutorialModalPanel != null && tutorialModalPanel.activeSelf)
        {
            tutorialModalPanel.SetActive(false);
        }
        else if (configurationPanel != null)
        {
            configurationPanel.SetActive(false);
        }
    }
}