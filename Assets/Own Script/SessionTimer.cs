using System;
using UnityEngine;

public class SessionTimer : MonoBehaviour
{
    public float ElapsedTimeSeconds { get; private set; }
    public float TotalSpeakingTimeSeconds { get; private set; }
    public float TotalSilenceTimeSeconds { get; private set; }
    public DateTime StartTimestamp { get; private set; }

    public void ResetTimer()
    {
        ElapsedTimeSeconds = 0f;
        TotalSpeakingTimeSeconds = 0f;
        TotalSilenceTimeSeconds = 0f;
        StartTimestamp = DateTime.UtcNow;
    }

    public void UpdateElapsedTime(float deltaTime)
    {
        if (Time.timeScale > 0f)
        {
            ElapsedTimeSeconds += deltaTime;
        }
    }

    public void RecordTurnDurations(float speechDuration, float silenceDuration)
    {
        TotalSpeakingTimeSeconds += speechDuration;
        TotalSilenceTimeSeconds += silenceDuration;
    }
}