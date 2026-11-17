using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.XR.Management;

public class InteractionManager : MonoBehaviour
{
    [Header("Sub-Managers")]
    [SerializeField] private OpenAIManager openAIManager;
    [SerializeField] private AudioRecorder audioRecorder;
    [SerializeField] private DialogueUIManager uiManager;
    [SerializeField] private VRPlayerSeating playerSeating;
    [SerializeField] private EyeContactTracker eyeContactTracker;
    [SerializeField] private GestureTracker gestureTracker;
    [SerializeField] private ObjectiveManager objectiveManager;
    [SerializeField] private HintUIController hintUIController;

    [Header("Extracted Helper Components")]
    private SessionResultSaver resultSaver;
    private LocomotionController locomotionController;
    private SessionTimer sessionTimer;
    private SpeechInteractionController speechController;
    private ScenarioTransitionController transitionController;
    private HintService hintService;

    [Header("Audio Feedback")]
    [SerializeField] private AudioSource sfxAudioSource;
    [SerializeField] private AudioClip sessionCompletedClip;
    [SerializeField] private AudioClip countdownTickClip;

    [Header("Multi-NPC Setup")]
    [Tooltip("Assign all NPCs available in this scene. Leave size = 1 for single-NPC scenes.")]
    [SerializeField] private List<NPCPersona> roomNPCs = new List<NPCPersona>();
    [SerializeField] private NPCPersona currentActiveNPC;
    [SerializeField] private AudioSource fallbackNpcAudioSource;
    private int currentNPCIndex = 0;

    [Header("NPC Emotion")]
    [SerializeField] private NPCEmotionController npcEmotionController;

    private string previousEmotion = "NEUTRAL";

    [Header("UI Controls")]
    [SerializeField] private GameObject startConfigPanel;
    [SerializeField] private GameObject countdownTimerPanel;
    [SerializeField] private TMP_Text countdownTimerText;
    [SerializeField] private float preSessionCountdownSeconds = 5f;
    [SerializeField] private GameObject emotionPanel;
    [SerializeField] private GameObject viewNPCFaceButton;
    [SerializeField] private Button startSessionButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private ScenarioBriefingUI[] scenarioBriefingPanels;
    [SerializeField] private GameObject exitSessionPanel;


    [Header("Navigation")]
    [SerializeField] private string defaultDashboardScene = "DashboardScene";

    private ScenarioConfig activeConfig;
    private List<DialogueTurn> conversationHistory = new List<DialogueTurn>();
    private bool isSessionActive = false;
    private bool isSavingSession = false;
    private bool sessionSaveFailed;
    private string sessionSaveOwner;

    private int totalFillerWordsDetected = 0;
    private bool leaveAfterSessionEnds = false;
    private string sessionCompletionStatus = "Completed";
    private Coroutine initiationCoroutine;
    private Coroutine activeSpeechLoopCoroutine;
    private Coroutine activeRecordingCoroutine;

    private void Start()
    {
        EnsureHelpersExist();
        // Hands off OpenXR editor init to ScenarioTransitionController
        transitionController.InitializeXRForEditor(this, InitializeSceneComponents);
    }

    private void InitializeSceneComponents()
    {
        Debug.Log("[InteractionManager] InitializeSceneComponents ran");
        InitScenarioConfig();

        EnsureHelpersExist();

        SetLocomotionEnabled(true);

        TutorialPanelManager tutorialManager = FindObjectOfType<TutorialPanelManager>();
        if (tutorialManager != null)
        {
            string modeStr = UserSession.InputMode;
            InputMode mode = (!string.IsNullOrEmpty(modeStr) && modeStr.Contains("Hand"))
                ? InputMode.HandTracking
                : InputMode.Controller;

            tutorialManager.InitializeForInputMode(mode);
        }

        if (roomNPCs != null && roomNPCs.Count > 0)
        {
            SelectActiveNPC(roomNPCs[0]);
        }

        //if (exitButton != null) exitButton.onClick.AddListener(ExitSession);

        if (hintUIController == null)
            hintUIController = FindObjectOfType<HintUIController>();


        if (startSessionButton != null)
        {
            startSessionButton.onClick.RemoveAllListeners();
            startSessionButton.onClick.AddListener(OnUserConfirmedStart);
            if (uiManager != null) uiManager.SetStatus("Press 'Start Conversation' to begin.");
        }
    }

    private void EnsureHelpersExist()
    {
        if (resultSaver == null) resultSaver = GetComponent<SessionResultSaver>() ?? gameObject.AddComponent<SessionResultSaver>();
        if (locomotionController == null) locomotionController = GetComponent<LocomotionController>() ?? gameObject.AddComponent<LocomotionController>();
        if (sessionTimer == null) sessionTimer = GetComponent<SessionTimer>() ?? gameObject.AddComponent<SessionTimer>();

        if (speechController == null)
        {
            speechController = GetComponent<SpeechInteractionController>() ?? gameObject.AddComponent<SpeechInteractionController>();
            speechController.InitializeDependencies(openAIManager, audioRecorder, uiManager);
        }

        if (transitionController == null) transitionController = GetComponent<ScenarioTransitionController>() ?? gameObject.AddComponent<ScenarioTransitionController>();

        if (hintService == null)
        {
            hintService = GetComponent<HintService>() ?? gameObject.AddComponent<HintService>();
            hintService.Initialize(openAIManager);
        }

        if (hintUIController == null)
            hintUIController = FindObjectOfType<HintUIController>();
    }

    public void SetLocomotionEnabled(bool enabled)
    {
        if (locomotionController == null)
            locomotionController = GetComponent<LocomotionController>() ?? gameObject.AddComponent<LocomotionController>();

        locomotionController.SetLocomotionEnabled(enabled);
    }

    private void InitScenarioConfig()
    {
        string currentSceneName = SceneManager.GetActiveScene().name;
        bool isTutorial = UserSession.IsRunningTutorial || currentSceneName == "TutorialScene";

        if (isTutorial)
        {
            activeConfig = new ScenarioConfig
            {
                scenario = "TutorialScene",
                topic = "System Calibration",
                difficulty = "Easy",
                participantRole = "Trainee",
                persona = "VR Guide",
                sessionObjective = "Guide the user to test their microphone. Ask the user to introduce themselves, state their name, and describe how they are feeling today in 1-2 sentences.",
                maxTurnLimit = 2,
                sessionStartSignal = "Start Tutorial"
            };
        }
        else
        {
            activeConfig = UserSession.SelectedScenarioConfig ?? new ScenarioConfig();
        }

        if (scenarioBriefingPanels != null)
        {
            foreach (var briefingUI in scenarioBriefingPanels)
            {
                if (briefingUI != null)
                    briefingUI.SetupBriefing(activeConfig);
            }
        }

        if (!isTutorial && objectiveManager != null)
        {
            objectiveManager.SetPanelActive(true);
        }

        if (countdownTimerPanel != null)
        {
            countdownTimerPanel.SetActive(false);
        }

        if (emotionPanel != null)
        {
            emotionPanel.SetActive(false);
        }

        if (viewNPCFaceButton != null)
        {
            viewNPCFaceButton.SetActive(false);
        }
    }


    public void OnUserConfirmedStart()
    {
        if (!isActiveAndEnabled || isSessionActive || isSavingSession || sessionSaveFailed) return;
        Debug.Log("[InteractionManager] OnUserConfirmedStart called");
        if (startSessionButton != null)
            startSessionButton.interactable = false;

        if (startConfigPanel != null)
            startConfigPanel.SetActive(false);

        // 1. Snap player into position FIRST while offsets and transforms are clear
        if (playerSeating != null)
            playerSeating.SnapPlayerToChair();

        // 2. Disable locomotion SECOND so player cannot walk out of the chair
        SetLocomotionEnabled(false);

        if (initiationCoroutine != null) StopCoroutine(initiationCoroutine);
        initiationCoroutine = StartCoroutine(HandsFreeSessionInitiationRoutine());
    }

    private IEnumerator HandsFreeSessionInitiationRoutine()
    {
        int totalSeconds = Mathf.Max(1, Mathf.CeilToInt(preSessionCountdownSeconds));

        if (countdownTimerPanel != null)
            countdownTimerPanel.SetActive(true);

        if (uiManager != null)
            uiManager.SetStatus("Get ready...");

        for (int i = totalSeconds; i > 0; i--)
        {
            if (countdownTimerText != null)
                countdownTimerText.text = i.ToString();

            if (countdownTickClip != null && sfxAudioSource != null)
                sfxAudioSource.PlayOneShot(countdownTickClip);

            yield return new WaitForSeconds(1.0f);
        }

        if (countdownTimerPanel != null)
            countdownTimerPanel.SetActive(false);

        if (uiManager != null)
            uiManager.SetStatus("Session Started");

        initiationCoroutine = null;
        StartCommunicationSession();
    }

    private void StartCommunicationSession()
    {
        UserSession.LastSessionEndedBeforeStart = false;
        UserSession.LastSessionAbortReason = "";

        isSessionActive = true;
        sessionSaveFailed = false;
        sessionSaveOwner = null;
        conversationHistory.Clear();
        if (uiManager != null) uiManager.RenderDialogue(conversationHistory);

        SetLocomotionEnabled(false);
        ArmTransitDetector transitDetector = FindObjectOfType<ArmTransitDetector>();
        Debug.Log($"[DEBUG] transitDetector found: {transitDetector != null}");
        if (transitDetector != null)
        {
            transitDetector.SetZonesActive(true);
            Debug.Log("[DEBUG] SetZonesActive(true) called");
        }

        if (uiManager != null)
        {
            uiManager.gameObject.SetActive(true);
        }

        if (hintUIController != null)
        {
            hintUIController.SyncInputModeFromUserSession();
            hintUIController.SetDialogueState(true, "Press Y/B or raise your index finger to request a dialogue hint.");
        }

        if (emotionPanel != null) emotionPanel.SetActive(true);
        sessionTimer.ResetTimer();
        totalFillerWordsDetected = 0;
        if (viewNPCFaceButton != null) viewNPCFaceButton.SetActive(true);
        previousEmotion = "NEUTRAL";

        NPCEmotionSessionManager.EnsureInstance().StartNewSession();
        sessionCompletionStatus = "Completed";

        if (npcEmotionController != null) npcEmotionController.SetEmotion("NEUTRAL", 0);
        if (eyeContactTracker != null) eyeContactTracker.StartTracking();
        if (gestureTracker != null)
        {
            gestureTracker.ResetTracker();
            gestureTracker.StartTracking();
        }
        if (hintUIController != null) hintUIController.ResetHintCount();
        if (uiManager != null && activeConfig != null) uiManager.InitTurnCounter(activeConfig.maxTurnLimit);

        StartCoroutine(TrackSessionTimerRoutine());
        if (activeSpeechLoopCoroutine != null) StopCoroutine(activeSpeechLoopCoroutine);
        activeSpeechLoopCoroutine = StartCoroutine(HandsFreeSpeechInteractionLoop());
    }

    public NPCEmotionController CurrentEmotionController => npcEmotionController;
    public bool IsSessionActive => isSessionActive;
    public bool IsPlayerCapturing => audioRecorder != null && audioRecorder.IsCapturing;
    public bool IsNpcSpeechPlaying(NPCEmotionController source) => speechController != null && speechController.IsPlayingSpeechFor(source);

    private IEnumerator TrackSessionTimerRoutine()
    {
        while (isSessionActive)
        {
            sessionTimer.UpdateElapsedTime(Time.deltaTime);
            uiManager.UpdateTimerDisplay(sessionTimer.ElapsedTimeSeconds);
            yield return null;
        }
    }

    private IEnumerator HandsFreeSpeechInteractionLoop()
    {
        while (isSessionActive)
        {
            AudioSource activeAudio = (currentActiveNPC != null && currentActiveNPC.npcAudioSource != null)
                ? currentActiveNPC.npcAudioSource
                : fallbackNpcAudioSource;

            while (activeAudio != null && activeAudio.isPlaying)
            {
                uiManager.SetStatus($"{currentActiveNPC?.name ?? "NPC"} Speaking...");
                yield return null;
            }

            if (!isSessionActive) yield break;

            int nextTurnNumber = conversationHistory.Count(t => t.speaker == "NPC") + 1;

            string userTranscript = "";
            float speechDuration = 0f, silenceDuration = 0f;
            bool captureDone = false;
            bool fatalError = false;

            yield return StartCoroutine(speechController.CaptureUserTurnWithRetriesRoutine(
                conversationHistory,
                fillers => totalFillerWordsDetected += fillers,
                (transcript, sDuration, silDuration) => {
                    userTranscript = transcript;
                    speechDuration = sDuration;
                    silenceDuration = silDuration;
                    captureDone = true;
                },
                () => {
                    fatalError = true;
                    captureDone = true;
                },
                onListeningAttemptStarted: () => SetNPCBodyActivity(NPCBodyActivity.LISTENING, nextTurnNumber),
                shouldCancel: () => !isSessionActive
            ));

            if (!isSessionActive || fatalError || !captureDone) yield break;

            sessionTimer.RecordTurnDurations(speechDuration, silenceDuration);
            yield return ProcessLLMTurnRoutine(userTranscript);
        }
    }

    private IEnumerator ProcessLLMTurnRoutine(string userTranscript)
    {
        // If speech was blank/silent, pass explicit silence context to LLM so PromptBuilder's Silence rule activates
        string formattedTranscript = string.IsNullOrWhiteSpace(userTranscript)
            ? "[Silence / No Response Provided]"
            : userTranscript;

        uiManager.SetStatus("NPC Thinking...");
        //int turnCounter = (conversationHistory.Count / 2) + 1;
        int turnCounter = conversationHistory.Count(t => t.speaker == "NPC");
        int expectedTurnNumber = turnCounter + 1;
        SetNPCBodyActivity(NPCBodyActivity.THINKING, expectedTurnNumber);

        LLMResponse response = null;
        string fallbackEmotion = EmotionIntensityClassifier.IsSupported(previousEmotion)
            ? previousEmotion.Trim().ToUpperInvariant() : "NEUTRAL";
        for (int attempt = 0; attempt < 2; attempt++)
        {
            LLMResponse candidate = null;
            bool llmDone = false;
            string prompt = PromptBuilder.BuildSystemPrompt(activeConfig, turnCounter, previousEmotion);
            if (attempt > 0)
                prompt += "\nReassess the emotion using the participant's actual context. Return one valid JSON response with a supported emotion.";
            StartCoroutine(openAIManager.SendChatRequest(prompt, conversationHistory, formattedTranscript,
                res => { candidate = res; llmDone = true; },
                err => { Debug.LogError($"[LLM Error] {err}"); llmDone = true; },
                true, () => this == null || !isSessionActive));
            yield return new WaitUntil(() => llmDone || !isSessionActive);
            if (!isSessionActive) yield break;
            if (candidate == null)
            {
                if (attempt == 0)
                {
                    Debug.LogWarning("[LLM] Response failed; retrying once.");
                    continue;
                }
                break;
            }
            response = candidate;
            if (!EmotionIntensityClassifier.NeedsReassessment(candidate.emotion, fallbackEmotion, userTranscript)) break;
            Debug.LogWarning(EmotionIntensityClassifier.IsSupported(candidate.emotion)
                ? "[Emotion Validation] Reassessing an emotion change without participant input."
                : "[Emotion Validation] Reassessing an unsupported emotion label.");
            if (attempt == 1)
            {
                response.emotion = fallbackEmotion;
                Debug.LogWarning("[Emotion Validation] Reassessment failed; retaining the previous valid emotion.");
            }
        }
        if (!isSessionActive) yield break;   // session was closed while waiting for the LLM
        if (response == null || string.IsNullOrEmpty(response.npc_response))
        {
            Debug.LogWarning("[ProcessLLMTurnRoutine] LLM returned null or empty response.");
            uiManager.SetStatus("NPC didn't hear that. Listening...");
            yield return new WaitForSeconds(1.5f);
            yield break;
        }
        if (EmotionIntensityClassifier.NeedsReassessment(response.emotion, fallbackEmotion, userTranscript))
            response.emotion = fallbackEmotion;

        // Check that the response belongs to the expected turn.
        if (response.turn_number != expectedTurnNumber)
        {
            Debug.LogWarning(
                $"Unexpected turn_number: {response.turn_number}. " +
                $"Expected: {expectedTurnNumber}"
            );

            // Correct the number instead of rejecting, so the user's turn is not lost
            response.turn_number = expectedTurnNumber;
        }

        // AI emotion has already been checked by OpenAIManager.
        // Save it for emotion continuity in the next turn.
        string validatedEmotion = response.emotion;
        previousEmotion = validatedEmotion;

        int emotionIntensity = EmotionIntensityClassifier.Classify(
            validatedEmotion, userTranscript, response.npc_response);

        if (NPCEmotionSessionManager.Instance != null)
        {
            string npcName = currentActiveNPC != null
                ? currentActiveNPC.name
                : "NPC";

            NPCEmotionSessionManager.Instance.RecordEmotion(
                expectedTurnNumber,
                npcName,
                validatedEmotion,
                emotionIntensity
            );
        }
        else
        {
            Debug.LogWarning(
                "NPCEmotionSessionManager not found."
            );
        }

        if (npcEmotionController != null)
        {
            npcEmotionController.SetEmotion(
                validatedEmotion,
                emotionIntensity
            );

            Debug.Log(
                $"[NPC Emotion] Turn {expectedTurnNumber}: " +
                $"{validatedEmotion}, Intensity: {emotionIntensity}"
            );
        }
        else
        {
            Debug.LogWarning(
                "NPCEmotionController is not assigned."
            );
        }

        conversationHistory.Add(new DialogueTurn { speaker = "NPC", dialogue = response.npc_response });
        uiManager.RenderDialogueWithPartialLatest(conversationHistory, "");

        if (hintUIController != null)
        {
            // Starts prefetching the hint in the background BEFORE/WHILE the NPC speaks!
            hintUIController.OnNewTurnStarted();
        }

        uiManager.SetStatus("Generating NPC speech...");
        yield return StartCoroutine(speechController.PlayNPCAudioRoutine(
            response.npc_response,
            currentActiveNPC,
            fallbackNpcAudioSource,
            expectedTurnNumber,
            onPartialTextUpdated: partial => {
                if (uiManager != null)
                    uiManager.RenderDialogueWithPartialLatest(conversationHistory, partial);
            }
        ));

        if (!isSessionActive) yield break;   // closed during NPC audio (CloseSession already saved)

        bool isFinalTurn = activeConfig != null && expectedTurnNumber >= activeConfig.maxTurnLimit;
        if (isFinalTurn || response.end_conversation)
        {
            isSessionActive = false;
            StopMetricCollection();
            CalculateAndSaveResults();
            yield break;
        }

        SelectNextNPC();
        uiManager.SetStatus("Recording...");
    }

    private void StopMetricCollection()
    {
        if (eyeContactTracker != null) eyeContactTracker.StopTracking();
        if (gestureTracker != null) gestureTracker.StopTracking();

        ArmTransitDetector det = FindObjectOfType<ArmTransitDetector>();
        if (det != null) det.SetZonesActive(false);
    }

    /// ALGORITHM 7: Hand Gesture Hint Activation Callback Method
    /// Invoked when GestureTracker detects an Open Palm > 1.0s.
    public void TriggerHintActivation()
    {
        if (hintService != null)
        {
            hintService.TriggerHintActivation(isSessionActive, activeConfig, conversationHistory, hintUIController, uiManager);
        }
    }

    public void PrefetchHintForCurrentTurn()
    {
        if (hintService != null)
        {
            hintService.TriggerHintActivation(isSessionActive, activeConfig, conversationHistory, hintUIController, uiManager, isBackgroundPrefetch: true);
        }
    }

    private void CalculateAndSaveResults()
    {
        if (isSavingSession) return;
        // Tutorial is calibration, not a saved training result. Keep its history in
        // memory but do not initialize the shared dashboard/save workflow.
        if (SceneManager.GetActiveScene().name == "TutorialScene")
        {
            SetNPCBodyActivity(NPCBodyActivity.IDLE);
            NPCEmotionSessionManager.Instance?.FinishBodyEventCollection();
            OnSessionCompletedSequence();
            return;
        }
        string currentAuthId = UserSession.AuthClient?.Auth?.CurrentUser?.Id
                    ?? UserSession.AuthClient?.Auth?.CurrentSession?.User?.Id;
        if (sessionSaveFailed && (sessionSaveOwner != UserSession.CurrentUserId || currentAuthId != sessionSaveOwner))
        {
            if (uiManager != null) uiManager.SetStatus("Sign in as the original session owner to retry saving.");
            return;
        }

        if (!sessionSaveFailed) sessionSaveOwner = UserSession.CurrentUserId;
        isSavingSession = true;
        SetNPCBodyActivity(NPCBodyActivity.IDLE);

        resultSaver.ProcessAndSaveResults(
            activeConfig, conversationHistory, sessionTimer.ElapsedTimeSeconds,
            sessionTimer.TotalSpeakingTimeSeconds, sessionTimer.TotalSilenceTimeSeconds, totalFillerWordsDetected,
            sessionTimer.StartTimestamp,
            eyeContactTracker, gestureTracker, hintUIController, uiManager,
            onComplete: () => {
                if (this == null) return;
                isSavingSession = false;
                sessionSaveFailed = false;
                OnSessionCompletedSequence();
            },
            sessionStatus: sessionCompletionStatus,
            onFailure: error => {
                if (this == null) return;
                isSavingSession = false;
                sessionSaveFailed = true;
                if (uiManager != null) uiManager.SetStatus("Save failed. Results retained; exit again to retry.");
                if (exitSessionPanel != null) exitSessionPanel.SetActive(true);
            },
            requireAuthenticatedSave: sessionSaveFailed
        );
    }

    /// <summary>
    /// "Close Session" / "End Conversation" Button: 
    /// Stops dialogue, saves metrics, and leaves the player in the room with the Exit Waypoint ON.
    /// </summary>
    public void CloseSession()
    {
        if (sessionSaveFailed && !isSavingSession)
        {
            CalculateAndSaveResults();
            return;
        }
        // GUARD: Prevent re-entry while a save sequence is actively running
        if (isSavingSession)
        {
            Debug.LogWarning("[InteractionManager] CloseSession ignored: Session save already in progress.");
            return;
        }

        SetNPCBodyActivity(NPCBodyActivity.IDLE);
        if (speechController != null) speechController.CancelPendingSpeech();
        if (hintService != null) hintService.CancelPendingHints();

        if (activeSpeechLoopCoroutine != null)
        {
            StopCoroutine(activeSpeechLoopCoroutine);
            activeSpeechLoopCoroutine = null;
        }

        SetLocomotionEnabled(true);

        if (initiationCoroutine != null)
        {
            StopCoroutine(initiationCoroutine);
            initiationCoroutine = null;
            if (countdownTimerPanel != null) countdownTimerPanel.SetActive(false);
        }

        if (currentActiveNPC != null && currentActiveNPC.npcAudioSource != null)
        {
            currentActiveNPC.npcAudioSource.Stop();
            Animator anim = currentActiveNPC.GetComponent<Animator>();
            if (anim != null) anim.SetBool("IsTalking", false);
        }
        
        if (sessionCompletedClip != null && sfxAudioSource != null)
        {
            sfxAudioSource.PlayOneShot(sessionCompletedClip);
        }

        if (fallbackNpcAudioSource != null)
        {
            fallbackNpcAudioSource.Stop();
        }

        if (audioRecorder != null)
        {
            audioRecorder.StopRecording();
        }

        StopMetricCollection();

        if (emotionPanel != null) emotionPanel.SetActive(false);
        if (startConfigPanel != null) startConfigPanel.SetActive(false);

        // Save results only if session is active AND at least one dialogue turn occurred
        if (isSessionActive && conversationHistory != null && conversationHistory.Count > 0)
        {
            sessionCompletionStatus = "Incomplete"; // User ended chat early
            isSessionActive = false;
            CalculateAndSaveResults();
        }
        else
        {
            // Leaving after an already completed conversation must not replace
            // its result with the dashboard's "no session started" state.
            if (conversationHistory == null || conversationHistory.Count == 0)
            {
                UserSession.LastSessionEndedBeforeStart = true;
                UserSession.LastSessionAbortReason = "Session ended early before any conversation took place. No metrics were recorded.";
            }
            if (uiManager != null) uiManager.SetStatus("Chat ended.");
            isSessionActive = false;
            OnSessionCompletedSequence();
        }
    }

    /// <summary>
    /// "Exit Session" Button: Immediately transitions back to the Dashboard.
    /// </summary>
    public void ExitSession()
    {
        if (isExiting) return;
        leaveAfterSessionEnds = true;
        // The existing completion callback will navigate after preparation finishes.
        if (isSavingSession) return;
        if (sessionSaveFailed) { CalculateAndSaveResults(); return; }
        CloseSession();
    }

    private void OnSessionCompletedSequence()
    {
        isSessionActive = false;
        isSavingSession = false;
        if (SceneManager.GetActiveScene().name == "TutorialScene")
            NPCEmotionSessionManager.Instance?.FinishBodyEventCollection();

        SetLocomotionEnabled(true);

        if (uiManager != null)
        {
            uiManager.HideTurnCounter();
            uiManager.SetStatus(""); // Clear status text
            uiManager.HideUI();
        }

        if (hintUIController != null)
        {
            hintUIController.HideHintCanvas();
            hintUIController.SetDialogueState(false);   // stops "Generating AI hint..." hanging after the session
        }

        if (audioRecorder != null)
        {
            audioRecorder.StopRecording();
        }

        if (emotionPanel != null)
        {
            emotionPanel.SetActive(false);
        }

        if (viewNPCFaceButton != null)
        {
            viewNPCFaceButton.SetActive(false);
        }

        if (startConfigPanel != null)
        {
            startConfigPanel.SetActive(false);
        }

        if (objectiveManager != null)
        {
            objectiveManager.EnableExitWaypointOnly();
        }

        bool isTutorial = UserSession.IsRunningTutorial;

        if (uiManager != null)
        {
            uiManager.HideTurnCounter();
        }

        if (leaveAfterSessionEnds)
        {
            leaveAfterSessionEnds = false;
            ExecuteSceneTransition();
        }
        else
        {
            if (exitSessionPanel != null)
            {
                exitSessionPanel.SetActive(true); // Pops up the exit panel in VR space
            }
        }
    }

    private bool isExiting = false;

    // Call this method when the player interacts with or walks into the Exit Door / Zone
    public void ExecuteSceneTransition()
    {
        if (isExiting) return;
        // Exit controls and door triggers must not bypass the session-ending path.
        if (isSessionActive || isSavingSession || sessionSaveFailed || initiationCoroutine != null)
        {
            ExitSession();
            return;
        }

        // If the player walked out the exit door without ever starting a session
        if (conversationHistory == null || conversationHistory.Count == 0)
        {
            UserSession.LastSessionEndedBeforeStart = true;
            UserSession.LastSessionAbortReason = "Session ended early before starting. No session data was saved.";
        }

        isExiting = true;
        StartCoroutine(ExecuteSceneTransitionRoutine());
    }

    private IEnumerator ExecuteSceneTransitionRoutine()
    {
        while (isSavingSession)
        {
            if (uiManager != null) uiManager.SetStatus("Saving progress... Please wait.");
            yield return new WaitForSeconds(0.5f);
        }
        isSessionActive = false;

        yield return transitionController.ExecuteSceneTransitionRoutine(activeConfig, uiManager);
    }

    public void SelectActiveNPC(NPCPersona targetNPC)
    {
        if (targetNPC == null) return;
        SetNPCBodyActivity(NPCBodyActivity.IDLE);
        currentActiveNPC = targetNPC;
        var targetEmotion = targetNPC.GetComponentInParent<NPCEmotionController>() ??
            targetNPC.GetComponentInChildren<NPCEmotionController>();
        npcEmotionController = targetEmotion;
        if (emotionPanel != null)
        {
            var indicator = emotionPanel.GetComponentInChildren<NPCEmotionIndicatorUI>(true);
            if (indicator != null) indicator.SetEmotionSource(targetEmotion);
        }
        if (targetEmotion == null)
            Debug.LogWarning("[InteractionManager] Selected NPC has no emotion controller.", targetNPC);

        if (eyeContactTracker != null)
        {
            if (targetNPC.facialCollider != null)
                eyeContactTracker.SetTargetFacialCollider(targetNPC.facialCollider);

            eyeContactTracker.SetEyeContactIcon(targetNPC.eyeContactIcon);
        }

        if (roomNPCs.Contains(targetNPC))
        {
            currentNPCIndex = roomNPCs.IndexOf(targetNPC);
        }

    }

    public void SelectNextNPC()
    {
        if (roomNPCs == null || roomNPCs.Count <= 1) return;
        currentNPCIndex = (currentNPCIndex + 1) % roomNPCs.Count;
        SelectActiveNPC(roomNPCs[currentNPCIndex]);
    }

    private void SetNPCBodyActivity(NPCBodyActivity activity, int turnNumber = 0)
    {
        NPCEmotionSessionManager.Instance?.SetRealismContext(npcEmotionController,
            currentActiveNPC != null ? currentActiveNPC.name : null, activity.ToString(), turnNumber);
        if (currentActiveNPC != null)
        {
            currentActiveNPC.GetComponentInParent<NPCBodyAnimation>()?.SetActivity(activity, turnNumber);
            currentActiveNPC.GetComponentInParent<NPCEyeContactController>()?.SetActivity(activity);
        }
    }
}
