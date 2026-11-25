using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpeechInteractionController : MonoBehaviour
{
    private int speechRequestVersion;
    private int transcriptionRequestVersion;
    private AudioSource activeSpeechSource;
    private NPCEmotionController activeSpeechEmotion;
    public bool IsSpeechPlaying => activeSpeechSource != null && activeSpeechSource.isPlaying;
    public bool IsPlayingSpeechFor(NPCEmotionController source) => source != null && activeSpeechEmotion == source && IsSpeechPlaying;
    private Animator activeSpeechAnimator;
    private NPCBodyAnimation activeSpeechBody;
    private NPCEyeContactController activeSpeechEyes;
    private bool activeTalkingParameter;

    [Header("Retry Settings")]
    [SerializeField] private int maxSilentAttempts = 3;
    [SerializeField] private float retryPauseSeconds = 0.5f;

    private int captureRequestVersion;

    public IEnumerator CaptureUserTurnWithRetriesRoutine(
        List<DialogueTurn> conversationHistory,
        Action<int> onFillersDetected,
        Action<string, float, float> onTurnCaptured,
        Action onFatalMicError,
        Action onListeningAttemptStarted = null,
        Func<bool> shouldCancel = null)
    {
        int requestVersion = ++captureRequestVersion;

        if (audioRecorder == null)
        {
            if (uiManager != null) uiManager.SetStatus("Audio recorder missing.");
            Debug.LogError("[SpeechInteractionController] AudioRecorder reference is null.");
            onFatalMicError?.Invoke();
            yield break;
        }

        int maxAttempts = Mathf.Max(1, maxSilentAttempts);
        int sttFailures = 0;
        float totalSpeechDuration = 0f;
        float totalSilenceDuration = 0f;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (requestVersion != captureRequestVersion || (shouldCancel != null && shouldCancel()))
                yield break;

            onListeningAttemptStarted?.Invoke();

            string statusPrefix = attempt switch
            {
                1 => "Recording",
                _ when attempt < maxAttempts => $"Re-listening ({attempt}/{maxAttempts}) — please speak clearly",
                _ => $"Final attempt ({attempt}/{maxAttempts}) — please speak louder"
            };

            if (uiManager != null)
                uiManager.SetStatus(attempt == 1 ? "Listening..." : $"{statusPrefix}...");

            byte[] wavBytes = null;
            float speechDuration = 0f, silenceDuration = 0f;
            bool recDone = false;

            Coroutine recRoutine = StartCoroutine(audioRecorder.RecordUserSpeechRoutine(
                (bytes, sDuration, silDuration) => {
                    wavBytes = bytes;
                    speechDuration = sDuration;
                    silenceDuration = silDuration;
                    recDone = true;
                },
                (currentSec, maxSec) => {
                    if (requestVersion != captureRequestVersion) return;
                    int remainingSec = Mathf.CeilToInt(maxSec - currentSec);
                    if (uiManager != null)
                        uiManager.SetStatus($"{statusPrefix}... {remainingSec}s remaining");
                }
            ));

            while (!recDone)
            {
                if (requestVersion != captureRequestVersion || (shouldCancel != null && shouldCancel()))
                {
                    if (recRoutine != null) StopCoroutine(recRoutine);
                    audioRecorder.StopRecording();
                    yield break;
                }
                yield return null;
            }

            if (wavBytes == null || wavBytes.Length == 0)
            {
                if (uiManager != null)
                    uiManager.SetStatus("Microphone unavailable. Check the microphone and restart the session.");
                Debug.LogError("[SpeechInteractionController] No microphone audio was recorded.");
                onFatalMicError?.Invoke();
                yield break;
            }

            totalSpeechDuration += speechDuration;
            totalSilenceDuration += silenceDuration;

            bool sttFailed = false;
            int historyCountBefore = conversationHistory.Count;

            yield return StartCoroutine(TranscribeUserAudioRoutine(
                wavBytes, speechDuration, silenceDuration, conversationHistory,
                onFillersDetected,
                onTranscriptionComplete: null,
                onTranscriptionFailed: () => sttFailed = true
            ));

            if (requestVersion != captureRequestVersion || (shouldCancel != null && shouldCancel()))
                yield break;

            if (sttFailed)
            {
                if (++sttFailures >= 3)
                {
                    if (uiManager != null)
                        uiManager.SetStatus("Speech recognition unavailable. Check your connection.");
                    onFatalMicError?.Invoke();
                    yield break;
                }

                if (uiManager != null)
                    uiManager.SetStatus("Speech recognition failed. Please try speaking again.");
                yield return new WaitForSeconds(1.5f);
                attempt--;
                continue;
            }

            bool hasSpeech = conversationHistory.Count > historyCountBefore
                && conversationHistory[conversationHistory.Count - 1].speaker == "User"
                && !string.IsNullOrWhiteSpace(conversationHistory[conversationHistory.Count - 1].dialogue);

            if (hasSpeech)
            {
                DialogueTurn latestTurn = conversationHistory[conversationHistory.Count - 1];
                latestTurn.speechDuration = totalSpeechDuration;
                latestTurn.silenceDuration = totalSilenceDuration;

                onTurnCaptured?.Invoke(latestTurn.dialogue, totalSpeechDuration, totalSilenceDuration);
                yield break;
            }

            if (attempt < maxAttempts)
            {
                if (uiManager != null)
                    uiManager.SetStatus($"No speech detected ({attempt}/{maxAttempts}). Re-listening, please speak louder...");

                yield return new WaitForSeconds(retryPauseSeconds);
            }
        }

        if (requestVersion == captureRequestVersion)
            onTurnCaptured?.Invoke("", totalSpeechDuration, totalSilenceDuration);
    }

    public void CancelPendingSpeech()
    {
        speechRequestVersion++;
        transcriptionRequestVersion++;
        captureRequestVersion++;
        StopActiveSpeech();
    }

    private void StopActiveSpeech()
    {
        if (activeSpeechSource != null) activeSpeechSource.Stop();
        if (activeTalkingParameter && activeSpeechAnimator != null)
            activeSpeechAnimator.SetBool("IsTalking", false);
        if (activeSpeechBody != null) activeSpeechBody.SetActivity(NPCBodyActivity.IDLE);
        if (activeSpeechEyes != null) activeSpeechEyes.SetActivity(NPCBodyActivity.IDLE);
        activeSpeechSource = null;
        activeSpeechEmotion = null;
        activeSpeechAnimator = null;
        activeSpeechBody = null;
        activeSpeechEyes = null;
        activeTalkingParameter = false;
    }
    [Header("Dependencies")]
    [SerializeField] private OpenAIManager openAIManager;
    [SerializeField] private AudioRecorder audioRecorder;
    [SerializeField] private DialogueUIManager uiManager;

    public void InitializeDependencies(OpenAIManager openAI, AudioRecorder recorder, DialogueUIManager ui)
    {
        openAIManager = openAI;
        audioRecorder = recorder;
        uiManager = ui;
    }

    public IEnumerator TranscribeUserAudioRoutine(
        byte[] wavBytes,
        float speechDuration,
        float silenceDuration,
        List<DialogueTurn> conversationHistory,
        Action<int> onFillersDetected,
        Action onTranscriptionComplete,
        Action onTranscriptionFailed = null)
    {
        int requestVersion = ++transcriptionRequestVersion;
        if (uiManager != null) uiManager.SetStatus("Transcribing...");

        bool sttDone = false;
        bool sttFailed = false;
        string userTranscript = "";

        StartCoroutine(openAIManager.TranscribeAudioWithPrompt(wavBytes, "Um, uh, er.", transcript => {
            userTranscript = transcript;
            sttDone = true;
        }, err => {
            Debug.LogError($"[STT Error] {err}");
            if (uiManager != null) uiManager.SetStatus("Speech Error.");
            sttFailed = true;
            sttDone = true;
        }, () => this == null || requestVersion != transcriptionRequestVersion));

        yield return new WaitUntil(() => sttDone || requestVersion != transcriptionRequestVersion);
        if (requestVersion != transcriptionRequestVersion) yield break;

        if (sttFailed)
        {
            onTranscriptionFailed?.Invoke();
            onTranscriptionComplete?.Invoke();
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(userTranscript))
        {
            int turnFillers = MetricsCalculator.DetectFillerWords(userTranscript);
            onFillersDetected?.Invoke(turnFillers);

            conversationHistory.Add(new DialogueTurn
            {
                speaker = "User",
                dialogue = userTranscript,
                speechDuration = speechDuration,
                silenceDuration = silenceDuration,
                fillerWordCount = turnFillers
            });

            if (uiManager != null) uiManager.RenderDialogue(conversationHistory);
        }
        else
        {
            if (uiManager != null) uiManager.SetStatus("No Speech Detected.");
            yield return new WaitForSeconds(1.5f);
        }
      
        if (requestVersion == transcriptionRequestVersion) onTranscriptionComplete?.Invoke();
    }

    public IEnumerator PlayNPCAudioRoutine(
        string textToSpeak,
        NPCPersona currentNPC,
        AudioSource fallbackAudio,
        int turnNumber = 0,
        Action<string> onPartialTextUpdated = null)
    {
        int requestVersion = ++speechRequestVersion;
        StopActiveSpeech();
        bool downloaded = false;
        AudioClip clip = null;

        string speakerName = (currentNPC != null && !string.IsNullOrWhiteSpace(currentNPC.characterName))
            ? currentNPC.characterName
            : (currentNPC != null ? currentNPC.name : "NPC");

        string voice = (currentNPC != null && !string.IsNullOrEmpty(currentNPC.voiceId))
            ? currentNPC.voiceId
            : "nova";

        StartCoroutine(openAIManager.TextToSpeech(textToSpeak, voice, c => { clip = c; downloaded = true; },
            err => downloaded = true, () => this == null || requestVersion != speechRequestVersion));
        yield return new WaitUntil(() => downloaded || requestVersion != speechRequestVersion);
        if (requestVersion != speechRequestVersion) yield break;

        var emotionSource = currentNPC != null
            ? currentNPC.GetComponentInParent<NPCEmotionController>() : null;

        if (clip == null)
        {
            NPCEmotionSessionManager.Instance?.RecordRealismEvent(
                emotionSource, "voice", "generation_failed", text: textToSpeak);
            // TTS failed: still reveal the full NPC line so the user can read it
            onPartialTextUpdated?.Invoke(textToSpeak);
            yield break;
        }

        AudioSource targetSource = (currentNPC != null && currentNPC.npcAudioSource != null)
            ? currentNPC.npcAudioSource
            : fallbackAudio;

        Animator npcAnimator = currentNPC != null ? currentNPC.GetComponentInParent<Animator>() : null;
        bool hasTalkingParameter = npcAnimator != null && Array.Exists(npcAnimator.parameters,
            parameter => parameter.name == "IsTalking" && parameter.type == AnimatorControllerParameterType.Bool);
        var body = currentNPC != null ? currentNPC.GetComponentInParent<NPCBodyAnimation>() : null;
        var eyes = currentNPC != null ? currentNPC.GetComponentInParent<NPCEyeContactController>() : null;

        if (targetSource == null)
        {
            Debug.LogError("[Speech] NPC audio source is missing; response could not be played.");
            onPartialTextUpdated?.Invoke(textToSpeak);
            yield break;
        }

        if (currentNPC != null)
        {
            foreach (var lipSync in currentNPC.GetComponentsInParent<NPCLipSync>(true))
                if (lipSync.isActiveAndEnabled) lipSync.SetAudioSource(targetSource);
        }

        try
        {
            targetSource.clip = clip;
            targetSource.Play();
            if (!targetSource.isPlaying)
            {
                Debug.LogWarning("[Speech] NPC audio playback did not start.");
                NPCEmotionSessionManager.Instance?.RecordRealismEvent(
                    emotionSource, "voice", "playback_failed", text: textToSpeak);
                onPartialTextUpdated?.Invoke(textToSpeak);
                yield break;
            }

            activeSpeechSource = targetSource;
            activeSpeechEmotion = emotionSource;
            activeSpeechAnimator = npcAnimator;
            activeSpeechBody = body;
            activeSpeechEyes = eyes;
            activeTalkingParameter = hasTalkingParameter;

            if (uiManager != null) uiManager.SetStatus($"{speakerName} Speaking...");
            NPCEmotionSessionManager.Instance?.SetRealismContext(emotionSource,
                currentNPC != null ? currentNPC.name : null, "SPEAKING", turnNumber);
            NPCEmotionSessionManager.Instance?.RecordRealismEvent(
                emotionSource, "voice", "playback_started", text: textToSpeak);

            if (hasTalkingParameter) npcAnimator.SetBool("IsTalking", true);
            if (body != null) body.SetActivity(NPCBodyActivity.SPEAKING, turnNumber);
            if (eyes != null) eyes.SetActivity(NPCBodyActivity.SPEAKING);

            string[] words = string.IsNullOrEmpty(textToSpeak)
                ? Array.Empty<string>()
                : textToSpeak.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int revealedWordCount = 0;

            if (words.Length > 0)
            {
                revealedWordCount = 1;
                onPartialTextUpdated?.Invoke(words[0]);
            }

            yield return new WaitForSeconds(0.1f);

            while (requestVersion == speechRequestVersion && targetSource != null && targetSource.isPlaying)
            {
                if (words.Length > 0 && clip.length > 0f)
                {
                    float progress = Mathf.Clamp01(targetSource.time / clip.length);
                    int targetWords = Mathf.Clamp(Mathf.CeilToInt(progress * words.Length), 1, words.Length);

                    if (targetWords != revealedWordCount)
                    {
                        revealedWordCount = targetWords;
                        onPartialTextUpdated?.Invoke(string.Join(" ", words, 0, revealedWordCount));
                    }
                }
                yield return null;
            }

            if (requestVersion == speechRequestVersion)
            {
                onPartialTextUpdated?.Invoke(textToSpeak);
            }
        }
        finally
        {
            if (requestVersion == speechRequestVersion) StopActiveSpeech();
        }
    }
}
