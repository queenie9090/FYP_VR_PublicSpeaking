using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class PauseManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject pauseMenuPanel;
    [SerializeField] private Button pauseButton;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button mainMenuButton;

    [Header("Dependencies")]
    [SerializeField] private InteractionManager interactionManager;
    [SerializeField] private AudioRecorder audioRecorder;

    [Header("Navigation")]
    [Tooltip("Exact name of your Main Menu scene in Build Settings")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    private bool isPaused = false;
    public bool IsPaused => isPaused;

    private void Start()
    {
        if (pauseMenuPanel != null)
            pauseMenuPanel.SetActive(false);

        if (pauseButton != null)
            pauseButton.onClick.AddListener(PauseSession);

        if (resumeButton != null)
            resumeButton.onClick.AddListener(ResumeSession);

        if (mainMenuButton != null)
            mainMenuButton.onClick.AddListener(ReturnToMainMenu);
    }

    public void TogglePause()
    {
        if (isPaused) ResumeSession();
        else PauseSession();
    }

    public void PauseSession()
    {
        if (isPaused) return;
        isPaused = true;

        // 1. Freeze game time & physics
        Time.timeScale = 0f;

        // 2. Show Pause UI Panel overlay
        if (pauseMenuPanel != null)
            pauseMenuPanel.SetActive(true);

        // 3. Pause active audio recording stream if recording
        if (audioRecorder != null)
        {
            AudioListener.pause = true;
        }

        // 4. Enable locomotion so player can adjust position/height while paused
        if (interactionManager != null)
        {
            interactionManager.SetLocomotionEnabled(true);
        }

        Debug.Log("[PauseManager] Session Paused.");
    }

    public void ResumeSession()
    {
        if (!isPaused) return;
        isPaused = false;

        // 1. Unfreeze game time
        Time.timeScale = 1f;

        // 2. Hide Pause UI Panel
        if (pauseMenuPanel != null)
            pauseMenuPanel.SetActive(false);

        // 3. Unpause audio engine
        AudioListener.pause = false;

        // 4. Lock locomotion back if session is active
        if (interactionManager != null)
        {
            interactionManager.SetLocomotionEnabled(false);
        }

        Debug.Log("[PauseManager] Session Resumed.");
    }

    public void ReturnToMainMenu()
    {
        // Reset time scale and audio state before changing scenes
        Time.timeScale = 1f;
        AudioListener.pause = false;

        // Stop active recording hardware if active
        if (audioRecorder != null)
        {
            audioRecorder.StopRecording();
        }

        Debug.Log($"[PauseManager] Loading Main Menu scene: '{mainMenuSceneName}'");
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private void OnDestroy()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }
}