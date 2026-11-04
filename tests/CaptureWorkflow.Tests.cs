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
        internal static void Run()
        {
            var previousClipboard = Clipboard.GetDataObject();
            var root = Path.Combine(Path.GetTempPath(), "ShotInk-capture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
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
                    System.Threading.Thread.Sleep(370); Application.DoEvents();
                    Mouse(overlay, "OnMouseUp", 50, 50);
                    Check(!Field<bool>(overlay, "ready"), "Long press without dragging selected the window");
                    Mouse(overlay, "OnMouseDown", 50, 50); Mouse(overlay, "OnMouseMove", 130, 110);
                    System.Threading.Thread.Sleep(370); Application.DoEvents();
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
                    Call(home, "BeginQuickCapture", false); Wait(delegate { return Application.OpenForms.Cast<Form>().Any(f => f is CaptureOverlay && f.Visible); });
                    var editor = Application.OpenForms.Cast<Form>().OfType<CaptureOverlay>().Single();
                    Check(Field<bool>(editor, "ready") && Field<Rectangle>(editor, "selection").Size == region.Size, "Quick edit did not open the previous selection");
                    editor.Close();
                    Call(home, "BeginFullScreenCapture"); Wait(delegate { return Application.OpenForms.Cast<Form>().Any(f => f is CaptureOverlay && f.Visible); });
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
                Console.WriteLine("PASS: shared window selection, long press, drag, region persistence, quick copy/edit, full screen and history pin fallback");
            }
            finally { if (previousClipboard != null) Clipboard.SetDataObject(previousClipboard, true, 5, 100); else Clipboard.Clear(); Directory.Delete(root, true); }
        }
    }
}
