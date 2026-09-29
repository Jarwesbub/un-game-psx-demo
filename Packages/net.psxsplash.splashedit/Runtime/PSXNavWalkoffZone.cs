using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Place this volume along edges of navigation regions that the player
    /// should be able to walk off (e.g. a pit edge in a walled chamber).
    /// Any nav region boundary edge whose midpoint falls inside this zone
    /// will allow the player to leave without being clamped back.
    /// </summary>
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXTriggerBox.png")]
    public class PSXNavWalkoffZone : MonoBehaviour
    {
        [SerializeField] private Vector3 size = new Vector3(1f, 2f, 0.5f);

        public Vector3 Size => size;

        public Bounds GetWorldBounds()
        {
            Vector3 halfSize = size * 0.5f;
            Vector3 worldMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 worldMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) != 0 ? halfSize.x : -halfSize.x,
                    (i & 2) != 0 ? halfSize.y : -halfSize.y,
                    (i & 4) != 0 ? halfSize.z : -halfSize.z
                );
                Vector3 world = transform.TransformPoint(corner);
                worldMin = Vector3.Min(worldMin, world);
                worldMax = Vector3.Max(worldMax, world);
            }

            Bounds b = new Bounds();
            b.SetMinMax(worldMin, worldMax);
            return b;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(Vector3.zero, size);
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
