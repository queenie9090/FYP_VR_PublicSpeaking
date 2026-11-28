using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class DataExporter : MonoBehaviour
{
    // A cancelled save dialog returns null and is not an error.
    public static void ExportWithFeedback(Func<string> export, Action<string> onError)
    {
        try { export(); }
        catch (Exception ex)
        {
            Debug.LogWarning("[DataExporter] Export failed: " + ex.Message);
            onError?.Invoke("Export failed. Check the save location and try again.");
        }
    }
    /// <summary>
    /// Exports complete session metrics and detailed dialogue logs to a formatted CSV file.
    /// Prompts the user to choose a save location on Windows PC by default.
    /// </summary>
    public static string ExportToCSV(NormalizedSessionData data, bool promptUserForPath = true)
    {
        if (data == null || data.performance_metric == null || data.performance_metric.Count == 0)
        {
            Debug.LogWarning("[DataExporter] No session data available to export.");
            return null;
        }

        string defaultName = $"SessionReport_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string filePath = ResolveSavePath(defaultName, "csv", promptUserForPath);
        if (string.IsNullOrEmpty(filePath)) return null;

        var metric = data.performance_metric[0];
        StringBuilder sb = new StringBuilder();
        string sessionStatus = string.IsNullOrEmpty(data.status) ? "Completed" : data.status;

        // Title Banner
        sb.AppendLine("VR COMMUNICATION EVALUATION REPORT");
        sb.AppendLine($"Generated,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        // 1. Session Summary (Vertical Key-Value layout — no horizontal scrolling)
        sb.AppendLine("1. SESSION SUMMARY");
        sb.AppendLine("Field,Value");
        sb.AppendLine($"Scenario,{CsvEscape(data.scenario_type)}");
        sb.AppendLine($"Topic,{CsvEscape(data.scenario_topic)}");
        sb.AppendLine($"Difficulty,{CsvEscape(data.difficulty_level)}");
        sb.AppendLine($"Input Mode,{CsvEscape(data.input_mode_used)}");
        sb.AppendLine($"Status,{CsvEscape(sessionStatus)}");
        sb.AppendLine($"Duration (s),{data.duration_seconds:F1}");
        sb.AppendLine();

        // 2. Performance Scores (Vertical table with Rating/Benchmark column)
        string wpmStatus = metric.average_wpm < 110 ? "Slow Pace" : (metric.average_wpm > 160 ? "Fast Pace" : "Optimal Pace");
        string eyeStatus = metric.eye_contact_pct >= 70 ? "Target Reached" : "Below Target";
        string gestureStatus = metric.gestures_per_minute >= 3.0f ? "Active Expression" : "Low Gesture Rate";

        sb.AppendLine("2. PERFORMANCE METRICS");
        sb.AppendLine("Metric,Score / Value,Assessment");
        sb.AppendLine($"Overall Composite Score,{metric.overall_score:F1}%,Overall Rating");
        sb.AppendLine($"Speech Clarity,{metric.speech_clarity_score:F1}%,{wpmStatus}");
        sb.AppendLine($"Average Speaking Rate,{metric.average_wpm:F1} WPM,Target: 110-160 WPM");
        sb.AppendLine($"Filler Words,{metric.filler_word_count},Total Count");
        sb.AppendLine($"Total Silence,{metric.total_silence_seconds:F1} s,Pause Duration");
        sb.AppendLine($"Eye Contact,{metric.eye_contact_pct:F1}%,{eyeStatus}");
        sb.AppendLine($"Hand Gesture Score,{metric.hand_gesture_score:F1}%,{gestureStatus}");
        sb.AppendLine($"Gestures Per Minute,{metric.gestures_per_minute:F1} GPM,Target: >= 3.0 GPM");
        sb.AppendLine();

        // 3. AI Coaching Summary
        sb.AppendLine("3. AI COACHING FEEDBACK");
        sb.AppendLine("Summary");
        sb.AppendLine(CsvEscape(metric.ai_feedback_summary));
        sb.AppendLine();

        // 4. Itemized Dialogue Log Breakdown (Uses all 6 columns A:F cleanly)
        sb.AppendLine("4. CONVERSATION TRANSCRIPT LOG");
        sb.AppendLine("Turn #,User Transcript,AI Response,Filler Words,Speaking Rate (WPM),Silence (s)");

        if (data.dialogue_turn != null && data.dialogue_turn.Count > 0)
        {
            foreach (var turn in data.dialogue_turn)
            {
                sb.AppendLine(
                    $"{turn.turn_sequence}," +
                    $"{CsvEscape(turn.user_transcript)}," +
                    $"{CsvEscape(turn.ai_response_text)}," +
                    $"{turn.filler_word_count}," +
                    $"{turn.speaking_rate_wpm:F1}," +
                    $"{turn.silence_duration_seconds:F1}"
                );
            }
        }
        else
        {
            sb.AppendLine("No dialogue turns recorded.");
        }

        // UTF-8 with BOM ensures Excel displays special characters cleanly
        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Debug.Log($"[DataExporter] Complete CSV exported successfully to: {filePath}");
        return filePath;
    }

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        // Flatten line breaks so multi-line AI responses stay cleanly inside a single CSV row
        string cleaned = value.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Replace("\"", "\"\"");
        return $"\"{cleaned}\"";
    }

    /// <summary>
    /// Exports full session structure and turns into a structured JSON payload.
    /// Prompts the user to choose a save location on Windows PC by default.
    /// </summary>
    public static string ExportToJSON(NormalizedSessionData data, bool promptUserForPath = true)
    {
        if (data == null)
        {
            Debug.LogWarning("[DataExporter] No session data available to export.");
            return null;
        }

        string defaultName = $"SessionReport_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        string filePath = ResolveSavePath(defaultName, "json", promptUserForPath);
        if (string.IsNullOrEmpty(filePath)) return null; // User cancelled dialog

        string jsonString = JsonUtility.ToJson(data, true);
        File.WriteAllText(filePath, jsonString, Encoding.UTF8);

        Debug.Log($"[DataExporter] Complete JSON exported successfully to: {filePath}");
        return filePath;
    }

    /// <summary>
    /// Generates a printable HTML/PDF report containing all session details, metrics, and dialogue logs.
    /// Prompts the user to choose a save location on Windows PC by default.
    /// </summary>
    public static string ExportToPDFReport(NormalizedSessionData data, bool promptUserForPath = true)
    {
        if (data == null || data.performance_metric == null || data.performance_metric.Count == 0)
        {
            Debug.LogWarning("[DataExporter] No session data available to export.");
            return null;
        }

        string defaultName = $"SessionReport_{DateTime.Now:yyyyMMdd_HHmmss}.html";
        string filePath = ResolveSavePath(defaultName, "html", promptUserForPath);
        if (string.IsNullOrEmpty(filePath)) return null; // User cancelled dialog

        var metric = data.performance_metric[0];
        StringBuilder html = new StringBuilder();

        string sessionStatus = string.IsNullOrEmpty(data.status) ? "Completed" : data.status;
        string statusColor = string.Equals(sessionStatus, "Incomplete", StringComparison.OrdinalIgnoreCase) ? "#ef4444" : "#22c55e";

        html.AppendLine("<!DOCTYPE html><html><head><meta charset='UTF-8'><title>VR Communication Evaluation Report</title><style>");
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
        html.AppendLine("@media print { body { background: #ffffff; color: #0f172a; } .card { background: #f8fafc; box-shadow: none; border: 1px solid #cbd5e1; } th { background: #e2e8f0; color: #0f172a; } }");
        html.AppendLine("</style></head><body>");

        // Header Section
        html.AppendLine("<div class='header'><h1>VR Communication Evaluation Report</h1>");
        html.AppendLine($"<p><b>Date Generated:</b> {DateTime.Now:MMMM dd, yyyy HH:mm} | <b>Session Duration:</b> {data.duration_seconds:F0}s</p></div>");

        // Scenario Metadata Card (Includes Status Badge)
        html.AppendLine("<div class='card'><h2>Scenario Configuration</h2>");
        html.AppendLine($"<p><b>Scenario Type:</b> {EscapeHtml(data.scenario_type)} | <b>Status:</b> <span style='color:{statusColor}; font-weight:bold;'>{EscapeHtml(sessionStatus.ToUpper())}</span></p>");
        html.AppendLine($"<p><b>Topic:</b> {EscapeHtml(data.scenario_topic)} | <b>Difficulty:</b> {EscapeHtml(data.difficulty_level)} | <b>Input Mode:</b> {EscapeHtml(data.input_mode_used)}</p>");
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
        html.AppendLine($"<p>{EscapeHtml(metric.ai_feedback_summary)}</p></div>");

        // Conversation Transcript Card
        html.AppendLine("<div class='card'><h2>Conversation Transcript Log</h2>");
        if (data.dialogue_turn != null && data.dialogue_turn.Count > 0)
        {
            foreach (var turn in data.dialogue_turn)
            {
                html.AppendLine($"<p><span class='user-dialogue'>Turn {turn.turn_sequence} - You:</span> {EscapeHtml(turn.user_transcript)}</p>");
                html.AppendLine($"<p><span class='npc-dialogue'>NPC Response:</span> {EscapeHtml(turn.ai_response_text)}</p>");
                html.AppendLine($"<p style='font-size: 12px; color: #94a3b8;'>[Turn Metrics -> WPM: {turn.speaking_rate_wpm:F0} | Fillers: {turn.filler_word_count} | Silence: {turn.silence_duration_seconds:F1}s]</p><hr style='border: 0.5px solid #334155;'>");
            }
        }
        else
        {
            html.AppendLine("<p>No dialogue turns recorded for this session.</p>");
        }
        html.AppendLine("</div></body></html>");

        File.WriteAllText(filePath, html.ToString(), Encoding.UTF8);

        Debug.Log($"[DataExporter] Complete HTML/PDF report saved to: {filePath}");
        return filePath;
    }

    private static string EscapeHtml(string text)
        => string.IsNullOrEmpty(text) ? "" : WebUtility.HtmlEncode(text);

    public static string ResolveSavePath(string defaultFileName, string extension, bool promptUserForPath = true)
    {
        if (!promptUserForPath)
            return Path.Combine(Application.persistentDataPath, defaultFileName);

#if UNITY_EDITOR
        string editorPath = EditorUtility.SaveFilePanel(
            "Export Session Report",
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Path.GetFileNameWithoutExtension(defaultFileName),
            extension
        );
        return string.IsNullOrEmpty(editorPath) ? null : editorPath;
#elif UNITY_STANDALONE_WIN
        return ShowWindowsSaveDialog(defaultFileName, extension);
#else
        return Path.Combine(Application.persistentDataPath, defaultFileName);
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int flagsEx;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetSaveFileName(ref OpenFileName ofn);

    private static string ShowWindowsSaveDialog(string defaultFileName, string extension)
    {
        OpenFileName ofn = new OpenFileName();
        ofn.lStructSize = Marshal.SizeOf(ofn);
        ofn.lpstrFilter = $"{extension.ToUpper()} Files (*.{extension})\0*.{extension}\0All Files (*.*)\0*.*\0";
        ofn.lpstrFile = defaultFileName.PadRight(260, '\0');
        ofn.nMaxFile = 260;
        ofn.lpstrInitialDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        ofn.lpstrTitle = "Export Session Report";
        ofn.lpstrDefExt = extension;
        ofn.Flags = 0x00000002 | 0x00000800; // OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST

        if (GetSaveFileName(ref ofn))
        {
            int nullIdx = ofn.lpstrFile.IndexOf('\0');
            return nullIdx >= 0 ? ofn.lpstrFile.Substring(0, nullIdx) : ofn.lpstrFile;
        }
        return null;
    }
#endif
}