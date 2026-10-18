using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public class UIButtonHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Target References")]
    [Tooltip("Assign child TextMeshPro component. Auto-detected if empty.")]
    public TextMeshProUGUI targetText;

    [Header("Scale Settings")]
    [Tooltip("Target scale when hovered (e.g., 0.9 for smaller, 1.1 for larger).")]
    public Vector3 hoverScale = new Vector3(0.9f, 0.9f, 1f);
    public float scaleSpeed = 12f;

    [Header("Text Color Settings")]
    public Color normalTextColor = Color.white;
    public Color hoverTextColor = Color.yellow;

    private Vector3 originalScale;
    private Vector3 targetScale;
    private Color targetColor;

    private void Awake()
    {
        originalScale = transform.localScale;
        targetScale = originalScale;

        if (targetText == null)
        {
            targetText = GetComponentInChildren<TextMeshProUGUI>();
        }

        targetColor = normalTextColor;
    }

    private void OnEnable()
    {
        // Reset transform state when UI panel enables
        transform.localScale = originalScale;
        targetScale = originalScale;
        targetColor = normalTextColor;

        if (targetText != null)
        {
            targetText.color = normalTextColor;
        }
    }

    private void Update()
    {
        // Smoothly scale the button
        if (transform.localScale != targetScale)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * scaleSpeed);
        }

        // Smoothly transition text color
        if (targetText != null && targetText.color != targetColor)
        {
            targetText.color = Color.Lerp(targetText.color, targetColor, Time.deltaTime * scaleSpeed);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        targetScale = Vector3.Scale(originalScale, hoverScale);
        targetColor = hoverTextColor;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        targetScale = originalScale;
        targetColor = normalTextColor;
    }
}