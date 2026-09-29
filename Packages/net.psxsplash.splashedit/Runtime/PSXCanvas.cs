using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Marks a Unity Canvas as a PSX UI canvas for splashpack export.
    /// Attach to a GameObject that also has a Unity Canvas component.
    /// Children with PSXUIImage / PSXUIBox / PSXUIText / PSXUIProgressBar
    /// components will be exported as UI elements in this canvas.
    /// Auto-configures the Canvas to the PSX resolution from PSXData settings.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [AddComponentMenu("PSX/UI/PSX Canvas")]
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXCanvas.png")]
    public class PSXCanvas : MonoBehaviour
    {
        [Tooltip("Name used to reference this canvas from Lua (max 24 chars). Must be unique per scene.")]
        [SerializeField] private string canvasName = "canvas";

        [Tooltip("Whether this canvas is visible when the scene first loads.")]
        [SerializeField] private bool startVisible = true;

        [Tooltip("Render order (0 = back, higher = front). Canvases render back-to-front.")]
        [Range(0, 255)]
        [SerializeField] private int sortOrder = 0;

        [Tooltip("Optional custom font for text elements in this canvas. If null, uses the built-in system font (8x16).")]
        [SerializeField] private PSXFontAsset defaultFont;

        /// <summary>Canvas name for Lua access. Truncated to 24 chars on export.</summary>
        public string CanvasName => canvasName;

        /// <summary>Initial visibility flag written into the splashpack.</summary>
        public bool StartVisible => startVisible;

        /// <summary>Sort order in 0-255 range.</summary>
        public byte SortOrder => (byte)Mathf.Clamp(sortOrder, 0, 255);

        /// <summary>Default font for text elements. Null = system font.</summary>
        public PSXFontAsset DefaultFont => defaultFont;

        /// <summary>
        /// PSX target resolution read from the PSXData asset. Falls back to 320x240.
        /// Cached per domain reload for efficiency.
        /// </summary>
        public static Vector2 PSXResolution
        {
            get
            {
                if (!s_resolutionCached)
                {
                    s_cachedResolution = LoadResolutionFromProject();
                    s_resolutionCached = true;
                }
                return s_cachedResolution;
            }
        }

        private static Vector2 s_cachedResolution = new Vector2(320, 240);
        private static bool s_resolutionCached = false;

        /// <summary>Invalidate the cached resolution (call when PSXData changes).</summary>
        public static void InvalidateResolutionCache()
        {
            s_resolutionCached = false;
        }

        private static Vector2 LoadResolutionFromProject()
        {
#if UNITY_EDITOR
            var data = AssetDatabase.LoadAssetAtPath<PSXData>("Assets/PSXData.asset");
            if (data != null)
                return data.OutputResolution;
#endif
            return new Vector2(320, 240);
        }

        private void Reset()
        {
            InvalidateResolutionCache();
            ConfigureCanvas();
        }

        private void OnEnable()
        {
            ConfigureCanvas();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Delay to avoid modifying in OnValidate directly
            UnityEditor.EditorApplication.delayCall += ConfigureCanvas;
        }
#endif

        /// <summary>
        /// Force the Canvas + CanvasScaler to match the PSX resolution from project settings.
        /// </summary>
        public void ConfigureCanvas()
        {
            if (this == null) return;

            Vector2 res = PSXResolution;

            Canvas canvas = GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.renderMode = RenderMode.WorldSpace;
            }

            RectTransform rt = GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(res.x, res.y);
        }
    }
}
