
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

    [Header("Five Mood Bar Images")]
    [SerializeField] private Image[] moodBars = new Image[5];

    private string lastEmotion = "";
    private int lastIntensity = -1;

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

        for (int i = 0; i < moodBars.Length; i++)
        {
            if (moodBars[i] == null)
                continue;

            moodBars[i].color = i < level
                ? Color.white
                : new Color(0.35f, 0.35f, 0.35f, 1f);
        }
    }
}
