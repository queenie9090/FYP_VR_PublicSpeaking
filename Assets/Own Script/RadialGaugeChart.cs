using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RadialGaugeChart : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image radialFillImage;
    [SerializeField] private TMP_Text scorePercentageText;
    [SerializeField] private TMP_Text ratingLabelText;

    /// <summary>
    /// Smoothly animates the gauge fill from 0 to target score.
    /// </summary>
    public void SetScore(float score, string rating)
    {
        if (scorePercentageText != null)
            scorePercentageText.text = $"{Mathf.RoundToInt(score)}%";

        if (ratingLabelText != null)
            ratingLabelText.text = rating;

        if (radialFillImage != null)
        {
            radialFillImage.type = Image.Type.Filled;
            radialFillImage.fillMethod = Image.FillMethod.Radial360;
            radialFillImage.fillOrigin = (int)Image.Origin360.Top;

            StartCoroutine(AnimateFillRoutine(score / 100.0f));
        }
    }

    private System.Collections.IEnumerator AnimateFillRoutine(float targetFill)
    {
        float currentFill = 0.0f;
        while (currentFill < targetFill)
        {
            currentFill += Time.deltaTime * 1.5f;
            radialFillImage.fillAmount = Mathf.Min(currentFill, targetFill);
            yield return null;
        }
        radialFillImage.fillAmount = targetFill;
    }
}