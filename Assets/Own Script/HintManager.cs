using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public enum InputMode
{
    Controller,
    HandTracking
}

public class HintManager : MonoBehaviour
{
    [Header("Input Mode Settings")]
    [SerializeField] private InputMode currentInputMode = InputMode.Controller;

    [Header("Controller Hint UI")]
    [SerializeField] private GameObject controllerHintCanvas;
    [SerializeField] private TextMeshProUGUI controllerHintText;
    // Assign 'XRI LeftHand Interaction/SecondaryButton' (Y Button)
    [SerializeField] private InputActionProperty controllerHintAction;

    [Header("Hand Tracking Hint UI")]
    [SerializeField] private GameObject handTrackingHintCanvas;
    [SerializeField] private TextMeshProUGUI handTrackingHintText;

    [Header("Manager References")]
    [SerializeField] private InteractionManager interactionManager;

    [Header("State Tracking")]
    private bool isDialogueActive = false;
    private string currentDialogueHint = "";

    private void Start()
    {
        HideAllCanvases();
        SyncInputModeFromUserSession();

        if (interactionManager == null)
            interactionManager = FindObjectOfType<InteractionManager>();
    }

    private void OnEnable()
    {
        if (controllerHintAction.action != null)
        {
            controllerHintAction.action.Enable();
            controllerHintAction.action.performed += OnControllerHintPressed;
        }
    }

    private void OnDisable()
    {
        if (controllerHintAction.action != null)
        {
            controllerHintAction.action.performed -= OnControllerHintPressed;
            controllerHintAction.action.Disable();
        }
    }

    // -------------------------------------------------------------
    // Input Entry Points
    // -------------------------------------------------------------

    // Triggered via Y Button on Controller
    private void OnControllerHintPressed(InputAction.CallbackContext context)
    {
        if (currentInputMode != InputMode.Controller) return;
        ProcessHintTrigger();
    }

    // Triggered via Open Palm (> 1.0s) from GestureTracker / InteractionManager
    public void OnOpenPalmGestureTriggered()
    {
        if (currentInputMode != InputMode.HandTracking) return;
        ProcessHintTrigger();
    }

    /// <summary>
    /// Unified hint activation pipeline for both Controller and Hand Tracking modes.
    /// </summary>
    private void ProcessHintTrigger()
    {
        GameObject activeCanvas = (currentInputMode == InputMode.Controller) ? controllerHintCanvas : handTrackingHintCanvas;

        // 1. If canvas is ALREADY showing -> Toggle OFF
        if (activeCanvas != null && activeCanvas.activeSelf)
        {
            activeCanvas.SetActive(false);
            return;
        }

        // 2. If canvas is HIDDEN and session active -> Fetch live AI coaching hint
        if (isDialogueActive)
        {
            if (interactionManager != null)
            {
                interactionManager.TriggerHintActivation();
            }
            else
            {
                ToggleHintDisplay(); // Fallback if InteractionManager missing
            }
        }
        else
        {
            // If session is ended or inactive, display standard notice/cached hint
            ToggleHintDisplay();
        }
    }

    // -------------------------------------------------------------
    // Display Logic & State Management
    // -------------------------------------------------------------

    public void ToggleHintDisplay()
    {
        GameObject activeCanvas = (currentInputMode == InputMode.Controller) ? controllerHintCanvas : handTrackingHintCanvas;
        TextMeshProUGUI activeText = (currentInputMode == InputMode.Controller) ? controllerHintText : handTrackingHintText;

        if (activeCanvas == null || activeText == null)
        {
            Debug.LogError("[HintManager] Active Canvas or Text UI reference is missing in the Inspector!");
            return;
        }

        if (activeCanvas.activeSelf)
        {
            activeCanvas.SetActive(false);
            return;
        }

        HideAllCanvases();

        if (isDialogueActive)
        {
            if (!string.IsNullOrEmpty(currentDialogueHint))
            {
                activeText.text = currentDialogueHint;
            }
            else
            {
                activeText.text = "Press Y (or hold Open Palm) to request a dialogue hint.";
            }
        }
        else
        {
            activeText.text = "Hints are only available during active dialogue scenarios.";
        }

        activeCanvas.SetActive(true);
        Debug.Log($"[HintManager] Displaying hint under {currentInputMode} mode.");
    }

    public void ShowDirectMessage(string message)
    {
        GameObject activeCanvas = (currentInputMode == InputMode.Controller) ? controllerHintCanvas : handTrackingHintCanvas;
        TextMeshProUGUI activeText = (currentInputMode == InputMode.Controller) ? controllerHintText : handTrackingHintText;

        if (activeCanvas == null || activeText == null) return;

        HideAllCanvases();

        // Set text directly without cutting off sentences
        activeText.text = message;
        activeCanvas.SetActive(true);
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

        Debug.Log($"[HintManager] Initialized Input Mode: {currentInputMode} (from UserSession: '{modeStr}')");
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
    /// Hides any visible hint popups when the pause menu opens or session completes.
    /// </summary>
    public void HideHintCanvas()
    {
        HideAllCanvases();
    }

    private void HideAllCanvases()
    {
        if (controllerHintCanvas != null) controllerHintCanvas.SetActive(false);
        if (handTrackingHintCanvas != null) handTrackingHintCanvas.SetActive(false);
    }
}