using UnityEngine;
using UnityEngine.UI;

public class MicrophoneVolumeMeter : MonoBehaviour
{
    [Header("UI Visual References")]
    [SerializeField] private Image blueFillImage;

    [Header("Volume Sensitivity Settings")]
    [SerializeField] private float sensitivity = 50.0f;
    [SerializeField] private float smoothness = 12.0f;

    private AudioClip activeMicClip;
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
        if (!Microphone.IsRecording(null)) return 0.0f;

        int micPosition = Microphone.GetPosition(null) - (128 + 1);
        if (micPosition < 0) return 0.0f;

        activeMicClip.GetData(sampleBuffer, micPosition);

        float sum = 0.0f;
        for (int i = 0; i < 128; i++)
        {
            sum += sampleBuffer[i] * sampleBuffer[i];
        }

        return Mathf.Sqrt(sum / 128.0f);
    }

    public void EnableVolumeMeter(bool active, AudioClip clip)
    {
        isListening = active;
        activeMicClip = clip;

        if (!active && blueFillImage != null)
        {
            blueFillImage.fillAmount = 0.0f;
        }
    }
}