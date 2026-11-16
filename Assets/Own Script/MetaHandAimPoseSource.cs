using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Hands.OpenXR;
using UnityEngine.XR.OpenXR;

/// <summary>
/// Keeps an aim driver's position, rotation and tracking state on the same Meta device.
/// Other input providers retain the existing prefab actions when the Meta feature is off.
/// </summary>
[DefaultExecutionOrder(-1000)]
[RequireComponent(typeof(TrackedPoseDriver))]
public sealed class MetaHandAimPoseSource : MonoBehaviour
{
    [SerializeField] bool rightHand;

    TrackedPoseDriver driver;
    InputActionProperty originalPosition, originalRotation, originalTracking;
    InputAction position, rotation, tracking;

    void OnEnable()
    {
        var settings = OpenXRSettings.Instance;
        var feature = settings != null ? settings.GetFeature<MetaHandTrackingAim>() : null;
        if (feature == null || !feature.enabled)
            return;

        driver = GetComponent<TrackedPoseDriver>();
        originalPosition = driver.positionInput;
        originalRotation = driver.rotationInput;
        originalTracking = driver.trackingStateInput;

        string hand = rightHand ? "RightHand" : "LeftHand";
        string path = "<MetaAimHand>{" + hand + "}/";
        position = new InputAction("Meta Aim Position", InputActionType.Value, path + "devicePosition", expectedControlType: "Vector3");
        rotation = new InputAction("Meta Aim Rotation", InputActionType.Value, path + "deviceRotation", expectedControlType: "Quaternion");
        tracking = new InputAction("Meta Aim Tracking State", InputActionType.Value, path + "trackingState", expectedControlType: "Integer");

        // The driver owns the lifetime of inline actions and respects their tracking-state bits.
        driver.positionInput = new InputActionProperty(position);
        driver.rotationInput = new InputActionProperty(rotation);
        driver.trackingStateInput = new InputActionProperty(tracking);
    }

    void OnDisable()
    {
        if (driver == null)
            return;

        // Avoid replacing a different owner's actions if another component changed the driver.
        if (driver.positionInput.action == position) driver.positionInput = originalPosition;
        if (driver.rotationInput.action == rotation) driver.rotationInput = originalRotation;
        if (driver.trackingStateInput.action == tracking) driver.trackingStateInput = originalTracking;
        position.Dispose();
        rotation.Dispose();
        tracking.Dispose();
        driver = null;
    }
}
