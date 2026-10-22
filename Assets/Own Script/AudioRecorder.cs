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
        AudioClip recordedClip = Microphone.Start(selectedMic, false, 30, 44100);

        if (micVolumeMeter != null)
            micVolumeMeter.EnableVolumeMeter(true, recordedClip, selectedMic);

        yield return new WaitForSeconds(0.2f); // Initial buffer

        float speechStart = Time.time;
        float silenceDuration = speechStart - turnStartTime;

        float dynamicSilenceTimer = 4.5f;
        float totalRecordingTimer = 0.0f;
        float maxAllowedDuration = 20.0f; // Safety cap
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

                        if (averageLoudness > 0.003f) dynamicSilenceTimer = 4.5f;
                        else dynamicSilenceTimer -= 0.1f;
                    }
                    else dynamicSilenceTimer -= 0.1f;
                }
                else dynamicSilenceTimer -= 0.1f;
            }
            else dynamicSilenceTimer -= 0.1f;
        }

        Microphone.End(selectedMic);
        if (micVolumeMeter != null)
            micVolumeMeter.EnableVolumeMeter(false, null, null);

        float speechDuration = Time.time - speechStart;
        byte[] wavBytes = SavWav.GetWavBytes(recordedClip);

        onRecordingComplete?.Invoke(wavBytes, speechDuration, silenceDuration);
    }
}