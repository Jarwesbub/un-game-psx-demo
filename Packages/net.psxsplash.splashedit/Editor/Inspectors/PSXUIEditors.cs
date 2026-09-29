using UnityEngine;
using UnityEditor;
using SplashEdit.RuntimeCode;
using System.Linq;

namespace SplashEdit.EditorCode
{
    // --- Scene Preview Gizmos ---
    // A single canvas-level gizmo draws all children in hierarchy order
    // so depth stacking is correct (last child in hierarchy renders on top).

    public static class PSXUIGizmos
    {
        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
        static void DrawCanvasGizmo(PSXCanvas canvas, GizmoType gizmoType)
        {
            RectTransform canvasRt = canvas.GetComponent<RectTransform>();
            if (canvasRt == null) return;

            bool canvasSelected = (gizmoType & GizmoType.Selected) != 0;

            // Canvas border
            Vector3[] canvasCorners = new Vector3[4];
            canvasRt.GetWorldCorners(canvasCorners);
            Color border = canvasSelected ? Color.yellow : new Color(1, 1, 0, 0.3f);
            Handles.DrawSolidRectangleWithOutline(canvasCorners, Color.clear, border);

            // Draw all children in hierarchy order (first child = back, last child = front)
            var children = canvas.GetComponentsInChildren<Transform>(true).Reverse();
            foreach (var child in children)
            {
                if (child == canvas.transform) continue;
                bool childSelected = Selection.Contains(child.gameObject);

                var box = child.GetComponent<PSXUIBox>();
                if (box != null) { DrawBox(box, childSelected); continue; }

                var line = child.GetComponent<PSXUILine>();
                if (line != null) { DrawLine(line, childSelected); continue; }

                var image = child.GetComponent<PSXUIImage>();
                if (image != null) { DrawImage(image, childSelected); continue; }

                var text = child.GetComponent<PSXUIText>();
                if (text != null) { DrawText(text, childSelected); continue; }

                var bar = child.GetComponent<PSXUIProgressBar>();
                if (bar != null) { DrawProgressBar(bar, childSelected); continue; }
            }

            // Canvas label when selected
            if (canvasSelected)
            {
                Vector2 res = PSXCanvas.PSXResolution;
                Vector3 topMid = (canvasCorners[1] + canvasCorners[2]) * 0.5f;
                string label = $"PSX Canvas: {canvas.CanvasName} ({res.x}x{res.y})";
                GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
                style.normal.textColor = Color.yellow;
                Handles.Label(topMid, label, style);
            }
        }

        static void DrawBox(PSXUIBox box, bool selected)
        {
            RectTransform rt = box.GetComponent<RectTransform>();
            if (rt == null) return;
            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Color fill = box.BoxColor;
            fill.a = selected ? 1f : 0.9f;
            Color borderColor = selected ? Color.white : new Color(1, 1, 1, 0.5f);
            Handles.DrawSolidRectangleWithOutline(corners, fill, borderColor);
        }

        // This converts from Unity's -160 + 160 to the PS1's 0 - 320 screen coordinates
        static Vector2 ToCanvasSpace(Vector2 p, RectTransform canvas)
        {
            return new Vector2(
                p.x - canvas.rect.width * 0.5f,
                canvas.rect.height * 0.5f - p.y
            );
        }

        static void DrawLine(PSXUILine line, bool selected)
        {
            Canvas canvas = line.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            RectTransform canvasRT = canvas.GetComponent<RectTransform>();

            Vector2 start = ToCanvasSpace(line.Point1, canvasRT);
            Vector2 end = ToCanvasSpace(line.Point2, canvasRT);

            Vector3 worldStart = canvasRT.TransformPoint(start);
            Vector3 worldEnd = canvasRT.TransformPoint(end);

            Color c = line.LineColor;
            c.a = selected ? 1f : 0.9f;

            Handles.color = c;
            Handles.DrawLine(worldStart, worldEnd, 2f);
        }

        static void DrawImage(PSXUIImage image, bool selected)
        {
            RectTransform rt = image.GetComponent<RectTransform>();
            if (rt == null) return;
            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);

            if (image.SourceTexture != null)
            {
                Color tint = image.TintColor;
                tint.a = selected ? 1f : 0.9f;
                Handles.DrawSolidRectangleWithOutline(corners, tint * 0.3f, tint);

                Handles.BeginGUI();
                Vector2 min = HandleUtility.WorldToGUIPoint(corners[0]);
                Vector2 max = HandleUtility.WorldToGUIPoint(corners[2]);
                Rect screenRect = new Rect(
                    Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y),
                    Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y));
                if (screenRect.width > 2 && screenRect.height > 2)
                {
                    GUI.color = new Color(tint.r, tint.g, tint.b, selected ? 1f : 0.9f);
                    GUI.DrawTexture(screenRect, image.SourceTexture, ScaleMode.StretchToFill);
                    GUI.color = Color.white;
                }
                Handles.EndGUI();
            }
            else
            {
                Color fill = new Color(0.4f, 0.4f, 0.8f, selected ? 0.8f : 0.6f);
                Handles.DrawSolidRectangleWithOutline(corners, fill, Color.cyan);
            }
        }

        static void DrawText(PSXUIText text, bool selected)
        {
            RectTransform rt = text.GetComponent<RectTransform>();
            if (rt == null) return;
            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);

            Color borderColor = text.TextColor;
            borderColor.a = selected ? 1f : 0.7f;
            Color fill = new Color(0, 0, 0, selected ? 0.6f : 0.4f);
            Handles.DrawSolidRectangleWithOutline(corners, fill, borderColor);

            string label = string.IsNullOrEmpty(text.DefaultText) ? "[empty]" : text.DefaultText;

            PSXFontAsset font = text.GetEffectiveFont();
            int glyphW = font != null ? font.GlyphWidth : 8;
            int glyphH = font != null ? font.GlyphHeight : 16;

            Handles.BeginGUI();
            Vector2 topLeft = HandleUtility.WorldToGUIPoint(corners[1]);
            Vector2 botRight = HandleUtility.WorldToGUIPoint(corners[3]);

            float rectScreenW = Mathf.Abs(botRight.x - topLeft.x);
            float rectW = rt.rect.width;
            float psxPixelScale = (rectW > 0.01f) ? rectScreenW / rectW : 1f;

            float guiX = Mathf.Min(topLeft.x, botRight.x);
            float guiY = Mathf.Min(topLeft.y, botRight.y);
            float guiW = Mathf.Abs(botRight.x - topLeft.x);
            float guiH = Mathf.Abs(botRight.y - topLeft.y);

            Color tintColor = text.TextColor;
            tintColor.a = selected ? 1f : 0.8f;

            if (font != null && font.FontTexture != null && font.SourceFont != null)
            {
                Texture2D fontTex = font.FontTexture;
                int glyphsPerRow = font.GlyphsPerRow;
                float cellScreenH = glyphH * psxPixelScale;

                float cursorX = guiX;
                GUI.color = tintColor;
                foreach (char ch in label)
                {
                    if (ch < 32 || ch > 126) continue;
                    int charIdx = ch - 32;
                    int col = charIdx % glyphsPerRow;
                    int row = charIdx / glyphsPerRow;

                    float advance = glyphW;
                    if (font.AdvanceWidths != null && charIdx < font.AdvanceWidths.Length)
                        advance = font.AdvanceWidths[charIdx];

                    if (ch != ' ')
                    {
                        float uvX = (float)(col * glyphW) / fontTex.width;
                        float uvY = 1f - (float)((row + 1) * glyphH) / fontTex.height;
                        float uvW = (float)glyphW / fontTex.width;
                        float uvH = (float)glyphH / fontTex.height;

                        float spriteScreenW = advance * psxPixelScale;
                        Rect screenRect = new Rect(cursorX, guiY, spriteScreenW, cellScreenH);
                        float uvWScaled = uvW * (advance / glyphW);
                        Rect uvRect = new Rect(uvX, uvY, uvWScaled, uvH);

                        if (screenRect.xMax > guiX && screenRect.x < guiX + guiW)
                            GUI.DrawTextureWithTexCoords(screenRect, fontTex, uvRect);
                    }

                    cursorX += advance * psxPixelScale;
                }
                GUI.color = Color.white;
            }
            else
            {
                int fSize = Mathf.Clamp(Mathf.RoundToInt(glyphH * psxPixelScale * 0.75f), 6, 72);
                GUIStyle style = new GUIStyle(EditorStyles.label);
                style.normal.textColor = tintColor;
                style.alignment = TextAnchor.UpperLeft;
                style.fontSize = fSize;
                style.wordWrap = false;
                style.clipping = TextClipping.Clip;

                Rect guiRect = new Rect(guiX, guiY, guiW, guiH);
                GUI.color = tintColor;
                GUI.Label(guiRect, label, style);
                GUI.color = Color.white;
            }
            Handles.EndGUI();
        }

        static void DrawProgressBar(PSXUIProgressBar bar, bool selected)
        {
            RectTransform rt = bar.GetComponent<RectTransform>();
            if (rt == null) return;
            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);

            Color bgColor = bar.BackgroundColor;
            bgColor.a = selected ? 1f : 0.9f;
            Handles.DrawSolidRectangleWithOutline(corners, bgColor, selected ? Color.white : new Color(1, 1, 1, 0.5f));

            float t = bar.InitialValue / 100f;
            if (t > 0.001f)
            {
                Vector3[] fillCorners = new Vector3[4];
                fillCorners[0] = corners[0];
                fillCorners[1] = corners[1];
                fillCorners[2] = Vector3.Lerp(corners[1], corners[2], t);
                fillCorners[3] = Vector3.Lerp(corners[0], corners[3], t);
                Color fillColor = bar.FillColor;
                fillColor.a = selected ? 1f : 0.9f;
                Handles.DrawSolidRectangleWithOutline(fillCorners, fillColor, Color.clear);
            }
        }
    }
    /// <summary>
    /// Custom inspector for PSXCanvas component.
    /// Shows canvas name, visibility, sort order, font, and a summary of child elements.
    /// </summary>
    [CustomEditor(typeof(PSXCanvas))]
    public class PSXCanvasEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            Vector2 res = PSXCanvas.PSXResolution;

            // Header card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField($"PSX Canvas ({res.x}x{res.y})", PSXEditorStyles.CardHeaderStyle);
            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Properties card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("canvasName"), new GUIContent("Canvas Name",
                "Name used from Lua: UI.FindCanvas(\"name\"). Max 24 chars."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("startVisible"), new GUIContent("Start Visible",
                "Whether the canvas is visible when the scene loads."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sortOrder"), new GUIContent("Sort Order",
                "Render priority (0 = back, 255 = front)."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultFont"), new GUIContent("Default Font",
                "Default custom font for text elements. If empty, uses built-in system font (8x16)."));

            PSXEditorStyles.DrawSeparator(6, 6);

            if (GUILayout.Button($"Reset Canvas to {res.x}x{res.y}", PSXEditorStyles.SecondaryButton))
            {
                PSXCanvas.InvalidateResolutionCache();
                ((PSXCanvas)target).ConfigureCanvas();
            }
            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Element summary card
            PSXCanvas canvas = (PSXCanvas)target;
            int imageCount = canvas.GetComponentsInChildren<PSXUIImage>(true).Length;
            int boxCount = canvas.GetComponentsInChildren<PSXUIBox>(true).Length;
            int textCount = canvas.GetComponentsInChildren<PSXUIText>(true).Length;
            int progressCount = canvas.GetComponentsInChildren<PSXUIProgressBar>(true).Length;
            int total = imageCount + boxCount + textCount + progressCount;

            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField(
                $"Elements: {total} total\n" +
                $"  Images: {imageCount}  |  Boxes: {boxCount}\n" +
                $"  Texts: {textCount}  |  Progress Bars: {progressCount}",
                PSXEditorStyles.InfoBox);

            if (total > 128)
                EditorGUILayout.LabelField("PS1 UI system supports max 128 elements total across all canvases.", PSXEditorStyles.InfoBox);
            PSXEditorStyles.EndCard();

            serializedObject.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Custom inspector for PSXUIImage component.
    /// </summary>
    [CustomEditor(typeof(PSXUIImage))]
    public class PSXUIImageEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Header card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("PSX UI Image", PSXEditorStyles.CardHeaderStyle);
            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Properties card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("elementName"), new GUIContent("Element Name",
                "Name used from Lua: UI.FindElement(canvas, \"name\"). Max 24 chars."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sourceTexture"), new GUIContent("Source Texture",
                "Texture to quantize and pack into VRAM."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bitDepth"), new GUIContent("Bit Depth",
                "VRAM storage depth. 4-bit = 16 colors, 8-bit = 256 colors, 16-bit = direct color."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("tintColor"), new GUIContent("Tint Color",
                "Color multiply applied to the image (white = no tint)."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("startVisible"));
            PSXEditorStyles.EndCard();

            // Texture size warning
            PSXUIImage img = (PSXUIImage)target;
            if (img.SourceTexture != null)
            {
                if (img.SourceTexture.width > 256 || img.SourceTexture.height > 256)
                {
                    EditorGUILayout.Space(4);
                    PSXEditorStyles.BeginCard();
                    EditorGUILayout.LabelField("Texture exceeds 256x256. It will be resized during export.", PSXEditorStyles.InfoBox);
                    PSXEditorStyles.EndCard();
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Custom inspector for PSXUIBox component.
    /// </summary>
    [CustomEditor(typeof(PSXUIBox))]
    public class PSXUIBoxEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Header card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("PSX UI Box", PSXEditorStyles.CardHeaderStyle);
            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Properties card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("elementName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("boxColor"), new GUIContent("Box Color",
                "Solid fill color rendered as a FastFill primitive."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("startVisible"));
            PSXEditorStyles.EndCard();

            serializedObject.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Custom inspector for PSXUIText component.
    /// </summary>
    [CustomEditor(typeof(PSXUIText))]
    public class PSXUITextEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Header card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("PSX UI Text", PSXEditorStyles.CardHeaderStyle);
            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Properties card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("elementName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultText"), new GUIContent("Default Text",
                "Initial text content. Max 63 chars. Change at runtime via UI.SetText()."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("textColor"), new GUIContent("Text Color",
                "Text render color."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("fontOverride"), new GUIContent("Font Override",
                "Custom font for this text element. If empty, uses the canvas default font or built-in system font (8x16)."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("startVisible"));
            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Warnings and info
            PSXUIText txt = (PSXUIText)target;
            if (!string.IsNullOrEmpty(txt.DefaultText) && txt.DefaultText.Length > 63)
            {
                PSXEditorStyles.BeginCard();
                EditorGUILayout.LabelField("Text exceeds 63 characters and will be truncated.", PSXEditorStyles.InfoBox);
                PSXEditorStyles.EndCard();
                EditorGUILayout.Space(4);
            }

            PSXEditorStyles.BeginCard();
            PSXFontAsset font = txt.GetEffectiveFont();
            if (font != null)
            {
                EditorGUILayout.LabelField(
                    $"Font: {font.name} ({font.GlyphWidth}x{font.GlyphHeight} glyphs)",
                    PSXEditorStyles.InfoBox);
            }
            else
            {
                EditorGUILayout.LabelField("Using built-in system font (8x16 glyphs).", PSXEditorStyles.InfoBox);
            }
            PSXEditorStyles.EndCard();

            serializedObject.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Custom inspector for PSXUIProgressBar component.
    /// </summary>
    [CustomEditor(typeof(PSXUIProgressBar))]
    public class PSXUIProgressBarEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Header card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("PSX UI Progress Bar", PSXEditorStyles.CardHeaderStyle);
            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Properties card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("elementName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("backgroundColor"), new GUIContent("Background Color",
                "Color shown behind the fill bar."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("fillColor"), new GUIContent("Fill Color",
                "Color of the progress fill."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("initialValue"), new GUIContent("Initial Value",
                "Starting progress (0-100). Change via UI.SetProgress()."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("startVisible"));

            PSXEditorStyles.DrawSeparator(6, 4);

            // Preview bar
            PSXUIProgressBar bar = (PSXUIProgressBar)target;
            PSXEditorStyles.DrawProgressBar(bar.InitialValue / 100f, "Preview", bar.FillColor, 16);
            PSXEditorStyles.EndCard();

            serializedObject.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Custom inspector for PSXFontAsset ScriptableObject.
    /// Shows font metrics, auto-conversion from TTF/OTF, and a preview of the glyph layout.
    /// </summary>
    [CustomEditor(typeof(PSXFontAsset))]
    public class PSXFontAssetEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            PSXFontAsset font = (PSXFontAsset)target;

            // Header card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("PSX Font Asset", PSXEditorStyles.CardHeaderStyle);
            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Source font card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("Auto-Convert from Font", PSXEditorStyles.CardHeaderStyle);
            PSXEditorStyles.DrawSeparator(2, 4);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("sourceFont"), new GUIContent("Source Font (TTF/OTF)",
                "Assign a Unity Font (TrueType/OpenType). Click 'Generate Bitmap' to rasterize it.\n" +
                "Glyph cell dimensions are auto-derived from the font metrics."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("fontSize"), new GUIContent("Font Size",
                "Pixel height for rasterization. Determines glyph cell height.\n" +
                "Glyph cell width is auto-derived from the widest character.\n" +
                "Changing this and re-generating will update both the bitmap AND the glyph dimensions."));

            if (font.SourceFont != null)
            {
                EditorGUILayout.Space(2);
                if (GUILayout.Button("Generate Bitmap from Font", PSXEditorStyles.PrimaryButton, GUILayout.Height(28)))
                {
                    Undo.RecordObject(font, "Generate PSX Font Bitmap");
                    font.GenerateBitmapFromFont();
                }

                if (font.FontTexture == null)
                    EditorGUILayout.LabelField(
                        "Click 'Generate Bitmap' to create the font texture.\n" +
                        "If generation fails, check that the font's import settings have " +
                        "'Character' set to 'ASCII Default Set'.", PSXEditorStyles.InfoBox);
            }
            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Manual bitmap card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("Manual Bitmap Source", PSXEditorStyles.CardHeaderStyle);
            PSXEditorStyles.DrawSeparator(2, 4);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("fontTexture"), new GUIContent("Font Texture",
                "256px wide bitmap. Glyphs in ASCII order from 0x20 (space). " +
                "Transparent = background, opaque = foreground."));
            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Glyph metrics card
            PSXEditorStyles.BeginCard();
            EditorGUILayout.LabelField("Glyph Metrics", PSXEditorStyles.CardHeaderStyle);
            PSXEditorStyles.DrawSeparator(2, 4);

            if (font.SourceFont != null && font.FontTexture != null)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.IntField(new GUIContent("Glyph Width", "Auto-derived from font. Re-generate to change."), font.GlyphWidth);
                EditorGUILayout.IntField(new GUIContent("Glyph Height", "Auto-derived from font. Re-generate to change."), font.GlyphHeight);
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.LabelField("Glyph dimensions are auto-derived when generating from a font.\n" +
                                        "Change the Font Size slider and re-generate to adjust.", PSXEditorStyles.InfoBox);
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("glyphWidth"), new GUIContent("Glyph Width",
                    "Width of each glyph cell in pixels. Must divide 256 evenly (4, 8, 16, or 32)."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("glyphHeight"), new GUIContent("Glyph Height",
                    "Height of each glyph cell in pixels."));
            }
            PSXEditorStyles.EndCard();

            EditorGUILayout.Space(4);

            // Layout info card
            PSXEditorStyles.BeginCard();
            int glyphsPerRow = font.GlyphsPerRow;
            int rowCount = font.RowCount;
            int totalH = font.TextureHeight;
            int vramBytes = totalH * 128;

            EditorGUILayout.LabelField(
                $"Layout: {glyphsPerRow} glyphs/row, {rowCount} rows\n" +
                $"Texture: 256 x {totalH} pixels (4bpp)\n" +
                $"VRAM: {vramBytes} bytes ({vramBytes / 1024f:F1} KB)\n" +
                $"Glyph cell: {font.GlyphWidth} x {font.GlyphHeight}",
                PSXEditorStyles.InfoBox);

            if (font.AdvanceWidths != null && font.AdvanceWidths.Length >= 95)
            {
                int minAdv = 255, maxAdv = 0;
                for (int i = 1; i < 95; i++)
                {
                    if (font.AdvanceWidths[i] < minAdv) minAdv = font.AdvanceWidths[i];
                    if (font.AdvanceWidths[i] > maxAdv) maxAdv = font.AdvanceWidths[i];
                }
                EditorGUILayout.LabelField(
                    $"Advance widths: {minAdv}-{maxAdv}px (proportional spacing stored)",
                    PSXEditorStyles.InfoBox);
            }
            else if (font.FontTexture != null)
            {
                EditorGUILayout.LabelField(
                    "No advance widths stored. Click 'Generate Bitmap' to compute them.",
                    PSXEditorStyles.InfoBox);
            }
            PSXEditorStyles.EndCard();

            // Validation
            if (font.FontTexture != null)
            {
                if (font.FontTexture.width != 256)
                {
                    EditorGUILayout.Space(4);
                    PSXEditorStyles.BeginCard();
                    EditorGUILayout.LabelField($"Font texture must be 256 pixels wide (currently {font.FontTexture.width}).", PSXEditorStyles.InfoBox);
                    PSXEditorStyles.EndCard();
                }

                if (256 % font.GlyphWidth != 0)
                {
                    EditorGUILayout.Space(4);
                    PSXEditorStyles.BeginCard();
                    EditorGUILayout.LabelField($"Glyph width ({font.GlyphWidth}) must divide 256 evenly. " +
                                            "Valid values: 4, 8, 16, 32.", PSXEditorStyles.InfoBox);
                    PSXEditorStyles.EndCard();
                }

                // Preview
                EditorGUILayout.Space(4);
                PSXEditorStyles.BeginCard();
                EditorGUILayout.LabelField("Preview", PSXEditorStyles.CardHeaderStyle);
                PSXEditorStyles.DrawSeparator(2, 4);
                Rect previewRect = EditorGUILayout.GetControlRect(false, 64);
                GUI.DrawTexture(previewRect, font.FontTexture, ScaleMode.ScaleToFit);
                PSXEditorStyles.EndCard();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
