using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpeechInteractionController : MonoBehaviour
{
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
        Action onTranscriptionComplete)
    {
        if (uiManager != null) uiManager.SetStatus("Transcribing...");

        bool sttDone = false;
        string userTranscript = "";

        StartCoroutine(openAIManager.TranscribeAudioWithPrompt(wavBytes, "Um, uh, er.", transcript => {
            userTranscript = transcript;
            sttDone = true;
        }, err => {
            Debug.LogError($"[STT Error] {err}");
            if (uiManager != null) uiManager.SetStatus("Speech Error.");
            sttDone = true;
        }));

        yield return new WaitUntil(() => sttDone);

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
      
        onTranscriptionComplete?.Invoke();
    }

    public IEnumerator PlayNPCAudioRoutine(string textToSpeak, NPCPersona currentNPC, AudioSource fallbackAudio)
    {
        bool downloaded = false;
        AudioClip clip = null;

        string voice = (currentNPC != null && !string.IsNullOrEmpty(currentNPC.voiceId))
            ? currentNPC.voiceId
            : "nova";

        StartCoroutine(openAIManager.TextToSpeech(textToSpeak, voice, c => { clip = c; downloaded = true; }, err => downloaded = true));
        yield return new WaitUntil(() => downloaded);

        if (clip != null)
        {
            AudioSource targetSource = (currentNPC != null && currentNPC.npcAudioSource != null)
                ? currentNPC.npcAudioSource
                : fallbackAudio;

            Animator npcAnimator = currentNPC != null ? currentNPC.GetComponent<Animator>() : null;
            if (npcAnimator != null) npcAnimator.SetBool("IsTalking", true);

            targetSource.clip = clip;
            targetSource.Play();
            yield return new WaitForSeconds(0.1f);
            while (targetSource != null && targetSource.isPlaying) yield return null;

            if (npcAnimator != null) npcAnimator.SetBool("IsTalking", false);
        }
    }
}