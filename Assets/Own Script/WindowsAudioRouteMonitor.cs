#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using Unity.Profiling;

/// <summary>Observes playback availability during a VR session; never changes capture devices.</summary>
public sealed class WindowsAudioRouteMonitor : MonoBehaviour
{
    const int MaxAttempts = 3;
    static readonly ProfilerMarker PollMarker = new ProfilerMarker("WindowsAudioRoute.Poll");
    string lastEndpoint;
    int lastPresence = -2;
    int attempts;
    bool routed;
    bool stopping;

    void OnEnable()
    {
        stopping = false;
        AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        StartCoroutine(Monitor());
    }

    void OnDisable()
    {
        stopping = true;
        StopAllCoroutines();
        AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
    }

    void OnApplicationQuit()
    {
        stopping = true;
        WindowsAudioRouter.RestoreDesktopSpeakers();
    }

    static void OnAudioConfigurationChanged(bool deviceChanged)
    {
        Debug.Log($"[WindowsAudioRouter] Unity audio configuration changed; externalDeviceChange={deviceChanged}. This does not identify the active stream endpoint.");
    }

    IEnumerator Monitor()
    {
        var delay = new WaitForSecondsRealtime(2f);
        while (!stopping && isActiveAndEnabled)
        {
            var hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            int presence = hmd.isValid && hmd.TryGetFeatureValue(CommonUsages.userPresence, out bool present) ? (present ? 1 : 0) : -1;
            string endpoint;
            using (PollMarker.Auto())
                endpoint = WindowsAudioRouter.ProbeQuestEndpoint();
            if (stopping) yield break;

            if (endpoint != lastEndpoint || presence != lastPresence)
            {
                Debug.Log($"[WindowsAudioRouter] VR route availability changed: Quest endpoint={endpoint ?? "unavailable or ambiguous"}, userPresence={(presence < 0 ? "unknown" : presence == 1 ? "present" : "absent")}.");
                lastEndpoint = endpoint;
                lastPresence = presence;
                attempts = 0;
                routed = false;
            }
            bool defaultsActive = true;
            if (routed && endpoint != null)
            {
                using (PollMarker.Auto())
                    defaultsActive = WindowsAudioRouter.AreQuestDefaultsActive(endpoint);
            }
            if (routed && endpoint != null && !defaultsActive)
            {
                Debug.LogWarning("[WindowsAudioRouter] Windows playback defaults changed during VR; retrying the Quest route.");
                routed = false;
                attempts = 0;
            }
            if (endpoint != null && presence != 0 && !routed && attempts < MaxAttempts)
            {
                attempts++;
                bool alreadySelected = WindowsAudioRouter.AreQuestDefaultsActive(endpoint);
                routed = WindowsAudioRouter.SwitchToOculusHeadset();
#if UNITY_EDITOR_WIN
                if (routed && !alreadySelected)
                    EditorAudioRouteRefresh.TryRefresh("Quest playback default changed");
#endif
                if (!routed && attempts == MaxAttempts)
                    Debug.LogWarning("[WindowsAudioRouter] Routing retries exhausted; a device or headset state change will trigger another attempt.");
            }
            yield return delay;
        }
    }
}
#endif
