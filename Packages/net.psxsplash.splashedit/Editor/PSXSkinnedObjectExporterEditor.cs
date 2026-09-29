#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    [CustomEditor(typeof(PSXSkinnedObjectExporter))]
    public class PSXSkinnedObjectExporterEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var skinExp = (PSXSkinnedObjectExporter)target;
            var smr = skinExp.GetComponentInChildren<SkinnedMeshRenderer>();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Skinned Mesh Info", EditorStyles.boldLabel);

            if (smr == null)
            {
                EditorGUILayout.HelpBox("No SkinnedMeshRenderer found on this GameObject.", MessageType.Error);
                return;
            }

            if (smr.sharedMesh == null)
            {
                EditorGUILayout.HelpBox("SkinnedMeshRenderer has no mesh assigned.", MessageType.Warning);
                return;
            }

            Mesh mesh = smr.sharedMesh;
            int boneCount = smr.bones != null ? smr.bones.Length : 0;
            int vertexCount = mesh.vertexCount;
            int triCount = mesh.triangles.Length / 3;

            EditorGUILayout.LabelField("Bones", boneCount.ToString());
            EditorGUILayout.LabelField("Vertices", vertexCount.ToString());
            EditorGUILayout.LabelField("Triangles", triCount.ToString());

            if (boneCount > 64)
            {
                EditorGUILayout.HelpBox(
                    $"Bone count ({boneCount}) exceeds PS1 limit of 64. " +
                    "Only the first 64 bones will be used.", MessageType.Warning);
            }

            // Clip validation
            if (skinExp.AnimationClips != null && skinExp.AnimationClips.Length > 0)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Animation Clips", EditorStyles.boldLabel);

                int totalFrames = 0;
                foreach (var clip in skinExp.AnimationClips)
                {
                    if (clip == null) continue;
                    int frames = Mathf.CeilToInt(clip.length * skinExp.TargetFPS) + 1;
                    totalFrames += frames;

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"  {clip.name}", GUILayout.Width(160));
                    EditorGUILayout.LabelField($"{frames} frames @ {skinExp.TargetFPS}fps");
                    EditorGUILayout.EndHorizontal();

                    if (frames > 1000)
                    {
                        long clipBytes = (long)frames * Mathf.Min(boneCount, 64) * 24;
                        EditorGUILayout.HelpBox(
                            $"Clip '{clip.name}' bakes {frames} frames ({clipBytes / 1024f:F0} KB). " +
                            "Consider lowering FPS or trimming the clip to save RAM.", MessageType.Info);
                    }
                }

                // RAM estimate
                int usedBones = Mathf.Min(boneCount, 64);
                long boneMatrixBytes = (long)totalFrames * usedBones * 24;
                long boneIndexBytes = triCount * 3;
                long totalBytes = boneMatrixBytes + boneIndexBytes + 64; // overhead

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Estimated RAM",
                    $"{totalBytes / 1024f:F1} KB ({totalFrames} frames × {usedBones} bones × 24 B + {boneIndexBytes} B indices)");

                if (skinExp.AnimationClips.Length > 16)
                {
                    EditorGUILayout.HelpBox(
                        $"Too many clips ({skinExp.AnimationClips.Length} > 16). " +
                        "Only the first 16 will be exported.", MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No animation clips assigned. Add clips to bake bone animations.",
                    MessageType.Info);
            }
        }
    }
}
#endif
