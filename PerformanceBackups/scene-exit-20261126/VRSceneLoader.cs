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
    /// Initializes OpenXR subsystems on demand and loads the target VR scene immediately.
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
            SceneManager.LoadScene(targetSceneName);
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

        SceneManager.LoadScene(targetSceneName);
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