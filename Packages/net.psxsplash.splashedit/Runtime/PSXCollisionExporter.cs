using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Surface flags for collision triangles — must match C++ SurfaceFlag enum
    /// </summary>
    [Flags]
    public enum PSXSurfaceFlag : byte
    {
        Solid   = 0x01,
        Slope   = 0x02,
        Stairs  = 0x04,
        NoWalk  = 0x10,
    }

    /// <summary>
    /// Exports scene collision geometry as a flat world-space triangle soup
    /// with per-triangle surface flags and world-space AABBs.
    /// 
    /// Binary layout (matches C++ structs):
    ///   CollisionDataHeader (20 bytes)
    ///   CollisionMeshHeader[meshCount] (32 bytes each)
    ///   CollisionTri[triangleCount]    (52 bytes each)
    ///   CollisionChunk[chunkW*chunkH]  (4 bytes each, exterior only)
    /// </summary>
    public class PSXCollisionExporter : IPSXBinaryWritable
    {
        // Configurable
        public float WalkableSlopeAngle = 46.0f;  // Degrees; steeper = wall

        // Build results
        private List<CollisionMesh> _meshes = new List<CollisionMesh>();
        private List<CollisionTriExport> _allTriangles = new List<CollisionTriExport>();
        private CollisionChunkExport[,] _chunks;
        private Vector3 _chunkOrigin;
        private float _chunkSize;
        private int _chunkGridW, _chunkGridH;

        public int MeshCount => _meshes.Count;
        public int TriangleCount => _allTriangles.Count;

        // Internal types
        private class CollisionMesh
        {
            public Bounds worldAABB;
            public int firstTriangle;
            public int triangleCount;
            public byte roomIndex;
        }

        private struct CollisionTriExport
        {
            public Vector3 v0, e1, e2, normal;
            public byte flags;
            public byte roomIndex;
        }

        private struct CollisionChunkExport
        {
            public int firstMeshIndex;
            public int meshCount;
        }

        /// <summary>
        /// Build collision data from scene exporters.
        /// When autoIncludeSolid is true, objects with CollisionType=None are
        /// automatically treated as Solid. This ensures all scene geometry
        /// blocks the player without requiring manual flagging.
        /// </summary>
        public void Build(PSXObjectExporter[] exporters, float gteScaling,
                          bool autoIncludeSolid = true)
        {
            _meshes.Clear();
            _allTriangles.Clear();

            float cosWalkable = Mathf.Cos(WalkableSlopeAngle * Mathf.Deg2Rad);
            int autoIncluded = 0;

            foreach (var exporter in exporters)
            {
                // Dynamic objects use runtime AABB colliders, skip them
                if (exporter.CollisionType == PSXCollisionType.Dynamic)
                    continue;

                PSXCollisionType effectiveType = exporter.CollisionType;

                if (effectiveType == PSXCollisionType.None)
                {
                    if (autoIncludeSolid)
                    {
                        effectiveType = PSXCollisionType.Static;
                        autoIncluded++;
                    }
                    else
                    {
                        continue;
                    }
                }

                MeshFilter mf = exporter.GetComponent<MeshFilter>();
                Mesh collisionMesh = mf?.sharedMesh;

                if (collisionMesh == null)
                    continue;

                Matrix4x4 worldMatrix = exporter.transform.localToWorldMatrix;
                Vector3[] vertices = collisionMesh.vertices;
                int[] indices = collisionMesh.triangles;

                int firstTri = _allTriangles.Count;
                Bounds meshBoundsWorld = new Bounds();
                bool boundsInit = false;

                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 v0 = worldMatrix.MultiplyPoint3x4(vertices[indices[i]]);
                    Vector3 v1 = worldMatrix.MultiplyPoint3x4(vertices[indices[i + 1]]);
                    Vector3 v2 = worldMatrix.MultiplyPoint3x4(vertices[indices[i + 2]]);

                    Vector3 edge1 = v1 - v0;
                    Vector3 edge2 = v2 - v0;
                    Vector3 normal = Vector3.Cross(edge1, edge2).normalized;

                    // Determine surface flags
                    byte flags = 0;

                    // Floor-like: normal.y > cosWalkable
                    float dotUp = normal.y;

                    if (dotUp > cosWalkable)
                    {
                        flags = (byte)PSXSurfaceFlag.Solid;

                        if (dotUp < 0.95f && dotUp > cosWalkable)
                        {
                            flags |= (byte)PSXSurfaceFlag.Stairs;
                        }
                    }
                    else if (dotUp > 0.0f)
                    {
                        flags = (byte)(PSXSurfaceFlag.Solid | PSXSurfaceFlag.Slope);
                    }
                    else
                    {
                        flags = (byte)PSXSurfaceFlag.Solid;
                    }

                    _allTriangles.Add(new CollisionTriExport
                    {
                        v0 = v0,
                        e1 = edge1,
                        e2 = edge2,
                        normal = normal,
                        flags = flags,
                        roomIndex = 0xFF,
                    });

                    // Update world bounds
                    if (!boundsInit)
                    {
                        meshBoundsWorld = new Bounds(v0, Vector3.zero);
                        boundsInit = true;
                    }
                    meshBoundsWorld.Encapsulate(v0);
                    meshBoundsWorld.Encapsulate(v1);
                    meshBoundsWorld.Encapsulate(v2);
                }

                int triCount = _allTriangles.Count - firstTri;
                if (triCount > 0)
                {
                    _meshes.Add(new CollisionMesh
                    {
                        worldAABB = meshBoundsWorld,
                        firstTriangle = firstTri,
                        triangleCount = triCount,
                        roomIndex = 0xFF,
                    });
                }
            }

            // Build spatial grid
            if (_meshes.Count > 0)
            {
                BuildSpatialGrid(gteScaling);
            }
            else
            {
                _chunkGridW = 0;
                _chunkGridH = 0;
            }
        }

        private void BuildSpatialGrid(float gteScaling)
        {
            // Compute world bounds of all collision
            Bounds allBounds = _meshes[0].worldAABB;
            foreach (var mesh in _meshes)
                allBounds.Encapsulate(mesh.worldAABB);

            // Grid cell size: ~4 GTE units in world space
            _chunkSize = 4.0f * gteScaling;
            _chunkOrigin = new Vector3(allBounds.min.x, 0, allBounds.min.z);

            _chunkGridW = Mathf.CeilToInt((allBounds.max.x - allBounds.min.x) / _chunkSize);
            _chunkGridH = Mathf.CeilToInt((allBounds.max.z - allBounds.min.z) / _chunkSize);

            // Clamp to reasonable limits
            _chunkGridW = Mathf.Clamp(_chunkGridW, 1, 64);
            _chunkGridH = Mathf.Clamp(_chunkGridH, 1, 64);

            // For each chunk, find which meshes overlap it
            // We store mesh indices sorted per chunk
            var chunkMeshLists = new List<int>[_chunkGridW, _chunkGridH];
            for (int z = 0; z < _chunkGridH; z++)
                for (int x = 0; x < _chunkGridW; x++)
                    chunkMeshLists[x, z] = new List<int>();

            for (int mi = 0; mi < _meshes.Count; mi++)
            {
                var mesh = _meshes[mi];
                int minCX = Mathf.FloorToInt((mesh.worldAABB.min.x - _chunkOrigin.x) / _chunkSize);
                int maxCX = Mathf.FloorToInt((mesh.worldAABB.max.x - _chunkOrigin.x) / _chunkSize);
                int minCZ = Mathf.FloorToInt((mesh.worldAABB.min.z - _chunkOrigin.z) / _chunkSize);
                int maxCZ = Mathf.FloorToInt((mesh.worldAABB.max.z - _chunkOrigin.z) / _chunkSize);

                minCX = Mathf.Clamp(minCX, 0, _chunkGridW - 1);
                maxCX = Mathf.Clamp(maxCX, 0, _chunkGridW - 1);
                minCZ = Mathf.Clamp(minCZ, 0, _chunkGridH - 1);
                maxCZ = Mathf.Clamp(maxCZ, 0, _chunkGridH - 1);

                for (int cz = minCZ; cz <= maxCZ; cz++)
                    for (int cx = minCX; cx <= maxCX; cx++)
                        chunkMeshLists[cx, cz].Add(mi);
            }

            // Flatten into contiguous array (mesh indices already in order)
            // We'll write chunks as (firstMeshIndex, meshCount) referencing the mesh header array
            _chunks = new CollisionChunkExport[_chunkGridW, _chunkGridH];
            for (int z = 0; z < _chunkGridH; z++)
            {
                for (int x = 0; x < _chunkGridW; x++)
                {
                    var list = chunkMeshLists[x, z];
                    _chunks[x, z] = new CollisionChunkExport
                    {
                        firstMeshIndex = list.Count > 0 ? list[0] : 0,
                        meshCount = list.Count,
                    };
                }
            }
        }

        /// <summary>
        /// Write collision data to binary.
        /// All coordinates converted to PS1 20.12 fixed-point with Y flip.
        /// </summary>
        public void WriteToBinary(BinaryWriter writer, float gteScaling)
        {
            // Header (20 bytes)
            writer.Write((ushort)_meshes.Count);
            writer.Write((ushort)_allTriangles.Count);
            writer.Write((ushort)_chunkGridW);
            writer.Write((ushort)_chunkGridH);
            writer.Write(PSXTrig.ConvertWorldToFixed12(_chunkOrigin.x / gteScaling));
            writer.Write(PSXTrig.ConvertWorldToFixed12(_chunkOrigin.z / gteScaling));
            writer.Write(PSXTrig.ConvertWorldToFixed12(_chunkSize / gteScaling));

            // Mesh headers (32 bytes each)
            foreach (var mesh in _meshes)
            {
                writer.Write(PSXTrig.ConvertWorldToFixed12(mesh.worldAABB.min.x / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(-mesh.worldAABB.max.y / gteScaling));  // Y flip
                writer.Write(PSXTrig.ConvertWorldToFixed12(mesh.worldAABB.min.z / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(mesh.worldAABB.max.x / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(-mesh.worldAABB.min.y / gteScaling));  // Y flip
                writer.Write(PSXTrig.ConvertWorldToFixed12(mesh.worldAABB.max.z / gteScaling));
                writer.Write((ushort)mesh.firstTriangle);
                writer.Write((ushort)mesh.triangleCount);
                writer.Write(mesh.roomIndex);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)0);
            }

            // Triangles (52 bytes each)
            foreach (var tri in _allTriangles)
            {
                // v0
                writer.Write(PSXTrig.ConvertWorldToFixed12(tri.v0.x / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(-tri.v0.y / gteScaling));  // Y flip
                writer.Write(PSXTrig.ConvertWorldToFixed12(tri.v0.z / gteScaling));
                // edge1
                writer.Write(PSXTrig.ConvertWorldToFixed12(tri.e1.x / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(-tri.e1.y / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(tri.e1.z / gteScaling));
                // edge2
                writer.Write(PSXTrig.ConvertWorldToFixed12(tri.e2.x / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(-tri.e2.y / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(tri.e2.z / gteScaling));
                // normal (in PS1 space: Y negated)
                writer.Write(PSXTrig.ConvertWorldToFixed12(tri.normal.x));
                writer.Write(PSXTrig.ConvertWorldToFixed12(-tri.normal.y));
                writer.Write(PSXTrig.ConvertWorldToFixed12(tri.normal.z));
                // flags
                writer.Write(tri.flags);
                writer.Write(tri.roomIndex);
                writer.Write((ushort)0); // pad
            }

            // Spatial grid chunks (4 bytes each, exterior only)
            if (_chunkGridW > 0 && _chunkGridH > 0)
            {
                for (int z = 0; z < _chunkGridH; z++)
                {
                    for (int x = 0; x < _chunkGridW; x++)
                    {
                        writer.Write((ushort)_chunks[x, z].firstMeshIndex);
                        writer.Write((ushort)_chunks[x, z].meshCount);
                    }
                }
            }
        }

        /// <summary>
        /// Get total bytes that will be written.
        /// </summary>
        public int GetBinarySize()
        {
            int size = 20; // header
            size += _meshes.Count * 32;
            size += _allTriangles.Count * 52;
            if (_chunkGridW > 0 && _chunkGridH > 0)
                size += _chunkGridW * _chunkGridH * 4;
            return size;
        }
    }
}
