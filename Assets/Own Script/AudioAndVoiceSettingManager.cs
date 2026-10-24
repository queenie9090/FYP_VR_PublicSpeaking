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
    [SerializeField] private TMP_Dropdown microphoneDropdown;

    [Header("Live Mic Visualizer")]
    [SerializeField] private Image liveInputMeterFill;
    [SerializeField] private Slider micSensitivitySlider;
    [SerializeField] private Button testMicButton;          // NEW: Dedicated Test Button
    [SerializeField] private TMP_Text testMicButtonText;      // NEW: Label to toggle "Test Mic" / "Stop Test"

    private const string KEY_MASTER_VOL = "Settings_MasterVol";
    private const string KEY_SFX_VOL = "Settings_SfxVol";
    private const string KEY_BGM_VOL = "Settings_BgmVol";
    private const string KEY_MIC_NAME = "Settings_SelectedMic";
    private const string KEY_MIC_SENS = "Settings_MicSensitivity";

    private AudioClip micTestClip;
    private string currentMicDevice;
    private bool isTestingMic = false;

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
        string savedMic = PlayerPrefs.GetString(KEY_MIC_NAME, "");

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

        int sampleWindow = 128;
        float[] waveData = new float[sampleWindow];
        int micPosition = Microphone.GetPosition(currentMicDevice) - (sampleWindow + 1);

        if (micPosition < 0) return;

        micTestClip.GetData(waveData, micPosition);

        float levelMax = 0;
        for (int i = 0; i < sampleWindow; i++)
        {
            float wavePeak = waveData[i] * waveData[i];
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

    private void RegisterListeners()
    {
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(SetMasterVolume);

        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.AddListener(SetSfxVolume);

        if (bgmVolumeSlider != null)
            bgmVolumeSlider.onValueChanged.AddListener(SetBgmVolume);

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

        if (masterVolumeSlider != null) masterVolumeSlider.value = master;
        if (sfxVolumeSlider != null) sfxVolumeSlider.value = sfx;
        if (bgmVolumeSlider != null) bgmVolumeSlider.value = bgm;
        if (micSensitivitySlider != null) micSensitivitySlider.value = sens;

        SetMasterVolume(master);
        SetSfxVolume(sfx);
        SetBgmVolume(bgm);
    }

    public static string GetActiveMicrophoneDevice()
    {
        return PlayerPrefs.GetString(KEY_MIC_NAME, Microphone.devices.Length > 0 ? Microphone.devices[0] : "");
    }
}