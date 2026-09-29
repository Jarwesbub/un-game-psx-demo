using UnityEngine;
using UnityEngine.Serialization;


namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Recast partition method for region generation.
    /// </summary>
    public enum NavPartitionMethod
    {
        /// <summary>Best for indoor/architectural scenes. Produces clean rectangular regions 
        /// but can create oversized regions on undulating terrain.</summary>
        Watershed = 0,
        /// <summary>Best for open terrain and natural geometry. Produces more uniform, 
        /// predictable regions that follow terrain contours better.</summary>
        Monotone = 1,
        /// <summary>Best for multi-level environments. Keeps height layers separated properly 
        /// for overlapping floors and bridges.</summary>
        Layer = 2,
    }

    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXPlayer.png")]
    public class PSXPlayer : MonoBehaviour
    {
        [Header("Player Dimensions")]
        [FormerlySerializedAs("PlayerHeight")]
        [Tooltip("Camera eye height above the player's feet")]
        [SerializeField] private float playerHeight = 1.8f;

        [Tooltip("Collision radius for wall sliding")]
        [SerializeField] private float playerRadius = 0.5f;

        [Header("Movement")]
        [Tooltip("Walk speed in world units per second")]
        [SerializeField] private float moveSpeed = 3.0f;

        [Tooltip("Sprint speed in world units per second")]
        [SerializeField] private float sprintSpeed = 8.0f;

        [Header("Navigation")]
        [Tooltip("Maximum height the agent can step up")]
        [SerializeField] private float maxStepHeight = 0.35f;

        [Tooltip("Maximum walkable slope angle in degrees")]
        [SerializeField] private float walkableSlopeAngle = 46.0f;

        [Tooltip("Voxel size in XZ plane (smaller = more accurate but slower)")]
        [SerializeField] private float navCellSize = 0.05f;

        [Tooltip("Voxel height (smaller = more accurate vertical resolution)")]
        [SerializeField] private float navCellHeight = 0.025f;

        [Header("Navigation — Advanced")]
        [Tooltip("Regions smaller than this (in voxels) are removed entirely. Raise to eliminate tiny slivers.")]
        [SerializeField] private int navMinRegionArea = 8;

        [Tooltip("Regions smaller than this (in voxels) get merged into their largest neighbor. " +
                 "Lower values = more regions, better terrain height accuracy. Higher = fewer regions, less PS1 memory.")]
        [SerializeField] private int navMergeRegionArea = 20;

        [Tooltip("Maximum deviation allowed when simplifying region contours (world units). " +
                 "Lower = polygon edges follow the voxelized boundary more closely. Higher = fewer vertices.")]
        [SerializeField] private float navMaxSimplifyError = 1.3f;

        [Tooltip("Maximum polygon edge length in world units. Shorter = smaller regions with better height fit on terrain.")]
        [SerializeField] private float navMaxEdgeLength = 12.0f;

        [Tooltip("Region partitioning algorithm.\n\n" +
                 "Watershed: Best for indoor/architectural scenes.\n" +
                 "Monotone: Better for open terrain and natural geometry.\n" +
                 "Layer: Best for multi-level environments with overlapping floors.")]
        [SerializeField] private NavPartitionMethod navPartitionMethod = NavPartitionMethod.Watershed;

        [Tooltip("Detail mesh height sampling distance as a multiplier of Cell Size. " +
                 "Lower = more height samples, more accurate floor planes on terrain.")]
        [SerializeField] private float navDetailSampleDist = 6.0f;

        [Tooltip("Maximum height error for the detail mesh (world units). " +
                 "Independent of Cell Height. Lower = more accurate floor Y on slopes.")]
        [SerializeField] private float navDetailMaxError = 0.025f;

        [Tooltip("Maximum height error for a single region's floor plane (world units). " +
                 "Regions with plane fit error exceeding this threshold are flagged as warnings.")]
        [SerializeField] private float navMaxPlaneError = 0.15f;

        [Header("Jump & Gravity")]
        [Tooltip("Peak jump height in world units")]
        [SerializeField] private float jumpHeight = 2.0f;

        [Tooltip("Downward acceleration in world units per second squared (positive value)")]
        [SerializeField] private float gravity = 20.0f;

        // Public accessors — Player dimensions
        public float PlayerHeight => playerHeight;
        public float PlayerRadius => playerRadius;

        // Public accessors — Movement
        public float MoveSpeed => moveSpeed;
        public float SprintSpeed => sprintSpeed;

        // Public accessors — Navigation core
        public float MaxStepHeight => maxStepHeight;
        public float WalkableSlopeAngle => walkableSlopeAngle;
        public float NavCellSize => navCellSize;
        public float NavCellHeight => navCellHeight;

        // Public accessors — Navigation advanced
        public int NavMinRegionArea => navMinRegionArea;
        public int NavMergeRegionArea => navMergeRegionArea;
        public float NavMaxSimplifyError => navMaxSimplifyError;
        public float NavMaxEdgeLength => navMaxEdgeLength;
        public NavPartitionMethod NavPartitionMethod => navPartitionMethod;
        public float NavDetailSampleDist => navDetailSampleDist;
        public float NavDetailMaxError => navDetailMaxError;
        public float NavMaxPlaneError => navMaxPlaneError;

        // Public accessors — Physics
        public float JumpHeight => jumpHeight;
        public float Gravity => gravity;
        public Vector3 CamPoint { get; protected set; }

        /// <summary>
        /// Apply an Indoor preset optimized for architectural/indoor scenes.
        /// </summary>
        public void ApplyIndoorPreset()
        {
            navCellSize = 0.05f;
            navCellHeight = 0.025f;
            navMinRegionArea = 8;
            navMergeRegionArea = 20;
            navMaxSimplifyError = 1.3f;
            navMaxEdgeLength = 12.0f;
            navPartitionMethod = NavPartitionMethod.Watershed;
            navDetailSampleDist = 6.0f;
            navDetailMaxError = 0.025f;
            navMaxPlaneError = 0.15f;
        }

        /// <summary>
        /// Apply a Terrain preset optimized for natural/outdoor geometry.
        /// </summary>
        public void ApplyTerrainPreset()
        {
            navCellSize = 0.08f;
            navCellHeight = 0.04f;
            navMinRegionArea = 4;
            navMergeRegionArea = 6;
            navMaxSimplifyError = 0.4f;
            navMaxEdgeLength = 5.0f;
            navPartitionMethod = NavPartitionMethod.Monotone;
            navDetailSampleDist = 4.0f;
            navDetailMaxError = 0.02f;
            navMaxPlaneError = 0.1f;
        }

        /// <summary>
        /// Apply a Multi-Level preset for environments with overlapping floors.
        /// </summary>
        public void ApplyMultiLevelPreset()
        {
            navCellSize = 0.05f;
            navCellHeight = 0.02f;
            navMinRegionArea = 6;
            navMergeRegionArea = 12;
            navMaxSimplifyError = 0.8f;
            navMaxEdgeLength = 8.0f;
            navPartitionMethod = NavPartitionMethod.Layer;
            navDetailSampleDist = 5.0f;
            navDetailMaxError = 0.02f;
            navMaxPlaneError = 0.12f;
        }

        public void FindNavmesh()
        {
            // Raycast down from the transform to find the ground,
            // then place CamPoint at ground + playerHeight
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 100f))
            {
                CamPoint = hit.point + new Vector3(0, playerHeight, 0);
            }
            else
            {
                // Fallback: no ground hit, use transform directly
                CamPoint = transform.position + new Vector3(0, playerHeight, 0);
            }
        }

        void OnDrawGizmos()
        {
            FindNavmesh();

            // Red sphere at camera eye point
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(CamPoint, 0.2f);

            // Wireframe sphere at feet showing player radius
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            Vector3 feet = CamPoint - new Vector3(0, playerHeight, 0);
            Gizmos.DrawWireSphere(feet, playerRadius);
        }
    }
}
