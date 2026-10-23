using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

public class DataExporter : MonoBehaviour
{
    /// <summary>
    /// Exports complete session metrics and detailed dialogue logs to a formatted CSV file.
    /// </summary>
    public static string ExportToCSV(NormalizedSessionData data)
    {
        if (data == null || data.performance_metric == null || data.performance_metric.Count == 0)
        {
            Debug.LogWarning("[DataExporter] No session data available to export.");
            return null;
        }

        var metric = data.performance_metric[0];
        StringBuilder sb = new StringBuilder();

        // 1. Session Summary Section
        sb.AppendLine("=== VR COMMUNICATION EVALUATION SUMMARY ===");
        sb.AppendLine("Scenario,Topic,Difficulty,InputMode,Date,DurationSeconds");
        sb.AppendLine($"\"{data.scenario_type}\",\"{data.scenario_topic}\",\"{data.difficulty_level}\",\"{data.input_mode_used}\",\"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",{data.duration_seconds:F1}");
        sb.AppendLine();

        // 2. Performance Scores Breakdown
        sb.AppendLine("=== PERFORMANCE SCORES & METRICS ===");
        sb.AppendLine("OverallScore,SpeechClarityScore,WPM,FillerWordCount,SilenceDurationSeconds,EyeContactPct,HandGestureScore,GesturesPerMinute");
        sb.AppendLine($"{metric.overall_score:F1},{metric.speech_clarity_score:F1},{metric.average_wpm:F1},{metric.filler_word_count},{metric.total_silence_seconds:F1},{metric.eye_contact_pct:F1},{metric.hand_gesture_score:F1},{metric.gestures_per_minute:F1}");
        sb.AppendLine();

        // 3. AI Coaching Summary
        sb.AppendLine("=== AI COACHING FEEDBACK ===");
        string sanitizedFeedback = metric.ai_feedback_summary?.Replace("\"", "\"\"") ?? "";
        sb.AppendLine($"\"{sanitizedFeedback}\"");
        sb.AppendLine();

        // 4. Itemized Dialogue Log Breakdown
        sb.AppendLine("=== CONVERSATION LOG TRANSCRIPT ===");
        sb.AppendLine("TurnSequence,UserTranscript,AIResponse,FillerWords,SpeakingRateWPM,SilenceSeconds");

        if (data.dialogue_turn != null)
        {
            foreach (var turn in data.dialogue_turn)
            {
                string sanitizedUser = turn.user_transcript?.Replace("\"", "\"\"") ?? "";
                string sanitizedAI = turn.ai_response_text?.Replace("\"", "\"\"") ?? "";

                sb.AppendLine($"{turn.turn_sequence},\"{sanitizedUser}\",\"{sanitizedAI}\",{turn.filler_word_count},{turn.speaking_rate_wpm:F1},{turn.silence_duration_seconds:F1}");
            }
        }

        string filePath = Path.Combine(Application.persistentDataPath, $"SessionReport_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

        Debug.Log($"[DataExporter] Complete CSV exported successfully to: {filePath}");
        return filePath;
    }

    /// <summary>
    /// Exports full session structure and turns into a structured JSON payload.
    /// </summary>
    public static string ExportToJSON(NormalizedSessionData data)
    {
        if (data == null)
        {
            Debug.LogWarning("[DataExporter] No session data available to export.");
            return null;
        }

        string jsonString = JsonUtility.ToJson(data, true);
        string filePath = Path.Combine(Application.persistentDataPath, $"SessionReport_{DateTime.Now:yyyyMMdd_HHmmss}.json");

        File.WriteAllText(filePath, jsonString, Encoding.UTF8);

        Debug.Log($"[DataExporter] Complete JSON exported successfully to: {filePath}");
        return filePath;
    }

    /// <summary>
    /// Generates a printable HTML/PDF report containing all session details, metrics, and dialogue logs.
    /// </summary>
    public static string ExportToPDFReport(NormalizedSessionData data)
    {
        if (data == null || data.performance_metric == null || data.performance_metric.Count == 0)
        {
            Debug.LogWarning("[DataExporter] No session data available to export.");
            return null;
        }

        var metric = data.performance_metric[0];
        StringBuilder html = new StringBuilder();

        html.AppendLine("<!DOCTYPE html><html><head><meta charset='UTF-8'><style>");
        html.AppendLine("body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 40px; background: #0f172a; color: #f8fafc; }");
        html.AppendLine(".header { border-bottom: 2px solid #38bdf8; padding-bottom: 10px; margin-bottom: 20px; }");
        html.AppendLine(".card { background: #1e293b; padding: 20px; border-radius: 8px; margin-bottom: 20px; box-shadow: 0 4px 6px rgba(0,0,0,0.3); }");
        html.AppendLine("h1 { color: #38bdf8; margin: 0 0 10px 0; } h2 { color: #818cf8; margin-top: 0; }");
        html.AppendLine("p { line-height: 1.6; margin: 6px 0; }");
        html.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 10px; }");
        html.AppendLine("th, td { padding: 12px; border: 1px solid #334155; text-align: left; }");
        html.AppendLine("th { background: #334155; color: #38bdf8; }");
        html.AppendLine(".user-dialogue { color: #38bdf8; font-weight: bold; }");
        html.AppendLine(".npc-dialogue { color: #4ade80; font-weight: bold; }");
        html.AppendLine("</style></head><body>");

        // Header Section
        html.AppendLine($"<div class='header'><h1>VR Communication Evaluation Report</h1>");
        html.AppendLine($"<p><b>Date Generated:</b> {DateTime.Now:MMMM dd, yyyy HH:mm} | <b>Session Duration:</b> {data.duration_seconds:F0}s</p></div>");

        // Scenario Metadata Card
        html.AppendLine($"<div class='card'><h2>Scenario Configuration</h2>");
        html.AppendLine($"<p><b>Scenario Type:</b> {data.scenario_type}</p>");
        html.AppendLine($"<p><b>Topic:</b> {data.scenario_topic} | <b>Difficulty:</b> {data.difficulty_level} | <b>Input Mode:</b> {data.input_mode_used}</p>");
        html.AppendLine($"<p><b>Overall Composite Score:</b> <span style='font-size: 20px; color: #38bdf8; font-weight: bold;'>{metric.overall_score:F0}%</span></p></div>");

        // Detailed Metrics Table Card
        html.AppendLine("<div class='card'><h2>Performance Metrics Breakdown</h2>");
        html.AppendLine("<table><tr><th>Evaluation Category</th><th>Score</th><th>Detailed Analysis</th></tr>");

        string wpmStatus = metric.average_wpm < 110 ? "Slow Pace" : (metric.average_wpm > 160 ? "Fast Pace" : "Optimal Pace");
        html.AppendLine($"<tr><td><b>Speech Clarity</b></td><td>{metric.speech_clarity_score:F0}%</td><td>WPM: {metric.average_wpm:F0} ({wpmStatus}) | Fillers: {metric.filler_word_count} | Silence: {metric.total_silence_seconds:F1}s</td></tr>");

        string eyeStatus = metric.eye_contact_pct >= 70 ? "Target Reached" : "Below Target";
        html.AppendLine($"<tr><td><b>Eye Contact</b></td><td>{metric.eye_contact_pct:F0}%</td><td>Visual Focus Rate ({eyeStatus})</td></tr>");

        string gestureStatus = metric.gestures_per_minute >= 3.0f ? "Active Non-Verbal Expression" : "Low Gesture Rate";
        html.AppendLine($"<tr><td><b>Hand Gestures</b></td><td>{metric.hand_gesture_score:F0}%</td><td>{metric.gestures_per_minute:F1} GPM ({gestureStatus})</td></tr>");
        html.AppendLine("</table></div>");

        // AI Coaching Summary Card
        html.AppendLine("<div class='card'><h2>AI Executive Coaching Summary</h2>");
        html.AppendLine($"<p>{metric.ai_feedback_summary}</p></div>");

        // Conversation Transcript Card
        html.AppendLine("<div class='card'><h2>Conversation Transcript Log</h2>");
        if (data.dialogue_turn != null && data.dialogue_turn.Count > 0)
        {
            foreach (var turn in data.dialogue_turn)
            {
                html.AppendLine($"<p><span class='user-dialogue'>Turn {turn.turn_sequence} - You:</span> {turn.user_transcript}</p>");
                html.AppendLine($"<p><span class='npc-dialogue'>NPC Response:</span> {turn.ai_response_text}</p>");
                html.AppendLine($"<p style='font-size: 12px; color: #94a3b8;'>[Turn Metrics -> WPM: {turn.speaking_rate_wpm:F0} | Fillers: {turn.filler_word_count} | Silence: {turn.silence_duration_seconds:F1}s]</p><hr style='border: 0.5px solid #334155;'>");
            }
        }
        else
        {
            html.AppendLine("<p>No dialogue turns recorded for this session.</p>");
        }
        html.AppendLine("</div>");

        html.AppendLine("</body></html>");

        string filePath = Path.Combine(Application.persistentDataPath, $"SessionReport_{DateTime.Now:yyyyMMdd_HHmmss}.html");
        File.WriteAllText(filePath, html.ToString(), Encoding.UTF8);

        Debug.Log($"[DataExporter] Complete HTML/PDF report saved to: {filePath}");
        return filePath;
    }
}