using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class TimelineChart : MonoBehaviour
{
    [Header("Timeline Chart Setup")]
    [SerializeField] private RectTransform chartContainer;
    [SerializeField] private GameObject barPrefab; // Simple UI Panel/Image prefab
    [SerializeField] private Color barColor = new Color(0.2f, 0.6f, 1.0f, 1.0f);

    /// <summary>
    /// Generates visual bars for each dialogue turn based on engagement performance.
    /// </summary>
    public void PopulateTimeline(List<float> turnEngagementScores)
    {
        if (chartContainer == null || barPrefab == null) return;

        // Clear existing bars
        foreach (Transform child in chartContainer)
        {
            Destroy(child.gameObject);
        }

        float containerWidth = chartContainer.rect.width;
        float containerHeight = chartContainer.rect.height;
        float barWidth = (containerWidth / turnEngagementScores.Count) - 10.0f;

        for (int i = 0; i < turnEngagementScores.Count; i++)
        {
            GameObject barObj = Instantiate(barPrefab, chartContainer);
            RectTransform rect = barObj.GetComponent<RectTransform>();
            Image barImage = barObj.GetComponent<Image>();

            if (barImage != null) barImage.color = barColor;

            float normalizedScore = Mathf.Clamp01(turnEngagementScores[i] / 100.0f);
            float barHeight = normalizedScore * containerHeight;

            rect.sizeDelta = new Vector2(barWidth, barHeight);
            rect.anchoredPosition = new Vector2(i * (barWidth + 10.0f) + (barWidth / 2.0f), barHeight / 2.0f);
        }
    }
}