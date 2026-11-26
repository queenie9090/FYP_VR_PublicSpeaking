using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Profiling;

public class DialogueUIManager : MonoBehaviour
{
    [Header("UI Canvas / Panel Container")]
    [Tooltip("Assign the Canvas or child Panel GameObject here (NOT the Manager GameObject)")]
    [SerializeField] private GameObject dialoguePanelContainer;

    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text conversationTimerText;
    [SerializeField] private ScrollRect dialogueScrollRect;

    [Header("Chat Bubble Prefabs")]
    [SerializeField] private Transform chatContentParent;
    [SerializeField] private GameObject npcMessagePrefab;
    [SerializeField] private GameObject userMessagePrefab;

    [Header("Turn Counter UI")]
    [SerializeField] private GameObject turnPanel;
    [SerializeField] private TMP_Text turnText;

    private int activeMaxTurns = 5;
    private readonly List<GameObject> spawnedBubbles = new List<GameObject>();
    private TMP_Text latestBubbleText;
    private bool scrollPending;
    private int lastDisplayedSecond = -1;
    private int lastDisplayedTurn = -1;
    private static readonly ProfilerMarker ScrollMarker = new ProfilerMarker("DialogueUI.ScrollLayout");

    public void ShowUI()
    {
        if (dialoguePanelContainer != null)
            dialoguePanelContainer.SetActive(true);
    }

    public void HideUI()
    {
        // Disables ONLY the visible UI Canvas/Panel, keeping all scripts active!
        if (dialoguePanelContainer != null)
            dialoguePanelContainer.SetActive(false);
    }

    public void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    public void UpdateTimerDisplay(float elapsedSeconds)
    {
        if (conversationTimerText == null) return;
        int minutes = Mathf.FloorToInt(elapsedSeconds / 60.0f);
        int seconds = Mathf.FloorToInt(elapsedSeconds % 60.0f);
        int totalSeconds = minutes * 60 + seconds;
        if (lastDisplayedSecond == totalSeconds) return;
        lastDisplayedSecond = totalSeconds;
        conversationTimerText.text = $"{minutes:D2}:{seconds:D2}";
    }

    /// <summary>
    /// Initializes the turn display configuration when a session starts.
    /// </summary>
    public void InitTurnCounter(int maxLimit)
    {
        activeMaxTurns = maxLimit;
        lastDisplayedTurn = -1;
        if (turnPanel != null) turnPanel.SetActive(true);
        UpdateTurnDisplay(1);
    }

    /// <summary>
    /// Renders conversation history while displaying partialText for the latest turn
    /// (used for word-by-word reveal synced to NPC voice playback).
    /// </summary>
    public void RenderDialogueWithPartialLatest(List<DialogueTurn> history, string partialText)
    {
        if (chatContentParent == null || history == null || history.Count == 0) return;

        while (spawnedBubbles.Count < history.Count)
        {
            SpawnBubble(history[spawnedBubbles.Count]);
        }

        if (latestBubbleText != null)
        {
            latestBubbleText.text = partialText;
        }

        int calculatedTurn = CurrentTurn(history);
        UpdateTurnDisplay(calculatedTurn);
        ScrollToBottom();
    }

    public void RenderDialogue(List<DialogueTurn> history)
    {
        if (chatContentParent == null) return;

        if (history == null || history.Count == 0)
        {
            ClearChatBubbles();
            return;
        }

        if (history.Count < spawnedBubbles.Count)
        {
            ClearChatBubbles();
        }

        for (int i = spawnedBubbles.Count; i < history.Count; i++)
        {
            SpawnBubble(history[i]);
        }

        int calculatedTurn = CurrentTurn(history);
        UpdateTurnDisplay(calculatedTurn);
        ScrollToBottom();
    }

    private int CurrentTurn(List<DialogueTurn> history)
    {
        int users = 0, explicitTurn = 0;
        foreach (var entry in history)
        {
            if (entry == null) continue;
            if (entry.speaker == "User") users++;
            explicitTurn = Mathf.Max(explicitTurn, entry.turnNumber);
        }
        return Mathf.Max(1, Mathf.Max(users, explicitTurn));
    }

    private void SpawnBubble(DialogueTurn turn)
    {
        bool isUser = turn.speaker == "User";
        GameObject prefab = isUser ? userMessagePrefab : npcMessagePrefab;
        if (prefab == null) return;

        GameObject bubbleObj = Instantiate(prefab, chatContentParent);
        spawnedBubbles.Add(bubbleObj);

        TMP_Text speakerText = FindChildText(bubbleObj.transform, "SpeakerNameText");
        if (speakerText != null) speakerText.text = isUser ? "" : turn.speaker;

        TMP_Text bodyText = FindChildText(bubbleObj.transform, "DialogueText");
        if (bodyText != null)
        {
            bodyText.text = turn.dialogue;
            latestBubbleText = bodyText;
        }
    }

    private TMP_Text FindChildText(Transform root, string childName)
    {
        foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (tmp.name == childName) return tmp;
        }
        return null;
    }

    private void ClearChatBubbles()
    {
        foreach (var bubble in spawnedBubbles)
        {
            if (bubble != null) Destroy(bubble);
        }
        spawnedBubbles.Clear();
        latestBubbleText = null;
    }

    private void ScrollToBottom()
    {
        if (dialogueScrollRect != null) scrollPending = true;
    }

    private void LateUpdate()
    {
        if (scrollPending && dialogueScrollRect != null && chatContentParent != null)
        {
            scrollPending = false;
            using (ScrollMarker.Auto())
            {
                Canvas.ForceUpdateCanvases();
                dialogueScrollRect.verticalNormalizedPosition = 0.0f;
            }
        }
    }

    public void UpdateTurnDisplay(int turnNumber)
    {
        if (turnText != null)
        {
            int displayTurn = Mathf.Min(turnNumber, activeMaxTurns);
            if (lastDisplayedTurn == displayTurn) return;
            lastDisplayedTurn = displayTurn;
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
