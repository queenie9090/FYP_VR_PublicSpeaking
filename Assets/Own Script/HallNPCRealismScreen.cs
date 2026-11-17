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
    [SerializeField] private NPCEmotionIndicatorUI tvEmotionBar;
    [Header("Automatic camera direction")]
    [SerializeField] private bool enableAutomaticDirection = true;
    [SerializeField, Min(1f)] private float playerSwitchMinSeconds = 8f, playerSwitchMaxSeconds = 12f;
    [SerializeField, Min(0.1f)] private float reactionMinSeconds = 4f, reactionMaxSeconds = 6f;
    [SerializeField] private bool returnToJudgeWhenIdle;
    [SerializeField, Min(0f)] private float idleReturnSeconds = 10f;
    [SerializeField, Range(256, 2048)] private int textureResolution = 1024;
#if UNITY_EDITOR
    [SerializeField] private bool enableManualKeyboardTesting;
    [Header("Editor-only direction simulation")]
    [SerializeField] private bool enableStateSimulation, simulateSessionActive, simulatePlayerCapturing, simulateJudgeSpeaking;
#endif
    private enum DirectionState { Idle, PlayerSpeaking, JudgeSpeaking, AudienceReaction, ManualOverride }
    private DirectionState directionState;
    private InteractionManager interaction;
    private readonly string[] names = { "Judge", "Audience Member A", "Audience Member B" };
    private NPCEmotionController[] targets;
    private Camera[] closeUpCameras;
    private RenderTexture feed;
    private Material displayMaterial, originalDisplayMaterial;
    private int selected = -1;
    private float stateDeadline;
    private bool manualMode, ready;
    private bool wasJudgeSpeaking, reactionPending;
    private int lastAudience = -1;

    private IEnumerator Start()
    {
        targets = new[] { judge, audienceA, audienceB };
        interaction = GetComponent<InteractionManager>();
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
        if (tvEmotionBar != null)
        {
            tvEmotionBar.gameObject.SetActive(index >= 0);
            if (index >= 0) tvEmotionBar.SetEmotionSource(targets[index]);
        }
        if (selected >= 0 && closeUpCameras[selected] != null)
        {
            closeUpCameras[selected].targetTexture = feed;
            closeUpCameras[selected].enabled = true;
            displayMaterial.SetColor("_BaseColor", Color.white);
        }
        else if (displayMaterial != null) displayMaterial.SetColor("_BaseColor", Color.black);

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
        if (!enableManualKeyboardTesting && manualMode) manualMode = false;
        if (enableManualKeyboardTesting && Keyboard.current != null)
        {
            var keyboard = Keyboard.current;
            int requested = keyboard.f6Key.wasPressedThisFrame ? 0 : keyboard.f7Key.wasPressedThisFrame ? 1 : keyboard.f8Key.wasPressedThisFrame ? 2 : -1;
            if (requested >= 0)
            {
                if (FindAvailable(requested) == requested) { manualMode = true; SelectTarget(requested); }
                else Debug.LogWarning("[Hall Screen] Requested NPC unavailable: " + names[requested], this);
            }
            else if (keyboard.f9Key.wasPressedThisFrame) { manualMode = false; Debug.Log("[Hall Screen] Automatic direction resumed.", this); }
        }
#endif
        bool sessionActive = interaction != null && interaction.IsSessionActive;
        bool playerCapturing = interaction != null && interaction.IsPlayerCapturing;
        bool judgeSpeaking = interaction != null && interaction.IsNpcSpeechPlaying(judge);
#if UNITY_EDITOR
        if (enableStateSimulation) { sessionActive = simulateSessionActive; playerCapturing = simulatePlayerCapturing; judgeSpeaking = simulateJudgeSpeaking; }
#endif
        playerCapturing &= sessionActive; judgeSpeaking &= sessionActive;
        if (!sessionActive) reactionPending = false;
        else if (wasJudgeSpeaking && !judgeSpeaking) reactionPending = true;
        wasJudgeSpeaking = judgeSpeaking;
        if (selected < 0 || closeUpCameras[selected] == null || !closeUpCameras[selected].gameObject.activeInHierarchy || targets[selected] == null || !targets[selected].isActiveAndEnabled || targets[selected].HeadMesh == null || !targets[selected].HeadMesh.gameObject.activeInHierarchy)
        {
            int available = FindAvailable(0);
            if (available != selected) SelectTarget(available);
        }
        if (manualMode) { directionState = DirectionState.ManualOverride; return; }
        if (!enableAutomaticDirection || !sessionActive) { directionState = DirectionState.Idle; return; }
        if (judgeSpeaking)
        {
            reactionPending = false; directionState = DirectionState.JudgeSpeaking;
            if (FindAvailable(0) == 0 && selected != 0) SelectTarget(0);
            return;
        }
        if (reactionPending)
        {
            reactionPending = false;
            int audience = PickAvailable(true, lastAudience);
            if (audience >= 0)
            {
                lastAudience = audience; if (selected != audience) SelectTarget(audience);
                directionState = DirectionState.AudienceReaction;
                stateDeadline = Time.unscaledTime + RandomDuration(reactionMinSeconds, reactionMaxSeconds, 0.1f);
                return;
            }
        }
        if (directionState == DirectionState.AudienceReaction && Time.unscaledTime < stateDeadline) return;
        DirectionState desired = playerCapturing ? DirectionState.PlayerSpeaking : DirectionState.Idle;
        if (directionState != desired)
        {
            directionState = desired;
            stateDeadline = Time.unscaledTime + (desired == DirectionState.PlayerSpeaking ? RandomDuration(playerSwitchMinSeconds, playerSwitchMaxSeconds, 1f) : Mathf.Max(0f, idleReturnSeconds));
        }
        if (Time.unscaledTime < stateDeadline) return;
        if (desired == DirectionState.PlayerSpeaking)
        {
            int next = PickAvailable(false, selected);
            if (next >= 0 && next != selected) SelectTarget(next);
            stateDeadline = Time.unscaledTime + RandomDuration(playerSwitchMinSeconds, playerSwitchMaxSeconds, 1f);
        }
        else if (returnToJudgeWhenIdle && selected != 0 && FindAvailable(0) == 0) SelectTarget(0);
    }

    private static float RandomDuration(float min, float max, float floor) { min = Mathf.Max(floor, min); return Random.Range(min, Mathf.Max(min, max)); }
    private int PickAvailable(bool audienceOnly, int previous)
    {
        int[] candidates = new int[3]; int count = 0;
        for (int i = audienceOnly ? 1 : 0; i < 3; i++) if (i != previous && FindAvailable(i) == i) candidates[count++] = i;
        if (count == 0 && previous >= (audienceOnly ? 1 : 0) && previous < 3 && FindAvailable(previous) == previous) candidates[count++] = previous;
        return count == 0 ? -1 : candidates[Random.Range(0, count)];
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
