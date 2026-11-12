using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpeechInteractionController : MonoBehaviour
{
    private int speechRequestVersion;
    private int transcriptionRequestVersion;
    private AudioSource activeSpeechSource;
    private Animator activeSpeechAnimator;
    private NPCBodyAnimation activeSpeechBody;
    private NPCEyeContactController activeSpeechEyes;
    private bool activeTalkingParameter;

    public void CancelPendingSpeech()
    {
        speechRequestVersion++;
        transcriptionRequestVersion++;
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

    public IEnumerator PlayNPCAudioRoutine(string textToSpeak, NPCPersona currentNPC, AudioSource fallbackAudio,
        int turnNumber = 0)
    {
        int requestVersion = ++speechRequestVersion;
        StopActiveSpeech();
        bool downloaded = false;
        AudioClip clip = null;

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
            NPCEmotionSessionManager.Instance?.RecordRealismEvent(
                emotionSource, "voice", "generation_failed", text: textToSpeak);
        if (clip != null)
        {
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
                yield break;
            }
            if (currentNPC != null)
            {
                // A scene may override the prefab with its own active lip-sync component.
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
                    yield break;
                }
                activeSpeechSource = targetSource;
                activeSpeechAnimator = npcAnimator;
                activeSpeechBody = body;
                activeSpeechEyes = eyes;
                activeTalkingParameter = hasTalkingParameter;
                if (uiManager != null) uiManager.SetStatus("NPC Speaking...");
                NPCEmotionSessionManager.Instance?.SetRealismContext(emotionSource,
                    currentNPC != null ? currentNPC.name : null, "SPEAKING", turnNumber);
                NPCEmotionSessionManager.Instance?.RecordRealismEvent(
                    emotionSource, "voice", "playback_started", text: textToSpeak);
                if (hasTalkingParameter) npcAnimator.SetBool("IsTalking", true);
                if (body != null) body.SetActivity(NPCBodyActivity.SPEAKING, turnNumber);
                if (eyes != null) eyes.SetActivity(NPCBodyActivity.SPEAKING);
                yield return new WaitForSeconds(0.1f);
                while (requestVersion == speechRequestVersion && targetSource != null && targetSource.isPlaying)
                    yield return null;
            }
            finally
            {
                if (requestVersion == speechRequestVersion) StopActiveSpeech();
            }
        }
    }
}
