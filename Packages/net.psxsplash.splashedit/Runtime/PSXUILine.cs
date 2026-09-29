using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// A solid-color rectangle UI element for PSX export.
    /// Rendered as a FastFill primitive on PS1 hardware.
    /// Attach to a child of a PSXCanvas GameObject.
    /// TODO: Use a different image for the UI Line - it still uses the box.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    [AddComponentMenu("PSX/UI/PSX UI Line")]
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXUIBox.png")]
    public class PSXUILine : MonoBehaviour
    {
        [Tooltip("Name used to reference this element from Lua (max 24 chars).")]
        [SerializeField] private string elementName = "line";

        [Tooltip("Fill color for the line.")]
        [SerializeField] private Color lineColor = Color.black;

        [Tooltip("Whether this element is visible when the scene first loads.")]
        [SerializeField] private bool startVisible = true;

        /// <summary>Used for the points of the line.</summary>
        [SerializeField] private Vector2 point1;
        [SerializeField] private Vector2 point2;

        public Vector2 Point1 => point1;
        public Vector2 Point2 => point2;

        /// <summary>Element name for Lua access.</summary>
        public string ElementName => elementName;

        /// <summary>Box fill color (RGB, alpha ignored).</summary>
        public Color LineColor => lineColor;

        /// <summary>Initial visibility flag.</summary>
        public bool StartVisible => startVisible;
    }
}
