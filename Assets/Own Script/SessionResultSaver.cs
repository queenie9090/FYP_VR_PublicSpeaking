using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SessionResultSaver : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private SupabaseManager supabaseManager;
    [SerializeField] private OpenAIManager openAIManager;

    public void ProcessAndSaveResults(
        ScenarioConfig activeConfig,
        List<DialogueTurn> conversationHistory,
        float sessionElapsedTimeSeconds,
        float totalSpeakingTimeSeconds,
        float totalSilenceTimeSeconds,
        int totalFillerWordsDetected,
        DateTime sessionStartTimestamp,
        EyeContactTracker eyeContactTracker,
        GestureTracker gestureTracker,
        HintUIController hintUIController,
        DialogueUIManager uiManager,
        Action onComplete)
    {
        StartCoroutine(ProcessResultsRoutine(
            activeConfig, conversationHistory, sessionElapsedTimeSeconds,
            totalSpeakingTimeSeconds, totalSilenceTimeSeconds, totalFillerWordsDetected,
            sessionStartTimestamp, eyeContactTracker, gestureTracker, hintUIController, uiManager, onComplete
        ));
    }

    private IEnumerator ProcessResultsRoutine(
        ScenarioConfig activeConfig,
        List<DialogueTurn> conversationHistory,
        float sessionElapsedTimeSeconds,
        float totalSpeakingTimeSeconds,
        float totalSilenceTimeSeconds,
        int totalFillerWordsDetected,
        DateTime sessionStartTimestamp,
        EyeContactTracker eyeContactTracker,
        GestureTracker gestureTracker,
        HintUIController hintUIController,
        DialogueUIManager uiManager,
        Action onComplete)
    {
        if (uiManager != null) uiManager.SetStatus("Saving Results...");

        DateTime sessionEndTimestamp = DateTime.UtcNow;
        string startTimeIso = sessionStartTimestamp.ToString("o");
        string endTimeIso = sessionEndTimestamp.ToString("o");

        // 1. Eye contact & Gesture calculations
        float eyeContactPct = 0.0f;
        if (eyeContactTracker != null)
        {
            eyeContactTracker.StopTracking();
            eyeContactPct = eyeContactTracker.CalculateEyeContactScore();
        }

        int totalExpressiveGestures = 0;
        float gpm = 0.0f;
        if (gestureTracker != null)
        {
            gestureTracker.StopTracking();
            var gestureMetrics = gestureTracker.CalculateGestureMetrics(sessionElapsedTimeSeconds);
            totalExpressiveGestures = gestureMetrics.expressiveCount;
            gpm = gestureMetrics.gpm;
        }

        // 2. Tutorial Bypass (Does not save to DB)
        string currentScene = SceneManager.GetActiveScene().name;
        bool isTutorial = UserSession.IsRunningTutorial || currentScene == "TutorialScene";
        if (isTutorial)
        {
            Debug.Log("[SessionResultSaver] Tutorial completed. Bypassing database save.");
            if (uiManager != null) uiManager.SetStatus("Calibration Complete!");
            onComplete?.Invoke();
            yield break;
        }

        // 3. Guest Mode Bypass (Prevents 23503 Foreign Key error)
        if (!UserSession.IsLoggedIn || string.IsNullOrEmpty(UserSession.CurrentUserId))
        {
            Debug.LogWarning("[SessionResultSaver] Guest user detected. Bypassing database save.");
            if (uiManager != null) uiManager.SetStatus("Session Complete (Guest Mode)");
            onComplete?.Invoke();
            yield break;
        }

        // 4. Speech metrics calculation
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
        var (clarityScore, _) = MetricsCalculator.CalculateSpeechClarity(totalFillerWordsDetected, totalSpeakingTimeSeconds, overallWpm);
        float gestureScore = Mathf.Clamp((gpm / 5.0f) * 100.0f, 0.0f, 100.0f);

        string rawScenario = (activeConfig != null && !string.IsNullOrEmpty(activeConfig.scenario)) ? activeConfig.scenario : "Normal Conversation";
        string currentScenario = rawScenario.Replace("Scene", "").Trim();

        float overallScore = MetricsCalculator.CalculateOverallScore(currentScenario, clarityScore, eyeContactPct, gestureScore);
        string activeUserId = UserSession.CurrentUserId;

        // 5. OpenAI Summary feedback generation
        if (uiManager != null) uiManager.SetStatus("Generating AI Feedback...");

        int unprofessionalCount = (gestureTracker != null) ? gestureTracker.GetUnprofessionalCount() : 0;
        int totalHintsUsed = (hintUIController != null) ? hintUIController.GetTotalHintCount() : 0;

        // Format UI Status Text with full breakdown
        string summaryText = $"Gestures: {totalExpressiveGestures} Hits ({gpm:F1} GPM) | Inappropriate: {unprofessionalCount}";
        Debug.Log($"[SessionResultSaver] Gesture Summary: {summaryText}");

        string summaryPrompt = $"You are an expert VR speech coach. Provide a concise 2-3 sentence constructive feedback summary with actionable suggestions for improvement based on this participant's performance in a '{currentScenario}' simulation: " +
                               $"Overall Score: {overallScore:F0}%, Speech Clarity: {clarityScore:F0}%, Speaking Rate: {overallWpm:F0} WPM, " +
                               $"Filler Words Used: {totalFillerWordsDetected}, Eye Contact: {eyeContactPct:F0}%, Expressive Gestures: {totalExpressiveGestures} ({gpm:F1} GPM), " +
                               $"Hints Requested: {totalHintsUsed}, Unprofessional Gestures Flagged: {unprofessionalCount}.";

        string aiSummary = null;
        bool aiSummaryDone = false;

        StartCoroutine(openAIManager.SendChatRequest(
            summaryPrompt, conversationHistory, "Generate Summary",
            res => {
                aiSummary = (res != null && !string.IsNullOrEmpty(res.npc_response)) ? res.npc_response : $"Your overall performance scored {overallScore:F0}%.";
                aiSummaryDone = true;
            },
            err => {
                aiSummary = $"Your overall performance scored {overallScore:F0}%.";
                aiSummaryDone = true;
            }
        ));

        yield return new WaitUntil(() => aiSummaryDone);

        // 6. Supabase database save
        if (supabaseManager != null)
        {
            // Change 'turnRecords: turnRecords' to 'dialogueTurns: turnRecords'
            yield return supabaseManager.SaveSessionNormalized(
                userId: activeUserId,
                scenarioType: currentScenario,
                selectedRole: activeConfig != null ? activeConfig.participantRole : "User",
                topic: activeConfig != null ? activeConfig.topic : "General",
                difficulty: activeConfig != null ? activeConfig.difficulty : "Medium",
                inputMode: UserSession.InputMode,
                sessionStatus: "Completed",
                startTime: startTimeIso,
                endTime: endTimeIso,
                durationSeconds: sessionElapsedTimeSeconds,
                overallScore: overallScore,
                clarityScore: clarityScore,
                eyeContactPct: eyeContactPct,
                gestureScore: gestureScore,
                wpm: overallWpm,
                totalFillers: totalFillerWordsDetected,
                gpm: gpm,
                totalSilenceSeconds: totalSilenceTimeSeconds,
                feedbackSummary: aiSummary,
                dialogueTurns: turnRecords, // <-- FIX: Renamed from turnRecords to dialogueTurns
                onSuccess: () => {
                    Debug.Log("[SessionResultSaver] Session successfully saved!");
                    if (uiManager != null) uiManager.SetStatus("Session Saved!");
                    onComplete?.Invoke();
                },
                onError: (err) => {
                    Debug.LogError($"[SessionResultSaver] Save Failed: {err}");
                    if (uiManager != null) uiManager.SetStatus("Save Failed!");
                    onComplete?.Invoke();
                }
            );
        }
        else
        {
            onComplete?.Invoke();
        }
    }
}