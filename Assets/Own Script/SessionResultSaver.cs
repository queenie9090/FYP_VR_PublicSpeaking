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
        Action onComplete,
        string sessionStatus = "Completed")
    {
        if (sessionStartTimestamp == default) return;
        if (supabaseManager == null) supabaseManager = FindFirstObjectByType<SupabaseManager>();
        if (openAIManager == null) openAIManager = FindFirstObjectByType<OpenAIManager>();
        var runner = NPCEmotionSessionManager.EnsureInstance();
        if (supabaseManager == null)
            supabaseManager = runner.GetComponent<SupabaseManager>() ??
                runner.gameObject.AddComponent<SupabaseManager>();
        var emotions = new List<NPCEmotionRecord>(runner.EmotionHistory);
        runner.FinishBodyEventCollection();
        var bodyEvents = new List<BodyAnimationEventRecord>(runner.BodyAnimationEvents);
        var realismEvents = new List<NPCRealismEvent>(runner.RealismEvents);
        runner.StartCoroutine(ProcessResultsRoutine(
            activeConfig, new List<DialogueTurn>(conversationHistory), sessionElapsedTimeSeconds,
            totalSpeakingTimeSeconds, totalSilenceTimeSeconds, totalFillerWordsDetected,
            sessionStartTimestamp, eyeContactTracker, gestureTracker, hintUIController, uiManager, onComplete,
            sessionStatus, emotions, bodyEvents, realismEvents
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
        Action onComplete,
        string sessionStatus,
        List<NPCEmotionRecord> emotions,
        List<BodyAnimationEventRecord> bodyEvents,
        List<NPCRealismEvent> realismEvents)
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

        var pairs = new List<(DialogueTurn user, DialogueTurn npc)>();
        DialogueTurn pendingUser = null;
        foreach (var entry in conversationHistory)
        {
            if (entry.speaker == "User") pendingUser = entry;
            else if (entry.speaker == "NPC")
            {
                pairs.Add((pendingUser, entry));
                pendingUser = null;
            }
        }
        if (pendingUser != null) pairs.Add((pendingUser, null));
        for (int i = 0; i < pairs.Count; i++)
        {
            string userText = pairs[i].user?.dialogue ?? "";
            string aiText = pairs[i].npc?.dialogue ?? "";
            int turnFillers = pairs[i].user?.fillerWordCount ?? 0;
            float userDurationSec = pairs[i].user?.speechDuration ?? 0f;
            float turnSilenceSec = pairs[i].user?.silenceDuration ?? 0f;

            var (wordCount, turnWpm) = MetricsCalculator.CalculateSpeakingRate(userText, userDurationSec);
            totalWordsSpoken += wordCount;

            turnRecords.Add(new DialogueTurnRecord
            {
                turn_sequence = i + 1,
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

        if (openAIManager == null)
            openAIManager = GetComponent<OpenAIManager>() ?? FindObjectOfType<OpenAIManager>();

        int unprofessionalCount = (gestureTracker != null) ? gestureTracker.GetUnprofessionalCount() : 0;
        int totalHintsUsed = (hintUIController != null) ? hintUIController.GetTotalHintCount() : 0;

        string summaryText = $"Gestures: {totalExpressiveGestures} Hits ({gpm:F1} GPM) | Inappropriate: {unprofessionalCount}";
        Debug.Log($"[SessionResultSaver] Gesture Summary: {summaryText}");

        string summaryPrompt = $"You are an expert VR speech coach. Provide a concise 2-3 sentence constructive feedback summary with actionable suggestions for improvement based on this participant's performance in a '{currentScenario}' simulation: " +
                               $"Overall Score: {overallScore:F0}%, Speech Clarity: {clarityScore:F0}%, Speaking Rate: {overallWpm:F0} WPM, " +
                               $"Filler Words Used: {totalFillerWordsDetected}, Eye Contact: {eyeContactPct:F0}%, Expressive Gestures: {totalExpressiveGestures} ({gpm:F1} GPM), " +
                               $"Hints Requested: {totalHintsUsed}, Unprofessional Gestures Flagged: {unprofessionalCount}.";

        // Default summary is used if OpenAI is missing, fails, or times out
        string aiSummary = $"Your overall performance scored {overallScore:F0}%.";

        if (openAIManager != null)
        {
            bool aiSummaryDone = false;

            NPCEmotionSessionManager.EnsureInstance().StartCoroutine(openAIManager.SendChatRequest(
                summaryPrompt, conversationHistory, "Generate Summary",
                res =>
                {
                    if (res != null && !string.IsNullOrEmpty(res.npc_response))
                        aiSummary = res.npc_response;
                    aiSummaryDone = true;
                },
                err =>
                {
                    Debug.LogWarning($"[SessionResultSaver] OpenAI summary failed: {err}");
                    aiSummaryDone = true;
                }
            ));

            // Wait for the response, but give up after 30s so the save can't hang
            float waited = 0f;
            while (!aiSummaryDone && waited < 30f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!aiSummaryDone)
                Debug.LogWarning("[SessionResultSaver] OpenAI summary timed out. Using default summary.");
        }
        else
        {
            Debug.LogWarning("[SessionResultSaver] OpenAIManager not found. Using default summary.");
        }

        // 6. Supabase database save
        if (supabaseManager == null)
        {
            supabaseManager = GetComponent<SupabaseManager>() ?? FindObjectOfType<SupabaseManager>();
        }

        if (!ReferenceEquals(supabaseManager, null))
        {
            yield return supabaseManager.SaveSessionNormalized(
                userId: activeUserId,
                scenarioType: currentScenario,
                selectedRole: activeConfig != null ? activeConfig.participantRole : "User",
                topic: activeConfig != null ? activeConfig.topic : "General",
                difficulty: activeConfig != null ? activeConfig.difficulty : "Medium",
                inputMode: UserSession.InputMode,
                sessionStatus: sessionStatus,
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
                dialogueTurns: turnRecords,
                emotions: emotions,
                bodyEvents: bodyEvents,
                realismEvents: realismEvents,
                onSuccess: () => {
                    Debug.Log("[SessionResultSaver] Session successfully saved!");
                    if (uiManager != null) uiManager.SetStatus("Session Saved!");
                    onComplete?.Invoke();
                },
                onError: (err) =>
                {
                    Debug.LogError($"[SessionResultSaver] Save Failed: {err}");
                    if (uiManager != null) uiManager.SetStatus("Save Failed!");
                    onComplete?.Invoke();
                }
            );
        }
        else
        {
            Debug.LogWarning("[SessionResultSaver] SupabaseManager component not found in scene. Skipping database save.");
            Debug.LogError("[SessionResultSaver] SupabaseManager missing. Session was not saved.");
            onComplete?.Invoke();
        }
    }
}
