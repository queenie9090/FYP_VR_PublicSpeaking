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

    [Header("Turn Counter UI")]
    [SerializeField] private GameObject turnPanel;
    [SerializeField] private TMP_Text turnText;

    private int activeMaxTurns = 5;

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

    /// <summary>
    /// Initializes the turn display configuration when a session starts.
    /// </summary>
    public void InitTurnCounter(int maxLimit)
    {
        activeMaxTurns = maxLimit;
        if (turnPanel != null) turnPanel.SetActive(true);
        UpdateTurnDisplay(1);
    }

    /// <summary>
    /// Renders dialogue log and automatically computes current turn from conversation history.
    /// </summary>
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

        // Compute current turn count from history (2 turns = 1 full conversation exchange)
        if (history != null && history.Count > 0)
        {
            int calculatedTurn = (history.Count / 2) + 1;
            UpdateTurnDisplay(calculatedTurn);
        }

        if (dialogueScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            dialogueScrollRect.verticalNormalizedPosition = 0.0f;
        }
    }

    public void UpdateTurnDisplay(int turnNumber)
    {
        if (turnText != null)
        {
            int displayTurn = Mathf.Min(turnNumber, activeMaxTurns);
            turnText.text = $"Turn {displayTurn} / {activeMaxTurns}";
        }
    }

    public void HideTurnCounter()
    {
        if (turnPanel != null)
        {
            turnPanel.SetActive(false);
        }
    }
}