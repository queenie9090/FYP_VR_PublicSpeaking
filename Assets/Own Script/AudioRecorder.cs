using System;
using System.Collections;
using UnityEngine;

public class AudioRecorder : MonoBehaviour
{
    [SerializeField] private MicrophoneVolumeMeter micVolumeMeter;
    private bool stopRequested = false;

    public void RequestStopRecording()
    {
        stopRequested = true;
    }

    public IEnumerator RecordUserSpeechRoutine(Action<byte[], float, float> onRecordingComplete)
    {
        stopRequested = false; // Reset flag
        string selectedMic = Microphone.devices.Length > 0 ? Microphone.devices[0] : null;

        if (string.IsNullOrEmpty(selectedMic))
        {
            Debug.LogError("[AudioRecorder] No valid microphone device found!");
            onRecordingComplete?.Invoke(null, 0f, 0f);
            yield break;
        }

        float turnStartTime = Time.time;

        // 1. Lower sample rate to 16000 Hz (speech-to-text optimal, drastically reduces size)
        int targetSampleRate = 16000;
        int maxClipSeconds = 15;
        AudioClip recordedClip = Microphone.Start(selectedMic, false, maxClipSeconds, targetSampleRate);

        if (micVolumeMeter != null)
            micVolumeMeter.EnableVolumeMeter(true, recordedClip, selectedMic);

        yield return new WaitForSeconds(0.2f); // Initial buffer

        float speechStart = Time.time;
        float silenceDuration = speechStart - turnStartTime;

        // 2. Tighter silence & duration limits for low-latency responsiveness
        float dynamicSilenceTimer = 2.0f;  // Reduced from 4.5s to 2.0s
        float totalRecordingTimer = 0.0f;
        float maxAllowedDuration = 10.0f;  // Reduced safety cap from 20s to 10s
        float[] sampleBuffer = new float[128];

        // Loop runs until VAD times out, max limit is reached, OR user hits Stop
        while (dynamicSilenceTimer > 0.0f && totalRecordingTimer < maxAllowedDuration && !stopRequested)
        {
            yield return new WaitForSeconds(0.1f);
            totalRecordingTimer += 0.1f;

            if (Microphone.IsRecording(selectedMic) && recordedClip != null)
            {
                int currentMicPos = Microphone.GetPosition(selectedMic);
                if (currentMicPos >= 128)
                {
                    int micPos = currentMicPos - 128;
                    if (micPos >= 0 && micPos + 128 <= recordedClip.samples)
                    {
                        recordedClip.GetData(sampleBuffer, micPos);
                        float sum = 0.0f;
                        for (int i = 0; i < 128; i++) sum += Mathf.Abs(sampleBuffer[i]);
                        float averageLoudness = sum / 128.0f;

                        if (averageLoudness > 0.003f) dynamicSilenceTimer = 2.0f;
                        else dynamicSilenceTimer -= 0.1f;
                    }
                    else dynamicSilenceTimer -= 0.1f;
                }
                else dynamicSilenceTimer -= 0.1f;
            }
            else dynamicSilenceTimer -= 0.1f;
        }

        int lastMicPosition = Microphone.GetPosition(selectedMic);
        Microphone.End(selectedMic);

        if (micVolumeMeter != null)
            micVolumeMeter.EnableVolumeMeter(false, null, null);

        float speechDuration = Time.time - speechStart;

        // 3. Trim the clip to actual recorded duration before converting to WAV
        AudioClip trimmedClip = TrimAudioClip(recordedClip, lastMicPosition);
        byte[] wavBytes = SavWav.GetWavBytes(trimmedClip);

        onRecordingComplete?.Invoke(wavBytes, speechDuration, silenceDuration);
    }

    // Trims empty trailing silence from the allocated AudioClip
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
}