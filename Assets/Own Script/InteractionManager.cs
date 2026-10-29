using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class InteractionManager : MonoBehaviour
{
    [Header("Sub-Managers")]
    [SerializeField] private OpenAIManager openAIManager;
    [SerializeField] private SupabaseManager supabaseManager;
    [SerializeField] private AudioRecorder audioRecorder;
    [SerializeField] private DialogueUIManager uiManager;
    [SerializeField] private VRPlayerSeating playerSeating;
    [SerializeField] private EyeContactTracker eyeContactTracker;
    [SerializeField] private GestureTracker gestureTracker;    
    [SerializeField] private ObjectiveManager objectiveManager;
    [SerializeField] private HintManager hintManager;


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
    [SerializeField] private Button startSessionButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private ScenarioBriefingUI scenarioBriefingUI;

    [Header("VR Locomotion Control")]
    [SerializeField] private UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider moveProvider;
    [SerializeField] private UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider teleportProvider;

    [Header("Navigation")]
    [SerializeField] private string defaultDashboardScene = "DashboardScene";

    private ScenarioConfig activeConfig;
    private List<DialogueTurn> conversationHistory = new List<DialogueTurn>();
    private bool isSessionActive = false;
    private bool isSavingSession = false;

    private float sessionElapsedTimeSeconds = 0.0f;
    private float totalSpeakingTimeSeconds = 0.0f;
    private float totalSilenceTimeSeconds = 0.0f;
    private int totalFillerWordsDetected = 0;
    private DateTime sessionStartTimestamp;
    private DateTime sessionEndTimestamp;

    private void Start()
    {
        InitScenarioConfig();

        if (roomNPCs != null && roomNPCs.Count > 0)
        {
            SelectActiveNPC(roomNPCs[0]);
        }

        if (exitButton != null) exitButton.onClick.AddListener(ExitSession);

        if (hintManager == null)
            hintManager = FindObjectOfType<HintManager>();

        if (startSessionButton != null)
        {
            startSessionButton.onClick.RemoveAllListeners();
            startSessionButton.onClick.AddListener(OnUserConfirmedStart);
            uiManager.SetStatus("Press 'Start Conversation' to begin.");
        }
    }

    public void SetLocomotionEnabled(bool enabled)
    {
        if (moveProvider != null) moveProvider.enabled = enabled;
        if (teleportProvider != null) teleportProvider.enabled = enabled;
    }

    private void InitScenarioConfig()
    {
        string currentSceneName = SceneManager.GetActiveScene().name;
        bool isTutorial = UserSession.IsRunningTutorial || currentSceneName == "TutorialScene";

        if (UserSession.IsRunningTutorial)
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

        if (emotionPanel != null)
        {
            emotionPanel.SetActive(false);
        }
    }


    public void OnUserConfirmedStart()
    {
        if (startSessionButton != null)
            startSessionButton.interactable = false;

        SetLocomotionEnabled(false);

        if (objectiveManager != null)
        {
            objectiveManager.OnConversationStarted();
        }

        if (startConfigPanel != null)
            startConfigPanel.SetActive(false);

        if (playerSeating != null)
            playerSeating.SnapPlayerToChair();

        StartCoroutine(HandsFreeSessionInitiationRoutine());
    }

    private IEnumerator HandsFreeSessionInitiationRoutine()
    {
        for (int i = 5; i > 0; i--)
        {
            uiManager.SetStatus($"Starting in {i}s...");
            yield return new WaitForSeconds(1.0f);
        }

        uiManager.SetStatus("Session Started");
        StartCommunicationSession();
    }

    private void StartCommunicationSession()
    {
        isSessionActive = true;
        conversationHistory.Clear();

        SetLocomotionEnabled(false);

        if (hintManager != null)
        {
            hintManager.SyncInputModeFromUserSession();
            hintManager.SetDialogueState(true, "Press Y or raise Open Palm to request a dialogue hint.");
        }

        if (emotionPanel != null) emotionPanel.SetActive(true);
        sessionElapsedTimeSeconds = 0f; totalSpeakingTimeSeconds = 0f; totalSilenceTimeSeconds = 0f; totalFillerWordsDetected = 0;
        previousEmotion = "NEUTRAL";

        // Capture precise start time (ISO 8601 string format for Supabase timestamptz)
        sessionStartTimestamp = DateTime.UtcNow;

        if (npcEmotionController != null) npcEmotionController.SetEmotion("NEUTRAL", 0);
        if (eyeContactTracker != null) eyeContactTracker.StartTracking();
        if (gestureTracker != null) gestureTracker.ResetTracker();
        if (uiManager != null && activeConfig != null) uiManager.InitTurnCounter(activeConfig.maxTurnLimit);       

        StartCoroutine(TrackSessionTimerRoutine());
        StartCoroutine(HandsFreeSpeechInteractionLoop());
    }

    private IEnumerator TrackSessionTimerRoutine()
    {
        while (isSessionActive)
        {
            if (Time.timeScale > 0f)
            {
                sessionElapsedTimeSeconds += Time.deltaTime;
                uiManager.UpdateTimerDisplay(sessionElapsedTimeSeconds);
            }
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

            byte[] wavBytes = null;
            float speechDuration = 0f, silenceDuration = 0f;
            bool recDone = false;

            // Pass live remaining time directly into the UI status text bar
            StartCoroutine(audioRecorder.RecordUserSpeechRoutine(
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

            if (!isSessionActive) yield break;

            totalSilenceTimeSeconds += silenceDuration;
            totalSpeakingTimeSeconds += speechDuration;

            yield return ProcessUserAudioRoutine(wavBytes, speechDuration, silenceDuration);
        }
    }

    private IEnumerator ProcessUserAudioRoutine(byte[] wavBytes, float speechDuration, float silenceDuration)
    {
        uiManager.SetStatus("Transcribing...");
        bool sttDone = false; string userTranscript = "";

        StartCoroutine(openAIManager.TranscribeAudioWithPrompt(wavBytes, "Um, uh, er.", transcript => {
            userTranscript = transcript; sttDone = true;
        }, err => {
            Debug.LogError($"[STT Error] {err}"); uiManager.SetStatus("Speech Error."); sttDone = true;
        }));

        yield return new WaitUntil(() => sttDone);

        if (!string.IsNullOrEmpty(userTranscript))
        {
            int turnFillers = MetricsCalculator.DetectFillerWords(userTranscript);
            totalFillerWordsDetected += turnFillers;

            conversationHistory.Add(new DialogueTurn
            {
                speaker = "User",
                dialogue = userTranscript,
                speechDuration = speechDuration,
                silenceDuration = silenceDuration,
                fillerWordCount = turnFillers
            });

            uiManager.RenderDialogue(conversationHistory);
            yield return ProcessLLMTurnRoutine(userTranscript);
        }
        else
        {
            uiManager.SetStatus("No Speech Detected.");
            yield return new WaitForSeconds(1.5f);
        }
    }

    private IEnumerator ProcessLLMTurnRoutine(string userTranscript)
    {
        uiManager.SetStatus("NPC Thinking...");
        //int turnCounter = (conversationHistory.Count / 2) + 1;
        int turnCounter = conversationHistory.Count / 2;
        int expectedTurnNumber = turnCounter + 1;

        LLMResponse response = null; bool llmDone = false;
        StartCoroutine(openAIManager.SendChatRequest(
            PromptBuilder.BuildSystemPrompt(activeConfig, turnCounter, previousEmotion), conversationHistory, userTranscript,
            res => { response = res; llmDone = true; },
            err => { Debug.LogError($"[LLM Error] {err}"); llmDone = true; }
        ));

        yield return new WaitUntil(() => llmDone);
        if (response == null || string.IsNullOrEmpty(response.npc_response)) yield break;

        // Check that the response belongs to the expected turn.
        if (response.turn_number != expectedTurnNumber)
        {
            Debug.LogWarning(
                $"Unexpected turn_number: {response.turn_number}. " +
                $"Expected: {expectedTurnNumber}"
            );

            uiManager.SetStatus("NPC Turn Validation Error.");
            yield break;
        }

        // AI emotion has already been checked by OpenAIManager.
        // Save it for emotion continuity in the next turn.
        string validatedEmotion = response.emotion;
        previousEmotion = validatedEmotion;

        // TEMPORARY intensity for integration testing.
        // Replace with Unity's actual intensity calculation later.
        int testIntensity =
            validatedEmotion == "NEUTRAL" ? 0 : 60;

        if (npcEmotionController != null)
        {
            npcEmotionController.SetEmotion(
                validatedEmotion,
                testIntensity
            );

            Debug.Log(
                $"[NPC Emotion] Turn {expectedTurnNumber}: " +
                $"{validatedEmotion}, Intensity: {testIntensity}"
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

        if (hintManager != null)
        {
            hintManager.SetDialogueState(true, $"Respond clearly to {currentActiveNPC?.name ?? "NPC"}'s question.");
        }

        uiManager.SetStatus("NPC Speaking...");
        yield return StartCoroutine(PlayNPCAudioRoutine(response.npc_response));

        if ((activeConfig != null && turnCounter >= activeConfig.maxTurnLimit) || (response != null && response.end_conversation))
        {
            isSessionActive = false;
            CalculateAndSaveResults();
            yield break;
        }

        SelectNextNPC();
        uiManager.SetStatus("Recording...");
    }

    private IEnumerator PlayNPCAudioRoutine(string textToSpeak)
    {
        bool downloaded = false; AudioClip clip = null;

        // Use current active NPC's specific Voice ID (e.g., "echo" vs "nova")
        string voice = (currentActiveNPC != null && !string.IsNullOrEmpty(currentActiveNPC.voiceId))
            ? currentActiveNPC.voiceId
            : "nova";

        StartCoroutine(openAIManager.TextToSpeech(textToSpeak, voice, c => { clip = c; downloaded = true; }, err => downloaded = true));
        yield return new WaitUntil(() => downloaded);

        if (clip != null)
        {
            // Play audio directly from the active NPC's mouth/desk AudioSource
            AudioSource targetSource = (currentActiveNPC != null && currentActiveNPC.npcAudioSource != null)
                ? currentActiveNPC.npcAudioSource
                : fallbackNpcAudioSource;

            Animator npcAnimator = currentActiveNPC != null ? currentActiveNPC.GetComponent<Animator>() : null;
            if (npcAnimator != null) npcAnimator.SetBool("IsTalking", true);

            targetSource.clip = clip;
            targetSource.Play();
            yield return new WaitForSeconds(0.1f);
            while (targetSource != null && targetSource.isPlaying) yield return null;

            if (npcAnimator != null) npcAnimator.SetBool("IsTalking", false);
        }
    }

    /// ALGORITHM 7: Hand Gesture Hint Activation Callback Method
    /// Invoked when GestureTracker detects an Open Palm > 1.0s.
    public void TriggerHintActivation()
    {
        if (!isSessionActive) return;

        // 1. Immediately show loading feedback on the active VR canvas
        if (hintManager != null)
        {
            hintManager.ShowDirectMessage("Generating AI hint...");
        }

        // 2. Build context prompt from conversation history
        string lastUserScript = conversationHistory.Count > 0 ? conversationHistory[conversationHistory.Count - 1].dialogue : "";
        string hintPrompt = PromptBuilder.BuildHintPrompt(activeConfig, conversationHistory, lastUserScript);

        // 3. Send API request to OpenAI
        StartCoroutine(openAIManager.SendHintRequest(
            hintPrompt,
            generatedHint => {
                if (!string.IsNullOrEmpty(generatedHint))
                {
                    Debug.Log($"[AI Hint Received] {generatedHint}");

                    // Update HintManager so it stores AND displays the new AI hint!
                    if (hintManager != null)
                    {
                        hintManager.SetDialogueState(true, generatedHint);
                        hintManager.ShowDirectMessage(generatedHint);
                    }

                    if (uiManager != null) uiManager.SetStatus("Hint Generated");
                }
            },
            err => {
                Debug.LogError($"[Hint Error] {err}");
                if (hintManager != null) hintManager.ShowDirectMessage("Failed to generate hint.");
            }
        ));
    }

    private void CalculateAndSaveResults()
    {
        if (isSavingSession) return;
        isSavingSession = true;
        uiManager.SetStatus("Saving Results...");

        // Capture session completion timestamp
        sessionEndTimestamp = DateTime.UtcNow;

        string startTimeIso = sessionStartTimestamp.ToString("o");
        string endTimeIso = sessionEndTimestamp.ToString("o");

        // 1. Calculate physical metrics using attached tracking components
        float eyeContactPct = 0.0f;
        if (eyeContactTracker != null)
        {
            eyeContactTracker.StopTracking();
            eyeContactPct = eyeContactTracker.CalculateEyeContactScore();
        }

        int expressiveGestureCount = 0;
        float gpm = 0.0f;
        if (gestureTracker != null)
        {
            gestureTracker.StopTracking();
            var gestureMetrics = gestureTracker.CalculateGestureMetrics(sessionElapsedTimeSeconds);
            expressiveGestureCount = gestureMetrics.expressiveCount;
            gpm = gestureMetrics.gpm;
        }

        // 2. Bypass database saving if this was just a calibration tutorial
        if (UserSession.IsRunningTutorial)
        {
            uiManager.SetStatus("Calibration Complete!");
            OnSessionCompletedSequence();
            return;
        }

        // 3. Compute speech metrics using MetricsCalculator methods
        int totalWordsSpoken = 0;
        List<DialogueTurnRecord> turnRecords = new List<DialogueTurnRecord>();

        for (int i = 0; i < conversationHistory.Count; i += 2)
        {
            string userText = conversationHistory[i].dialogue;
            string aiText = (i + 1 < conversationHistory.Count) ? conversationHistory[i + 1].dialogue : "";
            int turnFillers = conversationHistory[i].fillerWordCount;
            float userDurationSec = conversationHistory[i].speechDuration;
            float turnSilenceSec = conversationHistory[i].silenceDuration;

            var (wordCount, turnWpm) = MetricsCalculator.CalculateSpeakingRate(userText, userDurationSec);
            totalWordsSpoken += wordCount;

            turnRecords.Add(new DialogueTurnRecord
            {
                turn_sequence = (i / 2) + 1,
                user_transcript = userText,
                ai_response_text = aiText,
                filler_word_count = turnFillers,
                speaking_rate_wpm = turnWpm,
                silence_duration_seconds = turnSilenceSec
            });
        }

        float overallWpm = totalSpeakingTimeSeconds > 0 ? (totalWordsSpoken / totalSpeakingTimeSeconds) * 60.0f : 0.0f;
        var (clarityScore, fwpm) = MetricsCalculator.CalculateSpeechClarity(totalFillerWordsDetected, totalSpeakingTimeSeconds, overallWpm);
        float gestureScore = Mathf.Clamp(expressiveGestureCount * 10.0f, 0.0f, 100.0f);

        // Clean the scene name (e.g. "CafeteriaScene" -> "Cafeteria") so it matches scenario weights
        string rawScenario = activeConfig != null ? activeConfig.scenario : "Normal Conversation";
        string currentScenario = rawScenario.Replace("Scene", "").Trim();

        float overallScore = MetricsCalculator.CalculateOverallScore(currentScenario, clarityScore, eyeContactPct, gestureScore);

        string activeUserId = UserSession.IsLoggedIn ? UserSession.CurrentUserId : "00000000-0000-0000-0000-000000000000";

        // 4. Generate Dynamic AI Coaching Feedback Summary via OpenAI
        uiManager.SetStatus("Generating AI Feedback...");

        int unprofessionalCount = gestureTracker != null ? gestureTracker.GetUnprofessionalCount() : 0;

        // Pass unprofessional gesture count to the AI feedback prompt
        string summaryPrompt = $"You are an expert VR speech coach. Provide a concise 2-3 sentence constructive feedback summary with actionable suggestions for improvement based on this participant's performance in a '{currentScenario}' simulation: " +
                               $"Overall Score: {overallScore:F0}%, Speech Clarity: {clarityScore:F0}%, Speaking Rate: {overallWpm:F0} WPM, " +
                               $"Filler Words Used: {totalFillerWordsDetected}, Eye Contact: {eyeContactPct:F0}%, Gestures Rate: {gpm:F1} GPM, " +
                               $"Unprofessional Gestures Flagged: {unprofessionalCount}.";

        StartCoroutine(openAIManager.SendChatRequest(
            summaryPrompt,
            conversationHistory,
            "Generate Summary",
            res => {
                string aiSummary = (res != null && !string.IsNullOrEmpty(res.npc_response))
                    ? res.npc_response
                    : $"Your overall performance scored {overallScore:F0}%. Speech clarity was {clarityScore:F0}% with {totalFillerWordsDetected} filler words detected. Practice maintaining eye contact and reducing disfluencies.";

                SaveSessionToSupabase(activeUserId, currentScenario, startTimeIso, endTimeIso, overallScore, clarityScore, eyeContactPct, gestureScore, overallWpm, totalFillerWordsDetected, gpm, totalSilenceTimeSeconds, aiSummary, turnRecords);
            },
            err => {
                // Fallback constructive coaching string if API request fails
                string fallbackSummary = $"Your overall performance scored {overallScore:F0}%. Speech clarity reached {clarityScore:F0}%. To improve, focus on reducing filler words ('um', 'uh') during speech pauses and keeping steady visual focus on your audience.";
                SaveSessionToSupabase(activeUserId, currentScenario, startTimeIso, endTimeIso, overallScore, clarityScore, eyeContactPct, gestureScore, overallWpm, totalFillerWordsDetected, gpm, totalSilenceTimeSeconds, fallbackSummary, turnRecords);
            }
        ));
    }

    // Helper method to execute the database save call cleanly
    private void SaveSessionToSupabase(
        string activeUserId, string currentScenario, string startTimeIso, string endTimeIso,
        float overallScore, float clarityScore, float eyeContactPct, float gestureScore,
        float overallWpm, int totalFillers, float gpm, float totalSilenceSec,
        string aiSummary, List<DialogueTurnRecord> turnRecords)
    {
        if (supabaseManager != null)
        {
            StartCoroutine(supabaseManager.SaveSessionNormalized(
                userId: activeUserId,
                scenarioType: currentScenario,
                selectedRole: activeConfig != null ? activeConfig.participantRole : "User",
                topic: activeConfig != null ? activeConfig.topic : "General",
                difficulty: activeConfig != null ? activeConfig.difficulty : "Medium",
                inputMode: UserSession.InputMode,
                startTime: startTimeIso,
                endTime: endTimeIso,
                durationSeconds: sessionElapsedTimeSeconds,
                overallScore: overallScore,
                clarityScore: clarityScore,
                eyeContactPct: eyeContactPct,
                gestureScore: gestureScore,
                wpm: overallWpm,
                totalFillers: totalFillers,
                gpm: gpm,
                totalSilenceSeconds: totalSilenceSec, // ADDED: Pass total silence duration
                feedbackSummary: aiSummary,           // Uses dynamic AI summary
                dialogueTurns: turnRecords,
                onSuccess: () =>
                {
                    Debug.Log("[InteractionManager] Session successfully saved to Supabase!");
                    uiManager.SetStatus("Session Saved!");
                    OnSessionCompletedSequence();
                },
                onError: (err) =>
                {
                    Debug.LogError($"[InteractionManager] Failed to save session: {err}");
                    uiManager.SetStatus("Save Failed!");
                    OnSessionCompletedSequence();
                }
            ));
        }
        else
        {
            Debug.LogWarning("[InteractionManager] SupabaseManager reference missing!");
            OnSessionCompletedSequence();
        }
    }

    public void ExitSession()
    {
        SetLocomotionEnabled(true);

        // Stop active mic hardware immediately on exit
        if (audioRecorder != null)
        {
            audioRecorder.StopRecording();
        }

        if (startConfigPanel != null)
        {
            startConfigPanel.SetActive(false);
        }

        // 1. If the session is actively running with dialogue history, calculate and save scores first
        if (isSessionActive && conversationHistory != null && conversationHistory.Count > 0)
        {
            isSessionActive = false;
            CalculateAndSaveResults();
        }
        else
        {

            // Set UI status when ending manually or exiting after conversation is complete
            if (uiManager != null)
            {
                uiManager.SetStatus("Chat ended. Transitioning to scenario...");
            }
            if (emotionPanel != null)
            {
                emotionPanel.SetActive(false);
            }

            isSessionActive = false;
            StopAllCoroutines();
            OnSessionCompletedSequence();
        }

    }

    private void OnSessionCompletedSequence()
    {
        isSessionActive = false;
        isSavingSession = false;
        SetLocomotionEnabled(true);

        if (hintManager != null)
        {
            hintManager.HideHintCanvas();            
        }

        if (audioRecorder != null)
        {
            audioRecorder.StopRecording();
        }

        if (emotionPanel != null)
        {
            emotionPanel.SetActive(false);
        }

        if (startConfigPanel != null)
        {
            startConfigPanel.SetActive(false);
        }

        bool isQuickExit = AppSystemManager.Instance != null && AppSystemManager.Instance.isQuickExitEnabled;

        if (objectiveManager != null)
        {
            objectiveManager.ShowExitObjective(isQuickExit);
        }

        if (uiManager != null)
        {
            uiManager.HideTurnCounter();
        }

        if (isQuickExit)
        {
            if (uiManager != null)
            {
                uiManager.SetStatus("Session Ended. Click 'Exit Session' on UI to leave or walk to the door.");
            }

            // Point UI exit button directly to scene transition
            if (exitButton != null)
            {
                exitButton.onClick.RemoveAllListeners();
                exitButton.onClick.AddListener(ConfirmExitFromDoor);
            }
        }
        else
        {
            if (uiManager != null)
            {
                if (UserSession.IsRunningTutorial)
                    uiManager.SetStatus("Tutorial Completed! Walk to the Exit Zone to begin your scenario.");
                else
                    uiManager.SetStatus("Session Ended. Walk to the Exit Zone to leave.");
            }
        }
    }

    // Call this method when the player interacts with or walks into the Exit Door / Zone
    public void ConfirmExitFromDoor()
    {
        Debug.Log("[InteractionManager] Door interaction triggered successfully!");
        StartCoroutine(ConfirmExitRoutine());
    }

    private IEnumerator ConfirmExitRoutine()
    {
        // 1. If results are still saving to database/metrics, wait briefly for completion
        while (isSavingSession)
        {
            if (uiManager != null) uiManager.SetStatus("Saving progress... Please wait.");
            yield return new WaitForSeconds(0.5f);
        }

        // 2. Disable session flag so interaction loops exit naturally
        isSessionActive = false;

        // DO NOT call StopAllCoroutines() here as it kills this exit coroutine!

        // 3. Determine the target scene
        string targetScene = defaultDashboardScene;

        if (UserSession.IsRunningTutorial || (activeConfig != null && activeConfig.scenario == "TutorialScene"))
        {
            UserSession.IsRunningTutorial = false; // Reset tutorial flag

            // Check if user pre-selected a scenario (e.g. Interview, Classroom) from the dashboard
            if (UserSession.SelectedScenarioConfig != null &&
                !string.IsNullOrEmpty(UserSession.SelectedScenarioConfig.scenario) &&
                UserSession.SelectedScenarioConfig.scenario != "TutorialScene")
            {
                targetScene = UserSession.SelectedScenarioConfig.scenario;
            }
        }

        if (targetScene == "PCDashboardScene") targetScene = defaultDashboardScene;

        Debug.Log($"[InteractionManager] Exiting to target scene: '{targetScene}'");

        // 4. Force load the target scene (Unity will automatically clean up scene coroutines)
        SceneManager.LoadScene(targetScene);
    }

    private List<DialogueTurnRecord> MapDialogueTurnsToRecords()
    {
        List<DialogueTurnRecord> records = new List<DialogueTurnRecord>();

        // Pair up User and NPC turns into single records if structured pairwise,
        // or map user turns directly
        for (int i = 0; i < conversationHistory.Count; i += 2)
        {
            string userText = conversationHistory[i].dialogue;
            string aiText = (i + 1 < conversationHistory.Count) ? conversationHistory[i + 1].dialogue : "";
            int fillers = conversationHistory[i].fillerWordCount;

            // Calculate WPM if duration > 0
            float userDurationMin = conversationHistory[i].speechDuration / 60.0f;
            int wordCount = userText.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            float wpm = userDurationMin > 0 ? wordCount / userDurationMin : 0.0f;

            records.Add(new DialogueTurnRecord
            {
                user_transcript = userText,
                ai_response_text = aiText,
                filler_word_count = fillers,
                speaking_rate_wpm = wpm
            });
        }

        return records;
    }

    public void SelectActiveNPC(NPCPersona targetNPC)
    {
        if (targetNPC == null) return;
        currentActiveNPC = targetNPC;
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
}