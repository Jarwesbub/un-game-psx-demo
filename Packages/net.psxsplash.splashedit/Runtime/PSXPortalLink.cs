using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Defines a portal connecting two PSXRoom volumes.
    /// Place this object between two rooms and drag-and-drop the PSXRoom references
    /// into RoomA and RoomB.  The transform position becomes the portal center used
    /// for the camera-forward visibility test at runtime on PS1.
    ///
    /// This is independent of the navigation portal system (PSXNavRegion).
    /// </summary>
    [ExecuteInEditMode]
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXPortalLink.png")]
    public class PSXPortalLink : MonoBehaviour
    {
        [Tooltip("First room connected by this portal.")]
        public PSXRoom RoomA;

        [Tooltip("Second room connected by this portal.")]
        public PSXRoom RoomB;

        [Tooltip("Size of the portal opening (width, height) in world units. " +
                 "Used for the gizmo visualization and the screen-space margin " +
                 "when checking portal visibility at runtime.")]
        public Vector2 PortalSize = new Vector2(2f, 3f);

        void OnDrawGizmos()
        {
            var exporter = FindFirstObjectByType<PSXSceneExporter>();
            if (exporter != null && !exporter.PreviewRoomsPortals) return;

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(Vector3.zero, new Vector3(PortalSize.x, PortalSize.y, 0.05f));
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(PortalSize.x, PortalSize.y, 0.05f));
            Gizmos.matrix = Matrix4x4.identity;

            // Draw lines to connected rooms.
            if (RoomA != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, RoomA.transform.position);
            }
            if (RoomB != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position, RoomB.transform.position);
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(Vector3.zero, new Vector3(PortalSize.x, PortalSize.y, 0.05f));
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(PortalSize.x, PortalSize.y, 0.05f));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
