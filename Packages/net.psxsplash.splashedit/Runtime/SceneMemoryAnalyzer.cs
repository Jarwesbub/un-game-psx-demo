using System.Text;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Computes a SceneMemoryReport from scene export data.
    /// All sizes match PSXSceneWriter's binary layout exactly.
    /// </summary>
    public static class SceneMemoryAnalyzer
    {
        // Per-triangle binary size in splashpack (matches PSXSceneWriter.Write mesh section)
        // 3 vertices * 6 bytes + normal 6 bytes + 3 colors * 4 bytes + 3 UVs * 2 bytes + 2 pad + tpage 2 + clutXY 4 + pad 2
        private const int BytesPerTriangle = 52;

        // Per-GameObject entry in splashpack
        // offset(4) + position(12) + rotation(36) + polyCount(2) + luaIdx(2) + flags(4) + components(8) + AABB(24) = 92
        private const int BytesPerGameObject = 92;

        // Per-collider entry
        private const int BytesPerCollider = 32;

        // Per-interactable entry
        private const int BytesPerInteractable = 24;

        // Atlas metadata entry
        private const int BytesPerAtlasMetadata = 12;

        // CLUT metadata entry
        private const int BytesPerClutMetadata = 12;

        // Audio clip metadata entry (offset + size + sampleRate + flags + nameOffset)
        private const int BytesPerAudioMetadata = 16;

        // Renderer ordering table size (from renderer.hh)
        private const int OrderingTableSize = 2048 * 3;

        // Renderer bump allocator size (from renderer.hh)
        private const int BumpAllocatorSize = 8096 * 24;

        /// <summary>
        /// Analyze scene data and produce a memory report.
        /// Call this after PSXSceneWriter.Write to get accurate stats,
        /// or before to get estimates.
        /// </summary>
        public static SceneMemoryReport Analyze(
            in PSXSceneWriter.SceneData scene,
            string sceneName,
            int resolutionWidth,
            int resolutionHeight,
            bool dualBuffering,
            long compiledExeBytes = 0)
        {
            var report = new SceneMemoryReport();
            report.sceneName = sceneName;

            // ---- Count CLUTs ----
            int clutCount = 0;
            foreach (var atlas in scene.atlases)
                foreach (var tex in atlas.ContainedTextures)
                    if (tex.ColorPalette != null)
                        clutCount++;

            // ---- Count Lua files ----
            var luaFiles = new System.Collections.Generic.List<LuaFile>();
            foreach (var exporter in scene.exporters)
                if (exporter.LuaFile != null && !luaFiles.Contains(exporter.LuaFile))
                    luaFiles.Add(exporter.LuaFile);
            if (scene.sceneLuaFile != null && !luaFiles.Contains(scene.sceneLuaFile))
                luaFiles.Add(scene.sceneLuaFile);

            // ---- Count colliders ----
            int colliderCount = 0;
            foreach (var e in scene.exporters)
                if (e.CollisionType != PSXCollisionType.None)
                    colliderCount++;

            // ---- Count triangles ----
            int totalTriangles = 0;
            foreach (var e in scene.exporters)
                totalTriangles += e.Mesh.Triangles.Count;

            // ---- Store counts ----
            report.objectCount = scene.exporters.Length;
            report.triangleCount = totalTriangles;
            report.atlasCount = scene.atlases.Length;
            report.clutCount = clutCount;
            report.audioClipCount = scene.audioClips?.Length ?? 0;
            report.navRegionCount = scene.navRegionBuilder?.RegionCount ?? 0;
            report.roomCount = scene.roomBuilder?.RoomCount ?? 0;
            report.portalCount = scene.roomBuilder?.PortalCount ?? 0;

            // ---- Header (v11, ~96 bytes with all extensions) ----
            report.headerBytes = 96; // approximate header size including all version extensions

            // ---- Lua section ----
            long luaBytes = 0;
            foreach (var lua in luaFiles)
            {
                luaBytes += 8; // offset placeholder + size
                luaBytes += Align4(Encoding.UTF8.GetByteCount(lua.LuaScript));
            }
            report.luaBytes = luaBytes;

            // ---- GameObject section ----
            report.gameObjectBytes = (long)scene.exporters.Length * BytesPerGameObject;

            // ---- Collider section ----
            report.colliderBytes = (long)colliderCount * BytesPerCollider;

            // ---- BVH section ----
            report.bvhBytes = EstimateBvhSize(scene.bvh);

            // ---- Interactable section ----
            report.interactableBytes = (long)scene.interactables.Length * BytesPerInteractable;

            // ---- Nav region data ----
            report.navRegionBytes = EstimateNavRegionSize(scene.navRegionBuilder);

            // ---- Room/portal data ----
            report.roomPortalBytes = EstimateRoomPortalSize(scene.roomBuilder);

            // ---- Atlas metadata ----
            report.atlasMetadataBytes = (long)scene.atlases.Length * BytesPerAtlasMetadata;

            // ---- CLUT metadata ----
            report.clutMetadataBytes = (long)clutCount * BytesPerClutMetadata;

            // ---- Mesh data ----
            report.meshDataBytes = (long)totalTriangles * BytesPerTriangle;

            // ---- Atlas pixel data ----
            long atlasPixelBytes = 0;
            foreach (var atlas in scene.atlases)
                atlasPixelBytes += Align4((long)atlas.Width * TextureAtlas.Height * 2);
            report.atlasPixelBytes = atlasPixelBytes;

            // ---- CLUT data ----
            long clutDataBytes = 0;
            foreach (var atlas in scene.atlases)
                foreach (var tex in atlas.ContainedTextures)
                    if (tex.ColorPalette != null)
                        clutDataBytes += Align4((long)tex.ColorPalette.Count * 2);
            report.clutDataBytes = clutDataBytes;

            // ---- Name table ----
            long nameTableBytes = 0;
            foreach (var e in scene.exporters)
            {
                string name = e.gameObject.name;
                if (name.Length > 24) name = name.Substring(0, 24);
                nameTableBytes += 1 + Encoding.UTF8.GetByteCount(name) + 1; // length byte + name + null
            }
            report.nameTableBytes = nameTableBytes;

            // ---- Audio metadata + data ----
            int audioClipCount = scene.audioClips?.Length ?? 0;
            report.audioMetadataBytes = (long)audioClipCount * BytesPerAudioMetadata;

            long audioDataBytes = 0;
            long spuAudioBytes = 0;
            if (scene.audioClips != null)
            {
                foreach (var clip in scene.audioClips)
                {
                    if (clip.adpcmData != null)
                    {
                        audioDataBytes += Align4(clip.adpcmData.Length);
                        spuAudioBytes += clip.adpcmData.Length;
                    }
                    // clip name data
                    string name = clip.clipName ?? "";
                    audioDataBytes += name.Length + 1; // name + null
                }
            }
            report.audioDataBytes = audioDataBytes;
            report.spuAudioBytes = spuAudioBytes;

            // ---- VRAM breakdown ----
            int fbCount = dualBuffering ? 2 : 1;
            report.framebufferVramBytes = (long)resolutionWidth * resolutionHeight * 2 * fbCount;
            report.textureVramBytes = atlasPixelBytes; // same data, uploaded to VRAM
            report.clutVramBytes = clutDataBytes;

            // ---- Renderer overhead (double-buffered OTs + bump allocators) ----
            long otBytes = 2L * OrderingTableSize * 4; // two OTs, each entry is a pointer (4 bytes)
            long bumpBytes = 2L * BumpAllocatorSize;
            report.rendererOverhead = otBytes + bumpBytes;

            // ---- Skinned mesh data (v18) ----
            report.skinnedMeshBytes = PSXSkinnedMeshExporter.EstimateSkinDataBytes(scene.bakedSkinData);
            report.skinnedMeshCount = scene.bakedSkinData?.Length ?? 0;

            // ---- Executable size ----
            report.executableBytes = compiledExeBytes > 0 ? compiledExeBytes : 150 * 1024; // estimate if unknown

            return report;
        }

        /// <summary>
        /// Overload that reads the compiled .ps-exe file size if available.
        /// </summary>
        public static SceneMemoryReport Analyze(
            in PSXSceneWriter.SceneData scene,
            string sceneName,
            int resolutionWidth,
            int resolutionHeight,
            bool dualBuffering,
            string compiledExePath)
        {
            long exeBytes = 0;
            if (!string.IsNullOrEmpty(compiledExePath) && System.IO.File.Exists(compiledExePath))
                exeBytes = new System.IO.FileInfo(compiledExePath).Length;
            return Analyze(in scene, sceneName, resolutionWidth, resolutionHeight, dualBuffering, exeBytes);
        }

        private static long Align4(long size)
        {
            return (size + 3) & ~3L;
        }

        private static long EstimateBvhSize(BVH bvh)
        {
            if (bvh == null) return 0;
            // BVH nodes: each node has AABB (24 bytes) + child/tri info (8 bytes) = 32 bytes
            // Triangle refs: 4 bytes each (uint32)
            return (long)bvh.NodeCount * 32 + (long)bvh.TriangleRefCount * 4;
        }

        private static long EstimateCollisionSize(PSXCollisionExporter collision)
        {
            if (collision == null || collision.MeshCount == 0) return 0;
            // Each collision mesh header: AABB (24) + tri count (2) + flags (2) + offset (4) = 32 bytes
            // Each collision triangle: 3 verts * 12 bytes + normal 12 bytes + flags 4 bytes = 52 bytes
            return (long)collision.MeshCount * 32 + (long)collision.TriangleCount * 52;
        }

        private static long EstimateNavRegionSize(PSXNavRegionBuilder nav)
        {
            if (nav == null || nav.RegionCount == 0) return 0;
            // Region: 84 bytes each (header + vertex data)
            // Portal: 20 bytes each
            return (long)nav.RegionCount * 84 + (long)nav.PortalCount * 20;
        }

        private static long EstimateRoomPortalSize(PSXRoomBuilder rooms)
        {
            if (rooms == null || rooms.RoomCount == 0) return 0;
            // Room data: AABB (24 bytes) + tri ref range (4 bytes) + portal range (4 bytes) = 32 bytes per room
            // Portal data: 40 bytes each (with right/up vectors)
            // Tri refs: 4 bytes each (uint32)
            int roomCount = rooms.RoomCount + 1; // +1 for catch-all room
            return (long)roomCount * 32 + (long)rooms.PortalCount * 40 + (long)rooms.TotalTriRefCount * 4;
        }
    }
}
