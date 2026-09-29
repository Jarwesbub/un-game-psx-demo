using UnityEngine;
using UnityEditor;
using SplashEdit.RuntimeCode;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// Legacy editor window kept for menu-bar convenience.
    /// All navigation settings and the build/preview workflow have been merged
    /// into the PSXPlayer inspector. This window simply selects the PSXPlayer
    /// so users who click the old menu item land on the right place.
    /// </summary>
    public class PSXNavRegionEditor : EditorWindow
    {
        [MenuItem("PlayStation 1/Nav Region Builder")]
        public static void ShowWindow()
        {
            // Find the PSXPlayer in scene and select it so the inspector shows nav settings
            var players = FindObjectsByType<PSXPlayer>(FindObjectsSortMode.None);
            if (players.Length > 0)
            {
                Selection.activeGameObject = players[0].gameObject;
                EditorGUIUtility.PingObject(players[0]);
                Debug.Log("[Nav] Navigation settings are now on the PSXPlayer inspector. Selecting PSXPlayer.");
            }
            else
            {
                EditorUtility.DisplayDialog("Nav Region Builder",
                    "No PSXPlayer found in the scene.\n\nAdd a PSXPlayer component to a GameObject to configure navigation settings.",
                    "OK");
            }
        }
    }
}
