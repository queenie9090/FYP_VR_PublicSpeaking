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

                LLMResponse response = Newtonsoft.Json.JsonConvert.DeserializeObject<LLMResponse>(contentJson);
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
            voice = string.IsNullOrEmpty(voice) ? "alloy" : voice
        };

        string jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);

        using (UnityWebRequest www = new UnityWebRequest(TtsUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerAudioClip(TtsUrl, AudioType.MPEG);
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + ApiKey);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(www.error + " | " + www.downloadHandler.text);
            }
            else
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
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

    // Deepgram Deserialization Structures
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
}