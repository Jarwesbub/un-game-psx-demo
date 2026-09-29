using System.Collections.Generic;
using System.Linq;
using SplashEdit.RuntimeCode;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;


namespace SplashEdit.EditorCode
{
    public class VRAMEditorWindow : EditorWindow
    {
        private int VramWidth => VRAMPacker.VramWidth;
        private int VramHeight => VRAMPacker.VramHeight;

        private static readonly Vector2 MinSize = new Vector2(800, 600);
        private List<ProhibitedArea> prohibitedAreas = new List<ProhibitedArea>();
        private Vector2 scrollPosition;
        private Texture2D vramImage;
        private static readonly Vector2 selectedResolution = new Vector2(320, 240);
        private const bool dualBuffering = true;
        private const bool verticalLayout = true;
        private Color bufferColor1 = new Color(1, 0, 0, 0.5f);
        private Color bufferColor2 = new Color(0, 1, 0, 0.5f);
        private Color prohibitedColor = new Color(1, 0, 0, 0.3f);
        private PSXData _psxData;
        private PSXFontData[] _cachedFonts;

        [MenuItem("PlayStation 1/VRAM Editor")]
        public static void ShowWindow()
        {
            VRAMEditorWindow window = GetWindow<VRAMEditorWindow>("VRAM Editor");
            // Set minimum window dimensions.
            window.minSize = MinSize;
        }

        private void OnEnable()
        {
            // Initialize VRAM texture with black pixels.
            vramImage = new Texture2D(VramWidth, VramHeight);
            NativeArray<Color32> blackPixels = new NativeArray<Color32>(VramWidth * VramHeight, Allocator.Temp);
            vramImage.SetPixelData(blackPixels, 0);
            vramImage.Apply();
            blackPixels.Dispose();

            // Ensure minimum window size is applied.
            this.minSize = MinSize;

            Vector2 ignoredRes;
            bool ignoredDb, ignoredVl;
            _psxData = DataStorage.LoadData(out ignoredRes, out ignoredDb, out ignoredVl, out prohibitedAreas);
        }

        /// <summary>
        /// Pastes an overlay texture onto a base texture at the specified position.    
        /// </summary>
        public static void PasteTexture(Texture2D baseTexture, Texture2D overlayTexture, int posX, int posY)
        {
            if (baseTexture == null || overlayTexture == null)
            {
                Debug.LogError("Textures cannot be null!");
                return;
            }

            Color[] overlayPixels = overlayTexture.GetPixels();
            Color[] basePixels = baseTexture.GetPixels();

            int baseWidth = baseTexture.width;
            int baseHeight = baseTexture.height;
            int overlayWidth = overlayTexture.width;
            int overlayHeight = overlayTexture.height;

            // Copy each overlay pixel into the base texture if within bounds.
            for (int y = 0; y < overlayHeight; y++)
            {
                for (int x = 0; x < overlayWidth; x++)
                {
                    int baseX = posX + x;
                    int baseY = posY + y;
                    if (baseX >= 0 && baseX < baseWidth && baseY >= 0 && baseY < baseHeight)
                    {
                        int baseIndex = baseY * baseWidth + baseX;
                        int overlayIndex = y * overlayWidth + x;
                        basePixels[baseIndex] = overlayPixels[overlayIndex];
                    }
                }
            }

            baseTexture.SetPixels(basePixels);
            baseTexture.Apply();
        }

        /// <summary>
        /// Packs PSX textures into VRAM, rebuilds the VRAM texture and writes binary data to an output file.
        /// </summary>
        private void PackTextures()
        {
            // Reinitialize VRAM texture with black pixels.
            vramImage = new Texture2D(VramWidth, VramHeight);
            NativeArray<Color32> blackPixels = new NativeArray<Color32>(VramWidth * VramHeight, Allocator.Temp);
            vramImage.SetPixelData(blackPixels, 0);
            vramImage.Apply();
            blackPixels.Dispose();

            // Retrieve all PSXObjectExporter objects and create their PSX textures.
            PSXObjectExporter[] objects = FindObjectsByType<PSXObjectExporter>(FindObjectsSortMode.None);
            for (int i = 0; i < objects.Length; i++)
            {

                EditorUtility.DisplayProgressBar($"{nameof(VRAMEditorWindow)}", $"Export {nameof(PSXObjectExporter)}", ((float)i) / objects.Length);
                PSXObjectExporter exp = objects[i];
                exp.CreatePSXTextures2D();
            }

            EditorUtility.ClearProgressBar();

            // Define framebuffer regions based on selected resolution and layout.
            (Rect buffer1, Rect buffer2) = Utils.BufferForResolution(selectedResolution, verticalLayout);

            List<Rect> framebuffers = new List<Rect> { buffer1 };
            if (dualBuffering)
            {
                framebuffers.Add(buffer2);
            }

            // Pack textures into VRAM using the VRAMPacker.
            VRAMPacker tp = new VRAMPacker(framebuffers, prohibitedAreas);
            var packed = tp.PackTexturesIntoVRAM(objects);

            // Copy packed VRAM pixel data into the texture.
            for (int y = 0; y < VramHeight; y++)
            {
                for (int x = 0; x < VramWidth; x++)
                {
                    vramImage.SetPixel(x, VramHeight - y - 1, packed.vramPixels[x, y].GetUnityColor());
                }
            }

            // Overlay custom font textures into the VRAM preview.
            // Fonts live at x=960 (4bpp = 64 VRAM hwords wide), stacking from y=0.
            PSXFontData[] fonts;
            PSXUIExporter.CollectCanvases(selectedResolution, out fonts);
            _cachedFonts = fonts;
            if (fonts != null && fonts.Length > 0)
            {
                foreach (var font in fonts)
                {
                    if (font.PixelData == null || font.PixelData.Length == 0) continue;

                    int vramX = font.VramX;
                    int vramY = font.VramY;
                    int texH = font.TextureHeight;
                    int bytesPerRow = 256 / 2; // 4bpp: 2 pixels per byte, 256 pixels wide = 128 bytes/row

                    // Each byte holds two 4bpp pixels. In VRAM, 4 4bpp pixels = 1 16-bit hword.
                    // So 256 4bpp pixels = 64 VRAM hwords.
                    for (int y = 0; y < texH && (vramY + y) < VramHeight; y++)
                    {
                        for (int x = 0; x < 64 && (vramX + x) < VramWidth; x++)
                        {
                            // Read 4 4bpp pixels from this VRAM hword position
                            int byteIdx = y * bytesPerRow + x * 2;
                            if (byteIdx + 1 >= font.PixelData.Length) continue;
                            byte b0 = font.PixelData[byteIdx];
                            byte b1 = font.PixelData[byteIdx + 1];
                            // Each byte: low nibble = first pixel, high nibble = second
                            // 4 pixels per hword: b0 low, b0 high, b1 low, b1 high
                            bool anyOpaque = ((b0 & 0x0F) | (b0 >> 4) | (b1 & 0x0F) | (b1 >> 4)) != 0;

                            if (anyOpaque)
                            {
                                int px = vramX + x;
                                int py = VramHeight - 1 - (vramY + y);
                                if (px < VramWidth && py >= 0)
                                    vramImage.SetPixel(px, py, new Color(0.8f, 0.8f, 1f));
                            }
                        }
                    }
                }
            }

            // Also show system font area (960, 464)-(1023, 511) = 64x48
            for (int y = 464; y < 512 && y < VramHeight; y++)
            {
                for (int x = 960; x < 1024 && x < VramWidth; x++)
                {
                    int py = VramHeight - 1 - y;
                    Color existing = vramImage.GetPixel(x, py);
                    if (existing.r < 0.01f && existing.g < 0.01f && existing.b < 0.01f)
                        vramImage.SetPixel(x, py, new Color(0.3f, 0.3f, 0.5f));
                }
            }

            vramImage.Apply();
        }

        private void OnGUI()
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label("VRAM Editor", PSXEditorStyles.WindowHeader);
            GUILayout.Label("320x240, dual-buffered, vertical layout", PSXEditorStyles.InfoBox);

            PSXEditorStyles.DrawSeparator(6, 6);
            GUILayout.Label("Prohibited Areas", PSXEditorStyles.SectionHeader);
            GUILayout.Space(4);

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, GUILayout.MinHeight(300f), GUILayout.ExpandWidth(true));

            // List and edit each prohibited area.
            List<int> toRemove = new List<int>();

            for (int i = 0; i < prohibitedAreas.Count; i++)
            {
                var area = prohibitedAreas[i];

                PSXEditorStyles.BeginCard();

                // Display fields for editing the area
                area.X = EditorGUILayout.IntField("X Coordinate", area.X);
                area.Y = EditorGUILayout.IntField("Y Coordinate", area.Y);
                area.Width = EditorGUILayout.IntField("Width", area.Width);
                area.Height = EditorGUILayout.IntField("Height", area.Height);

                EditorGUILayout.Space(2);
                if (GUILayout.Button("Remove", PSXEditorStyles.DangerButton, GUILayout.Height(24)))
                {
                    toRemove.Add(i); // Mark for removal
                }

                prohibitedAreas[i] = area;

                PSXEditorStyles.EndCard();
                GUILayout.Space(4);
            }

            // Remove the areas marked for deletion outside the loop to avoid skipping elements
            foreach (var index in toRemove.OrderByDescending(x => x))
            {
                prohibitedAreas.RemoveAt(index);
            }

            GUILayout.EndScrollView();
            GUILayout.Space(10);

            if (GUILayout.Button("Add Prohibited Area", PSXEditorStyles.SecondaryButton))
            {
                prohibitedAreas.Add(new ProhibitedArea());
            }

            PSXEditorStyles.DrawSeparator(4, 4);

            // Button to pack and preview VRAM layout.
            if (GUILayout.Button("Pack Preview", PSXEditorStyles.PrimaryButton, GUILayout.Height(28)))
            {
                PackTextures();
            }

            EditorGUILayout.Space(2);

            // Button to save prohibited areas.
            if (GUILayout.Button("Save Settings", PSXEditorStyles.SuccessButton, GUILayout.Height(28)))
            {
                _psxData.OutputResolution = selectedResolution;
                _psxData.DualBuffering = dualBuffering;
                _psxData.VerticalBuffering = verticalLayout;
                _psxData.ProhibitedAreas = prohibitedAreas;

                DataStorage.StoreData(_psxData);
                EditorUtility.DisplayDialog("splashedit", "Vram configuration saved", "OK");
            }

            GUILayout.EndVertical();

            // Display VRAM image preview.
            Rect vramRect = GUILayoutUtility.GetRect(VramWidth, VramHeight, GUILayout.ExpandWidth(false));
            if (vramImage)
            {
                EditorGUI.DrawPreviewTexture(vramRect, vramImage, null, ScaleMode.ScaleToFit, 0, 0, ColorWriteMask.All);
            }

            // Draw framebuffer overlays.
            (Rect buffer1, Rect buffer2) = Utils.BufferForResolution(selectedResolution, verticalLayout, vramRect.min);

            EditorGUI.DrawRect(buffer1, bufferColor1);
            GUI.Label(new Rect(buffer1.center.x - 40, buffer1.center.y - 10, 120, 20), "Framebuffer A", EditorStyles.boldLabel);
            GUILayout.Space(10);
            if (dualBuffering)
            {
                EditorGUI.DrawRect(buffer2, bufferColor2);
                GUI.Label(new Rect(buffer2.center.x - 40, buffer2.center.y - 10, 120, 20), "Framebuffer B", EditorStyles.boldLabel);
            }

            // Draw overlays for each prohibited area.
            foreach (ProhibitedArea area in prohibitedAreas)
            {
                Rect areaRect = new Rect(vramRect.x + area.X, vramRect.y + area.Y, area.Width, area.Height);
                EditorGUI.DrawRect(areaRect, prohibitedColor);
            }

            // Draw font region overlays.
            if (_cachedFonts != null)
            {
                Color fontColor = new Color(0.2f, 0.4f, 0.9f, 0.25f);
                foreach (var font in _cachedFonts)
                {
                    if (font.PixelData == null || font.PixelData.Length == 0) continue;
                    Rect fontRect = new Rect(vramRect.x + font.VramX, vramRect.y + font.VramY, 64, font.TextureHeight);
                    EditorGUI.DrawRect(fontRect, fontColor);
                    GUI.Label(new Rect(fontRect.x + 2, fontRect.y + 2, 60, 16), "Font", EditorStyles.miniLabel);
                }

                // System font overlay
                Rect sysFontRect = new Rect(vramRect.x + 960, vramRect.y + 464, 64, 48);
                EditorGUI.DrawRect(sysFontRect, new Color(0.4f, 0.2f, 0.9f, 0.25f));
                GUI.Label(new Rect(sysFontRect.x + 2, sysFontRect.y + 2, 60, 16), "SysFont", EditorStyles.miniLabel);
            }

            GUILayout.EndHorizontal();
        }
    }
}