
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
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
    [SerializeField] private TMP_Text dashboardTitle;
    [SerializeField] private TMP_Text timelineTitle;
    [SerializeField] private TMP_Text distributionTitle;
    [SerializeField] private TMP_Text behaviourTitle;
    private TMP_Dropdown breakdown, timelinePage;
    private List<NPCEmotionRecord> sessionHistory, visibleHistory;
    private List<string> npcNames;
    private const int PageSize = 5;
    private Vector2 titleOffsetMax;

    [Header("Final Emotion Panel")]
    [SerializeField] private Image finalEmotionIcon;
    [SerializeField] private TMP_Text finalEmotionLabel;
    [SerializeField] private TMP_Text intensityText;
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
    [SerializeField] private TMP_Text eyeSummaryText;
    [SerializeField] private TMP_Text faceSummaryText;
    [SerializeField] private TMP_Text bodySummaryText;
    [SerializeField] private TMP_Text voiceSummaryText;
    [SerializeField] private TMP_Text lipSyncSummaryText;
    [SerializeField] private SupabaseManager supabaseManager;

    private readonly string[] emotionOrder =
    {
        "HAPPY",
        "SAD",
        "ANGRY",
        "NERVOUS",
        "THINKING",
        "NEUTRAL"
    };

    private readonly Dictionary<TMP_Text, Image> summaryIcons = new Dictionary<TMP_Text, Image>();
    private Sprite[] summaryStatusSprites;

    private void Start()
    {
        titleOffsetMax = dashboardTitle.rectTransform.offsetMax;
        breakdown = CreateSelector(dashboardTitle, "NPC Breakdown", 340, 0);
        breakdown.onValueChanged.AddListener(SelectNpc);
        timelinePage = CreateSelector(timelineTitle, "Timeline Page", 170, timelineTitle.rectTransform.anchoredPosition.y);
        timelinePage.onValueChanged.AddListener(ShowPage);
        EnsureLegendSwatches();
        ConfigureSummaryText();
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
            RefreshSummaryIcons();
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
                    lipSyncSummaryText.text = result != null && result.TryGetValue("voice_event", out var tts) && !string.IsNullOrWhiteSpace(tts)
                        ? FormatTts(tts) + "; lip sync not recorded" : "Not Recorded";
                RefreshSummaryIcons();
            }, error =>
            {
                Debug.LogError("[Emotion Dashboard] " + error);
                if (this == null || UserSession.EmotionDashboardSession?.session_id != sessionId) return;
                ShowBehaviourUnavailable();
            }));
            var savedHistory = new List<NPCEmotionRecord>();
            foreach (var turn in (selectedSession.dialogue_turn ?? new List<DialogueTurnRecord>())
                .Where(t => t != null).OrderBy(t => t.turn_sequence))
            {
                foreach (var item in (turn.npc_emotion_log ?? new List<NPCEmotionLogData>())
                    .Where(e => e != null).OrderBy(e => e.recorded_at))
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
            LoadBreakdown(savedHistory, selectedSession.scenario_type);
            if (sessionTotalTurnsText != null)
                sessionTotalTurnsText.text = selectedSession.dialogue_turn != null
                    ? selectedSession.dialogue_turn.Count(t => t != null).ToString() : "Not Available";
            return;
        }
        NPCEmotionSessionManager manager =
            NPCEmotionSessionManager.Instance;
        UpdateSessionInfo(null);
        UpdateBehaviourSummary(manager);
        RefreshSummaryIcons();

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

        LoadBreakdown(history, manager.SessionSceneName);

        if (sessionTotalTurnsText != null)
            sessionTotalTurnsText.text = "Not Available";

        Debug.Log(
            $"[Emotion Dashboard] Loaded {total} emotion turns."
        );
    }

    private void ShowEmptyDashboard()
    {
        LoadBreakdown(Array.Empty<NPCEmotionRecord>());

        if (sessionTotalTurnsText != null)
            sessionTotalTurnsText.text = "Not Available";
    }

    private TMP_Dropdown CreateSelector(TMP_Text title, string objectName, float width, float y)
    {
        var root = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        root.name = objectName;
        var rect = (RectTransform)root.transform;
        rect.SetParent(title.transform.parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, .5f);
        rect.anchoredPosition = new Vector2(-12, y);
        rect.sizeDelta = new Vector2(width, 48);
        var menu = root.GetComponent<TMP_Dropdown>();
        menu.image.color = new Color32(38, 76, 112, 255);
        // Keep the control readable without depending on Unity's optional default sprites.
        var arrow = rect.Find("Arrow").GetComponent<Image>();
        arrow.enabled = false;
        var arrowText = new GameObject("Expand", typeof(RectTransform), typeof(TextMeshProUGUI))
            .GetComponent<TextMeshProUGUI>();
        arrowText.rectTransform.SetParent(arrow.transform, false);
        arrowText.rectTransform.sizeDelta = new Vector2(24, 28);
        arrowText.text = ">";
        arrowText.rectTransform.localRotation = Quaternion.Euler(0, 0, -90);
        arrowText.fontSize = 18;
        arrowText.alignment = TextAlignmentOptions.Center;
        arrowText.raycastTarget = false;
        menu.captionText.font = menu.itemText.font = title.font;
        menu.captionText.fontSize = menu.itemText.fontSize = 23;
        menu.captionText.color = Color.white;
        menu.itemText.color = new Color32(25, 41, 60, 255);
        menu.captionText.richText = menu.itemText.richText = false;
        var caption = menu.captionText;
        caption.rectTransform.offsetMin = new Vector2(12, 0);
        caption.rectTransform.offsetMax = new Vector2(-32, 0);
        caption.alignment = TextAlignmentOptions.MidlineLeft;
        caption.textWrappingMode = TextWrappingModes.NoWrap;
        caption.overflowMode = TextOverflowModes.Overflow;
        caption.raycastTarget = false;
        menu.itemText.overflowMode = TextOverflowModes.Ellipsis;
        var item = (RectTransform)menu.itemText.transform.parent;
        item.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 44);
        ((RectTransform)item.parent).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 44);
        menu.template.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 200);
        return menu;
    }

    private bool hallBreakdown;

    private string NpcName(NPCEmotionRecord record)
    {
        string name = record.npcName?.Trim() ?? "";
        return hallBreakdown && string.Equals(name, "Tutor", StringComparison.OrdinalIgnoreCase) ? "Judge" : name;
    }

    private void LoadBreakdown(IEnumerable<NPCEmotionRecord> records, string scene = null)
    {
        sessionHistory = records.Where(r => r != null && emotionOrder.Contains(r.emotion))
            .OrderBy(r => r.turnNumber).ToList();
        bool hall = string.Equals(scene?.Trim(), "Hall", StringComparison.OrdinalIgnoreCase);
        string[] roster = hall ? new[] { "Judge", "Audience Member A", "Audience Member B" } :
            string.Equals(scene?.Trim(), "Classroom", StringComparison.OrdinalIgnoreCase)
                ? new[] { "Student A", "Student B" } : Array.Empty<string>();
        // Earlier Hall sessions saved the Judge under the prefab persona name.
        hallBreakdown = hall;
        npcNames = roster.Concat(sessionHistory.Select(NpcName)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        breakdown.ClearOptions();
        breakdown.AddOptions(npcNames.Count == 0 ? new List<string> { "No NPC data" } :
            npcNames.Select(n => n.Length == 0 ? "Unidentified NPC" : n).ToList());
        breakdown.SetValueWithoutNotify(0);
        breakdown.RefreshShownValue();
        breakdown.interactable = npcNames.Count > 1;
        dashboardTitle.rectTransform.offsetMax = titleOffsetMax - new Vector2(350, 0);
        SelectNpc(0);
    }

    private void SelectNpc(int selection)
    {
        visibleHistory = npcNames.Count == 0 ? new List<NPCEmotionRecord>() :
            sessionHistory.Where(r => string.Equals(NpcName(r), npcNames[selection],
                StringComparison.OrdinalIgnoreCase)).ToList();
        timelineTitle.text = "Emotion Timeline (NPC)";
        distributionTitle.text = "Emotion Distribution (NPC)";
        behaviourTitle.text = "Session Behaviour";
        UpdateFinalEmotion(visibleHistory.LastOrDefault());
        UpdateDistribution(visibleHistory, visibleHistory.Count);
        int pages = (visibleHistory.Count + PageSize - 1) / PageSize;
        timelinePage.ClearOptions();
        timelinePage.AddOptions(Enumerable.Range(1, pages).Select(i => $"Page {i} / {pages}").ToList());
        timelinePage.SetValueWithoutNotify(0);
        timelinePage.gameObject.SetActive(pages > 1);
        ShowPage(0);
    }

    private void ShowPage(int page)
    {
        UpdateTimeline(visibleHistory.Skip(page * PageSize).Take(PageSize).ToList());
    }

    private void UpdateFinalEmotion(NPCEmotionRecord record)
    {
        if (record == null)
        {
            if (finalEmotionLabel != null)
                finalEmotionLabel.text = "NO DATA";

            if (intensityText != null)
                intensityText.text = "Intensity --";

            if (finalEmotionIcon != null)
            {
                finalEmotionIcon.sprite = null;
                finalEmotionIcon.enabled = false;
            }

            NPCEmotionIndicatorUI.ApplyMoodBars(intensityBars, "NEUTRAL", 0);
            return;
        }

        string emotion = record.emotion;
        int intensity = Mathf.Clamp(record.intensity, 0, 100);
        if (string.Equals(emotion, "NEUTRAL", StringComparison.OrdinalIgnoreCase)) intensity = 0;

        if (finalEmotionLabel != null)
            finalEmotionLabel.text = emotion;

        if (intensityText != null)
            intensityText.text = $"Intensity {intensity}%";

        EmotionDisplayBinding binding = FindBinding(emotion);

        if (finalEmotionIcon != null)
        {
            finalEmotionIcon.sprite =
                binding != null ? binding.emotionSprite : null;

            finalEmotionIcon.enabled =
                finalEmotionIcon.sprite != null;
        }

        NPCEmotionIndicatorUI.ApplyMoodBars(intensityBars, emotion, intensity);
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
            var previous = timelineItemsContainer.GetChild(i).gameObject;
            previous.SetActive(false);
            Destroy(previous);
        }

        foreach (NPCEmotionRecord record in history)
        {
            GameObject item = Instantiate(
                emotionItemPrefab,
                timelineItemsContainer
            );

            item.SetActive(true);

            Transform iconTransform =
                item.transform.Find("EmotionIcon") ?? item.transform.Find("Emotion Icon");

            Transform labelTransform =
                item.transform.Find("EmotionLabel");

            Transform turnTransform = item.transform.Find("TurnLabel");
            if (turnTransform != null)
            {
                TMP_Text turnLabel = turnTransform.GetComponent<TMP_Text>();
                if (turnLabel != null)
                    turnLabel.text = record.turnNumber > 0 ? $"T{record.turnNumber}" : "";
            }

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

    private void EnsureLegendSwatches()
    {
        if (emotions == null) return;
        foreach (var binding in emotions)
        {
            if (binding?.legendIcon == null || binding.donutSlice == null) continue;
            var icon = binding.legendIcon.rectTransform;
            var parent = icon.parent as RectTransform;
            if (parent == null || (timelineItemsContainer != null && icon.IsChildOf(timelineItemsContainer))) continue;
            var existing = parent.Find("Distribution Colour Key") ?? icon.Find("Distribution Colour Key");
            var swatch = existing != null ? existing.GetComponent<Image>() : null;
            if (swatch == null)
            {
                var item = new GameObject("Distribution Colour Key", typeof(RectTransform), typeof(Image));
                item.layer = parent.gameObject.layer;
                swatch = item.GetComponent<Image>();
                swatch.raycastTarget = false;
            }
            // Keep the key in the distribution row, separate from any timeline icon.
            var rect = swatch.rectTransform;
            rect.SetParent(parent, false);
            rect.anchorMin = icon.anchorMin;
            rect.anchorMax = icon.anchorMax;
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(
                -211f, icon.anchoredPosition.y + icon.rect.height * (0.5f - icon.pivot.y));
            rect.sizeDelta = new Vector2(18f, 18f);
            swatch.color = binding.donutSlice.color;
        }
    }

    private void ConfigureSummaryText()
    {
        foreach (var label in new[] { eyeSummaryText, faceSummaryText, bodySummaryText, voiceSummaryText, lipSyncSummaryText })
        {
            if (label == null) continue;
            label.enabled = false;
            var existing = label.transform.Find("Behaviour Status Icon");
            var icon = existing != null ? existing.GetComponent<Image>() : null;
            if (icon == null)
            {
                var item = new GameObject("Behaviour Status Icon", typeof(RectTransform), typeof(Image));
                item.layer = label.gameObject.layer;
                item.transform.SetParent(label.transform, false);
                icon = item.GetComponent<Image>();
            }
            var rect = icon.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(30f, 30f);
            icon.raycastTarget = false;
            summaryIcons[label] = icon;
        }
        if (summaryStatusSprites == null)
            summaryStatusSprites = new[] { CreateStatusSprite(0), CreateStatusSprite(1), CreateStatusSprite(2), CreateStatusSprite(3) };
    }

    // 0 = recorded, 1 = not recorded, 2 = unavailable/error, 3 = loading.
    private static int SummaryStatusIndex(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("Not Recorded", StringComparison.OrdinalIgnoreCase)) return 1;
        if (value.Equals("Loading...", StringComparison.OrdinalIgnoreCase)) return 3;
        if (value.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
        return 0;
    }

    private void RefreshSummaryIcons()
    {
        if (summaryStatusSprites == null) return;
        foreach (var pair in summaryIcons)
            if (pair.Key != null && pair.Value != null)
                pair.Value.sprite = summaryStatusSprites[SummaryStatusIndex(pair.Key.text)];
    }

    private static Sprite CreateStatusSprite(int status)
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "Dashboard behaviour status " + status;
        texture.filterMode = FilterMode.Bilinear;
        var pixels = new Color32[size * size];
        Color32 fill = status == 0 ? new Color32(44, 159, 99, 255) :
            status == 1 ? new Color32(125, 136, 149, 255) :
            status == 2 ? new Color32(196, 61, 61, 255) : new Color32(80, 133, 185, 255);
        var white = new Color32(255, 255, 255, 255);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            bool inside = (p - new Vector2(32f, 32f)).sqrMagnitude <= 30f * 30f;
            bool mark = status == 0 ? DistanceToSegment(p, new Vector2(17, 32), new Vector2(27, 22)) < 3f || DistanceToSegment(p, new Vector2(27, 22), new Vector2(48, 44)) < 3f :
                status == 1 ? Mathf.Abs(p.y - 32f) < 3f && p.x >= 18f && p.x <= 46f :
                status == 2 ? (Mathf.Abs(p.x - 32f) < 3f && p.y >= 29f && p.y <= 46f) || (p - new Vector2(32, 20)).sqrMagnitude < 10f :
                (p - new Vector2(22, 32)).sqrMagnitude < 9f || (p - new Vector2(32, 32)).sqrMagnitude < 9f || (p - new Vector2(42, 32)).sqrMagnitude < 9f;
            pixels[y * size + x] = !inside ? new Color32(0, 0, 0, 0) : mark ? white : fill;
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / segment.sqrMagnitude);
        return Vector2.Distance(point, start + t * segment);
    }

    private void OnDestroy()
    {
        if (summaryStatusSprites == null) return;
        foreach (var sprite in summaryStatusSprites)
        {
            if (sprite == null) continue;
            Destroy(sprite.texture);
            Destroy(sprite);
        }
    }

    private void UpdateSessionInfo(NormalizedSessionData session)
    {
        if (sessionIdText != null) sessionIdText.text = Available(session?.session_id);
        if (scenarioText != null) scenarioText.text = Available(session?.scenario_type);
        if (startTimeText != null) startTimeText.text = FormatStartTime(session?.start_time);
        if (durationText != null) durationText.text = session != null
            ? FormatDuration(session.duration_seconds) : "Not Available";
    }

    private static string Available(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Not Available" : value;

    private static string FormatStartTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Not Available";
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var timestamp)
            ? timestamp.ToLocalTime().ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture)
            : "Not Available";
    }

    private static string FormatDuration(float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return "Not Available";
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private void UpdateBehaviourSummary(NPCEmotionSessionManager manager)
    {
        if (eyeSummaryText != null) eyeSummaryText.text = EventSummary(manager, "eye");
        if (faceSummaryText != null) faceSummaryText.text = EventSummary(manager, "face");
        var voice = manager?.RealismEvents.LastOrDefault(item => item.kind == "voice");
        if (voiceSummaryText != null) voiceSummaryText.text = voice != null && !string.IsNullOrWhiteSpace(voice.voiceStyle)
            ? voice.voiceStyle : "Not Recorded";
        if (lipSyncSummaryText != null) lipSyncSummaryText.text = voice != null && !string.IsNullOrWhiteSpace(voice.detail)
            ? FormatTts(voice.detail) + "; lip sync not recorded" : "Not Recorded";
        if (bodySummaryText != null)
        {
            var last = manager?.BodyAnimationEvents.LastOrDefault();
            bodySummaryText.text = last != null ? FormatEvent(last.animation_name) : "Not Recorded";
        }
    }

    private static string EventSummary(NPCEmotionSessionManager manager, string kind)
    {
        var last = manager?.RealismEvents.LastOrDefault(item => item.kind == kind);
        return last != null ? FormatEvent(last.detail) : "Not Recorded";
    }

    private static void SetSummary(TMP_Text label, Dictionary<string, string> values, string table)
    {
        if (label != null)
            label.text = values != null && values.TryGetValue(table, out var value) && !string.IsNullOrWhiteSpace(value)
                ? FormatEvent(value) : "Not Recorded";
    }

    private static string FormatEvent(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Not Recorded";
        if (value == "Expression blendshape transition started") return "Expression changed";
        if (value.StartsWith("Seated ", StringComparison.Ordinal) && value.EndsWith(" forearm gesture", StringComparison.Ordinal))
            return value.Substring(7, value.Length - 7 - " forearm gesture".Length) + " gesture";
        return value.Replace("BodyGestures.", "").Replace('_', ' ');
    }

    private static string FormatTts(string value)
    {
        switch (value)
        {
            case "playback_started": return "TTS started";
            case "playback_failed": return "TTS playback failed";
            case "generation_failed": return "TTS generation failed";
            default: return "TTS: " + FormatEvent(value);
        }
    }

    private void ShowBehaviourUnavailable()
    {
        foreach (var label in new[] { eyeSummaryText, faceSummaryText, bodySummaryText, voiceSummaryText, lipSyncSummaryText })
            if (label != null) label.text = "Unavailable";
        RefreshSummaryIcons();
    }
}
