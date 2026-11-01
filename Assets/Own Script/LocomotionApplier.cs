using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

public class LocomotionApplier : MonoBehaviour
{
    [Header("Locomotion Providers")]
    [SerializeField] private LocomotionProvider continuousMoveProvider;
    [SerializeField] private TeleportationProvider teleportProvider;
    [SerializeField] private CharacterController characterController;

    [Header("Turning Providers")]
    [SerializeField] private SnapTurnProvider snapTurnProvider;
    [SerializeField] private ContinuousTurnProvider continuousTurnProvider;

    [Header("XR Camera Setup")]
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private float seatedYOffset = 0.25f; // Extra height boost when seated

    private void Start()
    {
        ApplyAllVRPreferences();
    }

    public void ApplyAllVRPreferences()
    {
        if (AppSystemManager.Instance == null) return;

        // 1. Apply Locomotion Mode
        LocomotionMode locoMode = AppSystemManager.Instance.selectedLocomotionMode;
        switch (locoMode)
        {
            case LocomotionMode.Dynamic:
                if (continuousMoveProvider != null) continuousMoveProvider.enabled = true;
                if (teleportProvider != null) teleportProvider.enabled = true;
                if (characterController != null) characterController.enabled = true;
                break;

            case LocomotionMode.TeleportOnly:
                if (continuousMoveProvider != null) continuousMoveProvider.enabled = false;
                if (teleportProvider != null) teleportProvider.enabled = true;
                if (characterController != null) characterController.enabled = false;
                break;

            case LocomotionMode.Continuous:
                if (continuousMoveProvider != null) continuousMoveProvider.enabled = true;
                if (teleportProvider != null) teleportProvider.enabled = false;
                if (characterController != null) characterController.enabled = true;
                break;
        }

        // 2. Apply Turning Mode
        TurningMode turnMode = AppSystemManager.Instance.selectedTurningMode;
        if (snapTurnProvider != null) snapTurnProvider.enabled = (turnMode == TurningMode.Snap);
        if (continuousTurnProvider != null) continuousTurnProvider.enabled = (turnMode == TurningMode.Continuous);

        // 3. Apply Seated Mode Height Offset
        if (xrOrigin != null)
        {
            if (AppSystemManager.Instance.isSeatedModeEnabled)
            {
                xrOrigin.CameraYOffset = 1.36f + seatedYOffset; // Boost eye-level height for seated play
            }
            else
            {
                xrOrigin.CameraYOffset = 1.36f; // Standard standing height default
            }
        }

        Debug.Log($"[LocomotionApplier] Applied Preferences -> Loco: {locoMode} | Turn: {turnMode} | Seated: {AppSystemManager.Instance.isSeatedModeEnabled}");
    }
}