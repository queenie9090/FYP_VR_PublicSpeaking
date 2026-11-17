using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class AudioRecorder : MonoBehaviour
{
    private const string SavedMicPrefKey = "UserSelectedMicrophone";

    [SerializeField] private MicrophoneVolumeMeter micVolumeMeter;

    [Header("Optional UI Microphone Selector")]
    [Tooltip("Assign a TMP_Dropdown if you want users to pick their microphone in UI. Leave empty to use default/saved mic.")]
    [SerializeField] private TMP_Dropdown micDropdown;

    [Header("Recording Timers")]
    [Tooltip("Minimum seconds to keep recording even if no speech is detected yet.")]
    [SerializeField] private float minRecordDuration = 2.0f;

    [Tooltip("Seconds of silence allowed BEFORE the user starts speaking.")]
    [SerializeField] private float initialSilenceTimeout = 4.0f;

    [Tooltip("Seconds of silence allowed AFTER the user speaks before ending the turn.")]
    [SerializeField] private float trailingSilenceTimeout = 1.8f;

    [Tooltip("Maximum recording length per attempt.")]
    [SerializeField] private float maxAllowedDuration = 60.0f;

    [Header("Voice Activity Detection")]
    [SerializeField] private float loudnessThreshold = 0.003f;

    private bool stopRequested = false;
    private string activeMicDevice = null;
    public bool IsCapturing => !string.IsNullOrEmpty(activeMicDevice) && Microphone.IsRecording(activeMicDevice);
    private string userSelectedMic = null;

    private void OnEnable()
    {
        userSelectedMic = PlayerPrefs.GetString(SavedMicPrefKey, "");
        RefreshMicrophoneDropdown();
    }

    private void OnDisable()
    {
        if (micDropdown != null)
            micDropdown.onValueChanged.RemoveListener(OnMicDropdownChanged);
    }

    /// <summary>
    /// Populates the UI dropdown with all connected microphones and restores the user's saved choice.
    /// </summary>
    public void RefreshMicrophoneDropdown()
    {
        if (micDropdown == null) return;

        micDropdown.onValueChanged.RemoveListener(OnMicDropdownChanged);
        micDropdown.ClearOptions();

        string[] devices = Microphone.devices;
        if (devices == null || devices.Length == 0)
        {
            micDropdown.AddOptions(new List<string> { "No Microphone Detected" });
            micDropdown.interactable = false;
            return;
        }

        micDropdown.interactable = true;
        var options = new List<string>(devices);
        micDropdown.AddOptions(options);

        int selectedIndex = 0;
        if (!string.IsNullOrEmpty(userSelectedMic))
        {
            int found = Array.IndexOf(devices, userSelectedMic);
            if (found >= 0) selectedIndex = found;
        }

        micDropdown.SetValueWithoutNotify(selectedIndex);
        userSelectedMic = devices[selectedIndex];
        micDropdown.onValueChanged.AddListener(OnMicDropdownChanged);
    }

    private void OnMicDropdownChanged(int index)
    {
        string[] devices = Microphone.devices;
        if (devices == null || index < 0 || index >= devices.Length) return;

        SetSelectedMicrophone(devices[index]);
    }

    /// <summary>
    /// Call from any UI or settings script to set and persist the user's preferred microphone.
    /// </summary>
    public void SetSelectedMicrophone(string deviceName)
    {
        userSelectedMic = deviceName;
        PlayerPrefs.SetString(SavedMicPrefKey, deviceName ?? "");
        PlayerPrefs.Save();
        Debug.Log($"[AudioRecorder] User selected microphone: '{deviceName}'");
    }

    private string ResolveMicrophoneDevice()
    {
        string[] devices = Microphone.devices;
        if (devices == null || devices.Length == 0)
            return null;

        // 1. Use the user's explicitly chosen microphone if it is still connected
        if (!string.IsNullOrEmpty(userSelectedMic))
        {
            for (int i = 0; i < devices.Length; i++)
            {
                if (string.Equals(devices[i], userSelectedMic, StringComparison.OrdinalIgnoreCase))
                    return devices[i];
            }
        }

        // 2. Otherwise fall back to the default system microphone (index 0)
        return devices[0];
    }

    public void RequestStopRecording()
    {
        stopRequested = true;
    }

    public IEnumerator RecordUserSpeechRoutine(Action<byte[], float, float> onRecordingComplete, Action<float, float> onProgress = null)
    {
        stopRequested = false;
        activeMicDevice = ResolveMicrophoneDevice();

        if (string.IsNullOrEmpty(activeMicDevice))
        {
            Debug.LogError("[AudioRecorder] No valid microphone device found!");
            onRecordingComplete?.Invoke(null, 0f, 0f);
            yield break;
        }

        Debug.Log($"[AudioRecorder] Recording with microphone: '{activeMicDevice}'");

        int targetSampleRate = 16000;
        int maxClipSeconds = Mathf.CeilToInt(maxAllowedDuration);
        AudioClip recordedClip = Microphone.Start(activeMicDevice, false, maxClipSeconds, targetSampleRate);
        if (recordedClip == null)
        {
            Debug.LogError("[AudioRecorder] The microphone could not start recording.");
            onRecordingComplete?.Invoke(null, 0f, 0f);
            yield break;
        }

        if (micVolumeMeter != null)
            micVolumeMeter.EnableVolumeMeter(true, recordedClip, activeMicDevice);

        // Wait until the microphone driver starts writing samples (up to 0.5s)
        float initWait = 0f;
        while (Microphone.GetPosition(activeMicDevice) <= 0 && initWait < 0.5f && !stopRequested)
        {
            initWait += Time.unscaledDeltaTime;
            yield return null;
        }

        float recordStartTime = Time.time;
        float firstSpeechTime = -1f;
        bool hasDetectedSpeech = false;

        float silenceTimer = 0.0f;
        float totalRecordingTimer = 0.0f;
        float[] sampleBuffer = new float[128];

        while (totalRecordingTimer < maxAllowedDuration && !stopRequested)
        {
            yield return new WaitForSeconds(0.1f);

            if (Time.timeScale > 0f)
            {
                totalRecordingTimer += 0.1f;
                onProgress?.Invoke(totalRecordingTimer, maxAllowedDuration);
            }

            bool heardVoiceThisTick = false;

            if (Microphone.IsRecording(activeMicDevice) && recordedClip != null)
            {
                int currentMicPos = Microphone.GetPosition(activeMicDevice);
                if (currentMicPos >= 128)
                {
                    int micPos = currentMicPos - 128;
                    if (micPos >= 0 && micPos + 128 <= recordedClip.samples)
                    {
                        recordedClip.GetData(sampleBuffer, micPos);
                        float sum = 0.0f;
                        for (int i = 0; i < 128; i++) sum += Mathf.Abs(sampleBuffer[i]);
                        float averageLoudness = sum / 128.0f;

                        if (averageLoudness > loudnessThreshold)
                        {
                            heardVoiceThisTick = true;
                            if (!hasDetectedSpeech)
                            {
                                hasDetectedSpeech = true;
                                firstSpeechTime = Time.time;
                            }
                        }
                    }
                }
            }

            if (heardVoiceThisTick)
                silenceTimer = 0.0f;
            else
                silenceTimer += 0.1f;

            // Never cut off during the minimum grace window
            if (totalRecordingTimer >= minRecordDuration)
            {
                float activeSilenceLimit = hasDetectedSpeech ? trailingSilenceTimeout : initialSilenceTimeout;
                if (silenceTimer >= activeSilenceLimit)
                {
                    Debug.Log($"[AudioRecorder] Stopping recording (hasDetectedSpeech={hasDetectedSpeech}, silence={silenceTimer:F1}s, total={totalRecordingTimer:F1}s).");
                    break;
                }
            }
        }

        int lastMicPosition = Microphone.GetPosition(activeMicDevice);
        Microphone.End(activeMicDevice);

        if (micVolumeMeter != null)
            micVolumeMeter.EnableVolumeMeter(false, null, null);

        float endTime = Time.time;
        float initialSilenceDuration = hasDetectedSpeech
            ? Mathf.Max(0f, firstSpeechTime - recordStartTime)
            : (endTime - recordStartTime);
        float speechDuration = hasDetectedSpeech
            ? Mathf.Max(0f, endTime - firstSpeechTime)
            : 0f;

        AudioClip trimmedClip = TrimAudioClip(recordedClip, lastMicPosition);
        byte[] wavBytes = SavWav.GetWavBytes(trimmedClip);

        onRecordingComplete?.Invoke(wavBytes, speechDuration, initialSilenceDuration);
    }

    private AudioClip TrimAudioClip(AudioClip originalClip, int lastPosition)
    {
        if (originalClip == null || lastPosition <= 0) return originalClip;

        float[] samples = new float[lastPosition * originalClip.channels];
        originalClip.GetData(samples, 0);

        AudioClip trimmedClip = AudioClip.Create(
            originalClip.name + "_trimmed",
            lastPosition,
            originalClip.channels,
            originalClip.frequency,
            false
        );
        trimmedClip.SetData(samples, 0);
        return trimmedClip;
    }

    public void StopRecording()
    {
        stopRequested = true;
        StopAllCoroutines();

        if (!string.IsNullOrEmpty(activeMicDevice) && Microphone.IsRecording(activeMicDevice))
        {
            Microphone.End(activeMicDevice);
            Debug.Log("[AudioRecorder] Microphone capture forcibly stopped.");
        }

        if (micVolumeMeter != null)
        {
            micVolumeMeter.EnableVolumeMeter(false, null, null);
        }
    }
}
