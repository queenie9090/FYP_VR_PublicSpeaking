using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Secondary Gesture Engine: Detects zone transits between Rest Zone (A) and Gesture Zone (B).
/// Includes boundary debounce lockouts to prevent edge-flicker double-counting.
/// </summary>
public class ArmTransitDetector : MonoBehaviour
{
    [Header("Tracker Reference")]
    [SerializeField] private GestureTracker gestureTracker;

    [Header("Zones Layout")]
    [SerializeField] private bool followHeadset = true;
    [SerializeField] private bool autoGenerateZones = true;
    [SerializeField] private Transform headsetTransform;
    [SerializeField] private Transform bodyReference;

    [Header("Collider Zones (manual assignment)")]
    [SerializeField] private Collider restZoneCollider;
    [SerializeField] private Collider gestureZoneCollider;

    [Header("Hand / Controller Probes (manual)")]
    [SerializeField] private Collider leftHandProbe;
    [SerializeField] private Collider rightHandProbe;
    [SerializeField] private Collider leftControllerProbe;
    [SerializeField] private Collider rightControllerProbe;
    [SerializeField] private float probeRadius = 0.05f;
    [SerializeField] private bool showProbeGizmos = true;

    [Header("Testing")]
    [SerializeField] private bool testModeAlwaysOn = false;

    [Header("Transit Timing & Boundary Tolerance")]
    [SerializeField] private float minOptimalDuration = 0.30f;
    [SerializeField] private float maxOptimalDuration = 2.5f;
    [SerializeField] private float boundaryDebounceTime = 0.50f; // Edge tolerance lockout
    [SerializeField] private bool countReturnTrip = false;         // Downward trip resets state only

    [Header("Gizmo Visualization Settings")]
    [SerializeField] private bool showGizmos = true;
    [SerializeField] private Color restZoneColor = new Color(0f, 1f, 0f, 0.35f);
    [SerializeField] private Color gestureZoneColor = new Color(0f, 0.5f, 1f, 0.35f);

    private const float RestY = -0.70f, RestH = 0.30f;
    private const float GestureY = -0.22f, GestureH = 0.36f;

    private class HandState
    {
        public bool inTransit;
        public bool goingUp;
        public float startTime;
        public float lastExitLockout;
    }

    private readonly Dictionary<Collider, HandState> hands = new Dictionary<Collider, HandState>();
    private readonly List<GameObject> probes = new List<GameObject>();
    private readonly HashSet<Collider> activeProbeColliders = new HashSet<Collider>();
    private Collider leftProbeCol;
    private Collider rightProbeCol;
    private bool restIsAuto = false;
    private bool gestureIsAuto = false;

    private void Awake()
    {
        if (autoGenerateZones) CreateDefaultZones();
        PrepareZone(restZoneCollider, true);
        PrepareZone(gestureZoneCollider, false);
        SetZonesActive(false);
    }

    private IEnumerator Start()
    {
        if (gestureTracker == null) gestureTracker = FindObjectOfType<GestureTracker>();
        if (headsetTransform == null && Camera.main != null) headsetTransform = Camera.main.transform;

        if (testModeAlwaysOn)
        {
            yield return new WaitForSeconds(1f);
            SetZonesActive(true);
            if (gestureTracker != null) gestureTracker.StartTracking();
        }
    }

    private void CreateDefaultZones()
    {
        if (headsetTransform == null && Camera.main != null) headsetTransform = Camera.main.transform;

        if (restZoneCollider == null)
        {
            restZoneCollider = MakeZone("Auto_RestZone_A", new Vector3(0.6f, RestH, 0.5f)).GetComponent<BoxCollider>();
            restIsAuto = true;
        }

        if (gestureZoneCollider == null)
        {
            gestureZoneCollider = MakeZone("Auto_GestureZone_B", new Vector3(0.7f, GestureH, 0.5f)).GetComponent<BoxCollider>();
            gestureIsAuto = true;
        }
    }

    private GameObject MakeZone(string zoneName, Vector3 size)
    {
        var go = new GameObject(zoneName);
        go.layer = 2; // Ignore Raycast
        var box = go.AddComponent<BoxCollider>();
        box.size = size;
        return go;
    }

    private void PrepareZone(Collider zone, bool isRest)
    {
        if (zone == null) return;
        zone.isTrigger = true;
        if (zone.GetComponentInParent<Rigidbody>() == null)
        {
            var rb = zone.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        var proxy = zone.GetComponent<ZoneTriggerProxy>() ?? zone.gameObject.AddComponent<ZoneTriggerProxy>();
        proxy.Initialize(this, isRest);
    }

    public void SetZonesActive(bool active)
    {
        hands.Clear();
        ClearProbes();
        if (restZoneCollider != null) restZoneCollider.gameObject.SetActive(active);
        if (gestureZoneCollider != null) gestureZoneCollider.gameObject.SetActive(active);
        if (active) AttachProbes();
    }

    private void AttachProbes()
    {
        activeProbeColliders.Clear();
        string mode = UserSession.InputMode;
        bool useHands = !string.IsNullOrEmpty(mode) && mode.Contains("Hand");

        Collider manualLeft = useHands ? leftHandProbe : leftControllerProbe;
        Collider manualRight = useHands ? rightHandProbe : rightControllerProbe;

        if (manualLeft != null || manualRight != null)
        {
            RegisterManualProbe(manualLeft, "Left", true);
            RegisterManualProbe(manualRight, "Right", false);
            return;
        }

        if (gestureTracker == null) gestureTracker = FindObjectOfType<GestureTracker>();
        if (gestureTracker == null) return;

        var (left, right) = gestureTracker.GetActiveSources();
        MakeProbe(left, "Left", true);
        MakeProbe(right, "Right", false);
    }

    private void RegisterManualProbe(Collider probe, string label, bool isLeft)
    {
        if (probe == null) return;
        probe.isTrigger = true;
        if (probe.GetComponentInParent<Rigidbody>() == null)
        {
            var rb = probe.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        activeProbeColliders.Add(probe);
        if (isLeft) leftProbeCol = probe; else rightProbeCol = probe;
    }

    private void MakeProbe(Transform source, string label, bool isLeft)
    {
        if (source == null) return;
        var go = new GameObject($"ArmProbe_{label}");
        go.transform.SetParent(source, false);
        go.layer = 2;
        var sphere = go.AddComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = probeRadius;

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        probes.Add(go);
        activeProbeColliders.Add(sphere);
        if (isLeft) leftProbeCol = sphere; else rightProbeCol = sphere;
    }

    private void ClearProbes()
    {
        foreach (var p in probes) if (p != null) Destroy(p);
        probes.Clear();
        activeProbeColliders.Clear();
        leftProbeCol = rightProbeCol = null;
    }

    public (Transform left, Transform right) GetActiveProbeTransforms()
    {
        return (leftProbeCol != null ? leftProbeCol.transform : null,
                rightProbeCol != null ? rightProbeCol.transform : null);
    }

    private int GetHandIndex(Collider c)
    {
        if (c == leftProbeCol) return 0;
        if (c == rightProbeCol) return 1;
        return -1;
    }

    private HandState Get(Collider c)
    {
        if (!hands.TryGetValue(c, out var s)) hands[c] = s = new HandState();
        return s;
    }

    private void Update()
    {
        if (followHeadset && headsetTransform != null && (restIsAuto || gestureIsAuto))
        {
            Transform yawSource = bodyReference != null ? bodyReference : headsetTransform;
            Vector3 fwd = Vector3.ProjectOnPlane(yawSource.forward, Vector3.up);
            if (fwd.sqrMagnitude > 0.0001f)
            {
                fwd.Normalize();
                Quaternion rot = Quaternion.LookRotation(fwd);
                Vector3 head = headsetTransform.position;

                if (restIsAuto && restZoneCollider != null)
                    restZoneCollider.transform.SetPositionAndRotation(head + fwd * 0.2f + Vector3.up * RestY, rot);

                if (gestureIsAuto && gestureZoneCollider != null)
                    gestureZoneCollider.transform.SetPositionAndRotation(head + fwd * 0.35f + Vector3.up * GestureY, rot);
            }
        }
    }

    // ---------------- Trigger Callbacks ----------------
    public void OnHandEnterRestZone(Collider other)
    {
        if (!IsPlayerHand(other)) return;
        var s = Get(other);
        s.inTransit = false; // Reset state when returning to lap
    }

    public void OnHandExitRestZone(Collider other)
    {
        if (!IsPlayerHand(other)) return;
        var s = Get(other);

        // Debounce boundary jitter
        if (Time.time - s.lastExitLockout < boundaryDebounceTime) return;

        s.inTransit = true;
        s.goingUp = true;
        s.startTime = Time.time;
        s.lastExitLockout = Time.time;
    }

    public void OnHandEnterGestureZone(Collider other)
    {
        if (!IsPlayerHand(other)) return;
        var s = Get(other);

        if (s.inTransit && s.goingUp)
        {
            float duration = Time.time - s.startTime;
            s.inTransit = false;

            if (duration >= minOptimalDuration && duration <= maxOptimalDuration && gestureTracker != null)
            {
                gestureTracker.RegisterExpressiveArmTransit(duration, GetHandIndex(other));
            }
        }
    }

    public void OnHandExitGestureZone(Collider other)
    {
        if (!IsPlayerHand(other)) return;
        var s = Get(other);

        if (countReturnTrip && Time.time - s.lastExitLockout >= boundaryDebounceTime)
        {
            s.inTransit = true;
            s.goingUp = false;
            s.startTime = Time.time;
            s.lastExitLockout = Time.time;
        }
    }

    private bool IsPlayerHand(Collider col)
    {
        if (col == null) return false;
        if (activeProbeColliders.Count > 0) return activeProbeColliders.Contains(col);

        string n = col.name.ToLower();
        return col.CompareTag("PlayerHand") || col.CompareTag("GameController") || n.Contains("hand") || n.Contains("controller") || n.Contains("probe");
    }

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;
        if (restZoneCollider != null && restZoneCollider is BoxCollider boxA)
        {
            Gizmos.color = restZoneColor;
            Gizmos.DrawCube(boxA.transform.TransformPoint(boxA.center), Vector3.Scale(boxA.size, boxA.transform.lossyScale));
        }
        if (gestureZoneCollider != null && gestureZoneCollider is BoxCollider boxB)
        {
            Gizmos.color = gestureZoneColor;
            Gizmos.DrawCube(boxB.transform.TransformPoint(boxB.center), Vector3.Scale(boxB.size, boxB.transform.lossyScale));
        }
    }
}

public class ZoneTriggerProxy : MonoBehaviour
{
    private ArmTransitDetector detector;
    private bool isRestZone;

    public void Initialize(ArmTransitDetector parentDetector, bool isRestZone)
    {
        this.detector = parentDetector;
        this.isRestZone = isRestZone;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (detector == null) return;
        if (isRestZone) detector.OnHandEnterRestZone(other);
        else detector.OnHandEnterGestureZone(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (detector == null) return;
        if (isRestZone) detector.OnHandExitRestZone(other);
        else detector.OnHandExitGestureZone(other);
    }
}