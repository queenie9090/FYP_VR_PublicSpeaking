#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Temporary development-only Hall loading trace; remove after the loading cause is identified.</summary>
public sealed class HallLoadTrace : MonoBehaviour
{
    private static HallLoadTrace instance;
    private static long requestTicks;
    private static bool requested;
    private static readonly FrameTiming[] timing = new FrameTiming[1];
    private bool observing;
    private bool firstFrame;
    private double loadedAt, nextSummary;
    private int frames, slow, worstFrame;
    private double frameSeconds;
    private float worstMs;
    private long peakGcBytes;
    private ProfilerRecorder gcRecorder;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (instance != null) return;
        var host = new GameObject("Hall Load Trace (development)");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<HallLoadTrace>();
        SceneManager.sceneLoaded += instance.OnSceneLoaded;
    }

    internal static void BeginRequest(string sceneName)
    {
        if (sceneName != "Hall") return;
        requestTicks = Stopwatch.GetTimestamp();
        requested = true;
        Mark("request-begin", sceneName);
    }

    internal static void Mark(string stage, string sceneName)
    {
        if (sceneName != "Hall" || !requested) return;
        double wallMs = (Stopwatch.GetTimestamp() - requestTicks) * 1000.0 / Stopwatch.Frequency;
        UnityEngine.Debug.Log($"[HallLoad] stage={stage} requestWallMs={wallMs:F1} frame={Time.frameCount} realtime={Time.realtimeSinceStartupAsDouble:F3}");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (observing) { gcRecorder.Dispose(); observing = false; }
        if (scene.name != "Hall") return;
        if (requested) Mark("scene-loaded-after-awake-before-start", scene.name);
        else UnityEngine.Debug.Log($"[HallLoad] stage=scene-loaded-direct-launch requestWallMs=n/a frame={Time.frameCount} realtime={Time.realtimeSinceStartupAsDouble:F3}");
        requested = false;
        loadedAt = Time.realtimeSinceStartupAsDouble;
        nextSummary = loadedAt + 5.0;
        firstFrame = true;
        frames = slow = worstFrame = 0;
        frameSeconds = 0;
        worstMs = 0;
        peakGcBytes = 0;
        gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        observing = true;
    }

    private void Update()
    {
        if (!observing) return;
        double now = Time.realtimeSinceStartupAsDouble;
        if (firstFrame)
        {
            firstFrame = false;
            UnityEngine.Debug.Log($"[HallLoad] stage=first-update afterSceneLoadedMs={(now - loadedAt) * 1000:F1} frame={Time.frameCount} realtime={now:F3}");
        }
        float ms = Time.unscaledDeltaTime * 1000f;
        frames++;
        frameSeconds += Time.unscaledDeltaTime;
        if (ms > 20f) slow++;
        if (ms > worstMs) { worstMs = ms; worstFrame = Time.frameCount - 1; }
        if (gcRecorder.Valid && gcRecorder.Count > 0)
            peakGcBytes = System.Math.Max(peakGcBytes, gcRecorder.LastValue);
        if (now < nextSummary) return;

        uint count = FrameTimingManager.GetLatestTimings(1, timing);
        string gpu = count > 0 && timing[0].gpuFrameTime > 0 ? timing[0].gpuFrameTime.ToString("F1") : "n/a";
        string wait = count > 0 && timing[0].cpuMainThreadFrameTime > 0 ? timing[0].cpuMainThreadPresentWaitTime.ToString("F1") : "n/a";
        string gc = gcRecorder.Valid && gcRecorder.Count > 0 ? peakGcBytes.ToString() : "n/a";
        UnityEngine.Debug.Log($"[HallLoad] stage=startup-window sinceLoadedMs={(now - loadedAt) * 1000:F0} frames={frames} avgMs={(frames > 0 ? frameSeconds * 1000 / frames : 0):F1} worstMs={worstMs:F1} worstFrame~={worstFrame} slowOver20Ms={slow} peakGcBytes={gc} latestDelayedGpuMs={gpu} latestDelayedPresentWaitMs={wait} unityAllocatedBytes={UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()} frame={Time.frameCount} realtime={now:F3}");
        frames = slow = 0; frameSeconds = 0; worstMs = 0; peakGcBytes = 0;
        nextSummary = now + 5.0;
        if (now - loadedAt >= 30.0) { observing = false; gcRecorder.Dispose(); }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (observing) gcRecorder.Dispose();
        if (instance == this) instance = null;
    }
}
#endif
