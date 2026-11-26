using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using Unity.Collections;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Meta;

public class VRSceneLoader : MonoBehaviour
{
    private static bool vrSubsystemsStarted;
    public static void MarkVRSubsystemsStarted() => vrSubsystemsStarted = true;

#if UNITY_EDITOR
    private static bool editorVrSimulatorMode;
    public static bool IsEditorSimulatorMode => editorVrSimulatorMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEditorSimulatorMode() => editorVrSimulatorMode = false;

    public static void SetEditorSimulatorMode(bool enabled) => editorVrSimulatorMode = enabled;

    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterPlayModeCleanup()
    {
        UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
        {
            if (XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null)
            {
                var manager = XRGeneralSettings.Instance.Manager;
                if (manager.isInitializationComplete)
                {
                    try { manager.StopSubsystems(); } catch { }
                    try { manager.DeinitializeLoader(); } catch { }
                }
            }
            vrSubsystemsStarted = false;
        }
    }
#endif

    /// <summary>
    /// Initializes OpenXR subsystems on demand and loads the target VR scene asynchronously.
    /// </summary>
    public static IEnumerator EnableVROnDemandAndLoad(string targetSceneName)
    {
        if (!Application.CanStreamedLevelBeLoaded(targetSceneName))
        {
            Debug.LogError($"[VR Loader] Scene '{targetSceneName}' is unavailable.");
            yield break;
        }

#if UNITY_EDITOR
        // If Simulator Mode is active, bypass physical OpenXR initialization for ALL VR scenarios
        if (editorVrSimulatorMode)
        {
            yield return SceneManager.LoadSceneAsync(targetSceneName);
            yield break;
        }
#endif
        var xrManager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        if (xrManager == null)
        {
            Debug.LogError("[VR Loader] XR Manager is unavailable.");
            yield break;
        }

        if (!xrManager.isInitializationComplete)
        {
            yield return xrManager.InitializeLoader();
        }

        if (xrManager.activeLoader == null)
        {
            Debug.LogError("[VR Loader] Failed to initialize XR Loader! Check OpenXR settings.");
            yield break;
        }

        if (!vrSubsystemsStarted)
        {
            xrManager.StartSubsystems();
            vrSubsystemsStarted = true;
        }

        // The runtime can take a frame to start its display subsystem after StartSubsystems.
        yield return null;
        RequestTargetRefreshRate(xrManager);

        yield return SceneManager.LoadSceneAsync(targetSceneName);
    }

    private static void RequestTargetRefreshRate(XRManagerSettings manager)
    {
        var settings = OpenXRSettings.Instance;
        var feature = settings != null ? settings.GetFeature<DisplayUtilitiesFeature>() : null;
        if (feature == null || !feature.enabled || manager.activeLoader == null) return;

        var display = manager.activeLoader.GetLoadedSubsystem<XRDisplaySubsystem>();
        if (display == null || !display.running)
        {
            Debug.LogWarning("[VR Refresh] Display subsystem not running; refresh rate was not requested.");
            return;
        }
        if (display.TryGetDisplayRefreshRate(out float current) && Mathf.Abs(current - 90f) < 0.1f)
        {
            Debug.Log("[VR Refresh] Display already reports 90 Hz.");
            return;
        }

        Unity.Collections.NativeArray<float> rates = default;
        try
        {
            if (!display.TryGetSupportedDisplayRefreshRates(Allocator.Temp, out rates))
            {
                Debug.LogWarning("[VR Refresh] Supported refresh rates unavailable; runtime preference retained.");
                return;
            }
            for (int i = 0; i < rates.Length; i++)
            {
                if (Mathf.Abs(rates[i] - 90f) >= 0.1f) continue;
                bool accepted = display.TryRequestDisplayRefreshRate(rates[i]);
                Debug.Log($"[VR Refresh] Requested {rates[i]:F1} Hz; request accepted={accepted}. Actual rate may change later.");
                return;
            }
            Debug.LogWarning("[VR Refresh] 90 Hz is not in the runtime's supported rates; runtime preference retained.");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[VR Refresh] Unable to request a refresh rate; runtime preference retained: {ex.GetType().Name}.");
        }
        finally { if (rates.IsCreated) rates.Dispose(); }
    }

    /// <summary>
    /// Deinitializes XR subsystems and returns immediately to the desktop menu scene.
    /// </summary>
    public static IEnumerator DisableVRAndReturnToMenu(string menuSceneName)
    {
        if (!Application.CanStreamedLevelBeLoaded(menuSceneName))
        {
            Debug.LogError($"[VR Loader] Scene '{menuSceneName}' is unavailable.");
            yield break;
        }

        if (XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null)
        {
            var manager = XRGeneralSettings.Instance.Manager;
            if (manager.isInitializationComplete)
            {
                try { manager.StopSubsystems(); } catch { }
                try { manager.DeinitializeLoader(); } catch { }
            }
        }

        vrSubsystemsStarted = false;
        yield return null;
        SceneManager.LoadScene(menuSceneName);
    }
}
