using UnityEngine;

public class UIFadeOut : MonoBehaviour
{
    [Header("Fade Settings")]
    public float fadeDelay = 10f;       // Wait time before fading starts
    public float fadeDuration = 2f;     // Duration of the fade-out

    private CanvasGroup canvasGroup;
    private bool isFading = false;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void StartFadeOut()
    {
        if (!isFading)
            StartCoroutine(FadeOutRoutine());
    }

    private System.Collections.IEnumerator FadeOutRoutine()
    {
        isFading = true;

        // wait before starting fade
        yield return new WaitForSeconds(fadeDelay);

        float elapsed = 0f;
        float startAlpha = canvasGroup.alpha;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / fadeDuration);
            yield return null;
        }

        canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
    }
}
