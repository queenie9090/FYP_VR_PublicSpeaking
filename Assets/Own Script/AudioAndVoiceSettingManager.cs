using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Audio;
using TMPro;

public class AudioAndVoiceSettingsManager : MonoBehaviour
{
    [Header("Audio Mixer References")]
    [SerializeField] private AudioMixer mainAudioMixer;

    [Header("UI Sliders & Dropdowns")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private Slider bgmVolumeSlider;
    [SerializeField] private Slider npcVoiceVolumeSlider;
    [SerializeField] private TMP_Dropdown microphoneDropdown;

    [Header("Live Mic Visualizer")]
    [SerializeField] private Image liveInputMeterFill;
    [SerializeField] private Slider micSensitivitySlider;
    [SerializeField] private Button testMicButton;          // NEW: Dedicated Test Button
    [SerializeField] private TMP_Text testMicButtonText;      // NEW: Label to toggle "Test Mic" / "Stop Test"

    private const string KEY_MASTER_VOL = "Settings_MasterVol";
    private const string KEY_SFX_VOL = "Settings_SfxVol";
    private const string KEY_BGM_VOL = "Settings_BgmVol";
    private const string KEY_NPC_VOL = "Settings_NpcVoiceVol";
    internal const string KEY_MIC_NAME = "Settings_SelectedMic";
    internal const string KEY_MIC_SENS = "Settings_MicSensitivity";
    internal const string LEGACY_MIC_NAME = "UserSelectedMicrophone";

    private AudioClip micTestClip;
    private string currentMicDevice;
    private bool isTestingMic = false;
    private readonly float[] micTestSamples = new float[128];

    private void Start()
    {
        InitializeMicrophoneDropdown();
        LoadSavedSettings();
        RegisterListeners();
    }

    private void Update()
    {
        if (isTestingMic)
        {
            UpdateLiveMicInputMeter();
        }
    }

    private void OnDisable()
    {
        // Automatically stop recording whenever the settings panel is hidden or closed
        StopMicTest();
    }

    private void InitializeMicrophoneDropdown()
    {
        if (microphoneDropdown == null) return;

        microphoneDropdown.ClearOptions();
        List<string> options = new List<string>();

        string[] devices = Microphone.devices;
        if (devices.Length == 0)
        {
            options.Add("No Microphone Detected");
            microphoneDropdown.AddOptions(options);
            microphoneDropdown.interactable = false;
            return;
        }

        int savedIndex = 0;
        string savedMic = GetSavedMicrophone();

        for (int i = 0; i < devices.Length; i++)
        {
            options.Add(devices[i]);
            if (devices[i] == savedMic)
            {
                savedIndex = i;
            }
        }

        microphoneDropdown.AddOptions(options);
        microphoneDropdown.value = savedIndex;
        microphoneDropdown.RefreshShownValue();

        currentMicDevice = devices[savedIndex];
    }

    private void ToggleMicTest()
    {
        if (isTestingMic)
        {
            StopMicTest();
        }
        else
        {
            StartMicTest();
        }
    }

    private void StartMicTest()
    {
        if (string.IsNullOrEmpty(currentMicDevice) || currentMicDevice == "No Microphone Detected") return;

        if (Microphone.IsRecording(currentMicDevice))
        {
            Microphone.End(currentMicDevice);
        }

        micTestClip = Microphone.Start(currentMicDevice, true, 1, 44100);
        isTestingMic = true;

        if (testMicButtonText != null)
            testMicButtonText.text = "Stop Test";
    }

    private void StopMicTest()
    {
        if (!string.IsNullOrEmpty(currentMicDevice) && Microphone.IsRecording(currentMicDevice))
        {
            Microphone.End(currentMicDevice);
        }

        isTestingMic = false;

        if (liveInputMeterFill != null)
            liveInputMeterFill.fillAmount = 0f;

        if (testMicButtonText != null)
            testMicButtonText.text = "Test Mic";
    }

    private void UpdateLiveMicInputMeter()
    {
        if (liveInputMeterFill == null || string.IsNullOrEmpty(currentMicDevice) || micTestClip == null) return;
        if (!Microphone.IsRecording(currentMicDevice)) return;

        int sampleWindow = micTestSamples.Length;
        int micPosition = Microphone.GetPosition(currentMicDevice) - (sampleWindow + 1);

        if (micPosition < 0) return;

        micTestClip.GetData(micTestSamples, micPosition);

        float levelMax = 0;
        for (int i = 0; i < sampleWindow; i++)
        {
            float wavePeak = micTestSamples[i] * micTestSamples[i];
            if (levelMax < wavePeak)
            {
                levelMax = wavePeak;
            }
        }

        float rmsValue = Mathf.Sqrt(levelMax);
        float sensitivity = micSensitivitySlider != null ? micSensitivitySlider.value : 1.0f;

        float targetFill = Mathf.Clamp01(rmsValue * sensitivity * 10f);
        liveInputMeterFill.fillAmount = Mathf.Lerp(liveInputMeterFill.fillAmount, targetFill, Time.deltaTime * 15f);
    }

    private void SetNpcVoiceVolume(float value)
    {
        PlayerPrefs.SetFloat(KEY_NPC_VOL, value);
        if (mainAudioMixer != null)
            mainAudioMixer.SetFloat("NPCVol", Mathf.Log10(Mathf.Max(0.0001f, value)) * 20);
    }

    private void RegisterListeners()
    {
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(SetMasterVolume);

        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.AddListener(SetSfxVolume);

        if (bgmVolumeSlider != null)
            bgmVolumeSlider.onValueChanged.AddListener(SetBgmVolume);

        // Inside RegisterListeners():
        if (npcVoiceVolumeSlider != null)
            npcVoiceVolumeSlider.onValueChanged.AddListener(SetNpcVoiceVolume);

        if (microphoneDropdown != null)
            microphoneDropdown.onValueChanged.AddListener((index) =>
            {
                if (index >= 0 && index < Microphone.devices.Length)
                {
                    StopMicTest();
                    currentMicDevice = Microphone.devices[index];
                    PlayerPrefs.SetString(KEY_MIC_NAME, currentMicDevice);
                    PlayerPrefs.Save();
                }
            });

        if (micSensitivitySlider != null)
            micSensitivitySlider.onValueChanged.AddListener((val) =>
            {
                PlayerPrefs.SetFloat(KEY_MIC_SENS, val);
                PlayerPrefs.Save();
            });

        if (testMicButton != null)
            testMicButton.onClick.AddListener(ToggleMicTest);
    }

    private void SetMasterVolume(float value)
    {
        PlayerPrefs.SetFloat(KEY_MASTER_VOL, value);
        if (mainAudioMixer != null)
            mainAudioMixer.SetFloat("MasterVol", Mathf.Log10(Mathf.Max(0.0001f, value)) * 20);
    }

    private void SetSfxVolume(float value)
    {
        PlayerPrefs.SetFloat(KEY_SFX_VOL, value);
        if (mainAudioMixer != null)
            mainAudioMixer.SetFloat("SfxVol", Mathf.Log10(Mathf.Max(0.0001f, value)) * 20);
    }

    private void SetBgmVolume(float value)
    {
        PlayerPrefs.SetFloat(KEY_BGM_VOL, value);
        if (mainAudioMixer != null)
            mainAudioMixer.SetFloat("BgmVol", Mathf.Log10(Mathf.Max(0.0001f, value)) * 20);
    }

    private void LoadSavedSettings()
    {
        float master = PlayerPrefs.GetFloat(KEY_MASTER_VOL, 0.8f);
        float sfx = PlayerPrefs.GetFloat(KEY_SFX_VOL, 0.8f);
        float bgm = PlayerPrefs.GetFloat(KEY_BGM_VOL, 0.8f);
        float sens = PlayerPrefs.GetFloat(KEY_MIC_SENS, 1.0f);
        float npcVoice = PlayerPrefs.GetFloat(KEY_NPC_VOL, 0.8f);
        
        if (npcVoiceVolumeSlider != null) npcVoiceVolumeSlider.value = npcVoice;
        SetNpcVoiceVolume(npcVoice);
        if (masterVolumeSlider != null) masterVolumeSlider.value = master;
        if (sfxVolumeSlider != null) sfxVolumeSlider.value = sfx;
        if (bgmVolumeSlider != null) bgmVolumeSlider.value = bgm;
        if (micSensitivitySlider != null) micSensitivitySlider.value = sens;

        SetMasterVolume(master);
        SetSfxVolume(sfx);
        SetBgmVolume(bgm);
    }

    internal static string GetSavedMicrophone()
    {
        return PlayerPrefs.GetString(KEY_MIC_NAME, PlayerPrefs.GetString(LEGACY_MIC_NAME, ""));
    }

    public static string GetActiveMicrophoneDevice()
    {
        string[] devices = Microphone.devices;
        string saved = GetSavedMicrophone();
        foreach (string device in devices)
            if (string.Equals(device, saved, System.StringComparison.OrdinalIgnoreCase)) return device;
        return devices.Length > 0 ? devices[0] : "";
    }
}

// Reapply saved mixer levels whenever a scene loads, including direct scene launches.
internal static class SavedAudioLevels
{
    private static AudioMixer mixer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        mixer = null;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        mixer = null;
        AudioMixer current = FindMixer();
        if (current == null) return;
        Apply(current, "MasterVol", "Settings_MasterVol");
        Apply(current, "SfxVol", "Settings_SfxVol");
        Apply(current, "BgmVol", "Settings_BgmVol");
        Apply(current, "NPCVol", "Settings_NpcVoiceVol");
    }

    private static void Apply(AudioMixer current, string parameter, string key)
    {
        float value = Mathf.Clamp01(PlayerPrefs.GetFloat(key, 0.8f));
        if (!current.SetFloat(parameter, Mathf.Log10(Mathf.Max(0.0001f, value)) * 20f))
            Debug.LogWarning("[Audio Settings] Mixer parameter not found: " + parameter);
    }

    internal static void Route(AudioSource source, string groupName)
    {
        AudioMixer current = FindMixer();
        if (source == null || current == null) return;
        AudioMixerGroup[] groups = current.FindMatchingGroups(groupName);
        foreach (AudioMixerGroup group in groups)
        {
            if (group.name == groupName)
            {
                source.outputAudioMixerGroup = group;
                return;
            }
        }
        Debug.LogWarning("[Audio Settings] Mixer group not found: " + groupName);
    }

    private static AudioMixer FindMixer()
    {
        if (mixer != null) return mixer;
        foreach (AudioMixer candidate in Resources.FindObjectsOfTypeAll<AudioMixer>())
        {
            if (candidate.name == "AudioMixer")
            {
                mixer = candidate;
                break;
            }
        }
        return mixer;
    }
}
