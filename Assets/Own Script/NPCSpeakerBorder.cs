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

    private void Awake()
    {
        speech = GetComponent<SpeechInteractionController>();
        var shader = Resources.Load<Shader>("NPCSpeakerBorder");
        if (!speech || !shader) { enabled = false; return; }

        material = new Material(shader) { enableInstancing = true };
        var surface = new GameObject("Speaking NPC screen edge");
        surface.transform.SetParent(transform, false);
        mesh = new Mesh { name = "Screen edge ring" };
        var vertices = new Vector3[8];
        var triangles = new int[24];
        for (int i = 0; i < 4; i++)
        {
            vertices[i] = new Vector3(i == 0 || i == 3 ? -1 : 1, i < 2 ? -1 : 1);
            vertices[i + 4] = vertices[i] * .92f;
            int next = (i + 1) % 4;
            int t = i * 6;
            triangles[t] = i; triangles[t+1] = next; triangles[t+2] = i+4;
            triangles[t+3] = next; triangles[t+4] = next+4; triangles[t+5] = i+4;
        }
        mesh.vertices = vertices;
        mesh.triangles = triangles;
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
            overlay.transform.position = camera.transform.position + camera.transform.forward * (camera.nearClipPlane + .5f);
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
