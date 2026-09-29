using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Handles baking bone matrices and writing skin data to the splashpack binary.
    /// All skeleton hierarchy math happens here at export time. The PS1 runtime
    /// only indexes flat arrays of pre-composed bone matrices.
    /// </summary>
    public static class PSXSkinnedMeshExporter
    {
        private const int MAX_SKINNED_MESHES = 16;
        private const int MAX_BONES = 64;
        private const int MAX_CLIPS = 16;
        // No hard frame cap. frameCount is uint16 in the binary.
        private const int MAX_NAME_LEN = 24;

        /// <summary>
        /// Per-bone baked matrix for a single frame: 3x3 rotation (4.12 fp) + translation (int16 model units).
        /// Matches the C++ BakedBoneMatrix struct (24 bytes).
        /// </summary>
        public struct BakedBoneMatrix
        {
            public short r00, r01, r02;
            public short r10, r11, r12;
            public short r20, r21, r22;
            public short tx, ty, tz;
        }

        /// <summary>
        /// Baked result for one animation clip on one skinned mesh.
        /// </summary>
        public class BakedClipData
        {
            public string ClipName;
            public int FrameCount;
            public int Fps;
            public bool Loop;
            /// <summary>frames[frame * boneCount + boneIndex]</summary>
            public BakedBoneMatrix[] Frames;
        }

        /// <summary>
        /// Complete baked data for one skinned mesh.
        /// </summary>
        public class BakedSkinData
        {
            public int GameObjectIndex; // index into the combined exporters array
            public int BoneCount;
            public byte[] BoneIndices;  // per-triangle vertex bone indices: [triIdx*3+vertIdx]
            public List<BakedClipData> Clips = new List<BakedClipData>();
        }

        // ════════════════════════════════════════════════════════════
        // Proxy creation
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a proxy GameObject with MeshFilter + MeshRenderer + PSXObjectExporter
        /// from a PSXSkinnedObjectExporter's SkinnedMeshRenderer.
        /// The proxy uses the bind-pose mesh and is picked up by the normal export pipeline.
        /// </summary>
        public static void CreateProxy(PSXSkinnedObjectExporter skinExp)
        {
#if UNITY_EDITOR
            var smr = skinExp.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null || smr.sharedMesh == null) return;

            // Create proxy as a temporary sibling (not child, to avoid inheriting SkinnedMeshRenderer influence)
            // Use the original object name so the PS1 runtime can look it up by name (e.g. SkinnedAnim.Play)
            var proxyGO = new GameObject(skinExp.name);
            proxyGO.hideFlags = HideFlags.DontSave;
            proxyGO.transform.SetPositionAndRotation(skinExp.transform.position, skinExp.transform.rotation);
            proxyGO.transform.localScale = skinExp.transform.lossyScale;

            // For BakedLighting, bake lighting from the current pose into vertex colors
            // on a copy of the bind-pose mesh.
            Mesh proxyMesh = smr.sharedMesh;
            VertexColorMode effectiveColorMode = skinExp.ColorMode;

            if (skinExp.ColorMode == VertexColorMode.BakedLighting)
            {
                var bakedMesh = new Mesh();
                smr.BakeMesh(bakedMesh);

                // Ensure we have normals on the baked mesh
                if (bakedMesh.normals == null || bakedMesh.normals.Length == 0)
                    bakedMesh.RecalculateNormals();

                Vector3[] normalsForLighting = skinExp.SmoothNormals
                    ? PSXMesh.RecalculateSmoothNormals(bakedMesh)
                    : bakedMesh.normals;
                Vector3[] bakedVerts = bakedMesh.vertices;

                Light[] sceneLights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                // Filter to enabled lights
                var enabledLights = new System.Collections.Generic.List<Light>();
                foreach (var l in sceneLights)
                    if (l.enabled) enabledLights.Add(l);
                Light[] lights = enabledLights.ToArray();

                // Compute per-vertex lighting from the current-pose world normals/positions
                var colors = new Color[bakedVerts.Length];
                for (int i = 0; i < bakedVerts.Length; i++)
                {
                    Vector3 worldPos = smr.transform.TransformPoint(bakedVerts[i]);
                    Vector3 worldNormal = smr.transform.TransformDirection(normalsForLighting[i]).normalized;
                    colors[i] = PSXLightingBaker.ComputeLighting(worldPos, worldNormal, lights);
                }

                // Create a copy of the bind-pose mesh with baked lighting as vertex colors
                proxyMesh = UnityEngine.Object.Instantiate(smr.sharedMesh);
                proxyMesh.colors = colors;
                effectiveColorMode = VertexColorMode.MeshVertexColors;

                UnityEngine.Object.DestroyImmediate(bakedMesh);
            }

            proxyGO.AddComponent<MeshFilter>().sharedMesh = proxyMesh;
            var mr = proxyGO.AddComponent<MeshRenderer>();
            mr.sharedMaterials = smr.sharedMaterials;

            // Add PSXObjectExporter with matching settings via reflection
            var exp = proxyGO.AddComponent<PSXObjectExporter>();
            SetPrivateField(exp, "isActive", skinExp.IsActive);
            SetPrivateField(exp, "bitDepth", skinExp.BitDepth);
            SetPrivateField(exp, "luaFile", skinExp.LuaFile);
            SetPrivateField(exp, "vertexColorMode", effectiveColorMode);
            SetPrivateField(exp, "flatVertexColor", skinExp.FlatVertexColor);
            SetPrivateField(exp, "smoothNormals", skinExp.SmoothNormals);

            skinExp.ProxyExporter = exp;
            skinExp.ProxyGameObject = proxyGO;
#endif
        }

        /// <summary>
        /// Destroys the proxy GameObject created by CreateProxy.
        /// </summary>
        public static void DestroyProxy(PSXSkinnedObjectExporter skinExp)
        {
            if (skinExp.ProxyGameObject != null)
            {
                // If we created an instantiated mesh copy (for baked lighting), destroy it too
                var mf = skinExp.ProxyGameObject.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var smr = skinExp.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (smr != null && mf.sharedMesh != smr.sharedMesh)
                        UnityEngine.Object.DestroyImmediate(mf.sharedMesh);
                }
                UnityEngine.Object.DestroyImmediate(skinExp.ProxyGameObject);
                skinExp.ProxyGameObject = null;
                skinExp.ProxyExporter = null;
            }
        }

        private static void SetPrivateField(object obj, string fieldName, object value)
        {
            var field = obj.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null) field.SetValue(obj, value);
        }

        // ════════════════════════════════════════════════════════════
        // Humanoid import auto-detection
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Checks whether any assigned animation clip on this skinned exporter is a
        /// Humanoid muscle clip (no bone Transform curves).  If so, and the source
        /// model is NOT imported as Humanoid, automatically switches the import type
        /// to Humanoid and reimports.  This must run BEFORE proxy creation so that
        /// the SkinnedMeshRenderer's mesh / bones / bindposes are correct when the
        /// proxy is built and CreatePSXMesh runs.
        /// </summary>
        public static void EnsureHumanoidImportIfNeeded(PSXSkinnedObjectExporter skinExp)
        {
#if UNITY_EDITOR
            if (skinExp == null || skinExp.AnimationClips == null || skinExp.AnimationClips.Length == 0)
                return;

            var smr = skinExp.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null || smr.sharedMesh == null) return;

            string assetPath = AssetDatabase.GetAssetPath(smr.sharedMesh);
            if (string.IsNullOrEmpty(assetPath)) return;

            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null) return;

            // Already Humanoid, nothing to do
            if (importer.animationType == ModelImporterAnimationType.Human)
            {
                Debug.Log($"[SkinBake] Model '{System.IO.Path.GetFileName(assetPath)}' is already Humanoid.");
                return;
            }

            // Check whether any clip is Humanoid (no Transform curves)
            bool hasHumanoidClip = false;
            foreach (var clip in skinExp.AnimationClips)
            {
                if (clip == null) continue;
                bool hasTransformCurves = false;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type == typeof(Transform))
                    {
                        hasTransformCurves = true;
                        break;
                    }
                }
                if (!hasTransformCurves)
                {
                    hasHumanoidClip = true;
                    Debug.Log($"[SkinBake] Clip '{clip.name}' has NO Transform curves, Humanoid muscle clip detected.");
                    break;
                }
            }

            if (!hasHumanoidClip) return;

            Debug.LogWarning($"[SkinBake] Model '{System.IO.Path.GetFileName(assetPath)}' is imported as " +
                             $"{importer.animationType} but uses Humanoid animation clips. " +
                             $"Auto-switching to Humanoid and reimporting...");

            importer.animationType = ModelImporterAnimationType.Human;
            importer.SaveAndReimport();

            // Verify the reimport produced a valid Humanoid Avatar
            var subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            bool foundHumanAvatar = false;
            foreach (var sub in subAssets)
            {
                if (sub is Avatar avatar && avatar.isHuman && avatar.isValid)
                {
                    foundHumanAvatar = true;
                    Debug.Log($"[SkinBake] Humanoid reimport successful, Avatar '{avatar.name}' isHuman=true isValid=true.");
                    break;
                }
            }

            if (!foundHumanAvatar)
            {
                Debug.LogError($"[SkinBake] Humanoid reimport did NOT produce a valid Humanoid Avatar! " +
                               $"Unity could not auto-map bones. Please configure the bone mapping manually " +
                               $"in the model's import settings (Rig tab > Animation Type: Humanoid > Configure).");
            }
#endif
        }

        // ════════════════════════════════════════════════════════════
        // Bone baking
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Bake all animation clips for a PSXSkinnedObjectExporter.
        /// Returns bone indices (per-triangle-vertex) and per-clip frame data.
        /// </summary>
        public static BakedSkinData BakeSkinData(
            PSXSkinnedObjectExporter skinExp,
            PSXObjectExporter[] allExporters,
            float gteScaling)
        {
#if UNITY_EDITOR
            Debug.Log($"[SkinBake] === BakeSkinData START for '{skinExp.name}' ===");

            var smr = skinExp.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null || smr.sharedMesh == null)
            {
                Debug.LogError($"[SkinBake] No SkinnedMeshRenderer or sharedMesh on '{skinExp.name}'!");
                return null;
            }

            Mesh mesh = smr.sharedMesh;
            Transform[] bones = smr.bones;
            Matrix4x4[] bindPoses = mesh.bindposes;
            int boneCount = Mathf.Min(bones.Length, MAX_BONES);

            Debug.Log($"[SkinBake] SMR mesh='{mesh.name}', bones={bones.Length}, bindPoses={bindPoses.Length}, boneCount(clamped)={boneCount}");
            Debug.Log($"[SkinBake] SMR rootBone={smr.rootBone?.name ?? "null"}, sharedMaterials={smr.sharedMaterials?.Length ?? 0}");

            // Find the exporter index for this skinned mesh
            int exporterIndex = -1;
            if (skinExp.ProxyExporter != null)
            {
                for (int i = 0; i < allExporters.Length; i++)
                {
                    if (allExporters[i] == skinExp.ProxyExporter)
                    {
                        exporterIndex = i;
                        break;
                    }
                }
            }
            if (exporterIndex < 0)
            {
                Debug.LogError($"[SkinBake] Could not find proxy exporter index for '{skinExp.name}'! ProxyExporter={(skinExp.ProxyExporter != null ? skinExp.ProxyExporter.name : "null")}");
                return null;
            }
            Debug.Log($"[SkinBake] ProxyExporter found at index {exporterIndex}, proxy name='{skinExp.ProxyExporter.name}'");

            var result = new BakedSkinData
            {
                GameObjectIndex = exporterIndex,
                BoneCount = boneCount,
            };

            // ── Compute per-vertex bone indices (hard skinning: highest weight wins) ──
            result.BoneIndices = ComputePerTriangleBoneIndices(mesh, smr);

            // ── Bake animation clips ──
            // Capture objectInverse and uniformScale ONCE before sampling.
            // This must be fixed so that bone world-space movement (root motion)
            // is captured relative to the original object position.
            Matrix4x4 objectInverse = skinExp.transform.worldToLocalMatrix;
            float uniformScale = skinExp.transform.lossyScale.x;
            Debug.Log($"[SkinBake] objectTransform pos={skinExp.transform.position}, rot={skinExp.transform.rotation.eulerAngles}, lossyScale={skinExp.transform.lossyScale}");
            Debug.Log($"[SkinBake] gteScaling={gteScaling}");
            Debug.Log($"[SkinBake] AnimationClips count={skinExp.AnimationClips?.Length ?? 0}, TargetFPS={skinExp.TargetFPS}");

            // ── Find or create an Animator on the correct bone hierarchy root ──
            // The Avatar's bone paths (e.g. "Armature/hips") are relative to the
            // FBX root transform.  skinExp may be a child/sibling of that root,
            // NOT the root itself.  We probe each ancestor of rootBone with a
            // temporary Animator+Avatar to find the one where GetBoneTransform
            // succeeds, that is the FBX root where bone paths resolve correctly.
            Transform boneHierarchyRoot = skinExp.transform; // fallback

            // Load the Avatar from the model asset
            Avatar modelAvatar = null;
            string meshAssetPath = AssetDatabase.GetAssetPath(smr.sharedMesh);
            if (!string.IsNullOrEmpty(meshAssetPath))
            {
                foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(meshAssetPath))
                {
                    if (sub is Avatar a) { modelAvatar = a; break; }
                }
            }

            if (smr.rootBone != null && modelAvatar != null && modelAvatar.isHuman)
            {
                // Humanoid: probe each ancestor of rootBone to find the transform
                // where Avatar bone paths resolve correctly.
                Debug.Log($"[SkinBake] Probing ancestors of rootBone '{smr.rootBone.name}' for correct Animator placement...");
                Transform candidate = smr.rootBone.parent;
                while (candidate != null)
                {
                    // Check if there's already an Animator here, use it instead of adding a duplicate
                    Animator existingAnim = candidate.GetComponent<Animator>();
                    Animator probe = null;
                    bool addedProbe = false;
                    try
                    {
                        if (existingAnim != null)
                        {
                            probe = existingAnim;
                            var savedAvatar = probe.avatar;
                            probe.avatar = modelAvatar;
                            probe.Rebind();
                            bool ok = (probe.GetBoneTransform(HumanBodyBones.Hips) != null);
                            probe.avatar = savedAvatar; // restore
                            if (ok)
                            {
                                boneHierarchyRoot = candidate;
                                Debug.Log($"[SkinBake] Found correct Animator root via existing Animator probe: '{candidate.name}'");
                                break;
                            }
                        }
                        else
                        {
                            probe = candidate.gameObject.AddComponent<Animator>();
                            addedProbe = true;
                            probe.avatar = modelAvatar;
                            probe.Rebind();
                            bool ok = (probe.GetBoneTransform(HumanBodyBones.Hips) != null);
                            if (ok)
                            {
                                boneHierarchyRoot = candidate;
                                Debug.Log($"[SkinBake] Found correct Animator root via Avatar probe: '{candidate.name}'");
                                break;
                            }
                        }
                    }
                    catch (System.Exception probeEx)
                    {
                        Debug.Log($"[SkinBake]   probe '{candidate.name}' threw: {probeEx.Message}");
                    }
                    finally
                    {
                        if (addedProbe && probe != null)
                            UnityEngine.Object.DestroyImmediate(probe);
                    }
                    Debug.Log($"[SkinBake]   probe '{candidate.name}' GetBoneTransform(Hips)=null, trying parent...");
                    candidate = candidate.parent;
                }
                if (boneHierarchyRoot == skinExp.transform)
                    Debug.LogWarning($"[SkinBake] Avatar probe failed to find a working root! Falling back to '{skinExp.name}'");
            }
            else if (smr.rootBone != null)
            {
                // Non-humanoid: walk up to common ancestor of rootBone and SMR
                Transform t = smr.rootBone;
                while (t.parent != null)
                {
                    t = t.parent;
                    if (smr.transform.IsChildOf(t)) break;
                }
                boneHierarchyRoot = t;
            }
            Debug.Log($"[SkinBake] Bone hierarchy root: '{boneHierarchyRoot.name}' (skinExp='{skinExp.name}', rootBone='{smr.rootBone?.name ?? "null"}', smr='{smr.transform.name}')");

            // Search for an existing Animator, check both skinExp descendants
            // AND the bone hierarchy root (which may be a parent/sibling of skinExp).
            Animator animator = skinExp.GetComponentInChildren<Animator>();
            if (animator == null && boneHierarchyRoot != skinExp.transform)
                animator = boneHierarchyRoot.GetComponentInChildren<Animator>();
            bool addedAnimator = false;
            if (animator == null)
            {
                animator = boneHierarchyRoot.gameObject.AddComponent<Animator>();
                addedAnimator = true;
                Debug.Log($"[SkinBake] Added temporary Animator to '{boneHierarchyRoot.name}' (parent='{boneHierarchyRoot.parent?.name}')");
            }
            else
            {
                Debug.Log($"[SkinBake] Found existing Animator on '{animator.gameObject.name}'. avatar={animator.avatar?.name ?? "null"}, isHuman={animator.avatar?.isHuman ?? false}, isValid={animator.avatar?.isValid ?? false}");
            }
            // Ensure the Animator has a valid Avatar from the SkinnedMeshRenderer's
            // root bone hierarchy. This is required for Humanoid muscle clips.
            if (animator.avatar == null && modelAvatar != null)
            {
                animator.avatar = modelAvatar;
                Debug.Log($"[SkinBake] Assigned avatar '{modelAvatar.name}' isHuman={modelAvatar.isHuman} isValid={modelAvatar.isValid}");
            }
            else if (animator.avatar == null)
            {
                Debug.Log($"[SkinBake] Animator has no avatar, searching model importer...");
                string assetPath = AssetDatabase.GetAssetPath(smr.sharedMesh);
                Debug.Log($"[SkinBake] Mesh asset path: '{assetPath}'");
                if (!string.IsNullOrEmpty(assetPath))
                {
                    var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                    Debug.Log($"[SkinBake] ModelImporter found={importer != null}, animationType={importer?.animationType}");
                    if (importer != null)
                    {
                        var subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                        foreach (var sub in subAssets)
                        {
                            if (sub is Avatar avatar)
                            {
                                animator.avatar = avatar;
                                Debug.Log($"[SkinBake] Assigned avatar '{avatar.name}' isHuman={avatar.isHuman} isValid={avatar.isValid}");
                                break;
                            }
                        }
                    }
                }
                if (animator.avatar == null)
                    Debug.LogWarning($"[SkinBake] Could NOT find any Avatar for '{skinExp.name}'! Humanoid clips will NOT retarget.");
            }
            else
            {
                Debug.Log($"[SkinBake] Animator already has avatar '{animator.avatar.name}' isHuman={animator.avatar.isHuman} isValid={animator.avatar.isValid}");
            }

            // Force the Animator to (re)bind its internal bone references.
            // Without this, a freshly-added Animator or one whose avatar was
            // just changed won't know which transforms correspond to which
            // Humanoid bones, and Playables/AnimationMode won't drive anything.
            animator.Rebind();
            Debug.Log($"[SkinBake] Animator.Rebind() on '{animator.gameObject.name}', avatar={animator.avatar?.name}, isHuman={animator.avatar?.isHuman}");

            foreach (var clip in skinExp.AnimationClips)
            {
                if (clip == null)
                {
                    Debug.LogWarning($"[SkinBake] Null clip in AnimationClips array, skipping");
                    continue;
                }
                if (result.Clips.Count >= MAX_CLIPS) break;

                Debug.Log($"[SkinBake] --- Baking clip '{clip.name}' ---");
                Debug.Log($"[SkinBake]   clip.length={clip.length}s, clip.isLooping={clip.isLooping}, clip.legacy={clip.legacy}, clip.wrapMode={clip.wrapMode}");
                Debug.Log($"[SkinBake]   clip.isHumanMotion={clip.isHumanMotion}, clip.humanMotion={clip.humanMotion}");

                int fps = skinExp.TargetFPS;
                int frameCount = Mathf.CeilToInt(clip.length * fps) + 1;
                Debug.Log($"[SkinBake]   fps={fps}, frameCount={frameCount}");

                var bakedClip = new BakedClipData
                {
                    ClipName = clip.name,
                    Fps = fps,
                    FrameCount = frameCount,
                    Loop = clip.isLooping,
                    Frames = new BakedBoneMatrix[frameCount * boneCount],
                };

                // Determine whether this clip has bone transform curves (Generic/Legacy)
                // or only Humanoid muscle curves.
                bool hasTransformCurves = false;
                var bindings = AnimationUtility.GetCurveBindings(clip);
                int transformBindingCount = 0;
                int totalBindingCount = bindings.Length;
                foreach (var binding in bindings)
                {
                    if (binding.type == typeof(Transform))
                    {
                        hasTransformCurves = true;
                        transformBindingCount++;
                    }
                }
                Debug.Log($"[SkinBake]   CurveBindings: total={totalBindingCount}, Transform={transformBindingCount}, hasTransformCurves={hasTransformCurves}");
                if (totalBindingCount > 0 && !hasTransformCurves)
                {
                    // Log what binding types we DO have
                    var typeSet = new System.Collections.Generic.HashSet<string>();
                    foreach (var binding in bindings)
                    {
                        string key = $"{binding.type.Name}:{binding.propertyName}";
                        if (typeSet.Count < 10) typeSet.Add(key);
                    }
                    Debug.Log($"[SkinBake]   Non-Transform bindings (first 10): {string.Join(", ", typeSet)}");
                }

                // Use SampleAnimation only for truly legacy clips (no Avatar).
                // Generic clips need AnimationMode + Animator for correct bone paths.
                bool useLegacyPath = hasTransformCurves && modelAvatar == null;

                if (useLegacyPath)
                {
                    // True Legacy clip (no Avatar): SampleAnimation works directly.
                    // No per-frame transform restore -- root motion must be preserved.
                    Debug.Log($"[SkinBake]   Using TRUE LEGACY path (SampleAnimation, no Avatar)");
                    bool wasLegacy = clip.legacy;
                    clip.legacy = true;

                    try
                    {
                        for (int frame = 0; frame < frameCount; frame++)
                        {
                            float time = (frameCount > 1) ? (frame / (float)(frameCount - 1)) * clip.length : 0f;
                            clip.SampleAnimation(skinExp.gameObject, time);
                            BakeFrame(bakedClip, frame, boneCount, bones, bindPoses, objectInverse, uniformScale, gteScaling);
                        }
                    }
                    finally
                    {
                        clip.legacy = wasLegacy;
                    }
                }
                else
                {
                    // Generic or Humanoid clip: use AnimationMode.SampleAnimationClip
                    // for proper bone path resolution and muscle retargeting.
                    Debug.Log($"[SkinBake]   Using ANIMATOR path (AnimationMode.SampleAnimationClip)");
                    Debug.Log($"[SkinBake]   Animator on '{animator.gameObject.name}', avatar={animator.avatar?.name ?? "null"}, isHuman={animator.avatar?.isHuman ?? false}");

                    // Verify bone reachability from the Animator
                    if (boneCount > 0 && bones[0] != null)
                    {
                        string bonePath = AnimationUtility.CalculateTransformPath(bones[0], animator.transform);
                        Transform found = animator.transform.Find(bonePath);
                        bool reachable = (found == bones[0]);
                        Debug.Log($"[SkinBake]   bone[0] path from Animator: '{bonePath}', reachable={reachable}");
                        if (!reachable)
                            Debug.LogError($"[SkinBake]   CRITICAL: bone[0] NOT reachable from Animator '{animator.gameObject.name}'! Skin baking will produce identity matrices.");
                    }

                    // Also verify via GetBoneTransform
                    if (animator.avatar != null && animator.avatar.isHuman)
                    {
                        Transform hipsCheck = animator.GetBoneTransform(HumanBodyBones.Hips);
                        Debug.Log($"[SkinBake]   GetBoneTransform(Hips) = {(hipsCheck != null ? hipsCheck.name : "NULL")}");
                    }

                    // Save bone local transforms so we can restore after baking
                    var savedBonePos = new Vector3[boneCount];
                    var savedBoneRot = new Quaternion[boneCount];
                    var savedBoneScl = new Vector3[boneCount];
                    for (int bi = 0; bi < boneCount; bi++)
                    {
                        if (bones[bi] == null) continue;
                        savedBonePos[bi] = bones[bi].localPosition;
                        savedBoneRot[bi] = bones[bi].localRotation;
                        savedBoneScl[bi] = bones[bi].localScale;
                    }

                    // No per-frame transform restore -- root motion must be preserved.
                    AnimationMode.StartAnimationMode();
                    try
                    {
                        for (int frame = 0; frame < frameCount; frame++)
                        {
                            float time = (frameCount > 1) ? (frame / (float)(frameCount - 1)) * clip.length : 0f;

                            AnimationMode.BeginSampling();
                            AnimationMode.SampleAnimationClip(animator.gameObject, clip, time);
                            AnimationMode.EndSampling();

                            if (frame <= 2 && boneCount > 0 && bones[0] != null)
                            {
                                Debug.Log($"[SkinBake]   Post-Sample frame {frame} (t={time:F3}): bone[0] '{bones[0].name}' " +
                                          $"localPos={bones[0].localPosition}, localRot={bones[0].localRotation.eulerAngles}");
                            }

                            BakeFrame(bakedClip, frame, boneCount, bones, bindPoses, objectInverse, uniformScale, gteScaling);
                        }
                    }
                    finally
                    {
                        AnimationMode.StopAnimationMode();

                        // Restore bone transforms (AnimationMode should revert,
                        // but we do this as a safety net)
                        for (int bi = 0; bi < boneCount; bi++)
                        {
                            if (bones[bi] == null) continue;
                            bones[bi].localPosition = savedBonePos[bi];
                            bones[bi].localRotation = savedBoneRot[bi];
                            bones[bi].localScale = savedBoneScl[bi];
                        }
                    }
                }

                // Log frame comparison to verify animation is actually baking differently
                if (frameCount >= 2)
                {
                    var f0 = bakedClip.Frames[0]; // bone 0, frame 0
                    var f1 = bakedClip.Frames[1 * boneCount]; // bone 0, frame 1 (if > 1 frame)
                    int lastFrame = frameCount - 1;
                    var fL = bakedClip.Frames[lastFrame * boneCount]; // bone 0, last frame
                    Debug.Log($"[SkinBake]   Frame 0, Bone 0: r=({f0.r00},{f0.r01},{f0.r02},{f0.r10},{f0.r11},{f0.r12},{f0.r20},{f0.r21},{f0.r22}) t=({f0.tx},{f0.ty},{f0.tz})");
                    Debug.Log($"[SkinBake]   Frame 1, Bone 0: r=({f1.r00},{f1.r01},{f1.r02},{f1.r10},{f1.r11},{f1.r12},{f1.r20},{f1.r21},{f1.r22}) t=({f1.tx},{f1.ty},{f1.tz})");
                    Debug.Log($"[SkinBake]   Frame {lastFrame}, Bone 0: r=({fL.r00},{fL.r01},{fL.r02},{fL.r10},{fL.r11},{fL.r12},{fL.r20},{fL.r21},{fL.r22}) t=({fL.tx},{fL.ty},{fL.tz})");
                    
                    bool anyDiff = f0.r00 != f1.r00 || f0.r01 != f1.r01 || f0.r02 != f1.r02 ||
                                   f0.r10 != f1.r10 || f0.r11 != f1.r11 || f0.r12 != f1.r12 ||
                                   f0.tx != f1.tx || f0.ty != f1.ty || f0.tz != f1.tz;
                    if (!anyDiff)
                        Debug.LogWarning($"[SkinBake]   *** WARNING: Frame 0 and Frame 1 are IDENTICAL for bone 0! Animation may not be sampling correctly! ***");
                    else
                        Debug.Log($"[SkinBake]   Frame 0 and Frame 1 differ, animation IS being baked.");
                }

                result.Clips.Add(bakedClip);
            }

            // Clean up temporary Animator
            if (addedAnimator)
                UnityEngine.Object.DestroyImmediate(animator);

            Debug.Log($"[SkinBake] === BakeSkinData DONE for '{skinExp.name}': {result.Clips.Count} clip(s), {boneCount} bones, exporterIndex={exporterIndex}, boneIndices={result.BoneIndices?.Length ?? 0} bytes ===");

            return result;
#else
            return null;
#endif
        }

        /// <summary>
        /// Reads current bone transforms and writes one frame of baked data.
        /// objectInverse and uniformScale are pre-captured once before the
        /// animation loop so all frames are relative to the original object
        /// position, preserving root motion.
        /// </summary>
        private static void BakeFrame(
            BakedClipData bakedClip, int frame, int boneCount,
            Transform[] bones, Matrix4x4[] bindPoses,
            Matrix4x4 objectInverse, float uniformScale, float gteScaling)
        {
            for (int bi = 0; bi < boneCount; bi++)
            {
                Matrix4x4 skinMatrix = objectInverse * bones[bi].localToWorldMatrix * bindPoses[bi];
                bakedClip.Frames[frame * boneCount + bi] = ConvertToPSXBoneMatrix(skinMatrix, gteScaling, uniformScale);
            }
        }

        /// <summary>
        /// Compute bone indices per triangle vertex (matching PSXMesh triangle order).
        /// Returns byte[polyCount * 3] where indices are [tri*3+0, tri*3+1, tri*3+2].
        /// </summary>
        private static byte[] ComputePerTriangleBoneIndices(Mesh mesh, SkinnedMeshRenderer smr)
        {
            // Get per-vertex bone weights
            var boneWeights = mesh.boneWeights;
            int[] vertexBone = new int[mesh.vertexCount];
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                if (i < boneWeights.Length)
                    vertexBone[i] = boneWeights[i].boneIndex0; // hard skinning: highest weight
                else
                    vertexBone[i] = 0;
            }

            // Iterate triangles in the same order as PSXMesh.BuildFromMesh
            var indices = new List<byte>();
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            if (normals == null || normals.Length == 0)
            {
                mesh.RecalculateNormals();
                normals = mesh.normals;
            }

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] tris = mesh.GetTriangles(submesh);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int vid0 = tris[i];
                    int vid1 = tris[i + 1];
                    int vid2 = tris[i + 2];

                    // Apply the same winding order fix as PSXMesh.BuildFromMesh
                    Vector3 faceNormal = Vector3.Cross(
                        vertices[vid1] - vertices[vid0],
                        vertices[vid2] - vertices[vid0]).normalized;
                    if (Vector3.Dot(faceNormal, normals[vid0]) < 0)
                        (vid1, vid2) = (vid2, vid1);

                    indices.Add((byte)Mathf.Min(vertexBone[vid0], MAX_BONES - 1));
                    indices.Add((byte)Mathf.Min(vertexBone[vid1], MAX_BONES - 1));
                    indices.Add((byte)Mathf.Min(vertexBone[vid2], MAX_BONES - 1));
                }
            }

            return indices.ToArray();
        }

        /// <summary>
        /// Convert a Unity 4x4 skin matrix to a PS1 BakedBoneMatrix.
        /// Applies Y-axis flip for PS1 coordinate system.
        /// Rotation: 4.12 fixed-point (4096 = 1.0).
        /// Translation: 4.12 fixed-point in GTE-scaled units, matching vertex coordinate space.
        /// The skinMatrix's worldToLocalMatrix includes 1/scale, so uniformScale is needed
        /// to recover the true displacement before converting to 4.12 fp GTE units.
        /// </summary>
        private static BakedBoneMatrix ConvertToPSXBoneMatrix(Matrix4x4 m, float gteScaling, float uniformScale)
        {
            // Extract 3x3 rotation (includes any scale from bones)
            // Apply Y-flip: R_psx = FlipY * R * FlipY
            // FlipY = diag(1, -1, 1)
            // Result: negate elements in row 1 XOR column 1
            float r00 = m.m00;   float r01 = -m.m01;  float r02 = m.m02;
            float r10 = -m.m10;  float r11 = m.m11;   float r12 = -m.m12;
            float r20 = m.m20;   float r21 = -m.m21;  float r22 = m.m22;

            // Translation with Y-flip, converted to 4.12 fixed-point GTE units.
            // skinMatrix translation = actual_displacement / uniformScale (from worldToLocalMatrix).
            // Multiply by uniformScale to recover true displacement, then convert like vertices:
            //   psx_value = (displacement * uniformScale / gteScaling) * 4096
            // This matches ConvertCoordinateToPSX(vertex * lossyScale, gteScaling).
            float tx = (m.m03 * uniformScale / gteScaling) * 4096f;
            float ty = (-m.m13 * uniformScale / gteScaling) * 4096f;
            float tz = (m.m23 * uniformScale / gteScaling) * 4096f;

            return new BakedBoneMatrix
            {
                r00 = (short)Mathf.Clamp(Mathf.RoundToInt(r00 * 4096f), -32768, 32767),
                r01 = (short)Mathf.Clamp(Mathf.RoundToInt(r01 * 4096f), -32768, 32767),
                r02 = (short)Mathf.Clamp(Mathf.RoundToInt(r02 * 4096f), -32768, 32767),
                r10 = (short)Mathf.Clamp(Mathf.RoundToInt(r10 * 4096f), -32768, 32767),
                r11 = (short)Mathf.Clamp(Mathf.RoundToInt(r11 * 4096f), -32768, 32767),
                r12 = (short)Mathf.Clamp(Mathf.RoundToInt(r12 * 4096f), -32768, 32767),
                r20 = (short)Mathf.Clamp(Mathf.RoundToInt(r20 * 4096f), -32768, 32767),
                r21 = (short)Mathf.Clamp(Mathf.RoundToInt(r21 * 4096f), -32768, 32767),
                r22 = (short)Mathf.Clamp(Mathf.RoundToInt(r22 * 4096f), -32768, 32767),
                tx = (short)Mathf.Clamp(Mathf.RoundToInt(tx), -32768, 32767),
                ty = (short)Mathf.Clamp(Mathf.RoundToInt(ty), -32768, 32767),
                tz = (short)Mathf.Clamp(Mathf.RoundToInt(tz), -32768, 32767),
            };
        }

        // ════════════════════════════════════════════════════════════
        // Binary serialization
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Write the skin data section to the splashpack binary.
        /// Returns the offset of the skin table for header backfill.
        /// </summary>
        public static void ExportSkinData(
            BinaryWriter writer,
            BakedSkinData[] skinData,
            PSXObjectExporter[] allExporters,
            out long skinTableStart,
            Action<string, LogType> log = null)
        {
            skinTableStart = 0;
            if (skinData == null || skinData.Length == 0) return;

            int count = Mathf.Min(skinData.Length, MAX_SKINNED_MESHES);

            AlignToFourBytes(writer);
            skinTableStart = writer.BaseStream.Position;

            // ── Skin table: 12 bytes per entry ──
            long[] entryPositions = new long[count];
            for (int i = 0; i < count; i++)
            {
                entryPositions[i] = writer.BaseStream.Position;
                writer.Write((uint)0);  // dataOffset placeholder
                writer.Write((byte)0);  // nameLen placeholder
                writer.Write((byte)0);  // pad
                writer.Write((byte)0);  // pad
                writer.Write((byte)0);  // pad
                writer.Write((uint)0);  // nameOffset placeholder
            }

            // ── Per-mesh skin data blocks ──
            for (int si = 0; si < count; si++)
            {
                var skin = skinData[si];
                AlignToFourBytes(writer);

                long dataPos = writer.BaseStream.Position;

                // Header: gameObjectIndex(2) + boneCount(1) + clipCount(1) = 4 bytes
                writer.Write((ushort)skin.GameObjectIndex);
                writer.Write((byte)skin.BoneCount);
                writer.Write((byte)Mathf.Min(skin.Clips.Count, MAX_CLIPS));

                // Bone indices: polyCount * 3 bytes
                if (skin.BoneIndices != null)
                    writer.Write(skin.BoneIndices);

                // Align to 4 bytes
                AlignToFourBytes(writer);

                // Per-clip data
                int clipCount = Mathf.Min(skin.Clips.Count, MAX_CLIPS);
                for (int ci = 0; ci < clipCount; ci++)
                {
                    var clip = skin.Clips[ci];
                    string clipName = clip.ClipName ?? "clip";
                    if (clipName.Length > MAX_NAME_LEN) clipName = clipName.Substring(0, MAX_NAME_LEN);
                    byte[] nameBytes = Encoding.UTF8.GetBytes(clipName);

                    // clipNameLen (1 byte)
                    writer.Write((byte)nameBytes.Length);
                    // name chars
                    writer.Write(nameBytes);
                    // null byte (C++ parser overwrites this position with '\0')
                    writer.Write((byte)0);

                    // flags: bit 0 = loop
                    byte flags = (byte)(clip.Loop ? 0x01 : 0x00);
                    writer.Write(flags);
                    // fps (1 byte)
                    writer.Write((byte)clip.Fps);

                    // Align to 2 bytes for the uint16 frameCount
                    long pos = writer.BaseStream.Position;
                    if (pos % 2 != 0) writer.Write((byte)0);

                    // frameCount (2 bytes, little-endian uint16)
                    writer.Write((ushort)clip.FrameCount);

                    // Frame data: frameCount * boneCount * 24 bytes
                    int frameCount = clip.FrameCount;
                    for (int fi = 0; fi < frameCount; fi++)
                    {
                        for (int bi = 0; bi < skin.BoneCount; bi++)
                        {
                            var bm = clip.Frames[fi * skin.BoneCount + bi];
                            writer.Write(bm.r00); writer.Write(bm.r01); writer.Write(bm.r02);
                            writer.Write(bm.r10); writer.Write(bm.r11); writer.Write(bm.r12);
                            writer.Write(bm.r20); writer.Write(bm.r21); writer.Write(bm.r22);
                            writer.Write(bm.tx);  writer.Write(bm.ty);  writer.Write(bm.tz);
                        }
                    }
                }

                // Object name (for the table entry)
                string objName = skin.GameObjectIndex < allExporters.Length
                    ? allExporters[skin.GameObjectIndex].gameObject.name : "skinned";
                if (objName.Length > MAX_NAME_LEN) objName = objName.Substring(0, MAX_NAME_LEN);
                byte[] objNameBytes = Encoding.UTF8.GetBytes(objName);
                long namePos = writer.BaseStream.Position;
                writer.Write(objNameBytes);
                writer.Write((byte)0); // null terminator

                // Backfill table entry
                {
                    long curPos = writer.BaseStream.Position;
                    writer.Seek((int)entryPositions[si], SeekOrigin.Begin);
                    writer.Write((uint)dataPos);
                    writer.Write((byte)objNameBytes.Length);
                    writer.Write((byte)0); // pad
                    writer.Write((byte)0); // pad
                    writer.Write((byte)0); // pad
                    writer.Write((uint)namePos);
                    writer.Seek((int)curPos, SeekOrigin.Begin);
                }
            }

            int totalFrames = 0;
            int totalBones = 0;
            foreach (var s in skinData)
            {
                totalBones += s.BoneCount;
                foreach (var c in s.Clips) totalFrames += c.FrameCount;
            }
            log?.Invoke($"{count} skinned mesh(es) exported ({totalBones} total bones, {totalFrames} total frames).", LogType.Log);
        }

        /// <summary>
        /// Estimate the binary size of skin data for memory analysis.
        /// </summary>
        public static long EstimateSkinDataBytes(BakedSkinData[] skinData)
        {
            if (skinData == null) return 0;
            long total = 0;
            total += skinData.Length * 12; // table entries
            foreach (var skin in skinData)
            {
                total += 4; // header
                total += skin.BoneIndices?.Length ?? 0;
                total = Align4(total);
                foreach (var clip in skin.Clips)
                {
                    total += 1 + (clip.ClipName?.Length ?? 4) + 1 + 3; // name + null + flags/frameCount/fps
                    total = Align2(total);
                    total += (long)clip.FrameCount * skin.BoneCount * 24;
                }
                total += 30; // approximate name string
            }
            return total;
        }

        private static long Align4(long v) => (v + 3) & ~3L;
        private static long Align2(long v) => (v + 1) & ~1L;

        private static void AlignToFourBytes(BinaryWriter writer)
        {
            long pos = writer.BaseStream.Position;
            int padding = (int)(4 - (pos % 4)) % 4;
            if (padding > 0) writer.Write(new byte[padding]);
        }
    }
}
