
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class EmotionDisplayBinding
{
    public string emotionName;
    public Sprite emotionSprite;
    public Image donutSlice;
    public Image legendIcon;
    public TMP_Text legendValueText;
}

public class EmotionDashboardUIController : MonoBehaviour
{
    [Header("Final Emotion Panel")]
    [SerializeField] private Image finalEmotionIcon;
    [SerializeField] private TMP_Text finalEmotionLabel;
    [SerializeField] private TMP_Text intensityText;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private Image[] intensityBars;

    [Header("Emotion Timeline")]
    [SerializeField] private Transform timelineItemsContainer;
    [SerializeField] private GameObject emotionItemPrefab;

    [Header("Emotion Distribution")]
    [SerializeField] private EmotionDisplayBinding[] emotions;
    [SerializeField] private TMP_Text totalTurnsText;

    [Header("Optional Session Information")]
    [SerializeField] private TMP_Text sessionTotalTurnsText;
    [SerializeField] private TMP_Text sessionIdText;
    [SerializeField] private TMP_Text scenarioText;
    [SerializeField] private TMP_Text startTimeText;
    [SerializeField] private TMP_Text durationText;
    [SerializeField] private TMP_Text completionStatusText;
    [SerializeField] private TMP_Text eyeSummaryText;
    [SerializeField] private TMP_Text faceSummaryText;
    [SerializeField] private TMP_Text bodySummaryText;
    [SerializeField] private TMP_Text voiceSummaryText;
    [SerializeField] private TMP_Text lipSyncSummaryText;
    [SerializeField] private SupabaseManager supabaseManager;

    [Header("Bar Colors")]
    [SerializeField]
    private Color activeBarColor =
        new Color(0.643f, 0.549f, 1f, 1f);

    [SerializeField]
    private Color inactiveBarColor =
        new Color(0.337f, 0.380f, 0.494f, 1f);

    private readonly string[] emotionOrder =
    {
        "HAPPY",
        "SAD",
        "ANGRY",
        "NERVOUS",
        "THINKING",
        "NEUTRAL"
    };

    private void Start()
    {
        RefreshDashboard();
    }

    public void RefreshDashboard()
    {
        var selectedSession = UserSession.EmotionDashboardSession;
        if (selectedSession != null)
        {
            UpdateSessionInfo(selectedSession);
            UpdateBehaviourSummary(null);
            foreach (var label in new[] { eyeSummaryText, faceSummaryText, bodySummaryText, voiceSummaryText, lipSyncSummaryText })
                if (label != null) label.text = "Loading...";
            if (supabaseManager == null) supabaseManager = FindFirstObjectByType<SupabaseManager>();
            if (supabaseManager == null) supabaseManager = gameObject.AddComponent<SupabaseManager>();
            string sessionId = selectedSession.session_id;
            StartCoroutine(supabaseManager.GetSessionBehaviourSummary(sessionId, result =>
            {
                if (this == null || UserSession.EmotionDashboardSession?.session_id != sessionId) return;
                SetSummary(eyeSummaryText, result, "eye_behaviour_event");
                SetSummary(faceSummaryText, result, "facial_expression_event");
                SetSummary(bodySummaryText, result, "body_animation_event");
                SetSummary(voiceSummaryText, result, "voice_style");
                if (lipSyncSummaryText != null)
                    lipSyncSummaryText.text = result.TryGetValue("voice_event", out var tts) && !string.IsNullOrWhiteSpace(tts)
                        ? "TTS: " + tts + " | Lip sync: Not Recorded" : "Not Recorded";
            }, error =>
            {
                Debug.LogError("[Emotion Dashboard] " + error);
                if (this == null || UserSession.EmotionDashboardSession?.session_id != sessionId) return;
                foreach (var label in new[] { eyeSummaryText, faceSummaryText, bodySummaryText, voiceSummaryText, lipSyncSummaryText })
                    if (label != null) label.text = "Unavailable";
            }));
            var savedHistory = new List<NPCEmotionRecord>();
            foreach (var turn in (selectedSession.dialogue_turn ?? new List<DialogueTurnRecord>())
                .OrderBy(t => t.turn_sequence))
            {
                foreach (var item in (turn.npc_emotion_log ?? new List<NPCEmotionLogData>())
                    .OrderBy(e => e.recorded_at))
                {
                    if (!emotionOrder.Contains(item.current_emotion)) continue;
                    savedHistory.Add(new NPCEmotionRecord
                    {
                        turnNumber = turn.turn_sequence,
                        npcName = item.npc_name,
                        emotion = item.current_emotion,
                        intensity = item.emotion_intensity,
                        recordedAt = item.recorded_at
                    });
                }
            }
            UpdateFinalEmotion(savedHistory.LastOrDefault());
            UpdateTimeline(savedHistory);
            UpdateDistribution(savedHistory, savedHistory.Count);
            if (sessionTotalTurnsText != null)
                sessionTotalTurnsText.text = (selectedSession.dialogue_turn?.Count ?? 0).ToString();
            return;
        }
        NPCEmotionSessionManager manager =
            NPCEmotionSessionManager.Instance;
        UpdateSessionInfo(null);
        UpdateBehaviourSummary(manager);

        if (manager == null)
        {
            Debug.LogWarning(
                "[Emotion Dashboard] Session Manager not found."
            );

            ShowEmptyDashboard();
            return;
        }

        IReadOnlyList<NPCEmotionRecord> history =
            manager.EmotionHistory;

        int total = manager.TotalEmotionTurns;

        UpdateFinalEmotion(manager.GetFinalEmotion());
        UpdateTimeline(history);
        UpdateDistribution(history, total);

        if (sessionTotalTurnsText != null)
            sessionTotalTurnsText.text = "Not Available";

        Debug.Log(
            $"[Emotion Dashboard] Loaded {total} emotion turns."
        );
    }

    private void ShowEmptyDashboard()
    {
        UpdateFinalEmotion(null);

        UpdateTimeline(new List<NPCEmotionRecord>());
        UpdateDistribution(new List<NPCEmotionRecord>(), 0);

        if (sessionTotalTurnsText != null)
            sessionTotalTurnsText.text = "Not Available";
    }

    private void UpdateFinalEmotion(NPCEmotionRecord record)
    {
        if (record == null)
        {
            if (finalEmotionLabel != null)
                finalEmotionLabel.text = "NO DATA";

            if (intensityText != null)
                intensityText.text = "Intensity: --";

            if (levelText != null)
                levelText.text = "Level: Not Recorded";

            if (finalEmotionIcon != null)
            {
                finalEmotionIcon.sprite = null;
                finalEmotionIcon.enabled = false;
            }

            UpdateIntensityBars(0);
            return;
        }

        string emotion = record.emotion;
        int intensity = Mathf.Clamp(record.intensity, 0, 100);

        if (finalEmotionLabel != null)
            finalEmotionLabel.text = emotion;

        if (intensityText != null)
            intensityText.text = $"Intensity: {intensity}%";

        if (levelText != null)
            levelText.text = $"Level: {GetIntensityLevel(intensity)}";

        EmotionDisplayBinding binding = FindBinding(emotion);

        if (finalEmotionIcon != null)
        {
            finalEmotionIcon.sprite =
                binding != null ? binding.emotionSprite : null;

            finalEmotionIcon.enabled =
                finalEmotionIcon.sprite != null;
        }

        UpdateIntensityBars(intensity);
    }

    private string GetIntensityLevel(int intensity)
    {
        if (intensity <= 0) return "None";
        if (intensity <= 20) return "Very Low";
        if (intensity <= 40) return "Low";
        if (intensity <= 60) return "Moderate";
        if (intensity <= 80) return "High";
        return "Very High";
    }

    private void UpdateIntensityBars(int intensity)
    {
        if (intensityBars == null)
            return;

        int activeCount = Mathf.CeilToInt(
            Mathf.Clamp(intensity, 0, 100) / 20f
        );

        if (intensity <= 0)
            activeCount = 0;

        for (int i = 0; i < intensityBars.Length; i++)
        {
            if (intensityBars[i] == null)
                continue;

            intensityBars[i].color =
                i < activeCount
                    ? activeBarColor
                    : inactiveBarColor;
        }
    }

    private void UpdateTimeline(
        IReadOnlyList<NPCEmotionRecord> history)
    {
        if (timelineItemsContainer == null ||
            emotionItemPrefab == null)
            return;

        // Remove previously generated items.
        for (int i = timelineItemsContainer.childCount - 1;
             i >= 0; i--)
        {
            Destroy(timelineItemsContainer.GetChild(i).gameObject);
        }

        foreach (NPCEmotionRecord record in history)
        {
            GameObject item = Instantiate(
                emotionItemPrefab,
                timelineItemsContainer
            );

            item.SetActive(true);

            Transform iconTransform =
                item.transform.Find("EmotionIcon");

            Transform labelTransform =
                item.transform.Find("EmotionLabel");

            Transform turnTransform =
                item.transform.Find("TurnLabel");

            EmotionDisplayBinding binding =
                FindBinding(record.emotion);

            if (iconTransform != null)
            {
                Image icon =
                    iconTransform.GetComponent<Image>();

                if (icon != null)
                {
                    icon.sprite =
                        binding != null
                            ? binding.emotionSprite
                            : null;

                    icon.enabled = icon.sprite != null;
                }
            }

            if (labelTransform != null)
            {
                TMP_Text label =
                    labelTransform.GetComponent<TMP_Text>();

                if (label != null)
                    label.text = record.emotion;
            }

            if (turnTransform != null)
            {
                TMP_Text turnLabel =
                    turnTransform.GetComponent<TMP_Text>();

                if (turnLabel != null)
                    turnLabel.text = string.IsNullOrWhiteSpace(record.npcName)
                        ? $"T{record.turnNumber}" : $"T{record.turnNumber} · {record.npcName}";
            }
        }
    }

    private void UpdateDistribution(
        IReadOnlyList<NPCEmotionRecord> history,
        int total)
    {
        if (totalTurnsText != null)
            totalTurnsText.text = $"{total}\nEmotions";

        // Start at the top of the circle.
        float cumulativeFraction = 0f;

        foreach (string emotionName in emotionOrder)
        {
            EmotionDisplayBinding binding =
                FindBinding(emotionName);

            if (binding == null)
                continue;

            int count = 0;

            foreach (NPCEmotionRecord record in history)
            {
                if (string.Equals(
                    record.emotion,
                    emotionName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            float fraction =
                total > 0 ? (float)count / total : 0f;

            float percentage = fraction * 100f;

            if (binding.legendValueText != null)
            {
                binding.legendValueText.text =
                    $"{percentage:0.#}% ({count})";
            }

            if (binding.legendIcon != null)
            {
                binding.legendIcon.sprite =
                    binding.emotionSprite;
            }

            if (binding.donutSlice != null)
            {
                Image slice = binding.donutSlice;

                slice.type = Image.Type.Filled;
                slice.fillMethod = Image.FillMethod.Radial360;
                slice.fillOrigin = (int)Image.Origin360.Top;
                slice.fillClockwise = true;
                slice.fillAmount = fraction;

                // Rotate each segment to the end
                // of the previous segment.
                slice.rectTransform.localEulerAngles =
                    new Vector3(
                        0f,
                        0f,
                        -360f * cumulativeFraction
                    );

                slice.enabled = count > 0;
            }

            cumulativeFraction += fraction;
        }
    }

    private EmotionDisplayBinding FindBinding(string emotion)
    {
        if (emotions == null)
            return null;

        foreach (EmotionDisplayBinding binding in emotions)
        {
            if (binding != null &&
                string.Equals(
                    binding.emotionName,
                    emotion,
                    StringComparison.OrdinalIgnoreCase))
            {
                return binding;
            }
        }

        return null;
    }

    private void UpdateSessionInfo(NormalizedSessionData session)
    {
        if (sessionIdText != null) sessionIdText.text = Available(session?.session_id);
        if (scenarioText != null) scenarioText.text = Available(session?.scenario_type);
        if (startTimeText != null) startTimeText.text = Available(session?.start_time);
        if (durationText != null) durationText.text = session != null
            ? TimeSpan.FromSeconds(Math.Max(0, session.duration_seconds)).ToString(@"hh\:mm\:ss") : "Not Available";
        if (completionStatusText != null) completionStatusText.text = Available(session?.status);
    }

    private static string Available(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Not Available" : value;

    private void UpdateBehaviourSummary(NPCEmotionSessionManager manager)
    {
        if (eyeSummaryText != null) eyeSummaryText.text = EventSummary(manager, "eye");
        if (faceSummaryText != null) faceSummaryText.text = EventSummary(manager, "face");
        var voice = manager?.RealismEvents.LastOrDefault(item => item.kind == "voice");
        if (voiceSummaryText != null) voiceSummaryText.text = voice != null && !string.IsNullOrWhiteSpace(voice.voiceStyle)
            ? voice.voiceStyle : "Not Recorded";
        if (lipSyncSummaryText != null) lipSyncSummaryText.text = voice != null && !string.IsNullOrWhiteSpace(voice.detail)
            ? "TTS: " + voice.detail + " | Lip sync: Not Recorded" : "Not Recorded";
        if (bodySummaryText != null)
        {
            var last = manager?.BodyAnimationEvents.LastOrDefault();
            bodySummaryText.text = last != null ? Available(last.animation_name) : "Not Recorded";
        }
    }

    private static string EventSummary(NPCEmotionSessionManager manager, string kind)
    {
        var last = manager?.RealismEvents.LastOrDefault(item => item.kind == kind);
        return last != null && !string.IsNullOrWhiteSpace(last.detail) ? last.detail : "Not Recorded";
    }

    private static void SetSummary(TMP_Text label, Dictionary<string, string> values, string table)
    {
        if (label != null)
            label.text = values.TryGetValue(table, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value : "Not Recorded";
    }
}
