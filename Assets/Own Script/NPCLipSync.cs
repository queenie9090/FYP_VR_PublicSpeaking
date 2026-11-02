
using UnityEngine;

public class NPCLipSync : MonoBehaviour
{
    [Header("NPC Face")]
    [SerializeField] private SkinnedMeshRenderer faceMesh;

    [Header("NPC Voice")]
    [SerializeField] private AudioSource npcAudioSource;

    [Header("Mouth Settings")]
    [SerializeField] private string mouthBlendShape = "jawOpen";

    [Range(0f, 100f)]
    [SerializeField] private float maxMouthOpen = 65f;

    [Range(1f, 100f)]
    [SerializeField] private float sensitivity = 35f;

    [Range(1f, 30f)]
    [SerializeField] private float smoothSpeed = 12f;

    private int mouthIndex = -1;
    private readonly float[] samples = new float[256];
    private float currentWeight;
    private float originalWeight;

    private void Start()
    {
        if (faceMesh == null || faceMesh.sharedMesh == null)
        {
            Debug.LogError("[LipSync] Face Mesh not assigned.");
            enabled = false;
            return;
        }

        if (npcAudioSource == null)
        {
            Debug.LogError("[LipSync] AudioSource not assigned.");
            enabled = false;
            return;
        }

        mouthIndex = faceMesh.sharedMesh.GetBlendShapeIndex(
            mouthBlendShape
        );

        if (mouthIndex < 0)
        {
            Debug.LogError(
                "[LipSync] BlendShape not found: " +
                mouthBlendShape
            );
            enabled = false;
            return;
        }

        originalWeight = faceMesh.GetBlendShapeWeight(mouthIndex);
        currentWeight = originalWeight;
    }

    private void LateUpdate()
    {
        if (mouthIndex < 0 || npcAudioSource == null)
            return;

        float targetWeight = originalWeight;

        if (npcAudioSource.isPlaying)
        {
            npcAudioSource.GetOutputData(samples, 0);

            float sum = 0f;

            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }

            float rms = Mathf.Sqrt(sum / samples.Length);

            float mouthAmount = Mathf.Clamp01(
                rms * sensitivity
            );

            targetWeight = Mathf.Lerp(
                originalWeight,
                maxMouthOpen,
                mouthAmount
            );
        }

        currentWeight = Mathf.MoveTowards(
            currentWeight,
            targetWeight,
            smoothSpeed * 100f * Time.deltaTime
        );

        faceMesh.SetBlendShapeWeight(
            mouthIndex,
            currentWeight
        );
    }

    private void OnDisable()
    {
        if (faceMesh != null && mouthIndex >= 0)
        {
            faceMesh.SetBlendShapeWeight(
                mouthIndex,
                originalWeight
            );
        }
    }
}
