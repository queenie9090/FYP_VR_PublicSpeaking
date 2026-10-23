using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TimelineChart : MonoBehaviour
{
    [Header("UI Container")]
    [SerializeField] private RectTransform chartContainer;

    [Header("Bar Styling")]
    [SerializeField] private float barHeight = 36.0f;            // Bar thickness
    [SerializeField] private float barSpacing = 16.0f;           // Vertical spacing between turns
    [SerializeField] private Color barBGColor = new Color(0.12f, 0.15f, 0.22f, 0.6f);

    [Header("Axis & Grid Styling")]
    [SerializeField] private Color axisColor = new Color(0.35f, 0.45f, 0.6f, 0.8f);
    [SerializeField] private Color gridLineColor = new Color(0.2f, 0.28f, 0.38f, 0.4f);
    [SerializeField] private Color labelColor = new Color(0.7f, 0.78f, 0.88f, 0.9f);

    [Header("Performance Color Thresholds")]
    [SerializeField] private Color highScoreColor = new Color(0.2f, 0.85f, 0.45f, 1f); // Green (>= 80%)
    [SerializeField] private Color midScoreColor = new Color(1.0f, 0.75f, 0.2f, 1f);   // Amber (>= 60%)
    [SerializeField] private Color lowScoreColor = new Color(0.95f, 0.3f, 0.3f, 1f);   // Red (< 60%)

    public void PopulateTimeline(List<float> turnScores)
    {
        if (chartContainer == null || turnScores == null || turnScores.Count == 0) return;

        // 1. Clear all previous elements
        foreach (Transform child in chartContainer)
        {
            Destroy(child.gameObject);
        }

        float totalHeight = chartContainer.rect.height;
        float totalWidth = chartContainer.rect.width;

        // Margins: 40px left for Turn labels (T1, T2), 50px right for percentage labels, 30px bottom for X-Axis
        float xAxisHeight = 25.0f;
        float plotLeft = 40.0f;
        float plotRight = 50.0f;
        float plotTop = 15.0f;
        float plotWidth = Mathf.Max(totalWidth - (plotLeft + plotRight), 50.0f);
        float plotHeight = Mathf.Max(totalHeight - (plotTop + xAxisHeight), 50.0f);

        // 2. Render X-Axis Labels & Vertical Grid Lines (0%, 25%, 50%, 75%, 100%)
        int[] steps = new int[] { 0, 25, 50, 75, 100 };
        foreach (int step in steps)
        {
            float normX = step / 100.0f;
            float xPos = plotLeft + (normX * plotWidth);

            // X-Axis Text Label at Bottom
            GameObject labelGO = new GameObject($"XLabel_{step}", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelGO.transform.SetParent(chartContainer, false);
            RectTransform labelRT = labelGO.GetComponent<RectTransform>();
            TextMeshProUGUI labelTxt = labelGO.GetComponent<TextMeshProUGUI>();

            labelTxt.text = $"{step}%";
            labelTxt.fontSize = 11;
            labelTxt.alignment = TextAlignmentOptions.Center;
            labelTxt.color = labelColor;

            labelRT.anchorMin = new Vector2(0, 0);
            labelRT.anchorMax = new Vector2(0, 0);
            labelRT.pivot = new Vector2(0.5f, 1.0f);
            labelRT.anchoredPosition = new Vector2(xPos, xAxisHeight - 4.0f);
            labelRT.sizeDelta = new Vector2(40.0f, 20.0f);

            // Vertical Grid Line across chart height
            GameObject lineGO = new GameObject($"GridLine_{step}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            lineGO.transform.SetParent(chartContainer, false);
            RectTransform lineRT = lineGO.GetComponent<RectTransform>();
            Image lineImg = lineGO.GetComponent<Image>();
            lineImg.color = gridLineColor;

            lineRT.anchorMin = new Vector2(0, 0);
            lineRT.anchorMax = new Vector2(0, 0);
            lineRT.pivot = new Vector2(0.5f, 0);
            lineRT.anchoredPosition = new Vector2(xPos, xAxisHeight);
            lineRT.sizeDelta = new Vector2(1.0f, plotHeight);
        }

        // 3. X-Axis Horizontal Base Line
        GameObject xAxis = new GameObject("XAxisLine", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        xAxis.transform.SetParent(chartContainer, false);
        RectTransform xAxisRT = xAxis.GetComponent<RectTransform>();
        xAxis.GetComponent<Image>().color = axisColor;

        xAxisRT.anchorMin = new Vector2(0, 0);
        xAxisRT.anchorMax = new Vector2(0, 0);
        xAxisRT.pivot = new Vector2(0, 0.5f);
        xAxisRT.anchoredPosition = new Vector2(plotLeft, xAxisHeight);
        xAxisRT.sizeDelta = new Vector2(plotWidth, 2.0f);

        // 4. Bars Container (Vertical Layout for Top-to-Bottom Turn Rows)
        GameObject barsContainer = new GameObject("BarsContainer", typeof(RectTransform));
        barsContainer.transform.SetParent(chartContainer, false);
        RectTransform barsRT = barsContainer.GetComponent<RectTransform>();

        barsRT.anchorMin = new Vector2(0, 0);
        barsRT.anchorMax = new Vector2(1, 1);
        barsRT.offsetMin = new Vector2(plotLeft, xAxisHeight + 5.0f);
        barsRT.offsetMax = new Vector2(-plotRight, -plotTop);

        VerticalLayoutGroup layout = barsContainer.AddComponent<VerticalLayoutGroup>();
        layout.spacing = barSpacing;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;   // Expand container widths
        layout.childControlHeight = false; // Fixed row height
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // 5. Render Individual Horizontal Bars
        for (int i = 0; i < turnScores.Count; i++)
        {
            float score = Mathf.Clamp(turnScores[i], 0f, 100f);
            Color barColor = score >= 80f ? highScoreColor : (score >= 60f ? midScoreColor : lowScoreColor);

            // Row Wrapper
            GameObject row = new GameObject($"Turn_{i + 1}", typeof(RectTransform));
            row.transform.SetParent(barsContainer.transform, false);
            RectTransform rowRT = row.GetComponent<RectTransform>();
            rowRT.sizeDelta = new Vector2(plotWidth, barHeight);

            // Left Turn Label (T1, T2...)
            GameObject turnGO = new GameObject("TurnText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            turnGO.transform.SetParent(row.transform, false);
            RectTransform turnRT = turnGO.GetComponent<RectTransform>();
            TextMeshProUGUI turnTxt = turnGO.GetComponent<TextMeshProUGUI>();

            turnTxt.text = $"T{i + 1}";
            turnTxt.fontSize = 12;
            turnTxt.fontStyle = FontStyles.Bold;
            turnTxt.alignment = TextAlignmentOptions.Right;
            turnTxt.color = labelColor;

            turnRT.anchorMin = new Vector2(0, 0.5f);
            turnRT.anchorMax = new Vector2(0, 0.5f);
            turnRT.pivot = new Vector2(1.0f, 0.5f);
            turnRT.anchoredPosition = new Vector2(-8.0f, 0);
            turnRT.sizeDelta = new Vector2(35.0f, barHeight);

            // Dark Background Track
            GameObject bg = new GameObject("BG", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bg.transform.SetParent(row.transform, false);
            RectTransform bgRT = bg.GetComponent<RectTransform>();
            bg.GetComponent<Image>().color = barBGColor;

            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.sizeDelta = Vector2.zero;

            // Filled Horizontal Score Bar (Extends Left-to-Right)
            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fill.transform.SetParent(bg.transform, false);
            RectTransform fillRT = fill.GetComponent<RectTransform>();
            fill.GetComponent<Image>().color = barColor;

            fillRT.anchorMin = new Vector2(0, 0);
            fillRT.anchorMax = new Vector2(0, 1); // Full row height
            fillRT.pivot = new Vector2(0, 0.5f);  // Expands rightwards from left edge

            float fillWidth = (score / 100.0f) * plotWidth;
            fillRT.sizeDelta = new Vector2(Mathf.Max(fillWidth, 4.0f), 0);

            // Right Score Text (Placed at the end of the bar)
            GameObject scoreGO = new GameObject("ScoreText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            scoreGO.transform.SetParent(row.transform, false);
            RectTransform scoreRT = scoreGO.GetComponent<RectTransform>();
            TextMeshProUGUI scoreTxt = scoreGO.GetComponent<TextMeshProUGUI>();

            scoreTxt.text = $"{score:F0}%";
            scoreTxt.fontSize = 12;
            scoreTxt.fontStyle = FontStyles.Bold;
            scoreTxt.alignment = TextAlignmentOptions.Left;
            scoreTxt.color = barColor;

            scoreRT.anchorMin = new Vector2(1, 0.5f);
            scoreRT.anchorMax = new Vector2(1, 0.5f);
            scoreRT.pivot = new Vector2(0, 0.5f);
            scoreRT.anchoredPosition = new Vector2(8.0f, 0);
            scoreRT.sizeDelta = new Vector2(45.0f, barHeight);
        }
    }
}