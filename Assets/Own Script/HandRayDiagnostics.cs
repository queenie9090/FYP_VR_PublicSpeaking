using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Management;

/// <summary>Opt-in, read-only investigation aid. Does not enable actions or write poses.</summary>
public class HandRayDiagnostics : MonoBehaviour
{
    [System.Serializable]
    public class HandReferences
    {
        public TrackedPoseDriver aimPose;
        public NearFarInteractor uiObjectInteractor;
        public TrackedPoseDriver teleportPose;
        public XRRayInteractor teleportInteractor;

        [System.NonSerialized] public string previousState;
        [System.NonSerialized] public Quaternion lastAim;
        [System.NonSerialized] public Quaternion wristAtChange;
        [System.NonSerialized] public float lastChangeTime;
        [System.NonSerialized] public bool hasBaseline;
        [System.NonSerialized] public bool warnedStale;
    }

    public HandReferences left = new HandReferences();
    public HandReferences right = new HandReferences();
    [Tooltip("Seconds between diagnostic checks, not between pose updates.")]
    [Min(0.1f)] public float sampleInterval = 0.5f;
    [Tooltip("Warn only if the wrist has rotated while the aim rotation has remained unchanged.")]
    [Min(1f)] public float staleSeconds = 2f;

    readonly List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>();
    float nextSample;
    string lastLoader;

    void OnEnable()
    {
        ResetBaseline(left);
        ResetBaseline(right);
        lastLoader = null;
        nextSample = 0f;
        Debug.Log("[HandRayDiag] Initialization begins. Press F8 for a snapshot; rotate each wrist before capturing.", this);
    }

    static void ResetBaseline(HandReferences hand)
    {
        hand.previousState = null;
        hand.hasBaseline = false;
        hand.warnedStale = false;
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
            Snapshot();
        if (Time.unscaledTime < nextSample)
            return;
        nextSample = Time.unscaledTime + Mathf.Max(0.1f, sampleInterval);
        Sample(false);
    }

    [ContextMenu("Capture hand ray snapshot")]
    public void Snapshot() => Sample(true);

    void Sample(bool snapshot)
    {
        subsystems.Clear();
        SubsystemManager.GetSubsystems(subsystems);
        XRHandSubsystem hands = null;
        foreach (var candidate in subsystems)
            if (candidate.running) { hands = candidate; break; }

        var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        var loader = manager != null && manager.activeLoader != null ? manager.activeLoader.name : "none";
        var loaderState = $"loader={loader}, initializationComplete={(manager != null && manager.isInitializationComplete)}, handSubsystemRunning={hands != null}, subsystemCount={subsystems.Count}";
        if (snapshot || loaderState != lastLoader)
            Debug.Log("[HandRayDiag] " + loaderState, this);
        lastLoader = loaderState;
        SampleHand("Left", left, hands, true, snapshot);
        SampleHand("Right", right, hands, false, snapshot);
    }

    void SampleHand(string label, HandReferences refs, XRHandSubsystem subsystem, bool isLeft, bool snapshot)
    {
        bool tracked = subsystem != null && (isLeft ? subsystem.leftHand.isTracked : subsystem.rightHand.isTracked);
        var driver = refs.aimPose;
        var pos = driver != null ? driver.positionInput.action : null;
        var rot = driver != null ? driver.rotationInput.action : null;
        var state = driver != null ? driver.trackingStateInput.action : null;
        var teleport = refs.teleportPose;
        var teleportState = teleport != null ? teleport.trackingStateInput.action : null;
        string status = $"handTracked={tracked}; driver={(driver != null ? (driver.isActiveAndEnabled ? "active" : "inactive") : "missing")}; position={ActionStatus(pos)}; rotation={ActionStatus(rot)}; tracking={ActionStatus(state)} bits={TrackingBits(state)}; teleportDriver={(teleport != null ? (teleport.isActiveAndEnabled ? "active" : "inactive") : "missing")}; teleportPosition={ActionStatus(teleport != null ? teleport.positionInput.action : null)}; teleportRotation={ActionStatus(teleport != null ? teleport.rotationInput.action : null)}; teleportTracking={ActionStatus(teleportState)} bits={TrackingBits(teleportState)}";
        if (snapshot || status != refs.previousState)
        {
            Debug.Log($"[HandRayDiag][{label}] {(snapshot ? "SNAPSHOT" : "STATE CHANGE")} {status}\n{Details(refs)}", this);
            refs.previousState = status;
        }

        if (!tracked || rot == null || !rot.enabled || driver == null || !driver.isActiveAndEnabled)
        {
            refs.hasBaseline = false;
            refs.warnedStale = false;
            return;
        }
        var hand = isLeft ? subsystem.leftHand : subsystem.rightHand;
        if (!hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist))
        {
            refs.hasBaseline = false;
            return;
        }
        Quaternion value = rot.ReadValue<Quaternion>();
        if (!refs.hasBaseline || Quaternion.Angle(refs.lastAim, value) > 0.1f)
        {
            float jump = refs.hasBaseline ? Quaternion.Angle(refs.lastAim, value) : 0f;
            if (jump > 75f)
                Debug.LogWarning($"[HandRayDiag][{label}] Aim input changed {jump:F1} degrees between samples; fast hand motion can also cause this.\n{Details(refs)}", this);
            refs.lastAim = value;
            refs.wristAtChange = wrist.rotation;
            refs.lastChangeTime = Time.unscaledTime;
            refs.hasBaseline = true;
            refs.warnedStale = false;
        }
        else if (!refs.warnedStale && Time.unscaledTime - refs.lastChangeTime >= staleSeconds && Quaternion.Angle(refs.wristAtChange, wrist.rotation) > 10f)
        {
            refs.warnedStale = true;
            Debug.LogWarning($"[HandRayDiag][{label}] Wrist rotated {Quaternion.Angle(refs.wristAtChange, wrist.rotation):F1} degrees while aim input stayed nearly unchanged for {Time.unscaledTime - refs.lastChangeTime:F1}s.\n{Details(refs)}", this);
        }
    }

    static string ActionStatus(InputAction action) => action == null ? "missing" : $"{(action.enabled ? "enabled" : "disabled")} ({action.actionMap?.name}/{action.name}, control={action.activeControl?.path ?? "none"})";
    static string TrackingBits(InputAction action) => action != null && action.enabled ? action.ReadValue<int>().ToString() : "unavailable";
    static string TransformInfo(Transform t) => t == null ? "missing" : $"{t.name} worldPosition={t.position.ToString("F4")} worldRotation={t.rotation.ToString("F4")} forward={t.forward.ToString("F4")}";

    static string DriverInfo(TrackedPoseDriver driver)
    {
        if (driver == null) return "driver missing";
        var p = driver.positionInput.action;
        var r = driver.rotationInput.action;
        var s = driver.trackingStateInput.action;
        return $"driver={driver.name} active={driver.isActiveAndEnabled}; positionAction={ActionStatus(p)} value={(p != null && p.enabled ? p.ReadValue<Vector3>().ToString("F4") : "unavailable")}; rotationAction={ActionStatus(r)} value={(r != null && r.enabled ? r.ReadValue<Quaternion>().ToString("F4") : "unavailable")}; trackingAction={ActionStatus(s)} value={(s != null && s.enabled ? s.ReadValue<int>().ToString() : "unavailable")}; pose={TransformInfo(driver.transform)}";
    }

    static string Details(HandReferences refs)
    {
        var caster = refs.uiObjectInteractor != null ? refs.uiObjectInteractor.farInteractionCaster : null;
        var castOrigin = caster != null ? caster.castOrigin : null;
        var effectiveOrigin = caster != null ? caster.effectiveCastOrigin : null;
        var teleportOrigin = refs.teleportInteractor != null ? refs.teleportInteractor.rayOriginTransform : null;
        return $"  UI aim: {DriverInfo(refs.aimPose)}\n  UI cast origin: {TransformInfo(castOrigin)}\n  UI effective origin: {TransformInfo(effectiveOrigin)}\n  Teleport pose: {DriverInfo(refs.teleportPose)}\n  Teleport ray origin: {TransformInfo(teleportOrigin)}";
    }
}
