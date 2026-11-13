using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace ShotInk
{
    internal static class CaptureWorkflowTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Private).GetValue(target); }
        private static void Call(object target, string name, params object[] args) { target.GetType().GetMethod(name, Private).Invoke(target, args); }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Mouse(Control target, string action, int x, int y) { Call(target, action, new MouseEventArgs(MouseButtons.Left, 1, x, y, 0)); }
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

        internal static void Run()
        {
            CompactToolbar();
            KeyboardSelection();
            FreeSelection();
            var previousClipboard = Clipboard.GetDataObject();
            var root = Path.Combine(Path.GetTempPath(), "ShotInk-capture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
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
                    Field<AppSettings>(home, "settings").LastCaptureRegion = region;
                    Call(home, "BeginQuickCapture", true); Wait(delegate { return !Field<bool>(home, "captureBusy"); });
                    using (var image = Clipboard.GetImage()) Check(image != null && image.Size == region.Size, "Quick copy omitted clipboard image or changed dimensions: " + (image == null ? "null" : image.Size.ToString()) + " expected " + region.Size + " notice " + Field<Label>(home, "notice").Text);
                    Check(!Application.OpenForms.Cast<Form>().Any(f => f is CaptureOverlay || f is EditorForm), "Quick copy left an editor open");
                    var feedback = Field<CaptureFeedback>(home, "captureFeedback");
                    Check(feedback != null && feedback.Visible, "Quick copy omitted success feedback");
                    Check(Native.GetForegroundWindow() != feedback.Handle && !feedback.ContainsFocus, "Success feedback stole focus: foreground=" + Native.GetForegroundWindow() + " toast=" + feedback.Handle + " focused=" + feedback.ContainsFocus);
                    Wait(delegate { return feedback.IsDisposed; });
                    Call(home, "BeginQuickCapture", false); Wait(delegate { return Application.OpenForms.Cast<Form>().OfType<CaptureOverlay>().Any(f => f.Visible && Field<bool>(f, "ready")); });
                    var editor = Application.OpenForms.Cast<Form>().OfType<CaptureOverlay>().Single();
                    Check(Field<bool>(editor, "ready") && Field<Rectangle>(editor, "selection").Size == region.Size, "Quick edit did not open the previous selection");
                    editor.Close();
                    Call(home, "BeginFullScreenCapture"); Wait(delegate { return Application.OpenForms.Cast<Form>().OfType<CaptureOverlay>().Any(f => f.Visible && Field<bool>(f, "ready")); });
                    editor = Application.OpenForms.Cast<Form>().OfType<CaptureOverlay>().Single();
                    Check(Field<Rectangle>(editor, "selection") == new Rectangle(Point.Empty, screen.Size), "Full screen did not include every display: " + Field<Rectangle>(editor, "selection") + " expected " + screen);
                    editor.Close();
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
