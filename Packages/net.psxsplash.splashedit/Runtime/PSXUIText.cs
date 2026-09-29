using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// A text UI element for PSX export.
    /// Rendered via psyqo::Font::chainprintf on PS1 hardware.
    /// Attach to a child of a PSXCanvas GameObject.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    [AddComponentMenu("PSX/UI/PSX UI Text")]
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXUIText.png")]
    public class PSXUIText : MonoBehaviour
    {
        [Tooltip("Name used to reference this element from Lua (max 24 chars).")]
        [SerializeField] private string elementName = "text";

        [Tooltip("Default text content (max 63 chars). Can be changed at runtime via Lua UI.SetText().")]
        [SerializeField] private string defaultText = "";

        [Tooltip("Text color.")]
        [SerializeField] private Color textColor = Color.white;

        [Tooltip("Whether this element is visible when the scene first loads.")]
        [SerializeField] private bool startVisible = true;

        [Tooltip("Custom font override. If null, uses the canvas default font (or built-in system font).")]
        [SerializeField] private PSXFontAsset fontOverride;

        /// <summary>Element name for Lua access.</summary>
        public string ElementName => elementName;

        /// <summary>Default text content (truncated to 63 chars on export).</summary>
        public string DefaultText => defaultText;

        /// <summary>Text color (RGB, alpha ignored).</summary>
        public Color TextColor => textColor;

        /// <summary>Initial visibility flag.</summary>
        public bool StartVisible => startVisible;

        /// <summary>
        /// Custom font override. If null, inherits from parent PSXCanvas.DefaultFont.
        /// If that is also null, uses the built-in system font.
        /// </summary>
        public PSXFontAsset FontOverride => fontOverride;

        /// <summary>
        /// Resolve the effective font for this text element.
        /// Checks: fontOverride → parent PSXCanvas.DefaultFont → null (system font).
        /// </summary>
        public PSXFontAsset GetEffectiveFont()
        {
            if (fontOverride != null) return fontOverride;
            PSXCanvas canvas = GetComponentInParent<PSXCanvas>();
            if (canvas != null && canvas.DefaultFont != null) return canvas.DefaultFont;
            return null; // system font
        }
    }
}
