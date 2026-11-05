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
    [Tooltip("Assign each step panel in order: 0=UI Pinch, 1=Teleport, 2=Pinch Grab, 3=Open Palm, 4=Completion")]
    [SerializeField] private GameObject[] handStepPanels;

    [Tooltip("Assign parent World-Space Canvas GameObjects if steps live on separate canvases around the room.")]
    [SerializeField] private GameObject[] parentWorldSpaceCanvases;

    [Tooltip("Assign the status TMP_Text component located on EACH panel in matching order (0 to 4).")]
    [SerializeField] private TMP_Text[] handStatusTexts;

    [Tooltip("Gesture names matching GestureTracker (e.g., 'Pinch', 'Teleport', 'Grab', 'Open Palm'). Leave final step empty.")]
    [SerializeField] private string[] requiredGestureNames;

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

    [Header("Consolidated UI References")]
    [SerializeField] private Button handCloseButton;
    [SerializeField] private Button completionDoneButton;

    [Header("Status Messages")]
    [Tooltip("Custom instruction for each step (0=UI Pinch, 1=Teleport, 2=Pinch Grab, 3=Open Palm, 4=Completion)")]
    [SerializeField]
    private string[] defaultStepMessages = new string[]
    {
        "PLease click the button below using <color=#FFD700><b>UI Pinch</b></color> to try it out!",
        "Pinch and point your ray at the <color=#55FF55><b>green waypoint anchor</b></color> to teleport.",
        "Reach out and <color=#FFD700><b>pinch-grab</b></color> any object on the desk.",
        "Hold your hand up with an <color=#FFD700><b>open palm facing forward</b></color> to open the menu.",
        "You may proceed to the next step. Move to the <color=#55FF55><b>green waypoint anchor</b></color> at the desk to begin the <color=#00E5FF><b>NPC Dialogue Tutorial</b></color>."
    };

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

            // Force green waypoint anchor OFF immediately in Controller mode!
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

        // Pass 1: deactivate every parent canvas EXCEPT the root hand container
        if (parentWorldSpaceCanvases != null)
        {
            foreach (var canvasObj in parentWorldSpaceCanvases)
            {
                if (canvasObj != null && canvasObj != handTutorialContainer)
                    canvasObj.SetActive(false);
            }
        }

        // Make sure the root container is on
        if (handTutorialContainer != null) handTutorialContainer.SetActive(true);

        // Pass 2: activate the current step's canvas AND any inactive parents above it
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
    /// </summary>
    public void OnGesturePerformed(string gestureType)
    {
        if (activeMode != InputMode.HandTracking || advanceCoroutine != null || tutorialFinished)
            return;

        // Ignore global air-pinch gesture events on Step 0!
        // Step 0 must ONLY be completed by physically clicking the UI button.
        if (currentHandStepIndex != 3)
            return;

        ProcessGestureAdvance(gestureType);
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

    private void ProcessGestureAdvance(string gestureType)
    {
        if (handStepPanels == null || currentHandStepIndex >= handStepPanels.Length - 1)
            return;

        if (requiredGestureNames == null || currentHandStepIndex >= requiredGestureNames.Length)
            return;

        string requiredGesture = requiredGestureNames[currentHandStepIndex];

        if (string.IsNullOrEmpty(gestureType) || string.IsNullOrEmpty(requiredGesture))
            return;

        if (string.Equals(gestureType.Trim(), requiredGesture.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            TriggerStepSuccess();
        }
    }

    public bool ValidateTeleportTarget(GameObject targetObject)
    {
        if (currentHandStepIndex != 1) return true;

        if (greenWaypointAnchor != null && (targetObject == greenWaypointAnchor || targetObject.transform.IsChildOf(greenWaypointAnchor.transform)))
        {
            // Directly call TriggerStepSuccess() instead of ProcessGestureAdvance!
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

        // 1. Play success sound
        if (successAudioSource != null && successSound != null)
        {
            successAudioSource.PlayOneShot(successSound);
        }

        // 2. Instant advance to Step 2 (Pinch Grab Panel) when completing Step 1 (Teleport)
        if (currentHandStepIndex == 1)
        {
            currentHandStepIndex++;
            UpdateHandStepUI();
            return;
        }

        // Standard behavior for other steps
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
        isStepTransitioning = false; // Reset transition lock
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
            // Find Unity's XR Origin / Rig object
            var xrOrigin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null)
            {
                xrOrigin.MoveCameraToWorldLocation(spawnPointTransform.position);
                xrOrigin.transform.rotation = spawnPointTransform.rotation;
            }
        }

        if (controllerTutorialPanel != null) controllerTutorialPanel.SetActive(false);
        if (handTutorialContainer != null) handTutorialContainer.SetActive(false);

        // Turn OFF parent world space canvases on close
        if (parentWorldSpaceCanvases != null)
        {
            foreach (var canvasObj in parentWorldSpaceCanvases)
            {
                if (canvasObj != null) canvasObj.SetActive(false);
            }
        }

        // Hide green waypoint anchor on close
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