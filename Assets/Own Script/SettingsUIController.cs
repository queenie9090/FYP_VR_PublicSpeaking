using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SettingsUIController : MonoBehaviour
{
    [Header("Locomotion & Movement Controls")]
    [SerializeField] private TMP_Dropdown locomotionDropdown; // 0 = Dynamic, 1 = Teleport Only, 2 = Continuous
    [SerializeField] private TMP_Dropdown turningDropdown;    // 0 = Snap Turning, 1 = Continuous Turning
    [SerializeField] private Toggle seatedModeToggle;

    [Header("Action Buttons")]
    [SerializeField] private Button resetTutorialButton;
    private LocomotionApplier locomotionApplier;

    private void ApplyNow()
    {
        if (locomotionApplier == null) locomotionApplier = FindObjectOfType<LocomotionApplier>();
        if (locomotionApplier != null) locomotionApplier.ApplyAllVRPreferences();
    }

    private void Start()
    {
        // 1. Force visual UI elements to match AppSystemManager values first
        SyncUIToSystemSettings();

        // 2. Bind listeners AFTER initial values are set to avoid false triggers
        BindUIListeners();
    }

    private void OnEnable()
    {
        // Re-sync visual UI whenever the panel is opened
        SyncUIToSystemSettings();
    }

    private void SyncUIToSystemSettings()
    {
        if (AppSystemManager.Instance == null) return;

        // Locomotion Dropdown (Default: Dynamic = 0)
        if (locomotionDropdown != null)
        {
            locomotionDropdown.SetValueWithoutNotify((int)AppSystemManager.Instance.selectedLocomotionMode);
            locomotionDropdown.RefreshShownValue();
        }

        // Turning Dropdown (Default: Snap = 0)
        if (turningDropdown != null)
        {
            turningDropdown.SetValueWithoutNotify((int)AppSystemManager.Instance.selectedTurningMode);
            turningDropdown.RefreshShownValue();
        }

        // Seated Mode Toggle
        if (seatedModeToggle != null)
        {
            seatedModeToggle.SetIsOnWithoutNotify(AppSystemManager.Instance.isSeatedModeEnabled);
        }
    }

    private void BindUIListeners()
    {
        if (locomotionDropdown != null)
        {
            locomotionDropdown.onValueChanged.RemoveAllListeners();
            locomotionDropdown.onValueChanged.AddListener(OnLocomotionDropdownChanged);
        }

        if (turningDropdown != null)
        {
            turningDropdown.onValueChanged.RemoveAllListeners();
            turningDropdown.onValueChanged.AddListener(OnTurningDropdownChanged);
        }

        if (seatedModeToggle != null)
        {
            seatedModeToggle.onValueChanged.RemoveAllListeners();
            seatedModeToggle.onValueChanged.AddListener(OnSeatedModeToggled);
        }

        if (resetTutorialButton != null)
        {
            resetTutorialButton.onClick.RemoveAllListeners();
            resetTutorialButton.onClick.AddListener(OnResetTutorialClicked);
        }
    }

    public void OnLocomotionDropdownChanged(int index)
    {
        if (AppSystemManager.Instance != null)
        {
            AppSystemManager.Instance.SaveLocomotionModeSetting((LocomotionMode)index);
            ApplyNow();
        }
    }

    public void OnTurningDropdownChanged(int index)
    {
        if (AppSystemManager.Instance != null)
        {
            AppSystemManager.Instance.SaveTurningModeSetting((TurningMode)index);
            ApplyNow();
        }
    }

    public void OnSeatedModeToggled(bool value)
    {
        if (AppSystemManager.Instance != null)
        {
            AppSystemManager.Instance.SaveSeatedModeSetting(value);
            ApplyNow();
        }
    }

    public void OnResetTutorialClicked()
    {
        if (AppSystemManager.Instance != null)
            AppSystemManager.Instance.ResetTutorialPreferences();
    }
}