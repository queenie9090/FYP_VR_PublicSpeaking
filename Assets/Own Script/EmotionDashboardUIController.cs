
using System;
using System.Collections.Generic;
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
        NPCEmotionSessionManager manager =
            NPCEmotionSessionManager.Instance;

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
            sessionTotalTurnsText.text = total.ToString();

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
            sessionTotalTurnsText.text = "0";
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
                    turnLabel.text = $"T{record.turnNumber}";
            }
        }
    }

    private void UpdateDistribution(
        IReadOnlyList<NPCEmotionRecord> history,
        int total)
    {
        if (totalTurnsText != null)
            totalTurnsText.text = $"{total}\nTurns";

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
}
