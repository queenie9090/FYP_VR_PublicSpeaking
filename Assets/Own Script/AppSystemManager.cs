using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

public enum LocomotionMode
{
    Dynamic = 0,
    TeleportOnly = 1,
    Continuous = 2
}

public enum TurningMode
{
    Snap = 0,
    Continuous = 1
}

public class AppSystemManager : MonoBehaviour
{
    public static AppSystemManager Instance { get; private set; }

    [Header("VR Movement & Comfort Settings")]
    public LocomotionMode selectedLocomotionMode = LocomotionMode.Dynamic;
    public TurningMode selectedTurningMode = TurningMode.Snap;
    public bool isSeatedModeEnabled = false;

    [Header("Debug Overlay")]
    public bool showFpsCounter = true;
    private float deltaTime = 0.0f;

    public const string KEY_LOCOMOTION_MODE = "VR_LocomotionMode";
    public const string KEY_TURNING_MODE = "VR_TurningMode";
    public const string KEY_SEATED_MODE = "VR_SeatedMode";
    public const string KEY_QUICK_EXIT = "VR_QuickExitEnabled";
    public const string KEY_NEVER_SHOW_TUTORIAL = "Tutorial_NeverShowAgain";

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);
            LoadSettings();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        if (showFpsCounter)
        {
            deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        }
    }

    private void OnGUI()
    {
        if (!showFpsCounter) return;

        int w = Screen.width, h = Screen.height;
        GUIStyle style = new GUIStyle();
        Rect rect = new Rect(16, 16, w, h * 4 / 100);
        style.alignment = TextAnchor.UpperLeft;
        style.fontSize = Mathf.Max(18, h / 40);
        style.normal.textColor = Color.green;

        float msec = deltaTime * 1000.0f;
        float fps = deltaTime > 0f ? 1.0f / deltaTime : 0f;
        string text = string.Format("{0:0.0} ms ({1:0.} FPS)", msec, fps);
        GUI.Label(rect, text, style);
    }

    public void LoadSettings()
    {
        selectedLocomotionMode = (LocomotionMode)PlayerPrefs.GetInt(KEY_LOCOMOTION_MODE, (int)LocomotionMode.Dynamic);
        selectedTurningMode = (TurningMode)PlayerPrefs.GetInt(KEY_TURNING_MODE, (int)TurningMode.Continuous);
        isSeatedModeEnabled = PlayerPrefs.GetInt(KEY_SEATED_MODE, 0) == 1;

        Debug.Log($"[AppSystemManager] Loaded -> Loco: {selectedLocomotionMode} | Turn: {selectedTurningMode} | Seated: {isSeatedModeEnabled}");
    }

    public void SaveLocomotionModeSetting(LocomotionMode mode)
    {
        selectedLocomotionMode = mode;
        PlayerPrefs.SetInt(KEY_LOCOMOTION_MODE, (int)mode);
        PlayerPrefs.Save();
    }

    public void SaveTurningModeSetting(TurningMode mode)
    {
        selectedTurningMode = mode;
        PlayerPrefs.SetInt(KEY_TURNING_MODE, (int)mode);
        PlayerPrefs.Save();
    }

    public void SaveSeatedModeSetting(bool enabled)
    {
        isSeatedModeEnabled = enabled;
        PlayerPrefs.SetInt(KEY_SEATED_MODE, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void SaveQuickExitSetting(bool enabled)
    {
        PlayerPrefs.SetInt(KEY_QUICK_EXIT, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void ResetTutorialPreferences()
    {
        PlayerPrefs.DeleteKey(KEY_NEVER_SHOW_TUTORIAL);
        PlayerPrefs.Save();
    }

    public void QuitApplication()
    {
        UserSession.IsRunningTutorial = false;

        // Cleanly shut down XR subsystems before closing the process
        if (XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null)
        {
            var manager = XRGeneralSettings.Instance.Manager;
            if (manager.isInitializationComplete)
            {
                try { manager.StopSubsystems(); } catch { }
                try { manager.DeinitializeLoader(); } catch { }
            }
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_STANDALONE_WIN
        Application.Quit();
        // Fallback if a background thread (OpenXR/Supabase) keeps the Windows process alive
        System.Diagnostics.Process.GetCurrentProcess().Kill();
#else
        Application.Quit();
#endif
    }
}