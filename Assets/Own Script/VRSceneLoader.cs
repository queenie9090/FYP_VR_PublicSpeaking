using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

public class VRSceneLoader : MonoBehaviour
{
    /// <summary>
    /// Initializes OpenXR subsystems on demand and transitions to the target VR scene.
    /// </summary>
    public static IEnumerator EnableVROnDemandAndLoad(string targetSceneName)
    {
        Debug.Log($"[VR Loader] Initializing XR Subsystems for scene: '{targetSceneName}'...");

        // 1. Temporarily pause AudioListener to prevent audio pops during initialization
        AudioListener.pause = true;

        // 2. Initialize XR Manager loader if not already running
        if (XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null)
        {
            if (!XRGeneralSettings.Instance.Manager.isInitializationComplete)
            {
                yield return XRGeneralSettings.Instance.Manager.InitializeLoader();
            }

            if (XRGeneralSettings.Instance.Manager.activeLoader == null)
            {
                Debug.LogError("[VR Loader] Failed to initialize XR Loader! Check OpenXR settings.");
                AudioListener.pause = false;
                yield break;
            }

            // 3. Start XR rendering, tracking, and hand-tracking subsystems
            Debug.Log("[VR Loader] Starting XR Subsystems...");
            XRGeneralSettings.Instance.Manager.StartSubsystems();
        }

        // 4. Restore audio routing
        AudioListener.pause = false;

        // 5. Load the target VR scene
        Debug.Log($"[VR Loader] Loading scene: '{targetSceneName}'");
        SceneManager.LoadScene(targetSceneName);
    }

    /// <summary>
    /// Deinitializes XR subsystems when returning to the 2D desktop Main Menu.
    /// </summary>
    public static IEnumerator DisableVRAndReturnToMenu(string menuSceneName)
    {
        if (XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null)
        {
            var manager = XRGeneralSettings.Instance.Manager;
            if (manager.isInitializationComplete)
            {
                manager.StopSubsystems();
                manager.DeinitializeLoader();
            }
        }

        yield return null;                                   // let XR finish shutting down
        yield return new WaitForSecondsRealtime(0.25f);      // give the audio device time to switch
        AudioSettings.Reset(AudioSettings.GetConfiguration());  // re-pick default output

        SceneManager.LoadScene(menuSceneName);
    }
}