using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

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
        Action<string> onError)
    {
        string supabaseUrl = ConfigLoader.GetSupabaseUrl().Trim();
        string supabaseApiKey = ConfigLoader.GetSupabaseAnonKey().Trim();

        // -------------------------------------------------------------
        // STEP 1: Insert into 'session' table and return generated session_id
        // -------------------------------------------------------------
        string sessionEndpoint = $"{supabaseUrl}/rest/v1/session";

        var sessionPayload = new
        {
            user_id = userId,
            scenario_type = scenarioType,
            selected_role = selectedRole,
            scenario_topic = topic,
            difficulty_level = difficulty,
            input_mode_used = inputMode,
            start_time = startTime,
            end_time = endTime,
            duration_seconds = durationSeconds
        };

        string sessionJson = JsonConvert.SerializeObject(sessionPayload);
        string newSessionId = null;

        using (UnityWebRequest www = CreatePostRequest(sessionEndpoint, sessionJson, supabaseApiKey))
        {
            www.SetRequestHeader("Prefer", "return=representation");
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"[Session Insert Error] {www.error} | {www.downloadHandler.text}");
                yield break;
            }

            var createdSessions = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(www.downloadHandler.text);
            if (createdSessions != null && createdSessions.Count > 0)
            {
                newSessionId = createdSessions[0]["session_id"].ToString();
            }
        }

        if (string.IsNullOrEmpty(newSessionId))
        {
            onError?.Invoke("Failed to retrieve generated session_id from database.");
            yield break;
        }

        // -------------------------------------------------------------
        // STEP 2: Insert into 'performance_metric' table using foreign key
        // -------------------------------------------------------------
        string metricEndpoint = $"{supabaseUrl}/rest/v1/performance_metric";
        var metricData = new
        {
            session_id = newSessionId,
            overall_score = overallScore,
            speech_clarity_score = clarityScore,
            eye_contact_pct = eyeContactPct,
            hand_gesture_score = gestureScore,
            average_wpm = wpm,
            filler_word_count = totalFillers,
            gestures_per_minute = gpm,
            total_silence_seconds = totalSilenceSeconds,
            ai_feedback_summary = feedbackSummary
        };

        using (UnityWebRequest www = CreatePostRequest(metricEndpoint, JsonConvert.SerializeObject(metricData), supabaseApiKey))
        {
            yield return www.SendWebRequest();
            if (www.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"[Metric Insert Error] {www.error} | {www.downloadHandler.text}");
                yield break;
            }
        }

        // -------------------------------------------------------------
        // STEP 3: Insert dialogue turns into 'dialogue_turn' table
        // -------------------------------------------------------------
        if (dialogueTurns != null && dialogueTurns.Count > 0)
        {
            string turnEndpoint = $"{supabaseUrl}/rest/v1/dialogue_turn";
            List<object> turnPayloads = new List<object>();

            for (int i = 0; i < dialogueTurns.Count; i++)
            {
                turnPayloads.Add(new
                {
                    session_id = newSessionId,
                    turn_sequence = i + 1,
                    user_transcript = dialogueTurns[i].user_transcript,
                    ai_response_text = dialogueTurns[i].ai_response_text,
                    filler_word_count = dialogueTurns[i].filler_word_count,
                    speaking_rate_wpm = dialogueTurns[i].speaking_rate_wpm,
                    silence_duration_seconds = dialogueTurns[i].silence_duration_seconds
                });
            }

            using (UnityWebRequest www = CreatePostRequest(turnEndpoint, JsonConvert.SerializeObject(turnPayloads), supabaseApiKey))
            {
                yield return www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"[Dialogue Turn Insert Error] {www.error} | {www.downloadHandler.text}");
                    yield break;
                }
            }
        }

        onSuccess?.Invoke();
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
        string rawSelect = "*,performance_metric(*),dialogue_turn(*)";
        string encodedSelect = UnityWebRequest.EscapeURL(rawSelect);

        string endpoint = $"{supabaseUrl}/rest/v1/session?user_id=eq.{validUserId}&select={encodedSelect}&order=start_time.desc&limit=1";

        using (UnityWebRequest www = UnityWebRequest.Get(endpoint))
        {
            www.SetRequestHeader("apikey", supabaseApiKey);
            www.SetRequestHeader("Authorization", "Bearer " + supabaseApiKey);
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
        string rawSelect = "*,performance_metric(*),dialogue_turn(*)";
        string encodedSelect = UnityWebRequest.EscapeURL(rawSelect);

        string endpoint = $"{supabaseUrl}/rest/v1/session?user_id=eq.{validUserId}&select={encodedSelect}&order=start_time.desc";

        using (UnityWebRequest request = UnityWebRequest.Get(endpoint))
        {
            request.SetRequestHeader("apikey", supabaseApiKey);
            request.SetRequestHeader("Authorization", $"Bearer {supabaseApiKey}");
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
        string rawSelect = "*,performance_metric(*),dialogue_turn(*)";
        string encodedSelect = UnityWebRequest.EscapeURL(rawSelect);

        string endpoint = $"{supabaseUrl}/rest/v1/session?select={encodedSelect}&order=start_time.desc";

        using (UnityWebRequest request = UnityWebRequest.Get(endpoint))
        {
            request.SetRequestHeader("apikey", supabaseApiKey);
            request.SetRequestHeader("Authorization", $"Bearer {supabaseApiKey}");
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