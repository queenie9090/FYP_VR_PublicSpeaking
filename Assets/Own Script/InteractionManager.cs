using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

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

    [Header("Audio Output")]
    [SerializeField] private AudioSource npcAudioSource;
    [SerializeField] private NPCPersona currentActiveNPC;

    [Header("NPC Emotion")]
    [SerializeField] private NPCEmotionController npcEmotionController;

    private string previousEmotion = "NEUTRAL";

    [Header("UI Controls")]
    [SerializeField] private GameObject startConfigPanel;
    [SerializeField] private GameObject emotionPanel;
    [SerializeField] private Button startSessionButton;
    [SerializeField] private Button exitButton;

    [Header("Navigation")]
    [SerializeField] private string defaultDashboardScene = "DashboardScene";

    private ScenarioConfig activeConfig;
    private List<DialogueTurn> conversationHistory = new List<DialogueTurn>();
    private bool isSessionActive = false;

    private float sessionElapsedTimeSeconds = 0.0f;
    private float totalSpeakingTimeSeconds = 0.0f;
    private float totalSilenceTimeSeconds = 0.0f;
    private int totalFillerWordsDetected = 0;
    private DateTime sessionStartTimestamp;
    private DateTime sessionEndTimestamp;

    private void Start()
    {
        InitScenarioConfig();

        if (exitButton != null) exitButton.onClick.AddListener(ExitSession);
        if (startSessionButton != null)
        {
            startSessionButton.onClick.RemoveAllListeners();
            startSessionButton.onClick.AddListener(OnUserConfirmedStart);
            uiManager.SetStatus("Press 'Start Conversation' to begin.");
        }
    }

    private void InitScenarioConfig()
    {
        if (UserSession.IsRunningTutorial)
        {
            activeConfig = new ScenarioConfig
            {
                scenario = "TutorialScene",
                topic = "System Calibration",
                difficulty = "Easy",
                participantRole = "Trainee",
                persona = "VR Guide",
                sessionObjective = "Practice speech.",
                maxTurnLimit = 2,
                sessionStartSignal = "Start Tutorial"
            };
        }
        else
        {
            activeConfig = UserSession.SelectedScenarioConfig;
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

        // Ensure global session mode accurately reflects Hands-Free mode
        UserSession.InputMode = "Hands-Free";

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
        if (emotionPanel != null) emotionPanel.SetActive(true);
        sessionElapsedTimeSeconds = 0f; totalSpeakingTimeSeconds = 0f; totalSilenceTimeSeconds = 0f; totalFillerWordsDetected = 0;
        previousEmotion = "NEUTRAL";

        // Capture precise start time (ISO 8601 string format for Supabase timestamptz)
        sessionStartTimestamp = DateTime.UtcNow;

        if (npcEmotionController != null) npcEmotionController.SetEmotion("NEUTRAL", 0);
        if (eyeContactTracker != null) eyeContactTracker.StartTracking();
        if (gestureTracker != null) gestureTracker.ResetTracker();

        StartCoroutine(TrackSessionTimerRoutine());
        StartCoroutine(HandsFreeSpeechInteractionLoop());
    }

    private IEnumerator TrackSessionTimerRoutine()
    {
        while (isSessionActive)
        {
            sessionElapsedTimeSeconds += Time.deltaTime;
            uiManager.UpdateTimerDisplay(sessionElapsedTimeSeconds);
            yield return null;
        }
    }

    private IEnumerator HandsFreeSpeechInteractionLoop()
    {
        while (isSessionActive)
        {
            while (npcAudioSource != null && npcAudioSource.isPlaying)
            {
                uiManager.SetStatus("NPC Speaking...");
                yield return null;
            }

            if (!isSessionActive) yield break;

            uiManager.SetStatus("Listening...");

            byte[] wavBytes = null;
            float speechDuration = 0f, silenceDuration = 0f;
            bool recDone = false;

            StartCoroutine(audioRecorder.RecordUserSpeechRoutine((bytes, sDuration, silDuration) => {
                wavBytes = bytes; speechDuration = sDuration; silenceDuration = silDuration;
                recDone = true;
            }));

            uiManager.SetStatus("Recording...");
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

        uiManager.SetStatus("NPC Speaking...");
        yield return StartCoroutine(PlayNPCAudioRoutine(response.npc_response));

        if ((activeConfig != null && turnCounter >= activeConfig.maxTurnLimit) || (response != null && response.end_conversation))
        {
            isSessionActive = false;
            CalculateAndSaveResults();
            yield break;
        }

        uiManager.SetStatus("Recording...");
    }

    private IEnumerator PlayNPCAudioRoutine(string textToSpeak)
    {
        bool downloaded = false; AudioClip clip = null;
        string voice = (currentActiveNPC != null && !string.IsNullOrEmpty(currentActiveNPC.voiceId)) ? currentActiveNPC.voiceId : "nova";

        StartCoroutine(openAIManager.TextToSpeech(textToSpeak, voice, c => { clip = c; downloaded = true; }, err => downloaded = true));
        yield return new WaitUntil(() => downloaded);

        if (clip != null)
        {
            AudioSource targetSource = (currentActiveNPC != null && currentActiveNPC.npcAudioSource != null) ? currentActiveNPC.npcAudioSource : npcAudioSource;
            targetSource.clip = clip;
            targetSource.Play();
            yield return new WaitForSeconds(0.1f);
            while (targetSource != null && targetSource.isPlaying) yield return null;
        }
    }

    /// <summary>
    /// ALGORITHM 7: Hand Gesture Hint Activation Callback Method
    /// Invoked when GestureTracker detects an Open Palm > 1.0s.
    /// </summary>
    public void TriggerHintActivation()
    {
        if (!isSessionActive) return;

        uiManager.SetStatus("Generating Hint...");
        string lastUserScript = conversationHistory.Count > 0 ? conversationHistory[conversationHistory.Count - 1].dialogue : "";

        // Fix CS7036: Pass conversationHistory into BuildHintPrompt
        StartCoroutine(openAIManager.SendChatRequest(
            PromptBuilder.BuildHintPrompt(activeConfig, conversationHistory), conversationHistory, lastUserScript,
            res => {
                if (res != null && !string.IsNullOrEmpty(res.npc_response))
                {
                    // Fix CS1061: Render hint to UI
                    uiManager.SetStatus($"Hint: {res.npc_response}");
                }
            },
            err => Debug.LogError($"[Hint Error] {err}")
        ));
    }

    private void CalculateAndSaveResults()
    {
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

        // Refined Prompt explicitly requesting coaching suggestions
        string summaryPrompt = $"You are an expert VR speech coach. Provide a concise 2-3 sentence constructive feedback summary with actionable suggestions for improvement based on this participant's performance in a '{currentScenario}' simulation: " +
                               $"Overall Score: {overallScore:F0}%, Speech Clarity: {clarityScore:F0}%, Speaking Rate: {overallWpm:F0} WPM, " +
                               $"Filler Words Used: {totalFillerWordsDetected}, Eye Contact: {eyeContactPct:F0}%, Gestures Rate: {gpm:F1} GPM.";

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
        if (!isSessionActive) return;
        isSessionActive = false;
        StopAllCoroutines();
        uiManager.SetStatus("Chat Ended.");

        if (emotionPanel != null) emotionPanel.SetActive(false);
    }

    private void OnSessionCompletedSequence()
    {
        isSessionActive = false;

        if (emotionPanel != null)
        {
            emotionPanel.SetActive(false);
        }

        if (UserSession.IsRunningTutorial)
        {
            if (uiManager != null)
            {
                uiManager.SetStatus("Tutorial Completed! Walk to the Exit Zone to begin your scenario.");
            }
        }
        else
        {
            if (uiManager != null)
            {
                uiManager.SetStatus("Session Ended. Walk to the Exit Zone to leave.");
            }
        }
    }

    // Call this method when the player interacts with or walks into the Exit Door / Zone
    public void ConfirmExitFromDoor()
    {
        isSessionActive = false;
        StopAllCoroutines();

        string targetScene = defaultDashboardScene; // "DashboardScene"

        // Check if exiting from Tutorial
        if (UserSession.IsRunningTutorial || (activeConfig != null && activeConfig.scenario == "TutorialScene"))
        {
            UserSession.IsRunningTutorial = false; // Turn off tutorial flag

            // Route to the scenario selected before entering tutorial (if set)
            if (UserSession.SelectedScenarioConfig != null &&
                !string.IsNullOrEmpty(UserSession.SelectedScenarioConfig.scenario) &&
                UserSession.SelectedScenarioConfig.scenario != "TutorialScene")
            {
                targetScene = UserSession.SelectedScenarioConfig.scenario;
            }
        }
        else
        {
            // Exiting from regular scenario -> ALWAYS go to Dashboard
            targetScene = defaultDashboardScene;
        }

        // Safety check: Fix old scene names dynamically
        if (targetScene == "PCDashboardScene")
        {
            targetScene = defaultDashboardScene; // Force "DashboardScene"
        }

        Debug.Log($"[InteractionManager] Exiting scenario. Transitioning to target scene: {targetScene}");
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
}