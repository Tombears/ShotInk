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
                Run("text pages with whitespace and small overlaps preserve every row", TestDocumentStitch);
                Run("unchanged frame does not duplicate content", TestUnchanged);
                Run("unrelated frames are rejected", TestUnrelated);
                Run("changed frame dimensions are rejected", TestDimensions);
                Run("height limit rejects oversized first frame", TestInitialLimit);
                Run("accumulated height is bounded", TestAccumulatedLimit);
                Run("annotations render at original resolution and undo", TestAnnotations);
                Run("inline text supports multiline input, settings, cancellation and export", TestInlineText);
                Run("typing stays transparent, compact and anchored to the image caret", TestTransparentTextInput);
                Run("text moves, scales and rotates with independent undo at different zooms", TestTextTransform);
                Run("mosaic covers partial edge blocks", TestMosaic);
                Run("undo and redo restore additions and annotation erasing", TestUndoRedoErase);
                Run("tool buttons immediately activate defaults and expose independent settings", TestToolOptions);
                Run("tools retain separate colors and widths across tool changes and new editors", TestIndependentToolSettings);
                Run("settings preserve legacy history, shortcuts, tool styles and save preferences", TestSettings);
                Run("history defaults to ten and its on-screen limit persists and prunes only history", TestHistoryLimit);
                Run("manual scroll sampling and asynchronous stitching preserve pixels", TestScrollPipeline);
                Run("slow stitching retains every queued overlap frame under pressure", TestScrollBackPressure);
                Run("scroll capture automatically completes at limits and cancels without hanging", TestScrollLimitAndCancel);
                Run("integrated scroll capture restores tools and edits within a fixed viewport", TestIntegratedScroll);
                Run("an unrecoverable page jump stops with a visible partial-capture notice", TestScrollGap);
                Run("shortcut changes restore existing bindings on a registration conflict", TestHotkeyRollback);
                Run("configured save paths work without prompts in screenshots and the editor", TestSavePreferences);
                Run("OCR highlights form continuous bands without gaps or darker seams", TestOcrHighlight);
                Run("selection dragging repaints one surface and reframes once on release", TestSelectionDrag);
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
                    Run("manual scrolling reconstructs a real scrolling window", TestAutomaticCapture);
                    Preview(args[1]);
                }
                if (args.Length == 1 && args[0] == "--drag-preview") Run("live selection dragging clears old borders on screen", TestLiveDrag);
                if (args.Length == 1 && args[0] == "--scroll-preview")
                { Run("manual scrolling reconstructs a real scrolling window", TestAutomaticCapture); Run("live integrated scroll capture excludes its own chrome", TestLiveIntegratedScroll); }
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
        private static void TestIndependentToolSettings()
        {
            var settings = new AppSettings();
            using (var options = new AnnotationOptions(settings) { Width = 440 })
            using (var canvas = new AnnotationCanvas(new Bitmap(250, 150)))
            {
                canvas.Tool = DrawTool.Pen; options.ShowTool(DrawTool.Pen); options.ApplyTo(canvas);
                options.Ink = Color.Blue; Option<NumericUpDown>(options, "tool-stroke").Value = 8; options.ApplyTo(canvas);
                canvas.Tool = DrawTool.Rectangle; options.ShowTool(DrawTool.Rectangle); options.ApplyTo(canvas);
                Assert(canvas.Stroke == 3 && canvas.Ink != Color.Blue, "Pen settings leaked into the rectangle tool.");
                options.Ink = Color.Green; Option<NumericUpDown>(options, "tool-stroke").Value = 2; options.ApplyTo(canvas);
                canvas.Tool = DrawTool.Ellipse; options.ShowTool(DrawTool.Ellipse); options.ApplyTo(canvas);
                Assert(canvas.Stroke == 3 && canvas.Ink != Color.Green, "Shape variants share one profile.");
                canvas.Tool = DrawTool.Pen; options.ShowTool(DrawTool.Pen); options.ApplyTo(canvas);
                Assert(canvas.Stroke == 8 && canvas.Ink == Color.Blue, "Switching tools lost the pen profile.");
                Gesture(canvas, new Point(10, 10), new Point(60, 50));
                Assert(canvas.Marks[0].Width == 8 && canvas.Marks[0].Color == Color.Blue, "New marks ignored their tool profile.");
                using (var editor = new EditorForm(new Bitmap(250, 150), delegate(Bitmap image) { image.Dispose(); }, settings))
                {
                    var next = Field<AnnotationOptions>(editor, "toolOptions");
                    next.ShowTool(DrawTool.Pen); Assert(next.Stroke == 8 && next.Ink == Color.Blue, "A new editor lost the shared preferences.");
                }
            }
        }
        private static void TestSettings()
        {
            string root = Path.Combine(Path.GetTempPath(), "ShotInk-settings-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "settings.ini"); File.WriteAllText(path, "save=0");
                var settings = AppSettings.Load(path);
                Assert(!settings.AutoHistory && settings.AskSaveLocation && settings.CaptureShortcut == Keys.F1, "Legacy settings were not migrated with defaults.");
                settings.SaveDirectory = Path.Combine(root, "图片=测试"); settings.AskSaveLocation = false;
                settings.CaptureShortcut = Keys.Control | Keys.Shift | Keys.F8; settings.PinShortcut = Keys.None;
                settings.Tool(DrawTool.Pen).Ink = Color.Blue; settings.Tool(DrawTool.Pen).Width = 7;
                settings.Tool(DrawTool.Text).FontSize = 42; settings.Tool(DrawTool.Mosaic).BrushSize = 76;
                settings.Save(path); var restored = AppSettings.Load(path);
                Assert(!restored.AutoHistory && !restored.AskSaveLocation && restored.SaveDirectory == settings.SaveDirectory, "Saving lost history or directory preferences.");
                Assert(restored.CaptureShortcut == settings.CaptureShortcut && restored.PinShortcut == Keys.None, "Shortcut preferences were not persisted.");
                Assert(restored.Tool(DrawTool.Pen).Ink.ToArgb() == Color.Blue.ToArgb() && restored.Tool(DrawTool.Pen).Width == 7 && restored.Tool(DrawTool.Text).FontSize == 42 && restored.Tool(DrawTool.Mosaic).BrushSize == 76, "Tool preferences were not restored independently.");
                using (var dialog = new SettingsForm(restored, delegate { return null; }))
                {
                    Key(Option<ShortcutBox>(dialog, "hotkey-0"), Keys.Control | Keys.Alt | Keys.F9);
                    Option<CheckBox>(dialog, "ask-save-location").Checked = true;
                    var candidate = dialog.ReadSettings();
                    Assert(candidate.CaptureShortcut == (Keys.Control | Keys.Alt | Keys.F9) && candidate.AskSaveLocation && !restored.AskSaveLocation, "Editing the dialog mutated settings before save.");
                    Option<ShortcutBox>(dialog, "hotkey-1").ShortcutValue = candidate.CaptureShortcut;
                    bool rejected = false; try { dialog.ReadSettings(); } catch (ArgumentException) { rejected = true; }
                    Assert(rejected, "Duplicate global shortcuts were accepted.");
                }
                using (var home = new MainForm(root, false))
                {
                    var candidate = restored.Clone(); candidate.PinShortcut = Keys.F7;
                    Assert(home.ApplySettings(candidate) == null && AppSettings.Load(path).PinShortcut == Keys.F7, "Applying settings did not persist the new shortcuts.");
                    Field<CheckBox>(home, "autoSave").Checked = true;
                }
                restored = AppSettings.Load(path);
                Assert(restored.AutoHistory && restored.PinShortcut == Keys.F7 && restored.Tool(DrawTool.Pen).Width == 7, "History checkbox overwrote other settings.");
                using (var dialog = new SettingsForm(restored, delegate { return null; }))
                {
                    dialog.Show(); Application.DoEvents(); using (var bitmap = new Bitmap(dialog.ClientSize.Width, dialog.ClientSize.Height))
                    { dialog.DrawToBitmap(bitmap, dialog.ClientRectangle); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-settings-options.png")); }
                    dialog.Close();
                }
            }
            finally { Directory.Delete(root, true); }
        }
        private static void WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (!condition() && DateTime.UtcNow < deadline) { Application.DoEvents(); System.Threading.Thread.Sleep(2); }
            Assert(condition(), "Asynchronous operation timed out.");
        }
        private static void TestHistoryLimit()
        {
            string root = Path.Combine(Path.GetTempPath(), "ShotInk-history-" + Guid.NewGuid().ToString("N"));
            string captures = Path.Combine(root, "captures"); Directory.CreateDirectory(captures);
            try
            {
                using (var image = new Bitmap(2, 2))
                {
                    for (int i = 0; i < 15; i++)
                    {
                        string path = Path.Combine(captures, "capture-" + i + ".png"); image.Save(path, ImageFormat.Png);
                        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-i));
                    }
                    image.Save(Path.Combine(root, "manual.png"), ImageFormat.Png);
                }
                using (var home = new MainForm(root, false))
                {
                    var number = Field<NumericUpDown>(home, "historyLimit");
                    Assert(number.Value == 10 && Field<ListView>(home, "history").Items.Count == 10, "History did not default to ten.");
                    number.Value = 3;
                    Assert(Directory.GetFiles(captures, "*.png").Length == 3 && Field<ListView>(home, "history").Items.Count == 3, "Changing the count did not apply immediately.");
                    Assert(File.Exists(Path.Combine(root, "manual.png")) && File.Exists(Path.Combine(captures, "capture-0.png")), "History pruning removed manual or newest images.");
                    home.Show(); Application.DoEvents();
                    using (var preview = new Bitmap(home.Width, home.Height)) { home.DrawToBitmap(preview, home.ClientRectangle); preview.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-history-options.png")); }
                    home.Close();
                }
                Assert(AppSettings.Load(Path.Combine(root, "settings.ini")).HistoryLimit == 3, "History count was not persisted.");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void TestScrollPipeline()
        {
            using (var content = Pattern(100, 760, 221))
            {
                int offset = 0, calls = 0, thread = 0, ui = System.Threading.Thread.CurrentThread.ManagedThreadId;
                using (var session = new ScrollCaptureSession(delegate
                {
                    System.Threading.Interlocked.CompareExchange(ref thread, System.Threading.Thread.CurrentThread.ManagedThreadId, 0);
                    System.Threading.Interlocked.Increment(ref calls);
                    return content.Clone(new Rectangle(0, System.Threading.Volatile.Read(ref offset), 100, 200), PixelFormat.Format24bppRgb);
                }))
                {
                    var task = session.RunAsync(); WaitUntil(delegate { return session.Height == 200 && calls >= 5; });
                    Assert(!task.IsCompleted && session.Frames == 1 && thread != ui, "Manual capture stopped early, duplicated static frames or ran capture on the UI thread.");
                    foreach (int position in new[] { 35, 80, 120, 180, 220, 300, 360 })
                    { System.Threading.Volatile.Write(ref offset, position); WaitUntil(delegate { return session.Height == 200 + position; }); }
                    session.Finish(); Pump(task);
                    using (var result = task.Result)
                    { Assert(!result.LimitReached && result.Frames == 8, "Scroll frames were duplicated or lost."); EqualPixels(content, result.Image, 560); }
                    Assert(session.PeakPendingFrames <= ScrollCaptureSession.QueueCapacity && ScrollCaptureSession.PollInterval == 10, "Capture cadence or queue bound is incorrect.");
                }
            }
        }
        private static void TestScrollBackPressure()
        {
            using (var content = Pattern(100, 960, 225))
            {
                int captures = 0;
                using (var session = new ScrollCaptureSession(delegate
                {
                    int index = Math.Min(19, System.Threading.Interlocked.Increment(ref captures) - 1);
                    return content.Clone(new Rectangle(0, index * 40, 100, 200), PixelFormat.Format24bppRgb);
                }))
                {
                    session.BeforeStitch = delegate { System.Threading.Thread.Sleep(45); };
                    var task = session.RunAsync(); WaitUntil(delegate { return captures >= 20; }); session.Finish(); Pump(task);
                    using (var result = task.Result)
                    {
                        Assert(result.Frames == 20 && !result.AlignmentLost, "Overload dropped an intermediate overlap frame.");
                        EqualPixels(content, result.Image, content.Height);
                    }
                    Assert(session.PeakPendingFrames > 2 && session.PeakPendingFrames <= ScrollCaptureSession.QueueCapacity, "The pressure test did not fill the bounded FIFO queue.");
                }
            }
        }
        private static void TestScrollLimitAndCancel()
        {
            using (var content = Pattern(100, 640, 222))
            {
                foreach (bool pixels in new[] { false, true })
                {
                    int offset = 0;
                    using (var session = new ScrollCaptureSession(delegate { return content.Clone(new Rectangle(0, System.Threading.Volatile.Read(ref offset), 100, 240), PixelFormat.Format24bppRgb); },
                        pixels ? VerticalStitcher.MaxHeight : 400, pixels ? 40000 : VerticalStitcher.MaxPixels))
                    {
                        var task = session.RunAsync(); WaitUntil(delegate { return session.Height == 240; });
                        System.Threading.Volatile.Write(ref offset, 100); WaitUntil(delegate { return session.Height == 340; });
                        System.Threading.Volatile.Write(ref offset, 160); Pump(task);
                        using (var result = task.Result) { Assert(result.LimitReached && result.Image.Height == 400, "Length/pixel limit did not auto-complete."); EqualPixels(content, result.Image, 400); }
                    }
                }
                using (var session = new ScrollCaptureSession(delegate { return content.Clone(new Rectangle(0, 0, 100, 240), PixelFormat.Format24bppRgb); }))
                {
                    var task = session.RunAsync(); WaitUntil(delegate { return session.Frames == 1; }); session.Dispose();
                    WaitUntil(delegate { return task.IsCompleted; }); Assert(task.IsCanceled, "Cancellation left background capture running.");
                }
                using (var session = new ScrollCaptureSession(delegate { throw new InvalidOperationException("capture failed"); }))
                {
                    var task = session.RunAsync(); WaitUntil(delegate { return task.IsCompleted; });
                    Assert(task.IsFaulted && task.Exception.InnerException.Message == "capture failed", "A failed frame source hung or hid its error.");
                }
            }
        }
        private static void TestIntegratedScroll()
        {
            using (var content = Pattern(700, 900, 223))
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(20, 20, 900, 600)))
            {
                int offset = 0; Rectangle sampled = Rectangle.Empty;
                overlay.InterceptScrollWheel = false;
                overlay.CaptureScrollFrame = delegate(Rectangle region) { sampled = region; return content.Clone(new Rectangle(0, System.Threading.Volatile.Read(ref offset), 700, 370), PixelFormat.Format24bppRgb); };
                overlay.Show(); Select(overlay);
                var selection = Field<Rectangle>(overlay, "selection"); var toolbar = Field<CaptureToolbar>(overlay, "toolbar");
                var strip = Field<ToolStrip>(toolbar, "strip"); var recognize = Field<ToolStripButton>(toolbar, "recognize"); var scroll = Field<ToolStripButton>(toolbar, "scroll");
                Assert(strip.Items.IndexOf(scroll) == strip.Items.IndexOf(recognize) + 1, "Long capture is not beside OCR.");
                var task = overlay.CaptureLongAsync();
                WaitUntil(delegate { var session = Field<ScrollCaptureSession>(overlay, "scrollSession"); return session != null && session.Height == 370; });
                Assert(!strip.Visible && Option<Button>(toolbar, "finish-scroll-capture").Visible && !Field<AnnotationCanvas>(overlay, "canvas").Visible, "Capture mode exposed editing tools or the frozen canvas.");
                Assert(!overlay.Region.IsVisible(selection.Left + 50, selection.Top + 50), "The selected area did not pass mouse input through to the original window.");
                System.Threading.Volatile.Write(ref offset, 150);
                WaitUntil(delegate { return Field<ScrollCaptureSession>(overlay, "scrollSession").Height == 520; });
                Option<Button>(toolbar, "finish-scroll-capture").PerformClick(); Pump(task);
                var canvas = Field<AnnotationCanvas>(overlay, "canvas"); var viewport = Field<Panel>(overlay, "longViewport");
                Assert(sampled == new Rectangle(100, 90, 700, 370) && viewport.Bounds == selection && canvas.Size == selection.Size, "Long capture changed the selected scope or expanded its visible frame.");
                Assert(strip.Visible && Field<ScrollCaptureSession>(overlay, "scrollSession") == null && canvas.Image.Height == 520, "Completion did not restore all tools and the stitched image.");
                Assert(!Field<bool>(overlay, "scrollEscapeRegistered"), "Completion left the temporary Escape shortcut registered.");
                MouseWheel(canvas, -120); Assert(canvas.DocumentOffsetY > 0, "Mouse wheel did not pan the long screenshot.");
                float position = canvas.DocumentOffsetY;
                toolbar.SelectTool(DrawTool.Rectangle); Gesture(canvas, new Point(20, 20), new Point(80, 80));
                Assert(Math.Abs(canvas.Marks[0].Points[0].Y - position - 20 / canvas.Zoom) < 1, "Scrolled annotation used viewport rather than image coordinates.");
                toolbar.SelectTool(DrawTool.Text); Mouse(canvas, "OnMouseDown", MouseButtons.Left, 220, 100);
                Field<InlineTextInput>(canvas, "textEditor").Text = "滚动后的文字"; canvas.CommitTextEdit();
                Assert(Math.Abs(canvas.Marks[1].Points[0].Y - position - 100 / canvas.Zoom) < 1, "Text input ignored the long-image offset.");
                ToolPreview(overlay, "long-preview");
                using (var image = canvas.Render()) Assert(image.Size == new Size(700, 520), "Export returned only the visible part of the long screenshot.");
                overlay.Close();
            }
            using (var content = Pattern(700, 370, 224))
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(20, 20, 900, 600)))
            {
                int exports = 0, cancellations = 0;
                overlay.InterceptScrollWheel = false;
                overlay.Completed += delegate(Bitmap image, CaptureAction action) { exports++; image.Dispose(); };
                overlay.Cancelled += delegate { cancellations++; };
                overlay.CaptureScrollFrame = delegate { return new Bitmap(content); };
                overlay.Show(); Select(overlay);
                var task = overlay.CaptureLongAsync();
                WaitUntil(delegate { var session = Field<ScrollCaptureSession>(overlay, "scrollSession"); return session != null && session.Frames == 1; });
                Assert(Field<bool>(overlay, "scrollEscapeRegistered"), "Capture did not register Escape while the underlying window had focus.");
                SendMessage(overlay.Handle, Native.HotKeyMessage, new IntPtr(1001), IntPtr.Zero); Pump(task);
                Assert(overlay.IsDisposed && cancellations == 1 && exports == 0, "Escape failed to cancel background capture without exporting.");
                using (var probe = new Form())
                {
                    bool available = Native.RegisterHotKey(probe.Handle, 1001, 0x4000, (uint)Keys.Escape);
                    if (available) Native.UnregisterHotKey(probe.Handle, 1001);
                    Assert(available, "Cancellation left the temporary Escape shortcut registered.");
                }
            }
        }
        private static void TestScrollGap()
        {
            using (var before = Pattern(700, 370, 227))
            using (var after = Pattern(700, 370, 228))
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(20, 20, 900, 600)))
            {
                int changed = 0;
                overlay.InterceptScrollWheel = false;
                overlay.CaptureScrollFrame = delegate { return new Bitmap(System.Threading.Volatile.Read(ref changed) == 0 ? before : after); };
                overlay.Show(); Select(overlay);
                var task = overlay.CaptureLongAsync();
                WaitUntil(delegate { var session = Field<ScrollCaptureSession>(overlay, "scrollSession"); return session != null && session.Frames == 1; });
                System.Threading.Volatile.Write(ref changed, 1); Pump(task);
                Assert(Field<Label>(overlay, "sizeHint").Text.Contains("页面变化过大"), "A missing overlap was silently exported as a complete capture.");
                using (var result = Field<AnnotationCanvas>(overlay, "canvas").Render()) EqualPixels(before, result, before.Height);
                overlay.Close();
            }
        }
        private static void TestHotkeyRollback()
        {
            var live = new System.Collections.Generic.Dictionary<int, Keys>(); Keys reserved = Keys.F12;
            var bindings = new HotkeyBindings(delegate(int id, Keys key)
            {
                if (key == reserved || live.ContainsValue(key)) return false;
                live.Add(id, key); return true;
            }, delegate(int id) { live.Remove(id); });
            var defaults = new AppSettings(); Assert(bindings.RegisterInitial(defaults.Shortcuts) == "", "Initial shortcuts failed.");
            string error; Assert(!bindings.TryApply(new[] { Keys.F5, reserved, Keys.F6 }, out error) && error.Contains("F12"), "Conflict was not reported.");
            Assert(live.Count == 3 && live[1] == Keys.F1 && live[2] == Keys.F3 && live[3] == defaults.ScrollShortcut, "Conflict discarded the previous shortcuts.");
            Assert(bindings.TryApply(new[] { Keys.F3, Keys.F1, Keys.None }, out error) && live.Count == 2 && live[1] == Keys.F3 && live[2] == Keys.F1, "Swapping or clearing shortcuts failed.");
            Assert(!bindings.TryApply(new[] { Keys.F7, Keys.F8, Keys.F9 }, delegate { throw new IOException("disk unavailable"); }, out error) &&
                live.Count == 2 && live[1] == Keys.F3 && live[2] == Keys.F1, "Failed settings persistence left the new shortcuts active.");
            bindings.Clear(); Assert(live.Count == 0, "Disposal leaked hotkey registrations.");
        }
        private static void TestSavePreferences()
        {
            string root = Path.Combine(Path.GetTempPath(), "ShotInk-save-" + Guid.NewGuid().ToString("N"));
            var settings = new AppSettings { SaveDirectory = root, AskSaveLocation = false };
            try
            {
                using (var image = Pattern(120, 100, 22))
                {
                    string first, second = null; Assert(ImageExport.Save(null, image, settings, out first) && ImageExport.Save(null, image, settings, out second), "Direct save failed.");
                    Assert(first != second && Path.GetDirectoryName(first) == root, "Repeated saves overwrote an image or ignored the chosen folder.");
                    using (var saved = new Bitmap(first)) EqualPixels(image, saved, 100);
                }
                using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(0, 0, 900, 600), settings))
                {
                    int count = Directory.GetFiles(root, "*.png").Length; bool completed = false;
                    overlay.Completed += delegate(Bitmap image, CaptureAction action) { completed = action == CaptureAction.Save; image.Dispose(); };
                    overlay.Show(); Select(overlay); Key(overlay, Keys.Control | Keys.S);
                    Assert(completed && Directory.GetFiles(root, "*.png").Length == count + 1, "Screenshot save did not honor the no-prompt preference.");
                }
                using (var editor = new EditorForm(new Bitmap(120, 100), delegate(Bitmap image) { image.Dispose(); }, settings))
                {
                    int count = Directory.GetFiles(root, "*.png").Length;
                    Field<AnnotationCanvas>(editor, "canvas").AddMark(RectangleMark());
                    bool saved = (bool)typeof(EditorForm).GetMethod("SaveImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(editor, null);
                    Assert(saved && Directory.GetFiles(root, "*.png").Length == count + 1, "History/long-image editor ignored save preferences.");
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        private static void TestOcrHighlight()
        {
            var line = new OcrLine { Text = "ab cd", Box = new[] { 10F, 10F, 55F, 14F }, Characters = new[] {
                new OcrCharacter { Text = "a", Start = 0, Length = 1, Box = new[] { 10F, 10F, 9F, 10F } },
                new OcrCharacter { Text = "b", Start = 1, Length = 1, Box = new[] { 22F, 12F, 9F, 10F } },
                new OcrCharacter { Text = "c", Start = 3, Length = 1, Box = new[] { 44F, 11F, 9F, 10F } },
                new OcrCharacter { Text = "d", Start = 4, Length = 1, Box = new[] { 56F, 10F, 9F, 10F } } } };
            var next = new OcrLine { Text = "ok", Box = new[] { 75F, 10F, 20F, 14F } };
            var column = new OcrLine { Text = "列", Box = new[] { 140F, 10F, 20F, 14F } };
            var document = new OcrDocument { Width = 180, Height = 50, Lines = new[] { line, next, column } };
            var model = new OcrSelectionModel(document); model.SelectAll();
            using (var bitmap = new Bitmap(180, 50)) using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White); model.Draw(g); int expected = bitmap.GetPixel(14, 16).ToArgb();
                for (int x = 11; x < 95; x++) for (int y = 11; y < 24; y++) Assert(bitmap.GetPixel(x, y).ToArgb() == expected, "OCR highlight contains character/word gaps or overlapping alpha seams.");
                Assert(bitmap.GetPixel(110, 16).ToArgb() == Color.White.ToArgb(), "OCR highlight filled the gap between distant columns.");
            }
            using (var canvas = new AnnotationCanvas(new Bitmap(180, 50)))
            {
                using (var g = Graphics.FromImage(canvas.Image)) g.Clear(Color.White);
                canvas.SetTextRecognition(document); Assert(canvas.HasSelectedText, "Recognition did not select all text immediately.");
                using (var result = canvas.Render()) EqualPixels(canvas.Image, result, 50);
            }
        }
        private static void DragSelection(CaptureOverlay overlay, int steps)
        {
            var canvas = Field<AnnotationCanvas>(overlay, "canvas");
            Mouse(canvas, "OnMouseDown", MouseButtons.Left, 200, 100);
            for (int i = 1; i <= steps; i++) { Mouse(overlay, "OnMouseMove", MouseButtons.Left, 280 - 10 * i, 170 + 6 * i); overlay.Refresh(); Application.DoEvents(); }
        }
        private static void TestSelectionDrag()
        {
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(20, 20, 900, 600)))
            {
                overlay.Show(); Select(overlay); var canvas = Field<AnnotationCanvas>(overlay, "canvas"); canvas.AddMark(RectangleMark());
                int revision = canvas.Revision; var originalImage = canvas.Image;
                DragSelection(overlay, 5);
                Assert(!canvas.Visible && canvas.Revision == revision && object.ReferenceEquals(originalImage, canvas.Image), "Dragging rebuilt or moved a visible child canvas on each frame.");
                Mouse(overlay, "OnMouseUp", MouseButtons.Left, 230, 200);
                Assert(canvas.Visible && canvas.Revision == revision + 1 && canvas.Location == new Point(30, 100), "Drag completion did not reframe once at the final location.");
                var selection = Field<Rectangle>(overlay, "selection"); revision = canvas.Revision; originalImage = canvas.Image;
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, 100, 100); Mouse(overlay, "OnMouseMove", MouseButtons.Left, 170, 230); overlay.Capture = false;
                Assert(canvas.Visible && Field<Rectangle>(overlay, "selection") == selection && canvas.Revision == revision && object.ReferenceEquals(canvas.Image, originalImage), "Lost capture left a hidden canvas or changed the screenshot.");
                overlay.Close();
            }
        }
        private static void TestLiveDrag()
        {
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(20, 20, 900, 600)))
            {
                overlay.Show(); overlay.Activate(); Select(overlay); DragSelection(overlay, 5); overlay.Refresh(); Application.DoEvents();
                using (var screenshot = Native.Capture(overlay.Bounds))
                {
                    foreach (var point in new[] { new Point(79, 180), new Point(780, 180), new Point(400, 69) })
                    { var color = screenshot.GetPixel(point.X, point.Y); Assert(!(color.B > 180 && color.R < 100), "A previous selection border remained on screen."); }
                    screenshot.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-drag-options.png"));
                }
                Mouse(overlay, "OnMouseUp", MouseButtons.Left, 230, 200); overlay.Close();
            }
        }
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
            Assert(actual.Width == expected.Width && actual.Height == height, "Incorrect output dimensions: expected " + expected.Width + " x " + height + ", got " + actual.Width + " x " + actual.Height + ".");
            for (int y = 0; y < height; y++)
                for (int x = 0; x < actual.Width; x++)
                    Assert(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), "Pixel mismatch at " + x + ", " + y);
        }
        private static void TestDocumentStitch()
        {
            using (var content = new Bitmap(1280, 1800, PixelFormat.Format24bppRgb))
            using (var font = new Font("Microsoft YaHei UI", 13))
            using (var stitcher = new VerticalStitcher())
            {
                using (var graphics = Graphics.FromImage(content))
                {
                    graphics.Clear(Color.White);
                    for (int row = 0; row < 55; row++)
                    {
                        int y = 15 + row * 32;
                        graphics.DrawString("第 " + row + " 行：截图页面内容 / Document " + (row * 731) + "  " + new string((char)('A' + row % 26), 8 + row % 19), font, Brushes.Black, 75, y);
                        if (row % 7 == 0) graphics.FillRectangle(Brushes.SteelBlue, 920, y, 100 + row, 15);
                    }
                }
                var clock = System.Diagnostics.Stopwatch.StartNew();
                foreach (int offset in new[] { 0, 45, 135, 250, 365, 480, 600, 720, 840, 960, 1080 })
                    using (var frame = content.Clone(new Rectangle(0, offset, 1280, 720), PixelFormat.Format24bppRgb))
                        Assert(stitcher.Add(frame) == StitchStatus.Added, "A text page failed to match at offset " + offset + ".");
                Console.WriteLine("Text-page matching: " + clock.ElapsedMilliseconds + " ms for 11 frames at 1280 x 720.");
                using (var result = stitcher.Build()) EqualPixels(content, result, content.Height);
            }
            using (var content = Pattern(180, 420, 226))
            using (var stitcher = new VerticalStitcher())
            {
                foreach (int offset in new[] { 0, 180 })
                    using (var frame = content.Clone(new Rectangle(0, offset, 180, 240), PixelFormat.Format24bppRgb))
                        Assert(stitcher.Add(frame) == StitchStatus.Added, "A valid narrow overlap was rejected.");
                using (var result = stitcher.Build()) EqualPixels(content, result, content.Height);
            }
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
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr word, IntPtr value);
        private static void TestTransparentTextInput()
        {
            foreach (float zoom in new[] { 1F, 0.5F })
            using (var host = new Form { ClientSize = new Size(850, 500) })
            using (var canvas = new AnnotationCanvas(new Bitmap(850, 500)))
            {
                using (var g = Graphics.FromImage(canvas.Image))
                {
                    g.Clear(Color.FromArgb(30, 34, 40));
                    using (var pen = new Pen(Color.FromArgb(62, 70, 80)))
                    { for (int x = 0; x < 850; x += 20) g.DrawLine(pen, x, 0, x, 500); for (int y = 0; y < 500; y += 20) g.DrawLine(pen, 0, y, 850, y); }
                }
                host.Controls.Add(canvas); host.Show(); canvas.SetZoom(zoom); canvas.Tool = DrawTool.Text; canvas.Ink = Color.Red;
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, (int)(180 * zoom), (int)(120 * zoom));
                var input = Field<InlineTextInput>(canvas, "textEditor");
                foreach (char character in "中文  Abc") SendMessage(input.Handle, 0x102, new IntPtr(character), new IntPtr(1));
                Application.DoEvents();
                var mark = canvas.Marks[0];
                Assert(mark.Text == "中文  Abc", "Native character input was lost or duplicated.");
                var expectedCaret = TextGeometry.World(mark, TextGeometry.Caret(mark, mark.Text.Length));
                Assert(Math.Abs(input.Left - expectedCaret.X * zoom) <= 1 && Math.Abs(input.Top - expectedCaret.Y * zoom) <= 1,
                    "IME anchor moved to the canvas origin instead of following the visible caret.");
                var originalFont = input.Font;
                SendMessage(input.Handle, 0x102, new IntPtr('1'), new IntPtr(1));
                Assert(object.ReferenceEquals(input.Font, originalFont), "Typing recreated the font and interrupted IME composition.");
                Assert(TextGeometry.Size(mark).Width < 160, "Short text left a large empty tail.");
                var gap = TextGeometry.Caret(mark, 3); gap.Y += TextGeometry.LineHeight(mark) / 2;
                using (var screenshot = new Bitmap(canvas.Width, canvas.Height))
                {
                    canvas.DrawToBitmap(screenshot, canvas.ClientRectangle);
                    int x = (int)Math.Round((mark.Points[0].X + gap.X) * zoom), y = (int)Math.Round((mark.Points[0].Y + gap.Y) * zoom);
                    var pixel = screenshot.GetPixel(x, y);
                    Assert(pixel.R < 100 && pixel.G < 100 && pixel.B < 100, "Editing covered the screenshot with an opaque background.");
                    int redNearText = 0, redAtOrigin = 0;
                    for (int sy = 0; sy < screenshot.Height; sy++) for (int sx = 0; sx < screenshot.Width; sx++)
                    {
                        var color = screenshot.GetPixel(sx, sy);
                        if (color.R > 150 && color.G < 100 && color.B < 100)
                        { if (sx > 170 * zoom && sy > 110 * zoom) redNearText++; if (sx < 80 * zoom && sy < 80 * zoom) redAtOrigin++; }
                    }
                    Assert(redNearText > 15 && redAtOrigin == 0, "Typed text appeared at the top-left instead of its image position.");
                    if (zoom == 1) screenshot.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-text-transparent-options.png"));
                }
                input.Text = "长文字长文字长文字长文字长文字";
                float longWidth = TextGeometry.Size(mark).Width;
                Assert(Math.Abs(longWidth - input.Text.Length * mark.FontSize - 6) < 12, "CJK text measurement accumulated excess width: " + longWidth);
                input.Text = "短";
                Assert(TextGeometry.Size(mark).Width < 40, "Deleting text did not shrink the frame.");
                input.Text = "第一行\r\n第二行";
                Assert(Math.Abs(TextGeometry.Size(mark).Height - (2 * TextGeometry.LineHeight(mark) + 4)) < 1, "Multiline height did not fit its content.");
                var from = TextGeometry.World(mark, TextGeometry.Caret(mark, 1));
                var to = TextGeometry.World(mark, TextGeometry.Caret(mark, 2));
                from.Y += 10; to.Y += 10;
                TextGesture(canvas, from, to);
                Assert(input.SelectedText == "一", "Mouse selection did not track the painted characters.");
                input.Select(input.TextLength, 0); input.Composition = "ceshi";
                typeof(InlineTextInput).GetMethod("NotifyInputState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(input, null);
                var display = (Mark)typeof(AnnotationCanvas).GetMethod("EditingTextMark", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(canvas, null);
                Assert(display.Text.EndsWith("ceshi") && !mark.Text.Contains("ceshi"), "IME preview was not shown separately from committed text.");
                input.Composition = "";
                canvas.CommitTextEdit();
                using (var exported = canvas.Render()) Assert(exported.GetPixel(0, 0).ToArgb() == canvas.Image.GetPixel(0, 0).ToArgb(), "Input chrome leaked into export.");
                host.Close();
            }
        }
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
                    var source = canvas.Image.GetPixel(32, 32); int gray = (source.R * 299 + source.G * 587 + source.B * 114 + 500) / 1000;
                    Assert(rendered.GetPixel(31, 32).ToArgb() == Color.FromArgb(gray, gray, gray).ToArgb(), "Partial edge block did not use grayscale mosaic.");
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
        { var found = parent.Controls.Find(name, true); if (found.Length == 0) throw new Exception("Missing option: " + name); return (T)found[0]; }
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
        private static void MouseWheel(Control control, int delta)
        { control.GetType().GetMethod("OnMouseWheel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(control, new object[] { new HandledMouseEventArgs(MouseButtons.None, 0, 10, 10, delta) }); }
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
                scene.WheelRenderDelay = 120;
                scene.Show(); scene.Activate(); scene.Refresh(); Application.DoEvents();
                WaitUntil(delegate
                {
                    using (var displayed = Native.Capture(scene.RectangleToScreen(scene.ClientRectangle)))
                        return displayed.GetPixel(15, 15).ToArgb() == scene.Content.GetPixel(15, 15).ToArgb()
                            && displayed.GetPixel(100, 200).ToArgb() == scene.Content.GetPixel(100, 200).ToArgb();
                });
                using (var session = new ScrollCaptureSession(delegate { return Native.Capture(scene.RectangleToScreen(scene.ClientRectangle)); }))
                {
                    var task = session.RunAsync(); int wheels = 0; var next = DateTime.UtcNow.AddMilliseconds(250);
                    var deadline = DateTime.UtcNow.AddSeconds(22);
                    while (!task.IsCompleted && DateTime.UtcNow < deadline)
                    {
                        Application.DoEvents(); System.Threading.Thread.Sleep(10);
                        if (DateTime.UtcNow < next) continue;
                        if (wheels < 4) { MouseWheel(scene, -120); wheels++; next = DateTime.UtcNow.AddMilliseconds(250); }
                        else session.Finish();
                    }
                    Assert(task.IsCompleted, "Manual capture timed out.");
                    if (task.IsFaulted) throw task.Exception;
                    using (var result = task.Result) EqualPixels(scene.Content, result.Image, scene.Content.Height);
                }
                scene.Close();
            }
            Cursor.Position = oldCursor;
        }
        private static void TestLiveIntegratedScroll()
        {
            using (var scene = new ScrollTestWindow())
            {
                scene.WheelRenderDelay = 120;
                scene.Show(); scene.Activate(); scene.Refresh(); Application.DoEvents();
                var bounds = new Rectangle(20, 20, 900, 600);
                using (var overlay = new CaptureOverlay(false, Native.Capture(bounds), bounds))
                {
                    overlay.InterceptScrollWheel = false;
                    overlay.Show();
                    Mouse(overlay, "OnMouseDown", MouseButtons.Left, 80, 70); Mouse(overlay, "OnMouseUp", MouseButtons.Left, 360, 310);
                    var task = overlay.CaptureLongAsync();
                    WaitUntil(delegate { var session = Field<ScrollCaptureSession>(overlay, "scrollSession"); return session != null && session.Height == 240; });
                    // Deliver native wheel messages without relying on the shared desktop's mouse position.
                    SendMessage(overlay.Handle, 0x020A, new IntPtr(-120 << 16), IntPtr.Zero);
                    WaitUntil(delegate { return task.IsCompleted || Field<ScrollCaptureSession>(overlay, "scrollSession").Height == 345; });
                    Assert(!task.IsCompleted, "Delayed first wheel ended capture: " + Field<Label>(overlay, "sizeHint").Text);
                    for (int i = 0; i < 3; i++) SendMessage(overlay.Handle, 0x020A, new IntPtr(-120 << 16), IntPtr.Zero);
                    WaitUntil(delegate { return task.IsCompleted || Field<ScrollCaptureSession>(overlay, "scrollSession").Height == 660; });
                    Assert(!task.IsCompleted, "Delayed wheel burst ended capture: " + Field<Label>(overlay, "sizeHint").Text);
                    Assert(Field<ScrollCaptureSession>(overlay, "scrollSession").Frames == 5, "Rapid native wheel input skipped a viewport before sampling it.");
                    Option<Button>(Field<CaptureToolbar>(overlay, "toolbar"), "finish-scroll-capture").PerformClick(); Pump(task);
                    var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                    using (var image = canvas.Render()) EqualPixels(scene.Content, image, 660);
                    MouseWheel(canvas, -120); Assert(canvas.DocumentOffsetY > 0 && canvas.Height == 240, "Live preview did not stay inside its frame.");
                    ToolPreview(overlay, "integrated-long"); overlay.Close();
                    Assert(Field<ScrollWheelRouter>(overlay, "scrollWheel") == null, "Completion left the wheel hook active.");
                }
                scene.Close();
            }
            var color = Color.FromArgb(37, 91, 163);
            using (var scene = new Form { Bounds = new Rectangle(20, 20, 900, 600), StartPosition = FormStartPosition.Manual, FormBorderStyle = FormBorderStyle.None,
                BackColor = color, TopMost = true, ShowInTaskbar = false })
            {
                scene.Show(); scene.Refresh(); Application.DoEvents();
                using (var overlay = new CaptureOverlay(false, Native.Capture(scene.Bounds), scene.Bounds))
                {
                    overlay.InterceptScrollWheel = false;
                    overlay.Show(); Mouse(overlay, "OnMouseDown", MouseButtons.Left, 0, 0); Mouse(overlay, "OnMouseUp", MouseButtons.Left, 900, 600);
                    var task = overlay.CaptureLongAsync();
                    WaitUntil(delegate { var session = Field<ScrollCaptureSession>(overlay, "scrollSession"); return session != null && session.Height == 600; });
                    var toolbar = Field<CaptureToolbar>(overlay, "toolbar"); var at = toolbar.Bounds;
                    Option<Button>(toolbar, "finish-scroll-capture").PerformClick(); Pump(task);
                    using (var image = Field<AnnotationCanvas>(overlay, "canvas").Render())
                        Assert(image.GetPixel(at.Left + 20, at.Top + 20).ToArgb() == color.ToArgb(), "The finish button contaminated a full-frame capture.");
                    overlay.Close();
                }
                scene.Close();
            }
        }
        private sealed class ScrollTestWindow : Form
        {
            internal readonly Bitmap Content = Pattern(280, 660, 41);
            internal int WheelRenderDelay;
            private int offset;
            private int pendingOffset;
            private readonly Timer delayedWheel = new Timer();
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
                delayedWheel.Tick += delegate { delayedWheel.Stop(); offset = Math.Min(Content.Height - ClientSize.Height, offset + pendingOffset); pendingOffset = 0; Invalidate(); };
            }
            protected override void OnPaint(PaintEventArgs e) { e.Graphics.DrawImageUnscaled(Content, 0, -offset); }
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                if (WheelRenderDelay == 0) { offset = Math.Min(Content.Height - ClientSize.Height, offset + 105); Invalidate(); }
                else { pendingOffset += 105; if (!delayedWheel.Enabled) { delayedWheel.Interval = WheelRenderDelay; delayedWheel.Start(); } }
                base.OnMouseWheel(e);
            }
            protected override void Dispose(bool disposing) { if (disposing) { delayedWheel.Dispose(); Content.Dispose(); } base.Dispose(disposing); }
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
