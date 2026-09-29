using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXTriggerBox.png")]
    public class PSXTriggerBox : MonoBehaviour
    {
        [SerializeField] private Vector3 size = Vector3.one;
        [SerializeField] private LuaFile luaFile;

        public Vector3 Size => size;
        public LuaFile LuaFile => luaFile;

        public Bounds GetWorldBounds()
        {
            Vector3 halfSize = size * 0.5f;
            Vector3 worldCenter = transform.position;
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
    }
}
