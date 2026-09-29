using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// A solid-color rectangle UI element for PSX export.
    /// Rendered as a FastFill primitive on PS1 hardware.
    /// Attach to a child of a PSXCanvas GameObject.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    [AddComponentMenu("PSX/UI/PSX UI Box")]
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXUIBox.png")]
    public class PSXUIBox : MonoBehaviour
    {
        [Tooltip("Name used to reference this element from Lua (max 24 chars).")]
        [SerializeField] private string elementName = "box";

        [Tooltip("Fill color for the box.")]
        [SerializeField] private Color boxColor = Color.black;

        [Tooltip("Whether this element is visible when the scene first loads.")]
        [SerializeField] private bool startVisible = true;

        /// <summary>Element name for Lua access.</summary>
        public string ElementName => elementName;

        /// <summary>Box fill color (RGB, alpha ignored).</summary>
        public Color BoxColor => boxColor;

        /// <summary>Initial visibility flag.</summary>
        public bool StartVisible => startVisible;
    }
}
