using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

public class DataExporter : MonoBehaviour
{
    /// <summary>
    /// Exports normalized session metrics to a formatted CSV file.
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

        // Header Row
        sb.AppendLine("Scenario,Topic,Difficulty,InputMode,OverallScore,SpeechClarity,WPM,FillerWords,SilenceDuration,EyeContact,GesturesGPM");

        // Data Row
        sb.AppendLine($"\"{data.scenario_type}\",\"{data.scenario_topic}\",\"{data.difficulty_level}\",\"{data.input_mode_used}\"," +
                      $"{metric.overall_score:F1},{metric.speech_clarity_score:F1},{metric.average_wpm:F1}," +
                      $"{metric.filler_word_count},{metric.total_silence_seconds:F1},{metric.eye_contact_pct:F1},{metric.gestures_per_minute:F1}");

        // Dialogue Log Breakdown Section
        sb.AppendLine();
        sb.AppendLine("TurnSequence,UserTranscript,AIResponse,FillerWords,WPM,SilenceSeconds");

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

        Debug.Log($"[DataExporter] CSV exported successfully to: {filePath}");
        return filePath;
    }

    /// <summary>
    /// Exports session data to a formatted JSON file.
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

        Debug.Log($"[DataExporter] JSON exported successfully to: {filePath}");
        return filePath;
    }

    /// <summary>
    /// Generates a clean HTML-based formatted report saved as an exported file (openable in browsers/PDF viewers).
    /// </summary>
    public static string ExportToPDFReport(NormalizedSessionData data)
    {
        if (data == null || data.performance_metric == null || data.performance_metric.Count == 0)
            return null;

        var metric = data.performance_metric[0];
        StringBuilder html = new StringBuilder();

        html.AppendLine("<!DOCTYPE html><html><head><style>");
        html.AppendLine("body { font-family: Arial, sans-serif; margin: 30px; background: #0f172a; color: #f8fafc; }");
        html.AppendLine(".card { background: #1e293b; padding: 20px; border-radius: 8px; margin-bottom: 20px; }");
        html.AppendLine("h1 { color: #38bdf8; } h2 { color: #818cf8; }");
        html.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 10px; }");
        html.AppendLine("th, td { padding: 10px; border: 1px solid #334155; text-align: left; }");
        html.AppendLine("th { background: #334155; color: #38bdf8; }");
        html.AppendLine("</style></head><body>");

        html.AppendLine($"<h1>VR Communication Evaluation Report</h1>");
        html.AppendLine($"<div class='card'><h2>Scenario: {data.scenario_type}</h2>");
        html.AppendLine($"<p><b>Topic:</b> {data.scenario_topic} | <b>Difficulty:</b> {data.difficulty_level} | <b>Mode:</b> {data.input_mode_used}</p>");
        html.AppendLine($"<p><b>Overall Score:</b> {metric.overall_score:F0}%</p></div>");

        html.AppendLine("<div class='card'><h2>Performance Metrics</h2>");
        html.AppendLine("<table><tr><th>Metric</th><th>Score / Value</th></tr>");
        html.AppendLine($"<tr><td>Speech Clarity</td><td>{metric.speech_clarity_score:F0}% (WPM: {metric.average_wpm:F0}, Fillers: {metric.filler_word_count})</td></tr>");
        html.AppendLine($"<tr><td>Eye Contact</td><td>{metric.eye_contact_pct:F0}%</td></tr>");
        html.AppendLine($"<tr><td>Hand Gestures</td><td>{metric.hand_gesture_score:F0}% ({metric.gestures_per_minute:F1} GPM)</td></tr>");
        html.AppendLine("</table></div>");

        html.AppendLine("<div class='card'><h2>AI Feedback</h2>");
        html.AppendLine($"<p>{metric.ai_feedback_summary}</p></div>");

        html.AppendLine("</body></html>");

        string filePath = Path.Combine(Application.persistentDataPath, $"SessionReport_{DateTime.Now:yyyyMMdd_HHmmss}.html");
        File.WriteAllText(filePath, html.ToString(), Encoding.UTF8);

        Debug.Log($"[DataExporter] Report saved to: {filePath}");
        return filePath;
    }
}