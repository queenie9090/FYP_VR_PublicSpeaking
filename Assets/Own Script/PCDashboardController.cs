using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Text;

public class PCDashboardController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private SupabaseManager supabaseManager;

    [Header("Empty State UI")]
    [Tooltip("Parent GameObject holding all normal metrics, charts, and content panels")]
    [SerializeField] private GameObject dashboardDataContainer;
    [Tooltip("Overlay Panel shown when no session data is recorded")]
    [SerializeField] private GameObject noDataPanel;
    [SerializeField] private TMP_Text noDataMessageText;
    [SerializeField] private Button noDataReturnHomeButton;

    [Header("Header Metadata")]
    [SerializeField] private TMP_Text reportTitleText;
    [SerializeField] private TMP_Text sessionMetaText;

    [Header("Hero Donut Gauge & Score")]
    [SerializeField] private RadialGaugeChart overallGaugeChart;
    [SerializeField] private TMP_Text scenarioWeightsSubtext;

    [Header("Timeline Chart Reference")]
    [SerializeField] private TimelineChart timelineChart;

    [Header("Speech Clarity Breakdown")]
    [SerializeField] private TMP_Text speechClarityScoreText;
    [SerializeField] private Image speechClarityProgressBar;
    [SerializeField] private TMP_Text wpmText;
    [SerializeField] private TMP_Text fillerWordCountText;
    [SerializeField] private TMP_Text silenceDurationText;

    [Header("Eye Contact Breakdown")]
    [SerializeField] private TMP_Text eyeContactScoreText;
    [SerializeField] private Image eyeContactProgressBar;
    [SerializeField] private TMP_Text eyeContactStatusText; 

    [Header("Gesture Breakdown")]
    [SerializeField] private TMP_Text gestureScoreText;
    [SerializeField] private TMP_Text gpmText;
    [SerializeField] private Image gestureProgressBar;

    [Header("AI Feedback & Conversation Log")]
    [SerializeField] private TMP_Text aiFeedbackText;
    [SerializeField] private TMP_Text conversationLogText;

    [Header("Scroll View Container")]
    [SerializeField] private RectTransform dashboardContentTransform;

    [Header("Export Action Buttons")]
    [SerializeField] private Button exportPdfButton;
    [SerializeField] private Button exportCsvButton;
    [SerializeField] private Button exportJsonButton;

    [Header("Navigation")]
    [SerializeField] private Button backToMenuButton;
    [SerializeField] private Button viewEmotionReportButton;

    private NormalizedSessionData cachedCurrentSession;

    private void Start()
    {
        if (backToMenuButton != null)
        {
            backToMenuButton.onClick.AddListener(() =>
            {
                // If logged in, route directly back to your Main Menu / Dashboard scene
                SceneManager.LoadScene("MainMenu");
            });
        }

        if (noDataReturnHomeButton != null)
        {
            noDataReturnHomeButton.onClick.AddListener(() =>
            {
                SceneManager.LoadScene("MainMenu");
            });
        }

        if (viewEmotionReportButton != null)
        {
            viewEmotionReportButton.onClick.AddListener(() =>
            {
                SceneManager.LoadScene("EmotionDashboardScene");
            });
        }

        if (exportCsvButton != null)
            exportCsvButton.onClick.AddListener(OnExportCSVClicked);

        if (exportJsonButton != null)
            exportJsonButton.onClick.AddListener(OnExportJSONClicked);

        if (exportPdfButton != null)
            exportPdfButton.onClick.AddListener(OnExportPDFClicked);

        FetchAndDisplayLatestSession();
    }

    private void FetchAndDisplayLatestSession()
    {
        // 1. Check if user navigated here from Main Menu History
        if (UserSession.CurrentSessionData != null)
        {
            ShowDashboardContent(true);
            cachedCurrentSession = UserSession.CurrentSessionData;
            PopulateDashboard(cachedCurrentSession);

            // Reset payload so subsequent visits load default behavior
            UserSession.CurrentSessionData = null;
            return;
        }

        // 2. Otherwise, load the latest session from Supabase
        string userId = UserSession.IsLoggedIn ? UserSession.CurrentUserId : "UNAUTHENTICATED_USER";

        StartCoroutine(supabaseManager.GetLatestNormalizedSession(userId, (data) =>
        {
            if (data != null && data.performance_metric != null && data.performance_metric.Count > 0)
            {
                ShowDashboardContent(true);
                cachedCurrentSession = data;
                PopulateDashboard(data);
            }
            else
            {
                ShowEmptyState(); 
            }
        }, (error) =>
        {
            ShowEmptyState("Unable to load session data. Please check your network connection."); // Overrides message dynamically for network issues
            Debug.LogError($"[PCDashboard] Error fetching session: {error}");
        }));
    }

    private void PopulateDashboard(NormalizedSessionData data)
    {
        UserSession.EmotionDashboardSession = data;
        PerformanceMetricRecord metric = data.performance_metric[0];

        // 1. Header Metadata (Fixed data.duration_seconds reference)
        if (reportTitleText != null)
            reportTitleText.text = $"{data.scenario_type} Evaluation Report";

        if (sessionMetaText != null)
        {
            float talkTime = data.duration_seconds > 0 ? data.duration_seconds : 0f;
            string statusStr = string.Equals(data.status, "Incomplete", System.StringComparison.OrdinalIgnoreCase)
            ? "<color=#FF8A65>[Incomplete - Ended Early]</color>"
            : "<color=#81C784>[Completed]</color>";

            string formattedDate = System.DateTime.Now.ToString("MMM dd, yyyy");
            if (System.DateTime.TryParse(data.start_time, out System.DateTime parsedDate))
            {
                formattedDate = parsedDate.ToString("MMM dd, yyyy HH:mm");
            }

            sessionMetaText.text = $"Status: {statusStr} | Topic: {data.scenario_topic} | Difficulty: {data.difficulty_level}\n" +
                                   $"Mode: {data.input_mode_used} | Date: {formattedDate} | Duration: {talkTime:F0}s";
        }

        // 2. Donut Gauge Chart
        string rating = metric.overall_score >= 80.0f ? "Excellent" : (metric.overall_score >= 60.0f ? "Satisfactory" : "Needs Improvement");
        if (overallGaugeChart != null)
        {
            overallGaugeChart.SetScore(metric.overall_score, rating);
        }

        SetScenarioWeightsSubtext(data.scenario_type);

        // 3. Speech Clarity Breakdown
        if (speechClarityScoreText != null) speechClarityScoreText.text = $"{metric.speech_clarity_score:F0}%";
        if (speechClarityProgressBar != null) speechClarityProgressBar.fillAmount = metric.speech_clarity_score / 100.0f;

        string paceBadge = metric.average_wpm < 110f ? "<color=#FF8A65>(Slow - Target: 110-150)</color>" :
                         (metric.average_wpm > 160f ? "<color=#FF8A65>(Fast - Target: 110-150)</color>" : "<color=#81C784>(Optimal Pace)</color>");
        if (wpmText != null) wpmText.text = $"WPM: {metric.average_wpm:F0} {paceBadge}";

        if (fillerWordCountText != null) fillerWordCountText.text = $"FWC: {metric.filler_word_count}";
        if (silenceDurationText != null) silenceDurationText.text = $"Silence: {metric.total_silence_seconds:F1}s";

        // 4. Eye Contact Breakdown
        if (eyeContactScoreText != null) eyeContactScoreText.text = $"{metric.eye_contact_pct:F0}%";
        if (eyeContactProgressBar != null) eyeContactProgressBar.fillAmount = metric.eye_contact_pct / 100.0f;

        if (eyeContactStatusText != null)
        {
            eyeContactStatusText.text = metric.eye_contact_pct >= 70.0f
                ? "<color=#81C784>(Target Reached >= 70%)</color>"
                : "<color=#FF8A65>(Below Target < 70%)</color>";
        }

        // 5. Gesture Breakdown
        string gestureBadge = metric.gestures_per_minute >= 3.0f ? "<color=#81C784>(Active Expression)</color>" : "<color=#FF8A65>(Low Expression)</color>";
        if (gestureScoreText != null) gestureScoreText.text = $"{metric.hand_gesture_score:F0}%";
        if (gpmText != null) gpmText.text = $"Rate: {metric.gestures_per_minute:F1} GPM {gestureBadge}";
        if (gestureProgressBar != null) gestureProgressBar.fillAmount = metric.hand_gesture_score / 100.0f;

        // 6. Timeline Chart
        if (timelineChart != null && data.dialogue_turn != null && data.dialogue_turn.Count > 0)
        {
            List<float> turnScores = new List<float>();
            foreach (var turn in data.dialogue_turn)
            {
                int turnWords = string.IsNullOrEmpty(turn.user_transcript) ? 0 : turn.user_transcript.Split(new[] { ' ', '\t', '\n' }, System.StringSplitOptions.RemoveEmptyEntries).Length;
                float speakingDurationSec = turn.speaking_rate_wpm > 0 ? (turnWords / turn.speaking_rate_wpm) * 60.0f : 1.0f;

                var (turnClarity, _) = MetricsCalculator.CalculateSpeechClarity(
                    turn.filler_word_count,
                    speakingDurationSec,
                    turn.speaking_rate_wpm
                );
                turnScores.Add(turnClarity);
            }
            timelineChart.PopulateTimeline(turnScores);
        }

        // 7. AI Feedback Summary
        if (aiFeedbackText != null)
        {
            aiFeedbackText.text = string.IsNullOrEmpty(metric.ai_feedback_summary)
                ? GenerateAIFeedback(metric)
                : metric.ai_feedback_summary;
        }

        // 8. Dialogue Log
        PopulateConversationLog(data.dialogue_turn);

        // 9. Layout Rebuild
        RefreshScrollViewLayout(dashboardContentTransform);
    }

    private void SetScenarioWeightsSubtext(string scenario)
    {
        if (scenarioWeightsSubtext == null) return;

        string cleanScenario = string.IsNullOrEmpty(scenario) ? "" : scenario.Replace("Scene", "").Trim();

        switch (cleanScenario)
        {
            case "Hall":
                scenarioWeightsSubtext.text = "Weights: Clarity (40%)\nEye Contact (20%)\nGestures (40%)";
                break;
            case "Classroom":
                scenarioWeightsSubtext.text = "Weights: Clarity (40%)\nEye Contact (40%)\nGestures (20%)";
                break;
            case "Cafeteria":
            case "Interview":
            case "Normal Conversation":
            default:
                scenarioWeightsSubtext.text = "Weights: Clarity (50%)\nEye Contact (30%)\nGestures (20%)";
            break;
        }
    }

    private string GenerateAIFeedback(PerformanceMetricRecord metric)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<b>Performance Highlights:</b>");

        if (metric.speech_clarity_score >= 80.0f)
            sb.AppendLine("• <color=#81C784>Speech:</color> Excellent articulation with minimal disfluency.");
        else
            sb.AppendLine("• <color=#FF8A65>Speech:</color> Work on replacing filler words ('um', 'uh') with intentional pauses.");

        if (metric.eye_contact_pct >= 70.0f)
            sb.AppendLine("• <color=#81C784>Gaze:</color> Maintained strong visual engagement with the evaluator.");
        else
            sb.AppendLine("• <color=#FF8A65>Gaze:</color> Practice keeping direct eye gaze on the speaker's face.");

        if (metric.gestures_per_minute >= 3.0f)
            sb.AppendLine("• <color=#81C784>Gestures:</color> Dynamic hand expressions reinforced key dialogue points.");
        else
            sb.AppendLine("• <color=#FF8A65>Gestures:</color> Incorporate more open hand gestures to improve non-verbal engagement.");

        return sb.ToString();
    }

    private void PopulateConversationLog(List<DialogueTurnRecord> turns)
    {
        if (turns == null || conversationLogText == null) return;

        conversationLogText.text = "";
        turns.Sort((a, b) => a.turn_sequence.CompareTo(b.turn_sequence));

        StringBuilder sb = new StringBuilder();

        foreach (var turn in turns)
        {
            if (!string.IsNullOrEmpty(turn.user_transcript))
            {
                sb.AppendLine($"<color=#4FC3F7><b>You:</b></color> {turn.user_transcript}\n");
            }

            if (!string.IsNullOrEmpty(turn.ai_response_text))
            {
                sb.AppendLine($"<color=#AED581><b>NPC:</b></color> {turn.ai_response_text}\n");
            }
        }

        conversationLogText.text = sb.ToString();
    }

    private void RefreshScrollViewLayout(RectTransform contentRect)
    {
        if (contentRect != null)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        }
    }

    // --- EXPORT HANDLERS ---
    private void OnExportCSVClicked()
    {
        if (cachedCurrentSession != null)
            DataExporter.ExportToCSV(cachedCurrentSession);
    }

    private void OnExportJSONClicked()
    {
        if (cachedCurrentSession != null)
            DataExporter.ExportToJSON(cachedCurrentSession);
    }

    private void OnExportPDFClicked()
    {
        if (cachedCurrentSession != null)
            DataExporter.ExportToPDFReport(cachedCurrentSession);
    }

    private void ShowDashboardContent(bool hasData)
    {
        if (dashboardDataContainer != null) dashboardDataContainer.SetActive(hasData);
        if (noDataPanel != null) noDataPanel.SetActive(!hasData);
    }

    private void ShowEmptyState(string customMessage = null)
    {
        ShowDashboardContent(false);
        // Only overrides text if a custom message is passed and the component is assigned
        if (noDataMessageText != null && !string.IsNullOrEmpty(customMessage))
        {
            noDataMessageText.text = customMessage;
        }
    }
}