using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EducatorCohortRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text rowText;
    [SerializeField] private Button selectRowButton;

    private NormalizedSessionData boundSessionData;
    private System.Action<NormalizedSessionData> onSelectCallback;

    public void Setup(NormalizedSessionData data, System.Action<NormalizedSessionData> onSelect)
    {
        boundSessionData = data;
        onSelectCallback = onSelect;

        // Truncate UUID
        string shortId = !string.IsNullOrEmpty(data.user_id) && data.user_id.Length >= 6
            ? "P_" + data.user_id.Substring(0, 6)
            : data.user_id;

        // Parse date
        string cleanDate = data.start_time;
        if (System.DateTime.TryParse(data.start_time, out System.DateTime parsed))
        {
            cleanDate = parsed.ToString("yyyy-MM-dd");
        }

        string durationStr = $"{data.duration_seconds / 60:F0}m {data.duration_seconds % 60:F0}s";

        float overallScore = (data.performance_metric != null && data.performance_metric.Count > 0)
            ? data.performance_metric[0].overall_score
            : 0f;

        // Format single line with <pos> tags
        if (rowText != null)
        {
            rowText.text = $"<pos=3%>{shortId}" +
                           $"<pos=26%>{cleanDate}" +
                           $"<pos=48%>{data.scenario_type}" +
                           $"<pos=72%>{durationStr}" +
                           $"<pos=88%>{overallScore:F0}%";
        }
        else
        {
            Debug.LogError("[EducatorCohortRowUI] rowText reference is missing on prefab!");
        }

        // Bind button click listener
        if (selectRowButton != null)
        {
            selectRowButton.onClick.RemoveAllListeners();
            selectRowButton.onClick.AddListener(() => onSelectCallback?.Invoke(boundSessionData));
        }
        else
        {
            Debug.LogError("[EducatorCohortRowUI] selectRowButton reference is missing on prefab!");
        }
    }
}