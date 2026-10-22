using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TimelineChart : MonoBehaviour
{
    [Header("UI Container")]
    [SerializeField] private RectTransform chartContainer;

    [Header("Styling")]
    [SerializeField] private Color barColor = new Color(0.2f, 0.65f, 1.0f, 0.9f); // Soft Cyan/Blue
    [SerializeField] private Color barBackgroundColor = new Color(0.12f, 0.15f, 0.22f, 0.6f);
    [SerializeField] private float barSpacing = 12.0f;

    public void PopulateTimeline(List<float> turnScores)
    {
        if (chartContainer == null || turnScores == null || turnScores.Count == 0) return;

        // 1. Clear existing chart elements
        foreach (Transform child in chartContainer)
        {
            Destroy(child.gameObject);
        }

        // 2. Setup Horizontal Layout Group on chartContainer
        HorizontalLayoutGroup layout = chartContainer.GetComponent<HorizontalLayoutGroup>();
        if (layout == null) layout = chartContainer.gameObject.AddComponent<HorizontalLayoutGroup>();

        layout.padding = new RectOffset(10, 10, 25, 25); // Padding top & bottom for labels
        layout.spacing = barSpacing;
        layout.childAlignment = TextAnchor.LowerCenter; // Align all columns to the bottom
        layout.childControlWidth = true;
        layout.childControlHeight = true; // Control column container height explicitly
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        float availableHeight = chartContainer.rect.height - 50.0f; // Leave space for top & bottom text

        // 3. Create a bar column for each dialogue turn
        for (int i = 0; i < turnScores.Count; i++)
        {
            float score = Mathf.Clamp(turnScores[i], 0f, 100f);

            // Column Wrapper
            GameObject column = new GameObject($"Turn_{i + 1}_Column", typeof(RectTransform));
            column.transform.SetParent(chartContainer, false);

            // Dark Background Track (Full Height)
            GameObject barBG = new GameObject("BarBG", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            barBG.transform.SetParent(column.transform, false);
            RectTransform bgRT = barBG.GetComponent<RectTransform>();
            Image bgImg = barBG.GetComponent<Image>();
            bgImg.color = barBackgroundColor;

            // Anchor Background Track to fill column middle area
            bgRT.anchorMin = new Vector2(0.15f, 0.15f);
            bgRT.anchorMax = new Vector2(0.85f, 0.85f);
            bgRT.sizeDelta = Vector2.zero;

            // Filled Score Bar (Anchored from the Bottom of the Track)
            GameObject barFill = new GameObject("BarFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            barFill.transform.SetParent(barBG.transform, false);
            RectTransform fillRT = barFill.GetComponent<RectTransform>();
            Image fillImg = barFill.GetComponent<Image>();
            fillImg.color = barColor;

            fillRT.anchorMin = new Vector2(0, 0); // Bottom-left of track
            fillRT.anchorMax = new Vector2(1, 0); // Bottom-right of track
            fillRT.pivot = new Vector2(0.5f, 0);   // Scale up from bottom

            float trackHeight = availableHeight;
            float fillHeight = (score / 100.0f) * trackHeight;
            fillRT.sizeDelta = new Vector2(0, Mathf.Max(fillHeight, 4.0f));

            // --- Top Label: Value / Score Percentage ---
            GameObject valueLabelGO = new GameObject("ValueLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            valueLabelGO.transform.SetParent(column.transform, false);
            RectTransform valueRT = valueLabelGO.GetComponent<RectTransform>();
            TextMeshProUGUI valueTxt = valueLabelGO.GetComponent<TextMeshProUGUI>();

            valueTxt.text = $"{score:F0}%";
            valueTxt.fontSize = 12;
            valueTxt.fontStyle = FontStyles.Bold;
            valueTxt.alignment = TextAlignmentOptions.Center;
            valueTxt.color = new Color(0.3f, 0.85f, 1.0f, 1.0f);

            valueRT.anchorMin = new Vector2(0, 0.86f);
            valueRT.anchorMax = new Vector2(1, 1.0f);
            valueRT.sizeDelta = Vector2.zero;

            // --- Bottom Label: Turn Identifier (T1, T2...) ---
            GameObject labelGO = new GameObject("TurnLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelGO.transform.SetParent(column.transform, false);
            RectTransform labelRT = labelGO.GetComponent<RectTransform>();
            TextMeshProUGUI labelTxt = labelGO.GetComponent<TextMeshProUGUI>();

            labelTxt.text = $"T{i + 1}";
            labelTxt.fontSize = 11;
            labelTxt.alignment = TextAlignmentOptions.Center;
            labelTxt.color = new Color(0.75f, 0.8f, 0.88f, 0.9f);

            labelRT.anchorMin = new Vector2(0, 0f);
            labelRT.anchorMax = new Vector2(1, 0.14f);
            labelRT.sizeDelta = Vector2.zero;
        }
    }
}