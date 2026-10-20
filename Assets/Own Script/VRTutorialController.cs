using UnityEngine;

public class VRTutorialController : MonoBehaviour
{
    [Header("Tutorial Modules")]
    [SerializeField] private GameObject controllerTutorialGroup;
    [SerializeField] private GameObject handTrackingTutorialGroup;

    private void Start()
    {
        string selectedMode = ScenarioManager.Instance != null
            ? ScenarioManager.Instance.SelectedInputMode
            : "Controller";

        if (selectedMode == "HandTracking")
        {
            if (controllerTutorialGroup != null) controllerTutorialGroup.SetActive(false);
            if (handTrackingTutorialGroup != null) handTrackingTutorialGroup.SetActive(true);
        }
        else // Controller
        {
            if (controllerTutorialGroup != null) controllerTutorialGroup.SetActive(true);
            if (handTrackingTutorialGroup != null) handTrackingTutorialGroup.SetActive(false);
        }
    }

    // Call this from a VR button when tutorial finishes
    public void OnCompleteTutorial()
    {
        if (ScenarioManager.Instance != null)
        {
            ScenarioManager.Instance.LoadSelectedVRScene();
        }
    }
}