using SplashEdit.RuntimeCode;
using UnityEditor;
using UnityEngine;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// Custom inspector for <see cref="LuaFile"/> assets that displays the
    /// embedded Lua source code in a read-only text area with an option to
    /// open the source file in an external editor.
    /// </summary>
    [CustomEditor(typeof(LuaFile))]
    public class LuaScriptAssetEditor : Editor
    {
        private Vector2 _scrollPosition;

        public override void OnInspectorGUI()
        {
            LuaFile luaScriptAsset = (LuaFile)target;

            // Open in external editor button
            string assetPath = AssetDatabase.GetAssetPath(target);
            if (!string.IsNullOrEmpty(assetPath))
            {
                if (GUILayout.Button("Open in External Editor"))
                {
                    // Opens the .lua source file in the OS-configured editor
                    UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(assetPath, 1);
                }
                EditorGUILayout.Space(4);
            }

            // Read-only source view
            EditorGUILayout.LabelField("Lua Source", EditorStyles.boldLabel);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition,
                GUILayout.MaxHeight(400));
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextArea(luaScriptAsset.LuaScript, GUILayout.ExpandHeight(true));
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndScrollView();
        }
    }
}
