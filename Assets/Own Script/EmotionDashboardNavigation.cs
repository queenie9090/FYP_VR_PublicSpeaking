
using UnityEngine;
using UnityEngine.SceneManagement;

public class EmotionDashboardNavigation : MonoBehaviour
{
    [Header("Scene Names")]
    [SerializeField] private string performanceDashboardScene = "DashboardScene";
    [SerializeField] private string mainMenuScene = "MainMenu";

    public void BackToPerformanceDashboard()
    {
        SceneManager.LoadScene(performanceDashboardScene);
    }

    public void ReturnToMainMenu()
    {
        SceneManager.LoadScene(mainMenuScene);
    }
}
