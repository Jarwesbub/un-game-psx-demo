using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Memory usage breakdown for a single exported scene.
    /// Covers Main RAM, VRAM, SPU RAM, and CD storage.
    /// </summary>
    public class SceneMemoryReport
    {
        // ---- Capacities (PS1 hardware limits) ----
        public const long MainRamCapacity = 2 * 1024 * 1024;       // 2 MB
        public const long VramCapacity = 1024 * 512 * 2;           // 1 MB (1024x512 @ 16bpp)
        public const long SpuRamCapacity = 512 * 1024;             // 512 KB
        public const long CdRomCapacity = 650L * 1024 * 1024;      // 650 MB (standard CD)

        // ---- Scene name ----
        public string sceneName;

        // ---- Splashpack section sizes (bytes) ----
        public long headerBytes;
        public long luaBytes;
        public long gameObjectBytes;
        public long colliderBytes;
        public long bvhBytes;
        public long interactableBytes;
        public long collisionBytes;
        public long navRegionBytes;
        public long roomPortalBytes;
        public long atlasMetadataBytes;
        public long clutMetadataBytes;
        public long meshDataBytes;
        public long atlasPixelBytes;
        public long clutDataBytes;
        public long nameTableBytes;
        public long audioMetadataBytes;
        public long audioDataBytes;
        public long skinnedMeshBytes;

        // ---- Counts ----
        public int objectCount;
        public int triangleCount;
        public int atlasCount;
        public int clutCount;
        public int audioClipCount;
        public int navRegionCount;
        public int roomCount;
        public int portalCount;
        public int skinnedMeshCount;

        // ---- Main RAM estimate ----
        // Renderer double-buffered overhead (ordering tables + bump allocators)
        public long rendererOverhead;
        // PSYQo + psxsplash executable code (estimated from .ps-exe if available)
        public long executableBytes;

        // ---- VRAM breakdown ----
        public long framebufferVramBytes;
        public long textureVramBytes;
        public long clutVramBytes;

        // ---- SPU RAM ----
        public const long SpuReservedBytes = 0x1010; // capture buffers + PSYQo dummy sample
        public long spuAudioBytes;

        // ---- Computed properties ----

        public long TotalSplashpackBytes =>
            headerBytes + luaBytes + gameObjectBytes + colliderBytes +
            bvhBytes + interactableBytes + collisionBytes + navRegionBytes +
            roomPortalBytes + atlasMetadataBytes + clutMetadataBytes +
            meshDataBytes + atlasPixelBytes + clutDataBytes +
            nameTableBytes + audioMetadataBytes + audioDataBytes +
            skinnedMeshBytes;

        /// <summary>
        /// Total Main RAM usage estimate. Splashpack data is loaded into heap at runtime.
        /// </summary>
        public long TotalMainRamBytes =>
            executableBytes + TotalSplashpackBytes + rendererOverhead + 8192 /* stack */ + 16384 /* misc heap */;

        public float MainRamPercent => (float)TotalMainRamBytes / MainRamCapacity;
        public long MainRamFree => MainRamCapacity - TotalMainRamBytes;

        public long TotalVramBytes => framebufferVramBytes + textureVramBytes + clutVramBytes;
        public float VramPercent => (float)TotalVramBytes / VramCapacity;
        public long VramFree => VramCapacity - TotalVramBytes;

        public long TotalSpuBytes => SpuReservedBytes + spuAudioBytes;
        public float SpuPercent => (float)TotalSpuBytes / SpuRamCapacity;
        public long SpuFree => SpuRamCapacity - TotalSpuBytes;

        // CD storage includes the splashpack file
        public long CdSceneBytes => TotalSplashpackBytes;

        // ---- Breakdown lists for UI ----

        public struct BarSegment
        {
            public string label;
            public long bytes;
            public Color color;
        }

        public List<BarSegment> GetMainRamSegments()
        {
            var segments = new List<BarSegment>();
            if (executableBytes > 0)
                segments.Add(new BarSegment { label = "Executable", bytes = executableBytes, color = new Color(0.4f, 0.6f, 0.9f) });
            if (meshDataBytes > 0)
                segments.Add(new BarSegment { label = "Mesh Data", bytes = meshDataBytes, color = new Color(0.3f, 0.85f, 0.45f) });
            if (atlasPixelBytes > 0)
                segments.Add(new BarSegment { label = "Texture Data", bytes = atlasPixelBytes, color = new Color(0.95f, 0.75f, 0.2f) });
            if (audioDataBytes > 0)
                segments.Add(new BarSegment { label = "Audio Data", bytes = audioDataBytes, color = new Color(0.85f, 0.3f, 0.65f) });
            if (skinnedMeshBytes > 0)
                segments.Add(new BarSegment { label = "Skinned Mesh", bytes = skinnedMeshBytes, color = new Color(0.7f, 0.4f, 0.85f) });

            long otherSplashpack = TotalSplashpackBytes - meshDataBytes - atlasPixelBytes - audioDataBytes - skinnedMeshBytes;
            if (otherSplashpack > 0)
                segments.Add(new BarSegment { label = "Scene Metadata", bytes = otherSplashpack, color = new Color(0.6f, 0.6f, 0.65f) });

            if (rendererOverhead > 0)
                segments.Add(new BarSegment { label = "Renderer Buffers", bytes = rendererOverhead, color = new Color(0.3f, 0.85f, 0.95f) });

            long misc = 8192 + 16384; // stack + misc heap
            segments.Add(new BarSegment { label = "Stack + Misc", bytes = misc, color = new Color(0.45f, 0.45f, 0.5f) });

            return segments;
        }

        public List<BarSegment> GetVramSegments()
        {
            var segments = new List<BarSegment>();
            if (framebufferVramBytes > 0)
                segments.Add(new BarSegment { label = "Framebuffers", bytes = framebufferVramBytes, color = new Color(0.9f, 0.3f, 0.35f) });
            if (textureVramBytes > 0)
                segments.Add(new BarSegment { label = "Texture Atlases", bytes = textureVramBytes, color = new Color(0.95f, 0.75f, 0.2f) });
            if (clutVramBytes > 0)
                segments.Add(new BarSegment { label = "CLUTs", bytes = clutVramBytes, color = new Color(0.85f, 0.5f, 0.2f) });
            return segments;
        }

        public List<BarSegment> GetSpuSegments()
        {
            var segments = new List<BarSegment>();
            segments.Add(new BarSegment { label = "Reserved", bytes = SpuReservedBytes, color = new Color(0.45f, 0.45f, 0.5f) });
            if (spuAudioBytes > 0)
                segments.Add(new BarSegment { label = "Audio Clips", bytes = spuAudioBytes, color = new Color(0.85f, 0.3f, 0.65f) });
            return segments;
        }

        /// <summary>
        /// Get a severity color for a usage percentage.
        /// </summary>
        public static Color GetUsageColor(float percent)
        {
            if (percent < 0.6f) return new Color(0.35f, 0.85f, 0.45f);      // green
            if (percent < 0.8f) return new Color(0.95f, 0.75f, 0.2f);       // yellow
            if (percent < 0.95f) return new Color(0.95f, 0.5f, 0.2f);       // orange
            return new Color(0.9f, 0.3f, 0.35f);                             // red
        }
    }
}
