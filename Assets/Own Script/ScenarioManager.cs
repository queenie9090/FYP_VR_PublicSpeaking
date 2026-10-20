using UnityEngine;
using UnityEngine.SceneManagement;
using System;

public class ScenarioManager : MonoBehaviour
{
    public static ScenarioManager Instance { get; private set; }

    [Header("Selected Configuration")]
    public string SelectedScenario { get; private set; } = "Classroom";
    public string SelectedInputMode { get; private set; } = "Controller";

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void SetConfiguration(string scenario, string inputMode)
    {
        SelectedScenario = scenario;
        SelectedInputMode = inputMode;


        Debug.Log($"[ScenarioManager] Configured Scenario: {SelectedScenario} | Input Mode: {SelectedInputMode}");
    }

    // Async method sending log to Supabase database
    public async void GenerateSessionHistory()
    {
        // 1. Local PlayerPrefs Fallback Log
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        string historyEntry = $"{timestamp} | Scenario: {SelectedScenario} | Mode: {SelectedInputMode}";
        string existingLogs = PlayerPrefs.GetString("Session_History_Logs", "");
        PlayerPrefs.SetString("Session_History_Logs", historyEntry + "\n" + existingLogs);
        PlayerPrefs.Save();

        // 2. Upload to Supabase if client session exists
        try
        {
            var client = SupabaseAuthManager.Instance != null ? SupabaseAuthManager.Instance.Client : null;
            var currentUser = client?.Auth.CurrentUser;

            if (currentUser != null)
            {
                var newLog = new SessionHistoryModel
                {
                    UserId = currentUser.Id,
                    ScenarioName = SelectedScenario,
                    InputMode = SelectedInputMode,
                    CreatedAt = DateTime.UtcNow
                };

                await client.From<SessionHistoryModel>().Insert(newLog);
                Debug.Log($"[Supabase History] Logged successfully for user: {currentUser.Id}");
            }
            else
            {
                Debug.LogWarning("[Supabase History] No active user logged in. Saved locally to PlayerPrefs.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Supabase History] Upload failed: {ex.Message}. Local log preserved.");
        }
    }

    public void LoadTutorialScene()
    {
        GenerateSessionHistory();
        SceneManager.LoadScene("TutorialScene");
    }

    public void LoadSelectedVRScene()
    {
        GenerateSessionHistory();

        string targetScene = SelectedScenario;
        Debug.Log($"[ScenarioManager] Loading Scene: {targetScene}");
        SceneManager.LoadScene(targetScene);
    }
}