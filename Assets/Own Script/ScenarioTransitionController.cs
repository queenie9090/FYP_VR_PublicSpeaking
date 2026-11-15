using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

public class ScenarioTransitionController : MonoBehaviour
{
    public enum EditorXRTestMode
    {
        AutoDetect,         // Use physical Quest headset if connected; automatically fall back to PC Simulator if not
        PhysicalVRHeadset,  // Force physical Quest/OpenXR initialization
        DesktopSimulator    // Force PC / XR Device Simulator mode (skip physical headset)
    }

    [SerializeField] private string defaultDashboardScene = "DashboardScene";

#if UNITY_EDITOR
    [Header("Editor Testing Options")]
    [Tooltip("AutoDetect = Use Quest headset if Link is connected, otherwise fall back to PC Simulator.\nPhysicalVRHeadset = Always initialize physical Quest.\nDesktopSimulator = Always skip headset and test on PC.")]
    [SerializeField] private EditorXRTestMode editorTestMode = EditorXRTestMode.AutoDetect;

    private void Awake()
    {
        // Sync simulator flag early so MainMenu and direct-scene launches share the exact same mode
        if (editorTestMode == EditorXRTestMode.DesktopSimulator)
        {
            VRSceneLoader.SetEditorSimulatorMode(true);
        }
        else if (editorTestMode == EditorXRTestMode.PhysicalVRHeadset)
        {
            VRSceneLoader.SetEditorSimulatorMode(false);
        }
    }
#endif

    public void InitializeXRForEditor(MonoBehaviour runner, System.Action onComplete)
    {
#if UNITY_EDITOR
        if (XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null)
        {
            if (!XRGeneralSettings.Instance.Manager.isInitializationComplete)
            {
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
        // Option 1: Explicit Desktop / Simulator mode
        if (editorTestMode == EditorXRTestMode.DesktopSimulator || VRSceneLoader.IsEditorSimulatorMode)
        {
            VRSceneLoader.SetEditorSimulatorMode(true);
            Debug.Log("[ScenarioTransitionController] Desktop/Simulator mode active: skipping physical XR Loader.");
            yield return null;
            onComplete?.Invoke();
            yield break;
        }

        // Option 2 & 3: Try initializing physical OpenXR headset (PhysicalVRHeadset or AutoDetect)
        var manager = XRGeneralSettings.Instance.Manager;
        Debug.Log($"[ScenarioTransitionController] Attempting OpenXR initialization ({editorTestMode})...");
        yield return manager.InitializeLoader();

        if (manager.activeLoader != null)
        {
            VRSceneLoader.SetEditorSimulatorMode(false);
            manager.StartSubsystems();
            VRSceneLoader.MarkVRSubsystemsStarted();
            Debug.Log("[ScenarioTransitionController] Physical Quest/OpenXR initialized and subsystems started.");
        }
        else if (editorTestMode == EditorXRTestMode.AutoDetect)
        {
            // No headset connected -> seamlessly fall back to PC Simulator without throwing an error
            VRSceneLoader.SetEditorSimulatorMode(true);
            Debug.Log("[ScenarioTransitionController] No Quest headset detected; automatically fell back to Desktop/Simulator mode.");
        }
        else
        {
            Debug.LogError("[ScenarioTransitionController] PhysicalVRHeadset mode selected, but XR Loader failed to initialize. Check Meta Horizon Link.");
        }

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
                Debug.Log("[ScenarioTransitionController] Editor Play Mode stopped. OpenXR deinitialized.");
            }
        }
    }
#endif

    public IEnumerator ExecuteSceneTransitionRoutine(ScenarioConfig activeConfig, DialogueUIManager uiManager)
    {
        string targetScene = defaultDashboardScene;
        string currentSceneName = SceneManager.GetActiveScene().name;

        bool isExitingFromTutorial = UserSession.IsRunningTutorial ||
                                     currentSceneName == "TutorialScene" ||
                                     (activeConfig != null && activeConfig.scenario == "TutorialScene");

        if (isExitingFromTutorial)
        {
            UserSession.IsRunningTutorial = false;
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