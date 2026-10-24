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
    public IEnumerator TranscribeAudioWithPrompt(byte[] wavData, string initialPrompt, Action<string> onSuccess, Action<string> onError)
    {
        using (UnityWebRequest www = new UnityWebRequest(DeepgramUrl, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(wavData);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "audio/wav");
            www.SetRequestHeader("Authorization", "Token " + DeepgramApiKey);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(www.error + " | " + www.downloadHandler.text);
            }
            else
            {
                string rawJson = www.downloadHandler.text;
                DeepgramResponse res = Newtonsoft.Json.JsonConvert.DeserializeObject<DeepgramResponse>(rawJson);

                if (res != null && res.results != null && res.results.channels != null && res.results.channels.Length > 0)
                {
                    string transcript = res.results.channels[0].alternatives[0].transcript;
                    Debug.Log($"[Deepgram Transcript]: {transcript}");
                    onSuccess?.Invoke(transcript);
                }
                else
                {
                    onSuccess?.Invoke("");
                }
            }
        }
    }
    #endregion

    #region LLM Structured Completion
    public IEnumerator SendChatRequest(string systemPrompt, List<DialogueTurn> history, string currentTranscript, Action<LLMResponse> onSuccess, Action<string> onError)
    {
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        if (history != null)
        {
            foreach (var turn in history)
            {
                string role = turn.speaker == "User" ? "user" : "assistant";
                messages.Add(new { role = role, content = turn.dialogue });
            }
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
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + ApiKey);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(www.error + " | " + www.downloadHandler.text);
            }
            else
            {
                string rawJson = www.downloadHandler.text;
                var chatRes = Newtonsoft.Json.JsonConvert.DeserializeObject<ChatCompletionResponse>(rawJson);
                string contentJson = chatRes.choices[0].message.content;

                // Return to NEUTRAL when the AI give the emotion not suitable with the 6 basic emmotion.
                LLMResponse response;

                try
                {
                    response =
                        Newtonsoft.Json.JsonConvert.DeserializeObject<LLMResponse>(
                            contentJson
                        );
                }
                catch (Exception ex)
                {
                    onError?.Invoke("Invalid NPC response JSON: " + ex.Message);
                    yield break;
                }

                if (response == null ||
                    string.IsNullOrWhiteSpace(response.npc_response))
                {
                    onError?.Invoke("NPC response is missing or empty.");
                    yield break;
                }

                if (string.IsNullOrWhiteSpace(response.emotion))
                {
                    onError?.Invoke("NPC emotion is missing.");
                    yield break;
                }

                response.emotion = response.emotion.Trim().ToUpperInvariant();

                if (!IsValidEmotion(response.emotion))
                {
                    Debug.LogWarning(
                        "Unsupported NPC emotion: " + response.emotion
                    );

                    response.emotion = "NEUTRAL";
                }

                // Return the validated response.
                onSuccess?.Invoke(response);

            }
        }
    }


    public IEnumerator SendHintRequest(string hintPrompt, Action<string> onSuccess, Action<string> onError = null)
    {
        var messages = new List<object>
        {
            new { role = "system", content = hintPrompt }
        };

        var requestBody = new
        {
            model = "gpt-6-luna",
            response_format = new { type = "json_object" },
            messages = messages
        };

        string jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);

        using (UnityWebRequest www = new UnityWebRequest(ChatUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + ApiKey);

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                string rawJson = www.downloadHandler.text;
                var chatRes = Newtonsoft.Json.JsonConvert.DeserializeObject<ChatCompletionResponse>(rawJson);
                string contentJson = chatRes.choices[0].message.content;
                Debug.Log("[AI Response JSON] " + contentJson); //Debug

                HintResponse res = Newtonsoft.Json.JsonConvert.DeserializeObject<HintResponse>(contentJson);
                onSuccess?.Invoke(res.hint);
            }
            else
            {
                onError?.Invoke(www.error);
            }
        }
    }
    #endregion

    #region Text-to-Speech (OpenAI Speech)
    public IEnumerator TextToSpeech(string text, string voice, Action<AudioClip> onSuccess, Action<string> onError)
    {
        var requestBody = new
        {
            model = "tts-1",
            input = text,
            voice = voice,
            response_format = "mp3"
        };

        string jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

        using (UnityWebRequest request = new UnityWebRequest(TtsUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerAudioClip(TtsUrl, AudioType.MPEG);

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {ApiKey}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // Safely read standard HTTP error string instead of binary text!
                string errorMsg = $"[TTS Error]: {request.error}";
                Debug.LogError(errorMsg);
                onError?.Invoke(errorMsg);
            }
            else
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                onSuccess?.Invoke(clip);
            }
        }
    }
    #endregion

    #region Helper Classes
    private class ChatCompletionResponse { public List<Choice> choices; }
    private class Choice { public Message message; }
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