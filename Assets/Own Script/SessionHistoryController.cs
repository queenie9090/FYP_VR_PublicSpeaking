using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class SessionHistoryController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private SupabaseManager supabaseManager;

    [Header("Left Panel: Scroll View Container")]
    [SerializeField] private Transform scrollContentTransform; // Content RectTransform of ScrollView
    [SerializeField] private GameObject sessionHistoryCardPrefab; // SessionHistoryCard Prefab

    [Header("Loading Indicator")]
    [SerializeField] private GameObject loadingIndicator; // Spinner or Loading Text Panel

    [Header("Right Panel: Combined Session Details")]
    [SerializeField] private TMP_Text selectedSessionDetailsText; // Single combined text field for details

    [Header("Action Buttons")]
    [SerializeField] private Button viewDetailedAnalyticsButton;
    [SerializeField] private Button exportPdfReportButton;

    private NormalizedSessionData selectedSessionData;

    private void OnEnable()
    {
        // Fetch history when history panel opens/activates
        FetchUserSessionHistory();
    }

    private void Start()
    {
        if (viewDetailedAnalyticsButton != null)
            viewDetailedAnalyticsButton.onClick.AddListener(OnViewDetailedAnalyticsClicked);

        if (exportPdfReportButton != null)
            exportPdfReportButton.onClick.AddListener(OnExportPdfClicked);
    }

    private void FetchUserSessionHistory()
    {
        string userId = UserSession.IsLoggedIn ? UserSession.CurrentUserId : "UNAUTHENTICATED_USER";

        if (supabaseManager == null)
        {
            Debug.LogError("[SessionHistory] SupabaseManager reference missing!");
            return;
        }

        // 1. Show loading indicator and reset right panel text
        if (loadingIndicator != null)
            loadingIndicator.SetActive(true);

        if (selectedSessionDetailsText != null)
            selectedSessionDetailsText.text = "<color=#94A3B8><i>Loading session details...</i></color>";

        // 2. Clear previous card list
        foreach (Transform child in scrollContentTransform)
        {
            Destroy(child.gameObject);
        }

        // 3. Fetch data asynchronously from Supabase
        StartCoroutine(supabaseManager.GetAllUserNormalizedSessions(userId, (sessions) =>
        {
            // Hide loading indicator upon success
            if (loadingIndicator != null)
                loadingIndicator.SetActive(false);

            PopulateHistoryList(sessions);
        }, (error) =>
        {
            // Hide loading indicator on error and inform user
            if (loadingIndicator != null)
                loadingIndicator.SetActive(false);

            Debug.LogError($"[SessionHistory] Error fetching session list: {error}");

            if (selectedSessionDetailsText != null)
                selectedSessionDetailsText.text = "<color=#FF8A65><b>Failed to load session history.</b>\nPlease check your internet connection.</color>";
        }));
    }

    private void PopulateHistoryList(List<NormalizedSessionData> sessions)
    {
        if (sessions == null || sessions.Count == 0)
        {
            ClearSelectedDetails();
            return;
        }

        // Instantiate card items for each session
        for (int i = 0; i < sessions.Count; i++)
        {
            var data = sessions[i];
            GameObject cardGO = Instantiate(sessionHistoryCardPrefab, scrollContentTransform);

            SessionHistoryItemUI itemUI = cardGO.GetComponent<SessionHistoryItemUI>();
            if (itemUI != null)
            {
                itemUI.Setup(i + 1, data, OnSessionCardSelected);
            }
        }

        // Force scroll view layout recalculation
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContentTransform as RectTransform);

        // Auto-select the most recent session
        OnSessionCardSelected(sessions[0]);
    }

    private void OnSessionCardSelected(NormalizedSessionData data)
    {
        selectedSessionData = data;

        if (selectedSessionDetailsText != null)
        {
            string shortSessionId = !string.IsNullOrEmpty(data.session_id) && data.session_id.Length >= 8
                ? data.session_id.Substring(0, 8) + "..."
                : data.session_id;

            string formattedDateTime = data.start_time;
            if (System.DateTime.TryParse(data.start_time, out System.DateTime parsedDate))
            {
                formattedDateTime = parsedDate.ToString("MMM dd, yyyy · HH:mm");
            }

            selectedSessionDetailsText.text =
                $"<color=#94A3B8>Session ID :</color> <color=#FFFFFF><b>{shortSessionId}</b></color>\n" +
                $"<color=#94A3B8>Date & Time :</color> <color=#FFFFFF><b>{formattedDateTime}</b></color>\n" +
                $"<color=#94A3B8>Topic / Activity :</color> <color=#4FC3F7><b>{data.scenario_topic}</b></color>\n" +
                $"<color=#94A3B8>Difficulty Level :</color> <color=#FFFFFF><b>{data.difficulty_level}</b></color>\n" +
                $"<color=#94A3B8>Session Length :</color> <color=#81C784><b>{data.duration_seconds:F0} seconds</b></color>";
        }
    }

    private void ClearSelectedDetails()
    {
        selectedSessionData = null;
        if (selectedSessionDetailsText != null)
        {
            selectedSessionDetailsText.text =
                "<b>SELECTED SESSION DETAILS</b>\n\n" +
                "<i>No historical session records found.</i>";
        }
    }

    private void OnViewDetailedAnalyticsClicked()
    {
        if (selectedSessionData != null)
        {
            UserSession.CurrentSessionData = selectedSessionData;
            SceneManager.LoadScene("DashboardScene");
        }
    }

    private void OnExportPdfClicked()
    {
        if (selectedSessionData != null)
        {
            string path = DataExporter.ExportToPDFReport(selectedSessionData);
            Debug.Log($"[SessionHistory] Report exported to: {path}");
        }
    }
}