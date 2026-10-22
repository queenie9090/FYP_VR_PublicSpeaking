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

    [Header("UI Controls")]
    [SerializeField] private Button startSessionButton;
    [SerializeField] private Button exitButton;

    private ScenarioConfig activeConfig;
    private List<DialogueTurn> conversationHistory = new List<DialogueTurn>();
    private bool isSessionActive = false;

    private float sessionElapsedTimeSeconds = 0.0f;
    private float totalSpeakingTimeSeconds = 0.0f;
    private float totalSilenceTimeSeconds = 0.0f;
    private int totalFillerWordsDetected = 0;

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
                scenario = "Tutorial",
                topic = "System Calibration",
                difficulty = "Easy",
                participantRole = "Trainee",
                persona = "VR Guide",
                sessionObjective = "Practice speech.",
                maxTurnLimit = 2,
                sessionStartSignal = "Start Tutorial"
            };
        }
        else activeConfig = UserSession.SelectedScenarioConfig;
    }

    public void OnUserConfirmedStart()
    {
        if (startSessionButton != null) startSessionButton.interactable = false;
        if (playerSeating != null) playerSeating.SnapPlayerToChair();
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
        sessionElapsedTimeSeconds = 0f; totalSpeakingTimeSeconds = 0f; totalSilenceTimeSeconds = 0f; totalFillerWordsDetected = 0;

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
        int turnCounter = (conversationHistory.Count / 2) + 1;

        LLMResponse response = null; bool llmDone = false;
        StartCoroutine(openAIManager.SendChatRequest(
            PromptBuilder.BuildSystemPrompt(activeConfig, turnCounter), conversationHistory, userTranscript,
            res => { response = res; llmDone = true; },
            err => { Debug.LogError($"[LLM Error] {err}"); llmDone = true; }
        ));

        yield return new WaitUntil(() => llmDone);
        if (response == null || string.IsNullOrEmpty(response.npc_response)) yield break;

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

    private void CalculateAndSaveResults()
    {
        uiManager.SetStatus("Saving Results...");
        if (eyeContactTracker != null) eyeContactTracker.StopTracking();
        if (gestureTracker != null) gestureTracker.StopTracking();

        if (UserSession.IsRunningTutorial)
        {
            uiManager.SetStatus("Calibration Complete!");
            OnSessionCompletedSequence();
            return;
        }

        // Aggregate metric calculations...
        string activeUserId = (UserSession.IsLoggedIn && !string.IsNullOrEmpty(UserSession.CurrentUserId))
            ? UserSession.CurrentUserId
            : "00000000-0000-0000-0000-000000000000";

        // Save to Supabase...
        uiManager.SetStatus("Session Complete!");
        OnSessionCompletedSequence();
    }

    public void ExitSession()
    {
        if (!isSessionActive) return;
        isSessionActive = false;
        StopAllCoroutines();
        uiManager.SetStatus("Chat Ended.");
    }

    private void OnSessionCompletedSequence()
    {
        if (UserSession.IsRunningTutorial)
        {
            UserSession.IsRunningTutorial = false;
            SceneManager.LoadScene(UserSession.SelectedScenarioConfig != null ? UserSession.SelectedScenarioConfig.scenario : "PCDashboardScene");
        }
        else
        {
            uiManager.SetStatus("Session Finished! Please proceed to the Exit Door.");
        }
    }
}