using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TutorialPanelManager : MonoBehaviour
{
    [Header("Tutorial Containers")]
    [SerializeField] private GameObject handTutorialContainer;
    [SerializeField] private GameObject controllerTutorialPanel;

    [Header("Hand Tutorial Steps")]
    [Tooltip("Assign the parent panel GameObject for each gesture step, PLUS your final completion panel at the very end.")]
    [SerializeField] private GameObject[] handStepPanels;

    [Tooltip("Gesture names matching your gesture tracker. Leave the final completion step slot blank or empty!")]
    [SerializeField] private string[] requiredGestureNames;

    private int currentHandStepIndex = 0;
    private bool[] completedSteps;

    [Header("Shared UI References")]
    [SerializeField] private TextMeshProUGUI handProgressText;
    [SerializeField] private TextMeshProUGUI handStatusText;
    [SerializeField] private Button handPrevButton;
    [SerializeField] private Button handCloseButton;
    [SerializeField] private Button completionDoneButton; // Final Panel "Done" button
    
    [Header("Interactive Test Button")]
    [SerializeField] private Button testPinchButton;

    [Header("Status Panel")]
    [SerializeField] private GameObject statusPanelContainer;
    [SerializeField] private Image statusIconImage;
    [SerializeField] private Sprite defaultIcon;
    [SerializeField] private Sprite successIcon;

    [Header("Status Messages")]
    [SerializeField]
    private string defaultMessage =
        "Waiting for gesture... Move your hand into the correct position and try again.";

    [SerializeField]
    private string successMessage =
        "Correct! Well done! Moving to the next step...";

    [SerializeField]
    private string completedMessage =
        "Tutorial completed! Well done!";

    [Header("Success Feedback")]
    [SerializeField] private AudioSource successAudioSource;
    [SerializeField] private AudioClip successSound;
    [SerializeField] private float advanceDelay = 1.5f;

    [Header("External Managers")]
    [SerializeField] private InteractionManager interactionManager;

    private InputMode activeMode = InputMode.Controller;
    private Coroutine advanceCoroutine;
    private bool tutorialFinished = false;

    private void Start()
    {
        if (handCloseButton != null)
            handCloseButton.onClick.AddListener(CloseTutorial);

        if (handPrevButton != null)
            handPrevButton.onClick.AddListener(PreviousHandStep);

        if (completionDoneButton != null)
            completionDoneButton.onClick.AddListener(CloseTutorial);

        if (interactionManager == null)
            interactionManager = FindObjectOfType<InteractionManager>();
    }

    private void OnDestroy()
    {
        if (handCloseButton != null)
            handCloseButton.onClick.RemoveListener(CloseTutorial);

        if (handPrevButton != null)
            handPrevButton.onClick.RemoveListener(PreviousHandStep);

        if (completionDoneButton != null)
            completionDoneButton.onClick.RemoveListener(CloseTutorial);
    }

    public void InitializeForInputMode(InputMode mode)
    {
        activeMode = mode;

        if (interactionManager != null)
        {
            interactionManager.SetLocomotionEnabled(false);
        }

        if (activeMode == InputMode.Controller)
        {
            if (handTutorialContainer != null)
                handTutorialContainer.SetActive(false);

            if (controllerTutorialPanel != null)
                controllerTutorialPanel.SetActive(true);
        }
        else if (activeMode == InputMode.HandTracking)
        {
            if (controllerTutorialPanel != null)
                controllerTutorialPanel.SetActive(false);

            if (handTutorialContainer != null)
                handTutorialContainer.SetActive(true);

            ResetHandTutorial();
        }
    }

    private void ResetHandTutorial()
    {
        currentHandStepIndex = 0;
        tutorialFinished = false;

        if (advanceCoroutine != null)
        {
            StopCoroutine(advanceCoroutine);
            advanceCoroutine = null;
        }

        if (handStepPanels == null || handStepPanels.Length == 0)
        {
            Debug.LogWarning("[TutorialPanelManager] No hand tutorial step panels assigned!");
            return;
        }

        completedSteps = new bool[handStepPanels.Length];

        UpdateHandStepUI();
    }

    private void UpdateHandStepUI()
    {
        if (handStepPanels == null || handStepPanels.Length == 0)
            return;

        // Check if we reached the final completion panel
        bool isFinalCompletionStep = (currentHandStepIndex == handStepPanels.Length - 1);

        // Show only the current active step panel
        for (int i = 0; i < handStepPanels.Length; i++)
        {
            if (handStepPanels[i] != null)
            {
                handStepPanels[i].SetActive(i == currentHandStepIndex);
            }
        }

        // Update step counter text (excludes final completion panel from total count)
        if (handProgressText != null)
        {
            handProgressText.text = isFinalCompletionStep
                ? "Complete!"
                : $"Step {currentHandStepIndex + 1} / {handStepPanels.Length - 1}";
        }

        // Hide status panel bar when viewing the final completion panel
        if (isFinalCompletionStep)
        {
            if (statusPanelContainer != null)
                statusPanelContainer.SetActive(false);
        }
        else
        {
            SetDefaultStatus();
        }

        // Enable Previous button only when the previous step was completed
        if (handPrevButton != null)
        {
            bool previousStepCompleted =
                currentHandStepIndex > 0 &&
                completedSteps != null &&
                completedSteps[currentHandStepIndex - 1];

            handPrevButton.interactable = previousStepCompleted;
        }
    }

    private void SetDefaultStatus()
    {
        if (statusPanelContainer != null)
            statusPanelContainer.SetActive(true);

        if (handStatusText != null)
            handStatusText.text = defaultMessage;

        if (statusIconImage != null)
        {
            statusIconImage.gameObject.SetActive(true);

            if (defaultIcon != null)
                statusIconImage.sprite = defaultIcon;
        }
    }

    private void SetSuccessStatus()
    {
        if (statusPanelContainer != null)
            statusPanelContainer.SetActive(true);

        if (handStatusText != null)
            handStatusText.text = successMessage;

        if (statusIconImage != null)
        {
            statusIconImage.gameObject.SetActive(true);

            if (successIcon != null)
                statusIconImage.sprite = successIcon;
        }
    }

    public void OnGesturePerformed(string gestureType)
    {
        string required = (requiredGestureNames != null && currentHandStepIndex < requiredGestureNames.Length)
       ? requiredGestureNames[currentHandStepIndex] : "<none>";
        Debug.Log($"[Tutorial] Got '{gestureType}' | required '{required}' | step {currentHandStepIndex} | mode {activeMode} | advancing {advanceCoroutine != null} | finished {tutorialFinished}");

        if (activeMode != InputMode.HandTracking || advanceCoroutine != null || tutorialFinished)
            return;

        if (handStepPanels == null || currentHandStepIndex >= handStepPanels.Length - 1)
            return; // Don't process gesture triggers on the final completion panel

        if (requiredGestureNames == null || currentHandStepIndex >= requiredGestureNames.Length)
            return;

        string requiredGesture = requiredGestureNames[currentHandStepIndex];

        if (string.IsNullOrEmpty(gestureType) || string.IsNullOrEmpty(requiredGesture))
            return;

        if (string.Equals(gestureType.Trim(), requiredGesture.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            completedSteps[currentHandStepIndex] = true;

            SetSuccessStatus();

            if (successAudioSource != null && successSound != null)
            {
                successAudioSource.PlayOneShot(successSound);
            }

            advanceCoroutine = StartCoroutine(AdvanceAfterSuccess());
        }
    }

    private IEnumerator AdvanceAfterSuccess()
    {
        yield return new WaitForSecondsRealtime(advanceDelay);

        if (currentHandStepIndex < handStepPanels.Length - 1)
        {
            currentHandStepIndex++;
            UpdateHandStepUI();

            // Mark finished if advancing onto the final completion panel
            if (currentHandStepIndex == handStepPanels.Length - 1)
            {
                tutorialFinished = true;
            }
        }

        advanceCoroutine = null;
    }

    public void PreviousHandStep()
    {
        if (currentHandStepIndex <= 0)
            return;

        if (completedSteps == null || !completedSteps[currentHandStepIndex - 1])
            return;

        if (advanceCoroutine != null)
        {
            StopCoroutine(advanceCoroutine);
            advanceCoroutine = null;
        }

        tutorialFinished = false;
        currentHandStepIndex--;
        UpdateHandStepUI();
    }

    public void CloseTutorial()
    {
        if (advanceCoroutine != null)
        {
            StopCoroutine(advanceCoroutine);
            advanceCoroutine = null;
        }

        if (interactionManager != null)
        {
            interactionManager.SetLocomotionEnabled(true);
        }

        // Hide both tutorial UI containers explicitly
        if (controllerTutorialPanel != null)
        {
            controllerTutorialPanel.SetActive(false);
        }
    
        if (handTutorialContainer != null)
        {
            handTutorialContainer.SetActive(false);
        }
    }

    public void OnTestPinchButtonPressed()
    {
        Debug.Log("[Tutorial] 'Press Here!' button pressed via UI Pinch interactor!");

        // Trigger the exact same success flow as physical gesture recognition
        OnGesturePerformed("Pinch");
    }
}