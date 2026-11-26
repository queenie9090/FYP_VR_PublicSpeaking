using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.Profiling;

public class AudioRecorder : MonoBehaviour
{
    private const string SavedMicPrefKey = "UserSelectedMicrophone";
    private static readonly ProfilerMarker MicStartMarker = new ProfilerMarker("Voice.MicrophoneStart");
    private static readonly ProfilerMarker MicStopMarker = new ProfilerMarker("Voice.MicrophoneStop");
    private static readonly ProfilerMarker WavMarker = new ProfilerMarker("Voice.WavConversion");

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
    public bool IsCapturing => !string.IsNullOrEmpty(activeMicDevice) && VoiceMicTrace.IsRecording(activeMicDevice);
    private string userSelectedMic = null;
    public string ActiveMicrophoneDevice => activeMicDevice;
    public string SelectedMicrophoneDevice => userSelectedMic;

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

        string[] devices = VoiceMicTrace.Devices();
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
        string[] devices = VoiceMicTrace.Devices();
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
        string[] devices = VoiceMicTrace.Devices();
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
        AudioClip recordedClip;
        using (MicStartMarker.Auto())
            recordedClip = VoiceMicTrace.Start(activeMicDevice, maxClipSeconds, targetSampleRate);
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
        while (VoiceMicTrace.Position(activeMicDevice) <= 0 && initWait < 0.5f && !stopRequested)
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

            if (VoiceMicTrace.IsRecording(activeMicDevice) && recordedClip != null)
            {
                int currentMicPos = VoiceMicTrace.Position(activeMicDevice);
                if (currentMicPos >= 128)
                {
                    int micPos = currentMicPos - 128;
                    if (micPos >= 0 && micPos + 128 <= recordedClip.samples)
                    {
                        VoiceMicTrace.ReadClip(recordedClip, sampleBuffer, micPos);
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

        int lastMicPosition = VoiceMicTrace.Position(activeMicDevice);
        using (MicStopMarker.Auto())
            VoiceMicTrace.End(activeMicDevice);

        if (micVolumeMeter != null)
            micVolumeMeter.EnableVolumeMeter(false, null, null);

        float endTime = Time.time;
        float initialSilenceDuration = hasDetectedSpeech
            ? Mathf.Max(0f, firstSpeechTime - recordStartTime)
            : (endTime - recordStartTime);
        float speechDuration = hasDetectedSpeech
            ? Mathf.Max(0f, endTime - firstSpeechTime)
            : 0f;

        var conversionTimer = System.Diagnostics.Stopwatch.StartNew();
        int capturedFrames = lastMicPosition > 0 ? lastMicPosition : recordedClip.samples;
        byte[] wavBytes;
        using (WavMarker.Auto())
            wavBytes = SavWav.GetWavBytes(recordedClip, capturedFrames);
        Debug.Log($"[VoicePerf] audio conversion ms={conversionTimer.Elapsed.TotalMilliseconds:F1} bytes={(wavBytes != null ? wavBytes.Length : 0)} frame={Time.frameCount}");

        onRecordingComplete?.Invoke(wavBytes, speechDuration, initialSilenceDuration);
    }

    public void StopRecording()
    {
        stopRequested = true;
        StopAllCoroutines();

        if (!string.IsNullOrEmpty(activeMicDevice) && VoiceMicTrace.IsRecording(activeMicDevice))
        {
            VoiceMicTrace.End(activeMicDevice);
            Debug.Log("[AudioRecorder] Microphone capture forcibly stopped.");
        }

        if (micVolumeMeter != null)
        {
            micVolumeMeter.EnableVolumeMeter(false, null, null);
        }
    }
}

// Temporary investigation instrumentation. Unity microphone and AudioClip APIs stay on the calling thread.
internal static class VoiceMicTrace
{
    private static readonly ProfilerMarker DevicesMarker = new ProfilerMarker("Voice.MicDevices");
    private static readonly ProfilerMarker PositionMarker = new ProfilerMarker("Voice.MicPosition");
    private static readonly ProfilerMarker StateMarker = new ProfilerMarker("Voice.MicIsRecording");
    private static readonly ProfilerMarker ReadMarker = new ProfilerMarker("Voice.AudioClipGetData");
    private static readonly ProfilerMarker StartMarker = new ProfilerMarker("Voice.MicStartCall");
    private static readonly ProfilerMarker EndMarker = new ProfilerMarker("Voice.MicEndCall");
    private static readonly ProfilerMarker CapsMarker = new ProfilerMarker("Voice.MicDeviceCaps");

    internal struct Scope : IDisposable
    {
        private ProfilerMarker.AutoScope marker;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private long start;
        private string operation;
#endif
        internal Scope(ProfilerMarker profilerMarker, string name)
        {
            marker = profilerMarker.Auto();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            operation = name;
            start = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
        }
        public void Dispose()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
#endif
            marker.Dispose();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (elapsedMs >= 10.0)
                Debug.LogWarning($"[VoiceOp] {operation} main-thread ms={elapsedMs:F2} frame={Time.frameCount} time={Time.realtimeSinceStartupAsDouble:F3}");
#endif
        }
    }

    internal static string[] Devices() { using (new Scope(DevicesMarker, "Microphone.devices")) return Microphone.devices; }
    internal static int Position(string device) { using (new Scope(PositionMarker, "Microphone.GetPosition")) return Microphone.GetPosition(device); }
    internal static bool IsRecording(string device) { using (new Scope(StateMarker, "Microphone.IsRecording")) return Microphone.IsRecording(device); }
    internal static bool ReadClip(AudioClip clip, float[] samples, int offset) { using (new Scope(ReadMarker, "AudioClip.GetData")) return clip.GetData(samples, offset); }
    internal static void DeviceCaps(string device, out int min, out int max) { using (new Scope(CapsMarker, "Microphone.GetDeviceCaps")) Microphone.GetDeviceCaps(device, out min, out max); }
    internal static AudioClip Start(string device, int seconds, int rate) { using (new Scope(StartMarker, "Microphone.Start")) return Microphone.Start(device, false, seconds, rate); }
    internal static void End(string device) { using (new Scope(EndMarker, "Microphone.End")) Microphone.End(device); }
}
