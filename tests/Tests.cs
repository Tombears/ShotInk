using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace SnipasteLite
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
                Run("several scroll offsets reconstruct the original pixels", TestStitch);
                Run("unchanged frame does not duplicate content", TestUnchanged);
                Run("unrelated frames are rejected", TestUnrelated);
                Run("changed frame dimensions are rejected", TestDimensions);
                Run("height limit rejects oversized first frame", TestInitialLimit);
                Run("accumulated height is bounded", TestAccumulatedLimit);
                Run("annotations render at original resolution and undo", TestAnnotations);
                Run("mosaic covers partial edge blocks", TestMosaic);
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
                canvas.Marks.Add(rectangle); canvas.SetZoom(0.25F);
                using (var rendered = canvas.Render())
                { Assert(rendered.Size == new Size(120, 100), "Zoom changed export dimensions."); Assert(rendered.GetPixel(20, 50).R > 240 && rendered.GetPixel(20, 50).G < 40, "Annotation missing."); }
                Assert(canvas.Image.GetPixel(20, 50).ToArgb() == Color.White.ToArgb(), "Source image mutated.");
                canvas.Undo(); Assert(canvas.Marks.Count == 0, "Undo failed.");
            }
        }
        private static void TestMosaic()
        {
            using (var canvas = new AnnotationCanvas(Pattern(90, 100, 32)))
            {
                var mark = new Mark { Tool = DrawTool.Mosaic, Width = 3 };
                mark.Points.Add(new PointF(10, 10)); mark.Points.Add(new PointF(32, 33)); canvas.Marks.Add(mark);
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
            Console.WriteLine("Previews: " + root);
        }
    }
}
