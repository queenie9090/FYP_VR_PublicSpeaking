using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class SetupUIController : MonoBehaviour
{
    [Header("UI Panel References")]
    [SerializeField] private GameObject configurationPanel;
    [SerializeField] private GameObject tutorialModalPanel;

    [Header("Informational Card Display")]
    [SerializeField] private ScenarioCardUI scenarioCardUI;

    [Header("Scenario UI Input Components")]
    [SerializeField] private TMP_Dropdown scenarioDropdown;      // Hall, Classroom, Interview, Normal Conversation
    [SerializeField] private TMP_Dropdown topicDropdown;
    [SerializeField] private TMP_InputField activityInput;        // Topic / Activity name
    [SerializeField] private TMP_Dropdown difficultyDropdown;    // Easy, Medium, Hard
    
    [SerializeField] private Button toggleCustomTopicButton;           // <-- ADDED: Button beside field
    [SerializeField] private TMP_Text toggleCustomTopicButtonText;

    [SerializeField] private TMP_InputField participantRoleInput;// e.g., Presenter, Student, Candidate
    [SerializeField] private TMP_InputField npcPersonaInput;      // e.g., Audience, Evaluator, Interviewer
    [SerializeField] private Toggle inputModeToggle;              // ON = Hands-Free, OFF = Controller

    [Header("Tutorial Modal Preferences")]
    [SerializeField] private Toggle dontShowTutorialAgainToggle;  // "Don't show this message again" Toggle

#if UNITY_EDITOR
    [Header("Editor VR Testing")]
    [Tooltip("Use each scene's XR Device Simulator for Tutorial and Interview without a headset. Disable to test OpenXR in the Editor.")]
    [UnityEngine.Serialization.FormerlySerializedAs("tutorialEditorSimulatorMode")]
    [SerializeField] private bool editorVrSimulatorMode;

    private void ConfigureEditorSimulator(string targetScene)
    {
        VRSceneLoader.SetEditorSimulatorMode(editorVrSimulatorMode);
        if (editorVrSimulatorMode && (targetScene == "TutorialScene" || targetScene == "Interview") && UserSession.InputMode != "Controller")
        {
            // The existing simulator supplies controller poses, not physical hand tracking.
            UserSession.InputMode = "Controller";
            Debug.Log("[VR Loader] Editor VR Simulator Mode uses controller input for this session.");
        }
    }
#endif

    private const string NeverShowTutorialKey = "Tutorial_NeverShowAgain";

    private bool isCustomTopicMode = false;

    private readonly Dictionary<string, List<string>> scenarioTopicPresets = new Dictionary<string, List<string>>
    {
        { "Hall", new List<string> {
            "Pitching a Mental Health App",
            "Presenting a Smart Parking System",
            "Explaining an App Launch Delay",
            "How Sleep Affects Memory"
        } },
        { "Interview", new List<string> {
            "Junior Web Developer Interview",
            "Handling a Late Teammate",
            "Why I Deserve This Scholarship",
            "Negotiating a 15% Salary Raise"
        } },
        { "Classroom", new List<string> {
            "Explaining Cloud Storage",
            "Final Project Guidelines",
            "Giving Feedback on Group Slides",
            "Answering Questions on Exam Scope"
        } },
        { "Cafeteria", new List<string> {
            "Convincing a Friend to Visit Langkawi",
            "Apologizing for a Missed Meeting",
            "Asking for Internship Advice",
            "Inviting a Classmate to a Hackathon"
        } }
    };

    private void Awake()
    {
        // Guarantee game time and audio listener are active upon entering Main Menu
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

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
            bool isHandTracking = (UserSession.InputMode == "Hands-Tracking");
            inputModeToggle.isOn = isHandTracking;
        }

        if (toggleCustomTopicButton != null)
        {
            toggleCustomTopicButton.onClick.AddListener(ToggleCustomTopicMode);
        }
        
        SetCustomTopicMode(false); // Start in dropdown mode

        if (topicDropdown != null)
        {
            topicDropdown.onValueChanged.AddListener(OnTopicDropdownChanged);
        }

        // 2. Add dropdown listener to auto-populate defaults and update scenario weights
        if (scenarioDropdown != null)
        {
            scenarioDropdown.onValueChanged.AddListener(OnScenarioDropdownChanged);

            // Initialize displays for initial selected option
            OnScenarioDropdownChanged(scenarioDropdown.value);
        }

        ConnectIntroductionAndRoleControls();

        // 3. Hide tutorial modal by default
        if (tutorialModalPanel != null)
        {
            tutorialModalPanel.SetActive(false);
        }
    }

    private void ConnectIntroductionAndRoleControls()
    {
        if (participantRoleInput != null) participantRoleInput.readOnly = true;
        if (npcPersonaInput != null) npcPersonaInput.readOnly = true;

        ConnectRoleEditButton(participantRoleInput);
        ConnectRoleEditButton(npcPersonaInput);

        Transform menuCanvas = configurationPanel != null ? configurationPanel.transform.parent : null;
        Transform scenarioButtons = menuCanvas != null
            ? menuCanvas.Find("IntroPanel/settingBG/settingSide") : null;
        if (scenarioButtons == null)
        {
            Debug.LogWarning("[SetupUIController] Introduction scenario buttons were not found.");
            return;
        }

        ConnectScenarioButton(scenarioButtons, "HallBttn", "Hall");
        ConnectScenarioButton(scenarioButtons, "ClassroomBttn", "Classroom");
        ConnectScenarioButton(scenarioButtons, "CafeBttn", "Cafeteria");
        ConnectScenarioButton(scenarioButtons, "InterviewBttn", "Interview");
    }

    private void ConnectRoleEditButton(TMP_InputField input)
    {
        if (input == null) return;
        Transform buttonTransform = input.transform.parent.Find("editButton");
        Button button = buttonTransform != null ? buttonTransform.GetComponent<Button>() : null;
        if (button == null)
        {
            Debug.LogWarning("[SetupUIController] Edit button not found for " + input.name);
            return;
        }

        button.onClick.AddListener(() =>
        {
            input.readOnly = false;
            input.Select();
            input.ActivateInputField();
        });
    }

    private void ConnectScenarioButton(Transform parent, string buttonName, string scenario)
    {
        Transform buttonTransform = parent.Find(buttonName);
        Button button = buttonTransform != null ? buttonTransform.GetComponent<Button>() : null;
        if (button == null)
        {
            Debug.LogWarning("[SetupUIController] Scenario button not found: " + buttonName);
            return;
        }

        button.onClick.AddListener(() => SelectIntroductionScenario(scenario));
    }

    private void SelectIntroductionScenario(string scenario)
    {
        if (scenarioDropdown == null) return;
        int index = scenarioDropdown.options.FindIndex(option =>
            string.Equals(option.text, scenario, System.StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            Debug.LogWarning("[SetupUIController] Scenario dropdown has no option for " + scenario);
            return;
        }

        scenarioDropdown.value = index;
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

        PopulateTopicDropdown(selectedScenario);
        // Auto-populate default topic/role presets
        switch (selectedScenario)
        {
            case "Hall":
                SetRoles("Presenter", "Audience Member");
                break;
            case "Interview":
                SetRoles("Candidate", "Interviewer");
                break;
            case "Classroom":
                SetRoles("Lecturer", "Student");
                break;
            case "Cafeteria":
                SetRoles("Friend", "Close Friend");
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

    private string FormatDynamicObjective(string topic)
    {
        string cleanTopic = string.IsNullOrWhiteSpace(topic) ? "the assigned topic" : topic;
        return $"Demonstrate clear and structured communication while discussing '{cleanTopic}', effectively addressing questions and maintaining an appropriate tone.";
    }

    // Called when the user clicks "Confirm Scenario" / "Start Session"
    public void OnConfirmConfiguration()
    {
        string currentTopic = "General Presentation";
        if (isCustomTopicMode)
        {
            if (activityInput != null && !string.IsNullOrWhiteSpace(activityInput.text))
                currentTopic = activityInput.text.Trim();
        }
        else if (topicDropdown != null && topicDropdown.options.Count > 0)
        {
            currentTopic = topicDropdown.options[topicDropdown.value].text;
        }

        // Package setup selections into UserSession
        UserSession.SelectedScenarioConfig = new ScenarioConfig
        {
            scenario = scenarioDropdown != null ? scenarioDropdown.options[scenarioDropdown.value].text : "Classroom",
            topic = currentTopic,
            difficulty = difficultyDropdown != null ? difficultyDropdown.options[difficultyDropdown.value].text : "Medium",
            participantRole = participantRoleInput != null && !string.IsNullOrEmpty(participantRoleInput.text) ? participantRoleInput.text : "Student",
            persona = npcPersonaInput != null && !string.IsNullOrEmpty(npcPersonaInput.text) ? npcPersonaInput.text : "Evaluator",
            sessionObjective = FormatDynamicObjective(currentTopic),
            maxTurnLimit = 5,
            sessionStartSignal = "Session Start"
        };

        // Reads the actual state of the UI toggle
        UserSession.InputMode = (inputModeToggle != null && inputModeToggle.isOn) ? "Hands-Tracking" : "Controller";

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

        // Ensure InputMode is captured even when launching directly into the Tutorial
        if (inputModeToggle != null)
        {
            UserSession.InputMode = inputModeToggle.isOn ? "Hands-Tracking" : "Controller";
        }

        UserSession.IsRunningTutorial = true;
        if (tutorialModalPanel != null) tutorialModalPanel.SetActive(false);

        Debug.Log($"[Flow] Starting Tutorial Session in VR with mode: {UserSession.InputMode}...");
#if UNITY_EDITOR
        ConfigureEditorSimulator("TutorialScene");
#endif
        StartCoroutine(VRSceneLoader.EnableVROnDemandAndLoad("TutorialScene"));
    }

    // Called when user clicks "NO / SKIP" in Tutorial Modal
    public void StartMainScenario()
    {
        SaveTutorialPreference();

        UserSession.IsRunningTutorial = false;
        if (tutorialModalPanel != null) tutorialModalPanel.SetActive(false);

        string targetScene = UserSession.SelectedScenarioConfig.scenario;
#if UNITY_EDITOR
        ConfigureEditorSimulator(targetScene);
#endif
        Debug.Log($"[Flow] Skipping Tutorial. Loading target scene: {targetScene}");
        StartCoroutine(VRSceneLoader.EnableVROnDemandAndLoad(targetScene));
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

    private void PopulateTopicDropdown(string scenario)
    {
        if (topicDropdown == null) return;

        topicDropdown.ClearOptions();
        if (scenarioTopicPresets.TryGetValue(scenario, out List<string> topics))
        {
            topicDropdown.AddOptions(topics);
        }
        else
        {
            topicDropdown.AddOptions(new List<string> { "General Presentation" });
        }

        topicDropdown.value = 0;
        topicDropdown.RefreshShownValue();
        OnTopicDropdownChanged(0);
    }

    // <-- ADDED: Called when user clicks the "Custom" / "Presets" button
    public void ToggleCustomTopicMode()
    {
        SetCustomTopicMode(!isCustomTopicMode);
    }

    // <-- ADDED: Swaps visibility between topicDropdown and activityInput
    private void SetCustomTopicMode(bool enableCustom)
    {
        isCustomTopicMode = enableCustom;

        if (topicDropdown != null)
            topicDropdown.gameObject.SetActive(!isCustomTopicMode);

        if (activityInput != null)
        {
            activityInput.gameObject.SetActive(isCustomTopicMode);
            if (isCustomTopicMode)
            {
                activityInput.text = "";
                activityInput.Select();
                activityInput.ActivateInputField();
            }
        }

        if (toggleCustomTopicButtonText != null)
        {
            toggleCustomTopicButtonText.text = isCustomTopicMode ? "Presets" : "Custom";
        }
    }

    private void OnTopicDropdownChanged(int index)
    {
        if (topicDropdown != null && activityInput != null && topicDropdown.options.Count > 0)
        {
            activityInput.text = topicDropdown.options[index].text;
        }
    }

    private void SetRoles(string defaultRole, string defaultPersona)
    {
        if (participantRoleInput != null) participantRoleInput.text = defaultRole;
        if (npcPersonaInput != null) npcPersonaInput.text = defaultPersona;
    }

}
