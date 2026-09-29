using System;
using System.IO;
using UnityEngine;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// Memory analysis report for a single exported scene.
    /// All values are in bytes unless noted otherwise.
    /// </summary>
    [Serializable]
    public class SceneMemoryReport
    {
        public string sceneName;

        // ─── Main RAM ───
        public long splashpackFileSize;     // Total file on disc
        public long splashpackLiveSize;     // Bytes kept in RAM at runtime (before bulk data freed)
        public int  triangleCount;
        public int  gameObjectCount;

        // ─── VRAM (1024 x 512 x 2 = 1,048,576 bytes) ───
        public long framebufferSize;        // 2 x W x H x 2
        public long textureAtlasSize;       // Sum of atlas pixel data
        public long clutSize;               // Sum of CLUT entries x 2
        public long fontVramSize;           // Custom font textures
        public int  atlasCount;
        public int  clutCount;

        // ─── SPU RAM (512KB, 0x1010 reserved) ───
        public long audioDataSize;
        public int  audioClipCount;

        // ─── CD Storage ───
        public long loaderPackSize;

        // ─── Constants ───
        public const long TOTAL_RAM         = 2 * 1024 * 1024;
        public const long KERNEL_RESERVED   = 0x10000;          // 64KB kernel area
        public const long USABLE_RAM        = TOTAL_RAM - KERNEL_RESERVED;
        public const long TOTAL_VRAM        = 1024 * 512 * 2;   // 1MB
        public const long TOTAL_SPU         = 512 * 1024;
        public const long SPU_RESERVED      = 0x1010;
        public const long USABLE_SPU        = TOTAL_SPU - SPU_RESERVED;

        // Fixed runtime overhead from C++ (renderer.hh constants, now configurable)
        public static long BUMP_ALLOC_TOTAL  => 2L * SplashSettings.BumpSize;
        public static long OT_TOTAL          => 2L * SplashSettings.OtSize * 4;
        public const long VIS_REFS          = 4096 * 4;          // 16KB
        public const long STACK_ESTIMATE    = 32 * 1024;         // 32KB
        public const long LUA_OVERHEAD      = 16 * 1024;         // 16KB approximate
        public const long SYSTEM_FONT_VRAM  = 4 * 1024;          // ~4KB

        public long FixedOverhead => BUMP_ALLOC_TOTAL + OT_TOTAL + VIS_REFS + STACK_ESTIMATE + LUA_OVERHEAD;

        // Heap estimate and warnings
        public long EstimatedHeapFree => USABLE_RAM - TotalRamUsage;
        public bool IsHeapWarning  => EstimatedHeapFree < 128 * 1024;   // < 128KB free
        public bool IsHeapCritical => EstimatedHeapFree < 64 * 1024;    // < 64KB free

        /// <summary>RAM used by scene data (live portion of splashpack).</summary>
        public long SceneRamUsage => splashpackLiveSize > 0 ? splashpackLiveSize : splashpackFileSize;

        /// <summary>Total estimated RAM: fixed overhead + scene data. Does NOT include code/BSS.</summary>
        public long TotalRamUsage => FixedOverhead + SceneRamUsage;

        public long TotalVramUsed => framebufferSize + textureAtlasSize + clutSize + fontVramSize + SYSTEM_FONT_VRAM;
        public long TotalSpuUsed  => audioDataSize;
        public long TotalDiscSize => splashpackFileSize + loaderPackSize;

        public float RamPercent  => Mathf.Clamp01((float)TotalRamUsage / USABLE_RAM) * 100f;
        public float VramPercent => Mathf.Clamp01((float)TotalVramUsed / TOTAL_VRAM) * 100f;
        public float SpuPercent  => USABLE_SPU > 0 ? Mathf.Clamp01((float)TotalSpuUsed / USABLE_SPU) * 100f : 0f;

        public long RamFree  => USABLE_RAM - TotalRamUsage;
        public long VramFree => TOTAL_VRAM - TotalVramUsed;
        public long SpuFree  => USABLE_SPU - TotalSpuUsed;
    }

    /// <summary>
    /// Builds a SceneMemoryReport by reading the exported splashpack binary header
    /// and the scene's VRAM/audio data.
    /// </summary>
    public static class SceneMemoryAnalyzer
    {
        /// <summary>
        /// Analyze an exported scene. Call after ExportToPath().
        /// </summary>
        /// <param name="sceneName">Display name for the scene.</param>
        /// <param name="splashpackPath">Path to the exported .splashpack file.</param>
        /// <param name="loaderPackPath">Path to the loading screen file (may be null).</param>
        /// <param name="atlases">Texture atlases from the export pipeline.</param>
        /// <param name="audioExportSizes">Array of ADPCM byte sizes per audio clip.</param>
        /// <param name="fonts">Custom font descriptors.</param>
        public static SceneMemoryReport Analyze(
            string sceneName,
            string splashpackPath,
            string loaderPackPath,
            SplashEdit.RuntimeCode.TextureAtlas[] atlases,
            long[] audioExportSizes,
            SplashEdit.RuntimeCode.PSXFontData[] fonts,
            int triangleCount = 0)
        {
            var r = new SceneMemoryReport { sceneName = sceneName };

            // ── File sizes ──
            if (File.Exists(splashpackPath))
                r.splashpackFileSize = new FileInfo(splashpackPath).Length;
            if (!string.IsNullOrEmpty(loaderPackPath) && File.Exists(loaderPackPath))
                r.loaderPackSize = new FileInfo(loaderPackPath).Length;

            r.triangleCount = triangleCount;

            // ── Parse splashpack header for counts and pixelDataOffset ──
            if (File.Exists(splashpackPath))
            {
                try { ReadHeader(splashpackPath, r); }
                catch (Exception e) { Debug.LogWarning($"Memory report: failed to read header: {e.Message}"); }
            }

            // ── Framebuffers ──
            int fbW = SplashSettings.ResolutionWidth;
            int fbH = SplashSettings.ResolutionHeight;
            int fbCount = SplashSettings.DualBuffering ? 2 : 1;
            r.framebufferSize = fbW * fbH * 2L * fbCount;

            // ── VRAM: Texture atlases + CLUTs ──
            if (atlases != null)
            {
                r.atlasCount = atlases.Length;
                foreach (var atlas in atlases)
                {
                    r.textureAtlasSize += atlas.Width * SplashEdit.RuntimeCode.TextureAtlas.Height * 2L;
                    foreach (var tex in atlas.ContainedTextures)
                    {
                        if (tex.ColorPalette != null)
                        {
                            r.clutCount++;
                            r.clutSize += tex.ColorPalette.Count * 2L;
                        }
                    }
                }
            }

            // ── VRAM: Custom fonts ──
            if (fonts != null)
            {
                foreach (var font in fonts)
                {
                    if (font.TextureHeight > 0)
                        r.fontVramSize += 64L * font.TextureHeight * 2; // 4bpp = 64 hwords wide
                }
            }

            // ── SPU: Audio ──
            if (audioExportSizes != null)
            {
                r.audioClipCount = audioExportSizes.Length;
                foreach (long sz in audioExportSizes)
                    r.audioDataSize += sz;
            }

            return r;
        }

        private static void ReadHeader(string path, SceneMemoryReport r)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                if (reader.BaseStream.Length < 104) return;

                // Magic + version (4 bytes)
                reader.ReadBytes(4);

                // luaFileCount(2) + gameObjectCount(2) + textureAtlasCount(2) + clutCount(2)
                reader.ReadUInt16(); // luaFileCount
                r.gameObjectCount = reader.ReadUInt16();
                reader.ReadUInt16(); // textureAtlasCount
                reader.ReadUInt16(); // clutCount

                // Skip to pixelDataOffset at byte 100
                reader.BaseStream.Seek(100, SeekOrigin.Begin);
                uint pixelDataOffset = reader.ReadUInt32();
                r.splashpackLiveSize = pixelDataOffset > 0 ? pixelDataOffset : r.splashpackFileSize;
            }
        }
    }
}
