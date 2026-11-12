
using UnityEngine;

public class NPCLipSync : MonoBehaviour
{
    [Header("NPC Face")]
    [SerializeField] private SkinnedMeshRenderer faceMesh;

    [Header("NPC Teeth")]
    [SerializeField] private SkinnedMeshRenderer teethMesh;

    [Header("NPC Voice")]
    [SerializeField] private AudioSource npcAudioSource;

    [Header("Face BlendShape")]
    [SerializeField] private string mouthBlendShape = "jawOpen";

    [Header("Teeth BlendShape")]
    [SerializeField] private string teethBlendShape = "jawOpen";

    [Range(0f, 1f)]
    [SerializeField] private float teethMovementMultiplier = 1f;

    [Header("Lip Sync Settings")]
    [Range(0f, 100f)]
    [SerializeField] private float maxMouthOpen = 60f;

    [Range(1f, 100f)]
    [SerializeField] private float sensitivity = 35f;

    [Range(1f, 30f)]
    [SerializeField] private float smoothSpeed = 12f;

    private int mouthIndex = -1;
    private int teethIndex = -1;

    private readonly float[] samples = new float[256];

    private float currentWeight;
    private float originalMouthWeight;
    private float originalTeethWeight;

    // Follow the AudioSource actually selected for this NPC's speech.
    public void SetAudioSource(AudioSource source)
    {
        npcAudioSource = source;
    }

    private void Start()
    {
        if (faceMesh == null || faceMesh.sharedMesh == null)
        {
            Debug.LogError("[LipSync] Face Mesh not assigned!");
            enabled = false;
            return;
        }

        if (npcAudioSource == null)
        {
            Debug.LogError("[LipSync] AudioSource not assigned!");
            enabled = false;
            return;
        }

        mouthIndex =
            faceMesh.sharedMesh.GetBlendShapeIndex(mouthBlendShape);

        if (mouthIndex < 0)
        {
            Debug.LogError(
                "[LipSync] Face BlendShape not found: " +
                mouthBlendShape
            );

            enabled = false;
            return;
        }

        originalMouthWeight =
            faceMesh.GetBlendShapeWeight(mouthIndex);

        currentWeight = originalMouthWeight;

        if (teethMesh != null && teethMesh.sharedMesh != null)
        {
            teethIndex =
                teethMesh.sharedMesh.GetBlendShapeIndex(
                    teethBlendShape
                );

            if (teethIndex >= 0)
            {
                originalTeethWeight =
                    teethMesh.GetBlendShapeWeight(teethIndex);
            }
            else
            {
                Debug.LogWarning(
                    "[LipSync] Teeth BlendShape not found: " +
                    teethBlendShape
                );
            }
        }
    }

    private void LateUpdate()
    {
        if (mouthIndex < 0 || npcAudioSource == null)
            return;

        float targetWeight = originalMouthWeight;

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
                originalMouthWeight,
                maxMouthOpen,
                mouthAmount
            );
        }

        currentWeight = Mathf.MoveTowards(
            currentWeight,
            targetWeight,
            smoothSpeed * 100f * Time.deltaTime
        );

        // Move face mouth
        faceMesh.SetBlendShapeWeight(
            mouthIndex,
            currentWeight
        );

        // Move teeth together with mouth
        if (teethMesh != null && teethIndex >= 0)
        {
            float teethWeight =
                originalTeethWeight +
                (currentWeight - originalMouthWeight)
                * teethMovementMultiplier;

            teethMesh.SetBlendShapeWeight(
                teethIndex,
                Mathf.Clamp(teethWeight, 0f, 100f)
            );
        }
    }

    private void OnDisable()
    {
        currentWeight = originalMouthWeight;
        if (faceMesh != null && mouthIndex >= 0)
        {
            faceMesh.SetBlendShapeWeight(
                mouthIndex, originalMouthWeight
            );
        }

        if (teethMesh != null && teethIndex >= 0)
        {
            teethMesh.SetBlendShapeWeight(
                teethIndex, originalTeethWeight
            );
        }
    }
}
