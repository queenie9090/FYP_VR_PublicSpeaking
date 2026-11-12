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
    [SerializeField] private GameObject emotionPanel;
    [SerializeField] private GameObject viewNPCFaceButton;
    [SerializeField] private Button startSessionButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private ScenarioBriefingUI scenarioBriefingUI;
    [SerializeField] private GameObject exitSessionPanel;


    [Header("Navigation")]
    [SerializeField] private string defaultDashboardScene = "DashboardScene";

    private ScenarioConfig activeConfig;
    private List<DialogueTurn> conversationHistory = new List<DialogueTurn>();
    private bool isSessionActive = false;
    private bool isSavingSession = false;

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
            uiManager.SetStatus("Press 'Start Conversation' to begin.");
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
            activeConfig = UserSession.SelectedScenarioConfig;
        }

        if (scenarioBriefingUI != null)
        {
            scenarioBriefingUI.SetupBriefing(activeConfig);
        }

        if (!isTutorial && objectiveManager != null)
        {
            objectiveManager.SetPanelActive(true);
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
        if (!isActiveAndEnabled || isSessionActive || isSavingSession) return;
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
        for (int i = 5; i > 0; i--)
        {
            if (!isSessionActive && startConfigPanel == null)
            {
                // Safety check if session was cancelled
            }
            uiManager.SetStatus($"Starting in {i}s...");
            yield return new WaitForSeconds(1.0f);
        }

        uiManager.SetStatus("Session Started");
        initiationCoroutine = null;
        StartCommunicationSession();
    }

    private void StartCommunicationSession()
    {
        isSessionActive = true;
        conversationHistory.Clear();

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

            uiManager.SetStatus("Listening...");
            SetNPCBodyActivity(NPCBodyActivity.LISTENING, conversationHistory.Count(t => t.speaker == "NPC") + 1);

            byte[] wavBytes = null;
            float speechDuration = 0f, silenceDuration = 0f;
            bool recDone = false;

            // Pass live remaining time directly into the UI status text bar
            activeRecordingCoroutine = StartCoroutine(audioRecorder.RecordUserSpeechRoutine(
                (bytes, sDuration, silDuration) => {
                    wavBytes = bytes; speechDuration = sDuration; silenceDuration = silDuration;
                    recDone = true;
                },
                (currentSec, maxSec) => {
                    int remainingSec = Mathf.CeilToInt(maxSec - currentSec);
                    uiManager.SetStatus($"Recording... {remainingSec}s remaining");
                }
            ));

            yield return new WaitUntil(() => recDone);
            activeRecordingCoroutine = null;

            if (!isSessionActive) yield break;
            if (wavBytes == null || wavBytes.Length == 0)
            {
                uiManager.SetStatus("Microphone unavailable. Check the microphone and restart the session.");
                Debug.LogError("[InteractionManager] No microphone audio was recorded. The conversation was not advanced.");
                yield break;
            }

            sessionTimer.RecordTurnDurations(speechDuration, silenceDuration);

            bool turnDone = false;
            bool transcriptionFailed = false;
            SetNPCBodyActivity(NPCBodyActivity.THINKING, conversationHistory.Count(t => t.speaker == "NPC") + 1);
            int historyCountBeforeTranscription = conversationHistory.Count;
            StartCoroutine(speechController.TranscribeUserAudioRoutine(
                wavBytes, speechDuration, silenceDuration, conversationHistory,
                fillers => totalFillerWordsDetected += fillers,
                () => turnDone = true,
                () => transcriptionFailed = true
            ));

            yield return new WaitUntil(() => turnDone);
            if (!isSessionActive) yield break;
            if (transcriptionFailed)
            {
                uiManager.SetStatus("Speech recognition failed. Please try speaking again.");
                yield return new WaitForSeconds(1.5f);
                continue;
            }
            string userTranscript = (conversationHistory.Count > historyCountBeforeTranscription && conversationHistory[conversationHistory.Count - 1].speaker == "User")
    ? conversationHistory[conversationHistory.Count - 1].dialogue
    : "";
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
        uiManager.RenderDialogue(conversationHistory);

        if (hintUIController != null)
        {
            // Notify hintUIController that a new turn began.
            // If the panel is open in VR, it will automatically fetch and render the new turn's hint!
            hintUIController.OnNewTurnStarted();
        }

        uiManager.SetStatus("Generating NPC speech...");
        yield return StartCoroutine(speechController.PlayNPCAudioRoutine(
            response.npc_response, currentActiveNPC, fallbackNpcAudioSource, expectedTurnNumber
        ));
        //yield return StartCoroutine(PlayNPCAudioRoutine(response.npc_response));

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

    private void CalculateAndSaveResults()
    {
        if (isSavingSession) return;
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
                OnSessionCompletedSequence();
            },
            sessionStatus: sessionCompletionStatus
        );
    }

    /// <summary>
    /// "Close Session" / "End Conversation" Button: 
    /// Stops dialogue, saves metrics, and leaves the player in the room with the Exit Waypoint ON.
    /// </summary>
    public void CloseSession()
    {
        // GUARD: Prevent re-entry while a save sequence is actively running
        if (isSavingSession)
        {
            Debug.LogWarning("[InteractionManager] CloseSession ignored: Session save already in progress.");
            return;
        }

        SetNPCBodyActivity(NPCBodyActivity.IDLE);
        if (speechController != null) speechController.CancelPendingSpeech();

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

        if (activeRecordingCoroutine != null)
        {
            StopCoroutine(activeRecordingCoroutine);
            activeRecordingCoroutine = null;
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
        CloseSession();
    }

    private void OnSessionCompletedSequence()
    {
        isSessionActive = false;
        isSavingSession = false;

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
        if (isSessionActive || isSavingSession || initiationCoroutine != null)
        {
            ExitSession();
            return;
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
