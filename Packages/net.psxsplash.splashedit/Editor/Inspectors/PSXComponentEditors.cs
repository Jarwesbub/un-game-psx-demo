using UnityEngine;
using UnityEditor;
using SplashEdit.RuntimeCode;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// Custom inspector for PSXInteractable component.
    /// </summary>
    [CustomEditor(typeof(PSXInteractable))]
    public class PSXInteractableEditor : UnityEditor.Editor
    {
        private bool _interactionFoldout = true;
        private bool _advancedFoldout = false;

        private SerializedProperty _interactionRadius;
        private SerializedProperty _interactButton;
        private SerializedProperty _isRepeatable;
        private SerializedProperty _cooldownFrames;
        private SerializedProperty _showPrompt;
        private SerializedProperty _promptCanvasName;
        private SerializedProperty _requireLineOfSight;

        private static readonly string[] ButtonNames =
        {
            "Select", "L3", "R3", "Start", "Up", "Right", "Down", "Left",
            "L2", "R2", "L1", "R1", "Triangle", "Circle", "Cross", "Square"
        };

        private void OnEnable()
        {
            _interactionRadius = serializedObject.FindProperty("interactionRadius");
            _interactButton = serializedObject.FindProperty("interactButton");
            _isRepeatable = serializedObject.FindProperty("isRepeatable");
            _cooldownFrames = serializedObject.FindProperty("cooldownFrames");
            _showPrompt = serializedObject.FindProperty("showPrompt");
            _promptCanvasName = serializedObject.FindProperty("promptCanvasName");
            _requireLineOfSight = serializedObject.FindProperty("requireLineOfSight");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Header card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(EditorGUIUtility.IconContent("d_Selectable Icon"), GUILayout.Width(30), GUILayout.Height(30));
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("PSX Interactable", PSXEditorStyles.CardHeaderStyle);
            EditorGUILayout.LabelField("Player interaction trigger for PS1", PSXEditorStyles.RichLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            _interactionFoldout = PSXEditorStyles.DrawFoldoutCard("Interaction Settings", _interactionFoldout, () =>
            {
                EditorGUILayout.PropertyField(_interactionRadius);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Interact Button");
                _interactButton.intValue = EditorGUILayout.Popup(_interactButton.intValue, ButtonNames);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(_isRepeatable);

                if (_isRepeatable.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_cooldownFrames, new GUIContent("Cooldown (frames)"));

                    float seconds = _cooldownFrames.intValue / 60f;
                    EditorGUILayout.LabelField($"~ {seconds:F2} seconds at 60fps", EditorStyles.miniLabel);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(4);

                EditorGUILayout.PropertyField(_showPrompt, new GUIContent("Show Prompt Canvas"));

                if (_showPrompt.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_promptCanvasName, new GUIContent("Canvas Name"));
                    if (string.IsNullOrEmpty(_promptCanvasName.stringValue))
                    {
                        EditorGUILayout.HelpBox(
                            "Enter the name of a PSXCanvas that will be shown when the player is in range and hidden when they leave.",
                            MessageType.Info);
                    }
                    if (_promptCanvasName.stringValue != null && _promptCanvasName.stringValue.Length > 15)
                    {
                        EditorGUILayout.HelpBox("Canvas name is limited to 15 characters.", MessageType.Warning);
                    }
                    EditorGUI.indentLevel--;
                }
            });

            EditorGUILayout.Space(2);

            _advancedFoldout = PSXEditorStyles.DrawFoldoutCard("Advanced", _advancedFoldout, () =>
            {
                EditorGUILayout.PropertyField(_requireLineOfSight,
                    new GUIContent("Require Facing",
                        "Player must be facing the object to interact. Uses a forward-direction check."));
            });

            EditorGUILayout.Space(4);

            // Lua events card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("Lua Events", PSXEditorStyles.CardHeaderStyle);
            PSXEditorStyles.DrawSeparator(2, 4);
            EditorGUILayout.LabelField("onInteract", PSXEditorStyles.RichLabel);
            PSXEditorStyles.EndCard();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
