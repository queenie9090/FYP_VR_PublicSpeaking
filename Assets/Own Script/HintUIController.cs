using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public enum InputMode
{
    Controller,
    HandTracking
}

public class HintUIController : MonoBehaviour
{
    [Header("Input Mode Settings")]
    [SerializeField] private InputMode currentInputMode = InputMode.Controller;

    [Header("Unified World-Space Hint UI")]
    [Tooltip("Assign the single top-level 3D Handle GameObject")]
    [SerializeField] private GameObject hintPanelRoot;
    [Tooltip("Assign the TextMeshProUGUI child object inside the panel canvas")]
    [SerializeField] private TextMeshProUGUI hintText;

    [Header("Positioning Settings")]
    [Tooltip("Assign Main Camera (XR Camera / CenterEyeAnchor)")]
    [SerializeField] private Transform playerCamera;
    [SerializeField] private float spawnDistance = 1.2f;

    [Header("Audio Feedback")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip hintRequestedClip;
    [SerializeField] private AudioClip hintReceivedClip;
    [SerializeField] private AudioClip hintCloseClip;

    [Header("Manager References")]
    [SerializeField] private InteractionManager interactionManager;

    [Header("State Tracking")]
    private bool isDialogueActive = false;
    private string currentDialogueHint = "";
    private float lastGestureToggle = 0f;
    private int hintTriggerCount = 0;

    public void ResetHintCount() => hintTriggerCount = 0;
    public int GetTotalHintCount() => hintTriggerCount;

    private void Update()
    {
        if (currentInputMode != InputMode.Controller) return;

        var l = UnityEngine.InputSystem.XR.XRController.leftHand;
        var r = UnityEngine.InputSystem.XR.XRController.rightHand;

        var lb = l?.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("secondaryButton");
        var rb = r?.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("secondaryButton");

        bool pressed = (lb != null && lb.wasPressedThisFrame) ||
                       (rb != null && rb.wasPressedThisFrame);

        if (pressed)
        {
            Debug.Log($"[hintUIController] Secondary pressed | DialogueActive: {isDialogueActive}");
            ProcessHintTrigger();
        }
    }

    private void Start()
    {
        HideAllCanvases();
        SyncInputModeFromUserSession();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();

        if (interactionManager == null)
            interactionManager = FindObjectOfType<InteractionManager>();

        if (playerCamera == null && Camera.main != null)
        {
            playerCamera = Camera.main.transform;
        }
    }

    private void OnEnable()
    {
        Debug.Log("[hintUIController] OnEnable");
    }

    private void OnDisable()
    {
        Debug.Log("[hintUIController] OnDisable");
    }

    private void OnControllerHintPressed(InputAction.CallbackContext context)
    {
        Debug.Log($"[hintUIController] PRESSED: {context.control.path} | Mode: {currentInputMode} | DialogueActive: {isDialogueActive}");
        if (currentInputMode != InputMode.Controller) return;
        ProcessHintTrigger();
    }

    // -------------------------------------------------------------
    // Hand Tracking Gesture Callbacks (Have a Question & Thumbs Down)
    // -------------------------------------------------------------

    /// <summary>
    /// Triggered via "Have a Question" gesture (Index Up). Toggles panel display.
    /// </summary>
    public void OnHaveAQuestionGestureTriggered()
    {
        Debug.Log($"[hintUIController] HaveAQuestion gesture received. Mode = {currentInputMode}");
        if (currentInputMode != InputMode.HandTracking) return;
        if (Time.time - lastGestureToggle < 1.0f) return;
       
        lastGestureToggle = Time.time;
        Debug.Log("<color=green>[hintUIController]</color> OnHaveAQuestionGestureTriggered received. Processing toggle...");
        ProcessHintTrigger();
    }

    /// <summary>
    /// Triggered via "Thumbs Down" gesture to explicitly close the panel.
    /// </summary>
    public void OnThumbsDownGestureTriggered()
    {
        if (currentInputMode != InputMode.HandTracking) return;

        if (hintPanelRoot != null && hintPanelRoot.activeSelf)
        {
            Debug.Log("<color=orange>[hintUIController]</color> OnThumbsDownGestureTriggered received. Hiding 3D panel...");
            hintPanelRoot.SetActive(false);
        }
    }

    /// <summary>
    /// Unified hint activation pipeline for both Controller and Hand Tracking modes.
    /// </summary>
    private void ProcessHintTrigger()
    {
        if (audioSource != null && hintRequestedClip != null)
            audioSource.PlayOneShot(hintRequestedClip);

        if (hintPanelRoot == null || hintText == null) return;

        // 1. If panel is ALREADY open, toggle it OFF
        if (hintPanelRoot.activeSelf)
        {
            hintPanelRoot.SetActive(false);
            return;
        }

        // 2. Position the panel in front of the headset and turn it ON
        PositionInFrontOfPlayer(hintPanelRoot);
        hintPanelRoot.SetActive(true);

        // 3. Set text based on dialogue state
        if (isDialogueActive && interactionManager != null)
        {
            hintText.text = "Generating AI hint...";
            hintTriggerCount++;
            interactionManager.TriggerHintActivation();
        }
        else
        {
            // Allowed before session starts!
            hintText.text = "Hint panel ready. Hints will be generated once you start the conversation.";
        }
    }

    public void ToggleHintDisplay(bool reposition = true)
    {
        if (hintPanelRoot == null || hintText == null)
        {
            Debug.LogError("[hintUIController] Hint Panel Root or Text UI reference missing in Inspector!");
            return;
        }

        if (hintPanelRoot.activeSelf)
        {
            hintPanelRoot.SetActive(false);
            return;
        }

        //HideAllCanvases();

        if (isDialogueActive)
        {
            hintText.text = !string.IsNullOrEmpty(currentDialogueHint)
                ? currentDialogueHint
                : "Press Y/B or raise your index finger to request a dialogue hint.";
        }
        else
        {
            hintText.text = "Hints are only available during active dialogue scenarios.";
        }

        // Position panel at eye level before opening
        if (reposition)
        {
            PositionInFrontOfPlayer(hintPanelRoot);
        }
        hintPanelRoot.SetActive(true);
    }

    public void ShowDirectMessage(string message, bool isFinalResult = false)
    {
        if (isFinalResult && audioSource != null && hintReceivedClip != null)
            audioSource.PlayOneShot(hintReceivedClip);

        if (hintPanelRoot == null || hintText == null) return;

        hintText.text = message;

        // Only force open if this is the final AI hint result arriving from OpenAI
        if (isFinalResult && !hintPanelRoot.activeSelf)
        {
            PositionInFrontOfPlayer(hintPanelRoot);
            hintPanelRoot.SetActive(true);
        }
    }

    public void SyncInputModeFromUserSession()
    {
        string modeStr = UserSession.InputMode;

        if (!string.IsNullOrEmpty(modeStr) && (modeStr.Contains("Hand") || modeStr.Contains("Hands")))
        {
            SetInputMode(InputMode.HandTracking);
        }
        else
        {
            SetInputMode(InputMode.Controller);
        }

        Debug.Log($"[hintUIController] Initialized Input Mode: {currentInputMode} (from UserSession: '{modeStr}')");
    }

    public void SetInputMode(InputMode newMode)
    {
        currentInputMode = newMode;
        HideAllCanvases();
    }

    public void SetDialogueState(bool active, string activeHint = "")
    {
        isDialogueActive = active;
        currentDialogueHint = activeHint;
    }

    /// <summary>
    /// Hides visible hint popup when the pause menu opens or session completes.
    /// </summary>
    public void HideHintCanvas()
    {
        if (hintPanelRoot != null && hintPanelRoot.activeSelf)
        {
            hintPanelRoot.SetActive(false);
            if (audioSource != null && hintCloseClip != null)
            {
                audioSource.PlayOneShot(hintCloseClip);
            }
        }
    }

    private void HideAllCanvases()
    {
        if (hintPanelRoot != null) hintPanelRoot.SetActive(false);

    }

    /// <summary>
    /// Positions the root handle object directly in front of the player's eye level facing toward them.
    /// </summary>
    private void PositionInFrontOfPlayer(GameObject targetRoot)
    {
        if (playerCamera == null || targetRoot == null) return;

        Vector3 forwardDir = playerCamera.forward;
        forwardDir.y = 0; // Keep horizontal orientation flat
        forwardDir.Normalize();

        Vector3 targetPosition = playerCamera.position + (forwardDir * spawnDistance);
        targetPosition.y = playerCamera.position.y; // Match eye height

        targetRoot.transform.position = targetPosition;
        targetRoot.transform.rotation = Quaternion.LookRotation(forwardDir);
    }

    /// <summary>
    /// Called by InteractionManager at the start of a new turn.
    /// If the panel is currently open in VR space, it automatically requests a fresh AI hint.
    /// </summary>
    public void OnNewTurnStarted()
    {
        // Clear stored text from the previous turn
        currentDialogueHint = "";

        if (hintPanelRoot != null && hintPanelRoot.activeSelf && isDialogueActive)
        {
            if (interactionManager != null)
            {
                hintTriggerCount++;
                interactionManager.TriggerHintActivation();
            }
        }
    }

}