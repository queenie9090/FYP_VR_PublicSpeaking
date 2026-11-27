using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.Profiling;

public class AudioRecorder : MonoBehaviour
{
    private const string SavedMicPrefKey = AudioAndVoiceSettingsManager.KEY_MIC_NAME;
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
    private AudioClip sessionClip;
    private bool captureInProgress;
    public bool IsCapturing => captureInProgress && !string.IsNullOrEmpty(activeMicDevice) && VoiceMicTrace.IsRecording(activeMicDevice);
    private string userSelectedMic = null;
    public string ActiveMicrophoneDevice => activeMicDevice;
    public string SelectedMicrophoneDevice => userSelectedMic;

    private void OnEnable()
    {
        userSelectedMic = AudioAndVoiceSettingsManager.GetSavedMicrophone();
        RefreshMicrophoneDropdown();
    }

    private void OnDisable()
    {
        if (micDropdown != null)
            micDropdown.onValueChanged.RemoveListener(OnMicDropdownChanged);
        StopRecording();
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

        // Refresh the preference in case Settings changed since this component was enabled.
        userSelectedMic = AudioAndVoiceSettingsManager.GetSavedMicrophone();
        // Use the selected device if it is still connected, otherwise use the default.
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
        string requestedDevice = ResolveMicrophoneDevice();

        if (string.IsNullOrEmpty(requestedDevice))
        {
            Debug.LogError("[AudioRecorder] No valid microphone device found!");
            onRecordingComplete?.Invoke(null, 0f, 0f);
            yield break;
        }

        int targetSampleRate = 16000;
        bool reuse = sessionClip != null && string.Equals(activeMicDevice, requestedDevice, StringComparison.OrdinalIgnoreCase)
            && VoiceMicTrace.IsRecording(activeMicDevice);
        if (!reuse)
        {
            ReleaseSessionCapture();
            activeMicDevice = requestedDevice;
            Debug.Log($"[AudioRecorder] Opening session microphone: '{activeMicDevice}'");
            int bufferSeconds = Mathf.Max(1, Mathf.CeilToInt(maxAllowedDuration) + 2);
            using (MicStartMarker.Auto())
                sessionClip = VoiceMicTrace.Start(activeMicDevice, bufferSeconds, targetSampleRate, true);
        }
        AudioClip recordedClip = sessionClip;
        if (recordedClip == null)
        {
            Debug.LogError("[AudioRecorder] The microphone could not start recording.");
            onRecordingComplete?.Invoke(null, 0f, 0f);
            yield break;
        }

        captureInProgress = true;
        // A newly opened recording starts at zero; a reused ring starts at its current write position.
        int startPosition = reuse ? VoiceMicTrace.Position(activeMicDevice) : 0;
        if (startPosition < 0 || startPosition >= recordedClip.samples)
        {
            captureInProgress = false;
            Debug.LogError("[AudioRecorder] Microphone returned an invalid starting position.");
            onRecordingComplete?.Invoke(null, 0f, 0f);
            yield break;
        }
        double captureStarted = Time.realtimeSinceStartupAsDouble;

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
        // Higher sensitivity means quieter speech can cross the detection threshold.
        // A value of 1 preserves the original threshold; zero remains a usable minimum.
        float sensitivity = Mathf.Max(0.1f, PlayerPrefs.GetFloat(AudioAndVoiceSettingsManager.KEY_MIC_SENS, 1f));
        float effectiveThreshold = loudnessThreshold / sensitivity;

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

                        if (averageLoudness > effectiveThreshold)
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
        captureInProgress = false;

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
        // The microphone remains open between turns. GetData wraps at the end of the clip.
        int capturedFrames = lastMicPosition >= 0 && lastMicPosition < recordedClip.samples
            ? (lastMicPosition - startPosition + recordedClip.samples) % recordedClip.samples : 0;
        double elapsedCapture = Time.realtimeSinceStartupAsDouble - captureStarted;
        if (elapsedCapture >= (double)recordedClip.samples / recordedClip.frequency || capturedFrames <= 0)
        {
            Debug.LogWarning("[AudioRecorder] Capture buffer was empty or overwritten; discarding this turn.");
            onRecordingComplete?.Invoke(null, 0f, 0f);
            yield break;
        }
        float[] samples = null;
        int channels = recordedClip.channels;
        int frequency = recordedClip.frequency;
        try
        {
            using (WavMarker.Auto()) samples = SavWav.ReadSamples(recordedClip, startPosition, capturedFrames);
        }
        catch (Exception ex)
        {
            Debug.LogError("[AudioRecorder] Could not read recorded audio: " + ex.Message);
        }
        if (samples == null)
        {
            onRecordingComplete?.Invoke(null, 0f, 0f);
            yield break;
        }
        // Only the managed PCM conversion runs off-thread; Unity audio calls stay above.
        var conversion = System.Threading.Tasks.Task.Run(() => SavWav.EncodePcm16(samples, channels, frequency));
        while (!conversion.IsCompleted) yield return null;
        if (conversion.IsFaulted || conversion.IsCanceled)
        {
            Debug.LogError("[AudioRecorder] WAV conversion failed.");
            onRecordingComplete?.Invoke(null, 0f, 0f);
            yield break;
        }
        byte[] wavBytes = conversion.Result;
        Debug.Log($"[VoicePerf] audio conversion ms={conversionTimer.Elapsed.TotalMilliseconds:F1} bytes={wavBytes.Length} frame={Time.frameCount}");
        onRecordingComplete?.Invoke(wavBytes, speechDuration, initialSilenceDuration);
    }

    public void StopRecording()
    {
        stopRequested = true;
        StopAllCoroutines();
        captureInProgress = false;

        ReleaseSessionCapture();

        if (micVolumeMeter != null)
        {
            micVolumeMeter.EnableVolumeMeter(false, null, null);
        }
    }

    private void ReleaseSessionCapture()
    {
        if (!string.IsNullOrEmpty(activeMicDevice) && VoiceMicTrace.IsRecording(activeMicDevice))
        {
            using (MicStopMarker.Auto()) VoiceMicTrace.End(activeMicDevice);
            Debug.Log("[AudioRecorder] Session microphone closed.");
        }
        if (sessionClip != null) Destroy(sessionClip);
        sessionClip = null;
        activeMicDevice = null;
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
    internal static AudioClip Start(string device, int seconds, int rate, bool loop) { using (new Scope(StartMarker, "Microphone.Start")) return Microphone.Start(device, loop, seconds, rate); }
    internal static void End(string device) { using (new Scope(EndMarker, "Microphone.End")) Microphone.End(device); }
}
