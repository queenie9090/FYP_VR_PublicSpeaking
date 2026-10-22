using System.IO;
using UnityEngine;

public static class ConfigLoader
{
    [System.Serializable]
    private class ConfigData
    {
        public string apiKey;          // OpenAI API Key
        public string supabaseUrl;
        public string supabaseAnonKey;
        public string deepgramApiKey;  // Deepgram API Key
    }

    private static ConfigData cachedConfig;

    private static void LoadConfig()
    {
        if (cachedConfig != null) return;

        string path = Path.Combine(Application.streamingAssetsPath, "config.json");
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            cachedConfig = JsonUtility.FromJson<ConfigData>(json);
        }
        else
        {
            Debug.LogError("config.json file not found in StreamingAssets folder!");
        }
    }

    public static string GetApiKey()
    {
        LoadConfig();
        return cachedConfig != null ? cachedConfig.apiKey : "";
    }

    public static string GetSupabaseUrl()
    {
        LoadConfig();
        return cachedConfig != null ? cachedConfig.supabaseUrl : "";
    }

    public static string GetSupabaseAnonKey()
    {
        LoadConfig();
        return cachedConfig != null ? cachedConfig.supabaseAnonKey : "";
    }

    public static string GetDeepgramApiKey()
    {
        LoadConfig();
        return cachedConfig != null ? cachedConfig.deepgramApiKey : "";
    }
}