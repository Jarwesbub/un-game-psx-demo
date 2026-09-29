using System;
using System.Collections.Generic;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Defines a convex room volume for the portal/room occlusion system.
    /// Place one of these per room in the scene. Geometry is assigned to rooms by
    /// centroid containment during export. Portals between adjacent rooms are detected
    /// automatically.
    /// 
    /// This is independent of the navregion/portal system used for navigation.
    /// </summary>
    [ExecuteInEditMode]
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXRoom.png")]
    public class PSXRoom : MonoBehaviour
    {
        [Tooltip("Optional display name for this room (used in editor gizmos).")]
        public string RoomName = "";

        [Tooltip("Size of the room volume in local space. Defaults to the object's scale.")]
        public Vector3 VolumeSize = Vector3.one;

        [Tooltip("Offset of the volume center relative to the transform position.")]
        public Vector3 VolumeOffset = Vector3.zero;

        /// <summary>World-space AABB of this room.</summary>
        public Bounds GetWorldBounds()
        {
            Vector3 center = transform.TransformPoint(VolumeOffset);
            // Transform the 8 corners to get a world-space AABB
            Vector3 halfSize = VolumeSize * 0.5f;
            Vector3 wMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 wMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = VolumeOffset + new Vector3(
                    (i & 1) != 0 ? halfSize.x : -halfSize.x,
                    (i & 2) != 0 ? halfSize.y : -halfSize.y,
                    (i & 4) != 0 ? halfSize.z : -halfSize.z
                );
                Vector3 world = transform.TransformPoint(corner);
                wMin = Vector3.Min(wMin, world);
                wMax = Vector3.Max(wMax, world);
            }
            Bounds b = new Bounds();
            b.SetMinMax(wMin, wMax);
            return b;
        }

        /// <summary>Check if a world-space point is inside this room volume.</summary>
        public bool ContainsPoint(Vector3 worldPoint)
        {
            return GetWorldBounds().Contains(worldPoint);
        }

        void OnDrawGizmos()
        {
            var exporter = FindFirstObjectByType<PSXSceneExporter>();
            if (exporter != null && !exporter.PreviewRoomsPortals) return;

            Gizmos.color = new Color(0.2f, 0.8f, 0.4f, 0.15f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(VolumeOffset, VolumeSize);
            Gizmos.color = new Color(0.2f, 0.8f, 0.4f, 0.6f);
            Gizmos.DrawWireCube(VolumeOffset, VolumeSize);
            Gizmos.matrix = Matrix4x4.identity;

#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(RoomName))
            {
                UnityEditor.Handles.Label(transform.TransformPoint(VolumeOffset),
                    RoomName, new GUIStyle { normal = { textColor = Color.green } });
            }
#endif
        }
    }

    /// <summary>
    /// Portal between two PSXRoom volumes, stored during export.
    /// Built from PSXPortalLink scene components.
    /// </summary>
    public struct PSXPortal
    {
        public int roomA;
        public int roomB;
        public Vector3 center;      // World-space portal center (from PSXPortalLink transform)
        public Vector2 portalSize;  // Portal opening size in world units (width, height)
        public Vector3 normal;      // Portal facing direction (from PSXPortalLink transform.forward)
        public Vector3 right;       // Portal local right axis (world space)
        public Vector3 up;          // Portal local up axis (world space)
    }

    /// <summary>
    /// Builds and exports the room/portal system for a scene.
    /// Called during PSXSceneExporter.Export().
    /// Portals are user-defined via PSXPortalLink components instead of auto-detected.
    /// </summary>
    public class PSXRoomBuilder
    {
        private const int CELLS_PER_AXIS = 2; // 2×2×2 = 8 cells per room

        private PSXRoom[] _rooms;
        private List<PSXPortal> _portals = new List<PSXPortal>();
        private List<BVH.TriangleRef>[] _roomTriRefs;
        private List<BVH.TriangleRef> _catchAllTriRefs = new List<BVH.TriangleRef>();

        // Per-room spatial cells for sub-room frustum culling
        private List<RoomCellData> _allCells = new List<RoomCellData>();
        // Per-room: firstCell index and cellCount
        private int[] _roomFirstCell;
        private int[] _roomCellCount;

        // Per-room portal reference lists (Phase 5)
        private struct PortalRefEntry
        {
            public ushort portalIndex; // index into _portals
            public ushort otherRoom;   // room on the other side
        }
        private List<PortalRefEntry> _allPortalRefs = new List<PortalRefEntry>();
        private int[] _roomFirstPortalRef;
        private int[] _roomPortalRefCount;

        /// <summary>Cell data generated during Build, before coordinate conversion.</summary>
        private struct RoomCellData
        {
            public Bounds bounds;               // tight AABB around cell's actual triangles (world space)
            public List<BVH.TriangleRef> triRefs; // triangles in this cell
        }

        public int RoomCount => _rooms?.Length ?? 0;
        public int PortalCount => _portals?.Count ?? 0;
        public int CellCount => _allCells?.Count ?? 0;
        public int PortalRefCount => _allPortalRefs?.Count ?? 0;
        public int TotalTriRefCount
        {
            get
            {
                int count = 0;
                if (_roomTriRefs != null)
                    foreach (var list in _roomTriRefs) count += list.Count;
                count += _catchAllTriRefs.Count;
                return count;
            }
        }

        /// <summary>
        /// Build the room system: assign triangles to rooms and read user-defined portals.
        /// </summary>
        /// <param name="rooms">All PSXRoom components in the scene.</param>
        /// <param name="portalLinks">All PSXPortalLink components (user-placed portals).</param>
        /// <param name="exporters">All object exporters (for triangle centroid testing).</param>
        /// <param name="gteScaling">GTE coordinate scaling factor.</param>
        public void Build(PSXRoom[] rooms, PSXPortalLink[] portalLinks,
                          PSXObjectExporter[] exporters, float gteScaling)
        {
            _rooms = rooms;
            if (rooms == null || rooms.Length == 0) return;

            _roomTriRefs = new List<BVH.TriangleRef>[rooms.Length];
            for (int i = 0; i < rooms.Length; i++)
                _roomTriRefs[i] = new List<BVH.TriangleRef>();
            _catchAllTriRefs.Clear();
            _portals.Clear();

            // Assign each triangle to a room by vertex majority containment.
            // For each triangle, test all 3 world-space vertices against each room's AABB
            // (expanded by a margin to catch boundary geometry). The room containing the
            // most vertices wins. Ties broken by centroid. This prevents boundary triangles
            // (doorway walls, floor edges) from being assigned to the wrong room.
            const float ROOM_MARGIN = 0.5f; // expand AABBs by this much for testing
            Bounds[] roomBounds = new Bounds[rooms.Length];
            for (int i = 0; i < rooms.Length; i++)
            {
                roomBounds[i] = rooms[i].GetWorldBounds();
                roomBounds[i].Expand(ROOM_MARGIN * 2f); // Expand in all directions
            }

            for (int objIdx = 0; objIdx < exporters.Length; objIdx++)
            {
                var exporter = exporters[objIdx];
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

                    // Test all 3 vertices against each room, pick room with most hits.
                    int bestRoom = -1;
                    int bestHits = 0;
                    float bestDist = float.MaxValue;
                    for (int r = 0; r < rooms.Length; r++)
                    {
                        int hits = 0;
                        if (roomBounds[r].Contains(v0)) hits++;
                        if (roomBounds[r].Contains(v1)) hits++;
                        if (roomBounds[r].Contains(v2)) hits++;

                        if (hits > bestHits)
                        {
                            bestHits = hits;
                            bestRoom = r;
                            bestDist = (roomBounds[r].center - (v0 + v1 + v2) / 3f).sqrMagnitude;
                        }
                        else if (hits == bestHits && hits > 0)
                        {
                            // Tie-break: pick room whose center is closest to centroid
                            float dist = (roomBounds[r].center - (v0 + v1 + v2) / 3f).sqrMagnitude;
                            if (dist < bestDist)
                            {
                                bestRoom = r;
                                bestDist = dist;
                            }
                        }
                    }

                    var triRef = new BVH.TriangleRef(objIdx, i / 3);
                    if (bestRoom >= 0)
                        _roomTriRefs[bestRoom].Add(triRef);
                    else
                        _catchAllTriRefs.Add(triRef);
                }
            }

            // Build portals from user-placed PSXPortalLink components.
            // (Must happen before boundary duplication so we know which rooms are adjacent.)
            BuildPortals(portalLinks);

            // Phase 3: Duplicate boundary triangles into both rooms at portal boundaries.
            // When a triangle has vertices in multiple rooms, it was assigned to only
            // the "best" room. For triangles near a portal, also add them to the adjacent
            // room so doorway/boundary geometry is visible from both sides.
            DuplicateBoundaryTriangles(exporters, roomBounds);

            // Sort each room's tri-refs by objectIndex for GTE matrix batching.
            for (int i = 0; i < _roomTriRefs.Length; i++)
                _roomTriRefs[i].Sort((a, b) => a.objectIndex.CompareTo(b.objectIndex));
            _catchAllTriRefs.Sort((a, b) => a.objectIndex.CompareTo(b.objectIndex));

            // Generate per-room spatial cells for sub-room frustum culling.
            // This subdivides room tri-ref lists into spatial cells and re-orders
            // the tri-refs so each cell's refs are contiguous. Must happen after
            // sorting since it replaces and re-sorts.
            GenerateCells(exporters);

            // Generate per-room portal reference lists (Phase 5).
            // Each room gets a list of {portalIndex, otherRoom} entries so the
            // runtime can iterate only the portals touching a given room.
            GeneratePortalRefs();

            Debug.Log($"PSXRoomBuilder: {rooms.Length} rooms, {_portals.Count} portals, " +
                      $"{TotalTriRefCount} tri-refs ({_catchAllTriRefs.Count} catch-all), " +
                      $"{_allCells.Count} cells, {_allPortalRefs.Count} portal-refs");
        }

        /// <summary>
        /// Subdivide each room's triangle list into a coarse 3D grid (CELLS_PER_AXIS³).
        /// Each cell gets its own tight AABB and contiguous tri-ref sublist.
        /// The room's tri-ref list is rewritten so cells point into it.
        /// </summary>
        private void GenerateCells(PSXObjectExporter[] exporters)
        {
            _allCells.Clear();
            int totalRooms = _rooms.Length + 1; // +1 for catch-all
            _roomFirstCell = new int[totalRooms];
            _roomCellCount = new int[totalRooms];

            // Process each real room + the catch-all as the last entry.
            for (int ri = 0; ri < totalRooms; ri++)
            {
                List<BVH.TriangleRef> triRefs = (ri < _rooms.Length)
                    ? _roomTriRefs[ri]
                    : _catchAllTriRefs;

                _roomFirstCell[ri] = _allCells.Count;

                if (triRefs.Count == 0)
                {
                    _roomCellCount[ri] = 0;
                    continue;
                }

                // Compute the room's actual AABB from its triangles' centroids/vertices.
                Bounds roomBounds;
                if (ri < _rooms.Length)
                    roomBounds = _rooms[ri].GetWorldBounds();
                else
                {
                    // Catch-all: compute from triangles
                    roomBounds = ComputeTriRefBounds(triRefs, exporters);
                }

                Vector3 bmin = roomBounds.min;
                Vector3 bsize = roomBounds.size;
                // Prevent degenerate axes (e.g. flat room)
                if (bsize.x < 0.001f) bsize.x = 0.001f;
                if (bsize.y < 0.001f) bsize.y = 0.001f;
                if (bsize.z < 0.001f) bsize.z = 0.001f;

                int N = CELLS_PER_AXIS;
                int totalCells = N * N * N;

                // Assign each tri-ref to a cell based on centroid
                var cellTriRefs = new List<BVH.TriangleRef>[totalCells];
                var cellBounds = new Bounds?[totalCells];
                for (int c = 0; c < totalCells; c++)
                    cellTriRefs[c] = new List<BVH.TriangleRef>();

                foreach (var triRef in triRefs)
                {
                    if (triRef.objectIndex >= exporters.Length) continue;
                    var exporter = exporters[triRef.objectIndex];
                    MeshFilter mf = exporter.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    Mesh mesh = mf.sharedMesh;
                    Vector3[] vertices = mesh.vertices;
                    int[] indices = mesh.triangles;
                    Matrix4x4 worldMatrix = exporter.transform.localToWorldMatrix;

                    int triStart = triRef.triangleIndex * 3;
                    if (triStart + 2 >= indices.Length) continue;
                    Vector3 v0 = worldMatrix.MultiplyPoint3x4(vertices[indices[triStart]]);
                    Vector3 v1 = worldMatrix.MultiplyPoint3x4(vertices[indices[triStart + 1]]);
                    Vector3 v2 = worldMatrix.MultiplyPoint3x4(vertices[indices[triStart + 2]]);
                    Vector3 centroid = (v0 + v1 + v2) / 3f;

                    // Map centroid to cell index
                    int cx = Mathf.Clamp(Mathf.FloorToInt((centroid.x - bmin.x) / bsize.x * N), 0, N - 1);
                    int cy = Mathf.Clamp(Mathf.FloorToInt((centroid.y - bmin.y) / bsize.y * N), 0, N - 1);
                    int cz = Mathf.Clamp(Mathf.FloorToInt((centroid.z - bmin.z) / bsize.z * N), 0, N - 1);
                    int cellIdx = cx + cy * N + cz * N * N;

                    cellTriRefs[cellIdx].Add(triRef);

                    // Expand cell's tight bounds to include all vertices (not just centroid)
                    Bounds triBounds = new Bounds(v0, Vector3.zero);
                    triBounds.Encapsulate(v1);
                    triBounds.Encapsulate(v2);
                    if (cellBounds[cellIdx].HasValue)
                    {
                        var cb = cellBounds[cellIdx].Value;
                        cb.Encapsulate(triBounds);
                        cellBounds[cellIdx] = cb;
                    }
                    else
                    {
                        cellBounds[cellIdx] = triBounds;
                    }
                }

                // Rebuild the room's tri-ref list so each cell's refs are contiguous,
                // and each cell's refs are sorted by objectIndex for GTE batching.
                var newTriRefs = new List<BVH.TriangleRef>();
                int cellsAdded = 0;

                for (int c = 0; c < totalCells; c++)
                {
                    if (cellTriRefs[c].Count == 0) continue;

                    // Sort cell's tri-refs by objectIndex
                    cellTriRefs[c].Sort((a, b) => a.objectIndex.CompareTo(b.objectIndex));

                    int firstTriRef = newTriRefs.Count;
                    newTriRefs.AddRange(cellTriRefs[c]);

                    _allCells.Add(new RoomCellData
                    {
                        bounds = cellBounds[c].Value,
                        triRefs = cellTriRefs[c]  // kept for reference
                    });
                    cellsAdded++;
                }

                // Replace the room's tri-ref list with the cell-ordered version
                if (ri < _rooms.Length)
                    _roomTriRefs[ri] = newTriRefs;
                else
                    _catchAllTriRefs = newTriRefs;

                _roomCellCount[ri] = cellsAdded;
            }
        }

        /// <summary>Compute bounds from triangle refs (for catch-all room).</summary>
        /// <summary>
        /// Build per-room portal reference lists.
        /// For each room, store the portals that touch it and the room on the other side.
        /// This replaces the O(N) portal scan at runtime with an O(k) indexed lookup.
        /// </summary>
        private void GeneratePortalRefs()
        {
            int totalRooms = _rooms.Length + 1; // +1 for catch-all
            _allPortalRefs = new List<PortalRefEntry>();
            _roomFirstPortalRef = new int[totalRooms];
            _roomPortalRefCount = new int[totalRooms];

            // Collect portal refs per room
            var perRoom = new List<PortalRefEntry>[totalRooms];
            for (int i = 0; i < totalRooms; i++)
                perRoom[i] = new List<PortalRefEntry>();

            for (int pi = 0; pi < _portals.Count; pi++)
            {
                var portal = _portals[pi];
                int rA = portal.roomA;
                int rB = portal.roomB;
                if (rA >= 0 && rA < totalRooms)
                    perRoom[rA].Add(new PortalRefEntry { portalIndex = (ushort)pi, otherRoom = (ushort)rB });
                if (rB >= 0 && rB < totalRooms)
                    perRoom[rB].Add(new PortalRefEntry { portalIndex = (ushort)pi, otherRoom = (ushort)rA });
            }

            // Flatten into a single array
            for (int ri = 0; ri < totalRooms; ri++)
            {
                _roomFirstPortalRef[ri] = _allPortalRefs.Count;
                _roomPortalRefCount[ri] = perRoom[ri].Count;
                _allPortalRefs.AddRange(perRoom[ri]);
            }
        }

        private static Bounds ComputeTriRefBounds(List<BVH.TriangleRef> triRefs, PSXObjectExporter[] exporters)
        {
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var triRef in triRefs)
            {
                if (triRef.objectIndex >= exporters.Length) continue;
                var exporter = exporters[triRef.objectIndex];
                MeshFilter mf = exporter.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Mesh mesh = mf.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                int[] indices = mesh.triangles;
                Matrix4x4 worldMatrix = exporter.transform.localToWorldMatrix;
                int triStart = triRef.triangleIndex * 3;
                if (triStart + 2 >= indices.Length) continue;
                Vector3 v0 = worldMatrix.MultiplyPoint3x4(vertices[indices[triStart]]);
                Vector3 v1 = worldMatrix.MultiplyPoint3x4(vertices[indices[triStart + 1]]);
                Vector3 v2 = worldMatrix.MultiplyPoint3x4(vertices[indices[triStart + 2]]);
                if (first) { b = new Bounds(v0, Vector3.zero); first = false; }
                b.Encapsulate(v0); b.Encapsulate(v1); b.Encapsulate(v2);
            }
            return b;
        }

        /// <summary>
        /// For each portal, find triangles assigned to one adjacent room whose vertices
        /// also touch the other adjacent room. Duplicate those triangles into the other
        /// room so boundary geometry (doorway walls, floor edges) is visible from both sides.
        /// </summary>
        private void DuplicateBoundaryTriangles(PSXObjectExporter[] exporters, Bounds[] roomBounds)
        {
            if (_portals.Count == 0) return;

            int duplicated = 0;
            // Build a set of existing tri-refs per room for O(1) duplicate checking
            var roomSets = new HashSet<long>[_rooms.Length];
            for (int i = 0; i < _rooms.Length; i++)
            {
                roomSets[i] = new HashSet<long>();
                foreach (var tr in _roomTriRefs[i])
                    roomSets[i].Add(((long)tr.objectIndex << 16) | tr.triangleIndex);
            }

            foreach (var portal in _portals)
            {
                int rA = portal.roomA, rB = portal.roomB;
                if (rA < 0 || rA >= _rooms.Length || rB < 0 || rB >= _rooms.Length) continue;

                // For each triangle in room A, check if any vertex is inside room B's AABB.
                // If so, add a copy to room B (and vice versa).
                DuplicateDirection(rA, rB, exporters, roomBounds, roomSets, ref duplicated);
                DuplicateDirection(rB, rA, exporters, roomBounds, roomSets, ref duplicated);
            }

            if (duplicated > 0)
                Debug.Log($"PSXRoomBuilder: Duplicated {duplicated} boundary triangles across portal edges.");
        }

        private void DuplicateDirection(int srcRoom, int dstRoom,
            PSXObjectExporter[] exporters, Bounds[] roomBounds,
            HashSet<long>[] roomSets, ref int duplicated)
        {
            var srcList = new List<BVH.TriangleRef>(_roomTriRefs[srcRoom]);
            foreach (var triRef in srcList)
            {
                long key = ((long)triRef.objectIndex << 16) | triRef.triangleIndex;
                if (roomSets[dstRoom].Contains(key)) continue;  // Already in dst

                if (triRef.objectIndex >= exporters.Length) continue;
                var exporter = exporters[triRef.objectIndex];
                MeshFilter mf = exporter.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Mesh mesh = mf.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                int[] indices = mesh.triangles;
                Matrix4x4 worldMatrix = exporter.transform.localToWorldMatrix;

                int triStart = triRef.triangleIndex * 3;
                if (triStart + 2 >= indices.Length) continue;
                Vector3 v0 = worldMatrix.MultiplyPoint3x4(vertices[indices[triStart]]);
                Vector3 v1 = worldMatrix.MultiplyPoint3x4(vertices[indices[triStart + 1]]);
                Vector3 v2 = worldMatrix.MultiplyPoint3x4(vertices[indices[triStart + 2]]);

                // Check if any vertex is inside the destination room's AABB
                if (roomBounds[dstRoom].Contains(v0) ||
                    roomBounds[dstRoom].Contains(v1) ||
                    roomBounds[dstRoom].Contains(v2))
                {
                    _roomTriRefs[dstRoom].Add(triRef);
                    roomSets[dstRoom].Add(key);
                    duplicated++;
                }
            }
        }

        /// <summary>
        /// Convert PSXPortalLink components into PSXPortal entries.
        /// Maps PSXRoom references to room indices, validates, and stores center positions.
        /// </summary>
        private void BuildPortals(PSXPortalLink[] portalLinks)
        {
            if (portalLinks == null) return;

            // Build a fast lookup: PSXRoom instance → index.
            var roomIndex = new Dictionary<PSXRoom, int>();
            for (int i = 0; i < _rooms.Length; i++)
                roomIndex[_rooms[i]] = i;

            foreach (var link in portalLinks)
            {
                if (link == null) continue;
                if (link.RoomA == null || link.RoomB == null)
                {
                    Debug.LogWarning($"PSXPortalLink '{link.name}' has unassigned room references — skipped.");
                    continue;
                }
                if (link.RoomA == link.RoomB)
                {
                    Debug.LogWarning($"PSXPortalLink '{link.name}' references the same room twice — skipped.");
                    continue;
                }
                if (!roomIndex.TryGetValue(link.RoomA, out int idxA))
                {
                    Debug.LogWarning($"PSXPortalLink '{link.name}': RoomA '{link.RoomA.name}' is not a known PSXRoom — skipped.");
                    continue;
                }
                if (!roomIndex.TryGetValue(link.RoomB, out int idxB))
                {
                    Debug.LogWarning($"PSXPortalLink '{link.name}': RoomB '{link.RoomB.name}' is not a known PSXRoom — skipped.");
                    continue;
                }

                // Auto-correct normal direction: ensure it points from roomA toward roomB.
                // The user may place the PSXPortalLink facing either way; the runtime
                // backface cull assumes the normal always points A→B.
                Vector3 portalNormal = link.transform.forward;
                Vector3 portalRight = link.transform.right;
                Vector3 portalUp = link.transform.up;
                Vector3 roomACenter = link.RoomA.GetWorldBounds().center;
                Vector3 roomBCenter = link.RoomB.GetWorldBounds().center;
                Vector3 aToB = (roomBCenter - roomACenter).normalized;
                if (Vector3.Dot(portalNormal, aToB) < 0)
                {
                    // Normal faces toward A instead of B — flip it.
                    // Also flip the right axis to keep the coordinate system consistent
                    // (normal × right = up must stay right-handed).
                    portalNormal = -portalNormal;
                    portalRight = -portalRight;
                    Debug.Log($"PSXPortalLink '{link.name}': normal auto-corrected to point from RoomA→RoomB.");
                }

                _portals.Add(new PSXPortal
                {
                    roomA = idxA,
                    roomB = idxB,
                    center = link.transform.position,
                    portalSize = link.PortalSize,
                    normal = portalNormal,
                    right = portalRight,
                    up = portalUp
                });
            }
        }

        /// <summary>
        /// Write room/portal data to the splashpack binary.
        /// Layout: [RoomData × (N+1)] [PortalData × P] [TriangleRef × T] [RoomCell × C]
        /// </summary>
        public void WriteToBinary(System.IO.BinaryWriter writer, float gteScaling)
        {
            if (_rooms == null || _rooms.Length == 0) return;

            int totalRooms = _rooms.Length + 1; // +1 for catch-all (last entry)

            // Per-room data (36 bytes each):
            // AABB (24) + firstTriRef (2) + triRefCount (2) + firstCell (2) + cellCount (1) + portalRefCount (1) + firstPortalRef (2) + pad (2)
            int runningTriRefOffset = 0;
            for (int i = 0; i < _rooms.Length; i++)
            {
                Bounds wb = _rooms[i].GetWorldBounds();
                // PS1 coordinate space (negate Y, swap min/max Y)
                writer.Write(PSXTrig.ConvertWorldToFixed12(wb.min.x / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(-wb.max.y / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(wb.min.z / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(wb.max.x / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(-wb.min.y / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(wb.max.z / gteScaling));

                writer.Write((ushort)runningTriRefOffset);
                writer.Write((ushort)_roomTriRefs[i].Count);

                // Cell info
                int fc = (_roomFirstCell != null && i < _roomFirstCell.Length) ? _roomFirstCell[i] : 0;
                int cc = (_roomCellCount != null && i < _roomCellCount.Length) ? _roomCellCount[i] : 0;
                writer.Write((ushort)fc);
                writer.Write((byte)cc);

                // Portal ref info (Phase 5)
                int prc = (_roomPortalRefCount != null && i < _roomPortalRefCount.Length) ? _roomPortalRefCount[i] : 0;
                int fpr = (_roomFirstPortalRef != null && i < _roomFirstPortalRef.Length) ? _roomFirstPortalRef[i] : 0;
                writer.Write((byte)prc);
                writer.Write((ushort)fpr);
                writer.Write((ushort)0); // pad

                runningTriRefOffset += _roomTriRefs[i].Count;
            }

            // Catch-all room (always rendered) — written as an extra "room" entry
            {
                int catchAllRoomIdx = _rooms.Length; // index of catch-all in cell arrays
                writer.Write(PSXTrig.ConvertWorldToFixed12(-1000f / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(-1000f / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(-1000f / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(1000f / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(1000f / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(1000f / gteScaling));
                writer.Write((ushort)runningTriRefOffset);
                writer.Write((ushort)_catchAllTriRefs.Count);

                int fc = (_roomFirstCell != null && catchAllRoomIdx < _roomFirstCell.Length)
                    ? _roomFirstCell[catchAllRoomIdx] : 0;
                int cc = (_roomCellCount != null && catchAllRoomIdx < _roomCellCount.Length)
                    ? _roomCellCount[catchAllRoomIdx] : 0;
                writer.Write((ushort)fc);
                writer.Write((byte)cc);

                // Catch-all has no portals
                int prc = (_roomPortalRefCount != null && catchAllRoomIdx < _roomPortalRefCount.Length)
                    ? _roomPortalRefCount[catchAllRoomIdx] : 0;
                int fpr = (_roomFirstPortalRef != null && catchAllRoomIdx < _roomFirstPortalRef.Length)
                    ? _roomFirstPortalRef[catchAllRoomIdx] : 0;
                writer.Write((byte)prc);
                writer.Write((ushort)fpr);
                writer.Write((ushort)0); // pad
            }

            // Per-portal data (40 bytes each)
            foreach (var portal in _portals)
            {
                writer.Write((ushort)portal.roomA);
                writer.Write((ushort)portal.roomB);
                writer.Write(PSXTrig.ConvertWorldToFixed12(portal.center.x / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(-portal.center.y / gteScaling));
                writer.Write(PSXTrig.ConvertWorldToFixed12(portal.center.z / gteScaling));
                float halfW = portal.portalSize.x * 0.5f;
                float halfH = portal.portalSize.y * 0.5f;
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(halfW / gteScaling * 4096f), 1, 32767));
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(halfH / gteScaling * 4096f), 1, 32767));
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(portal.normal.x * 4096f), -32768, 32767));
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(-portal.normal.y * 4096f), -32768, 32767));
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(portal.normal.z * 4096f), -32768, 32767));
                writer.Write((short)0); // pad
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(portal.right.x * 4096f), -32768, 32767));
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(-portal.right.y * 4096f), -32768, 32767));
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(portal.right.z * 4096f), -32768, 32767));
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(portal.up.x * 4096f), -32768, 32767));
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(-portal.up.y * 4096f), -32768, 32767));
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(portal.up.z * 4096f), -32768, 32767));
            }

            // Triangle refs (4 bytes each) — rooms in order, then catch-all.
            // After GenerateCells, tri-refs are already ordered by cell within each room.
            for (int i = 0; i < _rooms.Length; i++)
            {
                foreach (var triRef in _roomTriRefs[i])
                {
                    writer.Write(triRef.objectIndex);
                    writer.Write(triRef.triangleIndex);
                }
            }
            foreach (var triRef in _catchAllTriRefs)
            {
                writer.Write(triRef.objectIndex);
                writer.Write(triRef.triangleIndex);
            }

            // Room cells (28 bytes each): AABB (24) + firstTriRef (2) + triRefCount (2)
            // Cell tri-ref indices are global (into the flat tri-ref array written above).
            // We compute them by walking through rooms and their cells.
            {
                int globalTriRefBase = 0;
                int cellWritten = 0;
                for (int ri = 0; ri < totalRooms; ri++)
                {
                    List<BVH.TriangleRef> roomRefs = (ri < _rooms.Length)
                        ? _roomTriRefs[ri] : _catchAllTriRefs;

                    int cellStart = (_roomFirstCell != null && ri < _roomFirstCell.Length)
                        ? _roomFirstCell[ri] : 0;
                    int cellCount = (_roomCellCount != null && ri < _roomCellCount.Length)
                        ? _roomCellCount[ri] : 0;

                    int localOffset = 0;
                    for (int ci = 0; ci < cellCount; ci++)
                    {
                        var cell = _allCells[cellStart + ci];
                        Bounds cb = cell.bounds;
                        int cellTriCount = cell.triRefs.Count;

                        // Write cell AABB in PS1 coords (negate Y, swap min/max Y)
                        writer.Write(PSXTrig.ConvertWorldToFixed12(cb.min.x / gteScaling));
                        writer.Write(PSXTrig.ConvertWorldToFixed12(-cb.max.y / gteScaling));
                        writer.Write(PSXTrig.ConvertWorldToFixed12(cb.min.z / gteScaling));
                        writer.Write(PSXTrig.ConvertWorldToFixed12(cb.max.x / gteScaling));
                        writer.Write(PSXTrig.ConvertWorldToFixed12(-cb.min.y / gteScaling));
                        writer.Write(PSXTrig.ConvertWorldToFixed12(cb.max.z / gteScaling));

                        writer.Write((ushort)(globalTriRefBase + localOffset));
                        writer.Write((ushort)cellTriCount);

                        localOffset += cellTriCount;
                        cellWritten++;
                    }

                    globalTriRefBase += roomRefs.Count;
                }
            }

            // Per-room portal references (4 bytes each): portalIndex (2) + otherRoom (2)
            // Written in flat order matching _allPortalRefs. Each room's RoomData has
            // firstPortalRef + portalRefCount pointing into this array.
            foreach (var pref in _allPortalRefs)
            {
                writer.Write(pref.portalIndex);
                writer.Write(pref.otherRoom);
            }
        }

        public int GetBinarySize()
        {
            if (_rooms == null || _rooms.Length == 0) return 0;
            int roomDataSize = (_rooms.Length + 1) * 36; // +1 for catch-all, 36 bytes each
            int portalDataSize = _portals.Count * 40;
            int triRefSize = TotalTriRefCount * 4;
            int cellSize = _allCells.Count * 28;
            int portalRefSize = _allPortalRefs.Count * 4;
            return roomDataSize + portalDataSize + triRefSize + cellSize + portalRefSize;
        }
    }
}
