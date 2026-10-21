using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class InteractionManager : MonoBehaviour
{
    [Header("Core Dependencies")]
    [SerializeField] private OpenAIManager openAIManager;
    [SerializeField] private SupabaseManager supabaseManager;
    [SerializeField] private EyeContactTracker eyeContactTracker;
    [SerializeField] private GestureTracker gestureTracker;
    [SerializeField] private MicrophoneVolumeMeter micVolumeMeter;
    [SerializeField] private AudioSource npcAudioSource;

    [Header("UI References")]
    [SerializeField] private TMP_Text statusText;            // Single status line for all state prompts
    [SerializeField] private TMP_Text dialogueText;          // Scrollable full conversation log (User + NPC)
    [SerializeField] private ScrollRect dialogueScrollRect;   // Auto-scroll rect handler
    [SerializeField] private TMP_Text hintTextDisplay;
    [SerializeField] private Button exitButton;

    private ScenarioConfig activeConfig;
    private List<DialogueTurn> conversationHistory = new List<DialogueTurn>();
    private List<bool> relevancyTracker = new List<bool>();

    private AudioClip recordedClip;
    private bool isSessionActive = false;

    // Aggregate Analytics Tracking
    private float totalSpeakingTimeSeconds = 0.0f;
    private float totalSilenceTimeSeconds = 0.0f;
    private int totalFillerWordsDetected = 0;

    private void Start()
    {
        // 1. Request Android/VR Microphone Permission
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
        }
#endif

        // 2. Load active config from global UserSession state
        if (UserSession.IsRunningTutorial)
        {
            activeConfig = new ScenarioConfig
            {
                scenario = "Tutorial",
                topic = "System Calibration",
                difficulty = "Easy",
                participantRole = "Trainee",
                persona = "VR Guide",
                sessionObjective = "Practice hands-free speech and gestures.",
                maxTurnLimit = 2,
                sessionStartSignal = "Start Tutorial"
            };
        }
        else
        {
            activeConfig = UserSession.SelectedScenarioConfig;
        }

        if (exitButton != null) exitButton.onClick.AddListener(ExitSession);

        StartCoroutine(HandsFreeSessionInitiationRoutine());
    }

    // --- ALGORITHM 8: HANDS-FREE SESSION INITIATION (Status Text Countdown) ---
    private IEnumerator HandsFreeSessionInitiationRoutine()
    {
        int countdown = 10;
        while (countdown > 0)
        {
            SetStatus($"Starting in {countdown}s...");
            yield return new WaitForSeconds(1.0f);
            countdown--;
        }

        SetStatus("Session Started");
        StartCommunicationSession();
    }

    private void StartCommunicationSession()
    {
        isSessionActive = true;
        conversationHistory.Clear();
        relevancyTracker.Clear();

        totalSpeakingTimeSeconds = 0.0f;
        totalSilenceTimeSeconds = 0.0f;
        totalFillerWordsDetected = 0;

        if (eyeContactTracker != null) eyeContactTracker.StartTracking();
        if (gestureTracker != null) gestureTracker.ResetTracker();

        StartCoroutine(HandsFreeSpeechInteractionLoop());
    }

    // --- ALGORITHM 8: HANDS-FREE SPEECH INTERACTION LOOP ---
    // --- ALGORITHM 8: HANDS-FREE SPEECH INTERACTION LOOP (DYNAMIC SILENCE TRACKING) ---
    private IEnumerator HandsFreeSpeechInteractionLoop()
    {
        while (isSessionActive)
        {
            // 1. Wait for NPC to finish speaking
            while (npcAudioSource != null && npcAudioSource.isPlaying)
            {
                SetStatus("NPC Speaking...");
                yield return null;
            }

            SetStatus("Listening...");

            float turnStartTime = Time.time;

            // 2. Start Mic Recording & Live Meter
            recordedClip = Microphone.Start(null, false, 15, 44100);
            if (micVolumeMeter != null) micVolumeMeter.EnableVolumeMeter(true, recordedClip);

            yield return new WaitForSeconds(1.2f); // Speech detection buffer

            SetStatus("Recording...");

            float speechStart = Time.time;
            float silenceDuration = speechStart - turnStartTime;
            totalSilenceTimeSeconds += silenceDuration;

            // 3. Dynamic 3-Second VAD Silence Tracking Loop
            float dynamicSilenceTimer = 3.0f;
            float[] sampleBuffer = new float[128];

            while (dynamicSilenceTimer > 0.0f)
            {
                yield return new WaitForSeconds(0.1f);

                // Read live microphone volume
                int micPos = Microphone.GetPosition(null) - 129;
                if (micPos >= 0 && recordedClip != null)
                {
                    recordedClip.GetData(sampleBuffer, micPos);
                    float volumeSum = 0.0f;
                    for (int i = 0; i < 128; i++) volumeSum += Mathf.Abs(sampleBuffer[i]);
                    float averageLoudness = volumeSum / 128.0f;

                    // Reset silence countdown if user is speaking (above threshold)
                    if (averageLoudness > 0.02f)
                    {
                        dynamicSilenceTimer = 3.0f; // Reset 3s buffer
                    }
                    else
                    {
                        dynamicSilenceTimer -= 0.1f; // Decrement when silent
                    }
                }
                else
                {
                    dynamicSilenceTimer -= 0.1f;
                }
            }

            // 4. Stop Microphone Recording
            Microphone.End(null);
            if (micVolumeMeter != null) micVolumeMeter.EnableVolumeMeter(false, null);

            float speechDuration = Time.time - speechStart;
            totalSpeakingTimeSeconds += speechDuration;

            // 5. Process user speech turn
            byte[] wavBytes = SavWav.GetWavBytes(recordedClip);
            yield return ProcessUserAudioRoutine(wavBytes, speechDuration, silenceDuration);
        }
    }

    private IEnumerator ProcessUserAudioRoutine(byte[] wavBytes, float speechDuration, float silenceDuration)
    {
        SetStatus("Transcribing...");

        string fillerPrompt = "Um, uh, eh, ah, er, hmm. Preserve spoken disfluencies and filler words.";
        bool sttDone = false;
        string userTranscript = "";

        StartCoroutine(openAIManager.TranscribeAudioWithPrompt(
            wavBytes,
            fillerPrompt,
            onSuccess: (transcript) =>
            {
                userTranscript = transcript;
                sttDone = true;
            },
            onError: (err) =>
            {
                Debug.LogError($"[STT Error] {err}");
                SetStatus("Speech Error. Try Again.");
                sttDone = true;
            }
        ));

        while (!sttDone) yield return null;

        if (!string.IsNullOrEmpty(userTranscript))
        {
            int turnFillers = MetricsCalculator.DetectFillerWords(userTranscript);
            totalFillerWordsDetected += turnFillers;

            // Add User speech to history & UI
            conversationHistory.Add(new DialogueTurn
            {
                speaker = "User",
                dialogue = userTranscript,
                speechDuration = speechDuration,
                silenceDuration = silenceDuration,
                fillerWordCount = turnFillers
            });

            UpdateDialogueUI();
            yield return ProcessLLMTurnRoutine(userTranscript);
        }
        else
        {
            SetStatus("No Speech Detected.");
            yield return new WaitForSeconds(1.5f);
        }
    }

    private IEnumerator ProcessLLMTurnRoutine(string userTranscript)
    {
        SetStatus("Thinking...");

        int completedTurns = conversationHistory.Count / 2;
        string systemPrompt = PromptBuilder.BuildSystemPrompt(activeConfig, completedTurns);
        bool llmDone = false;

        StartCoroutine(openAIManager.SendChatRequest(
            systemPrompt,
            conversationHistory,
            userTranscript,
            onSuccess: (response) =>
            {
                // Add NPC response to history & UI
                conversationHistory.Add(new DialogueTurn
                {
                    speaker = "NPC",
                    dialogue = response.npc_response,
                    speechDuration = 0.0f,
                    silenceDuration = 0.0f,
                    fillerWordCount = 0
                });

                relevancyTracker.Add(response.is_relevant);
                UpdateDialogueUI();

                // Play Audio
                StartCoroutine(openAIManager.TextToSpeech(response.npc_response, "alloy", clip =>
                {
                    if (npcAudioSource != null)
                    {
                        npcAudioSource.clip = clip;
                        npcAudioSource.Play();
                    }
                }, err => Debug.LogError($"[TTS Error] {err}")));

                if (response.end_conversation)
                {
                    isSessionActive = false;
                    CalculateAndSaveResults();
                }

                llmDone = true;
            },
            onError: (err) =>
            {
                Debug.LogError($"[LLM Error] {err}");
                SetStatus("Server Error.");
                llmDone = true;
            }
        ));

        while (!llmDone) yield return null;
    }

    // --- CONCISE STATUS & DIALOGUE LOG HELPERS ---
    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    private void UpdateDialogueUI()
    {
        if (dialogueText == null) return;

        string fullLog = "";
        foreach (var turn in conversationHistory)
        {
            if (turn.speaker == "User")
            {
                fullLog += $"<b>You:</b> {turn.dialogue}\n\n";
            }
            else
            {
                fullLog += $"<b>NPC:</b> {turn.dialogue}\n\n";
            }
        }

        dialogueText.text = fullLog;

        // Auto-scroll to bottom
        if (dialogueScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            dialogueScrollRect.verticalNormalizedPosition = 0.0f;
        }
    }

    // --- ALGORITHM 7: TRIGGERED BY GESTURE TRACKER ---
    public void TriggerHintGeneration()
    {
        string hintPrompt = PromptBuilder.BuildHintPrompt(activeConfig, conversationHistory);

        StartCoroutine(openAIManager.SendHintRequest(
            hintPrompt,
            onSuccess: (hint) =>
            {
                if (hintTextDisplay != null)
                {
                    hintTextDisplay.gameObject.SetActive(true);
                    hintTextDisplay.text = $"[HINT]: {hint}";
                    StartCoroutine(HideHintAfterDelay(6.0f));
                }
            },
            onError: (err) => Debug.LogError($"[Hint Error] {err}")
        ));
    }

    private IEnumerator HideHintAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (hintTextDisplay != null) hintTextDisplay.gameObject.SetActive(false);
    }

    private void CalculateAndSaveResults()
    {
        SetStatus("Saving Results...");

        if (eyeContactTracker != null) eyeContactTracker.StopTracking();
        if (gestureTracker != null) gestureTracker.StopTracking();

        float eyeContactScore = eyeContactTracker != null ? eyeContactTracker.CalculateEyeContactScore() : 0.0f;
        var (gestureCount, gpm) = gestureTracker != null ? gestureTracker.CalculateGestureMetrics() : (0, 0.0f);

        string fullUserTranscript = "";
        foreach (var turn in conversationHistory)
        {
            if (turn.speaker == "User") fullUserTranscript += turn.dialogue + " ";
        }

        var (totalWords, averageWpm) = MetricsCalculator.CalculateSpeakingRate(fullUserTranscript, totalSpeakingTimeSeconds);
        var (speechClarityScore, fwpm) = MetricsCalculator.CalculateSpeechClarity(totalFillerWordsDetected, totalSpeakingTimeSeconds, averageWpm);

        float gestureScore = Mathf.Clamp(gpm * 10.0f, 0.0f, 100.0f);
        float overallScore = MetricsCalculator.CalculateOverallScore(activeConfig.scenario, speechClarityScore, eyeContactScore, gestureScore);

        float relevanceScore = relevancyTracker.Count > 0
            ? ((float)relevancyTracker.FindAll(x => x).Count / relevancyTracker.Count) * 100.0f
            : 0.0f;

        string activeUserId = UserSession.IsLoggedIn ? UserSession.CurrentUserId : "UNAUTHENTICATED_USER";

        SessionResultPayload payload = new SessionResultPayload
        {
            user_id = activeUserId,
            scenario_name = activeConfig.scenario,
            input_mode = UserSession.InputMode,
            topic = activeConfig.topic,
            difficulty = activeConfig.difficulty,
            total_turns = relevancyTracker.Count,
            relevant_turns = relevancyTracker.FindAll(x => x).Count,
            relevance_score = relevanceScore,
            average_wpm = averageWpm,
            total_silence_sec = totalSilenceTimeSeconds,
            eye_contact_score = eyeContactScore,
            filler_word_count = totalFillerWordsDetected,
            fwpm = fwpm,
            speech_clarity_score = speechClarityScore,
            expressive_gesture_count = gestureCount,
            gestures_per_minute = gpm,
            overall_performance_score = overallScore,
            performance_rating = overallScore >= 80.0f ? "Excellent" : (overallScore >= 60.0f ? "Satisfactory" : "Needs Improvement"),
            conversation_log = Newtonsoft.Json.JsonConvert.SerializeObject(conversationHistory)
        };

        StartCoroutine(supabaseManager.SaveSessionResult(payload, () =>
        {
            SetStatus("Session Complete!");
            OnSessionCompletedSequence();
        }, err =>
        {
            Debug.LogError($"[Supabase Error] {err}");
            OnSessionCompletedSequence();
        }));
    }

    // --- UPDATED MANUAL EXIT (Stops conversation loop, keeps VR scene active) ---
    public void ExitSession()
    {
        if (!isSessionActive) return;

        isSessionActive = false;
        StopAllCoroutines();

        if (eyeContactTracker != null) eyeContactTracker.StopTracking();
        if (gestureTracker != null) gestureTracker.StopTracking();
        if (micVolumeMeter != null) micVolumeMeter.EnableVolumeMeter(false, null);

        SetStatus("Chat Ended. Explore or head to the Exit Door.");
        Debug.Log("[InteractionManager] Dialogue session stopped. User is now free to walk around.");
    }

    // --- UPDATED AUTOMATIC SESSION COMPLETION ---
    private void OnSessionCompletedSequence()
    {
        if (UserSession.IsRunningTutorial)
        {
            UserSession.IsRunningTutorial = false;
            string mainScenario = UserSession.SelectedScenarioConfig.scenario;
            SceneManager.LoadScene(mainScenario);
        }
        else
        {
            // Prompt user to physically move to the exit door instead of auto-switching scenes
            SetStatus("Session Finished! Please proceed to the Exit Door.");
        }
    }

    // --- DYNAMIC EXIT DOOR ROUTE ---
    public void ConfirmExitFromDoor()
    {
        if (UserSession.IsRunningTutorial)
        {
            // Path A: Tutorial complete/exited -> Progress into selected Main Scenario
            UserSession.IsRunningTutorial = false;
            string mainScenarioScene = UserSession.SelectedScenarioConfig.scenario;

            Debug.Log($"[ExitDoor] Tutorial complete. Transitioning to Main Scenario: {mainScenarioScene}");
            SceneManager.LoadScene(mainScenarioScene);
        }
        else
        {
            // Path B: Main Scenario complete/exited -> Transition to PC Dashboard
            Debug.Log("[ExitDoor] Main Scenario complete. Transitioning to PCDashboardScene.");
            SceneManager.LoadScene("PCDashboardScene");
        }
    }
}