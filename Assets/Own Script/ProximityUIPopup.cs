using UnityEngine;
using UnityEngine.Events;
using System.Collections;

[RequireComponent(typeof(Collider))]
public class ProximityUIPopup : MonoBehaviour
{
    [Header("UI Reference")]
    [Tooltip("The World Space Canvas or Panel RectTransform to animate.")]
    [SerializeField] private RectTransform uiPanelRect;

    [Header("Slide Animation Settings")]
    [Tooltip("Local Y position when UI is fully visible at eye level.")]
    [SerializeField] private float visibleLocalY = 1.6f;

    [Tooltip("Local Y position when hidden near the floor or origin.")]
    [SerializeField] private float hiddenLocalY = 0.5f;

    [Tooltip("Animation speed multiplier.")]
    [SerializeField] private float animSpeed = 8f;

    [Header("Detection Settings")]
    [SerializeField] private string playerTag = "Player";

    private Vector3 hiddenPos;
    private Vector3 visiblePos;
    private Coroutine activeAnimCoroutine;

    private void Start()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        if (uiPanelRect != null)
        {
            Vector3 currentLocal = uiPanelRect.localPosition;
            hiddenPos = new Vector3(currentLocal.x, hiddenLocalY, currentLocal.z);
            visiblePos = new Vector3(currentLocal.x, visibleLocalY, currentLocal.z);

            uiPanelRect.localPosition = hiddenPos;
            uiPanelRect.gameObject.SetActive(false);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsPlayer(other))
        {
            AnimateUI(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (IsPlayer(other))
        {
            AnimateUI(false);
        }
    }

    private bool IsPlayer(Collider other)
    {
        return other.CompareTag(playerTag) ||
               other.GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.XRBaseController>() != null;
    }

    private void AnimateUI(bool show)
    {
        if (uiPanelRect == null) return;

        if (activeAnimCoroutine != null)
        {
            StopCoroutine(activeAnimCoroutine);
        }

        uiPanelRect.gameObject.SetActive(true);
        activeAnimCoroutine = StartCoroutine(SlideRoutine(show));
    }

    private IEnumerator SlideRoutine(bool show)
    {
        Vector3 startPos = uiPanelRect.localPosition;
        Vector3 targetPos = show ? visiblePos : hiddenPos;
        float progress = 0f;

        while (progress < 1f)
        {
            progress += Time.deltaTime * animSpeed;
            uiPanelRect.localPosition = Vector3.Lerp(startPos, targetPos, progress);
            yield return null;
        }

        uiPanelRect.localPosition = targetPos;

        if (!show)
        {
            uiPanelRect.gameObject.SetActive(false);
        }
    }
}