
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NPCEmotionIndicatorUI : MonoBehaviour
{
    [Header("NPC Emotion Source")]
    [SerializeField] private NPCEmotionController emotionController;

    [Header("UI Text")]
    [Header("Emotion Image")]
    [SerializeField] private Image emotionIcon;

    [Header("Emotion Sprites")]
    [SerializeField] private Sprite happySprite;
    [SerializeField] private Sprite sadSprite;
    [SerializeField] private Sprite angrySprite;
    [SerializeField] private Sprite nervousSprite;
    [SerializeField] private Sprite thinkingSprite;
    [SerializeField] private Sprite neutralSprite;
    [SerializeField] private TMP_Text emotionLabel;
    [SerializeField] private TMP_Text intensityText;
    [SerializeField] private TMP_Text levelText;

    [Header("Five Mood Bar Images")]
    [SerializeField] private Image[] moodBars = new Image[5];

    private string lastEmotion = "";
    private int lastIntensity = -1;

    public void SetEmotionSource(NPCEmotionController source)
    {
        emotionController = source;
        lastEmotion = "";
        lastIntensity = -1;
    }

    private void Update()
    {
        if (emotionController == null)
            return;

        string emotion = emotionController.currentEmotion;
        int intensity = emotionController.emotionIntensity;

        if (emotion == lastEmotion &&
            intensity == lastIntensity)
            return;

        lastEmotion = emotion;
        lastIntensity = intensity;

        UpdateEmotionUI(emotion, intensity);
    }

    private void UpdateEmotionUI(string emotion, int intensity)
    {
        if (string.IsNullOrEmpty(emotion))
            emotion = "NEUTRAL";

        intensity = Mathf.Clamp(intensity, 0, 100);

        if (emotion == "NEUTRAL")
            intensity = 0;

        int level = intensity == 0
            ? 0
            : Mathf.CeilToInt(intensity / 20f);

        string levelName = level switch
        {
            1 => "Very Low",
            2 => "Low",
            3 => "Moderate",
            4 => "High",
            5 => "Very High",
            _ => "Neutral Baseline"
        };

        if (emotionIcon != null)
        {
            emotionIcon.sprite = emotion switch
            {
                "HAPPY" => happySprite,
                "SAD" => sadSprite,
                "ANGRY" => angrySprite,
                "NERVOUS" => nervousSprite,
                "THINKING" => thinkingSprite,
                _ => neutralSprite
            };
        }

        if (emotionLabel != null)
            emotionLabel.text = emotion;

        if (intensityText != null)
            intensityText.text = $"Intensity: {intensity}%";

        if (levelText != null)
            levelText.text = levelName;

        Color moodColor = emotion switch
        {
            "HAPPY" => new Color(1f, 0.8f, 0.3f),
            "SAD" => new Color(0.35f, 0.65f, 1f),
            "ANGRY" => new Color(1f, 0.35f, 0.3f),
            "NERVOUS" => new Color(0.9f, 0.65f, 0.3f),
            "THINKING" => new Color(0.65f, 0.55f, 1f),
            _ => Color.white
        };
        for (int i = 0; i < moodBars.Length; i++)
        {
            if (moodBars[i] == null)
                continue;

            moodBars[i].color = i < level
                ? Color.Lerp(Color.white, moodColor, 0.4f + 0.6f * intensity / 100f)
                : new Color(0.35f, 0.35f, 0.35f, 1f);
        }
    }
}
