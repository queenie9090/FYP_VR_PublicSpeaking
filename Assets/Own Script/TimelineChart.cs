using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TimelineChart : MonoBehaviour
{
    [Header("UI Container")]
    [SerializeField] private RectTransform chartContainer;

    [Header("Bar Styling Defaults")]
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

        // Force UI layout pass to ensure rect dimensions are accurately updated
        Canvas.ForceUpdateCanvases();

        // 1. Clear previous elements
        foreach (Transform child in chartContainer)
        {
            Destroy(child.gameObject);
        }

        float totalHeight = chartContainer.rect.height;
        float totalWidth = chartContainer.rect.width;

        // Dynamic plot margin calculations based on container dimensions
        float plotLeft = Mathf.Clamp(totalWidth * 0.08f, 35.0f, 60.0f);   // Left margin for T1, T2 labels
        float plotRight = Mathf.Clamp(totalWidth * 0.10f, 45.0f, 70.0f);  // Right margin for % labels
        float xAxisHeight = Mathf.Clamp(totalHeight * 0.10f, 20.0f, 35.0f); // Bottom margin for X-axis
        float plotTop = Mathf.Clamp(totalHeight * 0.05f, 10.0f, 25.0f);

        float plotWidth = Mathf.Max(totalWidth - (plotLeft + plotRight), 50.0f);
        float plotHeight = Mathf.Max(totalHeight - (plotTop + xAxisHeight), 50.0f);

        // Calculate dynamic bar height & spacing proportionally to fit turn count inside plotHeight
        int count = turnScores.Count;
        float calculatedSpacing = Mathf.Clamp(plotHeight * 0.04f, 4.0f, 16.0f);
        float calculatedBarHeight = (plotHeight - (calculatedSpacing * (count - 1))) / count;
        calculatedBarHeight = Mathf.Clamp(calculatedBarHeight, 14.0f, 48.0f); // Keep thickness within readable bounds

        // Dynamic font sizing driven by calculated bar height
        int baseFontSize = Mathf.Clamp(Mathf.RoundToInt(calculatedBarHeight * 0.45f), 10, 18);
        int axisFontSize = Mathf.Clamp(Mathf.RoundToInt(xAxisHeight * 0.45f), 10, 14);

        // 2. Render X-Axis Labels & Vertical Grid Lines (0%, 25%, 50%, 75%, 100%)
        int[] steps = new int[] { 0, 25, 50, 75, 100 };
        foreach (int step in steps)
        {
            float normX = step / 100.0f;
            float xPos = plotLeft + (normX * plotWidth);

            // X-Axis Text Label
            GameObject labelGO = new GameObject($"XLabel_{step}", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelGO.transform.SetParent(chartContainer, false);
            RectTransform labelRT = labelGO.GetComponent<RectTransform>();
            TextMeshProUGUI labelTxt = labelGO.GetComponent<TextMeshProUGUI>();

            labelTxt.text = $"{step}%";
            labelTxt.fontSize = axisFontSize;
            labelTxt.alignment = TextAlignmentOptions.Center;
            labelTxt.color = labelColor;

            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.zero;
            labelRT.pivot = new Vector2(0.5f, 1.0f);
            labelRT.anchoredPosition = new Vector2(xPos, xAxisHeight - 2.0f);
            labelRT.sizeDelta = new Vector2(50.0f, xAxisHeight);

            // Vertical Grid Line
            GameObject lineGO = new GameObject($"GridLine_{step}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            lineGO.transform.SetParent(chartContainer, false);
            RectTransform lineRT = lineGO.GetComponent<RectTransform>();
            lineGO.GetComponent<Image>().color = gridLineColor;

            lineRT.anchorMin = Vector2.zero;
            lineRT.anchorMax = Vector2.zero;
            lineRT.pivot = new Vector2(0.5f, 0);
            lineRT.anchoredPosition = new Vector2(xPos, xAxisHeight);
            lineRT.sizeDelta = new Vector2(1.0f, plotHeight);
        }

        // 3. X-Axis Base Line
        GameObject xAxis = new GameObject("XAxisLine", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        xAxis.transform.SetParent(chartContainer, false);
        RectTransform xAxisRT = xAxis.GetComponent<RectTransform>();
        xAxis.GetComponent<Image>().color = axisColor;

        xAxisRT.anchorMin = Vector2.zero;
        xAxisRT.anchorMax = Vector2.zero;
        xAxisRT.pivot = new Vector2(0, 0.5f);
        xAxisRT.anchoredPosition = new Vector2(plotLeft, xAxisHeight);
        xAxisRT.sizeDelta = new Vector2(plotWidth, 2.0f);

        // 4. Bars Container
        GameObject barsContainer = new GameObject("BarsContainer", typeof(RectTransform));
        barsContainer.transform.SetParent(chartContainer, false);
        RectTransform barsRT = barsContainer.GetComponent<RectTransform>();

        barsRT.anchorMin = Vector2.zero;
        barsRT.anchorMax = Vector2.one;
        barsRT.offsetMin = new Vector2(plotLeft, xAxisHeight + 4.0f);
        barsRT.offsetMax = new Vector2(-plotRight, -plotTop);

        VerticalLayoutGroup layout = barsContainer.AddComponent<VerticalLayoutGroup>();
        layout.spacing = calculatedSpacing;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // 5. Render Responsive Horizontal Bars
        for (int i = 0; i < turnScores.Count; i++)
        {
            float score = Mathf.Clamp(turnScores[i], 0f, 100f);
            Color barColor = score >= 80f ? highScoreColor : (score >= 60f ? midScoreColor : lowScoreColor);

            // Row Wrapper
            GameObject row = new GameObject($"Turn_{i + 1}", typeof(RectTransform));
            row.transform.SetParent(barsContainer.transform, false);
            RectTransform rowRT = row.GetComponent<RectTransform>();
            rowRT.sizeDelta = new Vector2(plotWidth, calculatedBarHeight);

            // Left Turn Indicator Label (T1, T2...)
            GameObject turnGO = new GameObject("TurnText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            turnGO.transform.SetParent(row.transform, false);
            RectTransform turnRT = turnGO.GetComponent<RectTransform>();
            TextMeshProUGUI turnTxt = turnGO.GetComponent<TextMeshProUGUI>();

            turnTxt.text = $"T{i + 1}";
            turnTxt.fontSize = baseFontSize;
            turnTxt.fontStyle = FontStyles.Bold;
            turnTxt.alignment = TextAlignmentOptions.Right;
            turnTxt.color = labelColor;

            turnRT.anchorMin = new Vector2(0, 0.5f);
            turnRT.anchorMax = new Vector2(0, 0.5f);
            turnRT.pivot = new Vector2(1.0f, 0.5f);
            turnRT.anchoredPosition = new Vector2(-6.0f, 0);
            turnRT.sizeDelta = new Vector2(plotLeft - 8.0f, calculatedBarHeight);

            // Dark Background Track
            GameObject bg = new GameObject("BG", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bg.transform.SetParent(row.transform, false);
            RectTransform bgRT = bg.GetComponent<RectTransform>();
            bg.GetComponent<Image>().color = barBGColor;

            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.sizeDelta = Vector2.zero;

            // Horizontal Score Fill Bar
            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fill.transform.SetParent(bg.transform, false);
            RectTransform fillRT = fill.GetComponent<RectTransform>();
            fill.GetComponent<Image>().color = barColor;

            fillRT.anchorMin = new Vector2(0, 0);
            fillRT.anchorMax = new Vector2(0, 1);
            fillRT.pivot = new Vector2(0, 0.5f);

            float fillWidth = (score / 100.0f) * plotWidth;
            fillRT.sizeDelta = new Vector2(Mathf.Max(fillWidth, 4.0f), 0);

            // Right Percentage Text Label
            GameObject scoreGO = new GameObject("ScoreText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            scoreGO.transform.SetParent(row.transform, false);
            RectTransform scoreRT = scoreGO.GetComponent<RectTransform>();
            TextMeshProUGUI scoreTxt = scoreGO.GetComponent<TextMeshProUGUI>();

            scoreTxt.text = $"{score:F0}%";
            scoreTxt.fontSize = baseFontSize;
            scoreTxt.fontStyle = FontStyles.Bold;
            scoreTxt.alignment = TextAlignmentOptions.Left;
            scoreTxt.color = barColor;

            scoreRT.anchorMin = new Vector2(1, 0.5f);
            scoreRT.anchorMax = new Vector2(1, 0.5f);
            scoreRT.pivot = new Vector2(0, 0.5f);
            scoreRT.anchoredPosition = new Vector2(6.0f, 0);
            scoreRT.sizeDelta = new Vector2(plotRight - 8.0f, calculatedBarHeight);
        }
    }
}