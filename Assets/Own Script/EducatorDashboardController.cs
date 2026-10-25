using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EducatorDashboardController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private SupabaseManager supabaseManager;

    [Header("Cohort Table Setup")]
    [SerializeField] private Transform cohortTableContent; // Viewport/Content RectTransform
    [SerializeField] private GameObject cohortRowPrefab;   // Prefab with EducatorCohortRowUI
    [SerializeField] private TMP_InputField searchInputField;

    [Header("Selected Record Inspector")]
    [SerializeField] private TMP_Text selectedRecordDetailsText; // Left side: Sensor metrics
    [SerializeField] private TMP_Text aiFeedbackText;            // Right side: AI Performance Feedback

    [Header("Export Action Buttons")]
    [SerializeField] private Button exportPdfButton;
    [SerializeField] private Button exportCsvButton;
    [SerializeField] private Button exportJsonButton;

    private List<NormalizedSessionData> allCohortSessions = new List<NormalizedSessionData>();
    private NormalizedSessionData currentlyInspectedSession;

    private void Start()
    {
        if (searchInputField != null)
            searchInputField.onValueChanged.AddListener(FilterCohortTable);

        if (exportPdfButton != null)
            exportPdfButton.onClick.AddListener(OnExportPdfClicked);

        if (exportCsvButton != null)
            exportCsvButton.onClick.AddListener(OnExportCsvClicked);

        if (exportJsonButton != null)
            exportJsonButton.onClick.AddListener(OnExportJsonClicked);

        FetchAllParticipantData();
    }

    private void FetchAllParticipantData()
    {
        if (supabaseManager == null)
        {
            Debug.LogError("[EducatorDashboard] SupabaseManager reference is missing!");
            return;
        }

        StartCoroutine(supabaseManager.GetAllCohortNormalizedSessions((sessions) =>
        {
            allCohortSessions = sessions;
            PopulateCohortTable(allCohortSessions);
        }, (error) =>
        {
            Debug.LogError($"[EducatorDashboard] Error fetching cohort data: {error}");
            if (selectedRecordDetailsText != null)
                selectedRecordDetailsText.text = "<color=#FF8A65><b>Failed to load cohort data.</b>\nPlease check network connection.</color>";
        }));
    }

    private void PopulateCohortTable(List<NormalizedSessionData> sessions)
    {
        foreach (Transform child in cohortTableContent)
        {
            Destroy(child.gameObject);
        }

        if (sessions == null || sessions.Count == 0)
        {
            if (selectedRecordDetailsText != null)
                selectedRecordDetailsText.text = "<b>PARTICIPANT PERFORMANCE DETAILS</b>\n\n<i>No cohort session records found.</i>";

            if (aiFeedbackText != null)
                aiFeedbackText.text = "<i>No feedback available.</i>";
            return;
        }

        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            GameObject rowGO = Instantiate(cohortRowPrefab, cohortTableContent);

            EducatorCohortRowUI rowUI = rowGO.GetComponent<EducatorCohortRowUI>();
            if (rowUI != null)
            {
                rowUI.Setup(session, OnParticipantSelectedForInspection);
            }
        }

        Canvas.ForceUpdateCanvases();
        if (cohortTableContent is RectTransform contentRect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        }

        // Auto-select the first record
        OnParticipantSelectedForInspection(sessions[0]);
    }

    private void FilterCohortTable(string searchQuery)
    {
        if (string.IsNullOrEmpty(searchQuery))
        {
            PopulateCohortTable(allCohortSessions);
            return;
        }

        string cleanQuery = searchQuery.ToLower().Trim();
        List<NormalizedSessionData> filtered = allCohortSessions.FindAll(s =>
            (s.user_id != null && s.user_id.ToLower().Contains(cleanQuery)) ||
            (s.scenario_type != null && s.scenario_type.ToLower().Contains(cleanQuery)) ||
            (s.scenario_topic != null && s.scenario_topic.ToLower().Contains(cleanQuery))
        );

        PopulateCohortTable(filtered);
    }

    private void OnParticipantSelectedForInspection(NormalizedSessionData session)
    {
        currentlyInspectedSession = session;

        string participantShortId = !string.IsNullOrEmpty(session.user_id) && session.user_id.Length >= 6
            ? "P_" + session.user_id.Substring(0, 6)
            : session.user_id;

        string dateStr = session.start_time;
        if (System.DateTime.TryParse(session.start_time, out System.DateTime parsed))
        {
            dateStr = parsed.ToString("yyyy-MM-dd");
        }

        var metric = (session.performance_metric != null && session.performance_metric.Count > 0)
            ? session.performance_metric[0]
            : null;

        float eyeContact = metric != null ? metric.eye_contact_pct : 0f;
        float wpm = metric != null ? metric.average_wpm : 0f;
        int fillers = metric != null ? metric.filler_word_count : 0;
        string paceEvaluation = wpm < 110 ? "Slow" : (wpm <= 160 ? "Optimal Pace" : "Fast");

        // 1. Render Left Panel: Sensor Details
        if (selectedRecordDetailsText != null)
        {
            selectedRecordDetailsText.text =
                $"<b>• Visual Engagement:</b> {eyeContact:F1}% Eye Contact with AI Avatar\n" +
                $"<b>• Speaking Rate:</b> {wpm:F0} WPM ({paceEvaluation})\n" +
                $"<b>• Communication Hesitancies:</b> {fillers} Filler Words Used (\"um\", \"like\")\n" +
                $"<b>• Associated Log Record:</b> Log_{participantShortId}_{parsed:yyyyMMdd}.json";
        }

        // 2. Render Right Panel: AI Feedback Summary
        if (aiFeedbackText != null)
        {
            string summary = (metric != null && !string.IsNullOrEmpty(metric.ai_feedback_summary))
                ? metric.ai_feedback_summary
                : "No qualitative AI feedback summary recorded for this session.";

            aiFeedbackText.text =
                $"<i>\"{summary}\"</i>";
        }
    }

    private void OnExportPdfClicked()
    {
        if (currentlyInspectedSession != null)
        {
            string path = DataExporter.ExportToPDFReport(currentlyInspectedSession);
            Debug.Log($"[EducatorDashboard] PDF exported: {path}");
        }
    }

    private void OnExportCsvClicked()
    {
        if (allCohortSessions == null || allCohortSessions.Count == 0) return;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("ParticipantID,Date,Scenario,DurationSeconds,OverallScore,EyeContactPct,WPM,FillerWords");

        foreach (var s in allCohortSessions)
        {
            var m = (s.performance_metric != null && s.performance_metric.Count > 0) ? s.performance_metric[0] : null;
            sb.AppendLine($"\"{s.user_id}\",\"{s.start_time}\",\"{s.scenario_type}\",{s.duration_seconds:F1},{(m != null ? m.overall_score : 0)},{(m != null ? m.eye_contact_pct : 0)},{(m != null ? m.average_wpm : 0)},{(m != null ? m.filler_word_count : 0)}");
        }

        string path = System.IO.Path.Combine(Application.persistentDataPath, $"CohortDataset_{System.DateTime.Now:yyyyMMdd}.csv");
        System.IO.File.WriteAllText(path, sb.ToString());
        Debug.Log($"[EducatorDashboard] CSV exported: {path}");
    }

    private void OnExportJsonClicked()
    {
        if (currentlyInspectedSession != null)
        {
            DataExporter.ExportToJSON(currentlyInspectedSession);
        }
    }
}