using UnityEngine;
using UnityEngine.UI;

public class MicrophoneVolumeMeter : MonoBehaviour
{
    [Header("UI Visual References")]
    [SerializeField] private Image blueFillImage;

    [Header("Volume Sensitivity Settings")]
    [SerializeField] private float sensitivity = 30.0f;
    [SerializeField] private float smoothness = 15.0f;

    private AudioClip activeMicClip;
    private string activeDeviceName = null;
    private bool isListening = false;
    private float[] sampleBuffer = new float[128];

    private void Update()
    {
        if (!isListening || blueFillImage == null || activeMicClip == null) return;

        float currentLoudness = GetMicLoudness() * sensitivity;
        blueFillImage.fillAmount = Mathf.Lerp(blueFillImage.fillAmount, Mathf.Clamp01(currentLoudness), Time.deltaTime * smoothness);
    }

    private float GetMicLoudness()
    {
        // 1. Verify recording state and clip validity
        if (!Microphone.IsRecording(activeDeviceName) || activeMicClip == null) return 0.0f;

        int currentMicPos = Microphone.GetPosition(activeDeviceName);

        // 2. Safely ensure we have accumulated at least 128 samples before reading
        if (currentMicPos < 128) return 0.0f;

        int micPosition = currentMicPos - 128;

        // 3. Prevent out-of-bounds offset reads on the AudioClip
        if (micPosition < 0 || micPosition + 128 > activeMicClip.samples) return 0.0f;

        // 4. Safely extract samples
        activeMicClip.GetData(sampleBuffer, micPosition);

        float sum = 0.0f;
        for (int i = 0; i < 128; i++)
        {
            sum += sampleBuffer[i] * sampleBuffer[i];
        }

        return Mathf.Sqrt(sum / 128.0f);
    }

    public void EnableVolumeMeter(bool active, AudioClip clip, string deviceName = null)
    {
        isListening = active;
        activeMicClip = clip;
        activeDeviceName = deviceName;

        if (!active && blueFillImage != null)
        {
            blueFillImage.fillAmount = 0.0f;
        }
    }
}