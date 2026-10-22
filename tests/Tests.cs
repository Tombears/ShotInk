using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ShotInk
{
    internal static class Tests
    {
        private static int passed;
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Native.EnableDpiAwareness();
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Run("renamed app preserves existing history and settings", TestDataRoot);
                Run("several scroll offsets reconstruct the original pixels", TestStitch);
                Run("unchanged frame does not duplicate content", TestUnchanged);
                Run("unrelated frames are rejected", TestUnrelated);
                Run("changed frame dimensions are rejected", TestDimensions);
                Run("height limit rejects oversized first frame", TestInitialLimit);
                Run("accumulated height is bounded", TestAccumulatedLimit);
                Run("annotations render at original resolution and undo", TestAnnotations);
                Run("inline text supports multiline input, settings, cancellation and export", TestInlineText);
                Run("text moves, scales and rotates with independent undo at different zooms", TestTextTransform);
                Run("mosaic covers partial edge blocks", TestMosaic);
                Run("undo and redo restore additions and annotation erasing", TestUndoRedoErase);
                Run("tool buttons immediately activate defaults and expose independent settings", TestToolOptions);
                Run("oval sliders support bounded dragging and keyboard adjustments", TestOvalSlider);
                Run("OCR selection preserves columns, punctuation and screenshot pixels", TestOcrSelection);
                Run("OCR requests suppress reentry and ignore stale or cancelled results", TestOcrSession);
                Run("mosaic brush follows continuous strokes and keeps sizes independent", TestMosaicBrush);
                Run("mosaic selection ignores brush size and respects pixel size", TestMosaicSelection);
                Run("eraser brush and selection restore only the requested pixels", TestEraserModes);
                Run("overlapping eraser strokes preserve coverage without deleting the whole line", TestEraserOverlap);
                Run("highlighter remains translucent and preserves the original", TestHighlighter);
                Run("floating selection stays open and exports only edited pixels", TestFloatingSelection);
                Run("selection resize and toolbar stay within display bounds", TestSelectionGeometry);
                Run("cancelled selection does not export a screenshot", TestSelectionCancel);
                if (args.Length == 2 && args[0] == "--preview")
                {
                    Run("native screen capture reads a known test window", TestScreenCapture);
                    Run("automatic wheel capture reconstructs a real scrolling window", TestAutomaticCapture);
                    Preview(args[1]);
                }
                Console.WriteLine("PASS: " + passed + " checks");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        }
        private static void TestDataRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "ShotInk-tests-" + Guid.NewGuid().ToString("N"));
            var current = Path.Combine(root, "ShotInk");
            var legacy = Path.Combine(root, "SnipasteLite");
            try
            {
                Assert(MainForm.ResolveDataRoot(root) == current, "Fresh installs use the wrong data directory.");
                Directory.CreateDirectory(Path.Combine(legacy, "captures"));
                var settings = Path.Combine(legacy, "settings.ini");
                File.WriteAllText(settings, "save=0");
                using (var image = new Bitmap(2, 2)) image.Save(Path.Combine(legacy, "captures", "saved.png"), ImageFormat.Png);
                Assert(MainForm.ResolveDataRoot(root) == legacy, "Upgrade did not reuse existing data.");
                using (var home = new MainForm(MainForm.ResolveDataRoot(root), false))
                {
                    Assert(!Field<CheckBox>(home, "autoSave").Checked, "Upgrade lost the auto-save preference.");
                    Assert(Field<ListView>(home, "history").Items.Count == 1, "Upgrade lost the screenshot history.");
                    Assert(home.Text.StartsWith("ShotInk"), "Window title still uses the previous name.");
                }
                Assert(File.ReadAllText(settings) == "save=0", "Opening the app changed the saved preference.");
                Directory.CreateDirectory(current);
                Assert(MainForm.ResolveDataRoot(root) == current, "An existing ShotInk directory was ignored.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void Run(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
        private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static Bitmap Pattern(int width, int height, int seed)
        {
            var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            var random = new Random(seed);
            var data = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[Math.Abs(data.Stride)];
                for (int y = 0; y < height; y++) { random.NextBytes(row); System.Runtime.InteropServices.Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), row.Length); }
            }
            finally { result.UnlockBits(data); }
            return result;
        }
        private static void EqualPixels(Bitmap expected, Bitmap actual, int height)
        {
            Assert(actual.Width == expected.Width && actual.Height == height, "Incorrect output dimensions.");
            for (int y = 0; y < height; y++)
                for (int x = 0; x < actual.Width; x++)
                    Assert(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), "Pixel mismatch at " + x + ", " + y);
        }
        private static void TestStitch()
        {
            using (var content = Pattern(113, 1200, 17))
            using (var stitcher = new VerticalStitcher())
            {
                int frameHeight = 320;
                int[] positions = { 0, 73, 197, 198, 411, 580, 601 };
                foreach (int position in positions)
                    using (var frame = content.Clone(new Rectangle(0, position, content.Width, frameHeight), PixelFormat.Format24bppRgb))
                        Assert(stitcher.Add(frame) == StitchStatus.Added, "Valid frame was rejected at " + position);
                using (var result = stitcher.Build()) EqualPixels(content, result, positions[positions.Length - 1] + frameHeight);
                Assert(stitcher.Frames == positions.Length, "Wrong frame count.");
            }
        }
        private static void TestUnchanged()
        {
            using (var frame = Pattern(90, 200, 3))
            using (var stitcher = new VerticalStitcher())
            { stitcher.Add(frame); Assert(stitcher.Add(frame) == StitchStatus.Unchanged, "Duplicate accepted."); Assert(stitcher.Height == 200, "Height changed on duplicate."); }
        }
        private static void TestUnrelated()
        {
            using (var before = Pattern(80, 200, 3))
            using (var after = Pattern(80, 200, 99))
            using (var stitcher = new VerticalStitcher())
            { stitcher.Add(before); Assert(stitcher.Add(after) == StitchStatus.Unmatched, "Unrelated content accepted."); Assert(stitcher.Height == 200, "Rejected image changed output."); }
        }
        private static void TestDimensions()
        {
            using (var before = new Bitmap(80, 200))
            using (var after = new Bitmap(81, 200))
            using (var stitcher = new VerticalStitcher())
            {
                stitcher.Add(before); bool rejected = false;
                try { stitcher.Add(after); } catch (ArgumentException) { rejected = true; }
                Assert(rejected, "Size change accepted.");
            }
        }
        private static void TestInitialLimit()
        {
            using (var oversized = new Bitmap(1, VerticalStitcher.MaxHeight + 1))
            using (var stitcher = new VerticalStitcher())
            { Assert(stitcher.Add(oversized) == StitchStatus.LimitReached, "Oversized first frame accepted."); Assert(stitcher.Frames == 0, "Rejected initial frame stored."); }
        }
        private static void TestAccumulatedLimit()
        {
            using (var content = Pattern(8, 31000, 19))
            using (var stitcher = new VerticalStitcher())
            {
                bool reached = false;
                for (int y = 0; y < 30500; y += 200)
                {
                    using (var frame = content.Clone(new Rectangle(0, y, 8, 400), PixelFormat.Format24bppRgb))
                    {
                        var status = stitcher.Add(frame);
                        if (status == StitchStatus.LimitReached) { reached = true; break; }
                        Assert(status == StitchStatus.Added, "Limit test frame mismatch.");
                    }
                }
                Assert(reached && stitcher.Height <= VerticalStitcher.MaxHeight, "Accumulated limit not enforced.");
            }
        }
        private static void TestAnnotations()
        {
            using (var canvas = new AnnotationCanvas(new Bitmap(120, 100)))
            {
                using (var g = Graphics.FromImage(canvas.Image)) g.Clear(Color.White);
                var rectangle = new Mark { Tool = DrawTool.Rectangle, Color = Color.Red, Width = 3 };
                rectangle.Points.Add(new PointF(80, 80)); rectangle.Points.Add(new PointF(20, 20));
                canvas.AddMark(rectangle); canvas.SetZoom(0.25F);
                using (var rendered = canvas.Render())
                { Assert(rendered.Size == new Size(120, 100), "Zoom changed export dimensions."); Assert(rendered.GetPixel(20, 50).R > 240 && rendered.GetPixel(20, 50).G < 40, "Annotation missing."); }
                Assert(canvas.Image.GetPixel(20, 50).ToArgb() == Color.White.ToArgb(), "Source image mutated.");
                canvas.Undo(); Assert(canvas.Marks.Count == 0, "Undo failed.");
            }
        }
        private static void TestInlineText()
        {
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(30, 30, 900, 560)))
            {
                overlay.Show(); Select(overlay);
                var toolbar = Field<CaptureToolbar>(overlay, "toolbar"); var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                var options = Field<AnnotationOptions>(toolbar, "options"); toolbar.SelectTool(DrawTool.Text);
                Option<NumericUpDown>(options, "text-font-size").Value = 32;
                options.Ink = Color.Blue; options.ApplyTo(canvas);
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, 100, 80);
                var input = Field<TextBox>(canvas, "textEditor");
                Assert(input != null && input.Parent == canvas && input.Multiline, "Text tool did not open a native inline editor.");
                input.Text = "中文 Abc123\r\n第二行";
                Assert(canvas.Marks[0].FontSize == 32 && canvas.Marks[0].Color == Color.Blue, "Text ignored selected size or color.");
                Option<NumericUpDown>(options, "text-font-size").Value = 40;
                Assert(canvas.Marks[0].FontSize == 40 && input.Font.Size > 39, "Text settings did not update the active editor.");
                ToolPreview(overlay, "text-input");
                input.Focus(); var enter = new KeyEventArgs(Keys.Enter);
                Assert(canvas.HandleTextKey(enter) && !enter.SuppressKeyPress && !overlay.IsDisposed, "Enter exported the screenshot instead of allowing a newline.");
                var copy = new KeyEventArgs(Keys.Control | Keys.C);
                Assert(canvas.HandleTextKey(copy) && !copy.SuppressKeyPress, "Copy shortcut was intercepted while typing.");
                using (var rendered = canvas.Render())
                {
                    Assert(Field<TextBox>(canvas, "textEditor") == null && canvas.Marks.Count == 1, "Export lost or duplicated the pending text.");
                    Assert(canvas.Marks[0].Text.Contains("第二行"), "Export lost multiline text.");
                    canvas.Tool = DrawTool.None;
                    using (var withoutSelection = canvas.Render()) EqualPixels(rendered, withoutSelection, rendered.Height);
                }
                toolbar.SelectTool(DrawTool.Text);
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, 120, 90); Mouse(canvas, "OnMouseUp", MouseButtons.Left, 120, 90);
                canvas.Marks[0].TextAngle = 20; ToolPreview(overlay, "text-transform"); canvas.Marks[0].TextAngle = 0;
                canvas.Undo(); Assert(canvas.Marks.Count == 0, "Typing and changing settings were not undone as one edit.");
                canvas.Redo(); Assert(canvas.Marks.Count == 1 && canvas.Marks[0].FontSize == 40, "Redo lost text or formatting.");
                toolbar.SelectTool(DrawTool.Text);
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, 420, 250);
                input = Field<TextBox>(canvas, "textEditor"); input.Text = "取消"; input.Focus();
                canvas.HandleTextKey(new KeyEventArgs(Keys.Escape));
                Assert(canvas.Marks.Count == 1 && !overlay.IsDisposed, "Cancelling text closed the screenshot or retained the draft.");
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, 420, 250); canvas.CommitTextEdit();
                Assert(canvas.Marks.Count == 1, "Empty editor created a text annotation.");
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, 120, 90);
                Mouse(canvas, "OnMouseUp", MouseButtons.Left, 120, 90);
                Mouse(canvas, "OnMouseDoubleClick", MouseButtons.Left, 120, 90);
                input = Field<TextBox>(canvas, "textEditor"); input.Text = "修改后的内容"; input.Focus();
                canvas.HandleTextKey(new KeyEventArgs(Keys.Escape));
                Assert(canvas.Marks[0].Text.Contains("第二行"), "Cancelling re-edit did not restore the previous text.");
                overlay.Close();
            }
        }
        private static void TextGesture(AnnotationCanvas canvas, PointF from, PointF to)
        { Gesture(canvas, new Point((int)Math.Round(from.X * canvas.Zoom), (int)Math.Round(from.Y * canvas.Zoom)), new Point((int)Math.Round(to.X * canvas.Zoom), (int)Math.Round(to.Y * canvas.Zoom))); }
        private static void TestTextTransform()
        {
            foreach (float zoom in new[] { 1F, 0.5F })
            using (var host = new Form { ClientSize = new Size(900, 650) })
            using (var canvas = new AnnotationCanvas(new Bitmap(900, 650)))
            {
                host.Controls.Add(canvas); host.Show(); canvas.SetZoom(zoom); canvas.Tool = DrawTool.Text;
                using (var g = Graphics.FromImage(canvas.Image)) g.Clear(Color.White);
                var text = new Mark { Tool = DrawTool.Text, Text = "可编辑文字 Abc", Color = Color.Red, FontSize = 30, TextAngle = 0 };
                text.Points.Add(new PointF(200, 200)); text.TextSize = TextGeometry.Measure(text); canvas.AddMark(text);
                var center = TextGeometry.Center(text);
                TextGesture(canvas, center, new PointF(center.X + 35, center.Y + 25));
                Assert(Math.Abs(text.Points[0].X - 235) < 2 && Math.Abs(text.Points[0].Y - 225) < 2, "Text move ignored image coordinates.");
                canvas.Undo(); Assert(canvas.Marks[0].Points[0] == new PointF(200, 200), "Move corrupted an undo snapshot.");
                canvas.Redo(); text = canvas.Marks[0];
                TextGesture(canvas, TextGeometry.Center(text), TextGeometry.Center(text));
                var corners = TextGeometry.Corners(text); var size = TextGeometry.Size(text);
                TextGesture(canvas, corners[2], new PointF(corners[2].X + size.Width * 0.5F, corners[2].Y + size.Height * 0.5F));
                Assert(Math.Abs(text.FontSize - 45) < 1, "Corner scaling did not scale the text.");
                Assert(DistanceForTest(TextGeometry.Corners(text)[0], corners[0]) < 2, "Scaling moved the opposite corner.");
                canvas.Undo(); Assert(Math.Abs(canvas.Marks[0].FontSize - 30) < 0.1, "Scale undo lost the old font size.");
                canvas.Redo(); text = canvas.Marks[0]; TextGesture(canvas, TextGeometry.Center(text), TextGeometry.Center(text));
                center = TextGeometry.Center(text);
                var rotation = TextGeometry.World(text, new PointF(TextGeometry.Size(text).Width / 2, -24 / zoom));
                float radius = (float)DistanceForTest(center, rotation);
                TextGesture(canvas, rotation, new PointF(center.X + radius, center.Y));
                Assert(Math.Abs(text.TextAngle - 90) < 2, "Rotation handle did not rotate around the text center.");
                var local = new PointF(12, 14); var roundTrip = TextGeometry.Local(text, TextGeometry.World(text, local));
                Assert(DistanceForTest(local, roundTrip) < 0.01, "Rotated text hit-testing does not invert the drawing transform.");
                using (var rotated = canvas.Render())
                {
                    canvas.Tool = DrawTool.None;
                    using (var plain = canvas.Render()) EqualPixels(rotated, plain, plain.Height);
                    int red = 0;
                    for (int y = 0; y < rotated.Height; y++) for (int x = 0; x < rotated.Width; x++)
                    { var pixel = rotated.GetPixel(x, y); if (pixel.R > 180 && pixel.G < 100) red++; }
                    Assert(red > 100, "Rotated text was absent from exported pixels.");
                }
                canvas.Undo(); Assert(Math.Abs(canvas.Marks[0].TextAngle) < 0.1, "Rotation undo failed.");
                canvas.Redo(); Assert(Math.Abs(canvas.Marks[0].TextAngle - 90) < 2, "Rotation redo failed.");
                host.Close();
            }
        }
        private static double DistanceForTest(PointF a, PointF b)
        { return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }
        private static void TestMosaic()
        {
            using (var canvas = new AnnotationCanvas(Pattern(90, 100, 32)))
            {
                var mark = new Mark { Tool = DrawTool.Mosaic, Width = 3 };
                mark.Points.Add(new PointF(10, 10)); mark.Points.Add(new PointF(32, 33)); canvas.AddMark(mark);
                using (var rendered = canvas.Render())
                {
                    Assert(rendered.GetPixel(31, 32).ToArgb() == canvas.Image.GetPixel(32, 32).ToArgb(), "Partial edge block was left exposed.");
                    Assert(rendered.GetPixel(9, 9).ToArgb() == canvas.Image.GetPixel(9, 9).ToArgb(), "Mosaic modified pixels outside region.");
                }
            }
        }
        private static void TestScreenCapture()
        {
            using (var scene = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new Point(80, 80), Size = new Size(240, 180), BackColor = Color.FromArgb(37, 91, 163), TopMost = true, ShowInTaskbar = false })
            {
                scene.Show(); Application.DoEvents(); System.Threading.Thread.Sleep(250); Application.DoEvents();
                var region = scene.RectangleToScreen(new Rectangle(20, 20, 100, 80));
                using (var screenshot = Native.Capture(region))
                {
                    var color = screenshot.GetPixel(50, 40);
                    Assert(Math.Abs(color.R - 37) < 5 && Math.Abs(color.G - 91) < 5 && Math.Abs(color.B - 163) < 5, "Native capture did not match the test window.");
                }
                scene.Close();
            }
        }
        private static Mark RectangleMark()
        {
            var mark = new Mark { Tool = DrawTool.Rectangle, Color = Color.Red, Width = 3 };
            mark.Points.Add(new PointF(20, 20)); mark.Points.Add(new PointF(80, 80)); return mark;
        }
        private static void TestUndoRedoErase()
        {
            using (var canvas = new AnnotationCanvas(new Bitmap(120, 100)))
            {
                using (var g = Graphics.FromImage(canvas.Image)) g.Clear(Color.White);
                canvas.AddMark(RectangleMark());
                canvas.Undo(); Assert(canvas.Marks.Count == 0 && canvas.CanRedo, "Undo did not make redo available.");
                canvas.Redo(); Assert(canvas.Marks.Count == 1, "Redo did not restore the mark.");
                canvas.Tool = DrawTool.Eraser; canvas.EraserBrushSize = 12;
                Gesture(canvas, new Point(20, 50), new Point(20, 50));
                using (var image = canvas.Render())
                {
                    Assert(image.GetPixel(20, 50).ToArgb() == Color.White.ToArgb(), "Eraser did not restore original pixels.");
                    Assert(image.GetPixel(20, 25).R > 240 && image.GetPixel(20, 25).G < 40, "Eraser removed the entire annotation.");
                }
                canvas.Undo(); Assert(canvas.Marks.Count == 1, "Undo did not restore erased annotation.");
                using (var image = canvas.Render()) Assert(image.GetPixel(20, 50).G < 40, "Undo did not restore erased pixels.");
                canvas.Redo();
                using (var image = canvas.Render()) Assert(image.GetPixel(20, 50).ToArgb() == Color.White.ToArgb(), "Redo did not restore erasure.");
                canvas.Undo(); canvas.AddMark(RectangleMark()); Assert(!canvas.CanRedo, "New edits kept an obsolete redo branch.");
                Assert(canvas.Image.GetPixel(20, 50).ToArgb() == Color.White.ToArgb(), "Eraser changed the original screenshot.");
            }
        }
        private static void Gesture(AnnotationCanvas canvas, params Point[] points)
        {
            Mouse(canvas, "OnMouseDown", MouseButtons.Left, points[0].X, points[0].Y);
            for (int i = 1; i < points.Length - 1; i++) Mouse(canvas, "OnMouseMove", MouseButtons.Left, points[i].X, points[i].Y);
            var last = points[points.Length - 1]; Mouse(canvas, "OnMouseUp", MouseButtons.Left, last.X, last.Y);
        }
        private static T Option<T>(Control parent, string name) where T : Control
        { return (T)parent.Controls.Find(name, true)[0]; }
        private static void TestToolOptions()
        {
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(0, 0, 900, 600)))
            {
                overlay.Show(); Select(overlay);
                var toolbar = Field<CaptureToolbar>(overlay, "toolbar");
                var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                var tools = Field<System.Collections.Generic.Dictionary<DrawTool, ToolStripItem>>(toolbar, "tools");
                var options = Field<AnnotationOptions>(toolbar, "options");
                var strip = Field<ToolStrip>(toolbar, "strip");
                var recognize = Field<ToolStripButton>(toolbar, "recognize");
                Assert(strip.Items.IndexOf(recognize) == strip.Items.IndexOf(tools[DrawTool.Eraser]) + 1, "OCR button is not immediately right of the eraser.");
                int requests = 0;
                using (var isolated = new CaptureToolbar())
                {
                    isolated.RecognitionClicked += delegate { requests++; };
                    Field<ToolStripButton>(isolated, "recognize").PerformClick();
                }
                Assert(requests == 1, "OCR button did not request recognition.");
                toolbar.SetRecognitionBusy(true); Assert(!recognize.Enabled, "Busy OCR button allowed duplicate clicks.");
                toolbar.SetRecognitionBusy(false);
                tools[DrawTool.Mosaic].PerformClick();
                Assert(canvas.Tool == DrawTool.Mosaic && canvas.MosaicMode == ApplyMode.Brush && options.Visible, "Mosaic required a second click to activate the brush.");
                ToolPreview(overlay, "mosaic");
                var sliders = options.Controls.Find("mosaic-size", true);
                Assert(sliders.Length == 1 && Option<OvalSlider>(options, "mosaic-size").Parent.Top < Option<OvalSlider>(options, "mosaic-brush-size").Parent.Top, "Mosaic sliders are missing or out of order.");
                Option<OvalSlider>(options, "mosaic-size").Value = 22;
                Option<OvalSlider>(options, "mosaic-brush-size").Value = 50;
                Assert(canvas.MosaicSize == 22 && canvas.MosaicBrushSize == 50, "Mosaic sliders did not update separate settings.");
                Option<Button>(options, "mode-rectangle").PerformClick();
                Assert(canvas.MosaicMode == ApplyMode.Rectangle && !Option<OvalSlider>(options, "mosaic-brush-size").Enabled, "Mosaic selection mode did not activate.");
                tools[DrawTool.Mosaic].PerformClick();
                Assert(canvas.MosaicMode == ApplyMode.Brush && Option<OvalSlider>(options, "mosaic-brush-size").Enabled, "Mosaic main button did not immediately restore its default brush.");
                tools[DrawTool.Eraser].PerformClick();
                Assert(canvas.Tool == DrawTool.Eraser && canvas.EraserMode == ApplyMode.Brush && options.Visible, "Eraser required a second click to activate the brush.");
                ToolPreview(overlay, "eraser");
                Option<OvalSlider>(options, "eraser-brush-size").Value = 62;
                Assert(canvas.EraserBrushSize == 62 && canvas.MosaicBrushSize == 50 && canvas.MosaicSize == 22, "Eraser size changed mosaic settings.");
                Option<Button>(options, "mode-rectangle").PerformClick();
                Assert(canvas.EraserMode == ApplyMode.Rectangle, "Eraser selection mode did not activate.");
                tools[DrawTool.Eraser].PerformClick();
                Assert(canvas.EraserMode == ApplyMode.Brush && canvas.Cursor == Cursors.Cross, "Eraser main button did not immediately restore its default brush.");
                tools[DrawTool.Rectangle].PerformClick();
                Assert(canvas.Tool == DrawTool.Rectangle && options.Visible, "Shape button did not select its default tool.");
                Option<Button>(options, "shape-ellipse").PerformClick();
                Assert(canvas.Tool == DrawTool.Ellipse, "Shape options did not switch to ellipse.");
                tools[DrawTool.Rectangle].PerformClick();
                Assert(canvas.Tool == DrawTool.Rectangle && canvas.Cursor == Cursors.Cross, "Shape button did not immediately restore its default rectangle.");
                tools[DrawTool.Arrow].PerformClick();
                Assert(canvas.Tool == DrawTool.Arrow, "Arrow button did not select its default tool.");
                Option<Button>(options, "line-straight").PerformClick();
                Assert(canvas.Tool == DrawTool.Line, "Line options did not switch to straight line.");
                tools[DrawTool.Arrow].PerformClick();
                Assert(canvas.Tool == DrawTool.Arrow && canvas.Cursor == Cursors.Cross, "Line button did not immediately restore its default arrow.");
                tools[DrawTool.Pen].PerformClick(); tools[DrawTool.Pen].PerformClick();
                Assert(canvas.Tool == DrawTool.Pen && options.Visible, "Clicking an active tool disabled it instead of showing options.");
                tools[DrawTool.None].PerformClick();
                Assert(canvas.Tool == DrawTool.None && !options.Visible && canvas.Cursor == Cursors.SizeAll, "Move selection mode was lost.");
                overlay.Close();
            }
        }
        private static void TestOvalSlider()
        {
            using (var slider = new OvalSlider { Minimum = 4, Maximum = 48, Value = 14 })
            {
                int changes = 0; slider.ValueChanged += delegate { changes++; };
                var thumb = slider.ThumbBounds;
                Assert(thumb.Width > thumb.Height, "Slider thumb is not an ellipse.");
                Mouse(slider, "OnMouseDown", MouseButtons.Left, (int)(thumb.Left + thumb.Width / 2), (int)(thumb.Top + thumb.Height / 2));
                Mouse(slider, "OnMouseMove", MouseButtons.Left, 10000, 13);
                Mouse(slider, "OnMouseUp", MouseButtons.Left, 10000, 13);
                Assert(slider.Value == 48 && changes > 0 && !slider.Capture, "Slider drag did not clamp at its maximum or release capture.");
                Mouse(slider, "OnMouseDown", MouseButtons.Left, 0, 13); Mouse(slider, "OnMouseUp", MouseButtons.Left, 0, 13);
                Assert(slider.Value == 4, "Track click did not reach the minimum.");
                Key(slider, Keys.Right); Assert(slider.Value == 5, "Arrow key did not increment the slider.");
                Key(slider, Keys.PageUp); Assert(slider.Value == 9, "Page Up did not apply the large increment.");
                Key(slider, Keys.End); Assert(slider.Value == 48, "End key did not reach the maximum.");
                slider.Enabled = false; Mouse(slider, "OnMouseDown", MouseButtons.Left, 0, 13); Key(slider, Keys.Home);
                Assert(slider.Value == 48, "Disabled slider changed its value.");
                slider.Enabled = true; slider.Minimum = 7; slider.Maximum = 7;
                Assert(slider.Value == 7 && !float.IsNaN(slider.ThumbBounds.X), "Zero-range slider produced invalid geometry.");
            }
        }
        private static void Key(Control control, Keys key)
        { control.GetType().GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(control, new object[] { new KeyEventArgs(key) }); }
        private static OcrLine RecognizedLine(string text, int left, int top)
        {
            var characters = new OcrCharacter[text.Length];
            for (int i = 0; i < text.Length; i++) characters[i] = new OcrCharacter { Text = text[i].ToString(), Start = i, Length = 1, Box = new[] { (float)(left + i * 10), (float)top, 10F, 20F } };
            return new OcrLine { Text = text, Box = new[] { (float)left, (float)top, (float)(text.Length * 10), 20F }, Characters = characters };
        }
        private static OcrDocument SelectionDocument(int width, int height)
        {
            return new OcrDocument { Width = width, Height = height, Lines = new[] {
                RecognizedLine("编号A01", 10, 10), RecognizedLine("release-sit", 200, 10),
                RecognizedLine("编号B02", 10, 50), RecognizedLine("hotfix-20261021", 200, 50),
                RecognizedLine("中文123，A_B-9.50!", 10, 100) } };
        }
        private static void TestOcrSelection()
        {
            var model = new OcrSelectionModel(SelectionDocument(700, 370));
            model.SelectRectangle(new RectangleF(195, 0, 170, 80));
            Assert(model.SelectedText == "release-sit" + Environment.NewLine + "hotfix-20261021", "Column selection included neighboring table cells.");
            model.SelectWord(new PointF(225, 20), false);
            Assert(model.SelectedText == "release-sit", "Click did not select a punctuation-bearing English word.");
            model.SelectRectangle(new RectangleF(200, 60, 150, 0));
            Assert(model.SelectedText == "hotfix-20261021", "A horizontal drag with no vertical motion selected no text.");
            model.SelectWord(new PointF(35, 110), true);
            Assert(model.SelectedText == "中文123，A_B-9.50!", "Line selection lost Chinese, digits or punctuation.");
            model.SelectAll(); Assert(model.SelectedText.StartsWith("编号A01\trelease-sit"), "Copy-all did not preserve table columns.");
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(0, 0, 900, 600)))
            {
                Select(overlay);
                var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                canvas.SetTextRecognition(SelectionDocument(700, 370));
                var before = Field<Rectangle>(overlay, "selection");
                Gesture(canvas, new Point(195, 0), new Point(365, 80));
                Assert(canvas.HasSelectedText && Field<Rectangle>(overlay, "selection") == before, "Selecting text moved the screenshot selection.");
                var copyAll = new KeyEventArgs(Keys.Control | Keys.A);
                Assert(canvas.HandleRecognitionKey(copyAll) && copyAll.SuppressKeyPress, "OCR mode did not consume select-all.");
                using (var image = canvas.Render()) EqualPixels(canvas.Image, image, image.Height);
                canvas.SetZoom(0.5F);
                Gesture(canvas, new Point(98, 0), new Point(182, 40));
                Assert(Field<OcrSelectionModel>(canvas, "recognition").SelectedText == "release-sit" + Environment.NewLine + "hotfix-20261021", "Zoomed selection used screen coordinates as image coordinates.");
                canvas.GetType().GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(canvas,
                    new object[] { new MouseEventArgs(MouseButtons.Left, 2, 18, 55, 0) });
                Mouse(canvas, "OnMouseDoubleClick", MouseButtons.Left, 18, 55); Mouse(canvas, "OnMouseUp", MouseButtons.Left, 18, 55);
                Assert(Field<OcrSelectionModel>(canvas, "recognition").SelectedText == "中文123，A_B-9.50!", "Mouse-up replaced double-click line selection with a single word.");
                Field<CaptureToolbar>(overlay, "toolbar").SelectTool(DrawTool.Pen);
                Assert(!canvas.HasTextRecognition && canvas.Tool == DrawTool.Pen, "Drawing tools did not exit OCR selection.");
            }
        }
        private sealed class FakeRecognizer : ITextRecognizer
        {
            internal readonly System.Threading.Tasks.TaskCompletionSource<OcrDocument> Completion = new System.Threading.Tasks.TaskCompletionSource<OcrDocument>();
            internal int Calls;
            internal System.Threading.CancellationToken Token;
            public System.Threading.Tasks.Task<OcrDocument> RecognizeAsync(Bitmap image, System.Threading.CancellationToken cancellation)
            { Calls++; Token = cancellation; Assert(image.Width == 20 && image.Height == 20, "OCR received the wrong snapshot."); return Completion.Task; }
        }
        private static void Pump(System.Threading.Tasks.Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
            Assert(task.IsCompleted, "OCR continuation did not complete."); task.GetAwaiter().GetResult();
        }
        private static void TestOcrSession()
        {
            using (var owner = new Form())
            {
                int revision = 1, results = 0, errors = 0; bool busy = false;
                var engine = new FakeRecognizer();
                using (var session = new TextRecognitionSession(owner, delegate { return new Bitmap(20, 20); }, delegate { return revision; }, delegate(bool state) { busy = state; }, engine,
                    delegate { results++; }, delegate { errors++; }))
                {
                    var request = session.RecognizeAsync(); session.RecognizeAsync().GetAwaiter().GetResult();
                    Assert(engine.Calls == 1 && busy, "OCR did not suppress duplicate requests.");
                    engine.Completion.SetResult(SelectionDocument(20, 20)); Pump(request);
                    Assert(results == 1 && errors == 0 && !busy, "OCR success did not restore the button and publish its result.");
                }
                engine = new FakeRecognizer();
                using (var session = new TextRecognitionSession(owner, delegate { return new Bitmap(20, 20); }, delegate { return revision; }, delegate(bool state) { busy = state; }, engine,
                    delegate { results++; }, delegate { errors++; }))
                {
                    var request = session.RecognizeAsync(); revision++;
                    engine.Completion.SetResult(SelectionDocument(20, 20)); Pump(request);
                    Assert(results == 1 && errors == 1 && !busy, "OCR result was applied to a changed screenshot.");
                }
                engine = new FakeRecognizer();
                var cancelled = new TextRecognitionSession(owner, delegate { return new Bitmap(20, 20); }, delegate { return revision; }, delegate(bool state) { busy = state; }, engine,
                    delegate { results++; }, delegate { errors++; });
                var pending = cancelled.RecognizeAsync(); cancelled.Dispose();
                Assert(engine.Token.IsCancellationRequested, "Closing OCR did not cancel its request.");
                engine.Completion.SetResult(SelectionDocument(20, 20)); Pump(pending);
                Assert(results == 1 && errors == 1, "Late OCR result escaped cancellation.");
                engine = new FakeRecognizer();
                using (var session = new TextRecognitionSession(owner, delegate { return new Bitmap(20, 20); }, delegate { return revision; }, delegate(bool state) { busy = state; }, engine,
                    delegate { results++; }, delegate { errors++; }))
                {
                    var failed = session.RecognizeAsync(); engine.Completion.SetException(new InvalidOperationException("model missing")); Pump(failed);
                    Assert(errors == 2 && !busy, "OCR failure left the UI busy.");
                }
            }
        }

        private static void TestMosaicBrush()
        {
            using (var canvas = new AnnotationCanvas(Pattern(160, 140, 31)))
            {
                canvas.Tool = DrawTool.Mosaic; canvas.MosaicBrushSize = 20; canvas.MosaicSize = 8; canvas.SetZoom(0.5F);
                Gesture(canvas, new Point(10, 20), new Point(60, 20), new Point(60, 50), new Point(10, 50));
                using (var image = canvas.Render())
                {
                    Assert(image.GetPixel(60, 40).ToArgb() != canvas.Image.GetPixel(60, 40).ToArgb(), "Fast brush movement left gaps in mosaic.");
                    Assert(image.GetPixel(120, 70).ToArgb() != canvas.Image.GetPixel(120, 70).ToArgb(), "Brush bounds ignored intermediate points.");
                    Assert(image.GetPixel(60, 70).ToArgb() == canvas.Image.GetPixel(60, 70).ToArgb(), "Mosaic filled the bounding rectangle instead of following the brush.");
                    Assert(image.GetPixel(60, 55).ToArgb() == canvas.Image.GetPixel(60, 55).ToArgb(), "Mosaic brush was larger than its configured size.");
                    canvas.MosaicSize = 32; canvas.MosaicBrushSize = 80;
                    using (var unchanged = canvas.Render()) EqualPixels(image, unchanged, image.Height);
                }
                Assert(canvas.Marks.Count == 1 && canvas.Marks[0].MosaicSize == 8 && canvas.Marks[0].BrushSize == 20, "Stroke settings were not captured independently.");
                canvas.Undo();
                using (var image = canvas.Render()) EqualPixels(canvas.Image, image, canvas.Image.Height);
                canvas.Redo(); Assert(canvas.Marks.Count == 1, "Mosaic stroke did not redo as one action.");
                canvas.SetZoom(1); canvas.MosaicSize = 8; canvas.MosaicBrushSize = 40;
                Gesture(canvas, new Point(20, 40), new Point(120, 40));
                using (var image = canvas.Render()) Assert(image.GetPixel(60, 55).ToArgb() != canvas.Image.GetPixel(60, 55).ToArgb(), "Brush size did not change the painted area.");
                Gesture(canvas, new Point(0, 0), new Point(0, 0));
                using (var image = canvas.Render()) Assert(image.GetPixel(0, 0).ToArgb() != canvas.Image.GetPixel(0, 0).ToArgb(), "Single click at image edge did not apply mosaic.");
            }
        }
        private static void ToolPreview(CaptureOverlay overlay, string tool)
        {
            Application.DoEvents();
            using (var image = new Bitmap(overlay.Width, overlay.Height))
            {
                overlay.DrawToBitmap(image, overlay.ClientRectangle);
                image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-" + tool + "-options.png"), ImageFormat.Png);
            }
        }
        private static void TestMosaicSelection()
        {
            using (var small = new AnnotationCanvas(Pattern(100, 80, 33)))
            using (var large = new AnnotationCanvas(Pattern(100, 80, 33)))
            {
                foreach (var canvas in new[] { small, large }) { canvas.Tool = DrawTool.Mosaic; canvas.MosaicMode = ApplyMode.Rectangle; canvas.MosaicSize = 8; }
                small.MosaicBrushSize = 4; large.MosaicBrushSize = 100;
                Gesture(small, new Point(80, 60), new Point(10, 10)); Gesture(large, new Point(80, 60), new Point(10, 10));
                using (var a = small.Render())
                using (var b = large.Render())
                {
                    EqualPixels(a, b, a.Height);
                    Assert(a.GetPixel(30, 30).ToArgb() == a.GetPixel(31, 31).ToArgb(), "Mosaic size did not produce blocks.");
                    Assert(a.GetPixel(9, 30).ToArgb() == small.Image.GetPixel(9, 30).ToArgb(), "Selection mosaic changed pixels outside the box.");
                    small.Undo(); small.MosaicSize = 24; Gesture(small, new Point(80, 60), new Point(10, 10));
                    using (var changed = small.Render()) Assert(changed.GetPixel(30, 30).ToArgb() != a.GetPixel(30, 30).ToArgb(), "Mosaic size did not affect pixelation.");
                }
            }
        }
        private static void TestEraserModes()
        {
            using (var canvas = new AnnotationCanvas(new Bitmap(160, 120)))
            {
                using (var g = Graphics.FromImage(canvas.Image)) g.Clear(Color.White);
                var line = new Mark { Tool = DrawTool.Line, Color = Color.Red, Width = 4 };
                line.Points.Add(new PointF(10, 50)); line.Points.Add(new PointF(150, 50)); canvas.AddMark(line);
                canvas.Tool = DrawTool.Eraser; canvas.EraserBrushSize = 20;
                Gesture(canvas, new Point(80, 10), new Point(80, 90));
                using (var image = canvas.Render())
                {
                    Assert(image.GetPixel(80, 50).ToArgb() == Color.White.ToArgb(), "Eraser skipped a fast stroke between events.");
                    Assert(image.GetPixel(100, 50).G < 40, "Small eraser removed pixels outside its brush.");
                }
                canvas.Undo(); Assert(canvas.Marks.Count == 1, "Eraser stroke was split into multiple undo steps.");
                canvas.EraserBrushSize = 60; Gesture(canvas, new Point(80, 10), new Point(80, 90));
                using (var image = canvas.Render()) Assert(image.GetPixel(100, 50).ToArgb() == Color.White.ToArgb(), "Eraser brush size did not affect coverage.");
                canvas.Undo(); canvas.EraserMode = ApplyMode.Rectangle;
                Gesture(canvas, new Point(75, 70), new Point(20, 30));
                using (var image = canvas.Render())
                {
                    Assert(image.GetPixel(40, 50).ToArgb() == Color.White.ToArgb(), "Selection eraser did not restore the selected area.");
                    Assert(image.GetPixel(100, 50).G < 40, "Selection eraser removed the whole mark.");
                }
                canvas.Undo(); canvas.Tool = DrawTool.Mosaic; canvas.MosaicMode = ApplyMode.Rectangle;
                // Verify erasing mosaic restores the frozen pixels, rather than painting a color.
                canvas.Reframe(Pattern(160, 120, 42), Point.Empty);
                Gesture(canvas, new Point(20, 20), new Point(140, 100));
                canvas.Tool = DrawTool.Eraser; Gesture(canvas, new Point(30, 30), new Point(70, 80));
                using (var image = canvas.Render()) Assert(image.GetPixel(50, 50).ToArgb() == canvas.Image.GetPixel(50, 50).ToArgb(), "Erasing mosaic did not restore the original screenshot.");
                canvas.Redo();
                canvas.Undo();
                using (var image = canvas.Render()) Assert(image.GetPixel(50, 50).ToArgb() != canvas.Image.GetPixel(50, 50).ToArgb(), "Undo did not restore erased mosaic.");
            }
        }

        private static void TestEraserOverlap()
        {
            using (var canvas = new AnnotationCanvas(new Bitmap(160, 120)))
            {
                using (var g = Graphics.FromImage(canvas.Image)) g.Clear(Color.White);
                var line = new Mark { Tool = DrawTool.Line, Color = Color.Red, Width = 6 };
                line.Points.Add(new PointF(10, 50)); line.Points.Add(new PointF(150, 50)); canvas.AddMark(line);
                canvas.Tool = DrawTool.Eraser; canvas.EraserBrushSize = 12;
                Gesture(canvas, new Point(60, 20), new Point(100, 80), new Point(60, 80), new Point(100, 20), new Point(60, 20));
                using (var image = canvas.Render())
                {
                    Assert(image.GetPixel(80, 50).ToArgb() == Color.White.ToArgb(), "Overlapping brush paths cancelled the erased intersection.");
                    Assert(image.GetPixel(20, 50).G < 40 && image.GetPixel(140, 50).G < 40, "Erasing an intersection removed the whole line.");
                }
                canvas.Undo();
                using (var image = canvas.Render()) Assert(image.GetPixel(80, 50).G < 40, "Undo did not restore the erased intersection.");
            }
        }

        private static void TestHighlighter()
        {
            using (var canvas = new AnnotationCanvas(new Bitmap(100, 80)))
            {
                using (var g = Graphics.FromImage(canvas.Image)) g.Clear(Color.White);
                var mark = new Mark { Tool = DrawTool.Highlight, Color = Color.Red, Width = 3 };
                mark.Points.Add(new PointF(20, 40)); mark.Points.Add(new PointF(80, 40)); canvas.AddMark(mark);
                using (var output = canvas.Render())
                { var pixel = output.GetPixel(50, 40); Assert(pixel.R > 240 && pixel.G > 100 && pixel.G < 220 && pixel.B == pixel.G, "Highlighter was opaque or absent."); }
                Assert(canvas.Image.GetPixel(50, 40).ToArgb() == Color.White.ToArgb(), "Highlighter changed original pixels.");
            }
        }
        private static void Mouse(Control control, string method, MouseButtons button, int x, int y)
        {
            control.GetType().GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(control, new object[] { new MouseEventArgs(button, 1, x, y, 0) });
        }
        private static T Field<T>(object target, string name)
        { return (T)target.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(target); }
        private static void Select(CaptureOverlay overlay)
        {
            Mouse(overlay, "OnMouseDown", MouseButtons.Left, 80, 70);
            Mouse(overlay, "OnMouseMove", MouseButtons.Left, 780, 440);
            Mouse(overlay, "OnMouseUp", MouseButtons.Left, 780, 440);
        }
        private static Bitmap FlatDesktop()
        {
            var image = new Bitmap(900, 600);
            using (var g = Graphics.FromImage(image)) g.Clear(Color.White);
            return image;
        }
        private static void TestFloatingSelection()
        {
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(0, 0, 900, 600)))
            {
                Bitmap output = null; bool cancelled = false;
                overlay.Completed += delegate(Bitmap image, CaptureAction action) { Assert(action == CaptureAction.Copy, "Enter did not choose copy."); output = image; };
                overlay.Cancelled += delegate { cancelled = true; };
                Select(overlay);
                Assert(!overlay.IsDisposed && Field<bool>(overlay, "ready"), "Selecting immediately closed the overlay.");
                var toolbar = Field<CaptureToolbar>(overlay, "toolbar");
                Assert(toolbar != null && toolbar.Top > 440, "Toolbar was not placed beneath the selection.");
                var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                toolbar.SelectTool(DrawTool.Rectangle);
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, 20, 20);
                Mouse(canvas, "OnMouseMove", MouseButtons.Left, 80, 80);
                Mouse(canvas, "OnMouseUp", MouseButtons.Left, 80, 80);
                overlay.GetType().GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(overlay, new object[] { new KeyEventArgs(Keys.Enter) });
                Assert(output != null && !cancelled, "Completion failed or emitted cancellation.");
                using (output)
                {
                    Assert(output.Size == new Size(700, 370), "Toolbar or border changed export dimensions.");
                    var pixel = output.GetPixel(20, 50); Assert(pixel.R > 220 && pixel.G < 100, "Edited annotation not exported.");
                    Assert(output.GetPixel(0, 0).ToArgb() == Color.White.ToArgb() && output.GetPixel(699, 369).ToArgb() == Color.White.ToArgb(), "Selection border or controls leaked into export.");
                }
            }
        }
        private static void TestSelectionGeometry()
        {
            var bounds = new Rectangle(0, 0, 900, 600);
            Assert(SelectionGeometry.Move(new Rectangle(80, 70, 700, 370), new Point(1000, 1000), bounds) == new Rectangle(200, 230, 700, 370), "Moving exceeded screen bounds.");
            Assert(SelectionGeometry.Resize(new Rectangle(80, 70, 700, 370), 0, new Point(1000, 1000), bounds).Size == new Size(8, 8), "Resize reversed the selection.");
            var work = new Rectangle(-1920, -200, 1920, 1080);
            var size = new Size(450, 66);
            var location = CaptureToolbar.Place(new Rectangle(-1000, 710, 990, 170), work, size);
            Assert(work.Contains(new Rectangle(location, size)) && location.Y < 710, "Toolbar overflowed a monitor with negative coordinates.");
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), bounds))
            {
                Select(overlay);
                var canvas = Field<AnnotationCanvas>(overlay, "canvas"); canvas.AddMark(RectangleMark());
                var grips = Field<Array>(overlay, "grips");
                var grip = (Control)grips.GetValue(0);
                Mouse(grip, "OnMouseDown", MouseButtons.Left, 4, 4);
                Mouse(overlay, "OnMouseMove", MouseButtons.Left, 90, 75);
                Mouse(overlay, "OnMouseUp", MouseButtons.Left, 90, 75);
                Assert(Field<Rectangle>(overlay, "selection") == new Rectangle(90, 75, 690, 365), "Resize grip did not change selection.");
                using (var output = canvas.Render()) Assert(output.GetPixel(10, 45).R > 240 && output.GetPixel(10, 45).G < 40, "Resizing lost the annotation's screen position.");
                canvas.Undo(); canvas.Redo(); Assert(canvas.Marks[0].Points[0] == new PointF(10, 15), "Resize corrupted undo/redo coordinates.");
            }
        }
        private static void TestSelectionCancel()
        {
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(0, 0, 900, 600)))
            {
                bool exported = false;
                overlay.Completed += delegate(Bitmap image, CaptureAction action) { exported = true; image.Dispose(); };
                Select(overlay);
                overlay.GetType().GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(overlay, new object[] { new KeyEventArgs(Keys.Escape) });
                Assert(!exported && overlay.IsDisposed, "Cancel exported or kept the overlay open.");
            }
        }
        private static Bitmap Sample()
        {
            var image = new Bitmap(820, 470);
            using (var g = Graphics.FromImage(image))
            using (var titleFont = new Font("Microsoft YaHei UI", 24))
            using (var font = new Font("Microsoft YaHei UI", 13))
            {
                g.Clear(Theme.Background);
                g.FillRectangle(Brushes.White, 30, 30, 760, 410);
                g.DrawString("屏幕上的灵感，即刻收藏。", titleFont, Brushes.MidnightBlue, 65, 64);
                g.DrawString("区域截图  /  标注编辑  /  桌面贴图  /  滚动长截图", font, Brushes.SlateGray, 69, 133);
                using (var brush = new SolidBrush(Theme.Accent)) g.FillRectangle(brush, 70, 204, 290, 164);
                using (var brush = new SolidBrush(Color.FromArgb(229, 233, 255))) g.FillRectangle(brush, 382, 204, 330, 164);
                g.DrawString("想法 01", titleFont, Brushes.White, 100, 252);
                g.DrawString("Capture your ideas.", font, Brushes.MidnightBlue, 410, 265);
            }
            return image;
        }
        private static void TestAutomaticCapture()
        {
            var oldCursor = Cursor.Position;
            using (var scene = new ScrollTestWindow())
            {
                scene.Show(); scene.Activate(); Application.DoEvents();
                using (var session = new ScrollCapture(scene.RectangleToScreen(scene.ClientRectangle)))
                {
                    Bitmap result = null;
                    session.Completed += delegate(Bitmap image) { result = image; };
                    session.Show();
                    var startMethod = typeof(ScrollCapture).GetMethod("StartCapture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var task = (System.Threading.Tasks.Task)startMethod.Invoke(session, null);
                    var deadline = DateTime.UtcNow.AddSeconds(22);
                    while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); System.Threading.Thread.Sleep(15); }
                    Assert(task.IsCompleted, "Automatic capture timed out.");
                    if (task.IsFaulted) throw task.Exception;
                    Assert(result != null, "Automatic capture did not finish.");
                    using (result) EqualPixels(scene.Content, result, scene.Content.Height);
                }
                scene.Close();
            }
            Cursor.Position = oldCursor;
        }
        private sealed class ScrollTestWindow : Form
        {
            internal readonly Bitmap Content = Pattern(280, 660, 41);
            private int offset;
            internal ScrollTestWindow()
            {
                AutoScaleMode = AutoScaleMode.None;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(100, 90);
                ClientSize = new Size(280, 240);
                DoubleBuffered = true;
                TopMost = true;
                ShowInTaskbar = false;
            }
            protected override void OnPaint(PaintEventArgs e) { e.Graphics.DrawImageUnscaled(Content, 0, -offset); }
            protected override void OnMouseWheel(MouseEventArgs e) { offset = Math.Min(Content.Height - ClientSize.Height, offset + 105); Invalidate(); base.OnMouseWheel(e); }
            protected override void Dispose(bool disposing) { if (disposing) Content.Dispose(); base.Dispose(disposing); }
        }
        private static void Preview(string root)
        {
            var storage = Path.Combine(root, "preview-data");
            Directory.CreateDirectory(Path.Combine(storage, "captures"));
            using (var sample = Sample()) sample.Save(Path.Combine(storage, "captures", "sample.png"), ImageFormat.Png);
            using (var home = new MainForm(storage, false))
            {
                home.Show(); home.Activate(); Application.DoEvents(); System.Threading.Thread.Sleep(200); Application.DoEvents();
                using (var bitmap = Native.Capture(home.Bounds)) bitmap.Save(Path.Combine(root, "preview-home.png"));
                home.Close();
            }
            using (var editor = new EditorForm(Sample(), delegate(Bitmap pin) { pin.Dispose(); }))
            {
                editor.Show(); editor.Activate(); Application.DoEvents(); System.Threading.Thread.Sleep(200); Application.DoEvents();
                using (var bitmap = Native.Capture(editor.Bounds)) bitmap.Save(Path.Combine(root, "preview-editor.png"));
                editor.Close();
            }
            using (var background = new Bitmap(1100, 700))
            {
                using (var g = Graphics.FromImage(background))
                using (var sample = Sample()) { g.Clear(Color.FromArgb(32, 37, 47)); g.DrawImageUnscaled(sample, 80, 30); }
                using (var overlay = new CaptureOverlay(false, new Bitmap(background), new Rectangle(40, 40, 1100, 700)))
                {
                    overlay.Show(); overlay.Activate(); Application.DoEvents();
                    Mouse(overlay, "OnMouseDown", MouseButtons.Left, 80, 50);
                    Mouse(overlay, "OnMouseMove", MouseButtons.Left, 900, 500);
                    Mouse(overlay, "OnMouseUp", MouseButtons.Left, 900, 500);
                    Application.DoEvents(); System.Threading.Thread.Sleep(180); Application.DoEvents();
                    using (var screenshot = Native.Capture(overlay.Bounds)) screenshot.Save(Path.Combine(root, "preview-floating-toolbar.png"));
                    Field<CaptureToolbar>(overlay, "toolbar").SelectTool(DrawTool.Arrow);
                    var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                    Mouse(canvas, "OnMouseDown", MouseButtons.Left, 570, 160);
                    Mouse(canvas, "OnMouseMove", MouseButtons.Left, 400, 100);
                    Mouse(canvas, "OnMouseUp", MouseButtons.Left, 400, 100);
                    Application.DoEvents(); System.Threading.Thread.Sleep(180); Application.DoEvents();
                    using (var screenshot = Native.Capture(overlay.Bounds)) screenshot.Save(Path.Combine(root, "preview-floating-drawing.png"));
                    overlay.Close();
                }
            }
            Console.WriteLine("Previews: " + root);
        }
    }
}
