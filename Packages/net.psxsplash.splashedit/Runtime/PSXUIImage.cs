using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// A textured UI image element for PSX export.
    /// Attach to a child of a PSXCanvas GameObject.
    /// The RectTransform determines position and size in PS1 screen space.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    [AddComponentMenu("PSX/UI/PSX UI Image")]
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXUIImage.png")]
    public class PSXUIImage : MonoBehaviour
    {
        [Tooltip("Name used to reference this element from Lua (max 24 chars).")]
        [SerializeField] private string elementName = "image";

        [Tooltip("Source texture for this UI image. Will be quantized and packed into VRAM.")]
        [SerializeField] private Texture2D sourceTexture;

        [Tooltip("Bit depth for VRAM storage.")]
        [SerializeField] private PSXBPP bitDepth = PSXBPP.TEX_8BIT;

        [Tooltip("Tint color applied to the image (white = no tint).")]
        [SerializeField] private Color tintColor = Color.white;

        [Tooltip("Whether this element is visible when the scene first loads.")]
        [SerializeField] private bool startVisible = true;

        /// <summary>Element name for Lua access.</summary>
        public string ElementName => elementName;

        /// <summary>Source texture for quantization and VRAM packing.</summary>
        public Texture2D SourceTexture => sourceTexture;

        /// <summary>Bit depth for the packed texture.</summary>
        public PSXBPP BitDepth => bitDepth;

        /// <summary>Tint color (RGB, alpha ignored).</summary>
        public Color TintColor => tintColor;

        /// <summary>Initial visibility flag.</summary>
        public bool StartVisible => startVisible;

        /// <summary>
        /// After VRAM packing, the exporter fills in these fields so the
        /// binary writer can emit the correct tpage/clut/UV data.
        /// </summary>
        [System.NonSerialized] public PSXTexture2D PackedTexture;
    }
}
