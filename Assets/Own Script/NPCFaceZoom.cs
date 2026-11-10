using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

// A live view of the existing animated mesh. This component owns no session data.
public class NPCFaceZoom : MonoBehaviour
{
    [SerializeField] private InteractionManager interactionManager;
    [SerializeField, Min(0.3f)] private float panelDistance = 1.3f;
    [SerializeField, Min(0.1f)] private float faceDistance = 0.65f;
    [SerializeField, Range(15f, 60f)] private float fieldOfView = 32f;
    private GameObject panel;
    private Camera faceCamera;
    private RenderTexture feed;
    private Camera playerCamera;
    private RawImage screen;

    public void OpenFaceZoom()
    {
        if (interactionManager == null)
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                interactionManager = root.GetComponentInChildren<InteractionManager>();
                if (interactionManager != null) break;
            }
        playerCamera = Camera.main;
        if (playerCamera == null || interactionManager == null ||
            interactionManager.CurrentEmotionController == null ||
            interactionManager.CurrentEmotionController.HeadMesh == null)
        {
            Debug.LogError("[NPC Face Zoom] Main camera or conversation NPC head mesh is missing.", this);
            return;
        }
        if (panel == null) CreateViewer();
        Vector3 forward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = playerCamera.transform.forward;
        panel.transform.SetPositionAndRotation(
            playerCamera.transform.position + forward * panelDistance,
            Quaternion.LookRotation(forward, Vector3.up));
        panel.SetActive(true);
        UpdateFaceCamera();
    }

    public void CloseFaceZoom()
    {
        if (panel != null) panel.SetActive(false);
        if (faceCamera != null) faceCamera.enabled = false;
    }

    private void CreateViewer()
    {
        feed = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32);
        feed.name = "NPC Face Live Feed";
        feed.Create();
        var cameraObject = new GameObject("NPC Face Zoom Camera", typeof(Camera));
        cameraObject.transform.SetParent(transform, false);
        faceCamera = cameraObject.GetComponent<Camera>();
        faceCamera.enabled = false;
        faceCamera.stereoTargetEye = StereoTargetEyeMask.None;
        faceCamera.targetTexture = feed;
        faceCamera.fieldOfView = fieldOfView;
        faceCamera.nearClipPlane = 0.02f;
        faceCamera.farClipPlane = 20f;
        faceCamera.clearFlags = CameraClearFlags.SolidColor;
        faceCamera.backgroundColor = new Color(0.025f, 0.035f, 0.055f);
        // Exclude the world-space UI so the monitor cannot capture itself.
        faceCamera.cullingMask = playerCamera.cullingMask & ~(1 << 5);

        panel = new GameObject("NPC Face Zoom Panel", typeof(RectTransform), typeof(Canvas),
            typeof(TrackedDeviceGraphicRaycaster), typeof(GraphicRaycaster));
        panel.layer = 5;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(panel, gameObject.scene);
        var canvas = panel.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = playerCamera;
        var rect = panel.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(800, 900);
        rect.localScale = Vector3.one * 0.001f;

        var background = MakeRect("Monitor Frame", panel.transform, new Vector2(800, 900), Vector2.zero);
        background.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.025f, 0.045f, 0.98f);
        var display = MakeRect("Live Face", panel.transform, new Vector2(760, 760), new Vector2(0, 45));
        screen = display.gameObject.AddComponent<RawImage>();
        screen.texture = feed;
        screen.raycastTarget = false;
        var exit = MakeRect("Exit NPC Face", panel.transform, new Vector2(520, 80), new Vector2(0, -380));
        var buttonImage = exit.gameObject.AddComponent<Image>();
        buttonImage.color = Color.white;
        var button = exit.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        var source = GetComponent<Button>();
        if (source != null) button.colors = source.colors;
        button.onClick.AddListener(CloseFaceZoom);
        var labelRect = MakeRect("Label", exit, new Vector2(500, 70), Vector2.zero);
        var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        var sourceLabel = GetComponentInChildren<TMP_Text>();
        if (sourceLabel != null) label.font = sourceLabel.font;
        label.text = "Exit NPC Face";
        label.fontSize = 32;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
    }

    private static RectTransform MakeRect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var item = new GameObject(name, typeof(RectTransform));
        item.layer = 5;
        var rect = item.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private void LateUpdate()
    {
        if (panel != null && panel.activeSelf) UpdateFaceCamera();
    }

    private void UpdateFaceCamera()
    {
        var controller = interactionManager != null ? interactionManager.CurrentEmotionController : null;
        var head = controller != null ? controller.HeadMesh : null;
        if (head == null || !head.gameObject.activeInHierarchy)
        {
            faceCamera.enabled = false;
            screen.enabled = false;
            return;
        }
        screen.enabled = true;
        Vector3 target = head.bounds.center;
        Vector3 towardPlayer = Vector3.ProjectOnPlane(
            playerCamera.transform.position - target, Vector3.up).normalized;
        if (towardPlayer.sqrMagnitude < 0.01f) towardPlayer = controller.transform.forward;
        faceCamera.transform.position = target + towardPlayer * faceDistance;
        faceCamera.transform.LookAt(target, Vector3.up);
        faceCamera.enabled = true;
    }

    private void OnDisable() => CloseFaceZoom();

    private void OnDestroy()
    {
        if (faceCamera != null) faceCamera.targetTexture = null;
        if (feed != null)
        {
            feed.Release();
            Destroy(feed);
        }
        if (panel != null) Destroy(panel);
    }
}
