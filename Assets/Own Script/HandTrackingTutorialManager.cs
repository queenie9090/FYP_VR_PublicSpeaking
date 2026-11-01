using UnityEngine;

public class HandTrackingTutorialManager : MonoBehaviour
{
    public enum TutorialStep
    {
        OpenPalmHint,
        PinchButton,
        PointPinchTeleport,
        GrabObject,
        SocialGreeting,
        CompletedPhase1
    }

    [Header("Current Progress")]
    public TutorialStep currentStep = TutorialStep.OpenPalmHint;

    [Header("Ghost Hand Visual Guide")]
    [SerializeField] private GameObject ghostHandVisual;
    [SerializeField] private Animator ghostHandAnimator;

    [Header("Phase 1 Spatial Objects")]
    [SerializeField] private GameObject teleportTargetZone;
    [SerializeField] private GameObject practiceGrabObject;

    [Header("Dependencies")]
    [SerializeField] private ObjectiveManager objectiveManager;
    [SerializeField] private InteractionManager interactionManager;

    private bool isHandTrackingMode = true;

    private void Start()
    {
        // Check input mode from UserSession (or default to true if using hands)
        isHandTrackingMode = UserSession.InputMode == "HandTracking";

        if (isHandTrackingMode)
        {
            // Activate Ghost Hand visual for Hand Tracking Mode
            if (ghostHandVisual != null) ghostHandVisual.SetActive(true);
            StartHandTrackingTutorial();
        }
        else
        {
            // Disable Ghost Hand visual for Controller Mode
            if (ghostHandVisual != null) ghostHandVisual.SetActive(false);
            StartControllerTutorial();
        }
    }

    #region Hand Tracking Flow (Phase 1)

    public void StartHandTrackingTutorial()
    {
        currentStep = TutorialStep.OpenPalmHint;

        if (practiceGrabObject != null) practiceGrabObject.SetActive(false);
        if (teleportTargetZone != null) teleportTargetZone.SetActive(false);

        if (objectiveManager != null)
        {
            objectiveManager.SetObjective("Step 1: Hold open palm for 1 sec to view Hint Panel.");
        }

        PlayGhostAnimation("Anim_OpenPalm");
    }

    public void OnOpenPalmDetected()
    {
        if (!isHandTrackingMode || currentStep != TutorialStep.OpenPalmHint) return;

        currentStep = TutorialStep.PinchButton;
        if (objectiveManager != null)
        {
            objectiveManager.SetObjective("Step 2: Pinch thumb & index finger to confirm UI selection.");
        }

        PlayGhostAnimation("Anim_Pinch");
    }

    public void OnPinchConfirmed()
    {
        if (!isHandTrackingMode || currentStep != TutorialStep.PinchButton) return;

        currentStep = TutorialStep.PointPinchTeleport;
        if (teleportTargetZone != null) teleportTargetZone.SetActive(true);

        if (objectiveManager != null)
        {
            objectiveManager.SetObjective("Step 3: Point at the floor target and Pinch to Teleport.");
        }

        PlayGhostAnimation("Anim_PointPinch");
    }

    public void OnPointPinchTeleportCompleted()
    {
        if (!isHandTrackingMode || currentStep != TutorialStep.PointPinchTeleport) return;

        if (teleportTargetZone != null) teleportTargetZone.SetActive(false);
        if (practiceGrabObject != null) practiceGrabObject.SetActive(true);

        currentStep = TutorialStep.GrabObject;
        if (objectiveManager != null)
        {
            objectiveManager.SetObjective("Step 4: Perform a Grab gesture on the object in front of you.");
        }

        PlayGhostAnimation("Anim_Grab");
    }

    public void OnObjectGrabbed()
    {
        if (!isHandTrackingMode || currentStep != TutorialStep.GrabObject) return;

        if (practiceGrabObject != null) practiceGrabObject.SetActive(false);

        currentStep = TutorialStep.SocialGreeting;
        if (objectiveManager != null)
        {
            objectiveManager.SetObjective("Step 5: Wave or Handshake near the NPC to initiate contact.");
        }

        PlayGhostAnimation("Anim_Wave");
    }

    public void OnSocialGreetingCompleted()
    {
        if (!isHandTrackingMode || currentStep != TutorialStep.SocialGreeting) return;

        CompleteTutorialPhase1();
    }

    #endregion

    #region Controller Fallback Flow

    private void StartControllerTutorial()
    {
        // For controller mode, jump straight to briefing objective
        if (objectiveManager != null)
        {
            objectiveManager.SetObjective("Walk to the NPC and review the briefing monitor.");
        }
    }

    #endregion

    private void CompleteTutorialPhase1()
    {
        currentStep = TutorialStep.CompletedPhase1;

        // Hide Ghost Hand visual once tutorial is completed
        if (ghostHandVisual != null) ghostHandVisual.SetActive(false);

        if (objectiveManager != null)
        {
            objectiveManager.SetObjective("Hand tracking mastered! Walk to the NPC and press Start.");
        }
    }

    private void PlayGhostAnimation(string stateName)
    {
        if (ghostHandAnimator != null && ghostHandVisual != null && ghostHandVisual.activeSelf)
        {
            ghostHandAnimator.Play(stateName);
        }
    }
}