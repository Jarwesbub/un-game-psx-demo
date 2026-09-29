using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// A progress bar UI element for PSX export.
    /// Rendered as two FastFill primitives (background + fill) on PS1 hardware.
    /// Attach to a child of a PSXCanvas GameObject.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    [AddComponentMenu("PSX/UI/PSX UI Progress Bar")]
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXUIProgressBar.png")]
    public class PSXUIProgressBar : MonoBehaviour
    {
        [Tooltip("Name used to reference this element from Lua (max 24 chars).")]
        [SerializeField] private string elementName = "progress";

        [Tooltip("Background color (shown behind the fill).")]
        [SerializeField] private Color backgroundColor = new Color(0.2f, 0.2f, 0.2f);

        [Tooltip("Fill color (the progressing portion).")]
        [SerializeField] private Color fillColor = Color.green;

        [Tooltip("Initial progress value (0-100).")]
        [Range(0, 100)]
        [SerializeField] private int initialValue = 0;

        [Tooltip("Whether this element is visible when the scene first loads.")]
        [SerializeField] private bool startVisible = true;

        /// <summary>Element name for Lua access.</summary>
        public string ElementName => elementName;

        /// <summary>Background color (RGB).</summary>
        public Color BackgroundColor => backgroundColor;

        /// <summary>Fill color (RGB).</summary>
        public Color FillColor => fillColor;

        /// <summary>Initial progress value 0-100.</summary>
        public int InitialValue => Mathf.Clamp(initialValue, 0, 100);

        /// <summary>Initial visibility flag.</summary>
        public bool StartVisible => startVisible;
    }
}
