using UnityEngine;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SplashEdit.RuntimeCode
{
    [CreateAssetMenu(fileName = "New PSXFont", menuName = "PSX/Font Asset")]
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXFontAsset.png")]
    public class PSXFontAsset : ScriptableObject
    {
        [Header("Source - Option A: TrueType/OTF Font")]
        [Tooltip("Assign a Unity Font asset (TTF/OTF). Click 'Generate Bitmap' to rasterize.")]
        [SerializeField] private Font sourceFont;

        [Tooltip("Font size in pixels. Larger = more detail but uses more VRAM.\n" +
                 "The actual glyph cell size is auto-computed to fit within PS1 texture page limits.")]
        [Range(6, 32)]
        [SerializeField] private int fontSize = 16;

        [Header("Source - Option B: Manual Bitmap")]
        [Tooltip("Font bitmap texture. Must be 256 pixels wide.\n" +
                 "Glyphs in ASCII order from 0x20, transparent = bg, opaque = fg.")]
        [SerializeField] private Texture2D fontTexture;

        [Header("Glyph Metrics")]
        [Tooltip("Width of each glyph cell (auto-set from font, editable for manual bitmap).\n" +
                 "Must divide 256 evenly: 4, 8, 16, or 32.")]
        [SerializeField] private int glyphWidth = 8;

        [Tooltip("Height of each glyph cell (auto-set from font, editable for manual bitmap).")]
        [SerializeField] private int glyphHeight = 16;

        [HideInInspector]
        [SerializeField] private byte[] storedAdvanceWidths;

        // Valid glyph widths: must divide 256 evenly for PSYQo texture UV wrapping.
        private static readonly int[] ValidGlyphWidths = { 4, 8, 16, 32 };

        // PS1 texture page is 256 pixels tall. Font texture MUST fit in one page.
        private const int MAX_TEXTURE_PAGE_HEIGHT = 256;

        public Font SourceFont => sourceFont;
        public int FontSize => fontSize;
        public Texture2D FontTexture => fontTexture;
        public int GlyphWidth => glyphWidth;
        public int GlyphHeight => glyphHeight;

        /// <summary>Per-character advance widths (96 entries, ASCII 0x20-0x7F). Computed during generation.</summary>
        public byte[] AdvanceWidths => storedAdvanceWidths;

        public int GlyphsPerRow => 256 / glyphWidth;
        public int RowCount => Mathf.CeilToInt(95f / GlyphsPerRow);
        public int TextureHeight => RowCount * glyphHeight;

#if UNITY_EDITOR
        public void GenerateBitmapFromFont()
        {
            if (sourceFont == null)
            {
                Debug.LogWarning("PSXFontAsset: No source font assigned.");
                return;
            }

            // ── Step 1: Populate the font atlas ──
            string ascii = "";
            for (int c = 0x20; c <= 0x7E; c++) ascii += (char)c;
            sourceFont.RequestCharactersInTexture(ascii, fontSize, FontStyle.Normal);

            // ── Step 2: Get readable copy of atlas texture ──
            // For non-dynamic fonts, the atlas may only be populated at the native size.
            // Try the requested size first, then fall back to size=0.
            Texture fontTex = sourceFont.material != null ? sourceFont.material.mainTexture : null;
            if (fontTex == null || fontTex.width == 0 || fontTex.height == 0)
            {
                // Retry with size=0 (native size) for non-dynamic fonts
                sourceFont.RequestCharactersInTexture(ascii, 0, FontStyle.Normal);
                fontTex = sourceFont.material != null ? sourceFont.material.mainTexture : null;
            }
            if (fontTex == null)
            {
                Debug.LogError("PSXFontAsset: Font atlas is null. Set Character to 'ASCII Default Set' in font import settings.");
                return;
            }

            int fontTexW = fontTex.width;
            int fontTexH = fontTex.height;
            if (fontTexW == 0 || fontTexH == 0)
            {
                Debug.LogError("PSXFontAsset: Font atlas has zero dimensions. Try re-importing the font with 'ASCII Default Set'.");
                return;
            }

            Color[] fontPixels;
            {
                RenderTexture rt = RenderTexture.GetTemporary(fontTexW, fontTexH, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(fontTex, rt);
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                Texture2D readable = new Texture2D(fontTexW, fontTexH, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, fontTexW, fontTexH), 0, 0);
                readable.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                fontPixels = readable.GetPixels();
                DestroyImmediate(readable);
            }

            // Verify atlas isn't blank
            bool hasAnyPixel = false;
            for (int i = 0; i < fontPixels.Length && !hasAnyPixel; i++)
            {
                if (fontPixels[i].a > 0.1f) hasAnyPixel = true;
            }
            if (!hasAnyPixel)
            {
                Debug.LogError("PSXFontAsset: Font atlas is blank. Set Character to 'ASCII Default Set' in font import settings.");
                return;
            }

            // ── Step 3: Get character info ──
            // Non-dynamic fonts only respond to size=0 or their native size.
            // Dynamic fonts respond to any size.
            CharacterInfo[] charInfos = new CharacterInfo[95];
            bool[] charValid = new bool[95];
            int validCount = 0;
            int workingSize = fontSize;

            // Try requested fontSize first
            for (int c = 0x20; c <= 0x7E; c++)
            {
                int idx = c - 0x20;
                if (sourceFont.GetCharacterInfo((char)c, out charInfos[idx], fontSize, FontStyle.Normal))
                {
                    charValid[idx] = true;
                    validCount++;
                }
            }

            // If that failed, try size=0 (non-dynamic fonts need this)
            if (validCount == 0)
            {
                sourceFont.RequestCharactersInTexture(ascii, 0, FontStyle.Normal);
                for (int c = 0x20; c <= 0x7E; c++)
                {
                    int idx = c - 0x20;
                    if (sourceFont.GetCharacterInfo((char)c, out charInfos[idx], 0, FontStyle.Normal))
                    {
                        charValid[idx] = true;
                        validCount++;
                    }
                }
                if (validCount > 0)
                {
                    workingSize = 0;
                }
            }

            // Last resort: read characterInfo array directly
            if (validCount == 0 && sourceFont.characterInfo != null)
            {
                foreach (CharacterInfo fci in sourceFont.characterInfo)
                {
                    int c = fci.index;
                    if (c >= 0x20 && c <= 0x7E)
                    {
                        charInfos[c - 0x20] = fci;
                        charValid[c - 0x20] = true;
                        validCount++;
                    }
                }
            }

            if (validCount == 0)
            {
                Debug.LogError("PSXFontAsset: Could not get character info from font.");
                return;
            }

            // ── Step 4: Choose glyph cell dimensions ──
            // Constraints:
            //   - glyphWidth must divide 256 (valid: 4, 8, 16, 32)
            //   - ceil(95 / (256/glyphWidth)) * glyphHeight <= 256 (must fit in one texture page)
            //   - glyphHeight in [4, 32]
            // Strategy: pick the smallest valid width where everything fits.
            // Glyphs that exceed the cell are scaled to fit.

            int measuredMaxW = 0, measuredMaxH = 0;
            for (int idx = 1; idx < 95; idx++) // skip space
            {
                if (!charValid[idx]) continue;
                CharacterInfo ci = charInfos[idx];
                int pw = Mathf.Abs(ci.maxX - ci.minX);
                int ph = Mathf.Abs(ci.maxY - ci.minY);
                if (pw > measuredMaxW) measuredMaxW = pw;
                if (ph > measuredMaxH) measuredMaxH = ph;
            }

            // Target height based on measured glyphs + margin
            int targetH = Mathf.Clamp(measuredMaxH + 2, 4, 32);

            // Find the best valid width: start from the IDEAL (closest to measured width)
            // and go smaller only if the texture wouldn't fit in 256px vertically.
            // This maximizes glyph quality by using the widest cells that fit.
            int bestW = -1, bestH = -1;

            // Find ideal: smallest valid width >= measured glyph width
            int idealIdx = ValidGlyphWidths.Length - 1; // default to largest (32)
            for (int i = 0; i < ValidGlyphWidths.Length; i++)
            {
                if (ValidGlyphWidths[i] >= measuredMaxW)
                {
                    idealIdx = i;
                    break;
                }
            }

            // Try from ideal downward until we find one that fits
            for (int i = idealIdx; i >= 0; i--)
            {
                int vw = ValidGlyphWidths[i];
                int perRow = 256 / vw;
                int rows = Mathf.CeilToInt(95f / perRow);
                int totalH = rows * targetH;
                if (totalH <= MAX_TEXTURE_PAGE_HEIGHT)
                {
                    bestW = vw;
                    bestH = targetH;
                    break;
                }
            }

            // If nothing fits even at width=4, clamp height
            if (bestW < 0)
            {
                bestW = 4;
                int rows4 = Mathf.CeilToInt(95f / 64); // 64 per row at width 4
                bestH = Mathf.Clamp(MAX_TEXTURE_PAGE_HEIGHT / rows4, 4, 32);
                Debug.LogWarning($"PSXFontAsset: Font too large for PS1 texture page. " +
                                 $"Clamping to {bestW}x{bestH} cells.");
            }

            glyphWidth = bestW;
            glyphHeight = bestH;

            int texW = 256;
            int glyphsPerRow = texW / glyphWidth;
            int rowCount = Mathf.CeilToInt(95f / glyphsPerRow);
            int texH = rowCount * glyphHeight;

            // Compute baseline metrics for proper vertical positioning.
            // Characters sit on a common baseline. Ascenders go up, descenders go down.
            int maxAscender = 0;  // highest point above baseline (positive)
            int maxDescender = 0; // lowest point below baseline (negative)
            for (int idx = 1; idx < 95; idx++)
            {
                if (!charValid[idx]) continue;
                CharacterInfo ci = charInfos[idx];
                if (ci.maxY > maxAscender) maxAscender = ci.maxY;
                if (ci.minY < maxDescender) maxDescender = ci.minY;
            }
            int totalFontH = maxAscender - maxDescender;

            // Vertical scale only if font exceeds cell height
            float vScale = 1f;
            int usableH = glyphHeight - 2;
            if (totalFontH > usableH)
                vScale = (float)usableH / totalFontH;

            // NO horizontal scaling. Glyphs rendered at native width, left-aligned.
            // This makes the native advance widths match the bitmap exactly for
            // proportional rendering. Characters wider than cell get clipped (rare).

            // ── Step 5: Render glyphs into grid ──
            // Each glyph is LEFT-ALIGNED at native width for proportional rendering.
            // The advance widths from CharacterInfo match native glyph proportions.
            Texture2D bmp = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
            bmp.filterMode = FilterMode.Point;
            bmp.wrapMode = TextureWrapMode.Clamp;

            Color[] clearPixels = new Color[texW * texH];
            bmp.SetPixels(clearPixels);

            int renderedCount = 0;

            for (int idx = 0; idx < 95; idx++)
            {
                if (!charValid[idx]) continue;
                CharacterInfo ci = charInfos[idx];

                int col = idx % glyphsPerRow;
                int row = idx / glyphsPerRow;
                int cellX = col * glyphWidth;
                int cellY = row * glyphHeight;

                int gw = Mathf.Abs(ci.maxX - ci.minX);
                int gh = Mathf.Abs(ci.maxY - ci.minY);
                if (gw <= 0 || gh <= 0) continue;

                // Use all four UV corners to handle atlas rotation.
                // Unity's atlas packer can rotate glyphs 90 degrees to pack efficiently.
                // Wide characters like 'm' and 'M' are commonly rotated.
                // Bilinear interpolation across the UV quad handles any orientation.
                Vector2 uvBL = ci.uvBottomLeft;
                Vector2 uvBR = ci.uvBottomRight;
                Vector2 uvTL = ci.uvTopLeft;
                Vector2 uvTR = ci.uvTopRight;

                // Native width (clipped to cell), scaled height
                int renderW = Mathf.Min(gw, glyphWidth);
                int renderH = Mathf.Max(1, Mathf.RoundToInt(gh * vScale));

                // Y offset: baseline positioning
                int baselineFromTop = 1 + Mathf.RoundToInt(maxAscender * vScale);
                int glyphTopFromBaseline = Mathf.RoundToInt(ci.maxY * vScale);
                int offsetY = baselineFromTop - glyphTopFromBaseline;
                if (offsetY < 0) offsetY = 0;

                // Include left bearing so glyph sits at correct position within
                // the advance space. Negative bearing (left overhang) clamped to 0.
                int offsetX = Mathf.Max(0, ci.minX);

                bool anyPixel = false;

                for (int py = 0; py < renderH && (offsetY + py) < glyphHeight; py++)
                {
                    for (int px = 0; px < renderW && (offsetX + px) < glyphWidth; px++)
                    {
                        // Scale to fit if glyph wider than cell, 1:1 otherwise
                        float srcU = (px + 0.5f) / renderW;
                        float srcV = (py + 0.5f) / renderH;

                        // Bilinear interpolation across the UV quad (handles rotation)
                        // Bottom edge: lerp BL->BR by srcU
                        // Top edge: lerp TL->TR by srcU
                        // Then lerp bottom->top by (1-srcV) for top-down rendering
                        float t = 1f - srcV; // 0=bottom, 1=top -> invert for top-down
                        float u = Mathf.Lerp(
                            Mathf.Lerp(uvBL.x, uvBR.x, srcU),
                            Mathf.Lerp(uvTL.x, uvTR.x, srcU), t);
                        float v = Mathf.Lerp(
                            Mathf.Lerp(uvBL.y, uvBR.y, srcU),
                            Mathf.Lerp(uvTL.y, uvTR.y, srcU), t);

                        int sx = Mathf.Clamp(Mathf.FloorToInt(u * fontTexW), 0, fontTexW - 1);
                        int sy = Mathf.Clamp(Mathf.FloorToInt(v * fontTexH), 0, fontTexH - 1);
                        Color sc = fontPixels[sy * fontTexW + sx];

                        if (sc.a <= 0.3f) continue;

                        int outX = cellX + offsetX + px;
                        int outY = texH - 1 - (cellY + offsetY + py);
                        if (outX >= 0 && outX < texW && outY >= 0 && outY < texH)
                        {
                            bmp.SetPixel(outX, outY, Color.white);
                            anyPixel = true;
                        }
                    }
                }
                if (anyPixel) renderedCount++;
            }

            bmp.Apply();

            if (renderedCount == 0)
            {
                Debug.LogError("PSXFontAsset: Generated bitmap is empty.");
                DestroyImmediate(bmp);
                return;
            }

            // Store advance widths from the same CharacterInfo used for rendering.
            // This guarantees advances match the bitmap glyphs exactly.
            storedAdvanceWidths = new byte[96];
            for (int idx = 0; idx < 96; idx++)
            {
                if (idx < 95 && charValid[idx])
                {
                    CharacterInfo ci = charInfos[idx];
                    storedAdvanceWidths[idx] = (byte)Mathf.Clamp(Mathf.CeilToInt(ci.advance), 1, 255);
                }
                else
                {
                    storedAdvanceWidths[idx] = (byte)glyphWidth; // fallback
                }
            }

            // ── Step 6: Save ──
            string path = AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrEmpty(path))
            {
                fontTexture = bmp;
                return;
            }

            string dir = System.IO.Path.GetDirectoryName(path);
            string texPath = dir + "/" + name + "_bitmap.png";
            System.IO.File.WriteAllBytes(texPath, bmp.EncodeToPNG());
            DestroyImmediate(bmp);

            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.isReadable = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            fontTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }
#endif

        public byte[] ConvertTo4BPP()
        {
            if (fontTexture == null) return null;

            int texW = 256;
            int texH = TextureHeight;
            int bytesPerRow = texW / 2;
            byte[] result = new byte[bytesPerRow * texH];

            Color[] pixels = fontTexture.GetPixels(0, 0, fontTexture.width, fontTexture.height);
            int srcW = fontTexture.width;
            int srcH = fontTexture.height;

            for (int y = 0; y < texH; y++)
            {
                for (int x = 0; x < texW; x += 2)
                {
                    byte lo = SamplePixel(pixels, srcW, srcH, x, y);
                    byte hi = SamplePixel(pixels, srcW, srcH, x + 1, y);
                    result[y * bytesPerRow + x / 2] = (byte)(lo | (hi << 4));
                }
            }

            return result;
        }

        private byte SamplePixel(Color[] pixels, int srcW, int srcH, int x, int y)
        {
            if (x >= srcW || y >= srcH) return 0;
            int srcY = srcH - 1 - y; // top-down (PS1) to bottom-up (Unity)
            if (srcY < 0 || srcY >= srcH) return 0;
            Color c = pixels[srcY * srcW + x];
            return c.a > 0.5f ? (byte)1 : (byte)0;
        }
    }
}
