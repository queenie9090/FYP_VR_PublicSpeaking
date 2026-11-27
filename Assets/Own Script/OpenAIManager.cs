using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class OpenAIManager : MonoBehaviour
{
    private const string DeepgramUrl = "https://api.deepgram.com/v1/listen?model=nova-3&language=en&filler_words=true&smart_format=true";
    private const string ChatUrl = "https://api.openai.com/v1/chat/completions";
    private const string TtsUrl = "https://api.openai.com/v1/audio/speech";

    public string ApiKey => ConfigLoader.GetApiKey();
    public string DeepgramApiKey => ConfigLoader.GetDeepgramApiKey();

    #region Speech-to-Text (Deepgram Nova-3 with Verbatim Fillers)
    public IEnumerator TranscribeAudioWithPrompt(byte[] wavData, string initialPrompt, Action<string> onSuccess, Action<string> onError, Func<bool> shouldCancel = null)
    {
        using (UnityWebRequest www = new UnityWebRequest(DeepgramUrl, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(wavData);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "audio/wav");
            www.SetRequestHeader("Authorization", "Token " + DeepgramApiKey);

            www.timeout = 45;
            yield return WaitForRequest(www, shouldCancel);
            if (shouldCancel != null && shouldCancel()) yield break;

            if (www.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(www.error + " | " + www.downloadHandler.text);
            }
            else
            {
                if (TryParseTranscription(www.downloadHandler.text, out string transcript, out string parseError))
                    onSuccess?.Invoke(transcript);
                else
                    onError?.Invoke(parseError);
            }
        }
    }
    internal static bool TryParseTranscription(string rawJson, out string transcript, out string error)
    {
        transcript = null;
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                error = "Speech recognition returned an empty response.";
            else
            {
                DeepgramResponse res = Newtonsoft.Json.JsonConvert.DeserializeObject<DeepgramResponse>(rawJson);
                var channels = res?.results?.channels;
                var alternatives = channels != null && channels.Length > 0 ? channels[0]?.alternatives : null;
                if (alternatives == null || alternatives.Length == 0 || alternatives[0] == null || alternatives[0].transcript == null)
                    error = "Speech recognition returned an invalid response.";
                else
                    transcript = alternatives[0].transcript;
            }
        }
        catch (Exception ex)
        {
            error = "Speech recognition response could not be parsed: " + ex.GetType().Name;
        }
        return error == null;
    }
    #endregion

    #region LLM Structured Completion
    public IEnumerator SendChatRequest(string systemPrompt, List<DialogueTurn> history, string currentTranscript, Action<LLMResponse> onSuccess, Action<string> onError, bool preserveInvalidEmotion = false, Func<bool> shouldCancel = null)
    {
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        // The latest user turn is already inside history, so it is added once below as currentTranscript.
        int count = history != null ? history.Count : 0;
        if (count > 0 && history[count - 1].speaker == "User") count--;

        for (int i = 0; i < count; i++)
        {
            var turn = history[i];
            string role = turn.speaker == "User" ? "user" : "assistant";
            string content = turn.dialogue;
            if (role == "assistant" && !string.IsNullOrWhiteSpace(turn.speaker) && turn.speaker != "NPC")
                content = $"[{turn.speaker}]: {content}";
            messages.Add(new { role = role, content = content });
        }

        messages.Add(new { role = "user", content = currentTranscript });

        var requestBody = new
        {
            model = "gpt-6-luna",
            response_format = new { type = "json_object" },
            messages = messages
        };

        string jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);

        using (UnityWebRequest www = new UnityWebRequest(ChatUrl, "POST"))
        {
            www.timeout = 45;
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + ApiKey);

            yield return WaitForRequest(www, shouldCancel);
            if (shouldCancel != null && shouldCancel()) yield break;

            if (www.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(www.error + " | " + www.downloadHandler.text);
            }
            else
            {
                if (!TryParseNPCResponse(www.downloadHandler.text, out LLMResponse response, out string parseError))
                {
                    onError?.Invoke(parseError);
                    yield break;
                }

                if (response == null)
                {
                    onError?.Invoke("NPC response is missing.");
                    yield break;
                }
                if (response.npc_responses == null || response.npc_responses.Count == 0)
                {
                    response.npc_responses = new List<NPCTurnLine>();
                    if (!string.IsNullOrWhiteSpace(response.npc_response))
                        response.npc_responses.Add(new NPCTurnLine { dialogue = response.npc_response, emotion = response.emotion });
                }
                response.npc_responses.RemoveAll(line => line == null || string.IsNullOrWhiteSpace(line.dialogue));
                if (response.npc_responses.Count == 0)
                {
                    onError?.Invoke("NPC response is missing or empty.");
                    yield break;
                }
                response.rawPrimaryEmotion = response.npc_responses[0].emotion;
                foreach (var line in response.npc_responses)
                {
                    line.emotion = string.IsNullOrWhiteSpace(line.emotion) ? "NEUTRAL" : line.emotion.Trim().ToUpperInvariant();
                    if (!IsValidEmotion(line.emotion)) line.emotion = "NEUTRAL";
                }
                response.emotion = response.npc_responses[0].emotion;
                if (string.IsNullOrWhiteSpace(response.npc_response)) response.npc_response = response.npc_responses[0].dialogue;

                // Return the validated response.
                onSuccess?.Invoke(response);

            }
        }
    }


    public IEnumerator SendHintRequest(string hintPrompt, Action<string> onSuccess, Action<string> onError = null)
    {
        var requestBody = new
        {
            model = "gpt-6-luna",
            response_format = new { type = "json_object" },
            messages = new List<object> { new { role = "system", content = hintPrompt } }
        };

        string jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
        Debug.Log($"[Hint Request] payload length: {jsonPayload.Length}\n{jsonPayload}");

        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using (UnityWebRequest www = new UnityWebRequest(ChatUrl, "POST"))
            {
                www.timeout = 45;
                www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonPayload));
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("Authorization", "Bearer " + ApiKey);

                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    string hint = null;
                    string parseError = null;
                    try
                    {
                        var chatRes = Newtonsoft.Json.JsonConvert.DeserializeObject<ChatCompletionResponse>(www.downloadHandler.text);
                        var res = Newtonsoft.Json.JsonConvert.DeserializeObject<HintResponse>(chatRes.choices[0].message.content);
                        hint = res?.hint;
                    }
                    catch (Exception e) { parseError = "Hint parse failed: " + e.Message; }

                    if (parseError != null) onError?.Invoke(parseError);
                    else onSuccess?.Invoke(hint);
                    yield break;
                }

                long code = www.responseCode;
                string body = www.downloadHandler?.text;
                Debug.LogError($"[Hint HTTP {code}] attempt {attempt}/{maxAttempts} | {www.error} | body: {body} | request-id: {www.GetResponseHeader("x-request-id")}");

                bool retryable = code >= 500 || code == 429;
                if (retryable && attempt < maxAttempts)
                {
                    yield return new WaitForSeconds(1.5f * attempt);
                    continue;
                }

                onError?.Invoke($"{www.error} | {body}");
                yield break;
            }
        }
    }
    #endregion

    #region Text-to-Speech (OpenAI Speech)
    public IEnumerator TextToSpeech(string text, string voice, Action<AudioClip> onSuccess, Action<string> onError, Func<bool> shouldCancel = null)
    {
        return RequestSpeech(text, voice, null, onSuccess, (code, message) => onError?.Invoke(message), shouldCancel);
    }

    internal static string VoiceInstructions(string emotion, int intensity)
    {
        string delivery;
        switch (emotion)
        {
            case "HAPPY": delivery = "warm, bright and pleased"; break;
            case "SAD": delivery = "soft and subdued, with a slower cadence"; break;
            case "ANGRY": delivery = "firm and frustrated, with crisp emphasis and controlled volume"; break;
            case "NERVOUS": delivery = "uneasy, with short hesitant pauses"; break;
            case "THINKING": delivery = "reflective, with deliberate pauses between thoughts"; break;
            default: return "Read the supplied line verbatim in a calm, neutral conversational tone.";
        }
        return $"Read the supplied line verbatim. Sound {delivery}. " +
            $"Express this at {Mathf.Clamp(intensity, 0, 100)} out of 100, where 0 is neutral and 100 is pronounced. " +
            "Keep the voice recognizable and the words intelligible.";
    }

    internal static bool CanRetryWithDefaultVoice(long status)
    {
        return status == 400 || status == 403 || status == 404 || (status >= 500 && status <= 599);
    }

    public IEnumerator GenerateNPCSpeech(string text, string voice, string emotion, int intensity,
        Action<AudioClip, string> onSuccess, Action<string> onError, Func<bool> shouldCancel)
    {
        emotion = EmotionIntensityClassifier.IsSupported(emotion) ? emotion.Trim().ToUpperInvariant() : "NEUTRAL";
        long status = 0;
        string failure = null;
        yield return RequestSpeech(text, voice, VoiceInstructions(emotion, intensity),
            clip => onSuccess(clip, emotion), (code, message) => { status = code; failure = message; }, shouldCancel);
        if (shouldCancel() || failure == null) yield break;
        if (!CanRetryWithDefaultVoice(status))
        {
            onError(failure);
            yield break;
        }
        Debug.LogWarning($"[Speech] Expressive synthesis returned HTTP {status}. Trying standard delivery once.");
        yield return TextToSpeech(text, voice, clip => onSuccess(clip, "Standard"), onError, shouldCancel);
    }

    internal static string SpeechPayload(string text, string voice, string instructions)
    {
        var requestBody = new Dictionary<string, string> {
            { "model", instructions == null ? "tts-1" : "gpt-4o-mini-tts" },
            { "input", text }, { "voice", voice }, { "response_format", "mp3" }
        };
        if (instructions != null) requestBody.Add("instructions", instructions);
        return Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
    }

    private IEnumerator RequestSpeech(string text, string voice, string instructions, Action<AudioClip> onSuccess,
        Action<long, string> onError, Func<bool> shouldCancel)
    {
        if (shouldCancel != null && shouldCancel()) yield break;
        byte[] bodyRaw = Encoding.UTF8.GetBytes(SpeechPayload(text, voice, instructions));

        using (UnityWebRequest request = new UnityWebRequest(TtsUrl, "POST"))
        {
            request.timeout = 45;
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerAudioClip(TtsUrl, AudioType.MPEG);

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {ApiKey}");

            yield return WaitForRequest(request, shouldCancel);
            if (shouldCancel != null && shouldCancel()) yield break;

            if (request.result != UnityWebRequest.Result.Success)
            {
                // Safely read standard HTTP error string instead of binary text!
                string errorMsg = $"[TTS Error]: {request.error}";
                onError?.Invoke(request.responseCode, errorMsg);
            }
            else
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip != null) onSuccess?.Invoke(clip);
                else onError?.Invoke(request.responseCode, "Speech response contained no playable audio.");
            }
        }
    }
    #endregion

    #region Helper Classes
    private class ChatCompletionResponse { public List<Choice> choices; }
    private class Choice { public Message message; }
    private static bool TryParseNPCResponse(string json, out LLMResponse response, out string error)
    {
        response = null;
        error = null;
        try
        {
            var envelope = Newtonsoft.Json.JsonConvert.DeserializeObject<ChatCompletionResponse>(json);
            if (envelope?.choices == null || envelope.choices.Count == 0 ||
                envelope.choices[0]?.message == null || string.IsNullOrWhiteSpace(envelope.choices[0].message.content))
            {
                error = "NPC response envelope is missing message content.";
                return false;
            }
            response = Newtonsoft.Json.JsonConvert.DeserializeObject<LLMResponse>(envelope.choices[0].message.content);
            return true;
        }
        catch (Exception)
        {
            error = "Invalid NPC response JSON.";
            return false;
        }
    }
    private class Message { public string content; }
    [Serializable] private class HintResponse { public string hint; }

    [Serializable]
    public class DeepgramResponse
    {
        public DeepgramResults results;
    }

    [Serializable]
    public class DeepgramResults
    {
        public DeepgramChannel[] channels;
    }

    [Serializable]
    public class DeepgramChannel
    {
        public DeepgramAlternative[] alternatives;
    }

    [Serializable]
    public class DeepgramAlternative
    {
        public string transcript;
        public float confidence;
    }
    #endregion

    private static IEnumerator WaitForRequest(UnityWebRequest request, Func<bool> shouldCancel)
    {
        string stage = request.url.StartsWith(DeepgramUrl, StringComparison.Ordinal) ? "transcription" :
            request.url.StartsWith(ChatUrl, StringComparison.Ordinal) ? "dialogue" : "tts";
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Debug.Log($"[VoicePerf] {stage} start frame={Time.frameCount} time={Time.realtimeSinceStartupAsDouble:F3}");
        var operation = request.SendWebRequest();
        while (!operation.isDone)
        {
            if (shouldCancel != null && shouldCancel())
            {
                request.Abort();
                Debug.Log($"[VoicePerf] {stage} cancelled ms={timer.Elapsed.TotalMilliseconds:F1} frame={Time.frameCount}");
                yield break;
            }
            yield return null;
        }
        Debug.Log($"[VoicePerf] {stage} complete ms={timer.Elapsed.TotalMilliseconds:F1} HTTP={request.responseCode} result={request.result} frame={Time.frameCount} time={Time.realtimeSinceStartupAsDouble:F3}");
    }

    private static bool IsValidEmotion(string emotion)
    {
        switch (emotion)
        {
            case "HAPPY":
            case "SAD":
            case "ANGRY":
            case "NERVOUS":
            case "THINKING":
            case "NEUTRAL":
                return true;

            default:
                return false;
        }
    }

}
