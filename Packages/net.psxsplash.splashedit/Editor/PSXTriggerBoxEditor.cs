using UnityEngine;
using UnityEditor;
using SplashEdit.RuntimeCode;

namespace SplashEdit.EditorCode
{
    [CustomEditor(typeof(PSXTriggerBox))]
    public class PSXTriggerBoxEditor : UnityEditor.Editor
    {
        private SerializedProperty sizeProp;
        private SerializedProperty luaFileProp;

        private void OnEnable()
        {
            sizeProp = serializedObject.FindProperty("size");
            luaFileProp = serializedObject.FindProperty("luaFile"); 
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Header card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("PSX Trigger Box", PSXEditorStyles.CardHeaderStyle);
            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Properties card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.PropertyField(sizeProp, new GUIContent("Size"));

            PSXEditorStyles.DrawSeparator(4, 4);

            EditorGUILayout.PropertyField(luaFileProp, new GUIContent("Lua Script"));

            if (luaFileProp.objectReferenceValue != null)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUI.indentLevel * 15);
                if (GUILayout.Button("Edit", PSXEditorStyles.SecondaryButton, GUILayout.Width(50)))
                    AssetDatabase.OpenAsset(luaFileProp.objectReferenceValue);
                if (GUILayout.Button("Clear", PSXEditorStyles.SecondaryButton, GUILayout.Width(50)))
                    luaFileProp.objectReferenceValue = null;
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUI.indentLevel * 15);
                if (GUILayout.Button("Create Lua Script", PSXEditorStyles.SecondaryButton, GUILayout.Width(130)))
                    CreateNewLuaScript();
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            PSXEditorStyles.EndCard();

            serializedObject.ApplyModifiedProperties();
        }

        private void CreateNewLuaScript()
        {
            var trigger = target as PSXTriggerBox;
            string defaultName = trigger.gameObject.name.ToLower().Replace(" ", "_");
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Lua Script", defaultName + ".lua", "lua",
                "Create a new Lua script for this trigger box");

            if (string.IsNullOrEmpty(path)) return;

            string template =
                "function onTriggerEnter(triggerIndex)\nend\n\nfunction onTriggerExit(triggerIndex)\nend\n";
            System.IO.File.WriteAllText(path, template);
            AssetDatabase.Refresh();

            var luaFile = AssetDatabase.LoadAssetAtPath<LuaFile>(path);
            if (luaFile != null)
            {
                luaFileProp.objectReferenceValue = luaFile;
                serializedObject.ApplyModifiedProperties();
            }
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void DrawTriggerGizmo(PSXTriggerBox trigger, GizmoType gizmoType)
        {
            bool selected = (gizmoType & GizmoType.Selected) != 0;

            Gizmos.color = selected ? new Color(0.2f, 1f, 0.3f, 0.8f) : new Color(0.2f, 1f, 0.3f, 0.25f);
            Gizmos.matrix = trigger.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, trigger.Size);

            if (selected)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.08f);
                Gizmos.DrawCube(Vector3.zero, trigger.Size);
            }

            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
