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
        private static GifClip Fixture()
        {
            var clip = new GifClip { Duration = 300 };
            foreach (var color in new[] { Color.Red, Color.Lime, Color.Blue })
            using (var image = new Bitmap(128, 96)) using (var bytes = new MemoryStream())
            { using (var graphics = Graphics.FromImage(image)) graphics.Clear(color); image.Save(bytes, ImageFormat.Png); clip.Frames.Add(new GifFrame { Time = clip.Frames.Count * 100, Png = bytes.ToArray() }); }
            return clip;
        }
        private static void Line(AnnotationCanvas canvas, int y)
        {
            var mark = new Mark { Tool = DrawTool.Pen, Color = Color.White, Width = 5 }; mark.Points.Add(new PointF(10, y)); mark.Points.Add(new PointF(45, y)); canvas.AddMark(mark);
        }
        internal static void Run()
        {
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
                Key(editor, Keys.Control | Keys.Z); Assert(clip.Frames.Count == 1, "Annotation undo restored deleted frames.");
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
