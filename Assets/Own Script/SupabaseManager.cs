using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class SupabaseManager : MonoBehaviour
{
    /// <summary>
    /// Saves completed session data across normalized tables: session, performance_metric, and dialogue_turn.
    /// </summary>
    public IEnumerator SaveSessionNormalized(
        string userId,
        string scenarioType,
        string selectedRole,
        string topic,
        string difficulty,
        string inputMode,
        string sessionStatus,
        string startTime,        // ISO 8601 string e.g. "2026-10-22T14:30:00.000Z"
        string endTime,          // ISO 8601 string
        float durationSeconds,
        float overallScore,
        float clarityScore,
        float eyeContactPct,
        float gestureScore,
        float wpm,
        int totalFillers,
        float gpm,
        float totalSilenceSeconds,
        string feedbackSummary,
        List<DialogueTurnRecord> dialogueTurns,
        Action onSuccess,
        Action<string> onError,
        List<NPCEmotionRecord> emotions = null,
        List<BodyAnimationEventRecord> bodyEvents = null,
        List<NPCRealismEvent> realismEvents = null)
    {
        string key = userId + "|" + startTime;
        if (!pendingSaves.TryGetValue(key, out var save))
        {
            string sessionId = Guid.NewGuid().ToString();
            var turns = new List<object>();
            var logs = new List<object>();
            var turnIds = new Dictionary<int, string>();
            foreach (var turn in dialogueTurns ?? new List<DialogueTurnRecord>())
            {
                if (turn == null || turn.turn_sequence < 1 || turnIds.ContainsKey(turn.turn_sequence))
                {
                    onError?.Invoke("[Dialogue mapping] A dialogue turn is missing or has an invalid or duplicate sequence.");
                    yield break;
                }
                string turnId = Guid.NewGuid().ToString();
                turnIds.Add(turn.turn_sequence, turnId);
                turns.Add(new { turn_id = turnId, session_id = sessionId,
                    turn.turn_sequence, turn.user_transcript, turn.ai_response_text,
                    turn.filler_word_count, turn.speaking_rate_wpm, turn.silence_duration_seconds });
            }
            var previousByNpc = new Dictionary<string, string>();
            foreach (var emotion in emotions ?? new List<NPCEmotionRecord>())
            {
                if (!turnIds.TryGetValue(emotion.turnNumber, out string turnId))
                {
                    onError?.Invoke("[Emotion mapping] No dialogue turn for emotion turn " + emotion.turnNumber);
                    yield break;
                }
                string npcName = string.IsNullOrWhiteSpace(emotion.npcName) ? "NPC" : emotion.npcName;
                string previous = emotion.previousEmotion;
                if (string.IsNullOrEmpty(previous))
                    previous = previousByNpc.TryGetValue(npcName, out var last) ? last : "NEUTRAL";
                previousByNpc[npcName] = emotion.emotion;
                if (string.IsNullOrEmpty(emotion.emotionLogId)) emotion.emotionLogId = Guid.NewGuid().ToString();
                logs.Add(new { emotion_log_id = emotion.emotionLogId, turn_id = turnId,
                    npc_name = npcName, previous_emotion = previous,
                    current_emotion = emotion.emotion, emotion_intensity = emotion.intensity,
                    emotion_trigger = emotion.emotionTrigger, recorded_at = emotion.recordedAt ?? endTime });
            }
            var animationLogs = new List<object>();
            foreach (var record in bodyEvents ?? new List<BodyAnimationEventRecord>())
            {
                // Listening can begin before any transcript exists. Such events are session-only.
                turnIds.TryGetValue(record.turnNumber, out string turnId);
                animationLogs.Add(new {
                    record.animation_event_id, record.npc_id, record.npc_type, record.scene_type,
                    record.npc_activity, record.emotion, record.animation_name, record.timestamp,
                    session_id = sessionId, turn_id = turnId
                });
            }
            var eyeEvents = new List<object>();
            var facialEvents = new List<object>();
            var voiceEvents = new List<object>();
            foreach (var record in realismEvents ?? new List<NPCRealismEvent>())
            {
                turnIds.TryGetValue(record.turnNumber, out string turnId);
                if (record.kind == "eye")
                    eyeEvents.Add(new { eye_event_id = record.id, session_id = sessionId, turn_id = turnId,
                        npc_id = record.npcId, npc_state = record.state, emotion = record.emotion,
                        gaze_target = record.target, eye_behaviour = record.detail,
                        blink_event = record.blink, timestamp = record.timestamp });
                else if (record.kind == "face")
                    facialEvents.Add(new { expression_event_id = record.id, session_id = sessionId, turn_id = turnId,
                        npc_id = record.npcId, npc_state = record.state, emotion = record.emotion,
                        emotion_intensity = record.intensity, expression_type = record.detail,
                        micro_expression = (string)null, timestamp = record.timestamp });
                else if (record.kind == "voice")
                {
                    if (turnId == null)
                    {
                        onError?.Invoke("[Voice mapping] No dialogue turn for voice event " + record.turnNumber);
                        yield break;
                    }
                    voiceEvents.Add(new { voice_event_id = record.id, session_id = sessionId, turn_id = turnId,
                        npc_id = record.npcId, emotion = record.emotion, emotion_intensity = record.intensity,
                        voice_style = record.voiceStyle, dialogue_text = record.text, tts_status = record.detail,
                        timestamp = record.timestamp });
                }
            }
            save = new PendingSave
            {
                UserId = userId,
                SessionId = sessionId,
                Operation = realismEvents != null && realismEvents.Count > 0 ? "save_training_session_v3" :
                    animationLogs.Count > 0 ? "save_training_session_v2" : "save_training_session",
                Json = JsonConvert.SerializeObject(new { payload = new {
                    session = new { session_id = sessionId, user_id = userId,
                        scenario_type = scenarioType, selected_role = selectedRole,
                        scenario_topic = topic, difficulty_level = difficulty, input_mode_used = inputMode,
                        status = sessionStatus, start_time = startTime, end_time = endTime,
                        duration_seconds = durationSeconds },
                    metric = new { metric_id = Guid.NewGuid().ToString(), session_id = sessionId,
                        overall_score = overallScore, speech_clarity_score = clarityScore,
                        eye_contact_pct = eyeContactPct, hand_gesture_score = gestureScore,
                        average_wpm = wpm, filler_word_count = totalFillers, gestures_per_minute = gpm,
                        total_silence_seconds = totalSilenceSeconds, ai_feedback_summary = feedbackSummary },
                    turns, emotions = logs, body_events = animationLogs, eye_events = eyeEvents,
                    facial_events = facialEvents, voice_events = voiceEvents } })
            };
            pendingSaves.Add(key, save);
        }
        yield return SubmitPending(save, () =>
        {
            // The performance dashboard already consumes CurrentSessionData.
            // Hand it the exact confirmed payload instead of querying the latest
            // session, which may belong to a different concurrent session.
            try
            {
                if (UserSession.CurrentUserId != save.UserId ||
                    UserSession.AuthClient?.Auth.CurrentUser?.Id != save.UserId)
                    throw new InvalidOperationException("Signed-in account changed during the save.");
                var confirmed = BuildConfirmedSessionSnapshot(save.Json);
                UserSession.CurrentSessionData = confirmed;
                UserSession.EmotionDashboardSession = confirmed;
            }
            catch (Exception ex)
            {
                Debug.LogError("[Session save] Saved, but dashboard handoff failed: " + ex.Message);
                onError?.Invoke("Session was saved, but dashboard handoff failed. Retry to open the confirmed session.");
                return;
            }
            onSuccess?.Invoke();
        }, onError);
    }

    private static NormalizedSessionData BuildConfirmedSessionSnapshot(string json)
    {
        var payload = JObject.Parse(json)["payload"];
        if (payload == null) throw new InvalidOperationException("Missing confirmed session payload.");
        var session = payload["session"]?.ToObject<NormalizedSessionData>();
        if (session == null || !Guid.TryParse(session.session_id, out _))
            throw new InvalidOperationException("Missing confirmed session ID.");
        var metric = payload["metric"]?.ToObject<PerformanceMetricRecord>();
        session.performance_metric = metric != null ? new List<PerformanceMetricRecord> { metric } : new List<PerformanceMetricRecord>();
        session.dialogue_turn = payload["turns"]?.ToObject<List<DialogueTurnRecord>>() ?? new List<DialogueTurnRecord>();
        var emotions = payload["emotions"]?.ToObject<List<NPCEmotionLogData>>() ?? new List<NPCEmotionLogData>();
        var turns = new Dictionary<string, DialogueTurnRecord>();
        foreach (var turn in session.dialogue_turn)
        {
            if (turn == null || string.IsNullOrEmpty(turn.turn_id)) continue;
            turn.npc_emotion_log = new List<NPCEmotionLogData>();
            turns.Add(turn.turn_id, turn);
        }
        foreach (var emotion in emotions)
        {
            if (emotion == null || !turns.TryGetValue(emotion.turn_id ?? "", out var turn))
                throw new InvalidOperationException("Emotion does not match a confirmed dialogue turn.");
            turn.npc_emotion_log.Add(emotion);
        }
        return session;
    }

    private class PendingSave
    {
        public string UserId;
        public string SessionId;
        public string Json;
        public string Operation;
        public bool Saving;
        public bool Saved;
    }

    private static readonly Dictionary<string, PendingSave> pendingSaves = new();

    public IEnumerator GetSessionBehaviourSummary(string sessionId, Action<Dictionary<string, string>> onSuccess,
        Action<string> onError)
    {
        var summary = new Dictionary<string, string>();
        string token = UserSession.AuthClient?.Auth.CurrentSession?.AccessToken;
        if (!Guid.TryParse(sessionId, out _) || string.IsNullOrEmpty(token))
        {
            onError?.Invoke("A valid session and signed-in account are required.");
            yield break;
        }
        var tables = new[] { "eye_behaviour_event", "facial_expression_event", "body_animation_event", "voice_event" };
        var fields = new[] { "eye_behaviour", "expression_type", "animation_name", "tts_status,voice_style" };
        for (int i = 0; i < tables.Length; i++)
        {
            string endpoint = ConfigLoader.GetSupabaseUrl().TrimEnd('/') + "/rest/v1/" + tables[i] +
                "?session_id=eq." + UnityWebRequest.EscapeURL(sessionId) + "&select=" + fields[i] +
                "&order=timestamp.desc&limit=1";
            using (var request = UnityWebRequest.Get(endpoint))
            {
                request.timeout = 20;
                request.SetRequestHeader("apikey", ConfigLoader.GetSupabaseAnonKey().Trim());
                request.SetRequestHeader("Authorization", "Bearer " + token);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"[{tables[i]} HTTP {request.responseCode}] {request.error} | {request.downloadHandler.text}");
                    yield break;
                }
                List<Dictionary<string, object>> rows;
                try { rows = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(request.downloadHandler.text); }
                catch (Exception ex) { onError?.Invoke("Behaviour response parsing failed: " + ex.Message); yield break; }
                if (rows != null && rows.Count > 0)
                {
                    string primaryField = i == 3 ? "tts_status" : fields[i];
                    if (rows[0].TryGetValue(primaryField, out var value))
                        summary[tables[i]] = value?.ToString();
                    if (i == 3 && rows[0].TryGetValue("voice_style", out var style))
                        summary["voice_style"] = style?.ToString();
                }
            }
        }
        onSuccess?.Invoke(summary);
    }

    [ContextMenu("Retry Pending Session Saves")]
    public void RetryPendingSaves()
    {
        foreach (var save in new List<PendingSave>(pendingSaves.Values))
            if (!save.Saved && !save.Saving && save.UserId == UserSession.CurrentUserId)
                NPCEmotionSessionManager.EnsureInstance().StartCoroutine(SubmitPending(save, null, null));
    }

    private static IEnumerator SubmitPending(PendingSave save, Action onSuccess, Action<string> onError)
    {
        while (save.Saving) yield return null;
        if (save.Saved) { onSuccess?.Invoke(); yield break; }
        var auth = UserSession.AuthClient?.Auth;
        string token = auth?.CurrentSession?.AccessToken;
        if (string.IsNullOrEmpty(token) || auth.CurrentUser?.Id != save.UserId)
        {
            string error = "[Session save] Sign in as the session owner before retrying. Data retained in memory.";
            Debug.LogError(error);
            onError?.Invoke(error);
            yield break;
        }
        save.Saving = true;
        try
        {
            using (var request = new UnityWebRequest(
                ConfigLoader.GetSupabaseUrl().TrimEnd('/') + "/rest/v1/rpc/" + save.Operation, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(save.Json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 45;
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("apikey", ConfigLoader.GetSupabaseAnonKey().Trim());
                request.SetRequestHeader("Authorization", "Bearer " + token);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = $"[{save.Operation} HTTP {request.responseCode}] {request.error} | {request.downloadHandler.text}";
                    Debug.LogError(error);
                    onError?.Invoke(error);
                    yield break;
                }
                if (request.downloadHandler.text.Trim().Trim('"') != save.SessionId)
                {
                    const string error = "[Session save] Unexpected confirmation. Data retained for retry.";
                    Debug.LogError(error);
                    onError?.Invoke(error);
                    yield break;
                }
                save.Saved = true;
                Debug.Log("[Session save] Confirmed all submitted session records: " + save.SessionId);
                onSuccess?.Invoke();
            }
        }
        finally { save.Saving = false; }
    }

    /// <summary>
    /// Fetches the latest normalized session along with performance metrics and dialogue turns.
    /// </summary>
    public IEnumerator GetLatestNormalizedSession(string userId, Action<NormalizedSessionData> onSuccess, Action<string> onError)
    {
        string supabaseUrl = ConfigLoader.GetSupabaseUrl().Trim();
        string supabaseApiKey = ConfigLoader.GetSupabaseAnonKey().Trim();

        // Fallback to default UUID if unauthenticated to avoid 400 bad request (invalid UUID syntax)
        string validUserId = (string.IsNullOrEmpty(userId) || userId == "UNAUTHENTICATED_USER")
            ? "00000000-0000-0000-0000-000000000000"
            : userId;

        // Escape query params so PostgREST parses joined tables correctly
        string rawSelect = "*,performance_metric(*),dialogue_turn(*,npc_emotion_log(*))";
        string encodedSelect = UnityWebRequest.EscapeURL(rawSelect);

        string endpoint = $"{supabaseUrl}/rest/v1/session?user_id=eq.{validUserId}&select={encodedSelect}&order=start_time.desc&limit=1";

        using (UnityWebRequest www = UnityWebRequest.Get(endpoint))
        {
            www.SetRequestHeader("apikey", supabaseApiKey);
            www.SetRequestHeader("Authorization", "Bearer " + (UserSession.AuthClient?.Auth.CurrentSession?.AccessToken ?? supabaseApiKey));
            www.SetRequestHeader("Accept", "application/json");

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"[HTTP {www.responseCode}] {www.error} | {www.downloadHandler?.text}");
            }
            else
            {
                string jsonText = www.downloadHandler.text != null ? www.downloadHandler.text.Trim() : "";

                if (string.IsNullOrEmpty(jsonText) || jsonText == "[]")
                {
                    onSuccess?.Invoke(null);
                    yield break;
                }

                try
                {
                    List<NormalizedSessionData> results = JsonConvert.DeserializeObject<List<NormalizedSessionData>>(jsonText);
                    if (results != null && results.Count > 0)
                    {
                        onSuccess?.Invoke(results[0]);
                    }
                    else
                    {
                        onSuccess?.Invoke(null);
                    }
                }
                catch (Exception ex)
                {
                    onError?.Invoke("JSON Deserialization Error: " + ex.Message);
                }
            }
        }
    }

    /// <summary>
    /// Fetches all historical normalized sessions for a specific user from Supabase.
    /// Returns a list of sessions ordered by start_time descending (newest first).
    /// </summary>
    public IEnumerator GetAllUserNormalizedSessions(string userId, Action<List<NormalizedSessionData>> onSuccess, Action<string> onError)
    {
        string supabaseUrl = ConfigLoader.GetSupabaseUrl().Trim();
        string supabaseApiKey = ConfigLoader.GetSupabaseAnonKey().Trim();

        // Fallback to default UUID if unauthenticated to avoid 400 bad request (invalid UUID syntax)
        string validUserId = (string.IsNullOrEmpty(userId) || userId == "UNAUTHENTICATED_USER")
            ? "00000000-0000-0000-0000-000000000000"
            : userId;

        // Escape query params so PostgREST parses joined tables correctly
        string rawSelect = "*,performance_metric(*),dialogue_turn(*,npc_emotion_log(*))";
        string encodedSelect = UnityWebRequest.EscapeURL(rawSelect);

        string endpoint = $"{supabaseUrl}/rest/v1/session?user_id=eq.{validUserId}&select={encodedSelect}&order=start_time.desc";

        using (UnityWebRequest request = UnityWebRequest.Get(endpoint))
        {
            request.SetRequestHeader("apikey", supabaseApiKey);
            request.SetRequestHeader("Authorization", "Bearer " + (UserSession.AuthClient?.Auth.CurrentSession?.AccessToken ?? supabaseApiKey));
            request.SetRequestHeader("Accept", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string jsonResponse = request.downloadHandler.text != null ? request.downloadHandler.text.Trim() : "";

                if (string.IsNullOrEmpty(jsonResponse) || jsonResponse == "[]")
                {
                    onSuccess?.Invoke(new List<NormalizedSessionData>());
                    yield break;
                }

                try
                {
                    List<NormalizedSessionData> sessions = JsonConvert.DeserializeObject<List<NormalizedSessionData>>(jsonResponse);
                    onSuccess?.Invoke(sessions ?? new List<NormalizedSessionData>());
                }
                catch (Exception ex)
                {
                    onError?.Invoke("JSON Deserialization Error: " + ex.Message);
                }
            }
            else
            {
                onError?.Invoke($"[HTTP {request.responseCode}] {request.error} | {request.downloadHandler?.text}");
            }
        }
    }

    /// <summary>
    /// Fetches all participant session records across all users for educator cohort analysis.
    /// </summary>
    public IEnumerator GetAllCohortNormalizedSessions(Action<List<NormalizedSessionData>> onSuccess, Action<string> onError)
    {
        string supabaseUrl = ConfigLoader.GetSupabaseUrl().Trim();
        string supabaseApiKey = ConfigLoader.GetSupabaseAnonKey().Trim();

        // Select all sessions and join performance_metric + dialogue_turn tables
        string rawSelect = "*,performance_metric(*),dialogue_turn(*,npc_emotion_log(*))";
        string encodedSelect = UnityWebRequest.EscapeURL(rawSelect);

        string endpoint = $"{supabaseUrl}/rest/v1/session?select={encodedSelect}&order=start_time.desc";

        using (UnityWebRequest request = UnityWebRequest.Get(endpoint))
        {
            request.SetRequestHeader("apikey", supabaseApiKey);
            request.SetRequestHeader("Authorization", "Bearer " + (UserSession.AuthClient?.Auth.CurrentSession?.AccessToken ?? supabaseApiKey));
            request.SetRequestHeader("Accept", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string jsonResponse = request.downloadHandler.text != null ? request.downloadHandler.text.Trim() : "";

                if (string.IsNullOrEmpty(jsonResponse) || jsonResponse == "[]")
                {
                    onSuccess?.Invoke(new List<NormalizedSessionData>());
                    yield break;
                }

                try
                {
                    List<NormalizedSessionData> sessions = JsonConvert.DeserializeObject<List<NormalizedSessionData>>(jsonResponse);
                    onSuccess?.Invoke(sessions ?? new List<NormalizedSessionData>());
                }
                catch (Exception ex)
                {
                    onError?.Invoke("JSON Deserialization Error: " + ex.Message);
                }
            }
            else
            {
                onError?.Invoke($"[HTTP {request.responseCode}] {request.error} | {request.downloadHandler?.text}");
            }
        }
    }

    private UnityWebRequest CreatePostRequest(string url, string jsonBody, string apiKey)
    {
        UnityWebRequest www = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("apikey", apiKey);
        www.SetRequestHeader("Authorization", "Bearer " + apiKey);
        return www;
    }
}
