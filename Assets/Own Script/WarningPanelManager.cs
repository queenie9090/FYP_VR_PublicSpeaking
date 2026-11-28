using System;
using System.Collections;
using UnityEngine.UI;
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

    [Header("Audio Feedback")]
    [SerializeField] private AudioSource sfxAudioSource;
    [SerializeField] private AudioClip warningSFX;

    private Coroutine activeWarningCoroutine;
    private RectTransform warningCanvasRect;
    private RectTransform warningTextRect;
    private float originalCanvasHeight;
    private float originalTextHeight;
    private bool layoutCached;
    private readonly Vector3[] layoutCorners = new Vector3[4];
    private Button recoveryButton;
    private bool recoveryVisible;
    private bool originalInteractable;
    private bool originalBlocksRaycasts;
    private Vector2 originalTextPosition;

    private void Start()
    {
        if (warningCanvas != null)
            warningCanvas.enabled = false;

        if (warningCanvasGroup != null)
            warningCanvasGroup.alpha = 0f;

        if (playerCamera == null && Camera.main != null)
            playerCamera = Camera.main.transform;

        CacheOriginalLayout();
    }

    /// <summary>
    /// Displays a temporary warning message at eye level that automatically fades out.
    /// </summary>
    public void ShowWarning(string message)
    {
        ShowWarning(message, null, null);
    }

    public void ShowWarning(string message, RectTransform retryControl, RectTransform exitControl)
    {

        if (recoveryVisible) return;
        Debug.Log($"ShowWarning called. canvas={warningCanvas != null}, group={warningCanvasGroup != null}, text={warningText != null}");
        if (warningCanvas == null || warningCanvasGroup == null || warningText == null) return;

        if (sfxAudioSource != null && warningSFX != null)
        {
            sfxAudioSource.PlayOneShot(warningSFX);
        }

        if (activeWarningCoroutine != null)
            StopCoroutine(activeWarningCoroutine);

        warningText.text = message;
        ResizeForMessage(message);
        PositionInFrontOfPlayer();
        PlaceAboveControls(retryControl, exitControl);

        activeWarningCoroutine = StartCoroutine(FadeInOutRoutine());
    }

    // Validation controls belong to other canvases. Move only this banner so
    // it does not cover them; generic warnings retain their original position.
    private void PlaceAboveControls(RectTransform retryControl, RectTransform exitControl)
    {
        if (warningCanvasRect == null || playerCamera == null) return;
        var camera = playerCamera.GetComponent<Camera>();
        if (camera == null) return;

        float controlsTop = float.NegativeInfinity;
        float nearestDepth = float.PositiveInfinity;
        IncludeControlBounds(camera, retryControl, ref controlsTop, ref nearestDepth);
        IncludeControlBounds(camera, exitControl, ref controlsTop, ref nearestDepth);
        if (float.IsNegativeInfinity(controlsTop)) return;

        GetViewportBounds(camera, warningCanvasRect, out float bottom, out float top, out _);
        float desiredBottom = controlsTop + 0.025f;
        float availableHeight = 0.96f - desiredBottom;
        if (availableHeight <= 0.05f) return;

        float height = top - bottom;
        if (height > availableHeight && spawnDistance > 0f)
        {
            // A slightly greater distance keeps a tall banner in the headset's
            // view without changing its font size or covering the controls.
            float maxDistance = Mathf.Max(spawnDistance, Mathf.Min(3f, nearestDepth * 0.9f));
            float distance = Mathf.Min(spawnDistance * height / availableHeight * 1.03f, maxDistance);
            Vector3 forward = playerCamera.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
            {
                Vector3 position = playerCamera.position + forward.normalized * distance;
                position.y = playerCamera.position.y;
                warningCanvas.transform.position = position;
                GetViewportBounds(camera, warningCanvasRect, out bottom, out top, out _);
            }
        }

        float shift = desiredBottom - bottom;
        Vector3 centre = camera.WorldToViewportPoint(warningCanvasRect.position);
        if (shift > 0f && centre.z > camera.nearClipPlane)
        {
            Vector3 from = camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, centre.z));
            Vector3 to = camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f + shift, centre.z));
            warningCanvas.transform.position += to - from;
        }
    }

    private void IncludeControlBounds(Camera camera, RectTransform control, ref float highest, ref float nearest)
    {
        if (control == null || !control.gameObject.activeInHierarchy) return;
        GetViewportBounds(camera, control, out float bottom, out float top, out float depth);
        if (depth <= camera.nearClipPlane || top < 0f || bottom > 1f) return;
        highest = Mathf.Max(highest, top);
        nearest = Mathf.Min(nearest, depth);
    }

    private void GetViewportBounds(Camera camera, RectTransform rect, out float bottom, out float top, out float depth)
    {
        rect.GetWorldCorners(layoutCorners);
        bottom = float.PositiveInfinity;
        top = float.NegativeInfinity;
        depth = float.PositiveInfinity;
        foreach (Vector3 corner in layoutCorners)
        {
            Vector3 point = camera.WorldToViewportPoint(corner);
            bottom = Mathf.Min(bottom, point.y);
            top = Mathf.Max(top, point.y);
            depth = Mathf.Min(depth, point.z);
        }
    }

    private void CacheOriginalLayout()
    {
        if (layoutCached || warningCanvas == null || warningText == null) return;
        warningCanvasRect = warningCanvas.transform as RectTransform;
        warningTextRect = warningText.rectTransform;
        if (warningCanvasRect == null || warningTextRect == null) return;
        Canvas.ForceUpdateCanvases();
        originalCanvasHeight = warningCanvasRect.rect.height;
        originalTextHeight = warningTextRect.rect.height;
        originalTextPosition = warningTextRect.anchoredPosition;
        layoutCached = originalCanvasHeight > 0f && originalTextHeight > 0f;
    }

    private void ResizeForMessage(string message)
    {
        CacheOriginalLayout();
        if (!layoutCached) return;

        // All five scenes use a centred text rect inside a background stretched
        // to the canvas. Preserve their original width, padding and minimum size.
        warningTextRect.anchoredPosition = originalTextPosition;
        warningText.textWrappingMode = TextWrappingModes.Normal;
        float width = warningTextRect.rect.width;
        if (width <= 0f) return;
        float preferredHeight = warningText.GetPreferredValues(message ?? string.Empty, width, Mathf.Infinity).y;
        float textHeight = Mathf.Max(originalTextHeight, preferredHeight);
        float addedHeight = textHeight - originalTextHeight;
        warningTextRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, textHeight);
        warningCanvasRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, originalCanvasHeight + addedHeight);
        Canvas.ForceUpdateCanvases();
    }

    // Clone the scene's existing start control so styling and button states remain consistent.
    // It is a separate object, and its persistent Start callbacks are replaced completely.
    public void ShowRecovery(string message, Button styleSource, Action onRetry)
    {
        if (warningCanvas == null || warningCanvasGroup == null || warningText == null || styleSource == null)
        {
            Debug.LogError("[WarningPanelManager] Cannot show recovery: warning UI or button style is missing.");
            return;
        }
        CacheOriginalLayout();
        if (!layoutCached) return;
        if (activeWarningCoroutine != null) StopCoroutine(activeWarningCoroutine);
        activeWarningCoroutine = null;
        if (recoveryButton == null)
        {
            recoveryButton = Instantiate(styleSource, warningCanvasRect, false);
            recoveryButton.name = "SpeechRecoveryRetryButton";
            recoveryButton.onClick = new Button.ButtonClickedEvent();
            var label = recoveryButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = "Retry";
        }
        recoveryButton.onClick.RemoveAllListeners();
        recoveryButton.onClick.AddListener(() => onRetry?.Invoke());
        recoveryButton.interactable = true;
        recoveryButton.gameObject.SetActive(true);
        if (!recoveryVisible)
        {
            originalInteractable = warningCanvasGroup.interactable;
            originalBlocksRaycasts = warningCanvasGroup.blocksRaycasts;
        }
        recoveryVisible = true;
        warningText.text = message;
        ResizeForMessage(message);

        var buttonRect = (RectTransform)recoveryButton.transform;
        buttonRect.anchorMin = buttonRect.anchorMax = buttonRect.pivot = new Vector2(0.5f, 0.5f);
        // Scale the copied control uniformly to fit the existing warning width.
        float scale = Mathf.Min(0.35f, warningTextRect.rect.width * 0.75f / Mathf.Max(1f, buttonRect.rect.width));
        buttonRect.localScale = Vector3.one * scale;
        // Match the text/background facing, including the existing flipped panel hierarchy.
        buttonRect.localRotation = Quaternion.Inverse(warningCanvasRect.rotation) * warningTextRect.rotation;
        float buttonHeight = buttonRect.rect.height * scale;
        float gap = 1.0f;
        float extra = buttonHeight + gap;
        warningCanvasRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, warningCanvasRect.rect.height + extra);
        // The background may be flipped relative to the canvas; move the text upward in world space.
        float textUp = Mathf.Sign(warningTextRect.parent.InverseTransformVector(Vector3.up).y);
        warningTextRect.anchoredPosition = originalTextPosition + Vector2.up * (textUp * extra * 0.5f);
        buttonRect.anchoredPosition = new Vector2(0f, -extra * 0.5f + warningTextRect.rect.height * 0.5f + gap + buttonHeight * 0.5f);
        Canvas.ForceUpdateCanvases();
        PositionInFrontOfPlayer();
        warningCanvas.enabled = true;
        warningCanvasGroup.alpha = 1f;
        warningCanvasGroup.interactable = true;
        warningCanvasGroup.blocksRaycasts = true;
    }

    public void DismissRecovery()
    {
        if (!recoveryVisible) return;
        recoveryVisible = false;
        if (recoveryButton != null) recoveryButton.gameObject.SetActive(false);
        if (warningCanvas != null) warningCanvas.enabled = false;
        if (warningCanvasGroup != null)
        {
            warningCanvasGroup.alpha = 0f;
            warningCanvasGroup.interactable = originalInteractable;
            warningCanvasGroup.blocksRaycasts = originalBlocksRaycasts;
        }
        if (warningTextRect != null) warningTextRect.anchoredPosition = originalTextPosition;
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
