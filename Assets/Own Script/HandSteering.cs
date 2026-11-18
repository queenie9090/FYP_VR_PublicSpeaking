using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;
using UnityEngine.XR.Hands.Samples.GestureSample;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.UI;

[Serializable]
public class HandSteeringSettings
{
    public bool enabled = true;
    [Min(0.001f)] public float pinchActivationDistance = 0.025f;
    [Min(0.001f)] public float pinchReleaseDistance = 0.04f;
    [Header("Steering Gesture")]
    [Tooltip("Assign the XRHandShape or XRHandPose asset (e.g., open index/ring/pinky with curled middle finger) required to allow hand steering.")]
    public ScriptableObject steeringHandShapeOrPose;
    [Min(0f)] public float deadZone = 0.025f;
    [Tooltip("Normalized steering input per meter beyond the dead zone.")]
    [Min(0f)] public float sensitivity = 6f;
    [Min(0f)] public float maximumDegreesPerSecond = 60f;
    [Tooltip("Time in seconds to smooth steering input. Zero disables smoothing.")]
    [Min(0f)] public float inputSmoothing = 0.10f;
    [Tooltip("Vignette fade-in smoothing time in seconds.")]
    [Min(0.01f)] public float vignetteFadeIn = 0.11f;
    [Tooltip("Vignette fade-out smoothing time in seconds.")]
    [Min(0.01f)] public float vignetteFadeOut = 0.30f;
    [Header("Steering Indicator")]
    public bool showIndicator = true;
    [Min(0.005f)] public float indicatorRadius = 0.035f;
    [Range(0f, 1f)] public float indicatorOpacity = 0.55f;
    [Min(0.01f)] public float indicatorFadeTime = 0.12f;
}

/// <summary>Applies the output of two independent hand detectors; never enables a locomotion provider.</summary>
[DefaultExecutionOrder(-100)]
public class HandSteering : MonoBehaviour, ITunnelingVignetteProvider
{
    private XROrigin origin;
    private HandSteeringSettings settings;
    private HandSteeringDetector detector;
    private GestureTracker tracker;
    private TunnelingVignetteController vignette;
    private readonly VignetteParameters effect = new VignetteParameters();
    private bool vignetteQueued;
    private float vignetteAmount;
    private float input;
    private float inputVelocity;
    private int owner = -1;
    private HandSteeringVisuals visuals;
    private float accumulatedTurnDegrees;
    private bool steeringGestureReported;
    private bool started;

    public VignetteParameters vignetteParameters => effect;

    public void Initialize(XROrigin xrOrigin, GameObject left, GameObject right, HandSteeringSettings configuration)
    {
        detector?.Stop();
        origin = xrOrigin;
        settings = configuration;
        detector = new HandSteeringDetector(left, right, configuration);
        if (started && isActiveAndEnabled) detector.Start();
    }

    private void Start()
    {
        started = true;
        if (origin == null) origin = FindObjectOfType<XROrigin>();
        tracker = FindObjectOfType<GestureTracker>();
        vignette = FindObjectOfType<TunnelingVignetteController>(true);
        detector?.Start();
    }

    private void Update()
    {
        if (detector == null || settings == null) return;
        bool handMode = settings.enabled && origin != null && origin.Camera != null &&
                        !string.IsNullOrEmpty(UserSession.InputMode) && UserSession.InputMode.Contains("Hand");
        detector.Update(handMode ? origin : null);
        if (tracker == null) tracker = FindObjectOfType<GestureTracker>();
        for (int i = 0; i < 2; i++) tracker?.SetHandSteering(i, detector.Active(i));

        int previousOwner = owner;
        if (owner < 0 || !detector.Active(owner))
            owner = detector.Active(0) ? 0 : detector.Active(1) ? 1 : -1;
        if (owner != previousOwner) { input = 0f; inputVelocity = 0f; }
        float target = owner >= 0 ? detector.Input(owner) : 0f;
        if (owner < 0) { input = 0f; inputVelocity = 0f; }
        else if (settings.inputSmoothing <= 0f) input = target;
        else input = Mathf.SmoothDamp(input, target, ref inputVelocity, settings.inputSmoothing);
        float speed = input * Mathf.Max(0f, settings.maximumDegreesPerSecond);
        if (owner >= 0 && !Mathf.Approximately(speed, 0f))
        {
            float deltaDegrees = speed * Time.deltaTime;
            origin.RotateAroundCameraUsingOriginUp(deltaDegrees);

            if (!steeringGestureReported)
            {
                accumulatedTurnDegrees += Mathf.Abs(deltaDegrees);
                if (accumulatedTurnDegrees >= 25f)
                {
                    steeringGestureReported = true;
                    tracker?.ProcessGestureEvent("HandSteering", 0.5f);
                }
            }
        }
        else if (owner < 0)
        {
            accumulatedTurnDegrees = 0f;
            steeringGestureReported = false;
        }
        if (settings.showIndicator && visuals == null) visuals = new HandSteeringVisuals();
        if (visuals != null)
        {
            bool visible = settings.showIndicator && owner >= 0;
            visuals.Update(visible, visible ? detector.AnchorWorld(owner) : Vector3.zero,
                visible ? detector.HandWorld(owner, origin) : Vector3.zero,
                origin != null && origin.Camera != null ? origin.Camera.transform : null, input, settings);
        }
        UpdateVignette(Mathf.Abs(input));
    }

    private void UpdateVignette(float target)
    {
        if (vignette == null) vignette = FindObjectOfType<TunnelingVignetteController>(true);
        if (vignette == null || !vignette.isActiveAndEnabled) return;
        float fadeTime = target > vignetteAmount ? settings.vignetteFadeIn : settings.vignetteFadeOut;
        float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeTime));
        vignetteAmount = Mathf.Lerp(vignetteAmount, Mathf.Clamp01(target), t);
        if (vignetteAmount > 0.001f)
        {
            effect.CopyFrom(vignette.defaultParameters);
            effect.apertureSize = Mathf.Lerp(1f, Mathf.Clamp01(vignette.defaultParameters.apertureSize), vignetteAmount);
            effect.easeInTime = 0f;
            effect.easeOutTime = Mathf.Max(0.01f, settings.vignetteFadeOut);
            effect.easeInTimeLock = false;
            effect.easeOutDelayTime = 0f;
            if (!vignetteQueued) { vignette.BeginTunnelingVignette(this); vignetteQueued = true; }
        }
        else if (vignetteQueued) { vignette.EndTunnelingVignette(this); vignetteQueued = false; }
    }

    private void OnDisable()
    {
        detector?.Stop();
        if (tracker != null) { tracker.SetHandSteering(0, false); tracker.SetHandSteering(1, false); }
        if (vignette != null && vignetteQueued) vignette.EndTunnelingVignette(this);
        visuals?.Dispose(); visuals = null;
        vignetteQueued = false; vignetteAmount = input = inputVelocity = 0f; owner = -1;
        accumulatedTurnDegrees = 0f; steeringGestureReported = false;
    }

    private void OnEnable() { detector?.Start(); }
    private void OnDestroy() { detector?.Stop(); }
}

/// <summary>Reads hand joints and existing interaction state without moving the XR Origin.</summary>
internal sealed class HandSteeringDetector
{
    private sealed class HandState
    {
        public bool active;
        public Vector3 anchor;
        public Vector3 anchorWorld;
        public Vector3 wristTrackingPosition;
        public float input;
        public GameObject root;
        public NearFarInteractor[] nearFar;
        public XRRayInteractor[] rays;
        public XRBaseInteractor[] interactors;
        public bool priorityGesture;
        public float lastGestureUpdate = -1f;
        public bool matchesSteeringPose;
        public float lastSteeringPoseTime = -1f;
    }

    private readonly HandState[] hands = { new HandState(), new HandState() };
    private readonly HandSteeringSettings settings;
    private readonly List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>();
    private readonly List<XRHandTrackingEvents> events = new List<XRHandTrackingEvents>();
    private StaticHandGesture[] gestures;
    private bool listening;
    private bool warnedMissingSteeringPose;
    private XRHandSubsystem currentSubsystem;

    public HandSteeringDetector(GameObject left, GameObject right, HandSteeringSettings configuration)
    {
        settings = configuration;
        hands[0].root = left; hands[1].root = right;
        foreach (var hand in hands)
        {
            hand.nearFar = hand.root != null ? hand.root.GetComponentsInChildren<NearFarInteractor>(true) : Array.Empty<NearFarInteractor>();
            hand.rays = hand.root != null ? hand.root.GetComponentsInChildren<XRRayInteractor>(true) : Array.Empty<XRRayInteractor>();
            hand.interactors = hand.root != null ? hand.root.GetComponentsInChildren<XRBaseInteractor>(true) : Array.Empty<XRBaseInteractor>();
        }
    }

    public void Start()
    {
        if (listening) return;
        gestures = UnityEngine.Object.FindObjectsOfType<StaticHandGesture>(true);
        events.Clear();
        foreach (var source in UnityEngine.Object.FindObjectsOfType<XRHandTrackingEvents>(true))
        {
            if (source == null || events.Contains(source)) continue;
            events.Add(source);
            source.jointsUpdated.AddListener(OnJointsUpdated);
        }
        listening = true;
    }

    public void Stop()
    {
        currentSubsystem = null;
        foreach (var source in events) if (source != null) source.jointsUpdated.RemoveListener(OnJointsUpdated);
        events.Clear(); listening = false;
        foreach (var hand in hands) { Reset(hand); InvalidatePose(hand); hand.priorityGesture = false; }
    }

    private void OnJointsUpdated(XRHandJointsUpdatedEventArgs args)
    {
        if (args.subsystem != currentSubsystem || currentSubsystem == null ||
            (args.hand.handedness != Handedness.Left && args.hand.handedness != Handedness.Right)) return;
        int index = args.hand.handedness == Handedness.Left ? 0 : 1;
        if (!args.hand.isTracked) { InvalidatePose(hands[index]); return; }
        bool matchesPose = false;
        if (settings.steeringHandShapeOrPose is XRHandShape steeringShape)
            matchesPose = steeringShape.CheckConditions(args);
        else if (settings.steeringHandShapeOrPose is XRHandPose steeringPose && steeringPose.relativeOrientation != null)
            matchesPose = steeringPose.CheckConditions(args);
        hands[index].matchesSteeringPose = matchesPose;
        hands[index].lastSteeringPoseTime = Time.unscaledTime;
        bool priority = false;
        foreach (var gesture in gestures)
        {
            if (gesture == null || !gesture.isActiveAndEnabled || gesture.handTrackingEvents == null ||
                gesture.handTrackingEvents.handedness != args.hand.handedness) continue;
            // The middle-finger warning is suppressed independently for a steering hand.
            bool warning = false;
            for (int i = 0; i < gesture.gesturePerformed.GetPersistentEventCount(); i++)
                if ((gesture.gesturePerformed.GetPersistentMethodName(i) ?? "").Contains("UnprofessionalGesture")) warning = true;
            if (warning) continue;
            if (gesture.handShapeOrPose is XRHandShape shape && shape.CheckConditions(args)) priority = true;
            else if (gesture.handShapeOrPose is XRHandPose pose && pose.CheckConditions(args)) priority = true;
        }
        hands[index].priorityGesture = priority;
        hands[index].lastGestureUpdate = Time.unscaledTime;
    }

    public bool Active(int index) => hands[index].active;
    public float Input(int index) => hands[index].input;
    public Vector3 AnchorWorld(int index) => hands[index].anchorWorld;
    public Vector3 HandWorld(int index, XROrigin origin)
    {
        Transform space = origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : origin.transform;
        return space.TransformPoint(hands[index].wristTrackingPosition);
    }

    public void Update(XROrigin origin)
    {
        if (settings.steeringHandShapeOrPose == null && !warnedMissingSteeringPose)
        {
            Debug.LogWarning("[HandSteering] Assign the required XRHandShape or XRHandPose asset in the Inspector to enable steering.");
            warnedMissingSteeringPose = true;
        }
        subsystems.Clear();
        SubsystemManager.GetSubsystems(subsystems);
        XRHandSubsystem subsystem = null;
        foreach (var candidate in subsystems) if (candidate.running) { subsystem = candidate; break; }
        if (subsystem != currentSubsystem)
        {
            currentSubsystem = subsystem;
            foreach (var hand in hands) { Reset(hand); InvalidatePose(hand); }
        }
        for (int i = 0; i < 2; i++)
        {
            var state = hands[i];
            if (subsystem != null && !(i == 0 ? subsystem.leftHand : subsystem.rightHand).isTracked)
            { Reset(state); InvalidatePose(state); continue; }
            if (settings.steeringHandShapeOrPose == null || !state.matchesSteeringPose ||
                state.lastSteeringPoseTime < 0f || Time.unscaledTime - state.lastSteeringPoseTime > 0.25f ||
                origin == null || subsystem == null ||
                state.root == null || !state.root.activeInHierarchy || Blocked(state))
            { Reset(state); continue; }
            var hand = i == 0 ? subsystem.leftHand : subsystem.rightHand;
            if (!hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumb) ||
                !hand.GetJoint(XRHandJointID.MiddleTip).TryGetPose(out var middle) ||
                !hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist))
            { Reset(state); continue; }
            float distance = Vector3.Distance(thumb.position, middle.position);
            float threshold = state.active ? Mathf.Max(settings.pinchActivationDistance, settings.pinchReleaseDistance) : settings.pinchActivationDistance;
            if (distance > threshold) { Reset(state); continue; }
            Transform space = origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : origin.transform;
            state.wristTrackingPosition = wrist.position;
            Vector3 relative = origin.Camera.transform.InverseTransformPoint(space.TransformPoint(wrist.position));
            if (!state.active) { state.anchor = relative; state.anchorWorld = space.TransformPoint(wrist.position); state.active = true; }
            float displacement = relative.x - state.anchor.x;
            float beyond = Mathf.Max(0f, Mathf.Abs(displacement) - Mathf.Max(0f, settings.deadZone));
            state.input = Mathf.Clamp(Mathf.Sign(displacement) * beyond * Mathf.Max(0f, settings.sensitivity), -1f, 1f);
        }
    }

    private static bool Blocked(HandState state)
    {
        if (state.priorityGesture && Time.unscaledTime - state.lastGestureUpdate < 0.25f) return true;
        foreach (var interactor in state.nearFar)
            if (interactor != null && interactor.isActiveAndEnabled && interactor.hasSelection) return true;
        foreach (var interactor in state.interactors)
        {
            if (interactor == null || !interactor.isActiveAndEnabled) continue;
            if (interactor.hasSelection) return true;
            if (interactor is XRPokeInteractor poke && TrackedDeviceGraphicRaycaster.IsPokeInteractingWithUI(poke)) return true;
        }
        // The hand teleport ray is inactive outside teleport mode.
        foreach (var ray in state.rays) if (ray != null && ray.isActiveAndEnabled) return true;
        return false;
    }

    private static void Reset(HandState hand) { hand.active = false; hand.input = 0f; }
    private static void InvalidatePose(HandState hand) { hand.matchesSteeringPose = false; hand.lastSteeringPoseTime = -1f; }
}

/// <summary>Small, collider-free line visuals; the hand marker and stationary anchor have separate positions.</summary>
internal sealed class HandSteeringVisuals : IDisposable
{
    private const int Segments = 32;
    private readonly GameObject root;
    private readonly Material material;
    private readonly LineRenderer anchorRing;
    private readonly LineRenderer directionDot;
    private readonly LineRenderer handMarker;
    private float opacity;

    public HandSteeringVisuals()
    {
        var shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogWarning("[HandSteering] Sprites/Default shader unavailable; steering indicator will not be displayed.");
            return;
        }
        material = new Material(shader) { hideFlags = HideFlags.DontSave };
        root = new GameObject("Hand Steering Indicator") { hideFlags = HideFlags.DontSave };
        anchorRing = MakeLine("Neutral anchor");
        directionDot = MakeLine("Steering direction");
        handMarker = MakeLine("Hand position");
    }

    private LineRenderer MakeLine(string name)
    {
        var child = new GameObject(name) { hideFlags = HideFlags.DontSave };
        child.transform.SetParent(root.transform, false);
        var line = child.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = Segments;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    public void Update(bool active, Vector3 anchor, Vector3 hand, Transform camera, float input, HandSteeringSettings settings)
    {
        if (root == null) return;
        float desired = active ? Mathf.Clamp01(settings.indicatorOpacity) : 0f;
        opacity = Mathf.MoveTowards(opacity, desired, Time.unscaledDeltaTime / Mathf.Max(0.01f, settings.indicatorFadeTime));
        if (active && camera != null)
        {
            float radius = Mathf.Max(0.005f, settings.indicatorRadius);
            Vector3 right = camera.right;
            Vector3 up = camera.up;
            DrawCircle(anchorRing, anchor, right, up, radius, 0.0015f);
            DrawCircle(directionDot, anchor + right * (Mathf.Clamp(input, -1f, 1f) * radius * 0.72f), right, up, radius * 0.12f, 0.0025f);
            Vector3 towardCamera = (camera.position - hand).normalized;
            DrawCircle(handMarker, hand + towardCamera * 0.02f, right, up, radius * 0.24f, 0.0015f);
        }
        var color = new Color(0.65f, 0.9f, 1f, opacity);
        SetColor(anchorRing, color);
        SetColor(directionDot, color);
        SetColor(handMarker, new Color(color.r, color.g, color.b, opacity * 0.7f));
    }

    private static void DrawCircle(LineRenderer line, Vector3 center, Vector3 right, Vector3 up, float radius, float width)
    {
        line.startWidth = line.endWidth = width;
        for (int i = 0; i < Segments; i++)
        {
            float angle = i * (Mathf.PI * 2f / Segments);
            line.SetPosition(i, center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius);
        }
    }

    private static void SetColor(LineRenderer line, Color color)
    {
        line.startColor = color;
        line.endColor = color;
        line.enabled = color.a > 0.001f;
    }

    public void Dispose()
    {
        if (root != null) UnityEngine.Object.Destroy(root);
        if (material != null) UnityEngine.Object.Destroy(material);
    }
}
