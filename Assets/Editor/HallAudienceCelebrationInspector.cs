#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HallAudienceCelebration))]
public sealed class HallAudienceCelebrationInspector : Editor
{
    private RuntimeAnimatorController originalA, originalB;
    private AnimatorOverrideController overrideA, overrideB;
    private AnimationClip originalClap;
    private double previewEnd;

    private void UpdatePreview()
    {
        if (!Application.isPlaying || EditorApplication.timeSinceStartup >= previewEnd) RestorePreview();
    }

    private void RestorePreview()
    {
        EditorApplication.update -= UpdatePreview;
        var reaction = target as HallAudienceCelebration;
        if (reaction == null || originalClap == null) return;
        reaction.StopReactions();
        var data = new SerializedObject(reaction);
        var a = data.FindProperty("audienceA").objectReferenceValue as Animator;
        var b = data.FindProperty("audienceB").objectReferenceValue as Animator;
        if (a != null && a.runtimeAnimatorController == overrideA) a.runtimeAnimatorController = originalA;
        if (b != null && b.runtimeAnimatorController == overrideB) b.runtimeAnimatorController = originalB;
        data.FindProperty("clapClip").objectReferenceValue = originalClap;
        data.ApplyModifiedPropertiesWithoutUndo();
        if (overrideA != null) DestroyImmediate(overrideA);
        if (overrideB != null) DestroyImmediate(overrideB);
        originalClap = null;
    }
    private void OnDisable() { RestorePreview(); }

    private void PreviewAlternative(string path)
    {
        RestorePreview();
        var reaction = (HallAudienceCelebration)target;
        var data = new SerializedObject(reaction);
        var a = data.FindProperty("audienceA").objectReferenceValue as Animator;
        var b = data.FindProperty("audienceB").objectReferenceValue as Animator;
        var current = data.FindProperty("clapClip").objectReferenceValue as AnimationClip;
        AnimationClip candidate = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) { candidate = clip; break; }
        if (a == null || b == null || current == null || candidate == null) { Debug.LogWarning("Hall clap preview: missing animator or clip."); return; }
        originalA = a.runtimeAnimatorController; originalB = b.runtimeAnimatorController; originalClap = current;
        overrideA = new AnimatorOverrideController(originalA); overrideA[current] = candidate;
        overrideB = new AnimatorOverrideController(originalB); overrideB[current] = candidate;
        a.runtimeAnimatorController = overrideA; b.runtimeAnimatorController = overrideB;
        data.FindProperty("clapClip").objectReferenceValue = candidate;
        data.ApplyModifiedPropertiesWithoutUndo();
        reaction.PreviewAudienceClap();
        previewEnd = EditorApplication.timeSinceStartup + candidate.length + 0.5;
        EditorApplication.update += UpdatePreview;
    }
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var reaction = (HallAudienceCelebration)target;
        var interaction = reaction.GetComponent<InteractionManager>();
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Play Mode preview only. These buttons do not start a session, voice or recording.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!Application.isPlaying || (interaction != null && interaction.IsSessionActive)))
        {
            if (GUILayout.Button("Preview: Judge + Both Audiences Clap")) { RestorePreview(); reaction.OnSessionStarted(); }
            if (GUILayout.Button("Preview: A Clap Only")) { RestorePreview(); reaction.PreviewSingleClap(true); }
            if (GUILayout.Button("Preview: B Clap Only")) { RestorePreview(); reaction.PreviewSingleClap(false); }
            if (GUILayout.Button("Preview: Slow Clap")) PreviewAlternative("Assets/Own Animation Clip/Sitting_SlowClap.fbx");
            if (GUILayout.Button("Preview: A Clap / B Cheer")) { RestorePreview(); reaction.OnJudgeHappy(); }
            if (GUILayout.Button("Stop Preview")) { RestorePreview(); reaction.StopReactions(); }
        }
    }
}
#endif
