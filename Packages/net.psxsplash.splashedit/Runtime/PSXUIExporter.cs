using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Collects all PSXCanvas hierarchies in the scene, bakes RectTransform
    /// coordinates into PS1 pixel space, and produces <see cref="PSXCanvasData"/>
    /// arrays ready for binary serialization.
    /// </summary>
    public static class PSXUIExporter
    {
        /// <summary> 
        /// Collect all PSXCanvas components and their child UI elements,
        /// converting RectTransform coordinates to PS1 pixel space.
        /// Also collects and deduplicates custom fonts.
        /// </summary>
        /// <param name="resolution">Target PS1 resolution (e.g. 320×240).</param>
        /// <param name="fonts">Output: collected custom font data (max 3).</param>
        /// <returns>Array of canvas data ready for binary writing.</returns>
        public static PSXCanvasData[] CollectCanvases(Vector2 resolution, out PSXFontData[] fonts)
        {
            // Collect and deduplicate all custom fonts used by text elements
            List<PSXFontAsset> uniqueFonts = new List<PSXFontAsset>();

#if UNITY_EDITOR
            PSXCanvas[] canvases = Object.FindObjectsByType<PSXCanvas>(FindObjectsSortMode.None);
#else
            PSXCanvas[] canvases = Object.FindObjectsOfType<PSXCanvas>();
#endif
            if (canvases == null || canvases.Length == 0)
            {
                fonts = new PSXFontData[0];
                return new PSXCanvasData[0];
            }

            return CollectCanvasesInternal(canvases, resolution, out fonts);
        }

        /// <summary>
        /// Collect a single canvas from a prefab instance for loading screen export.
        /// The prefab must have a PSXCanvas on its root.
        /// Note: Image elements that reference VRAM textures will NOT work in loading screens
        /// since the VRAM hasn't been populated yet. Use Box, Text, and ProgressBar only.
        /// </summary>
        public static PSXCanvasData[] CollectCanvasFromPrefab(GameObject prefab, Vector2 resolution, out PSXFontData[] fonts)
        {
            if (prefab == null)
            {
                fonts = new PSXFontData[0];
                return new PSXCanvasData[0];
            }

            PSXCanvas canvas = prefab.GetComponentInChildren<PSXCanvas>(true);
            if (canvas == null)
            {
                Debug.LogWarning($"PSXUIExporter: Prefab '{prefab.name}' has no PSXCanvas component.");
                fonts = new PSXFontData[0];
                return new PSXCanvasData[0];
            }

            return CollectCanvasesInternal(new[] { canvas }, resolution, out fonts);
        }

        /// <summary>
        /// Internal shared implementation for canvas collection.
        /// Works on an explicit array of PSXCanvas components.
        /// </summary>
        private static PSXCanvasData[] CollectCanvasesInternal(PSXCanvas[] canvases, Vector2 resolution, out PSXFontData[] fonts)
        {
            List<PSXFontAsset> uniqueFonts = new List<PSXFontAsset>();

            // First pass: collect unique fonts
            foreach (PSXCanvas canvas in canvases)
            {
                PSXUIText[] texts = canvas.GetComponentsInChildren<PSXUIText>(true);
                foreach (PSXUIText txt in texts)
                {
                    PSXFontAsset font = txt.GetEffectiveFont();
                    if (font != null && !uniqueFonts.Contains(font) && uniqueFonts.Count < 3)
                        uniqueFonts.Add(font);
                }
            }

            // Build font data with VRAM positions.
            // Each font gets its own texture page to avoid V-coordinate overflow.
            // Font textures go at x=960:
            //   Font 1: y=0   (page 15,0) - 256px available
            //   Font 2: y=256 (page 15,1) - 208px available (system font at y=464)
            //   Font 3: not supported (would need different VRAM column)
            // System font: (960, 464) in page (15,1), occupies y=464-511.
            List<PSXFontData> fontDataList = new List<PSXFontData>();
            ushort[] fontPageStarts = { 0, 256 }; // one per texture page
            int fontPageIndex = 0;

            foreach (PSXFontAsset fa in uniqueFonts)
            {
                byte[] pixelData = fa.ConvertTo4BPP();
                if (pixelData == null) continue;

                // Read advance widths directly from the font asset.
                // These were computed during bitmap generation from the exact same
                // CharacterInfo used to render the glyphs - guaranteed to match.
                byte[] advances = fa.AdvanceWidths;
                if (advances == null || advances.Length < 96)
                {
                    Debug.LogWarning($"PSXUIExporter: Font '{fa.name}' has no stored advance widths. Using cell width as fallback.");
                    advances = new byte[96];
                    for (int i = 0; i < 96; i++) advances[i] = (byte)fa.GlyphWidth;
                }

                ushort texH = (ushort)fa.TextureHeight;

                if (fontPageIndex >= fontPageStarts.Length)
                {
                    Debug.LogError($"PSXUIExporter: Max 2 custom fonts supported (need separate texture pages). Skipping '{fa.name}'.");
                    continue;
                }

                ushort vramY = fontPageStarts[fontPageIndex];
                int maxHeight = (fontPageIndex == 1) ? 208 : 256; // page 1 shares with system font
                if (texH > maxHeight)
                {
                    Debug.LogWarning($"PSXUIExporter: Font '{fa.name}' texture ({texH}px) exceeds page limit ({maxHeight}px). May be clipped.");
                }

                fontDataList.Add(new PSXFontData
                {
                    Source = fa,
                    GlyphWidth = (byte)fa.GlyphWidth,
                    GlyphHeight = (byte)fa.GlyphHeight,
                    VramX = 960,
                    VramY = vramY,
                    TextureHeight = texH,
                    PixelData = pixelData,
                    AdvanceWidths = advances
                });
                fontPageIndex++;
            }
            fonts = fontDataList.ToArray();

            // Second pass: collect canvases with font index assignment
            List<PSXCanvasData> result = new List<PSXCanvasData>();

            foreach (PSXCanvas canvas in canvases)
            {
                Canvas unityCanvas = canvas.GetComponent<Canvas>();
                if (unityCanvas == null) continue;

                RectTransform canvasRect = canvas.GetComponent<RectTransform>();
                float canvasW = canvasRect.rect.width;
                float canvasH = canvasRect.rect.height;
                if (canvasW <= 0) canvasW = resolution.x;
                if (canvasH <= 0) canvasH = resolution.y;

                float scaleX = resolution.x / canvasW;
                float scaleY = resolution.y / canvasH;

                List<PSXUIElementData> elements = new List<PSXUIElementData>();

                Debug.Log($"[UIExporter] Canvas '{canvas.CanvasName}' on '{canvas.gameObject.name}' " +
                          $"canvasW={canvasW} canvasH={canvasH} childCount={canvas.transform.childCount}");

                // Collect all UI elements in hierarchy order (depth-first, sibling index).

                CollectAllElementsInHierarchyOrder(canvas.transform, canvasRect, scaleX, scaleY, resolution, elements, uniqueFonts);
                Debug.Log($"[UIExporter]   TOTAL elements: {elements.Count}");

                string name = canvas.CanvasName ?? "canvas";
                if (name.Length > 24) name = name.Substring(0, 24);

                result.Add(new PSXCanvasData
                {
                    Name = name,
                    StartVisible = canvas.StartVisible,
                    SortOrder = canvas.SortOrder,
                    Elements = elements.ToArray()
                });
            }

            return result.ToArray();
        }

        // ─── Coordinate baking helpers ───

        /// <summary>
        /// Convert a RectTransform into PS1 pixel-space layout values.
        /// Handles anchor-based positioning and Y inversion.
        /// </summary>
        private static void BakeLayout(
            RectTransform rt, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            out short x, out short y, out short w, out short h,
            out byte anchorMinX, out byte anchorMinY,
            out byte anchorMaxX, out byte anchorMaxY)
        {
            // Anchor values in 8.8 fixed point (0-255 maps to 0.0-~1.0)
            anchorMinX = (byte)Mathf.Clamp(Mathf.RoundToInt(rt.anchorMin.x * 255f), 0, 255);
            anchorMinY = (byte)Mathf.Clamp(Mathf.RoundToInt((1f - rt.anchorMax.y) * 255f), 0, 255); // Y invert
            anchorMaxX = (byte)Mathf.Clamp(Mathf.RoundToInt(rt.anchorMax.x * 255f), 0, 255);
            anchorMaxY = (byte)Mathf.Clamp(Mathf.RoundToInt((1f - rt.anchorMin.y) * 255f), 0, 255); // Y invert

            if (Mathf.Approximately(rt.anchorMin.x, rt.anchorMax.x) &&
                Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y))
            {
                // Fixed-size element with single anchor point
                // anchoredPosition is the offset from the anchor in canvas pixels
                float px = rt.anchoredPosition.x * scaleX;
                float py = -rt.anchoredPosition.y * scaleY; // Y invert
                float pw = rt.rect.width * scaleX;
                float ph = rt.rect.height * scaleY;

                // Adjust for pivot (anchoredPosition is at the pivot point)
                px -= rt.pivot.x * pw;
                py -= (1f - rt.pivot.y) * ph; // pivot Y inverted

                x = (short)Mathf.RoundToInt(px);
                y = (short)Mathf.RoundToInt(py);
                w = (short)Mathf.Max(1, Mathf.RoundToInt(pw));
                h = (short)Mathf.Max(1, Mathf.RoundToInt(ph));
            }
            else
            {
                // Stretched element: offsets from anchored edges
                // offsetMin = distance from anchorMin corner, offsetMax = distance from anchorMax corner
                float leftOff = rt.offsetMin.x * scaleX;
                float rightOff = rt.offsetMax.x * scaleX;
                float topOff = -rt.offsetMax.y * scaleY;  // Y invert
                float bottomOff = -rt.offsetMin.y * scaleY; // Y invert

                // For stretched elements, x/y store the offset from the anchor start,
                // and w/h store the combined inset (negative = shrink)
                x = (short)Mathf.RoundToInt(leftOff);
                y = (short)Mathf.RoundToInt(topOff);
                w = (short)Mathf.RoundToInt(rightOff - leftOff);
                h = (short)Mathf.RoundToInt(bottomOff - topOff);
            }
        }

        private static string TruncateName(string name, int maxLen = 24)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Length > maxLen ? name.Substring(0, maxLen) : name;
        }

        // ─── Collectors ───

        /// <summary>
        /// Walk the hierarchy depth-first in sibling order, collecting every
        /// PSX UI component into <paramref name="elements"/> so that draw order
        /// matches the Unity scene tree (top-to-bottom = back-to-front).
        /// </summary>
        private static void CollectAllElementsInHierarchyOrder(
            Transform root, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements,
            List<PSXFontAsset> uniqueFonts)
        {
            // GetComponentsInChildren iterates depth-first in sibling order —
            // exactly the hierarchy ordering we want.
            Transform[] allTransforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in allTransforms)
            {
                if (t == root) continue; // skip the canvas root itself

                // Check each supported component type on this transform.
                // A single GameObject should only have one PSX UI component,
                // but we check all to be safe.
                PSXUIImage img = t.GetComponent<PSXUIImage>();
                if (img != null)
                {
                    CollectSingleImage(img, canvasRect, scaleX, scaleY, resolution, elements);
                    continue;
                }

                PSXUIBox box = t.GetComponent<PSXUIBox>();
                if (box != null)
                {
                    CollectSingleBox(box, canvasRect, scaleX, scaleY, resolution, elements);
                    continue;
                }

                PSXUILine line = t.GetComponent<PSXUILine>();
                if (line != null)
                {
                    CollectSingleLine(line, canvasRect, scaleX, scaleY, resolution, elements);
                    continue;
                }

                PSXUIText txt = t.GetComponent<PSXUIText>();
                if (txt != null)
                {
                    CollectSingleText(txt, canvasRect, scaleX, scaleY, resolution, elements, uniqueFonts);
                    continue;
                }

                PSXUIProgressBar bar = t.GetComponent<PSXUIProgressBar>();
                if (bar != null)
                {
                    CollectSingleProgressBar(bar, canvasRect, scaleX, scaleY, resolution, elements);
                    continue;
                }
            }
        }

        private static void CollectSingleImage(
            PSXUIImage img, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements)
        {
            RectTransform rt = img.GetComponent<RectTransform>();
            if (rt == null) return;

            BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                out short x, out short y, out short w, out short h,
                out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

            var data = new PSXUIElementData
            {
                Type = PSXUIElementType.Image,
                StartVisible = img.StartVisible,
                Name = TruncateName(img.ElementName),
                X = x, Y = y, W = w, H = h,
                AnchorMinX = amin_x, AnchorMinY = amin_y,
                AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(img.TintColor.r * 255f), 0, 255),
                ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(img.TintColor.g * 255f), 0, 255),
                ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(img.TintColor.b * 255f), 0, 255),
            };

            if (img.PackedTexture != null)
            {
                PSXTexture2D tex = img.PackedTexture;
                int expander = 16 / (int)tex.BitDepth;
                data.TexpageX = tex.TexpageX;
                data.TexpageY = tex.TexpageY;
                data.ClutX = (ushort)tex.ClutPackingX;
                data.ClutY = (ushort)tex.ClutPackingY;
                data.U0 = (byte)(tex.PackingX * expander);
                data.V0 = (byte)tex.PackingY;
                data.U1 = (byte)(tex.PackingX * expander + tex.Width - 1);
                data.V1 = (byte)(tex.PackingY + tex.Height - 1);
                data.BitDepthIndex = tex.BitDepth switch
                {
                    PSXBPP.TEX_4BIT => 0,
                    PSXBPP.TEX_8BIT => 1,
                    PSXBPP.TEX_16BIT => 2,
                    _ => 2
                };

                Debug.Log($"[UIImage] '{img.ElementName}' src='{(tex.OriginalTexture ? tex.OriginalTexture.name : "null")}' " +
                          $"bpp={(int)tex.BitDepth} W={tex.Width} H={tex.Height} QW={tex.QuantizedWidth} " +
                          $"packXY=({tex.PackingX},{tex.PackingY}) tpage=({tex.TexpageX},{tex.TexpageY}) " +
                          $"clutXY=({tex.ClutPackingX},{tex.ClutPackingY}) " +
                          $"UV=({data.U0},{data.V0})->({data.U1},{data.V1}) expander={expander} bitIdx={data.BitDepthIndex}");
            }
            else
            {
                Debug.LogWarning($"[UIImage] '{img.ElementName}' has NULL PackedTexture!");
            }

            elements.Add(data);
        }

        private static void CollectSingleBox(
            PSXUIBox box, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements)
        {
            RectTransform rt = box.GetComponent<RectTransform>();
            if (rt == null) return;

            BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                out short x, out short y, out short w, out short h,
                out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

            elements.Add(new PSXUIElementData
            {
                Type = PSXUIElementType.Box,
                StartVisible = box.StartVisible,
                Name = TruncateName(box.ElementName),
                X = x, Y = y, W = w, H = h,
                AnchorMinX = amin_x, AnchorMinY = amin_y,
                AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(box.BoxColor.r * 255f), 0, 255),
                ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(box.BoxColor.g * 255f), 0, 255),
                ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(box.BoxColor.b * 255f), 0, 255),
            });
        }

        private static void CollectSingleLine(
            PSXUILine line, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements)
        {
            RectTransform rt = line.GetComponent<RectTransform>();
            if (rt == null) return;

            BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                out short x, out short y, out short w, out short h,
                out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

            elements.Add(new PSXUIElementData
            {
                Type = PSXUIElementType.Line,
                StartVisible = line.StartVisible,
                Name = TruncateName(line.ElementName),
                // Width and height are used for the second endpoint of the line
                X = (byte)Mathf.RoundToInt(line.Point1.x), Y = (byte)Mathf.RoundToInt(line.Point1.y),
                W = (byte)Mathf.RoundToInt(line.Point2.x), H = (byte)Mathf.RoundToInt(line.Point2.y),
                AnchorMinX = amin_x, AnchorMinY = amin_y,
                AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(line.LineColor.r * 255f), 0, 255),
                ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(line.LineColor.g * 255f), 0, 255),
                ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(line.LineColor.b * 255f), 0, 255),
            });
        }

        private static void CollectSingleText(
            PSXUIText txt, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements,
            List<PSXFontAsset> uniqueFonts)
        {
            RectTransform rt = txt.GetComponent<RectTransform>();
            if (rt == null) return;

            BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                out short x, out short y, out short w, out short h,
                out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

            string defaultText = txt.DefaultText ?? "";
            if (defaultText.Length > 63) defaultText = defaultText.Substring(0, 63);

            byte fontIndex = 0;
            PSXFontAsset effectiveFont = txt.GetEffectiveFont();
            if (effectiveFont != null && uniqueFonts != null)
            {
                int idx = uniqueFonts.IndexOf(effectiveFont);
                if (idx >= 0) fontIndex = (byte)(idx + 1);
            }

            elements.Add(new PSXUIElementData
            {
                Type = PSXUIElementType.Text,
                StartVisible = txt.StartVisible,
                Name = TruncateName(txt.ElementName),
                X = x, Y = y, W = w, H = h,
                AnchorMinX = amin_x, AnchorMinY = amin_y,
                AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(txt.TextColor.r * 255f), 0, 255),
                ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(txt.TextColor.g * 255f), 0, 255),
                ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(txt.TextColor.b * 255f), 0, 255),
                DefaultText = defaultText,
                FontIndex = fontIndex,
            });
        }

        private static void CollectSingleProgressBar(
            PSXUIProgressBar bar, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements)
        {
            RectTransform rt = bar.GetComponent<RectTransform>();
            if (rt == null) return;

            BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                out short x, out short y, out short w, out short h,
                out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

            elements.Add(new PSXUIElementData
            {
                Type = PSXUIElementType.Progress,
                StartVisible = bar.StartVisible,
                Name = TruncateName(bar.ElementName),
                X = x, Y = y, W = w, H = h,
                AnchorMinX = amin_x, AnchorMinY = amin_y,
                AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.FillColor.r * 255f), 0, 255),
                ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.FillColor.g * 255f), 0, 255),
                ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.FillColor.b * 255f), 0, 255),
                BgR = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.BackgroundColor.r * 255f), 0, 255),
                BgG = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.BackgroundColor.g * 255f), 0, 255),
                BgB = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.BackgroundColor.b * 255f), 0, 255),
                ProgressValue = (byte)bar.InitialValue,
            });
        }

        // ─── Legacy per-type collectors (kept for reference, no longer called) ───

        private static void CollectImages(
            Transform root, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements)
        {
            PSXUIImage[] images = root.GetComponentsInChildren<PSXUIImage>(true);
            foreach (PSXUIImage img in images)
            {
                RectTransform rt = img.GetComponent<RectTransform>();
                if (rt == null) continue;

                BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                    out short x, out short y, out short w, out short h,
                    out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

                var data = new PSXUIElementData
                {
                    Type = PSXUIElementType.Image,
                    StartVisible = img.StartVisible,
                    Name = TruncateName(img.ElementName),
                    X = x, Y = y, W = w, H = h,
                    AnchorMinX = amin_x, AnchorMinY = amin_y,
                    AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                    ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(img.TintColor.r * 255f), 0, 255),
                    ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(img.TintColor.g * 255f), 0, 255),
                    ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(img.TintColor.b * 255f), 0, 255),
                };

                // Image texture data is filled in after VRAM packing by
                // FillImageTextureData() — see PSXSceneExporter integration
                if (img.PackedTexture != null)
                {
                    PSXTexture2D tex = img.PackedTexture;
                    // Convert PackingX from VRAM halfwords to texture-pixel U coords.
                    // 4bpp: 4 pixels per halfword, 8bpp: 2, 16bpp: 1
                    int expander = 16 / (int)tex.BitDepth;
                    data.TexpageX = tex.TexpageX;
                    data.TexpageY = tex.TexpageY;
                    data.ClutX = (ushort)tex.ClutPackingX;
                    data.ClutY = (ushort)tex.ClutPackingY;
                    data.U0 = (byte)(tex.PackingX * expander);
                    data.V0 = (byte)tex.PackingY;
                    // U1/V1 are the LAST texel (inclusive), not one-past-end.
                    // Without -1, values >= 256 overflow byte to 0.
                    data.U1 = (byte)(tex.PackingX * expander + tex.Width - 1);
                    data.V1 = (byte)(tex.PackingY + tex.Height - 1);
                    data.BitDepthIndex = tex.BitDepth switch
                    {
                        PSXBPP.TEX_4BIT => 0,
                        PSXBPP.TEX_8BIT => 1,
                        PSXBPP.TEX_16BIT => 2,
                        _ => 2
                    };

                    Debug.Log($"[UIImage] '{img.ElementName}' src='{(tex.OriginalTexture ? tex.OriginalTexture.name : "null")}' " +
                              $"bpp={(int)tex.BitDepth} W={tex.Width} H={tex.Height} QW={tex.QuantizedWidth} " +
                              $"packXY=({tex.PackingX},{tex.PackingY}) tpage=({tex.TexpageX},{tex.TexpageY}) " +
                              $"clutXY=({tex.ClutPackingX},{tex.ClutPackingY}) " +
                              $"UV=({data.U0},{data.V0})->({data.U1},{data.V1}) expander={expander} bitIdx={data.BitDepthIndex}");
                }
                else
                {
                    Debug.LogWarning($"[UIImage] '{img.ElementName}' has NULL PackedTexture!");
                }

                elements.Add(data);
            }
        }

        private static void CollectBoxes(
            Transform root, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements)
        {
            PSXUIBox[] boxes = root.GetComponentsInChildren<PSXUIBox>(true);
            foreach (PSXUIBox box in boxes)
            {
                RectTransform rt = box.GetComponent<RectTransform>();
                if (rt == null) continue;

                BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                    out short x, out short y, out short w, out short h,
                    out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

                elements.Add(new PSXUIElementData
                {
                    Type = PSXUIElementType.Box,
                    StartVisible = box.StartVisible,
                    Name = TruncateName(box.ElementName),
                    X = x, Y = y, W = w, H = h,
                    AnchorMinX = amin_x, AnchorMinY = amin_y,
                    AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                    ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(box.BoxColor.r * 255f), 0, 255),
                    ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(box.BoxColor.g * 255f), 0, 255),
                    ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(box.BoxColor.b * 255f), 0, 255),
                });
            }
        }

        private static void CollectLines(
            Transform root, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements)
        {
            PSXUILine[] lines = root.GetComponentsInChildren<PSXUILine>(true);
            foreach (PSXUILine line in lines)
            {
                RectTransform rt = line.GetComponent<RectTransform>();
                if (rt == null) continue;

                BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                    out short x, out short y, out short w, out short h,
                    out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

                elements.Add(new PSXUIElementData
                {
                    Type = PSXUIElementType.Line,
                    StartVisible = line.StartVisible,
                    Name = TruncateName(line.ElementName),
                    // Width and height are used for the second endpoint of the line
                    X = (byte)Mathf.RoundToInt(line.Point1.x), Y = (byte)Mathf.RoundToInt(line.Point1.y),
                    W = (byte)Mathf.RoundToInt(line.Point2.x), H = (byte)Mathf.RoundToInt(line.Point2.y),
                    AnchorMinX = amin_x, AnchorMinY = amin_y,
                    AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                    ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(line.LineColor.r * 255f), 0, 255),
                    ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(line.LineColor.g * 255f), 0, 255),
                    ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(line.LineColor.b * 255f), 0, 255),
                });
            }
        }

        private static void CollectTexts(
            Transform root, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements,
            List<PSXFontAsset> uniqueFonts = null)
        {
            PSXUIText[] texts = root.GetComponentsInChildren<PSXUIText>(true);
            foreach (PSXUIText txt in texts)
            {
                RectTransform rt = txt.GetComponent<RectTransform>();
                if (rt == null) continue;

                BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                    out short x, out short y, out short w, out short h,
                    out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

                string defaultText = txt.DefaultText ?? "";
                if (defaultText.Length > 63) defaultText = defaultText.Substring(0, 63);

                // Resolve font index: 0 = system font, 1+ = custom font
                byte fontIndex = 0;
                PSXFontAsset effectiveFont = txt.GetEffectiveFont();
                if (effectiveFont != null && uniqueFonts != null)
                {
                    int idx = uniqueFonts.IndexOf(effectiveFont);
                    if (idx >= 0) fontIndex = (byte)(idx + 1); // 1-based for custom fonts
                }

                elements.Add(new PSXUIElementData
                {
                    Type = PSXUIElementType.Text,
                    StartVisible = txt.StartVisible,
                    Name = TruncateName(txt.ElementName),
                    X = x, Y = y, W = w, H = h,
                    AnchorMinX = amin_x, AnchorMinY = amin_y,
                    AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                    ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(txt.TextColor.r * 255f), 0, 255),
                    ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(txt.TextColor.g * 255f), 0, 255),
                    ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(txt.TextColor.b * 255f), 0, 255),
                    DefaultText = defaultText,
                    FontIndex = fontIndex,
                });
            }
        }

        private static void CollectProgressBars(
            Transform root, RectTransform canvasRect,
            float scaleX, float scaleY, Vector2 resolution,
            List<PSXUIElementData> elements)
        {
            PSXUIProgressBar[] bars = root.GetComponentsInChildren<PSXUIProgressBar>(true);
            foreach (PSXUIProgressBar bar in bars)
            {
                RectTransform rt = bar.GetComponent<RectTransform>();
                if (rt == null) continue;

                BakeLayout(rt, canvasRect, scaleX, scaleY, resolution,
                    out short x, out short y, out short w, out short h,
                    out byte amin_x, out byte amin_y, out byte amax_x, out byte amax_y);

                elements.Add(new PSXUIElementData
                {
                    Type = PSXUIElementType.Progress,
                    StartVisible = bar.StartVisible,
                    Name = TruncateName(bar.ElementName),
                    X = x, Y = y, W = w, H = h,
                    AnchorMinX = amin_x, AnchorMinY = amin_y,
                    AnchorMaxX = amax_x, AnchorMaxY = amax_y,
                    // Fill color goes into primary color (used for the fill bar)
                    ColorR = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.FillColor.r * 255f), 0, 255),
                    ColorG = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.FillColor.g * 255f), 0, 255),
                    ColorB = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.FillColor.b * 255f), 0, 255),
                    // Background color goes into progress-specific fields
                    BgR = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.BackgroundColor.r * 255f), 0, 255),
                    BgG = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.BackgroundColor.g * 255f), 0, 255),
                    BgB = (byte)Mathf.Clamp(Mathf.RoundToInt(bar.BackgroundColor.b * 255f), 0, 255),
                    ProgressValue = (byte)bar.InitialValue,
                });
            }
        }
    }
}
