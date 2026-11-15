#if UNITY_EDITOR_WIN
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Best-effort editor audio reopen, guarded against losing runtime-generated audio.</summary>
internal static class EditorAudioRouteRefresh
{
    struct PlayingSource
    {
        public AudioSource source;
        public AudioClip clip;
        public int sample;
    }

    static string pendingScene;

    public static void QueueAfterSceneLoad(string sceneName)
    {
        Cancel();
        pendingScene = sceneName;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public static void Cancel()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        pendingScene = null;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != pendingScene) return;
        string expectedScene = pendingScene;
        Cancel();
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlaying && SceneManager.GetActiveScene().name == expectedScene)
                TryRefresh("desktop scene loaded");
        };
    }

    public static bool TryRefresh(string reason)
    {
        if (!Application.isPlaying) return false;
        foreach (string device in Microphone.devices)
        {
            if (!Microphone.IsRecording(device)) continue;
            Debug.LogWarning($"[EditorAudioRoute] Skipped audio refresh ({reason}): microphone recording is active.");
            return false;
        }

        // Unity can destroy data of generated clips even if they are not currently playing.
        foreach (var clip in Resources.FindObjectsOfTypeAll<AudioClip>())
        {
            if (clip == null || AssetDatabase.Contains(clip)) continue;
            Debug.LogWarning($"[EditorAudioRoute] Skipped audio refresh ({reason}): runtime audio clip '{clip.name}' is loaded.");
            return false;
        }

        var playing = new List<PlayingSource>();
        foreach (var source in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!source.isPlaying) continue;
            if (source.clip == null)
            {
                Debug.LogWarning($"[EditorAudioRoute] Skipped audio refresh ({reason}): '{source.name}' is playing audio without a restorable clip.");
                return false;
            }
            playing.Add(new PlayingSource { source = source, clip = source.clip, sample = source.timeSamples });
        }

        bool applied = false;
        try
        {
            // Force FMOD to tear down the old WASAPI device handle and open a fresh one on the new default endpoint.
            var config = AudioSettings.GetConfiguration();
            int originalBuffer = config.dspBufferSize;
            config.dspBufferSize = (originalBuffer == 512) ? 1024 : 512;
            AudioSettings.Reset(config);

            config.dspBufferSize = originalBuffer;
            applied = AudioSettings.Reset(config);
        }
        finally
        {
            foreach (var item in playing)
            {
                if (item.source == null || item.clip == null || !item.source.isActiveAndEnabled) continue;
                item.source.clip = item.clip;
                item.source.Play();
                if (item.clip.samples > 0) item.source.timeSamples = Mathf.Clamp(item.sample, 0, item.clip.samples - 1);
            }
        }
        if (applied)
            Debug.Log($"[EditorAudioRoute] Audio configuration reopened ({reason}); {playing.Count} sources resumed. Actual output still requires verification.");
        else
            Debug.LogWarning($"[EditorAudioRoute] Audio configuration reopen failed ({reason}); actual output is not known.");
        return applied;
    }
}
#endif