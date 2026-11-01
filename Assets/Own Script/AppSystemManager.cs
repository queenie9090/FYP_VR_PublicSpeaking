using UnityEngine;
using UnityEngine.SceneManagement;

public enum LocomotionMode
{
    Dynamic = 0,     // Both Teleport and Continuous enabled
    TeleportOnly = 1,// Only Teleportation enabled
    Continuous = 2   // Only Joystick Smooth Movement enabled
}

public enum TurningMode
{
    Snap = 0,       // Angle-based step turning (Best for comfort)
    Continuous = 1  // Smooth joystick rotation
}

public class AppSystemManager : MonoBehaviour
{
    public static AppSystemManager Instance { get; private set; }

    [Header("VR Movement & Comfort Settings")]
    public LocomotionMode selectedLocomotionMode = LocomotionMode.Dynamic;
    public TurningMode selectedTurningMode = TurningMode.Snap;
    public bool isSeatedModeEnabled = false;
    public bool isQuickExitEnabled = false;

    // PlayerPrefs Keys
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
            DontDestroyOnLoad(gameObject);
            LoadSettings();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void LoadSettings()
    {
        selectedLocomotionMode = (LocomotionMode)PlayerPrefs.GetInt(KEY_LOCOMOTION_MODE, (int)LocomotionMode.Dynamic);
        selectedTurningMode = (TurningMode)PlayerPrefs.GetInt(KEY_TURNING_MODE, (int)TurningMode.Snap);
        isSeatedModeEnabled = PlayerPrefs.GetInt(KEY_SEATED_MODE, 0) == 1;
        isQuickExitEnabled = PlayerPrefs.GetInt(KEY_QUICK_EXIT, 0) == 1;

        Debug.Log($"[AppSystemManager] Loaded -> Loco: {selectedLocomotionMode} | Turn: {selectedTurningMode} | Seated: {isSeatedModeEnabled} | QuickExit: {isQuickExitEnabled}");
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
        isQuickExitEnabled = enabled;
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

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_ANDROID
        using (AndroidJavaClass androidClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            AndroidJavaObject currentActivity = androidClass.GetStatic<AndroidJavaObject>("currentActivity");
            currentActivity.Call<bool>("moveTaskToBack", true);
        }
        Application.Quit();
#else
        Application.Quit();
#endif
    }
}