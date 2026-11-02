
using UnityEngine;

public class TwoNPCEmotionTest : MonoBehaviour
{
    [Header("NPCs")]
    public NPCEmotionController judge;
    public NPCEmotionController studentA;

    void Update()
    {
        // Judge
        if (Input.GetKeyDown(KeyCode.Alpha1))
            judge?.SetEmotion("HAPPY", 80);

        if (Input.GetKeyDown(KeyCode.Alpha2))
            judge?.SetEmotion("ANGRY", 80);

        if (Input.GetKeyDown(KeyCode.Alpha3))
            judge?.SetEmotion("NEUTRAL", 0);

        // Student A
        if (Input.GetKeyDown(KeyCode.Alpha4))
            studentA?.SetEmotion("HAPPY", 80);

        if (Input.GetKeyDown(KeyCode.Alpha5))
            studentA?.SetEmotion("NERVOUS", 80);

        if (Input.GetKeyDown(KeyCode.Alpha6))
            studentA?.SetEmotion("NEUTRAL", 0);
    }
}
