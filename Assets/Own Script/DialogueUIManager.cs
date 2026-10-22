using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DialogueUIManager : MonoBehaviour
{
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text conversationTimerText;
    [SerializeField] private TMP_Text dialogueText;
    [SerializeField] private ScrollRect dialogueScrollRect;
    [SerializeField] private TMP_Text hintTextDisplay;

    public void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    public void UpdateTimerDisplay(float elapsedSeconds)
    {
        if (conversationTimerText == null) return;
        int minutes = Mathf.FloorToInt(elapsedSeconds / 60.0f);
        int seconds = Mathf.FloorToInt(elapsedSeconds % 60.0f);
        conversationTimerText.text = $"{minutes:D2}:{seconds:D2}";
    }

    public void RenderDialogue(List<DialogueTurn> history)
    {
        if (dialogueText == null) return;

        string fullLog = "";
        foreach (var turn in history)
        {
            string speakerTag = turn.speaker == "User" ? "<b>You:</b>" : "<b>NPC:</b>";
            fullLog += $"{speakerTag} {turn.dialogue}\n\n";
        }

        dialogueText.text = fullLog;

        if (dialogueScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            dialogueScrollRect.verticalNormalizedPosition = 0.0f;
        }
    }

    public void ShowHint(string hintText)
    {
        if (hintTextDisplay == null) return;
        hintTextDisplay.gameObject.SetActive(true);
        hintTextDisplay.text = $"[HINT]: {hintText}";
    }

    public void HideHint()
    {
        if (hintTextDisplay != null) hintTextDisplay.gameObject.SetActive(false);
    }
}