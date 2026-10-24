
using UnityEngine;

//Testing Script
public class NPCEmotionKeyboardTest : MonoBehaviour
{
    public NPCEmotionController emotionController;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
            emotionController.SetEmotion("HAPPY", 80);

        if (Input.GetKeyDown(KeyCode.Alpha2))
            emotionController.SetEmotion("SAD", 80);

        if (Input.GetKeyDown(KeyCode.Alpha3))
            emotionController.SetEmotion("ANGRY", 80);

        if (Input.GetKeyDown(KeyCode.Alpha4))
            emotionController.SetEmotion("NERVOUS", 80);

        if (Input.GetKeyDown(KeyCode.Alpha5))
            emotionController.SetEmotion("THINKING", 80);

        if (Input.GetKeyDown(KeyCode.Alpha6))
            emotionController.SetEmotion("NEUTRAL", 0);
    }
}
