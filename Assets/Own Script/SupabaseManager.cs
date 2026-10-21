using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class SupabaseManager : MonoBehaviour
{
    public IEnumerator SaveSessionResult(SessionResultPayload payload, Action onSuccess, Action<string> onError)
    {
        string supabaseUrl = ConfigLoader.GetSupabaseUrl().Trim();
        string supabaseApiKey = ConfigLoader.GetSupabaseAnonKey().Trim();

        string endpoint = $"{supabaseUrl}/rest/v1/session_history";
        string jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(payload);

        using (UnityWebRequest www = new UnityWebRequest(endpoint, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();

            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("apikey", supabaseApiKey);
            www.SetRequestHeader("Authorization", "Bearer " + supabaseApiKey);
            www.SetRequestHeader("Prefer", "return=minimal");

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(www.error + " | " + www.downloadHandler.text);
            }
            else
            {
                onSuccess?.Invoke();
            }
        }
    }
}