#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    [CustomEditor(typeof(PSXAnimationClip))]
    public class PSXAnimationEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var clip = (PSXAnimationClip)target;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("PSX Animation Clip", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.LabelField("Name", clip.AnimationName);

            float durSec = clip.DurationFrames / 30f;
            EditorGUILayout.LabelField("Duration", $"{durSec:F2}s ({clip.DurationFrames} frames)");

            int trackCount = clip.Tracks?.Count ?? 0;
            EditorGUILayout.LabelField("Tracks", trackCount.ToString());

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
