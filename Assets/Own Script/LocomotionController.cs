using UnityEngine;
using UnityEngine.SceneManagement;

public class LocomotionController : MonoBehaviour
{
    [SerializeField] private VRPlayerSeating playerSeating;
    private LocomotionApplier locoApplier;

    private void Awake()
    {
        locoApplier = FindObjectOfType<LocomotionApplier>();
    }

    public void SnapToChair()
    {
        if (playerSeating != null)
            playerSeating.SnapPlayerToChair();
    }

    public void SetLocomotionEnabled(bool enabled)
    {
        if (locoApplier == null) locoApplier = FindObjectOfType<LocomotionApplier>();
        if (locoApplier == null) return;

        if (enabled)
        {
            locoApplier.ApplyAllVRPreferences();
        }
        else
        {
            locoApplier.DisableAllLocomotion();
        }
    }
}