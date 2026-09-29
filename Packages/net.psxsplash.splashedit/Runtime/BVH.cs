using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using SplashEdit.RuntimeCode;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Bounding Volume Hierarchy for PS1 frustum culling.
    /// Unlike BSP, BVH doesn't split triangles - it groups them by spatial locality.
    /// This is better for PS1 because:
    /// 1. No additional triangles created (memory constrained)
    /// 2. Simple AABB tests are fast on 33MHz CPU
    /// 3. Natural hierarchical culling
    /// </summary>
    public class BVH : IPSXBinaryWritable
    {
        // Configuration
        private const int MAX_TRIANGLES_PER_LEAF = 64;  // PS1 can handle batches of this size
        private const int MAX_DEPTH = 16;               // Prevent pathological cases
        private const int MIN_TRIANGLES_TO_SPLIT = 8;   // Don't split tiny groups
        
        private List<PSXObjectExporter> _objects;
        private BVHNode _root;
        private List<BVHNode> _allNodes;  // Flat list for export
        private List<TriangleRef> _allTriangleRefs;  // Triangle references for export
        
        public int NodeCount => _allNodes?.Count ?? 0;
        public int TriangleRefCount => _allTriangleRefs?.Count ?? 0;
        
        /// <summary>
        /// Reference to a triangle - doesn't copy data, just points to it
        /// </summary>
        public struct TriangleRef
        {
            public ushort objectIndex;      // Which GameObject
            public ushort triangleIndex;    // Which triangle in that object's mesh
            
            public TriangleRef(int objIdx, int triIdx)
            {
                objectIndex = (ushort)objIdx;
                triangleIndex = (ushort)triIdx;
            }
        }
        
        /// <summary>
        /// BVH Node - 32 bytes when exported
        /// </summary>
        public class BVHNode
        {
            public Bounds bounds;
            public BVHNode left;
            public BVHNode right;
            public List<TriangleRef> triangles;  // Only for leaf nodes
            public int depth;
            
            // Export indices (filled during serialization)
            public int nodeIndex = -1;
            public int leftIndex = -1;   // -1 = no child (leaf check)
            public int rightIndex = -1;
            public int firstTriangleIndex = -1;
            public int triangleCount = 0;
            
            public bool IsLeaf => left == null && right == null;
        }
        
        /// <summary>
        /// Triangle with bounds for building
        /// </summary>
        private struct TriangleWithBounds
        {
            public TriangleRef reference;
            public Bounds bounds;
            public Vector3 centroid;
        }
        
        public BVH(List<PSXObjectExporter> objects)
        {
            _objects = objects;
            _allNodes = new List<BVHNode>();
            _allTriangleRefs = new List<TriangleRef>();
        }
        
        public void Build()
        {
            _allNodes.Clear();
            _allTriangleRefs.Clear();
            
            // Extract all triangles with their bounds
            List<TriangleWithBounds> triangles = ExtractTriangles();
            
            if (triangles.Count == 0)
            {
                Debug.LogWarning("BVH: No triangles to process");
                return;
            }
            
            // Build the tree
            _root = BuildNode(triangles, 0);
            
            // Flatten for export
            FlattenTree();
        }
        
        private List<TriangleWithBounds> ExtractTriangles()
        {
            var result = new List<TriangleWithBounds>();
            
            for (int objIdx = 0; objIdx < _objects.Count; objIdx++)
            {
                var exporter = _objects[objIdx];
                if (!exporter.IsActive) continue;
                
                MeshFilter mf = exporter.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                
                Mesh mesh = mf.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                int[] indices = mesh.triangles;
                Matrix4x4 worldMatrix = exporter.transform.localToWorldMatrix;
                
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 v0 = worldMatrix.MultiplyPoint3x4(vertices[indices[i]]);
                    Vector3 v1 = worldMatrix.MultiplyPoint3x4(vertices[indices[i + 1]]);
                    Vector3 v2 = worldMatrix.MultiplyPoint3x4(vertices[indices[i + 2]]);
                    
                    // Calculate bounds
                    Bounds triBounds = new Bounds(v0, Vector3.zero);
                    triBounds.Encapsulate(v1);
                    triBounds.Encapsulate(v2);
                    
                    result.Add(new TriangleWithBounds
                    {
                        reference = new TriangleRef(objIdx, i / 3),
                        bounds = triBounds,
                        centroid = (v0 + v1 + v2) / 3f
                    });
                }
            }
            
            return result;
        }
        
        private const int SAH_BIN_COUNT = 8;  // Number of bins for SAH evaluation
        private const float TRAVERSAL_COST = 1.0f;  // Relative cost of a BVH node traversal
        private const float INTERSECT_COST = 1.0f;  // Relative cost of triangle intersection

        /// <summary>
        /// Compute the half-surface-area of an AABB (proportional to full surface area).
        /// </summary>
        private static float HalfSurfaceArea(Bounds b)
        {
            Vector3 s = b.size;
            return s.x * s.y + s.y * s.z + s.z * s.x;
        }

        private BVHNode BuildNode(List<TriangleWithBounds> triangles, int depth)
        {
            if (triangles.Count == 0)
                return null;
            
            var node = new BVHNode { depth = depth };
            
            // Calculate bounds encompassing all triangles
            node.bounds = triangles[0].bounds;
            foreach (var tri in triangles)
            {
                node.bounds.Encapsulate(tri.bounds);
            }
            
            // Create leaf if conditions met
            if (triangles.Count <= MAX_TRIANGLES_PER_LEAF || 
                depth >= MAX_DEPTH ||
                triangles.Count < MIN_TRIANGLES_TO_SPLIT)
            {
                node.triangles = triangles.Select(t => t.reference).ToList();
                return node;
            }

            float parentArea = HalfSurfaceArea(node.bounds);
            if (parentArea <= 0f)
            {
                // Degenerate AABB (zero volume), make a leaf
                node.triangles = triangles.Select(t => t.reference).ToList();
                return node;
            }
            
            // Compute centroid bounds to determine bin ranges
            Bounds centroidBounds = new Bounds(triangles[0].centroid, Vector3.zero);
            for (int i = 1; i < triangles.Count; i++)
                centroidBounds.Encapsulate(triangles[i].centroid);

            float bestCost = float.MaxValue;
            int bestAxis = -1;
            int bestSplit = -1; // split after bin index bestSplit (left = bins [0..bestSplit], right = [bestSplit+1..N-1])

            // Evaluate all 3 axes with binned SAH
            for (int axis = 0; axis < 3; axis++)
            {
                float cMin = (axis == 0) ? centroidBounds.min.x : (axis == 1) ? centroidBounds.min.y : centroidBounds.min.z;
                float cMax = (axis == 0) ? centroidBounds.max.x : (axis == 1) ? centroidBounds.max.y : centroidBounds.max.z;
                float range = cMax - cMin;
                if (range <= 1e-6f) continue; // No spread on this axis

                // Initialize bins
                int[] binCount = new int[SAH_BIN_COUNT];
                Bounds[] binBounds = new Bounds[SAH_BIN_COUNT];
                for (int b = 0; b < SAH_BIN_COUNT; b++)
                {
                    binCount[b] = 0;
                    binBounds[b] = new Bounds(Vector3.zero, Vector3.zero);
                }
                bool[] binInitialized = new bool[SAH_BIN_COUNT];

                float invRange = (SAH_BIN_COUNT) / range;

                // Assign triangles to bins by centroid
                foreach (var tri in triangles)
                {
                    float c = (axis == 0) ? tri.centroid.x : (axis == 1) ? tri.centroid.y : tri.centroid.z;
                    int bin = Mathf.Clamp((int)((c - cMin) * invRange), 0, SAH_BIN_COUNT - 1);
                    binCount[bin]++;
                    if (!binInitialized[bin])
                    {
                        binBounds[bin] = tri.bounds;
                        binInitialized[bin] = true;
                    }
                    else
                    {
                        binBounds[bin].Encapsulate(tri.bounds);
                    }
                }

                // Sweep from left to compute prefix counts and bounds
                int[] leftCount = new int[SAH_BIN_COUNT - 1];
                float[] leftArea = new float[SAH_BIN_COUNT - 1];
                {
                    int runCount = 0;
                    Bounds runBounds = new Bounds();
                    bool runInit = false;
                    for (int s = 0; s < SAH_BIN_COUNT - 1; s++)
                    {
                        runCount += binCount[s];
                        if (binCount[s] > 0)
                        {
                            if (!runInit) { runBounds = binBounds[s]; runInit = true; }
                            else runBounds.Encapsulate(binBounds[s]);
                        }
                        leftCount[s] = runCount;
                        leftArea[s] = runInit ? HalfSurfaceArea(runBounds) : 0f;
                    }
                }

                // Sweep from right
                int[] rightCount = new int[SAH_BIN_COUNT - 1];
                float[] rightArea = new float[SAH_BIN_COUNT - 1];
                {
                    int runCount = 0;
                    Bounds runBounds = new Bounds();
                    bool runInit = false;
                    for (int s = SAH_BIN_COUNT - 2; s >= 0; s--)
                    {
                        runCount += binCount[s + 1];
                        if (binCount[s + 1] > 0)
                        {
                            if (!runInit) { runBounds = binBounds[s + 1]; runInit = true; }
                            else runBounds.Encapsulate(binBounds[s + 1]);
                        }
                        rightCount[s] = runCount;
                        rightArea[s] = runInit ? HalfSurfaceArea(runBounds) : 0f;
                    }
                }

                // Find best split for this axis
                for (int s = 0; s < SAH_BIN_COUNT - 1; s++)
                {
                    if (leftCount[s] == 0 || rightCount[s] == 0) continue;
                    float cost = TRAVERSAL_COST +
                        (leftCount[s] * leftArea[s] + rightCount[s] * rightArea[s]) * INTERSECT_COST / parentArea;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestAxis = axis;
                        bestSplit = s;
                    }
                }
            }

            // Compare SAH cost to leaf cost
            float leafCost = triangles.Count * INTERSECT_COST;
            if (bestAxis < 0 || bestCost >= leafCost)
            {
                // No beneficial split found — make a leaf
                node.triangles = triangles.Select(t => t.reference).ToList();
                return node;
            }

            // Partition triangles according to best split
            float splitMin = (bestAxis == 0) ? centroidBounds.min.x : (bestAxis == 1) ? centroidBounds.min.y : centroidBounds.min.z;
            float splitMax = (bestAxis == 0) ? centroidBounds.max.x : (bestAxis == 1) ? centroidBounds.max.y : centroidBounds.max.z;
            float splitRange = splitMax - splitMin;
            float splitInvRange = SAH_BIN_COUNT / splitRange;

            var leftTris = new List<TriangleWithBounds>();
            var rightTris = new List<TriangleWithBounds>();
            foreach (var tri in triangles)
            {
                float c = (bestAxis == 0) ? tri.centroid.x : (bestAxis == 1) ? tri.centroid.y : tri.centroid.z;
                int bin = Mathf.Clamp((int)((c - splitMin) * splitInvRange), 0, SAH_BIN_COUNT - 1);
                if (bin <= bestSplit)
                    leftTris.Add(tri);
                else
                    rightTris.Add(tri);
            }
            
            // Safety: if partition failed (all on one side), fall back to leaf
            if (leftTris.Count == 0 || rightTris.Count == 0)
            {
                node.triangles = triangles.Select(t => t.reference).ToList();
                return node;
            }
            
            node.left = BuildNode(leftTris, depth + 1);
            node.right = BuildNode(rightTris, depth + 1);
            
            return node;
        }
        
        /// <summary>
        /// Flatten tree to arrays for export
        /// </summary>
        private void FlattenTree()
        {
            _allNodes.Clear();
            _allTriangleRefs.Clear();
            
            if (_root == null) return;
            
            // BFS to assign indices
            var queue = new Queue<BVHNode>();
            queue.Enqueue(_root);
            
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                node.nodeIndex = _allNodes.Count;
                _allNodes.Add(node);
                
                if (node.left != null) queue.Enqueue(node.left);
                if (node.right != null) queue.Enqueue(node.right);
            }
            
            // Second pass: fill in child indices and triangle data
            foreach (var node in _allNodes)
            {
                if (node.left != null)
                    node.leftIndex = node.left.nodeIndex;
                if (node.right != null)
                    node.rightIndex = node.right.nodeIndex;
                
                if (node.IsLeaf && node.triangles != null && node.triangles.Count > 0)
                {
                    // Sort tri-refs by objectIndex within each leaf so the C++ renderer
                    // can batch consecutive refs and avoid redundant GTE matrix reloads.
                    node.triangles.Sort((a, b) => a.objectIndex.CompareTo(b.objectIndex));
                    node.firstTriangleIndex = _allTriangleRefs.Count;
                    node.triangleCount = node.triangles.Count;
                    _allTriangleRefs.AddRange(node.triangles);
                }
            }
        }
        
        /// <summary>
        /// Export BVH to binary writer
        /// Format:
        /// - uint16 nodeCount
        /// - uint16 triangleRefCount
        /// - BVHNode[nodeCount] (32 bytes each)
        /// - TriangleRef[triangleRefCount] (4 bytes each)
        /// </summary>
        public void WriteToBinary(BinaryWriter writer, float gteScaling)
        {
            // Note: counts are already in the file header (bvhNodeCount, bvhTriangleRefCount)
            // Don't write them again here - C++ reads BVH data directly after colliders
            
            // Write nodes (32 bytes each)
            foreach (var node in _allNodes)
            {
                // AABB bounds (24 bytes)
                Vector3 min = node.bounds.min;
                Vector3 max = node.bounds.max;
                
                writer.Write(PSXTrig.ConvertWorldToFixed12(min.x / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(-max.y / gteScaling)); // Y flipped 
                writer.Write(PSXTrig.ConvertWorldToFixed12(min.z / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(max.x / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(-min.y / gteScaling)); // Y flipped
                writer.Write(PSXTrig.ConvertWorldToFixed12(max.z / gteScaling));
                
                // Child indices (4 bytes) - 0xFFFF means no child
                writer.Write((ushort)(node.leftIndex >= 0 ? node.leftIndex : 0xFFFF));
                writer.Write((ushort)(node.rightIndex >= 0 ? node.rightIndex : 0xFFFF));
                
                // Triangle data (4 bytes)
                writer.Write((ushort)(node.firstTriangleIndex >= 0 ? node.firstTriangleIndex : 0));
                writer.Write((ushort)node.triangleCount);
            }
            
            // Write triangle references (4 bytes each)
            foreach (var triRef in _allTriangleRefs)
            {
                writer.Write(triRef.objectIndex);
                writer.Write(triRef.triangleIndex);
            }
        }
        
        /// <summary>
        /// Get total bytes that will be written
        /// </summary>
        public int GetBinarySize()
        {
            // Just nodes + triangle refs, counts are in file header
            return (_allNodes.Count * 32) + (_allTriangleRefs.Count * 4);
        }
        
        /// <summary>
        /// Draw gizmos for debugging
        /// </summary>
        public void DrawGizmos(int maxDepth = 999)
        {
            if (_root == null) return;
            DrawNodeGizmos(_root, maxDepth);
        }
        
        private void DrawNodeGizmos(BVHNode node, int maxDepth)
        {
            if (node == null || node.depth > maxDepth) return;
            
            // Color by depth
            Color c = Color.HSVToRGB((node.depth * 0.12f) % 1f, 0.7f, 0.9f);
            c.a = node.IsLeaf ? 0.3f : 0.1f;
            Gizmos.color = c;
            
            // Draw bounds
            Gizmos.DrawWireCube(node.bounds.center, node.bounds.size);
            
            if (node.IsLeaf)
            {
                // Draw leaf as semi-transparent
                Gizmos.color = new Color(c.r, c.g, c.b, 0.1f);
                Gizmos.DrawCube(node.bounds.center, node.bounds.size);
            }
            
            // Recurse
            DrawNodeGizmos(node.left, maxDepth);
            DrawNodeGizmos(node.right, maxDepth);
        }
        
        /// <summary>
        /// Get statistics for debugging
        /// </summary>
        public string GetStatistics()
        {
            if (_root == null) return "BVH not built";
            
            int leafCount = 0;
            int maxDepth = 0;
            int totalTris = 0;
            
            void CountNodes(BVHNode node)
            {
                if (node == null) return;
                if (node.depth > maxDepth) maxDepth = node.depth;
                if (node.IsLeaf)
                {
                    leafCount++;
                    totalTris += node.triangleCount;
                }
                CountNodes(node.left);
                CountNodes(node.right);
            }
            
            CountNodes(_root);
            
            return $"Nodes: {_allNodes.Count}, Leaves: {leafCount}, Max Depth: {maxDepth}, Triangle Refs: {totalTris}";
        }
    }
}
