using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>Opt-in, observational diagnostics. Does not start capture or change audio routing.</summary>
public sealed class VoiceRuntimeDiagnostics : MonoBehaviour
{
    [SerializeField] private AudioRecorder recorder;
    [SerializeField, Min(0.5f)] private float pollSeconds = 2f;
    [SerializeField, Min(1f)] private float slowFrameMilliseconds = 20f;
    private string lastState;
    private int lastPosition = -1;
    private string lastActive;
    private int slowFrames;
    private float worstFrameMs;

    private void OnEnable() { StartCoroutine(Observe()); }
    private void OnDisable() { StopAllCoroutines(); }

    private void Update()
    {
        float ms = Time.unscaledDeltaTime * 1000f;
        if (ms >= slowFrameMilliseconds) { slowFrames++; worstFrameMs = Mathf.Max(worstFrameMs, ms); }
    }

    private IEnumerator Observe()
    {
        while (true)
        {
            string[] devices = Microphone.devices;
            var hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            string presence = hmd.isValid && hmd.TryGetFeatureValue(CommonUsages.userPresence, out bool present)
                ? (present ? "worn" : "not worn") : "unknown/unsupported";
            string permission = "not exposed by Unity on this platform; check OS privacy settings";
#if UNITY_ANDROID && !UNITY_EDITOR
            permission = Permission.HasUserAuthorizedPermission(Permission.Microphone) ? "granted" : "not granted";
#endif
            string selected = recorder != null ? recorder.SelectedMicrophoneDevice : null;
            string active = recorder != null ? recorder.ActiveMicrophoneDevice : null;
            bool capturing = recorder != null && recorder.IsCapturing;
            string state = $"presence={presence}; permission={permission}; devices=[{string.Join(", ", devices)}]; selected={selected ?? "<none>"}; active={active ?? "<none>"}; capturing={capturing}";
            if (state != lastState)
            {
                Debug.Log("[VoiceDiag] " + state + $" frame={Time.frameCount} time={Time.realtimeSinceStartupAsDouble:F3}");
                foreach (string device in devices)
                {
                    Microphone.GetDeviceCaps(device, out int min, out int max);
                    Debug.Log($"[VoiceDiag] input='{device}' frequency range={min}..{max} Hz (0..0 means unrestricted)");
                }
                lastState = state;
            }
            if (capturing)
            {
                int position = Microphone.GetPosition(active);
                if (active == lastActive && position == lastPosition)
                    Debug.LogWarning($"[VoiceDiag] Capture position did not advance: device='{active}', position={position}. Check permission, route and driver; a looping buffer can also wrap.");
                lastPosition = position;
                lastActive = active;
            }
            else { lastPosition = -1; lastActive = null; }
            if (slowFrames > 0)
                Debug.Log($"[VoicePerf] frame interval summary: count={slowFrames} worst={worstFrameMs:F1}ms threshold={slowFrameMilliseconds:F1}ms endFrame={Time.frameCount} time={Time.realtimeSinceStartupAsDouble:F3}");
            slowFrames = 0;
            worstFrameMs = 0;
            yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, pollSeconds));
        }
    }
}
