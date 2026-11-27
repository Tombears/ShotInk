using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShotInk
{
    internal static class CaptureWorkflowTests
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct CursorInfo
        {
            internal bool Icon;
            internal int HotspotX, HotspotY;
            internal IntPtr Mask, Color;
        }
        [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr cursor, out CursorInfo info);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr word, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr bitmap);

        private static Bitmap NativeCursorPixels(Cursor cursor)
        {
            CursorInfo info;
            Check(GetIconInfo(cursor.Handle, out info), "Windows could not inspect the cursor handle.");
            try
            {
                Check(!info.Icon && info.Color != IntPtr.Zero, "Windows loaded the cursor as a monochrome mask.");
                return Image.FromHbitmap(info.Color);
            }
            finally
            {
                if (info.Color != IntPtr.Zero) DeleteObject(info.Color);
                if (info.Mask != IntPtr.Zero) DeleteObject(info.Mask);
            }
        }

        private static bool CursorHasColor(Cursor cursor, Color expected)
        {
            using (var image = NativeCursorPixels(cursor))
                for (int y = 0; y < image.Height; y++)
                    for (int x = 0; x < image.Width; x++)
                    {
                        var pixel = image.GetPixel(x, y);
                        if (Math.Abs(pixel.R - expected.R) + Math.Abs(pixel.G - expected.G) + Math.Abs(pixel.B - expected.B) <= 20) return true;
                    }
            return false;
        }

        private static void CheckCursorWithoutSideIcon(Cursor cursor, Point hotspot)
        {
            Check(cursor.HotSpot == hotspot, "Simplified cursor moved its hotspot.");
            using (var rendered = new Bitmap(32, 32, PixelFormat.Format24bppRgb))
            {
                var background = Color.MediumVioletRed;
                using (var graphics = Graphics.FromImage(rendered))
                {
                    graphics.Clear(background);
                    cursor.Draw(graphics, new Rectangle(Point.Empty, rendered.Size));
                }
                bool visible = false;
                for (int y = 0; y < rendered.Height; y++)
                    for (int x = 0; x < rendered.Width; x++)
                    {
                        if (rendered.GetPixel(x, y).ToArgb() == background.ToArgb()) continue;
                        Check(x < 16, "A tool icon was drawn next to a simplified cursor.");
                        visible = true;
                    }
                Check(visible, "The simplified cursor had no visible marker.");
            }
        }

        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Private).GetValue(target); }
        private static void Call(object target, string name, params object[] args) { target.GetType().GetMethod(name, Private).Invoke(target, args); }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Mouse(Control target, string action, int x, int y) { Call(target, action, new MouseEventArgs(MouseButtons.Left, 1, x, y, 0)); }
        private static IntPtr PackedPoint(Point point) { return new IntPtr((point.Y << 16) | (point.X & 0xffff)); }
        private static void Wait(Func<bool> done)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!done()) { if (watch.ElapsedMilliseconds > 8000) throw new Exception("Capture timed out"); Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        }
        private static void UpdatePriority(string root)
        {
            using (var home = new MainForm(Path.Combine(root, "update-priority"), false))
            {
                home.Show(); Application.DoEvents();
                var pin = new PinForm(new Bitmap(40, 30)); pin.Show();
                var editor = new EditorForm(new Bitmap(40, 30), delegate(Bitmap image) { image.Dispose(); }, new AppSettings()); editor.Show();
                var clip = new GifClip { Duration = 100 };
                using (var image = new Bitmap(40, 30)) using (var bytes = new MemoryStream())
                { image.Save(bytes, ImageFormat.Png); clip.Frames.Add(new GifFrame { Time = 0, Png = bytes.ToArray() }); }
                var gif = new GifEditorForm(clip, new AppSettings()); gif.Show();
                Call(home, "BeginCapture", false);
                Wait(delegate { return Application.OpenForms.Cast<Form>().Any(f => f is CaptureOverlay && f.Visible); });
                var overlay = Application.OpenForms.Cast<Form>().OfType<CaptureOverlay>().Single();
                home.GetType().GetField("updateInstalling", Private).SetValue(home, true);
                var closing = home.CloseWindowsForUpdateAsync(System.Threading.CancellationToken.None);
                Wait(delegate { return closing.IsCompleted; });
                Check(closing.GetAwaiter().GetResult() == null && pin.IsDisposed && editor.IsDisposed && gif.IsDisposed && overlay.IsDisposed,
                    "Confirmed update did not close screenshot, pin and editor windows");
                Check(!Field<bool>(home, "captureBusy"), "Closing the screenshot left capture busy");
                Call(home, "BeginCapture", false); Call(home, "PinClipboard");
                Check(!Field<bool>(home, "captureBusy") && !Application.OpenForms.Cast<Form>().Any(f => f is PinForm || f is CaptureOverlay),
                    "Capture or pin reopened during update preparation");
                home.GetType().GetField("updateInstalling", Private).SetValue(home, false);
                Call(home, "BeginCapture", false);
                home.GetType().GetField("updateInstalling", Private).SetValue(home, true);
                closing = home.CloseWindowsForUpdateAsync(System.Threading.CancellationToken.None);
                Wait(delegate { return closing.IsCompleted; });
                Check(closing.GetAwaiter().GetResult() == null && !Field<bool>(home, "captureBusy") && !Application.OpenForms.Cast<Form>().Any(f => f is CaptureOverlay),
                    "Pending capture appeared after update confirmation");
                home.GetType().GetField("updateInstalling", Private).SetValue(home, false);
                using (var cancelled = new EditorForm(new Bitmap(40, 30), delegate(Bitmap image) { image.Dispose(); }, new AppSettings()))
                using (var keepPin = new PinForm(new Bitmap(40, 30)))
                {
                    cancelled.Show(); keepPin.Show();
                    FormClosingEventHandler cancel = delegate(object sender, FormClosingEventArgs e) { e.Cancel = true; };
                    cancelled.FormClosing += cancel;
                    var install = (System.Threading.Tasks.Task<string>)home.GetType().GetMethod("InstallUpdate", Private).Invoke(home,
                        new object[] { new PreparedUpdate(), System.Threading.CancellationToken.None });
                    Wait(delegate { return install.IsCompleted; });
                    Check(install.GetAwaiter().GetResult().Contains("已取消更新") && !Field<bool>(home, "updateInstalling") && !keepPin.IsDisposed,
                        "Cancelling editor close did not report cancellation and restore normal operation");
                    cancelled.FormClosing -= cancel; cancelled.Close(); keepPin.Close();
                }
                home.Close();
            }
            using (var dialog = new UpdateForm(delegate { throw new IOException("fixture install failure"); }, new UpdateInfo { Version = "99.0.0", Revision = new string('F', 64) }))
            {
                dialog.Show();
                Check(!Field<Label>(dialog, "notes").CanSelect, "Update details still accept focus or text selection");
                dialog.GetType().GetField("prepared", Private).SetValue(dialog, new PreparedUpdate());
                var action = (System.Threading.Tasks.Task)dialog.GetType().GetMethod("ActAsync", Private).Invoke(dialog, null);
                Wait(delegate { return action.IsCompleted; }); action.GetAwaiter().GetResult();
                Check(Field<Label>(dialog, "status").Text.Contains("fixture install failure") && Field<Button>(dialog, "action").Enabled,
                    "Install failure was invisible or left confirmation disabled");
                dialog.Close();
            }
            var stage = Path.Combine(root, "cleanup");
            Directory.CreateDirectory(Path.Combine(stage, "payload")); Directory.CreateDirectory(Path.Combine(stage, "backup"));
            File.WriteAllText(Path.Combine(stage, "ShotInk-portable.zip"), "package");
            File.WriteAllText(Path.Combine(stage, "payload", "ShotInk.exe"), "staged");
            File.WriteAllText(Path.Combine(stage, "backup", "ShotInk.exe"), "backup");
            File.WriteAllText(Path.Combine(stage, "update-error.txt"), "diagnostic");
            UpdateService.CleanupPackage(stage);
            Check(!File.Exists(Path.Combine(stage, "ShotInk-portable.zip")) && !Directory.Exists(Path.Combine(stage, "payload")) &&
                File.Exists(Path.Combine(stage, "backup", "ShotInk.exe")) && File.Exists(Path.Combine(stage, "update-error.txt")), "Cleanup deleted backups/logs or retained install files");
            UpdateService.CleanupPackage(stage);
            Console.WriteLine("PASS: package cleanup removes ZIP/payload and retains backups/logs; details cannot be selected");
            Console.WriteLine("PASS: update confirmation closes windows, cancels pending captures, blocks new captures and recovers after cancellation/failure");
        }

        private static void CompactToolbar()
        {
            using (var desktop = new Bitmap(900, 650))
            using (var g = Graphics.FromImage(desktop))
            {
                g.Clear(Color.SteelBlue);
                using (var overlay = new CaptureOverlay(false, (Bitmap)desktop.Clone(), new Rectangle(0, 0, 900, 650)))
                {
                    overlay.Show(); overlay.SelectRegion(new Rectangle(40, 40, 700, 450));
                    var toolbar = Field<CaptureToolbar>(overlay, "toolbar");
                    var options = Field<AnnotationOptions>(toolbar, "options");
                    foreach (var tool in new[] { DrawTool.Rectangle, DrawTool.Arrow, DrawTool.Pen, DrawTool.Highlight, DrawTool.Text, DrawTool.Mosaic, DrawTool.Eraser })
                    {
                        toolbar.SelectTool(tool); options.PerformLayout();
                        int expectedHeight = tool == DrawTool.Mosaic || tool == DrawTool.Eraser ? SliderAppearance.Height + 6 : 34;
                        Check(options.Height == expectedHeight, "Options did not reserve space for slider values: " + tool);
                        foreach (Control row in options.Controls)
                        {
                            Check(row.Top == options.Padding.Top && row.Right <= options.ClientSize.Width - options.Padding.Right, "Options are wrapped or clipped: " + tool);
                            foreach (Control child in row.Controls)
                                if (child is Button) Check(child.Text.Length == 0, "Option retains visible text");
                        }
                    }
                    toolbar.SelectTool(DrawTool.Pen);
                    toolbar.Location = new Point(70, 180);
                    Application.DoEvents();
                    var old = toolbar.Bounds;
                    toolbar.Location = new Point(90, 520); overlay.Update(); Native.FlushDesktop();
                    using (var actual = Native.Capture(overlay.RectangleToScreen(old)))
                    {
                        for (int y = 4; y < old.Height - 4; y += 7)
                            for (int x = 4; x < old.Width - 4; x += 11)
                                Check(actual.GetPixel(x, y).ToArgb() == Color.SteelBlue.ToArgb(), "Toolbar left stale pixels over canvas");
                    }
                    overlay.Close();
                }
            }
            Console.WriteLine("PASS: single row icon options and toolbar repaint");
        }

        internal static void ToolbarCursors()
        {
            using (var host = new Form { Cursor = ToolCursors.Crosshair })
            using (var canvas = new AnnotationCanvas(new Bitmap(80, 80)))
            using (var toolbar = new CaptureToolbar())
            {
                host.Controls.Add(canvas);
                host.Controls.Add(toolbar);
                toolbar.ToolChanged += delegate(DrawTool tool) { canvas.Tool = tool; };
                foreach (var tool in new[] { DrawTool.Arrow, DrawTool.Text, DrawTool.Mosaic, DrawTool.Eraser, DrawTool.ColorPicker })
                {
                    toolbar.SelectTool(tool);
                    CheckToolbarCursor(toolbar);
                }
                var strip = Field<ToolStrip>(toolbar, "strip");
                var previousCursor = Cursor.Current;
                try
                {
                    var hit = new IntPtr((0x200 << 16) | 1); // WM_MOUSEMOVE and HTCLIENT
                    SendMessage(canvas.Handle, 0x20, canvas.Handle, hit);
                    Check(Cursor.Current == canvas.Cursor, "Canvas did not use the sampling pen.");
                    SendMessage(strip.Handle, 0x20, strip.Handle, hit);
                    Check(Cursor.Current == Cursors.Arrow, "Moving from the canvas to the toolbar kept the sampling pen.");
                    SendMessage(canvas.Handle, 0x20, canvas.Handle, hit);
                    Check(Cursor.Current == canvas.Cursor, "Moving back from the toolbar did not restore the sampling pen.");
                }
                finally { Cursor.Current = previousCursor; }
            }
            using (var editor = new EditorForm(new Bitmap(40, 40), delegate(Bitmap image) { image.Dispose(); }))
                CheckToolbarCursor(Field<Panel>(editor, "toolArea"));
            Console.WriteLine("PASS: toolbar arrow and canvas cursor switching in native mouse messages");
        }

        private static void CheckToolbarCursor(Control control)
        {
            Check(control.Cursor == Cursors.Arrow, "The toolbar exposed a non-arrow cursor: " + control.Name);
            foreach (Control child in control.Controls) CheckToolbarCursor(child);
        }

        private static void KeyboardSelection()
        {
            using (var image = new Bitmap(900, 650))
            using (var overlay = new CaptureOverlay(false, (Bitmap)image.Clone(), new Rectangle(0, 0, 900, 650)))
            {
                overlay.Show(); overlay.SelectRegion(new Rectangle(40, 40, 700, 450));
                Field<AnnotationCanvas>(overlay, "canvas").Focus();
                Call(overlay, "OnKeyDown", new KeyEventArgs(Keys.Right));
                Check(Field<Rectangle>(overlay, "selection") == new Rectangle(41, 40, 700, 450), "Arrow failed to move selection by one pixel");
                for (int i = 0; i < 4; i++)
                {
                    int handle = i * 2 + 1;
                    var before = Field<Rectangle>(overlay, "selection");
                    var point = SelectionGeometry.Handles(before)[handle];
                    Call(overlay, "BeginAdjust", handle, point);
                    typeof(Timer).GetMethod("OnTick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Field<Timer>(overlay, "edgeHold"), new object[] { EventArgs.Empty });
                    Check(Field<int>(overlay, "keyboardEdge") == handle, "Long hold did not select the edge");
                    var key = i == 0 ? Keys.Up : i == 1 ? Keys.Right : i == 2 ? Keys.Down : Keys.Left;
                    for (int n = 0; n < 5; n++) Call(overlay, "OnKeyDown", new KeyEventArgs(key));
                    var expanded = Field<Rectangle>(overlay, "selection");
                    Check(expanded == SelectionGeometry.Resize(before, handle, new Point(i == 1 ? 5 : i == 3 ? -5 : 0, i == 0 ? -5 : i == 2 ? 5 : 0), overlay.ClientRectangle), "Repeated keys changed the wrong edge");
                    var opposite = i == 0 ? Keys.Down : i == 1 ? Keys.Left : i == 2 ? Keys.Up : Keys.Right;
                    Call(overlay, "OnKeyDown", new KeyEventArgs(opposite));
                    var shrunk = Field<Rectangle>(overlay, "selection");
                    Check((i % 2 == 0 ? shrunk.Height : shrunk.Width) == (i % 2 == 0 ? expanded.Height : expanded.Width) - 1, "Opposite arrow did not shrink edge");
                    Mouse(overlay, "OnMouseUp", point.X, point.Y);
                    Check(Field<int>(overlay, "keyboardEdge") == -1 && Field<Rectangle>(overlay, "selection") == shrunk, "Mouse release retained edge mode or changed bounds");
                    Call(overlay, "OnKeyDown", new KeyEventArgs(Keys.Right));
                    Check(Field<Rectangle>(overlay, "selection") == SelectionGeometry.Move(shrunk, new Point(1, 0), overlay.ClientRectangle), "Release did not restore whole-selection movement");
                }
                overlay.SelectRegion(new Rectangle(0, 0, 8, 8)); Field<AnnotationCanvas>(overlay, "canvas").Focus();
                Call(overlay, "OnKeyDown", new KeyEventArgs(Keys.Up));
                Check(Field<Rectangle>(overlay, "selection") == new Rectangle(0, 0, 8, 8), "Keyboard movement escaped desktop");
                var toolbar = Field<CaptureToolbar>(overlay, "toolbar"); toolbar.SelectTool(DrawTool.Text);
                toolbar.Controls.Find("text-font-size", true)[0].Focus();
                Call(overlay, "OnKeyDown", new KeyEventArgs(Keys.Right));
                Check(Field<Rectangle>(overlay, "selection") == new Rectangle(0, 0, 8, 8), "Numeric input arrows moved capture selection");
                overlay.Close();
            }
            Console.WriteLine("PASS: pixel nudging, held edge expansion/shrinking/repeat, release reset and keyboard input isolation");
        }

        internal static void EdgeResizeTargets()
        {
            var region = new Rectangle(100, 100, 500, 300);
            var handles = SelectionGeometry.Handles(region);
            var inward = new[] { new Point(0, 1), new Point(-1, 0), new Point(0, -1), new Point(1, 0) };
            for (int index = 0; index < 8; index++)
            {
                Check(SelectionGeometry.ResizeHandleAt(region, handles[index], 6, 1) == index, "Resize handle lost priority.");
                if (index % 2 == 0) continue;
                var direction = inward[(index - 1) / 2];
                foreach (int offset in new[] { -7, -6, -5, 1, 2, 6 })
                {
                    var point = handles[index];
                    point.Offset(direction.X * offset, direction.Y * offset);
                    int expected = offset >= -6 && offset <= 1 ? index : -1;
                    Check(SelectionGeometry.ResizeHandleAt(region, point, 6, 1) == expected, "Unexpected hit inside or outside the resize band.");
                }
            }
            Check(SelectionGeometry.ResizeHandleAt(region, new Point(350, 250), 6, 1) == -1 &&
                SelectionGeometry.ResizeHandleAt(region, new Point(103, 103), 6, 1) == -1, "Resize band swallowed the center or the space inside a corner.");
            Check(SelectionGeometry.ResizeHandleAt(region, new Point(350, 88), 12, 2) == 1 &&
                SelectionGeometry.ResizeHandleAt(region, new Point(350, 102), 12, 2) == 1 &&
                SelectionGeometry.ResizeHandleAt(region, new Point(94, 94), 6, 1) == 0, "Scaled edge tolerance or expanded corner priority was lost.");
            using (var overlay = new CaptureOverlay(false, new Bitmap(900, 650), new Rectangle(-300, 20, 900, 650)))
            {
                overlay.Show();
                int innerRadius = Math.Max(1, (int)Math.Round(overlay.DeviceDpi / 96.0));
                foreach (int handle in new[] { 1, 3, 5, 7 })
                foreach (int offset in new[] { -5, innerRadius })
                {
                    overlay.SelectRegion(new Rectangle(region.X - 300, region.Y + 20, region.Width, region.Height));
                    var point = handles[handle];
                    var direction = inward[(handle - 1) / 2];
                    point.Offset(direction.X * offset, direction.Y * offset);
                    var target = region.Contains(point) ? (Control)Field<AnnotationCanvas>(overlay, "canvas") : overlay;
                    var local = target.PointToClient(overlay.PointToScreen(point));
                    var hover = Message.Create(target.Handle, 0x200, IntPtr.Zero, PackedPoint(local));
                    Check(overlay.PreFilterMessage(ref hover) && Cursor.Current.Handle == (handle == 1 || handle == 5 ? Cursors.SizeNS : Cursors.SizeWE).Handle,
                        "Hovering near an edge did not show its resize cursor.");
                    Cursor.Current = target.Cursor;
                    var message = Message.Create(target.Handle, 0x201, new IntPtr(1), PackedPoint(local));
                    Check(overlay.PreFilterMessage(ref message) && Field<int>(overlay, "dragMode") == handle, "Native edge click moved the whole selection or missed the canvas edge.");
                    var end = new Point(point.X + (handle == 3 || handle == 7 ? 10 : 0), point.Y + (handle == 1 || handle == 5 ? 10 : 0));
                    Mouse(overlay, "OnMouseMove", end.X, end.Y); Mouse(overlay, "OnMouseUp", end.X, end.Y);
                    Check(Field<Rectangle>(overlay, "selection") == SelectionGeometry.Resize(region, handle, new Point(end.X - point.X, end.Y - point.Y), overlay.ClientRectangle), "Edge dragging resized the wrong side.");
                }
                var toolbar = Field<CaptureToolbar>(overlay, "toolbar");
                overlay.SelectRegion(new Rectangle(region.X - 300, region.Y + 20, region.Width, region.Height));
                toolbar.SelectTool(DrawTool.Pen);
                var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                var grip = (Control)Field<Array>(overlay, "grips").GetValue(0);
                var insideCorner = new Point(region.Left + innerRadius + 1, region.Top + innerRadius + 1);
                var hitTest = new object[] { Message.Create(grip.Handle, 0x84, IntPtr.Zero, PackedPoint(overlay.PointToScreen(insideCorner))) };
                Call(grip, "WndProc", hitTest);
                Check(((Message)hitTest[0]).Result.ToInt64() == -1, "The visible corner grip blocked drawing outside the inner resize band.");
                var localCorner = new Point(insideCorner.X - region.Left, insideCorner.Y - region.Top);
                var drawing = Message.Create(canvas.Handle, 0x201, new IntPtr(1), PackedPoint(localCorner));
                Check(!overlay.PreFilterMessage(ref drawing), "Clicking just inside the corner was intercepted.");
                Mouse(canvas, "OnMouseDown", localCorner.X, localCorner.Y);
                var crossing = Message.Create(canvas.Handle, 0x200, new IntPtr(1), PackedPoint(new Point(0, 0)));
                Check(!overlay.PreFilterMessage(ref crossing), "Moving an active stroke over the border was intercepted.");
                Mouse(canvas, "OnMouseMove", 0, 0); Mouse(canvas, "OnMouseUp", 0, 0);
                Check(canvas.Marks.Count == 1 && Field<Rectangle>(overlay, "selection") == region, "Drawing near the corner resized the selection.");
                var cursorMessage = Message.Create(overlay.Handle, 0x20, toolbar.Handle, new IntPtr(1));
                Check(!overlay.PreFilterMessage(ref cursorMessage), "Parent cursor forwarding intercepted toolbar controls.");
                overlay.Close();
            }
            Console.WriteLine("PASS: outer resize band, narrow inner band, corner click-through and uninterrupted drawing at the edge");
        }

        private static void FreeSelection()
        {
            using (var source = new Bitmap(600, 400))
            {
                using (var g = Graphics.FromImage(source)) g.Clear(Color.Orange);
                var settings = new AppSettings { LastCaptureRegion = new Rectangle(10, 20, 80, 60) };
                using (var overlay = new CaptureOverlay(false, (Bitmap)source.Clone(), new Rectangle(-300, -100, 600, 400), settings))
                {
                    overlay.Show();
                    ((Button)overlay.Controls.Find("selection-free", true)[0]).PerformClick();
                    Mouse(overlay, "OnMouseDown", 80, 60); Mouse(overlay, "OnMouseMove", 84, 62);
                    Check(!Field<bool>(overlay, "ready"), "Free selection snapped immediately after starting");
                    Mouse(overlay, "OnMouseMove", 180, 60); Mouse(overlay, "OnMouseMove", 260, 60); Mouse(overlay, "OnMouseMove", 260, 220);
                    Mouse(overlay, "OnMouseMove", 84, 63);
                    Check(Field<bool>(overlay, "ready") && !Field<bool>(overlay, "dragging"), "Returning near the start did not finish before mouse release");
                    Mouse(overlay, "OnMouseUp", 84, 63);
                    var bounds = Field<Rectangle>(overlay, "selection");
                    Check(bounds == new Rectangle(80, 60, 180, 160), "Free selection bounds lost negative-screen coordinates");
                    Check(settings.LastCaptureRegion == new Rectangle(10, 20, 80, 60), "Free selection replaced the remembered rectangular quick capture");
                    var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                    var toolbar = Field<CaptureToolbar>(overlay, "toolbar");
                    Check(!Field<ToolStripButton>(toolbar, "gif").Enabled && !Field<ToolStripButton>(toolbar, "scroll").Enabled, "Nonrectangular capture allowed unmasked recording");
                    var mark = new Mark { Tool = DrawTool.Pen, Color = Color.Red, Width = 12 };
                    mark.Points.Add(new PointF(5, 140)); mark.Points.Add(new PointF(175, 140)); canvas.AddMark(mark);
                    using (var image = canvas.Render())
                    {
                        Check(image.GetPixel(5, 150).ToArgb() == 0, "Outside pixels or RGB data leaked through the free-selection mask");
                        Check(image.GetPixel(150, 30).A == 255, "Interior pixels lost opacity");
                        bool soft = false;
                        for (int y = 1; y < image.Height - 1 && !soft; y++) for (int x = 1; x < image.Width - 1; x++)
                            if (image.GetPixel(x, y).A > 0 && image.GetPixel(x, y).A < 255) { soft = true; break; }
                        Check(soft, "Free selection edges lost antialiasing");
                        using (var bytes = new MemoryStream())
                        { image.Save(bytes, ImageFormat.Png); bytes.Position = 0; using (var saved = new Bitmap(bytes)) using (var editor = new AnnotationCanvas(new Bitmap(saved)))
                            using (var exported = editor.Render()) Check(exported.GetPixel(5, 150).A == 0, "PNG reopened in the editor lost transparency"); }
                    }
                    canvas.Undo(); canvas.Redo();
                    Call(overlay, "OnKeyDown", new KeyEventArgs(Keys.Right));
                    Check(Field<Rectangle>(overlay, "selection").X == 81, "Custom selection could not be nudged");
                    bounds = Field<Rectangle>(overlay, "selection"); var edge = SelectionGeometry.Handles(bounds)[3];
                    Call(overlay, "BeginAdjust", 3, edge); Mouse(overlay, "OnMouseUp", edge.X + 20, edge.Y);
                    Check(canvas.Image.Width == 200 && canvas.Region != null, "Custom mask did not resize with its bounding frame");
                    using (var image = canvas.Render()) Check(image.GetPixel(5, 150).A == 0 && image.GetPixel(170, 30).A == 255, "Resizing changed the custom mask");
                    using (var preview = new Bitmap(overlay.Width, overlay.Height))
                    { overlay.DrawToBitmap(preview, overlay.ClientRectangle); preview.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-free-selection-options.png")); }
                    Bitmap output = null; overlay.Completed += delegate(Bitmap image, CaptureAction action) { output = image; };
                    Call(overlay, "OnKeyDown", new KeyEventArgs(Keys.Enter));
                    using (output) Check(output != null && output.GetPixel(5, 150).A == 0, "Completing capture discarded the shape mask");
                }
                using (var overlay = new CaptureOverlay(false, (Bitmap)source.Clone(), new Rectangle(0, 0, 600, 400)))
                {
                    overlay.Show(); Call(overlay, "SetSelectionMode", true);
                    Mouse(overlay, "OnMouseDown", 50, 50); Mouse(overlay, "OnMouseMove", 180, 50); Mouse(overlay, "OnMouseMove", 180, 180); Mouse(overlay, "OnMouseUp", 60, 150);
                    Check(Field<bool>(overlay, "ready"), "An open contour was not closed on release");
                    Call(overlay, "SetSelectionMode", true);
                    Check(!Field<bool>(overlay, "ready") && overlay.Controls.Find("ocr-search", true).Length == 0, "Starting another contour left stale editor controls");
                    Mouse(overlay, "OnMouseDown", 50, 50); Mouse(overlay, "OnMouseUp", 55, 52);
                    Check(!Field<bool>(overlay, "ready") && !Field<bool>(overlay, "dragging"), "A tiny stroke became a selected region");
                    Call(overlay, "OnKeyDown", new KeyEventArgs(Keys.L));
                    Mouse(overlay, "OnMouseDown", 50, 50); Mouse(overlay, "OnMouseMove", 180, 180); Mouse(overlay, "OnMouseUp", 180, 180);
                    Check(Field<bool>(overlay, "ready") && Field<PointF[]>(overlay, "selectionOutline") == null, "Rectangle mode did not recover after custom selection");
                    overlay.Close();
                }
            }
            Console.WriteLine("PASS: freehand selection snaps/closes, rejects tiny strokes, preserves transparent masks through annotation/resize/export and restores rectangle mode");
        }

        private static void SelectionModePersistence(string root)
        {
            var path = Path.Combine(root, "selection-mode.ini");
            var settings = new AppSettings(); int changes = 0;
            settings.Changed += delegate { changes++; };
            using (var overlay = new CaptureOverlay(false, new Bitmap(600, 400), new Rectangle(0, 0, 600, 400), settings))
            {
                overlay.Show();
                ((Button)overlay.Controls.Find("selection-free", true)[0]).PerformClick();
                Check(settings.FreeSelectionMode && changes == 1, "Selection mode did not notify settings persistence");
                overlay.Close();
            }
            settings.Save(path); var restored = AppSettings.Load(path).Clone();
            Check(restored.FreeSelectionMode, "Selection mode was lost after restart or settings clone");
            using (var overlay = new CaptureOverlay(false, new Bitmap(600, 400), new Rectangle(0, 0, 600, 400), restored))
            {
                overlay.Show(); overlay.EnableWindowSelection(new[] { new Rectangle(0, 0, 600, 400) });
                Check(Field<bool>(overlay, "freeSelectionMode") && Field<Rectangle>(overlay, "selection").IsEmpty
                    && ((ToolOptionButton)overlay.Controls.Find("selection-free", true)[0]).Selected, "Saved free mode did not initialize the upper selector or suppressed window selection incorrectly");
                Mouse(overlay, "OnMouseDown", 40, 40); Mouse(overlay, "OnMouseMove", 200, 40); Mouse(overlay, "OnMouseMove", 200, 200); Mouse(overlay, "OnMouseUp", 50, 180);
                var toolbar = Field<CaptureToolbar>(overlay, "toolbar");
                Check(!Field<ToolStrip>(toolbar, "strip").Items.Cast<ToolStripItem>().Any(item => item.Name == "free-selection"), "Editing toolbar still contains a free-selection entry");
                Call(overlay, "SetSelectionMode", false); overlay.Close();
            }
            restored.Save(path); Check(!AppSettings.Load(path).FreeSelectionMode, "Rectangle mode did not replace the remembered option");
            restored.FreeSelectionMode = true;
            using (var scroll = new CaptureOverlay(true, new Bitmap(600, 400), new Rectangle(0, 0, 600, 400), restored))
                Check(!Field<bool>(scroll, "freeSelectionMode") && restored.FreeSelectionMode, "Long capture reset the normal capture preference");
            Console.WriteLine("PASS: upper-only selection controls remember the last option across restart, settings clone and long capture");
        }

        internal static void CropScreenshot()
        {
            var screen = new Rectangle(SystemInformation.VirtualScreen.Location, new Size(480, 360));
            using (var overlay = new CaptureOverlay(false, new Bitmap(screen.Width, screen.Height), screen))
            {
                overlay.Show();
                var selection = new Rectangle(screen.Left + 60, screen.Top + 50, 180, 120);
                overlay.SelectRegion(selection);
                var toolbar = Field<CaptureToolbar>(overlay, "toolbar");
                var strip = Field<ToolStrip>(toolbar, "strip");
                var crop = (ToolStripButton)strip.Items["crop-image"];
                Check(crop != null && !crop.Available && !crop.Visible, "Regular screenshot exposed crop");
                var ordinaryCanvas = Field<AnnotationCanvas>(overlay, "canvas");
                crop.PerformClick(); ordinaryCanvas.BeginCrop();
                Check(!ordinaryCanvas.IsCropping, "Regular screenshot entered crop mode");
                int ordinaryToolbarWidth = toolbar.Width;
                var image = new Bitmap(180, 250);
                for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++)
                    image.SetPixel(x, y, Color.FromArgb(x, y, 128));
                overlay.ShowLongPreview(image);
                var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                var viewport = Field<Panel>(overlay, "longViewport");
                Check(crop.Visible && toolbar.Width == ordinaryToolbarWidth + crop.Width
                    && strip.Items[strip.Items.IndexOf(crop) - 1].ToolTipText.StartsWith("橡皮擦"), "Long screenshot did not show crop after the eraser");
                canvas.AddMark(new Mark { Tool = DrawTool.Pen, Width = 5, Color = Color.Red,
                    Points = { new PointF(30, 42), new PointF(130, 42) } });
                var previousSkin = Skins.Current.Kind;
                try
                {
                    foreach (var skin in Skins.All)
                    {
                        Skins.Select(skin.Kind);
                        var glyph = (Bitmap)crop.Image;
                        Check(glyph.Size == new Size(20, 20) && glyph.GetPixel(5, 10).A > 0 && glyph.GetPixel(0, 0).A == 0, "Crop glyph missing for " + skin.Kind);
                    }
                }
                finally { Skins.Select(previousSkin); }
                using (var original = canvas.Render())
                {
                    canvas.ScrollDocumentHorizontal(8);
                    canvas.ScrollDocument(76);
                    var visibleSize = Field<Size>(canvas, "documentClient");
                    var midpoint = new Point(visibleSize.Width / 2, visibleSize.Height / 2);
                    int offsetX = (int)canvas.DocumentOffsetX, offsetY = (int)canvas.DocumentOffsetY;
                    crop.PerformClick();
                    Mouse(canvas, "OnMouseDown", midpoint.X, 0); Mouse(canvas, "OnMouseUp", midpoint.X, 0);
                    Check(Field<Rectangle>(canvas, "cropPreview") == canvas.ImageBounds, "Clicking a clipped crop edge moved it");
                    Mouse(canvas, "OnMouseDown", 0, midpoint.Y); Mouse(canvas, "OnMouseUp", 15, midpoint.Y);
                    Mouse(canvas, "OnMouseDown", midpoint.X, 0); Mouse(canvas, "OnMouseUp", midpoint.X, 12);
                    Mouse(canvas, "OnMouseDown", visibleSize.Width - 1, midpoint.Y);
                    Mouse(canvas, "OnMouseUp", visibleSize.Width - 21, midpoint.Y);
                    Mouse(canvas, "OnMouseDown", midpoint.X, visibleSize.Height - 1);
                    Mouse(canvas, "OnMouseUp", midpoint.X, visibleSize.Height - 17);
                    var visibleCrop = Rectangle.FromLTRB(offsetX + 15, offsetY + 12, offsetX + visibleSize.Width - 20, offsetY + visibleSize.Height - 16);
                    Check(Field<Rectangle>(canvas, "cropPreview") == visibleCrop, "Could not drag all four clipped crop edges from the middle of a scrolled image");
                    ((Button)toolbar.Controls.Find("cancel-crop", true).Single()).PerformClick();

                    Call(canvas, "NavigateDocument", new MouseEventArgs(MouseButtons.None, 0, midpoint.X, midpoint.Y, 120), Keys.Control);
                    Check(canvas.Zoom == 1.25F && Math.Abs(canvas.DocumentOffsetX + midpoint.X / canvas.Zoom - (offsetX + midpoint.X)) <= 1F
                        && Math.Abs(canvas.DocumentOffsetY + midpoint.Y / canvas.Zoom - (offsetY + midpoint.Y)) <= 1F, "Long preview zoom lost the document point under the cursor");
                    for (int step = 0; step < 5; step++)
                        Call(canvas, "NavigateDocument", new MouseEventArgs(MouseButtons.None, 0, midpoint.X, midpoint.Y, -120), Keys.Control);
                    Check(canvas.Zoom < 1F && viewport.Size == canvas.Size && canvas.Width < selection.Width && canvas.Height < selection.Height,
                        "Zoom out left a full-sized viewport around the preview");
                    using (var zoomed = canvas.Render())
                    {
                        Check(zoomed.Size == original.Size, "Preview zoom resized the exported image");
                        Check(zoomed.GetPixel(90, 200) == original.GetPixel(90, 200), "Preview zoom resampled the exported image");
                    }
                    canvas.SetZoom(1);
                    canvas.ScrollDocumentHorizontal(-image.Width);
                    canvas.ScrollDocument(-image.Height);
                    crop.PerformClick();
                    Check(canvas.IsCropping && !strip.Visible, "Crop action did not open crop mode");
                    Mouse(canvas, "OnMouseDown", 0, 60); Mouse(canvas, "OnMouseUp", 15, 60);
                    Mouse(canvas, "OnMouseDown", 89, 0); Mouse(canvas, "OnMouseUp", 89, 12);
                    canvas.ScrollDocumentHorizontal(image.Width);
                    int right = image.Width - 1 - (int)canvas.DocumentOffsetX;
                    Mouse(canvas, "OnMouseDown", right, 65); Mouse(canvas, "OnMouseUp", right - 20, 65);
                    canvas.ScrollDocumentHorizontal(-image.Width);
                    canvas.ScrollDocument(image.Height);
                    int bottom = image.Height - 1 - (int)canvas.DocumentOffsetY;
                    Mouse(canvas, "OnMouseDown", 87, bottom);
                    canvas.ScrollDocument(-107);
                    Mouse(canvas, "OnMouseUp", 87, bottom - 39);
                    Check(canvas.ImageBounds.Size == original.Size, "Cropping changed the image before confirmation");
                    ((Button)toolbar.Controls.Find("finish-crop", true).Single()).PerformClick();
                    var expected = new Rectangle(15, 12, 145, 92);
                    Check(!canvas.IsCropping && strip.Visible && canvas.ImageBounds == expected, "Four-edge crop produced wrong bounds");
                    Check(canvas.Size == expected.Size && viewport.Size == expected.Size
                        && Field<Rectangle>(overlay, "selection").Size == expected.Size, "Preview left empty space around the crop");
                    Check(canvas.DocumentOffsetX == 0 && canvas.DocumentOffsetY == 0, "Fitted crop retained a scroll offset");
                    using (var preview = new Bitmap(canvas.Width, canvas.Height))
                    using (var cropped = canvas.Render())
                    {
                        canvas.DrawToBitmap(preview, canvas.ClientRectangle);
                        Check(cropped.Size == expected.Size, "Crop did not change export size");
                        for (int y = 0; y < cropped.Height; y++) for (int x = 0; x < cropped.Width; x++)
                        {
                            Check(cropped.GetPixel(x, y) == original.GetPixel(x + expected.Left, y + expected.Top), "Crop altered pixels or annotation position");
                            Check(preview.GetPixel(x, y) == cropped.GetPixel(x, y), "Preview contains pixels outside the crop or is missing retained pixels");
                        }
                    }
                    canvas.Undo();
                    Check(canvas.ImageBounds.Size == original.Size && viewport.Size == selection.Size, "Undo did not restore the original long screenshot and frame");
                    canvas.Redo();
                    Check(canvas.ImageBounds == expected && viewport.Size == expected.Size, "Redo did not restore crop and frame bounds");
                    crop.PerformClick();
                    Mouse(canvas, "OnMouseDown", 0, 45); Mouse(canvas, "OnMouseUp", 25, 45);
                    ((Button)toolbar.Controls.Find("cancel-crop", true).Single()).PerformClick();
                    Check(!canvas.IsCropping && canvas.ImageBounds == expected, "Cancellation applied a preview crop");
                    overlay.Completed += delegate(Bitmap exported, CaptureAction action)
                    { using (exported) Check(exported.Size == expected.Size && action == CaptureAction.Copy, "Copy ignored confirmed crop"); };
                    Call(overlay, "Finish", CaptureAction.Copy);
                    Check(overlay.IsDisposed, "Copy did not complete the cropped screenshot");
                }
            }
            using (var editor = new EditorForm(new Bitmap(160, 120), image => image.Dispose()))
                Check(editor.Controls.Find("crop-image", true).Length == 0, "History image editor exposed crop");
            foreach (bool video in new[] { false, true })
                using (var toolbar = new CaptureToolbar())
                {
                    toolbar.ConfigureForGif(video);
                    Check(Field<ToolStrip>(toolbar, "strip").Items["crop-image"] == null, "Recording editor exposed crop");
                }
            Console.WriteLine("PASS: long screenshot crop edges remain draggable while scrolled, preview zoom preserves source pixels, and undo/redo");
        }

        private static void ManualSaveLocation(string root)
        {
            string configured = Path.Combine(root, "默认保存");
            string chosen = Path.Combine(root, "手动保存=图片");
            string profile = Path.Combine(root, "save-location.ini");
            Directory.CreateDirectory(chosen);
            File.WriteAllText(profile, "save-directory=" + configured);
            var settings = AppSettings.Load(profile);
            Check(settings.ManualSaveDirectory == configured, "First manual save did not use the configured directory");

            int changes = 0;
            settings.Changed += delegate { changes++; };
            settings.RememberManualSave(Path.Combine(chosen, "截图.png"));
            settings.RememberManualSave(Path.Combine(chosen, "录屏.mp4"));
            Check(changes == 1 && settings.ManualSaveDirectory == chosen, "Manual save did not record the selected directory once");
            settings.Save(profile);

            var restored = AppSettings.Load(profile).Clone();
            Check(restored.ManualSaveDirectory == chosen, "Manual save directory did not survive restart or settings cloning");
            restored.AskSaveLocation = false;
            using (var image = new Bitmap(20, 20))
            {
                string path;
                Check(ImageExport.Save(null, image, restored, out path) && Path.GetDirectoryName(path) == configured,
                    "Direct save did not use the configured directory");
            }
            Check(restored.ManualSaveDirectory == chosen, "Direct save replaced the last manual directory");
            Directory.Delete(chosen);
            Check(restored.ManualSaveDirectory == configured, "Unavailable manual save directory did not fall back to configuration");
            Console.WriteLine("PASS: manual save directory preference, restart persistence, and fallback");
        }

        internal static void SmallPinSize()
        {
            foreach (var size in new[] { new Size(16, 16), new Size(24, 55), new Size(55, 18) })
            {
                using (var pin = new PinForm(new Bitmap(size.Width, size.Height)))
                {
                    pin.Show();
                    Application.DoEvents();
                    Check(pin.ClientSize == size, "Showing a small pin imposed a minimum window size and stretched the image: " + pin.ClientSize);
                    Call(pin, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 0, 0, 120));
                    var scaled = new Size((int)Math.Round(size.Width * 1.1F), (int)Math.Round(size.Height * 1.1F));
                    Check(pin.ClientSize == scaled, "A small pin did not zoom proportionally");
                    pin.ContextMenuStrip.Items[1].PerformClick();
                    Check(pin.ClientSize == size, "Resetting the zoom stretched a small pin");
                }
            }
            Console.WriteLine("PASS: small desktop pins keep the image dimensions on display, zoom and reset");
        }

        private static void CopyColorAt(AnnotationCanvas canvas, int x, int y, bool append = false)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (append) Call(canvas, "PreviewColorAt", new Point(x, y), true, true);
                else Mouse(canvas, "OnMouseDown", x, y);
                var preview = Field<ColorPickerPreview>(canvas, "colorPreview");
                if (preview != null && preview.AccessibleDescription == (append ? "已追加" : "已复制")) return;
                Check(preview != null && preview.AccessibleDescription == "复制失败", "Sampling click did not attempt to copy.");
                // A clipboard manager can momentarily hold the desktop clipboard.
                Application.DoEvents();
                System.Threading.Thread.Sleep(50);
            }
            throw new Exception("The desktop clipboard stayed busy during the color picker test.");
        }

        internal static void ColorPicker(string previewPath = null)
        {
            var previousClipboard = Clipboard.GetDataObject();
            var screen = Screen.PrimaryScreen.Bounds;
            var bounds = new Rectangle(screen.X, screen.Y, 640, 480);
            var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(31, 105, 150));
                g.FillRectangle(Brushes.Coral, 88, 88, 22, 22);
                g.FillRectangle(Brushes.MediumSeaGreen, 102, 100, 20, 20);
            }
            bitmap.SetPixel(100, 100, Color.FromArgb(18, 52, 86));
            try
            {
                using (var overlay = new CaptureOverlay(false, bitmap, bounds))
                {
                    overlay.Show(); overlay.SelectRegion(new Rectangle(bounds.X + 40, bounds.Y + 40, 300, 250));
                    var toolbar = Field<CaptureToolbar>(overlay, "toolbar");
                    var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                    var strip = Field<ToolStrip>(toolbar, "strip");
                    var picker = (ToolStripButton)strip.Items["color-picker"];
                    picker.PerformClick();
                    Check(picker.Checked && toolbar.Height == 30 && canvas.Tool == DrawTool.ColorPicker, "Color picker toolbar entry did not enter sampling mode.");
                    Check(canvas.Cursor.Handle != Cursors.Cross.Handle && canvas.Cursor.HotSpot == new Point(5, 27),
                        "Color picker did not align the sampling pen tip with the cursor hotspot.");
                    Check(!((Control)Field<Array>(overlay, "grips").GetValue(0)).Visible, "Resize grip blocked the source pixel at the corner.");
                    Mouse(canvas, "OnMouseMove", 60, 60);
                    var preview = Field<ColorPickerPreview>(canvas, "colorPreview");
                    var firstCursor = canvas.Cursor;
                    int radius = ColorPickerPreview.Radius;
                    Check(preview.Visible && preview.Owner == overlay && preview.Pixels.GetPixel(radius, radius).ToArgb() == Color.FromArgb(18, 52, 86).ToArgb() &&
                        preview.Pixels.GetPixel(radius - 1, radius).ToArgb() == Color.Coral.ToArgb() &&
                        preview.Pixels.GetPixel(radius + 2, radius).ToArgb() == Color.MediumSeaGreen.ToArgb(),
                        "Magnifier center or neighboring pixels did not match the screenshot.");
                    CopyColorAt(canvas, 60, 60);
                    Check(Clipboard.GetText() == "#123456" && picker.Checked && preview.Visible, "Left click did not copy HEX or keep sampling active.");
                    CopyColorAt(canvas, 61, 60, true);
                    Check(!ReferenceEquals(canvas.Cursor, firstCursor) && canvas.Cursor.HotSpot == new Point(5, 27),
                        "Sampling a new color did not refresh the pen or moved its tip.");
                    var coralCursor = canvas.Cursor;
                    Check(CursorHasColor(coralCursor, Color.Coral), "The sampling pen did not display the sampled coral pixel.");
                    Mouse(canvas, "OnMouseMove", 61, 60);
                    Check(ReferenceEquals(canvas.Cursor, coralCursor), "Hovering over the same color rebuilt the pen.");
                    CopyColorAt(canvas, 62, 60, true);
                    Check(CursorHasColor(canvas.Cursor, Color.MediumSeaGreen), "The visible sampling pen did not change to the sampled green.");
                    Check(Clipboard.GetText() == string.Join(Environment.NewLine, "#123456", "#FF7F50", "#3CB371"),
                        "Ctrl+click did not append each color on its own line in click order.");
                    using (var image = preview.RenderPreview())
                    {
                        Check(image.GetPixel(0, 0).A == 0 && image.GetPixel(2, image.Height / 3).A == 0 &&
                            image.GetPixel(image.Width / 2, image.Height * 114 / 150).A == 0 &&
                            image.GetPixel(image.Width / 2, image.Height / 3).A == 255 &&
                            image.GetPixel(image.Width / 2, image.Height * 86 / 100).A == 255,
                            "Lens and color capsule did not render as a transparent circular overlay.");
                        if (previewPath != null) image.Save(previewPath, ImageFormat.Png);
                    }
                    CopyColorAt(canvas, 60, 60);
                    Check(Clipboard.GetText() == "#123456", "A plain left click did not reset the current color list.");
                    Mouse(canvas, "OnMouseMove", 0, 0);
                    Check(preview.Pixels.GetPixel(radius, radius).ToArgb() == Color.FromArgb(31, 105, 150).ToArgb() &&
                        preview.Pixels.GetPixel(radius - 1, radius).A == 0, "Magnifier sampled beyond the image or shifted the center at the corner.");
                    var edge = Message.Create(canvas.Handle, 0x201, new IntPtr(1), PackedPoint(Point.Empty));
                    Check(!overlay.PreFilterMessage(ref edge), "Selection resize intercepted a color picker click.");
                    Call(canvas, "OnMouseDown", new MouseEventArgs(MouseButtons.Right, 1, 0, 0, 0));
                    Check(overlay.Visible && !preview.Visible && !picker.Checked && canvas.Tool == DrawTool.None && canvas.Cursor == ToolCursors.For(DrawTool.None) && !canvas.CanUndo,
                        "Right click closed the capture, left the picker active or added an annotation.");
                    picker.PerformClick();
                    CopyColorAt(canvas, 61, 60, true);
                    Check(Clipboard.GetText() == "#FF7F50", "A new color picker session appended colors from the previous session.");
                    canvas.CancelColorPicker();

                    var document = new Bitmap(600, 500);
                    document.SetPixel(61, 54, Color.Lime);
                    overlay.ShowLongPreview(document);
                    canvas = Field<AnnotationCanvas>(overlay, "canvas");
                    canvas.ScrollDocumentHorizontal(28);
                    canvas.ScrollDocument(20);
                    canvas.BeginCrop();
                    canvas.GetType().GetField("cropPreview", Private).SetValue(canvas, new Rectangle(20, 15, 560, 450));
                    canvas.EndCrop(true);
                    canvas.SetZoom(2);
                    picker.PerformClick();
                    CopyColorAt(canvas, 66, 68);
                    Check(Field<ColorPickerPreview>(canvas, "colorPreview").AccessibleName == "#00FF00",
                        "The preview did not sample the correct pixel after scrolling, cropping and zooming.");
                    Check(Clipboard.GetText() == "#00FF00", "Zoomed, scrolled, cropped long image copied the wrong source pixel.");
                }
            }
            finally { if (previousClipboard != null) Clipboard.SetDataObject(previousClipboard, true, 10, 100); else Clipboard.Clear(); }
            Console.WriteLine("PASS: color picker preview, corner sampling, single and multiline clipboard, right-click exit and long-image coordinates");
        }

        internal static void AnnotationCursors()
        {
            var previous = Skins.Current.Kind;
            try
            {
                using (var canvas = new AnnotationCanvas(new Bitmap(80, 80)))
                {
                    var tools = new[] { DrawTool.Rectangle, DrawTool.Ellipse, DrawTool.Arrow, DrawTool.Line, DrawTool.Polyline,
                        DrawTool.ArrowPolyline, DrawTool.Pen, DrawTool.Highlight, DrawTool.Mosaic, DrawTool.Eraser };
                    var handles = new System.Collections.Generic.HashSet<IntPtr>();
                    foreach (var skin in Skins.All)
                    {
                        canvas.Tool = DrawTool.Rectangle;
                        Skins.Select(skin.Kind);
                        Check(canvas.Cursor == ToolCursors.Crosshair && handles.Add(canvas.Cursor.Handle),
                            "Changing skins did not refresh the active crosshair.");
                        CheckCursorWithoutSideIcon(canvas.Cursor, new Point(9, 9));
                        canvas.MosaicEffect = MosaicEffect.Pixelate;
                        foreach (var tool in tools)
                        {
                            canvas.Tool = tool;
                            Check(canvas.Cursor == ToolCursors.Crosshair, "Drawing tool did not use the plain crosshair: " + tool);
                        }
                        canvas.Tool = DrawTool.Mosaic;
                        canvas.MosaicEffect = MosaicEffect.GaussianBlur;
                        canvas.UpdateToolCursor();
                        Check(canvas.Cursor == ToolCursors.Crosshair, "Gaussian blur did not use the plain crosshair.");
                        canvas.Tool = DrawTool.Text;
                        Check(canvas.Cursor == ToolCursors.For(DrawTool.Text) && handles.Add(canvas.Cursor.Handle),
                            "Text cursor did not use its own themed I-beam.");
                        CheckCursorWithoutSideIcon(canvas.Cursor, new Point(9, 13));
                        using (var pen = ToolCursors.CreateSamplingPen(Color.OrangeRed))
                            Check(pen.View.HotSpot == new Point(5, 27) && CursorHasColor(pen.View, Color.OrangeRed),
                                "The themed pen moved its hotspot or discarded the sample in " + skin.Kind);
                    }
                    canvas.Tool = DrawTool.ColorPicker;
                    var inkSamplingPen = canvas.Cursor;
                    Skins.Select(SkinKind.Glass);
                    Check(!ReferenceEquals(canvas.Cursor, inkSamplingPen) && canvas.Cursor.HotSpot == new Point(5, 27) &&
                        CursorHasColor(canvas.Cursor, Color.FromArgb(91, 127, 242)),
                        "Changing skin did not refresh the active sampling pen while preserving its color.");
                }
            }
            finally { Skins.Select(previous); }
            Console.WriteLine("PASS: badge-free drawing and text cursors, and a color sampling pen in all eight themes");
        }

        internal static void Run()
        {
            CompactToolbar();
            ToolbarCursors();
            AnnotationCursors();
            ColorPicker();
            using (var overlay = new CaptureOverlay(false, new Bitmap(900, 650), new Rectangle(0, 0, 900, 650)))
            {
                overlay.EnableWindowSelection(new Rectangle[0]); overlay.Show(); Application.DoEvents();
                Mouse(overlay, "OnMouseDown", 30, 40); Mouse(overlay, "OnMouseMove", 330, 240); overlay.Update();
                var hint = Field<Label>(overlay, "sizeHint");
                Check(hint.Visible && hint.Text == "300 × 200 px", "Dragging a new selection hides its live pixel dimensions.");
                Mouse(overlay, "OnMouseUp", 330, 240);
                Call(overlay, "BeginAdjust", 4, new Point(330, 240));
                Mouse(overlay, "OnMouseMove", 430, 340); overlay.Update();
                Check(hint.Visible && hint.Text == "400 × 300 px", "Resizing a selection hides or freezes its pixel dimensions.");
                Mouse(overlay, "OnMouseUp", 430, 340);
                overlay.SelectRegion(new Rectangle(0, 0, 899, 649));
                Call(overlay, "BeginAdjust", 0, Point.Empty); Mouse(overlay, "OnMouseMove", 1, 1); overlay.Update();
                Check(hint.Visible && overlay.ClientRectangle.Contains(hint.Bounds) && overlay.Controls.GetChildIndex(hint) == 0,
                    "Screen-edge resizing clips or covers the pixel dimensions.");
                Mouse(overlay, "OnMouseUp", 1, 1); overlay.Close();
            }
            KeyboardSelection();
            EdgeResizeTargets();
            FreeSelection();
            var previousClipboard = Clipboard.GetDataObject();
            var root = Path.Combine(Path.GetTempPath(), "ShotInk-capture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                ManualSaveLocation(root);
                SelectionModePersistence(root);
                var settings = new AppSettings();
                var window = new Rectangle(40, 30, 180, 120);
                var bounds = new Rectangle(-300, 20, 600, 400);
                using (var overlay = new CaptureOverlay(false, new Bitmap(600, 400), bounds, settings))
                {
                    overlay.EnableWindowSelection(new[] { window }); overlay.Show();
                    Mouse(overlay, "OnMouseDown", 50, 50); Mouse(overlay, "OnMouseUp", 50, 50);
                    Check(Field<bool>(overlay, "ready") && Field<Rectangle>(overlay, "selection") == window, "Window click did not enter editing");
                    Check(settings.LastCaptureRegion == new Rectangle(-260, 50, 180, 120), "Window coordinates lost virtual-screen origin");
                    overlay.Close();
                }
                using (var overlay = new CaptureOverlay(false, new Bitmap(600, 400), bounds))
                {
                    overlay.EnableWindowSelection(new[] { window }); overlay.Show();
                    Mouse(overlay, "OnMouseDown", 50, 50);
                    System.Threading.Thread.Sleep(370);
                    Mouse(overlay, "OnMouseUp", 50, 50);
                    Check(!Field<bool>(overlay, "ready"), "Long press without dragging selected the window");
                    Mouse(overlay, "OnMouseDown", 50, 50); Mouse(overlay, "OnMouseMove", 130, 110);
                    typeof(Timer).GetMethod("OnTick", Private).Invoke(Field<Timer>(overlay, "selectionHold"), new object[] { EventArgs.Empty });
                    Check(Field<Rectangle>(overlay, "selection") == new Rectangle(50, 50, 80, 60), "Hold timer erased a manually dragged region");
                    Mouse(overlay, "OnMouseUp", 130, 110);
                    Check(Field<Rectangle>(overlay, "selection") == new Rectangle(50, 50, 80, 60), "Drag selected the detected window instead of custom bounds");
                    overlay.Close();
                }
                var path = Path.Combine(root, "settings.ini"); settings.Save(path);
                var restored = AppSettings.Load(path);
                Check(restored.LastCaptureRegion == settings.LastCaptureRegion && restored.Shortcuts.SequenceEqual(settings.Shortcuts), "New region or shortcuts lost on restart");
                File.WriteAllText(path, "hotkey-capture=" + (int)settings.QuickCopyShortcut);
                restored = AppSettings.Load(path);
                Check(restored.CaptureShortcut == settings.QuickCopyShortcut && restored.QuickCopyShortcut == Keys.None, "Upgrade displaced an existing shortcut");
                File.Delete(path); File.Delete(path + ".bak");
                using (var home = new MainForm(root, false))
                {
                    home.Show(); Application.DoEvents();
                    System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                    var screen = SystemInformation.VirtualScreen;
                    var region = new Rectangle(screen.Left + 20, screen.Top + 20, 120, 80);
                    var captureSettings = Field<AppSettings>(home, "settings");
                    captureSettings.LastCaptureRegion = region;
                    captureSettings.RandomSkin = true; captureSettings.RandomSkinMask = (1 << (int)SkinKind.Linear) | (1 << (int)SkinKind.Ink);
                    Skins.Select(SkinKind.Linear);
                    Call(home, "BeginQuickCapture", true); Wait(delegate { return !Field<bool>(home, "captureBusy"); });
                    Check(Skins.Current.Kind == SkinKind.Ink, "Quick copy did not select a random skin.");
                    using (var image = Clipboard.GetImage()) Check(image != null && image.Size == region.Size, "Quick copy omitted clipboard image or changed dimensions: " + (image == null ? "null" : image.Size.ToString()) + " expected " + region.Size + " notice " + Field<Label>(home, "notice").Text);
                    Check(!Application.OpenForms.Cast<Form>().Any(f => f is CaptureOverlay || f is EditorForm), "Quick copy left an editor open");
                    var feedback = Field<CaptureFeedback>(home, "captureFeedback");
                    Check(feedback != null && feedback.Visible, "Quick copy omitted success feedback");
                    Check(Native.GetForegroundWindow() != feedback.Handle && !feedback.ContainsFocus, "Success feedback stole focus: foreground=" + Native.GetForegroundWindow() + " toast=" + feedback.Handle + " focused=" + feedback.ContainsFocus);
                    Wait(delegate { return feedback.IsDisposed; });
                    Call(home, "BeginQuickCapture", false); Wait(delegate { return Application.OpenForms.Cast<Form>().OfType<CaptureOverlay>().Any(f => f.Visible && Field<bool>(f, "ready")); });
                    Check(Skins.Current.Kind == SkinKind.Linear, "Quick edit did not select a random skin.");
                    var editor = Application.OpenForms.Cast<Form>().OfType<CaptureOverlay>().Single();
                    Check(Field<bool>(editor, "ready") && Field<Rectangle>(editor, "selection").Size == region.Size, "Quick edit did not open the previous selection");
                    captureSettings.AskSaveLocation = false;
                    captureSettings.SaveDirectory = Path.Combine(root, "saved-capture");
                    Clipboard.SetDataObject("Before screenshot save", true, 5, 100);
                    Call(editor, "OnKeyDown", new KeyEventArgs(Keys.Control | Keys.S));
                    Check(editor.IsDisposed && !Field<bool>(home, "captureBusy"), "Saving did not finish the capture.");
                    using (var saved = new Bitmap(Directory.GetFiles(captureSettings.SaveDirectory, "*.png").Single()))
                    using (var copied = Clipboard.GetImage())
                    {
                        Check(copied != null && copied.Size == saved.Size && saved.Size == region.Size, "Saving did not copy the saved image.");
                        using (var pixels = new Bitmap(copied))
                            for (int y = 0; y < saved.Height; y++)
                                for (int x = 0; x < saved.Width; x++)
                                    Check(saved.GetPixel(x, y) == pixels.GetPixel(x, y), "Clipboard and saved image pixels differ.");
                    }
                    feedback = Field<CaptureFeedback>(home, "captureFeedback");
                    Check(feedback != null && feedback.Visible && feedback.Controls.OfType<Label>().Single().Text.Contains("已保存并复制"),
                        "Saving and copying omitted success feedback.");
                    Call(home, "BeginFullScreenCapture"); Wait(delegate { return Application.OpenForms.Cast<Form>().OfType<CaptureOverlay>().Any(f => f.Visible && Field<bool>(f, "ready")); });
                    Check(Skins.Current.Kind == SkinKind.Ink, "Full-screen capture did not select a random skin.");
                    editor = Application.OpenForms.Cast<Form>().OfType<CaptureOverlay>().Single();
                    Check(Field<Rectangle>(editor, "selection") == new Rectangle(Point.Empty, screen.Size), "Full screen did not include every display: " + Field<Rectangle>(editor, "selection") + " expected " + screen);
                    editor.Close();
                    foreach (bool scrolling in new[] { false, true })
                    {
                        var previousSkin = Skins.Current.Kind;
                        Call(home, "BeginCapture", scrolling);
                        Wait(delegate { return Application.OpenForms.Cast<Form>().OfType<CaptureOverlay>().Any(f => f.Visible); });
                        Check(Skins.Current.Kind != previousSkin, "Region or scrolling capture did not switch skins.");
                        var activeSkin = Skins.Current.Kind;
                        Call(home, "BeginCapture", false);
                        Check(Skins.Current.Kind == activeSkin, "Blocked duplicate capture changed the active skin.");
                        Application.OpenForms.Cast<Form>().OfType<CaptureOverlay>().Single().Close();
                    }
                    home.GetType().GetField("settingsOpen", Private).SetValue(home, true);
                    var beforeBlocked = Skins.Current.Kind; Call(home, "BeginCapture", false);
                    Check(Skins.Current.Kind == beforeBlocked && !Field<bool>(home, "captureBusy"), "Capture blocked by settings changed the skin.");
                    home.GetType().GetField("settingsOpen", Private).SetValue(home, false);
                    Clipboard.SetText("no image"); Call(home, "PinClipboard");
                    var pin = Application.OpenForms.Cast<Form>().OfType<PinForm>().Single();
                    Check(Field<Bitmap>(pin, "image").Size == region.Size, "Clipboard fallback did not use first history image"); pin.Close();
                    using (var blue = new Bitmap(23, 29))
                    { using (var g = Graphics.FromImage(blue)) g.Clear(Color.Blue); Clipboard.SetDataObject(blue, true); }
                    Call(home, "PinClipboard"); pin = Application.OpenForms.Cast<Form>().OfType<PinForm>().Single();
                    Check(Field<Bitmap>(pin, "image").Size == new Size(23, 29), "History overrode a valid clipboard image"); pin.Close();
                }
                using (var target = new Form { Bounds = new Rectangle(100, 100, 220, 180), TopMost = true })
                {
                    target.Show(); target.Activate(); Application.DoEvents(); Native.FlushDesktop();
                    var windows = Native.CaptureWindowBounds(SystemInformation.VirtualScreen);
                    var expected = target.RectangleToScreen(target.ClientRectangle); expected.Offset(-SystemInformation.VirtualScreen.X, -SystemInformation.VirtualScreen.Y);
                    Check(windows.Any(r => r.Contains(expected)), "Native window detection missed the visible target"); target.Close();
                }
                UpdatePriority(root);
                Console.WriteLine("PASS: shared window selection, long press, drag, region persistence, quick copy/edit, full screen and history pin fallback");
            }
            finally { if (previousClipboard != null) Clipboard.SetDataObject(previousClipboard, true, 5, 100); else Clipboard.Clear(); Directory.Delete(root, true); }
        }
    }
}
