using UnityEngine;
using UnityEditor;
using SplashEdit.RuntimeCode;
using System.Linq;

namespace SplashEdit.EditorCode
{
    [CustomEditor(typeof(PSXSceneExporter))]
    public class PSXSceneExporterEditor : UnityEditor.Editor
    {
        private SerializedProperty gteScalingProp;
        private SerializedProperty sceneLuaProp;
        private SerializedProperty fogEnabledProp;
        private SerializedProperty fogColorProp;
        private SerializedProperty fogDensityProp;
        private SerializedProperty sceneTypeProp;
        private SerializedProperty cutscenesProp;
        private SerializedProperty animationsProp;
        private SerializedProperty loadingScreenProp;
        private SerializedProperty previewBVHProp;
        private SerializedProperty previewRoomsPortalsProp;
        private SerializedProperty bvhDepthProp;

        private bool showFog = true;
        private bool showCutscenes = true;
        private bool showDebug = false;

        private void OnEnable()
        {
            gteScalingProp = serializedObject.FindProperty("GTEScaling");
            sceneLuaProp = serializedObject.FindProperty("SceneLuaFile");
            fogEnabledProp = serializedObject.FindProperty("FogEnabled");
            fogColorProp = serializedObject.FindProperty("FogColor");
            fogDensityProp = serializedObject.FindProperty("FogDensity");
            sceneTypeProp = serializedObject.FindProperty("SceneType");
            cutscenesProp = serializedObject.FindProperty("Cutscenes");
            animationsProp = serializedObject.FindProperty("Animations");
            loadingScreenProp = serializedObject.FindProperty("LoadingScreenPrefab");
            previewBVHProp = serializedObject.FindProperty("PreviewBVH");
            previewRoomsPortalsProp = serializedObject.FindProperty("PreviewRoomsPortals");
            bvhDepthProp = serializedObject.FindProperty("BVHPreviewDepth");
        }

        private void OnDisable()
        {
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var exporter = (PSXSceneExporter)target;

            DrawExporterHeader();
            EditorGUILayout.Space(4);

            DrawSceneSettings();
            PSXEditorStyles.DrawSeparator(6, 6);
            DrawFogSection(exporter);
            PSXEditorStyles.DrawSeparator(6, 6);
            DrawCutscenesSection();
            PSXEditorStyles.DrawSeparator(6, 6);
            DrawAnimationsSection();
            PSXEditorStyles.DrawSeparator(6, 6);
            DrawLoadingSection();
            PSXEditorStyles.DrawSeparator(6, 6);
            DrawDebugSection();
            PSXEditorStyles.DrawSeparator(6, 6);
            DrawSceneStats();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawExporterHeader()
        {
            EditorGUILayout.BeginVertical(PSXEditorStyles.CardStyle);
            EditorGUILayout.LabelField("Scene Exporter", PSXEditorStyles.CardHeaderStyle);
            EditorGUILayout.EndVertical();
        }

        private void DrawSceneSettings()
        {
            EditorGUILayout.PropertyField(sceneTypeProp, new GUIContent("Scene Type"));

            bool isInterior = (PSXSceneType)sceneTypeProp.enumValueIndex == PSXSceneType.Interior;
            EditorGUILayout.LabelField(
                isInterior
                    ? "<color=#88aaff>Room/portal occlusion culling.</color>"
                    : "<color=#88cc88>BVH frustum culling.</color>",
                PSXEditorStyles.RichLabel);

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(gteScalingProp, new GUIContent("GTE Scaling"));
            EditorGUILayout.PropertyField(sceneLuaProp, new GUIContent("Scene Lua"));

            if (sceneLuaProp.objectReferenceValue != null)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUI.indentLevel * 15);
                if (GUILayout.Button("Edit", EditorStyles.miniButtonLeft, GUILayout.Width(50)))
                    AssetDatabase.OpenAsset(sceneLuaProp.objectReferenceValue);
                if (GUILayout.Button("Clear", EditorStyles.miniButtonRight, GUILayout.Width(50)))
                    sceneLuaProp.objectReferenceValue = null;
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawFogSection(PSXSceneExporter exporter)
        {
            showFog = EditorGUILayout.Foldout(showFog, "Fog & Background", true, PSXEditorStyles.FoldoutHeader);
            if (!showFog) return;

            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(fogColorProp, new GUIContent("Background Color",
                "Background clear color. Also used as the fog blend target when fog is enabled."));

            EditorGUILayout.PropertyField(fogEnabledProp, new GUIContent("Distance Fog"));

            if (fogEnabledProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(fogDensityProp, new GUIContent("Density"));

                float gteScale = exporter.GTEScaling;
                int density = Mathf.Clamp(exporter.FogDensity, 1, 10);
                float fogFarUnity = (8000f / density) * gteScale / 4096f;
                float fogNearUnity = fogFarUnity / 3f;

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(
                    $"<color=#aaaaaa>GTE range: {fogNearUnity:F1} - {fogFarUnity:F1} units  |  " +
                    $"{8000f / (density * 3f):F0} - {8000f / density:F0} SZ</color>",
                    PSXEditorStyles.RichLabel);
                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel--;
        }

        private void DrawCutscenesSection()
        {
            showCutscenes = EditorGUILayout.Foldout(showCutscenes, "Cutscenes", true, PSXEditorStyles.FoldoutHeader);
            if (!showCutscenes) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(cutscenesProp, new GUIContent("Clips"), true);
            EditorGUI.indentLevel--;
        }

        private bool showAnimations = true;
        private void DrawAnimationsSection()
        {
            showAnimations = EditorGUILayout.Foldout(showAnimations, "Animations", true, PSXEditorStyles.FoldoutHeader);
            if (!showAnimations) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(animationsProp, new GUIContent("Clips"), true);
            EditorGUI.indentLevel--;
        }

        private void DrawLoadingSection()
        {
            EditorGUILayout.PropertyField(loadingScreenProp, new GUIContent("Loading Screen Prefab"));
            if (loadingScreenProp.objectReferenceValue != null)
            {
                var go = loadingScreenProp.objectReferenceValue as GameObject;
                if (go != null && go.GetComponentInChildren<PSXCanvas>() == null)
                {
                    EditorGUILayout.LabelField(
                        "<color=#ffaa44>Prefab has no PSXCanvas component.</color>",
                        PSXEditorStyles.RichLabel);
                }
            }
        }

        private void DrawDebugSection()
        {
            showDebug = EditorGUILayout.Foldout(showDebug, "Debug", true, PSXEditorStyles.FoldoutHeader);
            if (!showDebug) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(previewBVHProp, new GUIContent("Preview BVH"));
            if (previewBVHProp.boolValue)
                EditorGUILayout.PropertyField(bvhDepthProp, new GUIContent("BVH Depth"));
            EditorGUILayout.PropertyField(previewRoomsPortalsProp, new GUIContent("Preview Rooms/Portals"));
            EditorGUI.indentLevel--;
        }

        private void DrawSceneStats()
        {
            var exporters = FindObjectsByType<PSXObjectExporter>(FindObjectsSortMode.None);
            int total = exporters.Length;
            int active = exporters.Count(e => e.IsActive);
            int staticCol = exporters.Count(e => e.CollisionType == PSXCollisionType.Static);
            int dynamicCol = exporters.Count(e => e.CollisionType == PSXCollisionType.Dynamic);
            int triggerBoxes = FindObjectsByType<PSXTriggerBox>(FindObjectsSortMode.None).Length;

            EditorGUILayout.BeginVertical(PSXEditorStyles.CardStyle);
            EditorGUILayout.LabelField(
                $"<b>{active}</b>/{total} objects  |  <b>{staticCol}</b> static  <b>{dynamicCol}</b> dynamic  <b>{triggerBoxes}</b> triggers",
                PSXEditorStyles.RichLabel);
            EditorGUILayout.EndVertical();
        }

    }
}
