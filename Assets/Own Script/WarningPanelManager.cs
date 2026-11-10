using System.Collections;
using UnityEngine;
using TMPro;

public class WarningPanelManager : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Assign the Canvas component of your warning banner")]
    [SerializeField] private Canvas warningCanvas;
    [Tooltip("Assign the CanvasGroup component on the warning panel/canvas for fading")]
    [SerializeField] private CanvasGroup warningCanvasGroup;
    [Tooltip("Assign the TextMeshProUGUI text object inside the panel")]
    [SerializeField] private TextMeshProUGUI warningText;

    [Header("Positioning Settings")]
    [Tooltip("Assign Main Camera (XR Camera / CenterEyeAnchor)")]
    [SerializeField] private Transform playerCamera;
    [SerializeField] private float spawnDistance = 1.0f;

    [Header("Animation Settings")]
    [SerializeField] private float displayDuration = 3.0f;
    [SerializeField] private float fadeSpeed = 4.0f;

    private Coroutine activeWarningCoroutine;

    private void Start()
    {
        if (warningCanvas != null)
            warningCanvas.enabled = false;

        if (warningCanvasGroup != null)
            warningCanvasGroup.alpha = 0f;

        if (playerCamera == null && Camera.main != null)
            playerCamera = Camera.main.transform;
    }

    /// <summary>
    /// Displays a temporary warning message at eye level that automatically fades out.
    /// </summary>
    public void ShowWarning(string message)
    {

        Debug.Log($"ShowWarning called. canvas={warningCanvas != null}, group={warningCanvasGroup != null}, text={warningText != null}");
        if (warningCanvas == null || warningCanvasGroup == null || warningText == null) return;

        if (activeWarningCoroutine != null)
            StopCoroutine(activeWarningCoroutine);

        warningText.text = message;
        PositionInFrontOfPlayer();

        activeWarningCoroutine = StartCoroutine(FadeInOutRoutine());
    }

    private IEnumerator FadeInOutRoutine()
    {
        warningCanvas.enabled = true;

        // 1. Fade In
        while (warningCanvasGroup.alpha < 1.0f)
        {
            warningCanvasGroup.alpha += Time.deltaTime * fadeSpeed;
            yield return null;
        }
        warningCanvasGroup.alpha = 1.0f;

        // 2. Hold Visible
        yield return new WaitForSeconds(displayDuration);

        // 3. Fade Out
        while (warningCanvasGroup.alpha > 0.0f)
        {
            warningCanvasGroup.alpha -= Time.deltaTime * fadeSpeed;
            yield return null;
        }
        warningCanvasGroup.alpha = 0.0f;

        warningCanvas.enabled = false;
        activeWarningCoroutine = null;
    }

    private void PositionInFrontOfPlayer()
    {
        if (playerCamera == null || warningCanvas == null) return;

        Vector3 forwardDir = playerCamera.forward;
        forwardDir.y = 0;
        forwardDir.Normalize();

        Vector3 targetPosition = playerCamera.position + (forwardDir * spawnDistance);
        targetPosition.y = playerCamera.position.y;

        warningCanvas.transform.position = targetPosition;
        warningCanvas.transform.rotation = Quaternion.LookRotation(forwardDir);
    }
}