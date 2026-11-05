
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class NPCEmotionRecord
{
    public int turnNumber;
    public string npcName;
    public string emotion;
    public int intensity;
}

public class NPCEmotionSessionManager : MonoBehaviour
{
    public static NPCEmotionSessionManager Instance { get; private set; }

    [Header("Session Emotion History")]
    [SerializeField]
    private List<NPCEmotionRecord> emotionHistory = new();

    public IReadOnlyList<NPCEmotionRecord> EmotionHistory
        => emotionHistory;

    public int TotalEmotionTurns => emotionHistory.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void StartNewSession()
    {
        emotionHistory.Clear();
        Debug.Log("[Emotion Session] New session started.");
    }

    public void RecordEmotion(
        int turnNumber,
        string npcName,
        string emotion,
        int intensity)
    {
        if (turnNumber < 1 ||
            string.IsNullOrWhiteSpace(emotion))
            return;

        emotion = emotion.Trim().ToUpperInvariant();

        if (emotion != "HAPPY" &&
            emotion != "SAD" &&
            emotion != "ANGRY" &&
            emotion != "NERVOUS" &&
            emotion != "THINKING" &&
            emotion != "NEUTRAL")
        {
            Debug.LogWarning(
                "[Emotion Session] Invalid emotion: " + emotion
            );
            return;
        }

        // Prevent recording the same turn twice.
        foreach (var record in emotionHistory)
        {
            if (record.turnNumber == turnNumber &&
                record.npcName == npcName)
            {
                Debug.LogWarning(
                    "[Emotion Session] Duplicate turn skipped."
                );
                return;
            }
        }

        int validIntensity = emotion == "NEUTRAL"
            ? 0
            : Mathf.Clamp(intensity, 0, 100);

        emotionHistory.Add(new NPCEmotionRecord
        {
            turnNumber = turnNumber,
            npcName = npcName,
            emotion = emotion,
            intensity = validIntensity
        });

        Debug.Log(
            $"[Emotion Session] Turn {turnNumber} | " +
            $"{npcName} | {emotion} | {validIntensity}%"
        );
    }

    public int GetEmotionCount(string emotion)
    {
        int count = 0;

        foreach (var record in emotionHistory)
        {
            if (record.emotion == emotion)
                count++;
        }

        return count;
    }

    public float GetEmotionPercentage(string emotion)
    {
        if (emotionHistory.Count == 0)
            return 0f;

        return GetEmotionCount(emotion) * 100f
            / emotionHistory.Count;
    }

    public NPCEmotionRecord GetFinalEmotion()
    {
        if (emotionHistory.Count == 0)
            return null;

        return emotionHistory[emotionHistory.Count - 1];
    }
}
