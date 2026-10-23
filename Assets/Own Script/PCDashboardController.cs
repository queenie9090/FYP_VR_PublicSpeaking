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
    [SerializeField] private TMP_Text silenceDurationText; // Displays total silence time

    [Header("Eye Contact Breakdown")]
    [SerializeField] private TMP_Text eyeContactScoreText;
    [SerializeField] private Image eyeContactProgressBar;

    [Header("Gesture Breakdown")]
    [SerializeField] private TMP_Text gestureScoreText;
    [SerializeField] private TMP_Text gpmText;
    [SerializeField] private Image gestureProgressBar;

    [Header("AI Feedback & Conversation Log")]
    [SerializeField] private TMP_Text aiFeedbackText;
    [SerializeField] private TMP_Text conversationLogText;

    [Header("Scroll View Layout Containers")]
    [SerializeField] private RectTransform mainDashboardContentTransform; // Content Transform of the Main ScrollView
    [SerializeField] private RectTransform conversationLogContentTransform; // Content Transform of the Log ScrollView

    [Header("Navigation")]
    [SerializeField] private Button backToMenuButton;

    private void Start()
    {
        if (backToMenuButton != null)
            backToMenuButton.onClick.AddListener(() => SceneManager.LoadScene("MainMenu"));

        FetchAndDisplayLatestSession();
    }

    private void FetchAndDisplayLatestSession()
    {
        string userId = UserSession.IsLoggedIn ? UserSession.CurrentUserId : "UNAUTHENTICATED_USER";

        StartCoroutine(supabaseManager.GetLatestNormalizedSession(userId, (data) =>
        {
            if (data != null && data.performance_metric != null && data.performance_metric.Count > 0)
            {
                PopulateDashboard(data);
            }
            else
            {
                Debug.LogWarning("[PCDashboard] No normalized session record found.");
            }
        }, (error) =>
        {
            Debug.LogError($"[PCDashboard] Error fetching session: {error}");
        }));
    }

    private void PopulateDashboard(NormalizedSessionData data)
    {
        PerformanceMetricRecord metric = data.performance_metric[0];

        // 1. Header Metadata
        if (reportTitleText != null)
            reportTitleText.text = $"{data.scenario_type} Evaluation Report";

        if (sessionMetaText != null)
            sessionMetaText.text = $"Topic: {data.scenario_topic} | Difficulty: {data.difficulty_level} | Mode: {data.input_mode_used}";

        // 2. Animate Hero Donut Gauge Chart
        string rating = metric.overall_score >= 80.0f ? "Excellent" : (metric.overall_score >= 60.0f ? "Satisfactory" : "Needs Improvement");
        if (overallGaugeChart != null)
        {
            overallGaugeChart.SetScore(metric.overall_score, rating);
        }

        SetScenarioWeightsSubtext(data.scenario_type);

        // 3. Populate Metric Progress Bars
        if (speechClarityScoreText != null) speechClarityScoreText.text = $"{metric.speech_clarity_score:F0}%";
        if (speechClarityProgressBar != null) speechClarityProgressBar.fillAmount = metric.speech_clarity_score / 100.0f;
        if (wpmText != null) wpmText.text = $"WPM: {metric.average_wpm:F0}";
        if (fillerWordCountText != null) fillerWordCountText.text = $"FWC: {metric.filler_word_count}";
        if (silenceDurationText != null) silenceDurationText.text = $"Silence: {metric.total_silence_seconds:F1}s";

        if (eyeContactScoreText != null) eyeContactScoreText.text = $"{metric.eye_contact_pct:F0}%";
        if (eyeContactProgressBar != null) eyeContactProgressBar.fillAmount = metric.eye_contact_pct / 100.0f;

        if (gestureScoreText != null) gestureScoreText.text = $"{metric.hand_gesture_score:F0}%";
        if (gpmText != null) gpmText.text = $"Rate: {metric.gestures_per_minute:F1} GPM";
        if (gestureProgressBar != null) gestureProgressBar.fillAmount = metric.hand_gesture_score / 100.0f;

        // 4. Populate Timeline Chart (Using precise turn duration)
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

        // 5. Populate AI Feedback Highlights
        if (aiFeedbackText != null)
        {
            aiFeedbackText.text = string.IsNullOrEmpty(metric.ai_feedback_summary)
                ? GenerateAIFeedback(metric)
                : metric.ai_feedback_summary;
        }

        // 6. Populate Dialogue Log UI
        PopulateConversationLog(data.dialogue_turn);

        // 7. Force ScrollView Layout Rebuilds
        RefreshScrollViewLayout(conversationLogContentTransform);
        RefreshScrollViewLayout(mainDashboardContentTransform);
    }

    private void SetScenarioWeightsSubtext(string scenario)
    {
        if (scenarioWeightsSubtext == null) return;

        switch (scenario)
        {
            case "Hall":
            case "Stage Venue":
            case "StageVenue":
                scenarioWeightsSubtext.text = "Weights: Clarity (40%)\nEye Contact (20%)\nGestures (40%)";
                break;
            case "Classroom":
                scenarioWeightsSubtext.text = "Weights: Clarity (40%)\nEye Contact (40%)\nGestures (20%)";
                break;
            case "Cafeteria":
            case "Interview":
            default:
                scenarioWeightsSubtext.text = "Weights: Clarity (50%)\nEye Contact (30%)\nGestures (20%)";
                break;
        }
    }

    private string GenerateAIFeedback(PerformanceMetricRecord metric)
    {
        string feedback = $"<b>Performance Highlights:</b>\n";

        if (metric.speech_clarity_score >= 80.0f)
            feedback += "• Excellent spoken articulation with minimal disfluency.\n";
        else
            feedback += "• Work on reducing filler words ('um', 'uh') during speech pauses.\n";

        if (metric.eye_contact_pct >= 70.0f)
            feedback += "• Maintained strong visual engagement with the evaluator.\n";
        else
            feedback += "• Practice keeping direct eye gaze on the speaker's face.\n";

        if (metric.gestures_per_minute >= 3.0f)
            feedback += "• Dynamic non-verbal hand expressions reinforced key dialogue points.";
        else
            feedback += "• Incorporate more open hand gestures to improve non-verbal engagement.";

        return feedback;
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
}