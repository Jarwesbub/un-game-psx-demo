using System;
using System.Collections.Generic;
using System.Linq;
using SplashEdit.RuntimeCode;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    public enum PSXSceneType
    {
        Exterior = 0,
        Interior = 1
    }

    [ExecuteInEditMode]
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXSceneExporter.png")]
    public class PSXSceneExporter : MonoBehaviour
    {
        /// <summary>
        /// Editor code sets this delegate so the Runtime assembly can convert
        /// audio without directly referencing the Editor assembly.
        /// Signature: (AudioClip clip, int sampleRate, bool loop) => byte[] adpcm
        /// </summary>
        public static Func<AudioClip, int, bool, byte[]> AudioConvertDelegate;


        public float GTEScaling = 100.0f;
        public LuaFile SceneLuaFile;
        
        [Header("Fog & Background")]
        [Tooltip("Background clear color. Also used as the fog blend target when fog is enabled.")]
        public Color FogColor = new Color(0.5f, 0.5f, 0.6f);
        [Tooltip("Enable distance fog that blends geometry toward the background color.")]
        public bool FogEnabled = false;
        [Tooltip("Fog density (1 = light haze, 10 = pea soup).")]
        [Range(1, 10)]
        public int FogDensity = 5;
        
        [Header("Scene Type")]
        [Tooltip("Exterior uses BVH frustum culling. Interior uses room/portal occlusion.")]
        public PSXSceneType SceneType = PSXSceneType.Exterior;

        [Header("Cutscenes")]
        [Tooltip("Cutscene clips to include in this scene's splashpack. Only these will be exported.")]
        public PSXCutsceneClip[] Cutscenes = new PSXCutsceneClip[0];

        [Header("Animations")]
        [Tooltip("Animation clips to include in this scene's splashpack. Multiple can play simultaneously at runtime.")]
        public PSXAnimationClip[] Animations = new PSXAnimationClip[0];

        [Header("Loading Screen")]
        [Tooltip("Optional prefab containing a PSXCanvas to use as a loading screen when loading this scene.\n" +
                 "The canvas may contain a PSXUIProgressBar named 'loading' which will be automatically\n" +
                 "updated during scene load. If null, no loading screen is shown.")]
        public GameObject LoadingScreenPrefab;

        // Memory card save settings are global (project-wide) and live in the
        // PSXData asset; edit them in the SplashEdit Control Panel.

        private PSXObjectExporter[] _exporters;
        private TextureAtlas[] _atlases;

        // Skinned mesh export data
        private PSXSkinnedObjectExporter[] _skinnedExporters;
        private PSXSkinnedMeshExporter.BakedSkinData[] _bakedSkinData;

        // Component arrays
        private PSXInteractable[] _interactables;
        private PSXAudioClip[] _audioSources;
        private PSXTriggerBox[] _triggerBoxes;

        // ── Post-export data for memory analysis ──
        /// <summary>Texture atlases from the last export (null before first export).</summary>
        public TextureAtlas[] LastExportAtlases => _atlases;
        /// <summary>Custom font data from the last export.</summary>
        public PSXFontData[] LastExportFonts => _fonts;
        /// <summary>Audio clip ADPCM sizes from the last export.</summary>
        public long[] LastExportAudioSizes => _lastAudioSizes;
        private long[] _lastAudioSizes;
        /// <summary>Total triangle count from the last export.</summary>
        public int LastExportTriangleCount
        {
            get
            {
                if (_exporters == null) return 0;
                int count = 0;
                foreach (var exp in _exporters)
                    if (exp.Mesh != null) count += exp.Mesh.Triangles.Count;
                return count;
            }
        }
        
        // Phase 4: Nav regions
        private PSXNavRegionBuilder _navRegionBuilder;
        
        // Phase 5: Room/portal system (interior scenes)
        private PSXRoomBuilder _roomBuilder;

        // Phase 6: UI canvases
        private PSXCanvasData[] _canvases;
        private PSXFontData[] _fonts;

        private PSXData _psxData;

        private Vector2 selectedResolution;
        private bool dualBuffering;
        private bool verticalLayout;
        private List<ProhibitedArea> prohibitedAreas;

        private Vector3 _playerPos;
        private Quaternion _playerRot;
        private float _playerHeight;
        private float _playerRadius;
        private float _moveSpeed;
        private float _sprintSpeed;
        private float _jumpHeight;
        private float _gravity;

        private BVH _bvh;

        public bool PreviewBVH = true;
        public bool PreviewRoomsPortals = true;

        public int BVHPreviewDepth = 9999;

        /// <summary>
        /// Export with a file dialog (legacy workflow).
        /// </summary>
        public void Export()
        {
            ExportToPath(null);
        }

        /// <summary>
        /// Export to the given file path. If path is null, shows a file dialog.
        /// Called by the Control Panel pipeline for automated exports.
        /// </summary>
        public void ExportToPath(string outputPath)
        {
#if UNITY_EDITOR
            _psxData = DataStorage.LoadData(out selectedResolution, out dualBuffering, out verticalLayout, out prohibitedAreas);

            // Destroy any stale proxy objects from previous exports that may have leaked
            // (e.g. if a prior export threw an exception before cleanup, or the proxy used
            //  non-DontSave HideFlags and got saved into the scene).
            foreach (var staleExp in FindObjectsByType<PSXObjectExporter>(FindObjectsSortMode.None))
            {
                if (staleExp.gameObject.hideFlags != HideFlags.None)
                    DestroyImmediate(staleExp.gameObject);
            }

            // Discover and prepare skinned mesh exporters first (creates proxy GameObjects
            // with PSXObjectExporter that will be picked up by the FindObjectsByType below).
            _skinnedExporters = FindObjectsByType<PSXSkinnedObjectExporter>(FindObjectsSortMode.None);

            // Auto-switch Generic models to Humanoid if they have Humanoid animation clips.
            // This MUST run before CreateProxy so the SkinnedMeshRenderer's mesh/bones are
            // correct when the proxy is built and CreatePSXMesh later processes it.
            foreach (var skinExp in _skinnedExporters)
                PSXSkinnedMeshExporter.EnsureHumanoidImportIfNeeded(skinExp);

            foreach (var skinExp in _skinnedExporters)
                PSXSkinnedMeshExporter.CreateProxy(skinExp);

            // Build exporters list. Collect the set of proxy exporters so we can
            // deduplicate: some Unity versions include DontSave objects in
            // FindObjectsByType, others don't. We filter them out, then add back
            // exactly once at the end.
            // Also exclude any PSXObjectExporter that lives on or under a
            // PSXSkinnedObjectExporter — only the proxy should represent those meshes.
            var proxySet = new System.Collections.Generic.HashSet<PSXObjectExporter>();
            foreach (var skinExp in _skinnedExporters)
            {
                if (skinExp.ProxyExporter != null)
                    proxySet.Add(skinExp.ProxyExporter);
            }
            var skinnedGOs = new System.Collections.Generic.HashSet<GameObject>();
            foreach (var skinExp in _skinnedExporters)
                skinnedGOs.Add(skinExp.gameObject);

            var exportersList = new System.Collections.Generic.List<PSXObjectExporter>();
            foreach (var exp in FindObjectsByType<PSXObjectExporter>(FindObjectsSortMode.None))
            {
                if (proxySet.Contains(exp)) continue; // skip proxy (we add them at the end)

                // Skip any exporter that sits on or under a skinned-object hierarchy
                bool underSkinned = false;
                Transform t = exp.transform;
                while (t != null)
                {
                    if (skinnedGOs.Contains(t.gameObject)) { underSkinned = true; break; }
                    t = t.parent;
                }
                if (underSkinned)
                {
                    Debug.Log($"[Export] Skipping PSXObjectExporter on '{exp.name}' — covered by PSXSkinnedObjectExporter proxy");
                    continue;
                }

                exportersList.Add(exp);
            }
            // Append proxies at the end — guaranteed exactly once
            exportersList.AddRange(proxySet);
            _exporters = exportersList.ToArray();
            try
            {
            for (int i = 0; i < _exporters.Length; i++)
            {
                PSXObjectExporter exp = _exporters[i];
                EditorUtility.DisplayProgressBar($"{nameof(PSXSceneExporter)}", $"Export {nameof(PSXObjectExporter)}", ((float)i) / _exporters.Length);
                exp.CreatePSXTextures2D();
                exp.CreatePSXMesh(GTEScaling);
            }
            
            // Collect components
            _interactables = FindObjectsByType<PSXInteractable>(FindObjectsSortMode.None);
            _audioSources = FindObjectsByType<PSXAudioClip>(FindObjectsSortMode.None);
            _triggerBoxes = FindObjectsByType<PSXTriggerBox>(FindObjectsSortMode.None);

            // Collect UI image textures for VRAM packing alongside 3D textures
            PSXUIImage[] uiImages = FindObjectsByType<PSXUIImage>(FindObjectsSortMode.None);
            List<PSXTexture2D> uiTextures = new List<PSXTexture2D>();
            foreach (PSXUIImage img in uiImages)
            {
                if (img.SourceTexture != null)
                {
                    Utils.SetTextureImporterFormat(img.SourceTexture, true);
                    PSXTexture2D tex = PSXTexture2D.CreateFromTexture2D(img.SourceTexture, img.BitDepth);
                    tex.OriginalTexture = img.SourceTexture;
                    img.PackedTexture = tex;
                    uiTextures.Add(tex);
                }
            }

            EditorUtility.ClearProgressBar();

            PackTextures(uiTextures);

            // Collect UI canvases after VRAM packing (so PSXUIImage.PackedTexture has valid VRAM coords)
            _canvases = PSXUIExporter.CollectCanvases(selectedResolution, out _fonts);

            PSXPlayer player = FindObjectsByType<PSXPlayer>(FindObjectsSortMode.None).FirstOrDefault();
            if (player != null)
            {
                player.FindNavmesh();
                _playerPos = player.CamPoint;
                _playerHeight = player.PlayerHeight;
                _playerRadius = player.PlayerRadius;
                _moveSpeed = player.MoveSpeed;
                _sprintSpeed = player.SprintSpeed;
                _jumpHeight = player.JumpHeight;
                _gravity = player.Gravity;
                _playerRot = player.transform.rotation;
            }

            _bvh = new BVH(_exporters.ToList());
            _bvh.Build();

            // Phase 4+5: Room volumes are needed by BOTH the nav region builder
            // (for spatial room assignment) and the room builder (for triangle assignment).
            // Collect them early so both systems use the same room indices.
            PSXRoom[] rooms = null;
            PSXPortalLink[] portalLinks = null;
            if (SceneType == PSXSceneType.Interior)
            {
                rooms = FindObjectsByType<PSXRoom>(FindObjectsSortMode.None);
                portalLinks = FindObjectsByType<PSXPortalLink>(FindObjectsSortMode.None);
            }

            // Phase 4: Build nav regions
            _navRegionBuilder = new PSXNavRegionBuilder();
            _navRegionBuilder.AgentRadius = _playerRadius;
            _navRegionBuilder.AgentHeight = _playerHeight;
            if (player != null)
            {
                _navRegionBuilder.MaxStepHeight = player.MaxStepHeight;
                _navRegionBuilder.WalkableSlopeAngle = player.WalkableSlopeAngle;
                _navRegionBuilder.CellSize = player.NavCellSize;
                _navRegionBuilder.CellHeight = player.NavCellHeight;
                _navRegionBuilder.MinRegionArea = player.NavMinRegionArea;
                _navRegionBuilder.MergeRegionArea = player.NavMergeRegionArea;
                _navRegionBuilder.MaxSimplifyError = player.NavMaxSimplifyError;
                _navRegionBuilder.MaxEdgeLength = player.NavMaxEdgeLength;
                _navRegionBuilder.PartitionMethod = player.NavPartitionMethod;
                _navRegionBuilder.DetailSampleDist = player.NavDetailSampleDist;
                _navRegionBuilder.DetailMaxError = player.NavDetailMaxError;
                _navRegionBuilder.MaxPlaneError = player.NavMaxPlaneError;
            }
            // Pass PSXRoom volumes so nav regions get spatial room assignment
            // instead of BFS connectivity. This ensures nav region roomIndex
            // matches the PSXRoomBuilder room indices used by the renderer.
            if (rooms != null && rooms.Length > 0)
                _navRegionBuilder.PSXRooms = rooms;

            // Collect platform exporters and walkoff zones for nav region flagging
            var platformList = new System.Collections.Generic.List<PSXObjectExporter>();
            foreach (var exp in _exporters)
            {
                if (exp.IsPlatform)
                    platformList.Add(exp);
            }
            if (platformList.Count > 0)
                _navRegionBuilder.PlatformExporters = platformList.ToArray();

            PSXNavWalkoffZone[] walkoffZones = FindObjectsByType<PSXNavWalkoffZone>(FindObjectsSortMode.None);
            if (walkoffZones != null && walkoffZones.Length > 0)
                _navRegionBuilder.WalkoffZones = walkoffZones;

            _navRegionBuilder.Build(_exporters, _playerPos);
            if (_navRegionBuilder.RegionCount == 0)
                Debug.LogWarning("No nav regions! Enable 'Generate Navigation' on your floor meshes.");

            // Phase 5: Build room/portal system (for interior scenes)
            _roomBuilder = new PSXRoomBuilder();
            if (SceneType == PSXSceneType.Interior)
            {
                if (rooms != null && rooms.Length > 0)
                {
                    _roomBuilder.Build(rooms, portalLinks, _exporters, GTEScaling);
                    if (portalLinks == null || portalLinks.Length == 0)
                        Debug.LogWarning("Interior scene has rooms but no PSXPortalLink components! " +
                                         "Place PSXPortalLink objects between rooms for portal culling.");
                }
                else
                {
                    Debug.LogWarning("Interior scene type but no PSXRoom volumes found! Place PSXRoom components.");
                }
            }

            // Phase 6: Bake skinned mesh data
            if (_skinnedExporters != null && _skinnedExporters.Length > 0)
            {
                Debug.Log($"[SkinExport] Found {_skinnedExporters.Length} PSXSkinnedObjectExporter(s), total exporters={_exporters.Length}");
                // Record which exporter index each skinned mesh maps to
                var skinDataList = new System.Collections.Generic.List<PSXSkinnedMeshExporter.BakedSkinData>();
                foreach (var skinExp in _skinnedExporters)
                {
                    if (skinExp.ProxyExporter == null)
                    {
                        Debug.LogWarning($"[SkinExport] Skinned exporter '{skinExp.name}' has no ProxyExporter, skipping!");
                        continue;
                    }
                    Debug.Log($"[SkinExport] Baking skin data for '{skinExp.name}' (proxy='{skinExp.ProxyExporter.name}')");
                    var baked = PSXSkinnedMeshExporter.BakeSkinData(skinExp, _exporters, GTEScaling);
                    if (baked != null)
                    {
                        skinDataList.Add(baked);
                        Debug.Log($"[SkinExport] Baked OK: GOIndex={baked.GameObjectIndex}, bones={baked.BoneCount}, clips={baked.Clips.Count}, boneIndicesLen={baked.BoneIndices?.Length ?? 0}");
                    }
                    else
                    {
                        Debug.LogError($"[SkinExport] BakeSkinData returned null for '{skinExp.name}'!");
                    }
                }
                _bakedSkinData = skinDataList.ToArray();
                Debug.Log($"[SkinExport] Total baked skin data entries: {_bakedSkinData.Length}");
            }
            else
            {
                _bakedSkinData = null;
            }

            ExportFile(outputPath);
        }
        finally
        {
            // Always clean up skinned mesh proxies, even if export threw an exception
            if (_skinnedExporters != null)
            {
                foreach (var skinExp in _skinnedExporters)
                    PSXSkinnedMeshExporter.DestroyProxy(skinExp);
            }
        }
#endif
        }

        void PackTextures(List<PSXTexture2D> additionalTextures = null)
        {
            (Rect buffer1, Rect buffer2) = Utils.BufferForResolution(selectedResolution, verticalLayout);

            List<Rect> framebuffers = new List<Rect> { buffer1 };
            if (dualBuffering)
            {
                framebuffers.Add(buffer2);
            }

            VRAMPacker tp = new VRAMPacker(framebuffers, prohibitedAreas);
            var packed = tp.PackTexturesIntoVRAM(_exporters, additionalTextures);
            _exporters = packed.processedObjects;
            _atlases = packed.atlases;

        }

        void ExportFile(string outputPath = null)
        {
#if UNITY_EDITOR
            string path = outputPath;
            if (string.IsNullOrEmpty(path))
                path = EditorUtility.SaveFilePanel("Select Output File", "", "output", "bin");
            if (string.IsNullOrEmpty(path))
                return;

            // Convert audio clips to ADPCM (Editor-only, before passing to Runtime writer)
            AudioClipExport[] audioExports = null;
            if (_audioSources != null && _audioSources.Length > 0)
            {
                var list = new List<AudioClipExport>();
                foreach (var src in _audioSources)
                {
                    if (src.Clip != null)
                    {
                        if (AudioConvertDelegate == null)
                            throw new InvalidOperationException("AudioConvertDelegate not set. Ensure PSXAudioConverter registers it.");
                        byte[] adpcm = AudioConvertDelegate(src.Clip, src.SampleRate, src.Loop);
                        list.Add(new AudioClipExport { adpcmData = adpcm, sampleRate = src.SampleRate, loop = src.Loop, clipName = src.ClipName });
                    }
                    else
                    {
                        Debug.LogWarning($"Audio source on {src.gameObject.name} has no clip assigned.");
                        list.Add(new AudioClipExport { adpcmData = null, sampleRate = src.SampleRate, loop = src.Loop, clipName = src.ClipName });
                    }
                }
                audioExports = list.ToArray();
            }

            // Cache audio sizes for memory analysis
            if (audioExports != null)
            {
                _lastAudioSizes = new long[audioExports.Length];
                for (int i = 0; i < audioExports.Length; i++)
                    _lastAudioSizes[i] = audioExports[i].adpcmData != null ? audioExports[i].adpcmData.Length : 0;
            }
            else
            {
                _lastAudioSizes = null;
            }

            var scene = new PSXSceneWriter.SceneData
            {
                exporters = _exporters,
                atlases = _atlases,
                interactables = _interactables,
                audioClips = audioExports,
                navRegionBuilder = _navRegionBuilder,
                roomBuilder = _roomBuilder,
                bvh = _bvh,
                sceneLuaFile = SceneLuaFile,
                gteScaling = GTEScaling,
                playerPos = _playerPos,
                playerRot = _playerRot,
                playerHeight = _playerHeight,
                playerRadius = _playerRadius,
                moveSpeed = _moveSpeed,
                sprintSpeed = _sprintSpeed,
                jumpHeight = _jumpHeight,
                gravity = _gravity,
                sceneType = SceneType,
                fogEnabled = FogEnabled,
                fogColor = FogColor,
                fogDensity = FogDensity,
                cutscenes = Cutscenes,
                animations = Animations,
                audioSources = _audioSources,
                canvases = _canvases,
                fonts = _fonts,
                triggerBoxes = _triggerBoxes,
                bakedSkinData = _bakedSkinData,
                skinnedExporters = _skinnedExporters,
                memCardEnabled = _psxData != null && _psxData.MemCardEnabled,
                memCardRegion = _psxData != null ? _psxData.MemCardRegion : "BA",
                memCardProduct = _psxData != null ? _psxData.MemCardProduct : "SLUS-00000",
                memCardTitle = _psxData != null ? _psxData.MemCardTitle : "PSXSPLASH SAVE",
                memCardIcons = _psxData != null ? _psxData.MemCardIcons : null,
            };

            PSXSceneWriter.Write(path, in scene, (msg, type) =>
            {
                switch (type)
                {
                    case LogType.Error:   Debug.LogError(msg);   break;
                    case LogType.Warning: Debug.LogWarning(msg); break;
                    default:              Debug.Log(msg);        break;
                }
            });
#endif
        }

        void OnDrawGizmos()
        {
            Vector3 sceneOrigin = new Vector3(0, 0, 0);
            Vector3 cubeSize = new Vector3(8.0f * GTEScaling, 8.0f * GTEScaling, 8.0f * GTEScaling);
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(sceneOrigin, cubeSize);

            if (_bvh == null || !PreviewBVH) return;
            _bvh.DrawGizmos(BVHPreviewDepth);
        }

    }
}
