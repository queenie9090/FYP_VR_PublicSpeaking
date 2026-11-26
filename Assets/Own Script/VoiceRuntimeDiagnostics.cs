using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using Unity.Profiling;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>Opt-in, observational diagnostics. Does not start capture or change audio routing.</summary>
public sealed class VoiceRuntimeDiagnostics : MonoBehaviour
{
    [SerializeField] private AudioRecorder recorder;
    [SerializeField, Min(0.5f)] private float pollSeconds = 2f;
    [SerializeField, Min(1f)] private float slowFrameMilliseconds = 20f;
    [SerializeField, Tooltip("Collects frame timing and profiler counters; use for development profiling.")]
    private bool collectPerformanceMetrics = true;
    [SerializeField, Min(1f)] private float targetFrameMilliseconds = 11.111f;
    private string lastState;
    private int lastPosition = -1;
    private string lastActive;
    private int slowFrames;
    private float worstFrameMs;
    private int worstFrameNumber;
    private int frameCount;
    private int overBudgetFrames;
    private double frameSeconds;
    private long gcBytesTotal;
    private long gcBytesMax;
    private int gcMaxFrame;
    private readonly FrameTiming[] frameTimings = new FrameTiming[1];
    private ProfilerRecorder gcAllocated;
    private ProfilerRecorder totalUsed;
    private ProfilerRecorder drawCalls;
    private ProfilerRecorder triangles;
    private ProfilerRecorder vertices;
    private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
    private bool xrConfigurationLogged;

    private void OnEnable()
    {
        if (collectPerformanceMetrics)
        {
            gcAllocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            totalUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            vertices = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
        }
        StartCoroutine(Observe());
    }
    private void OnDisable()
    {
        StopAllCoroutines();
        gcAllocated.Dispose(); totalUsed.Dispose(); drawCalls.Dispose(); triangles.Dispose(); vertices.Dispose();
        frameCount = overBudgetFrames = slowFrames = 0;
        frameSeconds = 0;
        worstFrameMs = 0;
        gcBytesTotal = gcBytesMax = 0;
    }

    private void Update()
    {
        float ms = Time.unscaledDeltaTime * 1000f;
        if (ms >= slowFrameMilliseconds) slowFrames++;
        if (ms > worstFrameMs) { worstFrameMs = ms; worstFrameNumber = Time.frameCount - 1; }
        if (collectPerformanceMetrics)
        {
            frameCount++;
            frameSeconds += Time.unscaledDeltaTime;
            if (ms > targetFrameMilliseconds) overBudgetFrames++;
            if (gcAllocated.Valid && gcAllocated.Count > 0)
            {
                long bytes = gcAllocated.LastValue;
                gcBytesTotal += bytes;
                if (bytes > gcBytesMax) { gcBytesMax = bytes; gcMaxFrame = Time.frameCount - 1; }
            }
            FrameTimingManager.CaptureFrameTimings();
        }
    }

    private IEnumerator Observe()
    {
        while (true)
        {
            string[] devices = VoiceMicTrace.Devices();
            var hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!xrConfigurationLogged && hmd.isValid)
            {
                SubsystemManager.GetSubsystems(displays);
                foreach (var display in displays)
                {
                    if (!display.running || !display.TryGetDisplayRefreshRate(out float hz) || hz <= 0f) continue;
                    Debug.Log($"[VoicePerf] XR config refreshHz={hz:F1} eye={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} eyeScale={XRSettings.eyeTextureResolutionScale:F2} viewportScale={XRSettings.renderViewportScale:F2} stereo={XRSettings.stereoRenderingMode} quality={QualitySettings.GetQualityLevel()} vSyncCount={QualitySettings.vSyncCount} targetFrameRate={Application.targetFrameRate} frame={Time.frameCount}");
                    xrConfigurationLogged = true;
                    break;
                }
            }
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
                    VoiceMicTrace.DeviceCaps(device, out int min, out int max);
                    Debug.Log($"[VoiceDiag] input='{device}' frequency range={min}..{max} Hz (0..0 means unrestricted)");
                }
                lastState = state;
            }
            if (capturing)
            {
                int position = VoiceMicTrace.Position(active);
                if (active == lastActive && position == lastPosition)
                    Debug.LogWarning($"[VoiceDiag] Capture position did not advance: device='{active}', position={position}. Check permission, route and driver; a looping buffer can also wrap.");
                lastPosition = position;
                lastActive = active;
            }
            else { lastPosition = -1; lastActive = null; }
            if (collectPerformanceMetrics && frameCount > 0)
            {
                uint timings = FrameTimingManager.GetLatestTimings(1, frameTimings);
                string cpu = timings > 0 && frameTimings[0].cpuFrameTime > 0 ? frameTimings[0].cpuFrameTime.ToString("F2") : "n/a";
                string gpu = timings > 0 && frameTimings[0].gpuFrameTime > 0 ? frameTimings[0].gpuFrameTime.ToString("F2") : "n/a";
                string main = timings > 0 && frameTimings[0].cpuMainThreadFrameTime > 0 ? frameTimings[0].cpuMainThreadFrameTime.ToString("F2") : "n/a";
                string presentWait = timings > 0 && frameTimings[0].cpuMainThreadFrameTime > 0 ? frameTimings[0].cpuMainThreadPresentWaitTime.ToString("F2") : "n/a";
                string render = timings > 0 && frameTimings[0].cpuRenderThreadFrameTime > 0 ? frameTimings[0].cpuRenderThreadFrameTime.ToString("F2") : "n/a";
                string gc = gcAllocated.Valid && gcAllocated.Count > 0 ? $"total={gcBytesTotal},max={gcBytesMax},maxFrame~={gcMaxFrame}" : "n/a";
                Debug.Log($"[VoicePerf] interval fps={(frameSeconds > 0 ? frameCount / frameSeconds : 0):F1} avgMs={(frameSeconds * 1000 / frameCount):F2} worstMs={worstFrameMs:F1} worstFrame~={worstFrameNumber} overBudget={overBudgetFrames}/{frameCount} budgetMs={targetFrameMilliseconds:F2} slow={slowFrames} slowMs={slowFrameMilliseconds:F1} latestDelayedCpuMs={cpu} latestDelayedMainMs={main} latestDelayedPresentWaitMs={presentWait} latestDelayedRenderMs={render} latestDelayedGpuMs={gpu} gcBytes({gc}) usedBytes={Counter(totalUsed)} drawCalls={Counter(drawCalls)} triangles={Counter(triangles)} vertices={Counter(vertices)} endFrame={Time.frameCount} time={Time.realtimeSinceStartupAsDouble:F3}");
            }
            else if (slowFrames > 0)
                Debug.Log($"[VoicePerf] frame interval summary: count={slowFrames} worst={worstFrameMs:F1} worstFrame~={worstFrameNumber} threshold={slowFrameMilliseconds:F1}ms endFrame={Time.frameCount} time={Time.realtimeSinceStartupAsDouble:F3}");
            slowFrames = 0;
            worstFrameMs = 0;
            frameCount = overBudgetFrames = 0;
            frameSeconds = 0;
            gcBytesTotal = gcBytesMax = 0;
            yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, pollSeconds));
        }
    }

    private static string Counter(ProfilerRecorder recorder)
    {
        return recorder.Valid && recorder.Count > 0 ? recorder.LastValue.ToString() : "n/a";
    }
}
