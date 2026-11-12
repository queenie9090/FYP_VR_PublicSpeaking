using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

public class ScenarioTransitionController : MonoBehaviour
{
    [SerializeField] private string defaultDashboardScene = "DashboardScene";

    public void InitializeXRForEditor(MonoBehaviour runner, System.Action onComplete)
    {
#if UNITY_EDITOR
        if (XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null)
        {
            if (!XRGeneralSettings.Instance.Manager.isInitializationComplete)
            {
                Debug.Log("[ScenarioTransitionController] Initializing XR for Editor play...");
                runner.StartCoroutine(DebugInitializeXRRoutine(onComplete));
                return;
            }
        }
#endif
        onComplete?.Invoke();
    }

#if UNITY_EDITOR
    private IEnumerator DebugInitializeXRRoutine(System.Action onComplete)
    {
        Debug.Log("[ScenarioTransitionController] Running in Unity Editor / Simulator mode.");
        yield return null;
        onComplete?.Invoke();
    }

    private void OnApplicationQuit()
    {
        if (XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null)
        {
            if (XRGeneralSettings.Instance.Manager.isInitializationComplete)
            {
                XRGeneralSettings.Instance.Manager.StopSubsystems();
                XRGeneralSettings.Instance.Manager.DeinitializeLoader();
                Debug.Log("[ScenarioTransitionController] Editor Play Mode stopped. OpenXR cleanly deinitialized.");
            }
        }
    }
#endif

    public IEnumerator ExecuteSceneTransitionRoutine(ScenarioConfig activeConfig, DialogueUIManager uiManager)
    {
        string targetScene = defaultDashboardScene;
        string currentSceneName = SceneManager.GetActiveScene().name;

        // Direct scene check prevents defaulting to menu when coming out of the Tutorial
        bool isExitingFromTutorial = UserSession.IsRunningTutorial ||
                                     currentSceneName == "TutorialScene" ||
                                     (activeConfig != null && activeConfig.scenario == "TutorialScene");

        if (isExitingFromTutorial)
        {
            UserSession.IsRunningTutorial = false;
            // A standalone tutorial has no saved result to show on a dashboard.
            targetScene = "MainMenu";

            if (UserSession.SelectedScenarioConfig != null &&
                !string.IsNullOrEmpty(UserSession.SelectedScenarioConfig.scenario) &&
                UserSession.SelectedScenarioConfig.scenario != "TutorialScene" &&
                !UserSession.SelectedScenarioConfig.scenario.Equals("DashboardScene", System.StringComparison.OrdinalIgnoreCase) &&
                !UserSession.SelectedScenarioConfig.scenario.Equals("PCDashboardScene", System.StringComparison.OrdinalIgnoreCase) &&
                !UserSession.SelectedScenarioConfig.scenario.Equals("EmotionDashboardScene", System.StringComparison.OrdinalIgnoreCase))
            {
                targetScene = UserSession.SelectedScenarioConfig.scenario;
            }
        }

        if (targetScene == "PCDashboardScene") targetScene = defaultDashboardScene;

        Debug.Log($"[ScenarioTransitionController] Exiting from '{currentSceneName}' to target scene: '{targetScene}'");

        if (targetScene == "MainMenu" || targetScene == defaultDashboardScene || targetScene == "DashboardScene" || targetScene == "PCDashboardScene")
        {
            yield return StartCoroutine(VRSceneLoader.DisableVRAndReturnToMenu(targetScene));
        }
        else
        {
            yield return StartCoroutine(VRSceneLoader.EnableVROnDemandAndLoad(targetScene));
        }
    }
}
