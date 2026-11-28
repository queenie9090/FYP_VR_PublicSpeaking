using UnityEngine;
using UnityEngine.Rendering;

// A single border follows this conversation's actual speech playback.
[DisallowMultipleComponent]
public sealed class NPCSpeakerBorder : MonoBehaviour
{
    private SpeechInteractionController speech;
    private MeshRenderer overlay;
    private Material material;
    private Mesh mesh;
    private Color tint = Color.clear;
    private static readonly int Tint = Shader.PropertyToID("_Tint");
    private static readonly int Headset = Shader.PropertyToID("_Headset");

    private void Awake()
    {
        speech = GetComponent<SpeechInteractionController>();
        var shader = Resources.Load<Shader>("NPCSpeakerBorder");
        if (!speech || !shader) { enabled = false; return; }

        material = new Material(shader) { enableInstancing = true };
        var surface = new GameObject("Speaking NPC screen edge");
        surface.transform.SetParent(transform, false);
        mesh = new Mesh { name = "Speaker border surface" };
        mesh.vertices = new[] { new Vector3(-1,-1), new Vector3(1,-1), new Vector3(1,1), new Vector3(-1,1) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        surface.AddComponent<MeshFilter>().sharedMesh = mesh;
        overlay = surface.AddComponent<MeshRenderer>();
        overlay.sharedMaterial = material;
        overlay.shadowCastingMode = ShadowCastingMode.Off;
        overlay.receiveShadows = false;
        overlay.allowOcclusionWhenDynamic = false;
        overlay.enabled = false;
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
    }

    private void LateUpdate()
    {
        var speaker = speech.CurrentSpeaker;
        Color target = speaker ? EmotionColor(speaker.currentEmotion) : Color.clear;
        target.a *= speaker ? Mathf.Clamp01(speaker.emotionIntensity / 100f) : 0f;
        // Fade alpha separately so a quiet speaker still has a legible edge.
        target.a = target.a > 0 ? Mathf.Lerp(.18f, .53f, target.a) : 0;
        if (target.a == 0) { target.r = tint.r; target.g = tint.g; target.b = tint.b; }
        if (tint.a < .001f) { tint.r = target.r; tint.g = target.g; tint.b = target.b; }
        tint = Color.Lerp(tint, target, 1f - Mathf.Exp(-7f * Time.unscaledDeltaTime));
        material.SetColor(Tint, tint);
    }

    private static Color EmotionColor(string emotion) => emotion switch
    {
        "HAPPY" => new Color(1f,.84f,.20f),
        "SAD" => new Color(.22f,.63f,1f),
        "ANGRY" => new Color(1f,.17f,.06f),
        "NERVOUS" => new Color(1f,.53f,.07f),
        "THINKING" => new Color(.68f,.40f,1f),
        _ => Color.clear
    };

    private void BeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (!overlay) return;
        overlay.enabled = tint.a > .002f && camera.cameraType == CameraType.Game && camera == Camera.main;
        if (overlay.enabled)
        {
            material.SetFloat(Headset, camera.stereoEnabled ||
                (UnityEngine.XR.XRSettings.isDeviceActive && camera.stereoTargetEye != StereoTargetEyeMask.None) ? 1f : 0f);
            overlay.transform.position = camera.transform.position + camera.transform.forward * (camera.nearClipPlane + .5f);
        }
    }

    private void EndCamera(ScriptableRenderContext context, Camera camera)
    {
        if (overlay) overlay.enabled = false;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
        if (overlay) overlay.enabled = false;
        tint = Color.clear;
    }

    private void OnDestroy()
    {
        if (overlay) Destroy(overlay.gameObject);
        if (mesh) Destroy(mesh);
        if (material) Destroy(material);
    }
}
