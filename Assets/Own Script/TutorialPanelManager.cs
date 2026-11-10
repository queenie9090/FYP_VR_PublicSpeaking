using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class TutorialPanelManager : MonoBehaviour
{
    public static TutorialPanelManager Instance { get; private set; }

    [Header("Tutorial Containers")]
    [SerializeField] private GameObject handTutorialContainer;
    [SerializeField] private GameObject controllerTutorialPanel;
    [SerializeField] private GameObject roomTeleportAnchors;

    [Header("Hand Tutorial Step Panels")]
    [Tooltip("Assign each step panel in order: 0=UI Pinch, 1=Teleport, 2=Pinch Grab, 3=Hint Gesture, 4=Completion")]
    [SerializeField] private GameObject[] handStepPanels;

    [Tooltip("Assign parent World-Space Canvas GameObjects if steps live on separate canvases around the room.")]
    [SerializeField] private GameObject[] parentWorldSpaceCanvases;

    [Tooltip("Assign the status TMP_Text component located on EACH panel in matching order (0 to 4).")]
    [SerializeField] private TMP_Text[] handStatusTexts;

    [Header("Teleport Step Setup")]
    [SerializeField] private GameObject greenWaypointAnchor;
    private TeleportationAnchor waypointTeleportAnchor;
    [SerializeField] private Transform spawnPointTransform;
    [SerializeField] private MonoBehaviour anchorOutlineScript;
    private bool isStepTransitioning = false;

    [Header("Grab Step Setup")]
    [SerializeField] private XRBaseInteractable[] grabTargets;

    private int currentHandStepIndex = 0;
    private bool[] completedSteps;
    private bool hintPanelOpenedInTutorial = false; // Tracks 2-stage gesture progress

    [Header("Consolidated UI References")]
    [SerializeField] private Button handCloseButton;
    [SerializeField] private Button completionDoneButton;

    [Header("Status Messages")]
    [Tooltip("Custom instruction for each step (0=UI Pinch, 1=Teleport, 2=Pinch Grab, 3=Hint Gesture, 4=Completion)")]
    [SerializeField]
    private string[] defaultStepMessages = new string[]
    {
        "Please click the button below using <color=#FFD700><b>UI Pinch</b></color> to try it out!",
        "Pinch and point your ray at the <color=#55FF55><b>green waypoint anchor</b></color> to teleport.",
        "Reach out and <color=#FFD700><b>pinch-grab</b></color> any object on the desk.",
        "Raise your right index finger <color=#FFD700><b>(Have a Question)</b></color> to open the AI hint panel.",
        "You may proceed to the next step. Move to the <color=#55FF55><b>green waypoint anchor</b></color> at the desk to begin the <color=#00E5FF><b>NPC Dialogue Tutorial</b></color>."
    };

    [SerializeField] private string hintOpenedMessage = "<color=#81C784><b>Great job!</b></color> Now make a <color=#FFD700><b>Thumbs Down</b></color> gesture to close the panel.";
    [SerializeField] private string successMessage = "<color=#81C784><b>Correct! Well done! Moving to next step...</b></color>";
    [SerializeField] private string warningMessage = "<color=#FF5555><b>ACCESS RESTRICTED:</b></color> Please teleport to the <color=#55FF55>green waypoint anchor</color>.";

    [Header("Success Feedback")]
    [SerializeField] private AudioSource successAudioSource;
    [SerializeField] private AudioClip successSound;
    [SerializeField] private float advanceDelay = 1.5f;

    [Header("External Managers")]
    [SerializeField] private InteractionManager interactionManager;

    private InputMode activeMode = InputMode.Controller;
    private Coroutine advanceCoroutine;
    private bool tutorialFinished = false;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (grabTargets != null)
            foreach (var g in grabTargets)
                if (g != null) g.selectEntered.AddListener(OnObjectGrabbed);

        if (greenWaypointAnchor != null)
        {
            waypointTeleportAnchor = greenWaypointAnchor.GetComponentInChildren<TeleportationAnchor>(true);
            if (waypointTeleportAnchor != null)
                waypointTeleportAnchor.teleporting.AddListener(OnWaypointTeleported);
        }

        if (handCloseButton != null)
            handCloseButton.onClick.AddListener(CloseTutorial);

        if (completionDoneButton != null)
            completionDoneButton.onClick.AddListener(CloseTutorial);

        if (interactionManager == null)
            interactionManager = FindObjectOfType<InteractionManager>();
    }

    private void OnDestroy()
    {
        if (grabTargets != null)
            foreach (var g in grabTargets)
                if (g != null) g.selectEntered.RemoveListener(OnObjectGrabbed);

        if (waypointTeleportAnchor != null)
            waypointTeleportAnchor.teleporting.RemoveListener(OnWaypointTeleported);

        if (handCloseButton != null)
            handCloseButton.onClick.RemoveListener(CloseTutorial);

        if (completionDoneButton != null)
            completionDoneButton.onClick.RemoveListener(CloseTutorial);
    }

    private void OnObjectGrabbed(SelectEnterEventArgs args)
    {
        if (activeMode != InputMode.HandTracking || tutorialFinished) return;
        if (currentHandStepIndex == 2) TriggerStepSuccess();
    }

    private void OnWaypointTeleported(TeleportingEventArgs args)
    {
        ValidateTeleportTarget(greenWaypointAnchor);
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
            if (handTutorialContainer != null) handTutorialContainer.SetActive(false);
            if (controllerTutorialPanel != null) controllerTutorialPanel.SetActive(true);

            if (greenWaypointAnchor != null) greenWaypointAnchor.SetActive(false);
            if (anchorOutlineScript != null) anchorOutlineScript.enabled = false;
        }
        else if (activeMode == InputMode.HandTracking)
        {
            if (controllerTutorialPanel != null) controllerTutorialPanel.SetActive(false);
            if (handTutorialContainer != null) handTutorialContainer.SetActive(true);

            ResetHandTutorial();
        }
    }

    private void ResetHandTutorial()
    {
        currentHandStepIndex = 0;
        hintPanelOpenedInTutorial = false;
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
        if (handStepPanels == null || handStepPanels.Length == 0) return;

        if (parentWorldSpaceCanvases != null)
        {
            foreach (var canvasObj in parentWorldSpaceCanvases)
            {
                if (canvasObj != null && canvasObj != handTutorialContainer)
                    canvasObj.SetActive(false);
            }
        }

        if (handTutorialContainer != null) handTutorialContainer.SetActive(true);

        if (parentWorldSpaceCanvases != null &&
            currentHandStepIndex < parentWorldSpaceCanvases.Length &&
            parentWorldSpaceCanvases[currentHandStepIndex] != null)
        {
            Transform t = parentWorldSpaceCanvases[currentHandStepIndex].transform;
            while (t != null && (handTutorialContainer == null || t != handTutorialContainer.transform.parent))
            {
                t.gameObject.SetActive(true);
                t = t.parent;
            }
        }

        for (int i = 0; i < handStepPanels.Length; i++)
        {
            if (handStepPanels[i] != null)
                handStepPanels[i].SetActive(i == currentHandStepIndex);
        }

        bool isTeleportStep = (activeMode == InputMode.HandTracking) && (currentHandStepIndex == 1);

        if (greenWaypointAnchor != null) greenWaypointAnchor.SetActive(isTeleportStep);
        if (anchorOutlineScript != null) anchorOutlineScript.enabled = isTeleportStep;

        if (isTeleportStep && interactionManager != null)
            interactionManager.SetLocomotionEnabled(true);

        SetDefaultStatus();
    }

    private TMP_Text GetActiveStatusText()
    {
        if (handStatusTexts != null && currentHandStepIndex < handStatusTexts.Length)
        {
            return handStatusTexts[currentHandStepIndex];
        }
        return null;
    }

    private void SetDefaultStatus()
    {
        TMP_Text activeText = GetActiveStatusText();
        if (activeText != null && defaultStepMessages != null && currentHandStepIndex < defaultStepMessages.Length)
        {
            activeText.text = defaultStepMessages[currentHandStepIndex];
        }
    }

    private void SetSuccessStatus()
    {
        TMP_Text activeText = GetActiveStatusText();
        if (activeText != null)
        {
            activeText.text = successMessage;
        }
    }

    /// <summary>
    /// Called by GestureTracker when global gestures are recognized.
    /// Handles 2-stage gesture progression (Open Panel -> Close Panel).
    /// </summary>
    public void OnGesturePerformed(string gestureType)
    {
        if (activeMode != InputMode.HandTracking || advanceCoroutine != null || tutorialFinished)
            return;

        if (currentHandStepIndex == 3)
        {
            // Stage 1: User performs HaveAQuestion gesture -> Opens hint panel
            if (!hintPanelOpenedInTutorial && gestureType == "HaveAQuestion")
            {
                hintPanelOpenedInTutorial = true;

                if (successAudioSource != null && successSound != null)
                {
                    successAudioSource.PlayOneShot(successSound);
                }

                TMP_Text activeText = GetActiveStatusText();
                if (activeText != null)
                {
                    activeText.text = hintOpenedMessage;
                }
                return;
            }

            // Stage 2: User performs ThumbsDown gesture -> Closes hint panel & completes step
            if (hintPanelOpenedInTutorial && gestureType == "ThumbsDown")
            {
                TriggerStepSuccess();
            }
        }
    }

    /// <summary>
    /// Event hook assigned directly to the UI Button OnClick() on Step 0.
    /// </summary>
    public void OnTestPinchButtonPressed()
    {
        if (activeMode != InputMode.HandTracking || advanceCoroutine != null || tutorialFinished)
            return;

        if (currentHandStepIndex == 0)
        {
            TriggerStepSuccess();
        }
    }

    public bool ValidateTeleportTarget(GameObject targetObject)
    {
        if (currentHandStepIndex != 1) return true;

        if (greenWaypointAnchor != null && (targetObject == greenWaypointAnchor || targetObject.transform.IsChildOf(greenWaypointAnchor.transform)))
        {
            TriggerStepSuccess();
            return true;
        }

        ShowTeleportRestrictionWarning();
        return false;
    }

    private void ShowTeleportRestrictionWarning()
    {
        TMP_Text activeText = GetActiveStatusText();
        if (activeText != null)
        {
            activeText.text = warningMessage;
            CancelInvoke(nameof(SetDefaultStatus));
            Invoke(nameof(SetDefaultStatus), 3.0f);
        }
    }

    private void TriggerStepSuccess()
    {
        if (advanceCoroutine != null || tutorialFinished || isStepTransitioning) return;

        completedSteps[currentHandStepIndex] = true;

        if (successAudioSource != null && successSound != null)
        {
            successAudioSource.PlayOneShot(successSound);
        }

        if (currentHandStepIndex == 1)
        {
            currentHandStepIndex++;
            UpdateHandStepUI();
            return;
        }

        isStepTransitioning = true;
        SetSuccessStatus();
        advanceCoroutine = StartCoroutine(AdvanceAfterSuccess());
    }

    private IEnumerator AdvanceAfterSuccess()
    {
        yield return new WaitForSecondsRealtime(advanceDelay);

        if (currentHandStepIndex < handStepPanels.Length - 1)
        {
            currentHandStepIndex++;
            UpdateHandStepUI();

            if (currentHandStepIndex == handStepPanels.Length - 1)
            {
                tutorialFinished = true;
            }
        }

        advanceCoroutine = null;
        isStepTransitioning = false;
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

        if (spawnPointTransform != null)
        {
            var xrOrigin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null)
            {
                xrOrigin.MoveCameraToWorldLocation(spawnPointTransform.position);
                xrOrigin.transform.rotation = spawnPointTransform.rotation;
            }
        }

        if (controllerTutorialPanel != null) controllerTutorialPanel.SetActive(false);
        if (handTutorialContainer != null) handTutorialContainer.SetActive(false);

        if (parentWorldSpaceCanvases != null)
        {
            foreach (var canvasObj in parentWorldSpaceCanvases)
            {
                if (canvasObj != null) canvasObj.SetActive(false);
            }
        }

        if (greenWaypointAnchor != null) greenWaypointAnchor.SetActive(false);
        if (anchorOutlineScript != null) anchorOutlineScript.enabled = false;

        ObjectiveManager objectiveManager = FindObjectOfType<ObjectiveManager>();
        if (objectiveManager != null)
        {
            objectiveManager.ShowStartWaypointAfterTutorial();
        }

        if (roomTeleportAnchors != null)
        {
            roomTeleportAnchors.SetActive(true);
        }
    }
}