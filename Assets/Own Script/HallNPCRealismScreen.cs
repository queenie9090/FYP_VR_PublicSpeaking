using System.Collections;
using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

// Hall-only display. It observes NPCs and session state without changing them.
public class HallNPCRealismScreen : MonoBehaviour
{
    [SerializeField] private NPCEmotionController judge;
    [SerializeField] private NPCEmotionController audienceA;
    [SerializeField] private NPCEmotionController audienceB;
    [SerializeField] private Camera judgeCamera;
    [SerializeField] private Camera audienceACamera;
    [SerializeField] private Camera audienceBCamera;
    [SerializeField] private Transform tvRoot;
    [SerializeField] private MeshRenderer displaySurface;
    [SerializeField] private TMP_Text viewingLabel;
    [SerializeField] private TMP_Text[] previewLabels = new TMP_Text[2];
    [SerializeField, Min(10f)] private float switchInterval = 20f;
    [SerializeField, Range(256, 2048)] private int textureResolution = 1024;
#if UNITY_EDITOR
    [SerializeField] private bool enableManualKeyboardTesting;
#endif
    private readonly string[] names = { "Judge", "Audience Member A", "Audience Member B" };
    private NPCEmotionController[] targets;
    private Camera[] closeUpCameras;
    private RenderTexture feed;
    private Material displayMaterial, originalDisplayMaterial;
    private int selected = -1;
    private float nextSwitch;
    private bool manualMode, ready;
    private bool wasSessionActive;

    private IEnumerator Start()
    {
        targets = new[] { judge, audienceA, audienceB };
        closeUpCameras = new[] { judgeCamera, audienceACamera, audienceBCamera };
        DisableCloseUpCameras();
        if (tvRoot == null || displaySurface == null || displaySurface.sharedMaterial == null ||
            viewingLabel == null || previewLabels == null || previewLabels.Length != 2 || previewLabels[0] == null || previewLabels[1] == null)
        {
            Debug.LogWarning("[Hall Screen] TV, display material or serialized TV labels are missing.", this);
            yield break;
        }
        for (int i = 0; i < closeUpCameras.Length; i++)
            if (closeUpCameras[i] == null) Debug.LogWarning("[Hall Screen] Missing close-up camera for " + names[i], this);
        CreateDisplay();
        ready = true;
        SelectTarget(FindAvailable(0));
    }

    private void CreateDisplay()
    {
        int resolution = Mathf.Clamp(textureResolution, 256, 2048);
        feed = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32) { name = "Hall NPC Close-up" };
        feed.Create();
        originalDisplayMaterial = displaySurface.sharedMaterial;
        displayMaterial = new Material(originalDisplayMaterial) { name = "Hall TV Live Display (runtime)" };
        displayMaterial.SetTexture("_BaseMap", feed);
        displayMaterial.SetColor("_BaseColor", Color.white);
        displaySurface.sharedMaterial = displayMaterial;
    }

    private int FindAvailable(int start)
    {
        if (targets == null) return -1;
        for (int offset = 0; offset < targets.Length; offset++)
        {
            int index = (start + offset) % targets.Length;
            if (closeUpCameras[index] != null && closeUpCameras[index].gameObject.activeInHierarchy &&
                targets[index] != null && targets[index].isActiveAndEnabled && targets[index].HeadMesh != null &&
                targets[index].HeadMesh.gameObject.activeInHierarchy) return index;
        }
        return -1;
    }

    private void SelectTarget(int index)
    {
        DisableCloseUpCameras();
        selected = index;
        if (selected >= 0 && closeUpCameras[selected] != null)
        {
            closeUpCameras[selected].targetTexture = feed;
            closeUpCameras[selected].enabled = true;
            displayMaterial.SetColor("_BaseColor", Color.white);
        }
        else if (displayMaterial != null) displayMaterial.SetColor("_BaseColor", Color.black);

        nextSwitch = Time.unscaledTime + Mathf.Max(10f, switchInterval);
        viewingLabel.text = index >= 0 ? "Currently Viewing: " + names[index] : "Currently Viewing: unavailable";
        int slot = 0;
        for (int i = 0; i < names.Length && slot < 2; i++)
        {
            if (i == index) continue;
            bool available = closeUpCameras[i] != null && closeUpCameras[i].gameObject.activeInHierarchy && targets[i] != null && targets[i].isActiveAndEnabled && targets[i].HeadMesh != null && targets[i].HeadMesh.gameObject.activeInHierarchy;
            previewLabels[slot++].text = names[i] + (available ? "\nShown on rotation" : "\nUnavailable");
        }
        while (slot < 2) previewLabels[slot++].text = "Unavailable";
        Debug.Log("[Hall Screen] " + viewingLabel.text, this);
    }

    private void Update()
    {
        if (!ready) return;
#if UNITY_EDITOR
        if (!enableManualKeyboardTesting && manualMode) { manualMode = false; nextSwitch = Time.unscaledTime + Mathf.Max(10f, switchInterval); }
        if (enableManualKeyboardTesting && Keyboard.current != null)
        {
            var keyboard = Keyboard.current;
            int requested = keyboard.f6Key.wasPressedThisFrame ? 0 : keyboard.f7Key.wasPressedThisFrame ? 1 : keyboard.f8Key.wasPressedThisFrame ? 2 : -1;
            if (requested >= 0)
            {
                if (FindAvailable(requested) == requested) { manualMode = true; SelectTarget(requested); }
                else Debug.LogWarning("[Hall Screen] Requested NPC unavailable: " + names[requested], this);
            }
            else if (keyboard.f9Key.wasPressedThisFrame) { manualMode = false; nextSwitch = Time.unscaledTime + Mathf.Max(10f, switchInterval); Debug.Log("[Hall Screen] Automatic switching resumed.", this); }
        }
#endif
        bool sessionActive = NPCEmotionSessionManager.Instance != null && NPCEmotionSessionManager.Instance.IsCollectingBodyEvents;
        if (sessionActive && !wasSessionActive) nextSwitch = Time.unscaledTime + Mathf.Max(10f, switchInterval);
        wasSessionActive = sessionActive;
        if (selected < 0 || closeUpCameras[selected] == null || !closeUpCameras[selected].gameObject.activeInHierarchy || targets[selected] == null || !targets[selected].isActiveAndEnabled || targets[selected].HeadMesh == null || !targets[selected].HeadMesh.gameObject.activeInHierarchy)
        {
            int available = FindAvailable(0);
            if (available != selected) SelectTarget(available);
        }
        else if (!manualMode && sessionActive && Time.unscaledTime >= nextSwitch)
            SelectTarget(FindAvailable((selected + 1) % targets.Length));
    }

    private void DisableCloseUpCameras()
    {
        if (closeUpCameras == null) return;
        foreach (var camera in closeUpCameras)
        {
            if (camera == null) continue;
            camera.enabled = false;
            if (camera.targetTexture == feed) camera.targetTexture = null;
        }
    }

    private void OnDisable() { DisableCloseUpCameras(); if (displayMaterial != null) displayMaterial.SetColor("_BaseColor", Color.black); }
    private void OnEnable() { if (ready) SelectTarget(FindAvailable(selected >= 0 ? selected : 0)); }
    private void OnDestroy()
    {
        DisableCloseUpCameras();
        if (displaySurface != null && displaySurface.sharedMaterial == displayMaterial) displaySurface.sharedMaterial = originalDisplayMaterial;
        if (displayMaterial != null) Destroy(displayMaterial);
        if (feed != null) { feed.Release(); Destroy(feed); }
    }
}
