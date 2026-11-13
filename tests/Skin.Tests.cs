using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace ShotInk
{
    internal static class SkinTests
    {
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static long Signature(Bitmap bitmap)
        {
            long value = 17;
            unchecked { for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++) value = value * 31 + bitmap.GetPixel(x, y).ToArgb(); }
            return value;
        }
        private static readonly string[] Names = { "lasso", "ocr-copy-selected", "ocr-copy-all", "ocr-return", "search", "search-previous", "search-next", "shape-rectangle", "mode-rectangle", "shape-ellipse", "mode-brush", "line-arrow", "line-straight", "text-bold", "text-italic", "stroke-1", "stroke-3", "stroke-6", "mosaic-size", "mosaic-pixelate", "blur-radius", "mosaic-gaussian", "mosaic-brush-size", "eraser-brush-size", "custom-color", "grip", "play", "pause", "microphone", "speaker", "pointer", "video", "gif", "scroll", "move", "shape", "arrow", "pen", "highlight", "mosaic", "text", "eraser", "ocr", "undo", "redo", "cancel", "pin", "save", "copy" };
        internal static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "ShotInk-skins-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Check(Skins.All.Length == 8 && new AppSettings().Skin == SkinKind.Classic, "Skin catalog or legacy default changed.");
                string path = Path.Combine(root, "settings.ini");
                File.WriteAllText(path, "save=1\nhistory-limit=10");
                Check(AppSettings.Load(path).Skin == SkinKind.Classic, "Old settings need a skin key.");
                File.WriteAllText(path, "save=1\nskin=99");
                Check(AppSettings.Load(path).Skin == SkinKind.Classic, "Unknown skin was not recovered safely.");
                foreach (var palette in Skins.All.Skip(1))
                {
                    Skins.Select(palette.Kind);
                    using (var wheel = ToolbarIcons.Create("custom-color"))
                    {
                        var hues = new HashSet<int>();
                        for (int y = 0; y < wheel.Height; y++) for (int x = 0; x < wheel.Width; x++)
                        {
                            var pixel = wheel.GetPixel(x, y);
                            if (pixel.A > 100 && pixel.GetSaturation() > .3F) hues.Add((int)(pixel.GetHue() / 60));
                        }
                        if (palette.Kind != SkinKind.Ink) Check(hues.Count >= 4, "Approved color-wheel artwork was lost: " + palette.Name);
                    }
                }
                Skins.Select(SkinKind.Classic);
                var signatures = new HashSet<long>();
                using (var host = new Form())
                using (var toolbar = new CaptureToolbar())
                using (var canvas = new AnnotationCanvas(new Bitmap(40, 30)))
                {
                    host.Controls.Add(toolbar); host.Show(); Application.DoEvents();
                    using (var original = canvas.Render())
                    {
                        long originalPixels = Signature(original);
                        foreach (var palette in Skins.All)
                        {
                            var settings = new AppSettings { Skin = palette.Kind, RecordMicrophoneDevice = "missing-test-device", RecordMicrophoneGain = 275 }; settings.Save(path);
                            Check(AppSettings.Load(path).Clone().RecordMicrophoneDevice == "missing-test-device" && AppSettings.Load(path).RecordMicrophoneGain == 275, "Microphone settings did not survive save/load/clone.");
                            Check(AppSettings.Load(path).Clone().Skin == palette.Kind, "Skin persistence or cloning failed.");
                            Skins.Select(palette.Kind);
                            using (var image = new Bitmap(toolbar.Width, toolbar.Height))
                            { toolbar.DrawToBitmap(image, toolbar.ClientRectangle); signatures.Add(Signature(image)); }
                            foreach (var name in Names)
                            using (var icon = ToolbarIcons.Create(name))
                            {
                                bool visible = false;
                                for (int y = 0; y < icon.Height; y++) for (int x = 0; x < icon.Width; x++)
                                {
                                    var pixel = icon.GetPixel(x, y); visible |= pixel.A > 30;
                                    if (palette.Kind == SkinKind.Ink && pixel.A > 0) Check(pixel.R == pixel.G && pixel.G == pixel.B, "Ink icon gained a colored pixel.");
                                }
                                Check(visible, "A skin icon is blank: " + palette.Name + " / " + name);
                            }
                            Skins.Apply(canvas);
                            using (var rendered = canvas.Render()) Check(Signature(rendered) == originalPixels, "Changing skins changed screenshot pixels.");
                        }
                    }
                    Check(signatures.Count == 8, "Live toolbar skin caches were not refreshed.");
                    host.Close();
                }
                Skins.Select(SkinKind.Classic);
                using (var options = new SettingsForm(new AppSettings(), delegate { return "simulated save failure"; }, delegate { return true; }))
                {
                    options.Show(); Application.DoEvents();
                    Check(options.Controls.Find("skin-feedback", true).Length == 0, "Removed skin description is still visible.");
                    var selector = (ComboBox)options.Controls.Find("skin-selector", true)[0];
                    selector.SelectedIndex = (int)SkinKind.Ink;
                    Check(Skins.Current.Kind == SkinKind.Ink && options.ReadSettings().Skin == SkinKind.Ink, "Skin preview or settings entry point failed.");
                    var save = FindButton(options, "保存"); save.PerformClick();
                    Check(options.DialogResult != DialogResult.OK, "Failed save closed the settings dialog.");
                    options.Close();
                }
                Check(Skins.Current.Kind == SkinKind.Classic, "Cancelling skin preview failed to restore the previous skin.");
                using (var button = new ToolOptionButton { IconName = "pen", Size = new Size(28, 28) })
                {
                    foreach (var palette in Skins.All)
                    {
                        Skins.Select(palette.Kind);
                        using (var normal = ToolbarIcons.Create("pen"))
                        using (var disabled = ToolbarIcons.Create("pen", ToolAppearance.Muted))
                            Check(Signature(normal) != Signature(disabled), "Disabled icon feedback disappeared.");
                        using (var image = new Bitmap(28, 28))
                        {
                            button.DrawToBitmap(image, button.ClientRectangle); long idle = Signature(image);
                            typeof(Control).GetMethod("OnMouseEnter", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(button, new object[] { EventArgs.Empty });
                            button.DrawToBitmap(image, button.ClientRectangle); Check(Signature(image) != idle, "Hover feedback disappeared.");
                            typeof(Control).GetMethod("OnMouseLeave", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(button, new object[] { EventArgs.Empty });
                        }
                    }
                }
                Console.WriteLine("PASS: 8 skins, 49 glyphs each, persistence, live toolbar refresh, grayscale ink, hover, disabled state, preview rollback and screenshot isolation");
            }
            finally { Skins.Select(SkinKind.Classic); Directory.Delete(root, true); }
        }
        private static Button FindButton(Control root, string text)
        {
            foreach (Control child in root.Controls)
            { if (child is Button && child.Text == text) return (Button)child; var found = FindButton(child, text); if (found != null) return found; }
            return null;
        }
        internal static void Previews(string directory)
        {
            Directory.CreateDirectory(directory);
            string profile = Path.Combine(Path.GetTempPath(), "ShotInk-skin-preview-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var home = new MainForm(profile, false))
                {
                    home.Show(); Application.DoEvents();
                    foreach (var palette in Skins.All.Skip(1))
                    {
                        var settings = new AppSettings { Skin = palette.Kind };
                        Check(home.ApplySettings(settings) == null, "Could not apply preview skin."); Application.DoEvents();
                        SavePreview(home, Path.Combine(directory, palette.Name + "-工作台.png"));
                        using (var options = new SettingsForm(settings, delegate { return null; }, delegate { return true; }))
                        {
                            options.Show(home);
                            var tabs = options.Controls.OfType<TabControl>().Single(); tabs.SelectedIndex = 2;
                            Application.DoEvents(); SavePreview(options, Path.Combine(directory, palette.Name + "-外观设置.png")); options.Close();
                        }
                    }
                    home.Close();
                }
            }
            finally { Skins.Select(SkinKind.Classic); if (Directory.Exists(profile)) Directory.Delete(profile, true); }
        }
        private static void SavePreview(Control control, string path)
        { using (var image = new Bitmap(control.Width, control.Height)) { control.DrawToBitmap(image, new Rectangle(Point.Empty, control.Size)); image.Save(path, ImageFormat.Png); } }
    }
}
