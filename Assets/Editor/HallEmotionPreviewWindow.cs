#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicitly started, local-only test; no scene objects are created.
public sealed class HallEmotionPreviewWindow : EditorWindow
{
    private readonly string[] labels = { "HAPPY", "SAD", "ANGRY", "NERVOUS", "THINKING", "NEUTRAL" };
    private readonly List<NPCEmotionController> actors = new List<NPCEmotionController>();
    private readonly List<string> originalEmotions = new List<string>();
    private readonly List<int> originalIntensities = new List<int>();
    private readonly List<Behaviour> pausedTests = new List<Behaviour>();
    private int selected, intensity = 50;
    private bool running;

    [MenuItem("Tools/Hall/Emotion Preview (Play Mode)")]
    private static void Open() => GetWindow<HallEmotionPreviewWindow>("Hall Emotion Preview");
    private void OnEnable() { EditorApplication.playModeStateChanged += OnPlayModeChanged; }
    private void OnDisable() { StopPreview(); EditorApplication.playModeStateChanged -= OnPlayModeChanged; }
    private void OnPlayModeChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) StopPreview(); }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Local preview only. It pauses legacy emotion hotkeys while active and restores NPC emotions when stopped. Do not start a session during preview.", MessageType.Info);
        if (!Application.isPlaying) { EditorGUILayout.LabelField("Enter Hall Play Mode first."); return; }
        if (!running)
        {
            if (GUILayout.Button("Start isolated preview")) StartPreview();
            return;
        }
        if (NPCEmotionSessionManager.Instance != null && NPCEmotionSessionManager.Instance.IsCollectingBodyEvents)
        {
            EditorGUILayout.HelpBox("A session started; preview will stop to protect its data.", MessageType.Warning);
            StopPreview(); return;
        }
        selected = EditorGUILayout.Popup("NPC", selected, new[] { "Judge", "Audience A", "Audience B" });
        intensity = EditorGUILayout.IntSlider("Intensity", intensity, 0, 100);
        EditorGUILayout.LabelField("Use F6/F7/F8 on the Hall screen to focus the same NPC.", EditorStyles.wordWrappedLabel);
        foreach (string emotion in labels)
            if (GUILayout.Button(emotion) && actors[selected] != null)
                actors[selected].SetEmotion(emotion, emotion == "NEUTRAL" ? 0 : intensity);
        if (GUILayout.Button("Stop and restore original emotions")) StopPreview();
    }

    private void StartPreview()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/Hall.unity");
        if (!scene.IsValid() || !scene.isLoaded || (NPCEmotionSessionManager.Instance != null && NPCEmotionSessionManager.Instance.IsCollectingBodyEvents))
        { ShowNotification(new GUIContent("Open Hall in Play Mode with no active session.")); return; }
        HallNPCRealismScreen screen = null;
        foreach (var root in scene.GetRootGameObjects()) { var x = root.GetComponentInChildren<HallNPCRealismScreen>(true); if (x != null) { screen = x; break; } }
        if (screen == null) { ShowNotification(new GUIContent("Hall screen missing.")); return; }
        var so = new SerializedObject(screen);
        foreach (string field in new[] { "judge", "audienceA", "audienceB" })
        {
            var p = so.FindProperty(field); var actor = p != null ? p.objectReferenceValue as NPCEmotionController : null;
            if (actor == null || !actor.isActiveAndEnabled || actor.HeadMesh == null) { actors.Clear(); ShowNotification(new GUIContent("An NPC emotion source is unavailable.")); return; }
            actors.Add(actor);
        }
        foreach (var actor in actors) { originalEmotions.Add(actor.currentEmotion); originalIntensities.Add(actor.emotionIntensity); }
        foreach (var root in scene.GetRootGameObjects())
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null && behaviour.enabled && (behaviour is NPCEmotionKeyboardTest || behaviour is TwoNPCEmotionTest))
                { pausedTests.Add(behaviour); behaviour.enabled = false; }
        running = true;
    }
    private void StopPreview()
    {
        if (!running) return;
        // Do not overwrite genuine emotion data if a session started while this window was open.
        bool sessionActive = NPCEmotionSessionManager.Instance != null && NPCEmotionSessionManager.Instance.IsCollectingBodyEvents;
        if (Application.isPlaying && !sessionActive)
            for (int i = 0; i < actors.Count; i++) if (actors[i] != null) actors[i].SetEmotion(originalEmotions[i], originalIntensities[i]);
        foreach (var test in pausedTests) if (test != null) test.enabled = true;
        actors.Clear(); originalEmotions.Clear(); originalIntensities.Clear(); pausedTests.Clear(); running = false;
    }
}
#endif
