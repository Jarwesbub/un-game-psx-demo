namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Pre-computed data for one UI canvas and its elements,
    /// ready for binary serialization by <see cref="PSXSceneWriter"/>.
    /// Populated by <see cref="PSXUIExporter"/> during the export pipeline.
    /// </summary>
    public struct PSXCanvasData
    {
        /// <summary>Canvas name (max 24 chars, truncated on export).</summary>
        public string Name;

        /// <summary>Initial visibility flag.</summary>
        public bool StartVisible;

        /// <summary>Sort order (0 = back, 255 = front).</summary>
        public byte SortOrder;

        /// <summary>Exported elements belonging to this canvas.</summary>
        public PSXUIElementData[] Elements;
    }

    /// <summary>
    /// Pre-computed data for one UI element, ready for binary serialization.
    /// Matches the 48-byte on-disk element record parsed by uisystem.cpp.
    /// </summary>
    public struct PSXUIElementData
    {
        // Identity
        public PSXUIElementType Type;
        public bool StartVisible;
        public string Name; // max 24 chars

        // Layout (PS1 pixel coords, already Y-inverted)
        public short X, Y, W, H;

        // Anchors (8.8 fixed-point: 0=0.0, 128=0.5, 255≈1.0)
        public byte AnchorMinX, AnchorMinY;
        public byte AnchorMaxX, AnchorMaxY;

        // Primary color (RGB)
        public byte ColorR, ColorG, ColorB;

        // Type-specific: Image
        public byte TexpageX, TexpageY;
        public ushort ClutX, ClutY;
        public byte U0, V0, U1, V1;
        public byte BitDepthIndex; // 0=4bit, 1=8bit, 2=16bit

        // Type-specific: Progress
        public byte BgR, BgG, BgB;
        public byte ProgressValue;

        // Type-specific: Text
        public string DefaultText; // max 63 chars
        public byte FontIndex;     // 0 = system font, 1+ = custom font
    }

    /// <summary>
    /// Export data for a custom font to be embedded in the splashpack.
    /// </summary>
    public struct PSXFontData
    {
        /// <summary>Source font asset (for identification/dedup).</summary>
        public PSXFontAsset Source;

        /// <summary>Glyph cell width in pixels.</summary>
        public byte GlyphWidth;

        /// <summary>Glyph cell height in pixels.</summary>
        public byte GlyphHeight;

        /// <summary>VRAM X position for upload (16-bit pixel units).</summary>
        public ushort VramX;

        /// <summary>VRAM Y position for upload (16-bit pixel units).</summary>
        public ushort VramY;

        /// <summary>Texture height in pixels (width is always 256 in 4bpp = 64 VRAM hwords).</summary>
        public ushort TextureHeight;

        /// <summary>Packed 4bpp pixel data ready for VRAM upload.</summary>
        public byte[] PixelData;

        /// <summary>Per-character advance widths (96 entries, ASCII 0x20-0x7F) for proportional rendering.</summary>
        public byte[] AdvanceWidths;
    }
}
