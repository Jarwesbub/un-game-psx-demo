using System.Collections.Generic;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Marks a SkinnedMeshRenderer for export as a bone-animated mesh.
    /// Attach to GameObjects with a SkinnedMeshRenderer component (or whose children have one).
    /// At export time, the bind-pose mesh is exported through the standard pipeline,
    /// and per-bone animation frames are baked and written as skin data.
    /// </summary>
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXObjectExporter.png")]
    public class PSXSkinnedObjectExporter : MonoBehaviour
    {
        [Tooltip("Animation clips to bake for this skinned mesh. " +
                 "Each becomes a named clip addressable via SkinnedAnim.Play in Lua.")]
        public AnimationClip[] AnimationClips = new AnimationClip[0];

        [Tooltip("Sampling rate for baking bone matrices (frames per second). " +
                 "Lower values save memory; 10-15 is usually sufficient for PS1.")]
        [Range(1, 30)]
        public int TargetFPS = 15;

        [Tooltip("Whether this object starts active in the scene.")]
        public bool IsActive = true;

        [Tooltip("Lua script file for this object's behaviour.")]
        public LuaFile LuaFile;

        [Tooltip("Texture bit depth for the mesh materials.")]
        public PSXBPP BitDepth = PSXBPP.TEX_8BIT;

        [Tooltip("Vertex color mode for lighting.")]
        public VertexColorMode ColorMode = VertexColorMode.BakedLighting;

        [Tooltip("Flat vertex color (used when ColorMode is FlatColor).")]
        public Color32 FlatVertexColor = new Color32(128, 128, 128, 255);

        [Tooltip("Smooth normals for lighting. Disable for flat/faceted shading.")]
        public bool SmoothNormals = true;

        /// <summary>
        /// The proxy PSXObjectExporter created during export (lives on a temporary child GO).
        /// Null outside of export.
        /// </summary>
        [System.NonSerialized] public PSXObjectExporter ProxyExporter;

        /// <summary>
        /// The proxy GameObject created during export. Destroyed after export.
        /// </summary>
        [System.NonSerialized] public GameObject ProxyGameObject;

        /// <summary>
        /// Index into the combined exporters array (set during export).
        /// </summary>
        [System.NonSerialized] public int ExporterIndex = -1;
    }
}
