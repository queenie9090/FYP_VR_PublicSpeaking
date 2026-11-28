using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        HallLoadTrace.BeginRequest(targetSceneName);
#endif
        if (!Application.CanStreamedLevelBeLoaded(targetSceneName))
        {
            Debug.LogError($"[VR Loader] Scene '{targetSceneName}' is unavailable.");
            yield break;
        }

#if UNITY_EDITOR
        // If Simulator Mode is active, bypass physical OpenXR initialization for ALL VR scenarios
        if (editorVrSimulatorMode)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            HallLoadTrace.Mark("scene-load-async-request", targetSceneName);
#endif
            var simulatorLoad = SceneManager.LoadSceneAsync(targetSceneName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            HallLoadTrace.Mark("scene-load-async-returned", targetSceneName);
#endif
            yield return simulatorLoad;
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            HallLoadTrace.Mark("xr-initialize-begin", targetSceneName);
#endif
            yield return xrManager.InitializeLoader();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            HallLoadTrace.Mark("xr-initialize-complete", targetSceneName);
#endif
        }

        if (xrManager.activeLoader == null)
        {
            Debug.LogError("[VR Loader] Failed to initialize XR Loader! Check OpenXR settings.");
            yield break;
        }

        if (!vrSubsystemsStarted)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            HallLoadTrace.Mark("xr-subsystems-start-begin", targetSceneName);
#endif
            xrManager.StartSubsystems();
            vrSubsystemsStarted = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            HallLoadTrace.Mark("xr-subsystems-start-complete", targetSceneName);
#endif
        }

        // The runtime can take a frame to start its display subsystem after StartSubsystems.
        yield return null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        HallLoadTrace.Mark("refresh-request-begin", targetSceneName);
#endif
        RequestTargetRefreshRate(xrManager);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        HallLoadTrace.Mark("refresh-request-complete", targetSceneName);
        HallLoadTrace.Mark("scene-load-async-request", targetSceneName);
#endif

        var load = SceneManager.LoadSceneAsync(targetSceneName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        HallLoadTrace.Mark("scene-load-async-returned", targetSceneName);
#endif
        yield return load;
    }

    private static void RequestTargetRefreshRate(XRManagerSettings manager)
    {
        // Refresh rate is controlled by the Meta Quest Link PC app on Windows PC-VR.
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
