using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public static class MetricsCalculator
{
    // Predefined Filler Word List as specified in FYP report
    private static readonly List<string> FillerWordList = new List<string>
    {
        "um", "uh", "eh", "ah", "er", "hmm"
    };

    /// <summary>
    /// ALGORITHM 3: Filler Word Detection Algorithm
    /// Identifies and counts filler words from the cleaned transcript.
    /// </summary>
    public static int DetectFillerWords(string transcript)
    {
        if (string.IsNullOrEmpty(transcript)) return 0;

        // Clean transcript: convert to lowercase and remove punctuation
        string cleanedText = Regex.Replace(transcript.ToLower(), @"[^\w\s]", "");
        string[] words = cleanedText.Split(new char[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        int fillerWordCount = 0;
        foreach (string word in words)
        {
            if (FillerWordList.Contains(word))
            {
                fillerWordCount++;
            }
        }

        return fillerWordCount;
    }

    /// <summary>
    /// ALGORITHM 2: Speaking Rate Calculation Algorithm
    /// Calculates total words and Words Per Minute (WPM).
    /// </summary>
    public static (int totalWordCount, float wpm) CalculateSpeakingRate(string transcript, float speakingDurationSeconds)
    {
        if (string.IsNullOrEmpty(transcript)) return (0, 0.0f);

        string cleanedText = Regex.Replace(transcript.ToLower(), @"[^\w\s]", "");
        string[] words = cleanedText.Split(new char[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        int totalWordCount = words.Length;

        float wpm = 0.0f;
        if (speakingDurationSeconds > 0.0f)
        {
            wpm = (totalWordCount / speakingDurationSeconds) * 60.0f;
        }

        return (totalWordCount, wpm);
    }

    /// <summary>
    /// ALGORITHM 4: Speech Clarity Scoring Algorithm
    /// Evaluates FWPM, pace deviation from 140 WPM, and outputs a normalized 0-100 score.
    /// </summary>
    public static (float speechClarityScore, float fillerWordsPerMinute) CalculateSpeechClarity(
        int totalFillerWordCount,
        float speakingDurationSeconds,
        float wpm)
    {
        float speakingDurationMinutes = speakingDurationSeconds / 60.0f;
        float fwpm = 0.0f;

        if (speakingDurationSeconds > 0.0f)
        {
            fwpm = totalFillerWordCount / speakingDurationMinutes;
        }

        // Pace Deviation from target 140 WPM
        float paceDeviation = Math.Abs(wpm - 140.0f);

        // Filler Frequency Ratio relative to literature reference point (5 per minute)
        float fillerFrequencyRatio = fwpm / 5.0f;

        // System Normalization Process
        // Raw penalty weights: Pace Deviation (0.4 weight) + Filler Ratio (15.0 penalty weight)
        float rawPenalty = (paceDeviation * 0.4f) + (fillerFrequencyRatio * 15.0f);
        float speechClarityScore = Math.Clamp(100.0f - rawPenalty, 0.0f, 100.0f);

        return (speechClarityScore, fwpm);
    }

    /// <summary>
    /// ALGORITHM 9: Overall Performance Score Algorithm
    /// Computes the weighted composite score based on the selected scenario type.
    /// </summary>
    public static float CalculateOverallScore(
    string scenarioType,
    float speechClarityScore,
    float eyeContactScore,
    float gestureScore)
    {
        float wClarity = 0.40f;
        float wEye = 0.30f;
        float wGesture = 0.30f;

        // Remove "Scene", spaces, and make lowercase for foolproof matching
        string cleanScenario = string.IsNullOrEmpty(scenarioType)
            ? ""
            : scenarioType.Replace("Scene", "").Replace(" ", "").Trim().ToLower();

        switch (cleanScenario)
        {
            case "hall":
            case "stagevenue":
                wClarity = 0.40f;
                wEye = 0.20f;
                wGesture = 0.40f;
                break;

            case "cafeteria":
            case "interview":
            case "normalconversation":
                wClarity = 0.50f;
                wEye = 0.30f;
                wGesture = 0.20f;
                break;

            case "classroom":
                wClarity = 0.40f;
                wEye = 0.40f;
                wGesture = 0.20f;
                break;

            default:
                Debug.LogWarning($"[MetricsCalculator] Unrecognized scenario '{scenarioType}'. Applying default 40/30/30 weighting.");
                wClarity = 0.40f;
                wEye = 0.30f;
                wGesture = 0.30f;
                break;
        }

        float rawCompositeScore = (wClarity * speechClarityScore) + (wEye * eyeContactScore) + (wGesture * gestureScore);
        return Math.Clamp((float)Math.Round(rawCompositeScore), 0.0f, 100.0f);
    }
}