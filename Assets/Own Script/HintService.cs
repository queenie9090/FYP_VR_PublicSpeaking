using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HintService : MonoBehaviour
{
    private OpenAIManager openAIManager;

    public void Initialize(OpenAIManager openAI)
    {
        openAIManager = openAI;
    }

    /// <summary>
    /// Builds context prompt, sends OpenAI hint request, and updates HintUIController.
    /// </summary>
    public void TriggerHintActivation(
        bool isSessionActive,
        ScenarioConfig activeConfig,
        List<DialogueTurn> conversationHistory,
        HintUIController hintUI,
        DialogueUIManager uiManager)
    {
        if (!isSessionActive || openAIManager == null) return;

        // 1. Show loading status on VR panel
        if (hintUI != null)
        {
            hintUI.ShowDirectMessage("Generating AI hint...");
        }

        // 2. Build context prompt from conversation history
        bool lastIsUser = conversationHistory.Count > 0 && conversationHistory[conversationHistory.Count - 1].speaker == "User";
        string lastUserScript = lastIsUser ? conversationHistory[conversationHistory.Count - 1].dialogue : "";
        string hintPrompt = PromptBuilder.BuildHintPrompt(activeConfig, conversationHistory, lastUserScript);

        // 3. Send network API request to OpenAI
        StartCoroutine(openAIManager.SendHintRequest(
            hintPrompt,
            generatedHint => {
                if (!string.IsNullOrEmpty(generatedHint))
                {
                    Debug.Log($"[AI Hint Received] {generatedHint}");

                    if (hintUI != null)
                    {
                        hintUI.SetDialogueState(true, generatedHint);
                        hintUI.ShowDirectMessage(generatedHint, isFinalResult: true);
                    }

                    if (uiManager != null) uiManager.SetStatus("Hint Generated");
                }
                else if (hintUI != null)
                {
                    hintUI.ShowDirectMessage("No hint available. Try again.");
                }
            },
            err => {
                Debug.LogError($"[Hint Error] {err}");
                if (hintUI != null) hintUI.ShowDirectMessage("Failed to generate hint.");
            }
        ));
    }
}