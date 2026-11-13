using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShotInk
{
    internal static class GifToolsTests
    {
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        private static T Field<T>(object item, string name) { return (T)item.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(item); }
        private static void Key(GifEditorForm editor, Keys key) { typeof(GifEditorForm).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(editor, new object[] { new Message(), key }); }
        private static void Mouse(Control control, string method, int x, int y)
        { control.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control, new object[] { new MouseEventArgs(MouseButtons.Left, 1, x, y, 0) }); }
        private static GifClip Fixture()
        {
            var clip = new GifClip { Duration = 300 };
            foreach (var color in new[] { Color.Red, Color.Lime, Color.Blue })
            using (var image = new Bitmap(128, 96)) using (var bytes = new MemoryStream())
            { using (var graphics = Graphics.FromImage(image)) graphics.Clear(color); image.Save(bytes, ImageFormat.Png); clip.Frames.Add(new GifFrame { Time = clip.Frames.Count * 100, Png = bytes.ToArray() }); }
            return clip;
        }
        private static void TestFrameControls()
        {
            var clip = Fixture();
            var editorSettings = new AppSettings();
            using (var editor = new GifEditorForm(clip, editorSettings))
            {
                editor.Show(); Application.DoEvents();
                Assert(editor.Controls.Find("gif-frame-count", true).Length == 0, "GIF retained the removed frame count adjustment.");
                Key(editor, Keys.Right);
                Field<Button>(editor, "deleteCurrent").PerformClick();
                Assert(clip.Frames.Count == 2 && clip.Duration == 200 && Field<int>(editor, "frameIndex") == 1, "Delete current frame affected the full selected range.");
                using (var image = clip.Frames[1].Decode()) Assert(image.GetPixel(60, 50).B > 240, "Current-frame deletion removed the wrong image.");
                Field<Button>(editor, "deleteCurrent").PerformClick(); Field<Button>(editor, "deleteCurrent").PerformClick();
                Assert(clip.Frames.Count == 0 && !Field<Button>(editor, "deleteCurrent").Enabled, "Empty clip retained frame controls.");
                Key(editor, Keys.Control | Keys.Z); Key(editor, Keys.Control | Keys.Z); Key(editor, Keys.Control | Keys.Z);
                Assert(clip.Frames.Count == 3 && clip.Duration == 300 && clip.Frames.Select(frame => frame.Time).SequenceEqual(new[] { 0, 100, 200 }), "Undo did not restore deleted frames and original timing.");
                var range = Field<GifRangeSlider>(editor, "range"); range.SetRange(1, 2);
                var firstInput = (SliderValueBox)range.Controls.Find("gif-first-frame", true)[0];
                var lastInput = (SliderValueBox)range.Controls.Find("gif-last-frame", true)[0];
                firstInput.Text = "1"; lastInput.Text = "2";
                Assert(range.First == 0 && range.Last == 1 && firstInput.Parent.Left < range.Width / 2 && lastInput.Parent.Left > range.Width / 2, "GIF endpoint inputs did not edit the matching range at the requested corners.");
                range.SetRange(1, 2);
                Key(editor, Keys.Control | Keys.Shift | Keys.X);
                Assert(clip.Frames.Count == 1 && clip.Duration == 100, "Ctrl+Shift+X did not delete the selected interval.");
                Key(editor, Keys.Control | Keys.Z);
                Assert(clip.Frames.Count == 3 && range.First == 1 && range.Last == 2, "Undo did not restore selection.");
                Key(editor, Keys.Control | Keys.X); Assert(clip.Frames.Count == 2, "Ctrl+X did not delete only the current frame.");
                Key(editor, Keys.Control | Keys.Z); range.SelectBoundary(true); range.SetRange(0, 2);
                editorSettings.GifDeleteCurrentShortcut = Keys.Control | Keys.Alt | Keys.D;
                editorSettings.GifUndoShortcut = Keys.Control | Keys.Alt | Keys.Z;
                Key(editor, Keys.Control | Keys.X); Assert(clip.Frames.Count == 3, "Disabled default shortcut remained active.");
                var toolbar = Field<CaptureToolbar>(editor, "toolbar"); toolbar.SelectTool(DrawTool.Pen); Line(Field<AnnotationCanvas>(editor, "frameCanvas"), 15);
                Key(editor, editorSettings.GifDeleteCurrentShortcut); Assert(clip.Frames.Count == 2, "Configured deletion shortcut was ignored.");
                Key(editor, editorSettings.GifUndoShortcut); Assert(clip.Frames.Count == 3 && clip.Frames[0].Annotations.Count == 1, "Undo deletion lost frame annotations.");
                Key(editor, editorSettings.GifUndoShortcut); Assert(clip.Frames[0].Annotations.Count == 0, "Deletion discarded earlier annotation history.");
                Key(editor, Keys.Control | Keys.Y); Key(editor, Keys.Control | Keys.Y);
                Assert(clip.Frames.Count == 2, "Redo failed across annotation and deletion operations.");
                editor.Close();
            }
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gif-settings-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = Path.Combine(root, "settings.ini"); var settings = AppSettings.Load(path);
                Assert(settings.GifFramesPerSecond == 30, "Default GIF frame rate did not increase from 10 to 30.");
                settings.GifFramesPerSecond = 24; settings.GifDeleteCurrentShortcut = Keys.Control | Keys.Alt | Keys.D; settings.Save(path);
                Assert(AppSettings.Load(path).Clone().GifFramesPerSecond == 24, "GIF frame rate did not survive save, load and clone.");
                Assert(AppSettings.Load(path).Clone().GifDeleteCurrentShortcut == settings.GifDeleteCurrentShortcut, "GIF shortcuts did not survive save, load and clone.");
                using (var form = new SettingsForm(settings, delegate { return null; }, delegate { return true; }))
                {
                    var number = (NumericUpDown)form.Controls.Find("gif-fps", true)[0]; number.Value = 50;
                    Assert(form.ReadSettings().GifFramesPerSecond == 50, "Settings UI ignored the default GIF frame rate.");
                    ((ShortcutBox)form.Controls.Find("gif-delete-selected", true)[0]).ShortcutValue = Keys.Control | Keys.Alt | Keys.S;
                    Assert(form.ReadSettings().GifDeleteSelectedShortcut == (Keys.Control | Keys.Alt | Keys.S), "Settings UI ignored the GIF deletion shortcut.");
                    form.Show(); Application.DoEvents();
                    var tabs = form.Controls.OfType<TabControl>().Single(); tabs.SelectedIndex = 1; Application.DoEvents();
                    using (var preview = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size)); preview.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-settings-gif-options.png")); }
                    form.Close();
                }
                using (var session = new GifCaptureSession(delegate { return new Bitmap(2, 2); }, 24))
                    Assert(Math.Abs(Field<double>(session, "interval") - 1000.0 / 24) < 0.001, "Capture ignored the configured frame rate.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        private static void TestDragPreview()
        {
            var clip = new GifClip { Duration = 2400 };
            var colors = new[] { Color.Red, Color.Lime, Color.Blue };
            var images = new byte[3][];
            for (int index = 0; index < images.Length; index++)
            using (var image = new Bitmap(768, 432)) using (var bytes = new MemoryStream())
            { using (var graphics = Graphics.FromImage(image)) graphics.Clear(colors[index]); image.Save(bytes, ImageFormat.Png); images[index] = bytes.ToArray(); }
            for (int index = 0; index < 60; index++) clip.Frames.Add(new GifFrame { Time = index * 40, Png = images[index % 3] });
            using (var editor = new GifEditorForm(clip, new AppSettings()))
            {
                editor.Show(); Application.DoEvents(); var range = Field<GifRangeSlider>(editor, "range"); var preview = Field<PictureBox>(editor, "preview");
                var firstImage = preview.Image; var clock = System.Diagnostics.Stopwatch.StartNew();
                for (int index = 0; index < GifEncoder.Workers; index++) GifRenderWorkers.Gate.Wait();
                try
                {
                    Mouse(range, "OnMouseDown", 18, 38);
                    for (int index = 0; index < 300; index++) Mouse(range, "OnMouseMove", index % 2 == 0 ? 18 : range.Width / 2, 38);
                    Console.WriteLine("GIF timeline: " + clock.ElapsedMilliseconds + " ms for 300 pointer updates.");
                    Assert(ReferenceEquals(firstImage, preview.Image) && Field<bool>(editor, "dragPreviewRunning"), "Dragging decoded preview frames synchronously on the UI thread.");
                }
                finally { GifRenderWorkers.Gate.Release(GifEncoder.Workers); }
                int endX = 18 + (int)Math.Round((range.Width - 36) * 31.0 / 59);
                Mouse(range, "OnMouseUp", endX, 38);
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (Field<bool>(editor, "dragPreviewRunning") && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
                Assert(!Field<bool>(editor, "dragPreviewRunning") && Field<int>(editor, "frameIndex") == 31 && ((Bitmap)preview.Image).GetPixel(50, 50).G > 240, "A stale preview replaced the final dragged frame.");
                Mouse(range, "OnMouseDown", 18, 38); editor.Close();
                deadline = DateTime.UtcNow.AddSeconds(10);
                while (Field<bool>(editor, "dragPreviewRunning") && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
                Assert(!Field<bool>(editor, "dragPreviewRunning"), "Closing the GIF editor left a drag preview worker running.");
            }
        }
        private static void Line(AnnotationCanvas canvas, int y)
        {
            var mark = new Mark { Tool = DrawTool.Pen, Color = Color.White, Width = 5 }; mark.Points.Add(new PointF(10, y)); mark.Points.Add(new PointF(45, y)); canvas.AddMark(mark);
        }
        private static void Wait(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
            Assert(task.IsCompleted, "Recording operation timed out."); task.GetAwaiter().GetResult();
        }
        private static void TestVideoTools()
        {
            TestAudioTrack();
            Assert(File.Exists(VideoExport.EncoderPath), "Recording encoder was omitted from the test output.");
            var settings = new AppSettings { RecordSpeaker = false, RecordingGuideSeen = true };
            var desktop = new Bitmap(640, 480);
            using (var graphics = Graphics.FromImage(desktop)) graphics.Clear(Color.White);
            using (var overlay = new CaptureOverlay(false, desktop, new Rectangle(0, 0, 640, 480), settings))
            {
                overlay.Show(); overlay.SelectRegion(new Rectangle(40, 80, 320, 180)); Application.DoEvents();
                var bar = Field<CaptureToolbar>(overlay, "toolbar");
                var strip = Field<ToolStrip>(bar, "strip");
                Assert(strip.Items.IndexOf(Field<ToolStripButton>(bar, "video")) == strip.Items.IndexOf(Field<ToolStripButton>(bar, "gif")) + 1, "Video button must follow GIF.");
                Field<ToolStripButton>(bar, "video").PerformClick();
                Assert(!Field<bool>(overlay, "ready"), "Recording must allow a fresh region selection.");
                overlay.SelectRegion(new Rectangle(40, 80, 320, 180)); Application.DoEvents();
                Assert(Field<Panel>(overlay, "recordingOptions").Visible && !Field<CaptureToolbar>(overlay, "toolbar").Visible, "Recording selection controls are missing.");
                Assert(Field<Panel>(overlay, "recordingOptions").Height == bar.Height, "Recording toolbar height differs from the capture toolbar.");
                var tips = Field<ToolTip>(overlay, "recordingTip");
                Assert(!tips.IsBalloon && tips.ToolTipTitle.Length == 0, "Recording tips must use the standard tooltip style.");
                foreach (Control control in Field<Panel>(overlay, "recordingOptions").Controls)
                    Assert(control.Bounds.Bottom <= 30 && control.Bounds.Right <= 204, "Recording controls overflow their compact toolbar.");
                Assert(Field<Panel>(overlay, "recordingOptions").Top > 260 && overlay.Controls.Find("recording-drag", true).Length == 1, "Recording options should start below the region and provide a drag handle.");
                Field<AnnotationCanvas>(overlay, "canvas").Hide();
                overlay.GetType().GetField("recordingCountdownValue", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(overlay, 3);
                using (var image = new Bitmap(640, 480))
                {
                    overlay.DrawToBitmap(image, new Rectangle(0, 0, 640, 480));
                    Assert(image.GetPixel(121, 91).ToArgb() == Color.White.ToArgb(), "Countdown retained a rectangular background.");
                    var fill = image.GetPixel(269, 170);
                    Assert(fill.R >= 159 && fill.R <= 162, "Countdown background must be 60 percent transparent.");
                }
                overlay.GetType().GetField("recordingCountdownValue", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(overlay, 0);
                Field<AnnotationCanvas>(overlay, "canvas").Show();
                overlay.CaptureGifFrame = delegate { return new Bitmap(32, 32); };
                using (var stop = new System.Windows.Forms.Timer { Interval = 50 })
                {
                    stop.Tick += delegate
                    {
                        var session = Field<GifCaptureSession>(overlay, "gifSession");
                        var navigation = Field<Panel>(overlay, "recordingNavigation");
                        if (navigation != null)
                        {
                            Assert(navigation.Height == 30, "Recording navigation must match the capture toolbar height.");
                            foreach (Control control in navigation.Controls)
                                Assert(control.Bounds.Bottom <= navigation.Height && control.Bounds.Right <= navigation.Width, "Recording navigation controls overlap the edge.");
                        }
                        if (session != null && session.Elapsed > 200) { stop.Stop(); session.Finish(); }
                    };
                    stop.Start(); var clock = System.Diagnostics.Stopwatch.StartNew();
                    Wait((Task)overlay.GetType().GetMethod("CaptureVideoAsync", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(overlay, null));
                    Assert(clock.ElapsedMilliseconds >= 3000, "Recording skipped the countdown.");
                }
                var videoEditor = Application.OpenForms.Cast<Form>().OfType<GifEditorForm>().Single();
                Assert(videoEditor.Text.Contains("编辑录屏"), "Recording did not open the video editor."); videoEditor.Close();
            }
            using (var capture = new GifCaptureSession(delegate { return new Bitmap(32, 32); }, 10))
            {
                var running = capture.RunAsync(); Thread.Sleep(140); capture.SetPaused(true); int elapsed = capture.Elapsed;
                Thread.Sleep(120); Assert(Math.Abs(capture.Elapsed - elapsed) < 15, "Pause advanced the recording clock.");
                capture.SetPaused(false); Thread.Sleep(120); capture.Finish(); Wait(running);
                Assert(running.Result.Frames.Count > 1 && running.Result.Duration < 360, "Pause was included in the output timeline.");
            }
            var clip = Fixture(); clip.Recording = new RecordingMedia();
            for (int index = 0; index < clip.Frames.Count; index++) { clip.Frames[index].SourceTime = index * 100; clip.Frames[index].SourceDuration = 100; }
            string wav = Path.Combine(clip.Recording.DirectoryPath, "tone.wav");
            using (var writer = new NAudio.Wave.WaveFileWriter(wav, new NAudio.Wave.WaveFormat(48000, 16, 1)))
                for (int sample = 0; sample < 14400; sample++) writer.WriteSample((float)(Math.Sin(sample * 2 * Math.PI * 440 / 48000) * .2));
            clip.Recording.AudioPaths.Add(wav);
            using (var editor = new GifEditorForm(clip, settings))
            {
                editor.Show(); Application.DoEvents();
                var toolbar = Field<CaptureToolbar>(editor, "toolbar");
                Assert(!Field<ToolStrip>(toolbar, "strip").Items.Cast<ToolStripItem>().Any(item => item.Name == "recognize-text" || item.Name == "video-capture"), "Unsuitable video editor tools remained visible.");
                Assert(Field<GifScopeSelector>(editor, "scope").Value == GifEditScope.Selected, "Video annotation scope should start at the selected range.");
                Field<GifRangeSlider>(editor, "range").SetRange(1, 1); Key(editor, settings.GifDeleteSelectedShortcut);
                Assert(clip.Frames.Count == 2 && clip.Duration == 200 && clip.Frames[1].SourceTime == 200, "Deletion lost audio source timing.");
                var export = VideoExport.Encode(clip, 0, 1, CancellationToken.None); Wait(export);
                Assert(new FileInfo(export.Result).Length > 500, "MP4 export failed.");
                Key(editor, settings.GifUndoShortcut); Assert(clip.Frames.Count == 3 && clip.Duration == 300, "Video deletion undo failed.");
                editor.Close();
            }
            Console.WriteLine("PASS video entry, selection, pause timing, audio-aware segment deletion, MP4 export and undo");
        }
        private sealed class TestAudioInput : NAudio.Wave.IWaveIn
        {
            public NAudio.Wave.WaveFormat WaveFormat { get; set; }
            public event EventHandler<NAudio.Wave.WaveInEventArgs> DataAvailable;
            public event EventHandler<NAudio.Wave.StoppedEventArgs> RecordingStopped;
            internal byte[] Samples;
            internal bool Disposed;
            public void StartRecording() { DataAvailable(this, new NAudio.Wave.WaveInEventArgs(Samples, Samples.Length)); }
            public void StopRecording() { RecordingStopped(this, new NAudio.Wave.StoppedEventArgs()); }
            public void Dispose() { Disposed = true; }
        }
        private static void TestAudioTrack()
        {
            foreach (var format in new[] { new NAudio.Wave.WaveFormat(16000, 16, 1), NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48000, 2) })
            using (var media = new RecordingMedia())
            {
                var device = new TestAudioInput { WaveFormat = format, Samples = new byte[format.AverageBytesPerSecond / 5] };
                for (int index = 0; index < device.Samples.Length; index++) device.Samples[index] = (byte)(index % 127);
                typeof(RecordingMedia).GetMethod("AddTrack", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(media, new object[] { device, "input.wav" });
                Wait(media.StopAudio());
                using (var reader = new NAudio.Wave.WaveFileReader(media.AudioPaths.Single()))
                {
                    Assert(reader.WaveFormat.SampleRate == format.SampleRate && reader.WaveFormat.Channels == format.Channels && reader.WaveFormat.Encoding == format.Encoding, "Capture changed the native input format.");
                    var captured = new byte[device.Samples.Length];
                    Assert(reader.Read(captured, 0, captured.Length) == captured.Length && captured.SequenceEqual(device.Samples), "Captured microphone packets were lost or altered.");
                }
                Assert(device.Disposed, "Stopped audio input was not released.");
            }
        }
        internal static void Run()
        {
            TestVideoTools();
            TestFrameControls();
            TestDragPreview();
            var clip = Fixture();
            using (var editor = new GifEditorForm(clip, new AppSettings()))
            {
                editor.Show(); Application.DoEvents();
                var toolbar = Field<CaptureToolbar>(editor, "toolbar"); var range = Field<GifRangeSlider>(editor, "range"); var scope = Field<GifScopeSelector>(editor, "scope");
                Assert(scope.Value == GifEditScope.Current, "GIF scope did not default to current frame.");
                var row = toolbar.Parent;
                Assert(row.Top < Field<Label>(editor, "position").Top && scope.Left >= toolbar.Right, "GIF toolbar or scope selector was not placed above frame count at the right edge.");
                var strip = Field<ToolStrip>(toolbar, "strip");
                Assert(!strip.Items.Cast<ToolStripItem>().Any(item => item.Name == "scroll-capture" || item.Name == "gif-capture"), "GIF annotation toolbar retained capture tools.");
                toolbar.SelectTool(DrawTool.Pen); Line(Field<AnnotationCanvas>(editor, "frameCanvas"), 12);
                Assert(clip.Frames[0].Annotations.Count == 1 && clip.Frames[1].Annotations.Count == 0 && clip.Frames[2].Annotations.Count == 0, "Current-frame edits changed other frames.");
                range.SetRange(1, 2); scope.SelectScope(GifEditScope.Selected); Line(Field<AnnotationCanvas>(editor, "frameCanvas"), 32);
                Assert(clip.Frames[0].Annotations.Count == 1 && clip.Frames[1].Annotations.Count == 1 && clip.Frames[2].Annotations.Count == 1, "Selected-frame edits escaped the selected interval.");
                scope.SelectScope(GifEditScope.All); Line(Field<AnnotationCanvas>(editor, "frameCanvas"), 52);
                Assert(clip.Frames.All(frame => frame.Annotations.Count == 2), "All-frame edits were not applied to every frame.");
                var cache = Field<GifFrameCache>(editor, "frameCache"); cache.Pending.GetAwaiter().GetResult();
                Assert(cache.LastError == null && cache.CachedBytes <= GifFrameCache.Budget && GifEncoder.Workers >= 1 && GifEncoder.Workers <= 4, "Background rendering failed or exceeded its resource limits.");
                Key(editor, Keys.Control | Keys.Z); Assert(clip.Frames.All(frame => frame.Annotations.Count == 1), "Undo did not restore the whole multi-frame edit.");
                Key(editor, Keys.Control | Keys.Y); Assert(clip.Frames.All(frame => frame.Annotations.Count == 2), "Redo did not restore the whole multi-frame edit.");
                scope.SelectScope(GifEditScope.Current); Key(editor, Keys.Right); Key(editor, Keys.Left);
                Assert(Field<AnnotationCanvas>(editor, "frameCanvas").Marks.Count == 2, "Switching frames lost annotations.");
                byte[] gif = Task.Run(delegate
                {
                    var context = SynchronizationContext.Current;
                    using (var raster = clip.Frames[0].Render(clip.Frames[0].Annotations)) { }
                    Assert(ReferenceEquals(context, SynchronizationContext.Current), "Background drawing installed a UI context on a worker thread.");
                    return GifEncoder.Encode(clip, 0, 2);
                }).GetAwaiter().GetResult();
                using (var stream = new MemoryStream(gif)) using (var image = Image.FromStream(stream))
                {
                    Assert(image.GetFrameCount(FrameDimension.Time) == 3, "Annotated GIF lost frames.");
                    for (int index = 0; index < 3; index++)
                    {
                        image.SelectActiveFrame(FrameDimension.Time, index); using (var frame = new Bitmap(image))
                        {
                            Color background = frame.GetPixel(90, 80), ink = frame.GetPixel(25, 52);
                            Assert(ink.R > 220 && ink.G > 220 && ink.B > 220, "Export omitted an all-frame annotation.");
                            Assert(index == 0 ? background.R > 220 && background.G < 30 : index == 1 ? background.G > 220 && background.B < 30 : background.B > 220 && background.R < 30, "Batch edits replaced a frame's original image.");
                        }
                    }
                }
                using (var screenshot = new Bitmap(editor.Width, editor.Height)) { editor.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size)); screenshot.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-gif-frame-tools-options.png")); }
                Field<Button>(editor, "delete").PerformClick();
                Assert(clip.Frames.Count == 1 && range.First == 0 && range.Last == 0 && range.Maximum == 0, "Deletion with frame annotations did not reset its range.");
                Key(editor, Keys.Control | Keys.Z); Assert(clip.Frames.Count == 3 && clip.Frames.All(frame => frame.Annotations.Count == 2), "Undo deletion did not restore all annotated frames.");
                editor.Close();
            }
            string directory = Path.Combine(Path.GetTempPath(), "ShotInk-gif-export-" + Guid.NewGuid().ToString("N"));
            try
            {
                clip = new GifClip { Duration = 6000 };
                using (var image = new Bitmap(320, 180)) using (var data = new MemoryStream())
                {
                    using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.CornflowerBlue);
                    image.Save(data, ImageFormat.Png); byte[] png = data.ToArray();
                    for (int index = 0; index < 60; index++) clip.Frames.Add(new GifFrame { Time = index * 100, Png = png });
                }
                using (var editor = new GifEditorForm(clip, new AppSettings { AskSaveLocation = false, SaveDirectory = directory }))
                using (var pulse = new System.Windows.Forms.Timer { Interval = 10 })
                {
                    editor.Show(); Application.DoEvents(); int beats = 0; pulse.Tick += delegate { beats++; }; pulse.Start();
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    var task = (Task)typeof(GifEditorForm).GetMethod("ExportAsync", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(editor, new object[] { false });
                    Assert(clock.ElapsedMilliseconds < 1000, "GIF generation blocked the editor before yielding to its worker.");
                    var deadline = DateTime.UtcNow.AddSeconds(20);
                    while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(1); }
                    Assert(task.IsCompleted, "Background GIF generation did not finish."); task.GetAwaiter().GetResult();
                    Assert(clock.ElapsedMilliseconds < 80 || beats > 0, "GIF generation stopped the UI message loop.");
                    Assert(Directory.GetFiles(directory, "*.gif").Length == 1, "Background GIF export did not save its result."); editor.Close();
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
#if !SHOTINK_LITE
            var recognizer = new FrameRecognizer(); clip = Fixture();
            using (var editor = new GifEditorForm(clip, new AppSettings(), recognizer))
            {
                editor.Show(); Application.DoEvents(); var toolbar = Field<CaptureToolbar>(editor, "toolbar");
                Field<GifScopeSelector>(editor, "scope").SelectScope(GifEditScope.All);
                Field<ToolStripButton>(toolbar, "recognize").PerformClick(); Application.DoEvents();
                Assert(recognizer.Calls == 1 && Field<AnnotationCanvas>(editor, "frameCanvas").HasTextRecognition && clip.Frames.All(frame => frame.Annotations.Count == 0), "OCR was applied outside the current frame or changed frame graphics.");
                Assert(Field<GifScopeSelector>(editor, "scope").Value == GifEditScope.All && Field<GifScopeSelector>(editor, "scope").Enabled, "OCR changed or locked the drawing scope selector.");
                Key(editor, Keys.Right); Assert(Field<AnnotationCanvas>(editor, "frameCanvas") == null, "OCR selection leaked into another frame."); editor.Close();
            }
#else
            Assert(typeof(GifEditorForm).Assembly.GetType("ShotInk.RapidTextRecognizer") == null && typeof(GifEditorForm).Assembly.GetType("ShotInk.OcrSelectionModel") == null, "Lite assembly still contains OCR logic.");
#endif
        }
#if !SHOTINK_LITE
        private sealed class FrameRecognizer : ITextRecognizer
        {
            internal int Calls;
            public Task<OcrDocument> RecognizeAsync(Bitmap image, CancellationToken cancellation)
            {
                Calls++; return Task.FromResult(new OcrDocument { Width = image.Width, Height = image.Height, Lines = new[] { new OcrLine { Text = "X", Box = new float[] { 10, 10, 20, 20 }, Characters = new[] { new OcrCharacter { Text = "X", Start = 0, Length = 1, Box = new float[] { 10, 10, 20, 20 } } } } } });
            }
        }
#endif
    }
}
