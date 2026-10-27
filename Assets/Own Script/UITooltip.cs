using UnityEngine;
using UnityEngine.EventSystems;

public class UITooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Tooltip Container")]
    [Tooltip("Drag the UI Panel/Text object that holds the tooltip message here.")]
    [SerializeField] private GameObject tooltipPanel;

    private void Start()
    {
        // Ensure the tooltip is hidden when the scene loads
        if (tooltipPanel != null)
        {
            tooltipPanel.SetActive(false);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (tooltipPanel != null)
        {
            tooltipPanel.SetActive(true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (tooltipPanel != null)
        {
            tooltipPanel.SetActive(false);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Keeps the tooltip visible even if clicked
        if (tooltipPanel != null)
        {
            tooltipPanel.SetActive(true);
        }
    }
}