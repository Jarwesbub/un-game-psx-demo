using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Writes a standalone "loader pack" binary (.loading) for a loading screen canvas.
    /// 
    /// Format v2:
    ///   Header (16 bytes):
    ///     char[2]   magic      = "LP"
    ///     uint16    version    = 2
    ///     uint8     fontCount
    ///     uint8     canvasCount (always 1)
    ///     uint16    resW       — target PS1 resolution width
    ///     uint16    resH       — target PS1 resolution height
    ///     uint8     atlasCount — number of texture atlases
    ///     uint8     clutCount  — number of CLUTs
    ///     uint32    tableOffset — offset to UI table (font descs + canvas data)
    ///
    ///   After header (at offset 16):
    ///     Atlas headers (12 bytes each × atlasCount)
    ///     CLUT headers  (12 bytes each × clutCount)
    ///     Atlas pixel data (referenced by offsets in atlas headers)
    ///     CLUT pixel data  (referenced by offsets in CLUT headers)
    ///
    ///   At tableOffset:
    ///     Same layout as the splashpack UI section:
    ///     - Font descriptors (112 bytes each)
    ///     - Font pixel data
    ///     - Canvas descriptors (12 bytes each)
    ///     - Element data (48 bytes per element)
    ///     - String data (names + text content)
    ///
    /// This reuses the same binary layout that UISystem::loadFromSplashpack() parses,
    /// so the C++ side can reuse the same parsing code.
    /// </summary>
    public static class PSXLoaderPackWriter
    {
        public const ushort LOADER_PACK_VERSION = 2;

        /// <summary>
        /// Write a loader pack file for a loading screen canvas prefab.
        /// </summary>
        /// <param name="path">Output file path.</param>
        /// <param name="prefab">The loading screen prefab (must contain PSXCanvas).</param>
        /// <param name="resolution">Target PS1 resolution.</param>
        /// <param name="atlases">Texture atlases from VRAM packing (may be null if no images).</param>
        /// <param name="log">Optional log callback.</param>
        /// <returns>True on success.</returns>
        public static bool Write(string path, GameObject prefab, Vector2 resolution,
                                 TextureAtlas[] atlases = null,
                                 System.Action<string, LogType> log = null)
        {
            if (prefab == null)
            {
                log?.Invoke("LoaderPackWriter: No prefab specified.", LogType.Error);
                return false;
            }

            // Collect canvas data from the prefab
            PSXFontData[] fonts;
            PSXCanvasData[] canvases = PSXUIExporter.CollectCanvasFromPrefab(prefab, resolution, out fonts);

            if (canvases == null || canvases.Length == 0)
            {
                log?.Invoke($"LoaderPackWriter: No PSXCanvas found in prefab '{prefab.name}'.", LogType.Error);
                return false;
            }

            // Only export the first canvas (loading screen = single canvas)
            PSXCanvasData canvas = canvases[0];

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                // Count CLUTs across all atlases
                int clutCount = 0;
                if (atlases != null)
                {
                    foreach (var atlas in atlases)
                        foreach (var tex in atlas.ContainedTextures)
                            if (tex.ColorPalette != null)
                                clutCount++;
                }

                // ── Header (16 bytes) ──
                writer.Write((byte)'L');
                writer.Write((byte)'P');
                writer.Write(LOADER_PACK_VERSION);
                writer.Write((byte)(fonts?.Length ?? 0));
                writer.Write((byte)1); // canvasCount = 1
                writer.Write((ushort)resolution.x);
                writer.Write((ushort)resolution.y);
                writer.Write((byte)(atlases?.Length ?? 0)); // atlasCount
                writer.Write((byte)clutCount);              // clutCount
                long tableOffsetPos = writer.BaseStream.Position;
                writer.Write((uint)0); // tableOffset placeholder

                // ── Atlas headers (12 bytes each) ──
                List<long> atlasOffsetPlaceholders = new List<long>();
                if (atlases != null)
                {
                    foreach (var atlas in atlases)
                    {
                        atlasOffsetPlaceholders.Add(writer.BaseStream.Position);
                        writer.Write((uint)0); // pixelDataOffset placeholder
                        writer.Write((ushort)atlas.Width);
                        writer.Write((ushort)TextureAtlas.Height);
                        writer.Write((ushort)atlas.PositionX);
                        writer.Write((ushort)atlas.PositionY);
                    }
                }

                // ── CLUT headers (12 bytes each) ──
                List<long> clutOffsetPlaceholders = new List<long>();
                if (atlases != null)
                {
                    foreach (var atlas in atlases)
                    {
                        foreach (var tex in atlas.ContainedTextures)
                        {
                            if (tex.ColorPalette != null)
                            {
                                clutOffsetPlaceholders.Add(writer.BaseStream.Position);
                                writer.Write((uint)0); // clutDataOffset placeholder
                                writer.Write((ushort)tex.ClutPackingX);
                                writer.Write((ushort)tex.ClutPackingY);
                                writer.Write((ushort)tex.ColorPalette.Count);
                                writer.Write((ushort)0); // pad
                            }
                        }
                    }
                }

                // ── Atlas pixel data ──
                int atlasIdx = 0;
                if (atlases != null)
                {
                    foreach (var atlas in atlases)
                    {
                        AlignToFourBytes(writer);
                        long dataPos = writer.BaseStream.Position;

                        // Backfill this atlas header's pixelDataOffset
                        long cur = writer.BaseStream.Position;
                        writer.Seek((int)atlasOffsetPlaceholders[atlasIdx], SeekOrigin.Begin);
                        writer.Write((uint)dataPos);
                        writer.Seek((int)cur, SeekOrigin.Begin);

                        // Write pixel data in row-major order (same as PSXSceneWriter)
                        for (int y = 0; y < atlas.vramPixels.GetLength(1); y++)
                            for (int x = 0; x < atlas.vramPixels.GetLength(0); x++)
                                writer.Write(atlas.vramPixels[x, y].Pack());

                        atlasIdx++;
                    }
                }

                // ── CLUT pixel data ──
                int clutIdx = 0;
                if (atlases != null)
                {
                    foreach (var atlas in atlases)
                    {
                        foreach (var tex in atlas.ContainedTextures)
                        {
                            if (tex.ColorPalette != null)
                            {
                                AlignToFourBytes(writer);
                                long dataPos = writer.BaseStream.Position;

                                // Backfill this CLUT header's clutDataOffset
                                long cur = writer.BaseStream.Position;
                                writer.Seek((int)clutOffsetPlaceholders[clutIdx], SeekOrigin.Begin);
                                writer.Write((uint)dataPos);
                                writer.Seek((int)cur, SeekOrigin.Begin);

                                foreach (VRAMPixel color in tex.ColorPalette)
                                    writer.Write((ushort)color.Pack());

                                clutIdx++;
                            }
                        }
                    }
                }

                // ── Font pixel data (written BEFORE the UI table, alongside atlas/CLUT data) ──
                // The C++ parser expects canvas descriptors immediately after font descriptors
                // (font pixel data is at absolute offsets, not inline). Write pixel data here
                // so it doesn't sit between font descriptors and canvas descriptors.
                List<long> fontDataOffsetPositions = new List<long>();
                List<long> fontPixelDataPositions = new List<long>();
                if (fonts != null)
                {
                    for (int fi = 0; fi < fonts.Length; fi++)
                    {
                        var font = fonts[fi];
                        if (font.PixelData == null || font.PixelData.Length == 0)
                        {
                            fontPixelDataPositions.Add(0);
                            continue;
                        }

                        AlignToFourBytes(writer);
                        long dataPos = writer.BaseStream.Position;
                        writer.Write(font.PixelData);
                        fontPixelDataPositions.Add(dataPos);
                    }
                }

                // ── UI table (same format as splashpack UI section) ──
                AlignToFourBytes(writer);
                long uiTableStart = writer.BaseStream.Position;

                // ── Font descriptors (112 bytes each) ──
                if (fonts != null)
                {
                    for (int fi = 0; fi < fonts.Length; fi++)
                    {
                        var font = fonts[fi];
                        writer.Write(font.GlyphWidth);          // [0]
                        writer.Write(font.GlyphHeight);         // [1]
                        writer.Write(font.VramX);               // [2-3]
                        writer.Write(font.VramY);               // [4-5]
                        writer.Write(font.TextureHeight);       // [6-7]
                        // dataOffset: use the pre-written pixel data position
                        long pixPos = fontPixelDataPositions[fi];
                        writer.Write((uint)pixPos);             // [8-11] dataOffset (0 if no data)
                        writer.Write((uint)(font.PixelData?.Length ?? 0)); // [12-15] dataSize
                        if (font.AdvanceWidths != null && font.AdvanceWidths.Length >= 96)
                            writer.Write(font.AdvanceWidths, 0, 96);
                        else
                            writer.Write(new byte[96]);
                    }
                }

                // Canvas descriptors now follow immediately after font descriptors
                // (no font pixel data in between — it was written above).

                // ── Canvas descriptor (12 bytes) ──
                // Must align here: the C++ parser aligns fontDataEnd to 4 bytes
                // when skipping past font pixel data to find the canvas descriptor.
                AlignToFourBytes(writer);
                var elements = canvas.Elements ?? new PSXUIElementData[0];
                string cvName = canvas.Name ?? "loading";
                if (cvName.Length > 24) cvName = cvName.Substring(0, 24);

                long canvasDataOffsetPos = writer.BaseStream.Position;
                writer.Write((uint)0); // dataOffset placeholder
                writer.Write((byte)cvName.Length);
                writer.Write(canvas.SortOrder);
                writer.Write((byte)elements.Length);
                byte flags = 0;
                if (canvas.StartVisible) flags |= 0x01;
                writer.Write(flags);
                long canvasNameOffsetPos = writer.BaseStream.Position;
                writer.Write((uint)0); // nameOffset placeholder

                // ── Element data (48 bytes per element) ──
                AlignToFourBytes(writer);
                long elemDataStart = writer.BaseStream.Position;

                // Backfill canvas data offset
                {
                    long cur = writer.BaseStream.Position;
                    writer.Seek((int)canvasDataOffsetPos, SeekOrigin.Begin);
                    writer.Write((uint)elemDataStart);
                    writer.Seek((int)cur, SeekOrigin.Begin);
                }

                List<long> textOffsetPositions = new List<long>();
                List<string> textContents = new List<string>();

                for (int ei = 0; ei < elements.Length; ei++)
                {
                    var el = elements[ei];

                    // Identity (8 bytes)
                    writer.Write((byte)el.Type);
                    byte eFlags = 0;
                    if (el.StartVisible) eFlags |= 0x01;
                    writer.Write(eFlags);
                    string eName = el.Name ?? "";
                    if (eName.Length > 24) eName = eName.Substring(0, 24);
                    writer.Write((byte)eName.Length);
                    writer.Write((byte)0); // pad0
                    long elemNameOffPos = writer.BaseStream.Position;
                    writer.Write((uint)0); // nameOffset placeholder

                    // Layout (8 bytes)
                    writer.Write(el.X);
                    writer.Write(el.Y);
                    writer.Write(el.W);
                    writer.Write(el.H);

                    // Anchors (4 bytes)
                    writer.Write(el.AnchorMinX);
                    writer.Write(el.AnchorMinY);
                    writer.Write(el.AnchorMaxX);
                    writer.Write(el.AnchorMaxY);

                    // Primary color (4 bytes)
                    writer.Write(el.ColorR);
                    writer.Write(el.ColorG);
                    writer.Write(el.ColorB);
                    writer.Write((byte)0); // pad1

                    // Type-specific data (16 bytes)
                    switch (el.Type)
                    {
                        case PSXUIElementType.Image:
                            writer.Write(el.TexpageX);
                            writer.Write(el.TexpageY);
                            writer.Write(el.ClutX);
                            writer.Write(el.ClutY);
                            writer.Write(el.U0);
                            writer.Write(el.V0);
                            writer.Write(el.U1);
                            writer.Write(el.V1);
                            writer.Write(el.BitDepthIndex);
                            writer.Write(new byte[5]);
                            break;
                        case PSXUIElementType.Progress:
                            writer.Write(el.BgR);
                            writer.Write(el.BgG);
                            writer.Write(el.BgB);
                            writer.Write(el.ProgressValue);
                            writer.Write(new byte[12]);
                            break;
                        case PSXUIElementType.Text:
                            writer.Write(el.FontIndex);
                            writer.Write(new byte[15]);
                            break;
                        default:
                            writer.Write(new byte[16]);
                            break;
                    }

                    // Text content offset (8 bytes)
                    long textOff = writer.BaseStream.Position;
                    writer.Write((uint)0); // textOffset placeholder
                    writer.Write((uint)0); // pad2

                    textOffsetPositions.Add(textOff);
                    textContents.Add(el.Type == PSXUIElementType.Text ? (el.DefaultText ?? "") : null);

                    textOffsetPositions.Add(elemNameOffPos);
                    textContents.Add("__NAME__" + eName);
                }

                // ── String data (text content + element names) ──
                for (int si = 0; si < textOffsetPositions.Count; si++)
                {
                    string content = textContents[si];
                    if (content == null) continue;

                    bool isName = content.StartsWith("__NAME__");
                    string str = isName ? content.Substring(8) : content;
                    if (string.IsNullOrEmpty(str)) continue;

                    AlignToFourBytes(writer);
                    long strPos = writer.BaseStream.Position;
                    byte[] strBytes = Encoding.UTF8.GetBytes(str);
                    writer.Write(strBytes);
                    writer.Write((byte)0); // null terminator

                    long cur = writer.BaseStream.Position;
                    writer.Seek((int)textOffsetPositions[si], SeekOrigin.Begin);
                    writer.Write((uint)strPos);
                    writer.Seek((int)cur, SeekOrigin.Begin);
                }

                // ── Canvas name ──
                {
                    AlignToFourBytes(writer);
                    long namePos = writer.BaseStream.Position;
                    byte[] nameBytes = Encoding.UTF8.GetBytes(cvName);
                    writer.Write(nameBytes);
                    writer.Write((byte)0);

                    long cur = writer.BaseStream.Position;
                    writer.Seek((int)canvasNameOffsetPos, SeekOrigin.Begin);
                    writer.Write((uint)namePos);
                    writer.Seek((int)cur, SeekOrigin.Begin);
                }

                // ── Backfill header table offset ──
                {
                    long cur = writer.BaseStream.Position;
                    writer.Seek((int)tableOffsetPos, SeekOrigin.Begin);
                    writer.Write((uint)uiTableStart);
                    writer.Seek((int)cur, SeekOrigin.Begin);
                }
            }

            log?.Invoke($"LoaderPackWriter: Wrote loading screen '{canvas.Name}' to {Path.GetFileName(path)}", LogType.Log);
            return true;
        }

        private static void AlignToFourBytes(BinaryWriter writer)
        {
            long pos = writer.BaseStream.Position;
            int pad = (int)((4 - (pos % 4)) % 4);
            for (int i = 0; i < pad; i++)
                writer.Write((byte)0);
        }
    }
}
