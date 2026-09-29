// I raged that my scrollwheel was broken while writing this and that's why it's 2 files.


using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using SplashEdit.RuntimeCode;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// Custom inspector for PSXAudioClip component.
    /// </summary>
    [CustomEditor(typeof(PSXAudioClip))]
    public class PSXAudioClipEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Header card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("PSX Audio Clip", PSXEditorStyles.CardHeaderStyle);

            PSXAudioClip audioClip = (PSXAudioClip)target;

            EditorGUILayout.BeginHorizontal();
            if (audioClip.Clip != null)
                PSXEditorStyles.DrawStatusBadge("Clip Set", PSXEditorStyles.Success, 70);
            else
                PSXEditorStyles.DrawStatusBadge("No Clip", PSXEditorStyles.Warning, 70);

            if (audioClip.Loop)
                PSXEditorStyles.DrawStatusBadge("Loop", PSXEditorStyles.AccentCyan, 50);
            EditorGUILayout.EndHorizontal();

            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Properties card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("Clip Settings", PSXEditorStyles.CardHeaderStyle);
            PSXEditorStyles.DrawSeparator(2, 4);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("ClipName"), new GUIContent("Clip Name",
                "Name used to identify this clip in Lua (Audio.Play(\"name\"))."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Clip"), new GUIContent("Audio Clip",
                "Unity AudioClip to convert to PS1 SPU ADPCM format."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("SampleRate"), new GUIContent("Sample Rate",
                "Target sample rate for the PS1 (lower = smaller, max 44100)."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Loop"), new GUIContent("Loop",
                "Whether this clip should loop when played."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("DefaultVolume"), new GUIContent("Volume",
                "Default playback volume (0-127)."));

            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Info card
            if (audioClip.Clip != null)
            {
                PSXEditorStyles.BeginCard();
                float duration = audioClip.Clip.length;
                int srcRate = audioClip.Clip.frequency;
                EditorGUILayout.LabelField(
                    $"Source: {srcRate} Hz, {duration:F2}s, {audioClip.Clip.channels}ch\n" +
                    $"Target: {audioClip.SampleRate} Hz SPU ADPCM",
                    PSXEditorStyles.InfoBox);
                PSXEditorStyles.EndCard();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Custom inspector for PSXPlayer component.
    /// Merges the NavRegionBuilder preview/build workflow directly into the player inspector
    /// so all navigation configuration and preview lives in one place.
    /// </summary>
    [CustomEditor(typeof(PSXPlayer))]
    public class PSXPlayerEditor : Editor
    {
        private bool _dimensionsFoldout = true;
        private bool _movementFoldout = true;
        private bool _navigationFoldout = true;
        private bool _navAdvancedFoldout = false;
        private bool _navPreviewFoldout = false;
        private bool _physicsFoldout = true;

        // Nav region preview state (previously in PSXNavRegionEditor window)
        private static PSXNavRegionBuilder _builder;
        private static bool _previewRegions = true;
        private static bool _previewPortals = true;
        private static bool _previewLabels = true;
        private static int _selectedRegion = -1;
        private static bool _sceneGuiRegistered = false;

        private void OnEnable()
        {
            if (!_sceneGuiRegistered)
            {
                SceneView.duringSceneGui += OnSceneGUI;
                _sceneGuiRegistered = true;
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            PSXPlayer player = (PSXPlayer)target;

            // ────────────────────────────────────────────────────────
            // Header
            // ────────────────────────────────────────────────────────
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("PSX Player", PSXEditorStyles.CardHeaderStyle);
            EditorGUILayout.LabelField("First-person player controller for PS1", PSXEditorStyles.RichLabel);
            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // ────────────────────────────────────────────────────────
            // Player Dimensions
            // ────────────────────────────────────────────────────────
            _dimensionsFoldout = PSXEditorStyles.DrawFoldoutCard("Player Dimensions", _dimensionsFoldout, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("playerHeight"), new GUIContent("Height",
                    "Camera eye height above the player's feet."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("playerRadius"), new GUIContent("Radius",
                    "Collision radius for wall sliding."));
            });

            EditorGUILayout.Space(2);

            // ────────────────────────────────────────────────────────
            // Movement
            // ────────────────────────────────────────────────────────
            _movementFoldout = PSXEditorStyles.DrawFoldoutCard("Movement", _movementFoldout, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("moveSpeed"), new GUIContent("Walk Speed",
                    "Walk speed in world units per second."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sprintSpeed"), new GUIContent("Sprint Speed",
                    "Sprint speed in world units per second."));
            });

            EditorGUILayout.Space(2);

            // ────────────────────────────────────────────────────────
            // Navigation (fully merged from PSXNavRegionEditor)
            // ────────────────────────────────────────────────────────
            _navigationFoldout = PSXEditorStyles.DrawFoldoutCard("Navigation", _navigationFoldout, () =>
            {
                EditorGUILayout.LabelField(
                    "<color=#99bbdd>Navigation uses DotRecast (Recast) to voxelize your scene geometry into a 3D grid, " +
                    "identify walkable surfaces by slope and clearance, then decompose them into convex polygonal regions " +
                    "with portal edges between neighbors. Each region stores a floor plane equation (Y = A\u00b7X + B\u00b7Z + D) " +
                    "so the PS1 can compute exact floor height at any position in O(1).\n\n" +
                    "The PS1 runtime has no FPU \u2014 all math is 20.12 fixed-point on a 33MHz CPU with 2MB RAM. " +
                    "More regions = more memory and slower brute-force fallback lookups. " +
                    "Fewer regions = worse floor height accuracy on uneven terrain.\n\n" +
                    "Set collision to <b>Static</b> on floor/wall meshes for them to contribute to the navmesh. " +
                    "Objects with <b>None</b> or <b>Dynamic</b> collision are excluded from navigation generation.</color>",
                    PSXEditorStyles.RichLabel);

                EditorGUILayout.Space(6);

                // ── Agent settings ──
                EditorGUILayout.LabelField("Agent", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("maxStepHeight"), new GUIContent("Max Step Height",
                    "Maximum vertical step the agent can climb. If your stairs are taller than this, the navmesh won't connect across them."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("walkableSlopeAngle"), new GUIContent("Walkable Slope (\u00b0)",
                    "Maximum slope angle in degrees that is considered walkable. Surfaces steeper than this become walls."));

                PSXEditorStyles.DrawSeparator(4, 4);

                // ── Voxelization ──
                EditorGUILayout.LabelField("Voxelization", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "<color=#888888>Controls the 3D grid resolution used to analyze your geometry. " +
                    "Smaller cells = more accurate edges and slopes, but exponentially slower and more regions.</color>",
                    PSXEditorStyles.RichLabel);
                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("navCellSize"), new GUIContent("Cell Size (XZ)",
                    "Horizontal voxel size in world units. This is the fundamental accuracy limit for edge placement. " +
                    "Typical: 0.03\u20130.1. Smaller = polygon edges follow geometry more tightly."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("navCellHeight"), new GUIContent("Cell Height (Y)",
                    "Vertical voxel resolution. Affects how accurately step heights and vertical clearance are detected. " +
                    "Also determines the vertical quantization of walkable surfaces. " +
                    "Typical: half of Cell Size or smaller."));

                // Computed voxel stats
                EditorGUILayout.Space(3);
                float cs = player.NavCellSize;
                float ch = player.NavCellHeight;
                int walkH = Mathf.Max(1, (int)System.Math.Ceiling(player.PlayerHeight / ch));
                int walkR = Mathf.Max(1, (int)System.Math.Ceiling(player.PlayerRadius / cs));
                int walkC = (int)System.Math.Floor(player.MaxStepHeight / ch);
                EditorGUILayout.LabelField($"  \u2192 Walkable height: {walkH} cells  |  Radius: {walkR} cells  |  Climb: {walkC} cells ({walkC * ch:F3}m)",
                    EditorStyles.miniLabel);

                if (walkH < 4)
                    EditorGUILayout.HelpBox("Walkable height is very low (<4 cells). The agent may clip through low ceilings. Decrease Cell Height or increase Player Height.", MessageType.Warning);
                if (walkH > 200)
                    EditorGUILayout.HelpBox("Walkable height is very high (>200 cells). This wastes voxel memory. Increase Cell Height.", MessageType.Warning);

                PSXEditorStyles.DrawSeparator(4, 4);

                // ── Region Partitioning ──
                EditorGUILayout.LabelField("Region Partitioning", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "<color=#888888>Controls how walkable voxels are grouped into convex regions. " +
                    "This is the most impactful section for terrain quality.</color>",
                    PSXEditorStyles.RichLabel);
                EditorGUILayout.Space(2);

                EditorGUILayout.PropertyField(serializedObject.FindProperty("navPartitionMethod"), new GUIContent("Method",
                    "Watershed: Classic Recast algorithm, best for indoor/architectural scenes. Can create oversized regions on rolling terrain.\n\n" +
                    "Monotone: Produces more uniform, predictable regions. Better for open terrain and natural geometry.\n\n" +
                    "Layer: Designed for multi-level environments. Keeps overlapping floors and bridges separated."));

                EditorGUILayout.PropertyField(serializedObject.FindProperty("navMinRegionArea"), new GUIContent("Min Region Area",
                    "Regions with fewer voxels than this are removed entirely. Eliminates tiny slivers on complex geometry. " +
                    "Typical: 4\u201316."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("navMergeRegionArea"), new GUIContent("Merge Region Area",
                    "Regions with fewer voxels than this get merged into their largest neighbor. " +
                    "THIS IS THE MOST IMPORTANT PARAMETER FOR TERRAIN. " +
                    "High values (20+) create huge regions that span valleys \u2014 the single floor plane can't represent the actual height. " +
                    "Low values (4\u20138) keep regions small with accurate floor planes.\n\n" +
                    "Trade-off: Lower = better terrain accuracy but more regions (more PS1 memory). " +
                    "Higher = fewer regions but worse height accuracy on uneven ground."));

                PSXEditorStyles.DrawSeparator(4, 4);

                // ── Contour & Edge ──
                EditorGUILayout.LabelField("Contour & Edge", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("navMaxSimplifyError"), new GUIContent("Max Simplify Error",
                    "Maximum distance (world units) that polygon edges can deviate from the voxelized boundary during contour simplification. " +
                    "Lower = edges follow terrain more closely (more vertices). Higher = smoother edges but less accurate.\n\n" +
                    "For flat indoor floors: 1.0\u20132.0. For natural terrain: 0.3\u20130.6."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("navMaxEdgeLength"), new GUIContent("Max Edge Length",
                    "Maximum polygon edge length in world units. Shorter edges mean smaller regions with better floor plane fits. " +
                    "Longer edges reduce region count.\n\n" +
                    "For terrain: 4\u20136. For indoors: 8\u201316."));

                // ── Advanced detail settings ──
                DrawAdvancedNavSection(player);

                // ── Presets ──
                DrawPresetsSection(player);
            });

            EditorGUILayout.Space(2);

            // ────────────────────────────────────────────────────────
            // Jump & Gravity
            // ────────────────────────────────────────────────────────
            _physicsFoldout = PSXEditorStyles.DrawFoldoutCard("Jump & Gravity", _physicsFoldout, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("jumpHeight"), new GUIContent("Jump Height",
                    "Peak jump height in world units."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("gravity"), new GUIContent("Gravity",
                    "Downward acceleration in world units per second squared."));
            });

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(4);

            // ────────────────────────────────────────────────────────
            // Nav Region Preview (build, visualize, stats, validation)
            // ────────────────────────────────────────────────────────
            DrawNavPreviewSection(player);
        }

        // ================================================================
        // Advanced nav settings (detail mesh, plane error)
        // ================================================================
        private void DrawAdvancedNavSection(PSXPlayer player)
        {
            PSXEditorStyles.DrawSeparator(4, 4);
            _navAdvancedFoldout = EditorGUILayout.Foldout(_navAdvancedFoldout, "Detail Mesh & Plane Accuracy", true, EditorStyles.foldoutHeader);
            if (!_navAdvancedFoldout) return;

            EditorGUILayout.LabelField(
                "<color=#888888>The detail mesh adds interior height samples to each polygon for more accurate floor planes. " +
                "These settings control that sampling and the plane error threshold used for validation.</color>",
                PSXEditorStyles.RichLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("navDetailSampleDist"), new GUIContent("Detail Sample Dist",
                "Detail mesh height sampling distance, as a multiplier of Cell Size. " +
                "Lower = more height samples, more accurate floor planes on slopes. Higher = faster build.\n\n" +
                "Typical: 4\u20138. For terrain: 3\u20135."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("navDetailMaxError"), new GUIContent("Detail Max Error",
                "Maximum allowed height error for the detail mesh (world units). " +
                "This is independent of Cell Height. Lower = floor Y values track the actual surface more closely.\n\n" +
                "Typical: 0.01\u20130.05."));

            PSXEditorStyles.DrawSeparator(2, 2);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("navMaxPlaneError"), new GUIContent("Max Plane Error",
                "Maximum acceptable plane-fit deviation for a single region (world units). " +
                "Regions exceeding this threshold are flagged as warnings after building. " +
                "A high value here means the floor plane doesn't match the actual terrain well \u2014 " +
                "the player may float or sink.\n\n" +
                "Typical: 0.05\u20130.2. For precise terrain: 0.05\u20130.1."));
        }

        // ================================================================
        // Presets (Indoor / Terrain / Multi-Level)
        // ================================================================
        private void DrawPresetsSection(PSXPlayer player)
        {
            PSXEditorStyles.DrawSeparator(6, 4);
            EditorGUILayout.LabelField("Quick Presets", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "<color=#888888>Apply tuned defaults for common scene types. You can customize individual values afterwards.</color>",
                PSXEditorStyles.RichLabel);
            EditorGUILayout.Space(3);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(new GUIContent("Indoor", "Optimized for architectural/indoor scenes with flat floors and walls."), GUILayout.Height(24)))
            {
                Undo.RecordObject(player, "Apply Indoor Nav Preset");
                player.ApplyIndoorPreset();
                EditorUtility.SetDirty(player);
                serializedObject.Update();
            }
            if (GUILayout.Button(new GUIContent("Terrain", "Optimized for natural terrain, hills, and outdoor environments."), GUILayout.Height(24)))
            {
                Undo.RecordObject(player, "Apply Terrain Nav Preset");
                player.ApplyTerrainPreset();
                EditorUtility.SetDirty(player);
                serializedObject.Update();
            }
            if (GUILayout.Button(new GUIContent("Multi-Level", "Optimized for multi-story buildings, bridges, and overlapping floors."), GUILayout.Height(24)))
            {
                Undo.RecordObject(player, "Apply Multi-Level Nav Preset");
                player.ApplyMultiLevelPreset();
                EditorUtility.SetDirty(player);
                serializedObject.Update();
            }

            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // Nav Region Preview section
        // ================================================================
        private void DrawNavPreviewSection(PSXPlayer player)
        {
            _navPreviewFoldout = PSXEditorStyles.DrawFoldoutCard("Nav Region Preview", _navPreviewFoldout, () =>
            {
                EditorGUILayout.LabelField(
                    "<color=#99bbdd>Build a preview of the navigation regions directly in the scene view. " +
                    "This uses the same DotRecast pipeline as the final export so what you see is what you get. " +
                    "The preview is not saved \u2014 it rebuilds from your settings each time.</color>",
                    PSXEditorStyles.RichLabel);
                EditorGUILayout.Space(4);

                // Build / Clear buttons
                EditorGUILayout.BeginHorizontal();
                GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
                if (GUILayout.Button("Build Nav Regions", GUILayout.Height(30)))
                {
                    BuildNavRegions(player);
                }
                GUI.backgroundColor = Color.white;

                if (_builder != null && _builder.RegionCount > 0)
                {
                    GUI.backgroundColor = new Color(0.8f, 0.4f, 0.4f);
                    if (GUILayout.Button("Clear", GUILayout.Width(60), GUILayout.Height(30)))
                    {
                        _builder = null;
                        _selectedRegion = -1;
                        SceneView.RepaintAll();
                    }
                    GUI.backgroundColor = Color.white;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(4);

                // Visualization toggles
                EditorGUILayout.BeginHorizontal();
                _previewRegions = GUILayout.Toggle(_previewRegions, "Regions", EditorStyles.miniButtonLeft);
                _previewPortals = GUILayout.Toggle(_previewPortals, "Portals", EditorStyles.miniButtonMid);
                _previewLabels = GUILayout.Toggle(_previewLabels, "Labels", EditorStyles.miniButtonRight);
                EditorGUILayout.EndHorizontal();

                // Statistics & validation
                if (_builder != null && _builder.RegionCount > 0)
                {
                    EditorGUILayout.Space(6);
                    DrawNavStatistics(player);
                    DrawNavValidation(player);
                }
                else
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField(
                        "<color=#999966>No nav regions built yet. Click 'Build Nav Regions' to generate a preview.</color>",
                        PSXEditorStyles.RichLabel);
                }
            });
        }

        // ================================================================
        // Statistics display
        // ================================================================
        private void DrawNavStatistics(PSXPlayer player)
        {
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("Statistics", EditorStyles.boldLabel);
            PSXEditorStyles.DrawSeparator(2, 4);

            EditorGUILayout.LabelField("Regions", _builder.RegionCount.ToString());
            EditorGUILayout.LabelField("Portals", _builder.PortalCount.ToString());

            var rooms = new HashSet<byte>();
            for (int i = 0; i < _builder.RegionCount; i++)
                rooms.Add(_builder.Regions[i].roomIndex);
            EditorGUILayout.LabelField("Rooms", rooms.Count.ToString());

            int exportSize = _builder.GetBinarySize();
            EditorGUILayout.LabelField("Export Size", $"{exportSize:N0} bytes ({exportSize / 1024f:F1} KB)");

            int flat = 0, ramp = 0, stairs = 0;
            float worstDev = 0;
            int badPlaneCount = 0;
            for (int i = 0; i < _builder.RegionCount; i++)
            {
                var r = _builder.Regions[i];
                switch (r.surfaceType)
                {
                    case NavSurfaceType.Flat: flat++; break;
                    case NavSurfaceType.Ramp: ramp++; break;
                    case NavSurfaceType.Stairs: stairs++; break;
                }
                if (r.maxPlaneDeviation > worstDev) worstDev = r.maxPlaneDeviation;
                if (r.maxPlaneDeviation > player.NavMaxPlaneError) badPlaneCount++;
            }
            EditorGUILayout.LabelField("Surface Types", $"{flat} flat, {ramp} ramp, {stairs} stairs");
            EditorGUILayout.LabelField("Worst Plane Error", $"{worstDev:F3}m");
            if (badPlaneCount > 0)
                EditorGUILayout.LabelField($"<color=#ee8844>{badPlaneCount} region(s) exceed max plane error ({player.NavMaxPlaneError:F3}m)</color>",
                    PSXEditorStyles.RichLabel);

            // PS1 budget warnings
            if (_builder.RegionCount > 256)
                EditorGUILayout.HelpBox($"Region count ({_builder.RegionCount}) is very high for PS1. " +
                    "The brute-force fallback iterates all regions on a 33MHz CPU. " +
                    "Consider increasing Merge Region Area or Max Edge Length.", MessageType.Error);
            else if (_builder.RegionCount > 128)
                EditorGUILayout.HelpBox($"Region count ({_builder.RegionCount}) is getting high for PS1. " +
                    "Consider increasing Merge Region Area slightly.", MessageType.Warning);

            if (exportSize > 16384)
                EditorGUILayout.HelpBox($"Export size ({exportSize / 1024f:F1} KB) is very large for PS1 (2MB total RAM). " +
                    "Reduce region count with coarser settings.", MessageType.Error);
            else if (exportSize > 8192)
                EditorGUILayout.HelpBox($"Export size ({exportSize / 1024f:F1} KB) is large for PS1.", MessageType.Warning);

            // Selected region detail
            if (_selectedRegion >= 0 && _selectedRegion < _builder.RegionCount)
            {
                PSXEditorStyles.DrawSeparator(4, 4);
                EditorGUILayout.LabelField($"Selected Region #{_selectedRegion}", EditorStyles.boldLabel);
                var region = _builder.Regions[_selectedRegion];
                EditorGUILayout.LabelField("  Vertices", region.vertsXZ.Count.ToString());
                EditorGUILayout.LabelField("  Portals", region.portalCount.ToString());
                EditorGUILayout.LabelField("  Surface", region.surfaceType.ToString());
                EditorGUILayout.LabelField("  Room", region.roomIndex == 0xFF ? "Exterior" : region.roomIndex.ToString());
                EditorGUILayout.LabelField("  Floor Plane", $"Y = {region.planeA:F3}\u00b7X + {region.planeB:F3}\u00b7Z + {region.planeD:F2}");
                EditorGUILayout.LabelField("  Plane Error", $"{region.maxPlaneDeviation:F3}m");
                if (region.maxPlaneDeviation > player.NavMaxPlaneError)
                    EditorGUILayout.LabelField("  <color=#ee4444>\u26a0 Exceeds max plane error threshold!</color>", PSXEditorStyles.RichLabel);
            }

            PSXEditorStyles.EndCard();
        }

        // ================================================================
        // Validation warnings
        // ================================================================
        private void DrawNavValidation(PSXPlayer player)
        {
            if (_builder == null) return;

            var warnings = new List<string>();

            for (int i = 0; i < _builder.RegionCount; i++)
            {
                var region = _builder.Regions[i];
                if (region.vertsXZ.Count < 3)
                    warnings.Add($"Region {i}: degenerate ({region.vertsXZ.Count} verts)");
                if (region.portalCount == 0 && _builder.RegionCount > 1)
                    warnings.Add($"Region {i}: isolated (no portal connections)");
                if (region.vertsXZ.Count > 8)
                    warnings.Add($"Region {i}: too many verts ({region.vertsXZ.Count} > 8 max for PS1)");
                if (region.maxPlaneDeviation > player.NavMaxPlaneError)
                    warnings.Add($"Region {i}: plane error {region.maxPlaneDeviation:F3}m (limit {player.NavMaxPlaneError:F3}m)");
            }

            if (warnings.Count > 0)
            {
                EditorGUILayout.Space(4);
                PSXEditorStyles.BeginCard();
                EditorGUILayout.LabelField($"Warnings ({warnings.Count})", EditorStyles.boldLabel);
                PSXEditorStyles.DrawSeparator(2, 4);
                int show = Mathf.Min(warnings.Count, 15);
                for (int i = 0; i < show; i++)
                    EditorGUILayout.LabelField($"<color=#ee8844>\u2022 {warnings[i]}</color>", PSXEditorStyles.RichLabel);
                if (warnings.Count > show)
                    EditorGUILayout.LabelField($"<color=#999999>... and {warnings.Count - show} more</color>", PSXEditorStyles.RichLabel);
                PSXEditorStyles.EndCard();
            }
        }

        // ================================================================
        // Build nav regions
        // ================================================================
        private void BuildNavRegions(PSXPlayer player)
        {
            EditorUtility.DisplayProgressBar("Nav Region Builder", "Building nav regions with DotRecast...", 0.3f);

            _builder = new PSXNavRegionBuilder();
            _builder.AgentHeight = player.PlayerHeight;
            _builder.AgentRadius = player.PlayerRadius;
            _builder.MaxStepHeight = player.MaxStepHeight;
            _builder.WalkableSlopeAngle = player.WalkableSlopeAngle;
            _builder.CellSize = player.NavCellSize;
            _builder.CellHeight = player.NavCellHeight;
            _builder.MinRegionArea = player.NavMinRegionArea;
            _builder.MergeRegionArea = player.NavMergeRegionArea;
            _builder.MaxSimplifyError = player.NavMaxSimplifyError;
            _builder.MaxEdgeLength = player.NavMaxEdgeLength;
            _builder.PartitionMethod = player.NavPartitionMethod;
            _builder.DetailSampleDist = player.NavDetailSampleDist;
            _builder.DetailMaxError = player.NavDetailMaxError;
            _builder.MaxPlaneError = player.NavMaxPlaneError;

            Vector3 playerSpawn = player.transform.position;
            player.FindNavmesh();
            playerSpawn = player.CamPoint;

            PSXObjectExporter[] exporters =
                Object.FindObjectsByType<PSXObjectExporter>(FindObjectsSortMode.None);

            _builder.Build(exporters, playerSpawn);

            _selectedRegion = -1;
            EditorUtility.ClearProgressBar();
            SceneView.RepaintAll();
        }

        // ================================================================
        // Scene view drawing (region polygons, portals, labels)
        // ================================================================
        private static readonly Color[] RoomColors = new Color[]
        {
            new Color(0.2f, 0.8f, 0.2f),
            new Color(0.2f, 0.6f, 0.9f),
            new Color(0.9f, 0.7f, 0.1f),
            new Color(0.8f, 0.2f, 0.8f),
            new Color(0.1f, 0.9f, 0.9f),
            new Color(0.9f, 0.5f, 0.2f),
            new Color(0.5f, 0.9f, 0.3f),
            new Color(0.9f, 0.3f, 0.5f),
            new Color(0.4f, 0.4f, 0.9f),
            new Color(0.7f, 0.9f, 0.7f),
            new Color(0.9f, 0.9f, 0.4f),
            new Color(0.6f, 0.3f, 0.6f),
            new Color(0.3f, 0.7f, 0.7f),
            new Color(0.8f, 0.6f, 0.4f),
            new Color(0.4f, 0.8f, 0.6f),
            new Color(0.7f, 0.4f, 0.4f),
        };

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (_builder == null || _builder.RegionCount == 0) return;

            var regions = _builder.Regions;

            if (_previewRegions)
            {
                for (int i = 0; i < regions.Count; i++)
                {
                    var region = regions[i];
                    bool selected = (i == _selectedRegion);

                    Color baseColor = RoomColors[region.roomIndex % RoomColors.Length];
                    // Tint regions red if their plane deviation is bad
                    if (region.maxPlaneDeviation > 0.15f)
                        baseColor = Color.Lerp(baseColor, Color.red,
                            Mathf.Clamp01((region.maxPlaneDeviation - 0.15f) / 0.3f));

                    float fillAlpha = selected ? 0.4f : 0.15f;

                    if (region.vertsXZ.Count >= 3)
                    {
                        Vector3[] worldVerts = new Vector3[region.vertsXZ.Count];
                        for (int v = 0; v < region.vertsXZ.Count; v++)
                        {
                            float y = region.planeA * region.vertsXZ[v].x +
                                      region.planeB * region.vertsXZ[v].y +
                                      region.planeD;
                            worldVerts[v] = new Vector3(
                                region.vertsXZ[v].x, y + 0.05f, region.vertsXZ[v].y);
                        }

                        Handles.color = selected
                            ? Color.white
                            : new Color(baseColor.r, baseColor.g, baseColor.b, 0.8f);
                        for (int v = 0; v < worldVerts.Length; v++)
                            Handles.DrawLine(worldVerts[v],
                                worldVerts[(v + 1) % worldVerts.Length]);

                        Handles.color = new Color(baseColor.r, baseColor.g, baseColor.b,
                            fillAlpha);
                        for (int v = 1; v < worldVerts.Length - 1; v++)
                            Handles.DrawAAConvexPolygon(
                                worldVerts[0], worldVerts[v], worldVerts[v + 1]);

                        if (_previewLabels)
                        {
                            Vector3 center = Vector3.zero;
                            foreach (var wv in worldVerts) center += wv;
                            center /= worldVerts.Length;

                            string label = $"R{i}";
                            if (region.roomIndex != 0xFF)
                                label += $"\nRm{region.roomIndex}";
                            Handles.Label(center, label, EditorStyles.whiteBoldLabel);

                            if (Handles.Button(center, Quaternion.identity,
                                0.2f, 0.3f, Handles.SphereHandleCap))
                            {
                                _selectedRegion = i;
                                // Force inspector repaint
                                var players = Object.FindObjectsByType<PSXPlayer>(FindObjectsSortMode.None);
                                if (players.Length > 0)
                                    EditorUtility.SetDirty(players[0]);
                            }
                        }
                    }
                }
            }

            if (_previewPortals && _builder.Portals != null)
            {
                for (int i = 0; i < regions.Count; i++)
                {
                    var region = regions[i];
                    int pStart = region.portalStart;
                    int pCount = region.portalCount;

                    for (int p = pStart;
                         p < pStart + pCount && p < _builder.Portals.Count; p++)
                    {
                        var portal = _builder.Portals[p];

                        float yA = region.planeA * portal.a.x +
                                   region.planeB * portal.a.y + region.planeD;
                        float yB = region.planeA * portal.b.x +
                                   region.planeB * portal.b.y + region.planeD;

                        Vector3 worldA = new Vector3(portal.a.x, yA + 0.08f, portal.a.y);
                        Vector3 worldB = new Vector3(portal.b.x, yB + 0.08f, portal.b.y);

                        if (Mathf.Abs(portal.heightDelta) <= 0.35f)
                            Handles.color = new Color(1f, 1f, 1f, 0.9f);
                        else
                            Handles.color = new Color(1f, 0.9f, 0.2f, 0.9f);

                        Handles.DrawLine(worldA, worldB, 3f);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Custom inspector for PSXPortalLink component.
    /// </summary>
    [CustomEditor(typeof(PSXPortalLink))]
    public class PSXPortalLinkEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            PSXPortalLink portal = (PSXPortalLink)target;

            // Header card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("PSX Portal Link", PSXEditorStyles.CardHeaderStyle);

            EditorGUILayout.BeginHorizontal();
            bool valid = portal.RoomA != null && portal.RoomB != null && portal.RoomA != portal.RoomB;
            if (valid)
                PSXEditorStyles.DrawStatusBadge("Valid", PSXEditorStyles.Success, 55);
            else
                PSXEditorStyles.DrawStatusBadge("Invalid", PSXEditorStyles.Error, 60);
            EditorGUILayout.EndHorizontal();

            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Room references card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("Connected Rooms", PSXEditorStyles.CardHeaderStyle);
            PSXEditorStyles.DrawSeparator(2, 4);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("RoomA"), new GUIContent("Room A",
                "First room connected by this portal."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("RoomB"), new GUIContent("Room B",
                "Second room connected by this portal."));

            // Validation warnings
            if (portal.RoomA == null || portal.RoomB == null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Both Room A and Room B must be assigned for export.", PSXEditorStyles.InfoBox);
            }
            else if (portal.RoomA == portal.RoomB)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Room A and Room B must be different rooms.", PSXEditorStyles.InfoBox);
            }

            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Portal size card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("Portal Dimensions", PSXEditorStyles.CardHeaderStyle);
            PSXEditorStyles.DrawSeparator(2, 4);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("PortalSize"), new GUIContent("Size (W, H)",
                "Size of the portal opening (width, height) in world units."));

            PSXEditorStyles.EndCard();

            serializedObject.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Custom inspector for PSXRoom component.
    /// </summary>
    [CustomEditor(typeof(PSXRoom))]
    public class PSXRoomEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            PSXRoom room = (PSXRoom)target;

            // Header card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("PSX Room", PSXEditorStyles.CardHeaderStyle);
            if (!string.IsNullOrEmpty(room.RoomName))
                EditorGUILayout.LabelField(room.RoomName, PSXEditorStyles.RichLabel);
            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Properties card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("Room Settings", PSXEditorStyles.CardHeaderStyle);
            PSXEditorStyles.DrawSeparator(2, 4);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("RoomName"), new GUIContent("Room Name",
                "Optional display name for this room (used in editor gizmos)."));

            PSXEditorStyles.DrawSeparator(4, 4);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("VolumeSize"), new GUIContent("Volume Size",
                "Size of the room volume in local space."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("VolumeOffset"), new GUIContent("Volume Offset",
                "Offset of the volume center relative to the transform position."));

            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Info card
            PSXEditorStyles.BeginCard();
            Bounds wb = room.GetWorldBounds();
            Vector3 size = wb.size;
            EditorGUILayout.LabelField(
                $"World bounds: {size.x:F1} x {size.y:F1} x {size.z:F1}",
                PSXEditorStyles.InfoBox);
            PSXEditorStyles.EndCard();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
