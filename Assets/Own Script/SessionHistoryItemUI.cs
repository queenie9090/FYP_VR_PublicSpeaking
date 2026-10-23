using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SessionHistoryItemUI : MonoBehaviour
{
    [SerializeField] private Button cardButton;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text detailsText;
    [SerializeField] private TMP_Text metaText;

    private NormalizedSessionData boundData;
    private System.Action<NormalizedSessionData> onSelectedCallback;

    public void Setup(int index, NormalizedSessionData data, System.Action<NormalizedSessionData> onClickAction)
    {
        boundData = data;
        onSelectedCallback = onClickAction;

        if (titleText != null)
            titleText.text = $"<color=#38BDF8>[{index}] {data.scenario_type.ToUpper()}</color>";

        if (detailsText != null)
            detailsText.text = $"<color=#94A3B8>Topic:</color> {data.scenario_topic}";

        if (metaText != null)
        {
            string cardDate = data.start_time;
            if (System.DateTime.TryParse(data.start_time, out System.DateTime parsedDate))
            {
                cardDate = parsedDate.ToString("yyyy-MM-dd HH:mm");
            }

            metaText.text = $"<color=#94A3B8>Date:</color> {cardDate}\n<color=#94A3B8>Mode:</color> {data.input_mode_used}";
        }

        if (cardButton != null)
        {
            cardButton.onClick.RemoveAllListeners();
            cardButton.onClick.AddListener(() => onSelectedCallback?.Invoke(boundData));
        }
    }
}