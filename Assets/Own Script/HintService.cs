using System.Collections.Generic;
using UnityEngine;

public class HintService : MonoBehaviour
{
    private OpenAIManager openAIManager;
    private int hintRequestVersion;

    public void Initialize(OpenAIManager openAI)
    {
        openAIManager = openAI;
    }

    public void CancelPendingHints()
    {
        hintRequestVersion++;
    }

    /// <summary>
    /// Builds context prompt, sends OpenAI hint request, and updates HintUIController.
    /// When isBackgroundPrefetch is true, caches the result silently without changing status bar text.
    /// </summary>
    public void TriggerHintActivation(
        bool isSessionActive,
        ScenarioConfig activeConfig,
        List<DialogueTurn> conversationHistory,
        HintUIController hintUI,
        DialogueUIManager uiManager,
        bool isBackgroundPrefetch = false)
    {
        if (!isSessionActive || openAIManager == null) return;

        int requestVersion = ++hintRequestVersion;

        if (hintUI != null)
        {
            hintUI.SetHintPrefetching(true);
            if (!isBackgroundPrefetch)
            {
                hintUI.ShowDirectMessage("Generating AI hint...");
            }
        }

        bool lastIsUser = conversationHistory.Count > 0 && conversationHistory[conversationHistory.Count - 1].speaker == "User";
        string lastUserScript = lastIsUser ? conversationHistory[conversationHistory.Count - 1].dialogue : "";
        string hintPrompt = PromptBuilder.BuildHintPrompt(activeConfig, conversationHistory, lastUserScript);

        StartCoroutine(openAIManager.SendHintRequest(
            hintPrompt,
            generatedHint => {
                if (this == null || requestVersion != hintRequestVersion) return;

                if (!string.IsNullOrEmpty(generatedHint))
                {
                    Debug.Log($"[AI Hint {(isBackgroundPrefetch ? "Prefetched" : "Received")}] {generatedHint}");

                    if (hintUI != null)
                    {
                        hintUI.ShowDirectMessage(generatedHint, isFinalResult: true);
                    }

                    if (!isBackgroundPrefetch && uiManager != null)
                    {
                        uiManager.SetStatus("Hint Generated");
                    }
                }
                else if (hintUI != null)
                {
                    hintUI.SetHintPrefetching(false);
                    if (!isBackgroundPrefetch)
                        hintUI.ShowDirectMessage("No hint available. Try again.");
                }
            },
            err => {
                if (this == null || requestVersion != hintRequestVersion) return;
                Debug.LogError($"[Hint Error] {err}");
                if (hintUI != null)
                {
                    hintUI.SetHintPrefetching(false);
                    if (!isBackgroundPrefetch)
                        hintUI.ShowDirectMessage("Failed to generate hint.");
                }
            }
        ));
    }
}