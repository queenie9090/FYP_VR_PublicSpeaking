using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

public class LocomotionApplier : MonoBehaviour
{
    [Header("Locomotion Providers")]
    [SerializeField] private LocomotionProvider continuousMoveProvider;
    [SerializeField] private TeleportationProvider teleportProvider;
    [SerializeField] private CharacterController characterController;

    [Header("Turning Providers")]
    [SerializeField] private SnapTurnProvider snapTurnProvider;
    [SerializeField] private ContinuousTurnProvider continuousTurnProvider;

    [Header("Controller Input Managers (Starter Assets)")]
    [Tooltip("Left manager handles movement, right manager handles turning. Assign both. If left empty they are auto-found by name (Left/Right).")]
    [SerializeField] private ControllerInputActionManager leftControllerManager;
    [SerializeField] private ControllerInputActionManager rightControllerManager;

    [Header("XR Camera Setup")]
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private float standingCameraYOffset = 1.36f;
    [SerializeField] private float seatedYOffset = 0.25f; // Extra height boost when seated

    [Header("Input Mode Interactor Hierarchy (Controllers vs. Hands)")]
    [Tooltip("Assign the Left & Right Controller GameObjects under XR Origin")]
    [SerializeField] private GameObject leftControllerObject;
    [SerializeField] private GameObject rightControllerObject;

    [Tooltip("Assign the Left & Right Hand Tracking GameObjects under XR Origin")]
    [SerializeField] private GameObject leftHandTrackingObject;
    [SerializeField] private GameObject rightHandTrackingObject;

    private void Awake()
    {
        FindMissingControllerManagers();
    }

    private void Start()
    {
        ApplyAllVRPreferences();
    }

    public void ApplyAllVRPreferences()
    {
        // IMPORTANT: this must run FIRST. Re-activating the controller objects triggers the
        // managers' OnEnable, which resets their turn/move actions to the Inspector values.
        // We set the managers AFTER this so our values win.
        ApplyInputModePreference();

        LocomotionMode locoMode;
        TurningMode turnMode;
        bool seated;

        var sys = AppSystemManager.Instance;
        if (sys != null)
        {
            locoMode = sys.selectedLocomotionMode;
            turnMode = sys.selectedTurningMode;
            seated = sys.isSeatedModeEnabled;
        }
        else
        {
            // Fallback: AppSystemManager isn't in this scene, read saved prefs directly
            locoMode = (LocomotionMode)PlayerPrefs.GetInt(AppSystemManager.KEY_LOCOMOTION_MODE, (int)LocomotionMode.Dynamic);
            turnMode = (TurningMode)PlayerPrefs.GetInt(AppSystemManager.KEY_TURNING_MODE, (int)TurningMode.Snap);
            seated = PlayerPrefs.GetInt(AppSystemManager.KEY_SEATED_MODE, 0) == 1;
            Debug.LogWarning("[LocomotionApplier] AppSystemManager.Instance is null, using PlayerPrefs.");
        }

        // Hand tracking has no thumbsticks, so continuous move/turn can't work.
        // Always use teleport for hands, regardless of the saved preference.
        string currentMode = UserSession.InputMode;
        bool isHandTracking = !string.IsNullOrEmpty(currentMode) && currentMode.Contains("Hand");
        if (isHandTracking)
        {
            locoMode = LocomotionMode.TeleportOnly;
            turnMode = TurningMode.Snap;
        }

        // ---------------- Movement ----------------
        bool allowSmoothMove = locoMode == LocomotionMode.Dynamic || locoMode == LocomotionMode.Continuous;
        bool allowTeleport = locoMode == LocomotionMode.Dynamic || locoMode == LocomotionMode.TeleportOnly;

        SetProvider(continuousMoveProvider, allowSmoothMove);
        SetProvider(teleportProvider, allowTeleport);
        if (characterController != null) characterController.enabled = true;

        // ---------------- Turning ----------------
        bool useSmoothTurn = turnMode == TurningMode.Continuous;

        SetProvider(snapTurnProvider, !useSmoothTurn);
        SetProvider(continuousTurnProvider, useSmoothTurn);

        // Input actions: this is what makes snap turn actually receive input.
        // LEFT controller  = movement only.
        // RIGHT controller = turning only (snap / continuous camera rotation).
        if (leftControllerManager != null)
            leftControllerManager.smoothMotionEnabled = allowSmoothMove;

        if (rightControllerManager != null)
            rightControllerManager.smoothTurnEnabled = useSmoothTurn;

        // ---------------- Seated ----------------
        if (xrOrigin != null)
            xrOrigin.CameraYOffset = seated ? (standingCameraYOffset + seatedYOffset) : standingCameraYOffset;

        Debug.Log($"[LocomotionApplier] Applied Preferences -> Loco: {locoMode} | Turn: {turnMode} | Seated: {seated}");
    }

    private static void SetProvider(Behaviour b, bool on)
    {
        if (b == null) return;

        // Never deactivate the GameObject: it may be shared with other providers.
        if (on && !b.gameObject.activeSelf) b.gameObject.SetActive(true);
        b.enabled = on;
    }

    /// <summary>
    /// Reads UserSession.InputMode and enables Controllers or Hand Tracking interactors accordingly.
    /// </summary>
    public void ApplyInputModePreference()
    {
        string currentMode = UserSession.InputMode;
        bool isHandTracking = !string.IsNullOrEmpty(currentMode) &&
                              (currentMode.Contains("Hand") || currentMode.Contains("Hands"));

        Debug.Log($"[LocomotionApplier] Setting 3D Interactors to: {(isHandTracking ? "Hand Tracking" : "Controllers")}");

        // Toggle Controller GameObjects
        if (leftControllerObject != null) leftControllerObject.SetActive(!isHandTracking);
        if (rightControllerObject != null) rightControllerObject.SetActive(!isHandTracking);

        // Toggle Hand Tracking GameObjects
        if (leftHandTrackingObject != null) leftHandTrackingObject.SetActive(isHandTracking);
        if (rightHandTrackingObject != null) rightHandTrackingObject.SetActive(isHandTracking);
    }

    /// <summary>
    /// Disables movement and turning providers (managers stay enabled so
    /// ApplyAllVRPreferences can cleanly restore everything later).
    /// </summary>
    public void DisableAllLocomotion()
    {
        Debug.Log("[LocomotionApplier] DisableAllLocomotion called from:\n" + System.Environment.StackTrace);
        SetProvider(continuousMoveProvider, false);
        SetProvider(teleportProvider, false);
        SetProvider(snapTurnProvider, false);
        SetProvider(continuousTurnProvider, false);
        Debug.Log("[LocomotionApplier] All locomotion & turning disabled for UI/Tutorial.");
    }

    // Fallback so a missing Inspector assignment doesn't silently break turning.
    private void FindMissingControllerManagers()
    {
        if (leftControllerManager != null && rightControllerManager != null) return;

        foreach (var m in FindObjectsOfType<ControllerInputActionManager>(true))
        {
            string n = m.gameObject.name.ToLower();
            if (leftControllerManager == null && n.Contains("left")) leftControllerManager = m;
            else if (rightControllerManager == null && n.Contains("right")) rightControllerManager = m;
        }

        if (leftControllerManager == null || rightControllerManager == null)
            Debug.LogWarning("[LocomotionApplier] Could not find both ControllerInputActionManagers. " +
                             "Assign them in the Inspector or snap/continuous turn may not respond.");
    }
}