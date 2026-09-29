#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    [CustomEditor(typeof(PSXCutsceneClip))]
    public class PSXCutsceneEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var clip = (PSXCutsceneClip)target;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("PSX Cutscene Clip", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.LabelField("Name", clip.CutsceneName);

            float durSec = clip.DurationFrames / 30f;
            EditorGUILayout.LabelField("Duration", $"{durSec:F2}s ({clip.DurationFrames} frames)");

            int trackCount = clip.Tracks?.Count ?? 0;
            EditorGUILayout.LabelField("Tracks", trackCount.ToString());

            int audioEvtCount = clip.AudioEvents?.Count ?? 0;
            if (audioEvtCount > 0)
                EditorGUILayout.LabelField("Audio Events", audioEvtCount.ToString());

            int skinEvtCount = clip.SkinAnimEvents?.Count ?? 0;
            if (skinEvtCount > 0)
                EditorGUILayout.LabelField("Skin Anim Events", skinEvtCount.ToString());

            EditorGUILayout.Space(8);

            if (GUILayout.Button("Open in Timeline", GUILayout.Height(28)))
                EditorCode.PSXTimelineWindow.Open(clip);
        }
    }
}
#endif
