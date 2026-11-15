using UnityEngine;

#if UNITY_EDITOR
using TMPro;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
#endif

// This component has no keyboard-testing code in player builds.
public class InterviewNPCAnimationKeyboardTester : MonoBehaviour
{
#if UNITY_EDITOR
    [SerializeField] private NPCBodyAnimation bodyAnimation;
    [SerializeField] private bool enableKeyboardAnimationTesting;
    [Tooltip("Preview Tutorial's SittingTalking clip only while keyboard testing is active.")]
    [SerializeField] private bool previewTutorialSpeaking = true;
    private bool controlling;
    private bool lastPreviewSetting;
    private readonly List<SavedBinding> mutedBindings = new List<SavedBinding>();

    private struct SavedBinding
    {
        public InputAction action;
        public int index;
        public string path, interactions, processors;
    }

    private void Update()
    {
        if (!enableKeyboardAnimationTesting)
        {
            StopTesting();
            return;
        }

        if (NPCEmotionSessionManager.Instance?.IsCollectingBodyEvents == true)
        {
            StopTesting();
            enableKeyboardAnimationTesting = false;
            Debug.LogWarning("[Interview Animation Test] A real session is active; keyboard testing was disabled.", this);
            return;
        }

        if (bodyAnimation == null || !bodyAnimation.isActiveAndEnabled)
        {
            StopTesting();
            enableKeyboardAnimationTesting = false;
            Debug.LogWarning("[Interview Animation Test] Assign an enabled Interview NPC Body Animation component.", this);
            return;
        }

        if (!controlling)
        {
            MuteSimulatorNumberKeys();
            bodyAnimation.BeginManualTest();
            controlling = true;
            lastPreviewSetting = previewTutorialSpeaking;
            bodyAnimation.ConfigureManualSpeakingPreview(lastPreviewSetting);
            LogActivity("Enabled", NPCBodyActivity.IDLE);
        }
        else if (!bodyAnimation.ManualTestActive)
        {
            StopTesting();
            enableKeyboardAnimationTesting = false;
            Debug.LogWarning("[Interview Animation Test] Manual control ended; enable it again when no session is active.", this);
            return;
        }

        if (previewTutorialSpeaking != lastPreviewSetting)
        {
            lastPreviewSetting = previewTutorialSpeaking;
            bodyAnimation.ConfigureManualSpeakingPreview(lastPreviewSetting);
            Debug.Log($"[Interview Animation Test] Tutorial Speaking preview: {lastPreviewSetting}.", this);
        }

        if (IsTyping() || Keyboard.current == null) return;
        var keyboard = Keyboard.current;
        if (keyboard.digit1Key.wasPressedThisFrame) Choose("1", NPCBodyActivity.SPEAKING);
        else if (keyboard.digit2Key.wasPressedThisFrame) Choose("2", NPCBodyActivity.LISTENING);
        else if (keyboard.digit3Key.wasPressedThisFrame) Choose("3", NPCBodyActivity.THINKING);
        else if (keyboard.digit4Key.wasPressedThisFrame) Choose("4", NPCBodyActivity.IDLE);
        else if (keyboard.digit0Key.wasPressedThisFrame) Choose("0", NPCBodyActivity.IDLE);
        else if (keyboard.digit5Key.wasPressedThisFrame)
        {
            bool started = bodyAnimation.PreviewAngryPointing();
            Debug.Log($"[Interview Animation Test] Key 5: pointing {(started ? "requested" : "unavailable")}; {bodyAnimation.ManualTestPlayback}", this);
        }
        else if (keyboard.digit6Key.wasPressedThisFrame) ReportUnavailable("6", "HAPPY thumbs-up");
    }

    private static bool IsTyping()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null) return false;
        var tmp = selected.GetComponentInParent<TMP_InputField>();
        var legacy = selected.GetComponentInParent<InputField>();
        return (tmp != null && tmp.isFocused) || (legacy != null && legacy.isFocused);
    }

    private void MuteSimulatorNumberKeys()
    {
        // Preserve other bindings and restore existing overrides when testing ends.
        foreach (var asset in Resources.FindObjectsOfTypeAll<InputActionAsset>())
        {
            if (asset.name != "XR Device Simulator Controls" && asset.name != "XR Device Controller Controls") continue;
            foreach (var map in asset.actionMaps)
            foreach (var action in map.actions)
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                string path = binding.effectivePath;
                if (path != "<Keyboard>/1" && path != "<Keyboard>/2" &&
                    path != "<Keyboard>/3" && path != "<Keyboard>/4" && path != "<Keyboard>/0" &&
                    path != "<Keyboard>/5" && path != "<Keyboard>/6") continue;
                mutedBindings.Add(new SavedBinding { action = action, index = i, path = binding.overridePath,
                    interactions = binding.overrideInteractions, processors = binding.overrideProcessors });
                action.ApplyBindingOverride(i, new InputBinding { overridePath = "" });
            }
        }
    }

    private void RestoreSimulatorNumberKeys()
    {
        foreach (var saved in mutedBindings)
        {
            if (saved.path == null && saved.interactions == null && saved.processors == null)
                saved.action.RemoveBindingOverride(saved.index);
            else
                saved.action.ApplyBindingOverride(saved.index, new InputBinding { overridePath = saved.path,
                    overrideInteractions = saved.interactions, overrideProcessors = saved.processors });
        }
        mutedBindings.Clear();
    }

    private void Choose(string key, NPCBodyActivity value)
    {
        bodyAnimation.SetManualTestActivity(value);
        LogActivity("Key " + key, value);
    }

    private void ReportUnavailable(string key, string gesture) =>
        Debug.LogWarning($"[Interview Animation Test] Key {key}: {gesture} is not configured; a verified seated hand gesture is required. {bodyAnimation.ManualTestPlayback}", this);

    private void LogActivity(string source, NPCBodyActivity value) =>
        Debug.Log($"[Interview Animation Test] {source} → {value} | Current: {bodyAnimation.ManualTestActivity} | Controller: {bodyAnimation.ManualTestController} | Manual: {bodyAnimation.ManualTestActive} | Before transition: {bodyAnimation.ManualTestPlayback}", this);

    private void StopTesting()
    {
        if (!controlling) return;
        if (bodyAnimation != null) bodyAnimation.EndManualTest();
        RestoreSimulatorNumberKeys();
        controlling = false;
        Debug.Log("[Interview Animation Test] Manual control ended; automatic control restored.", this);
    }

    private void OnDisable() => StopTesting();
#endif
}
