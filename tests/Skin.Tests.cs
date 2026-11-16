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
                TestRandomSkins(path);
                TestPreload();
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
                    var random = (CheckBox)options.Controls.Find("random-skin", true)[0];
                    var choices = options.Controls.Find("random-skin-choices", true)[0];
                    Check(!random.Checked && !choices.Enabled && choices.Controls.Cast<CheckBox>().All(choice => choice.Checked), "Random skin candidates must default to all selected.");
                    selector.SelectedIndex = (int)SkinKind.Ink;
                    Check(Skins.Current.Kind == SkinKind.Ink && options.ReadSettings().Skin == SkinKind.Ink, "Skin preview or settings entry point failed.");
                    random.Checked = true;
                    Check(!selector.Enabled && choices.Enabled, "Random skin toggle did not update its controls.");
                    foreach (CheckBox choice in choices.Controls) choice.Checked = false;
                    var save = FindButton(options, "保存"); save.PerformClick();
                    Check(options.DialogResult != DialogResult.OK, "Empty enabled random selection was accepted.");
                    ((CheckBox)options.Controls.Find("random-skin-Ink", true)[0]).Checked = true;
                    var candidate = options.ReadSettings();
                    Check(candidate.RandomSkin && candidate.RandomSkinMask == 1 << (int)SkinKind.Ink, "Candidate checkboxes were not read from the settings dialog.");
                    save.PerformClick();
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
        private static void TestRandomSkins(string path)
        {
            var defaults = AppSettings.Load(path);
            Check(!defaults.RandomSkin && defaults.RandomSkinMask == Skins.AllMask, "Legacy settings changed manual mode or lost the default candidate set.");
            foreach (string mask in new[] { "-1", "256", "bad", "0" })
            {
                File.WriteAllText(path, "skin=2\nrandom-skin=1\nrandom-skin-mask=" + mask);
                var recovered = AppSettings.Load(path);
                Check(recovered.RandomSkin && recovered.RandomSkinMask == Skins.AllMask, "Damaged random skin candidates were not recovered.");
                recovered.Validate();
            }
            var settings = new AppSettings { Skin = SkinKind.Glass, RandomSkin = true, RandomSkinMask = (1 << (int)SkinKind.Clay) | (1 << (int)SkinKind.Ink) };
            settings.Save(path);
            var restored = AppSettings.Load(path).Clone();
            Check(restored.RandomSkin && restored.RandomSkinMask == settings.RandomSkinMask, "Random preferences did not survive save/load/clone.");
            Skins.Select(SkinKind.Glass);
            for (int repeat = 0; repeat < 100; repeat++)
            {
                var previous = Skins.Current.Kind;
                Skins.SelectForCapture(restored);
                Check((restored.RandomSkinMask & (1 << (int)Skins.Current.Kind)) != 0 && Skins.Current.Kind != previous, "Random capture escaped its candidates or repeated the previous skin.");
            }
            Check(restored.Skin == SkinKind.Glass, "Random capture overwrote the manually selected skin.");
            restored.RandomSkinMask = 1 << (int)SkinKind.Ink;
            for (int repeat = 0; repeat < 3; repeat++) { Skins.SelectForCapture(restored); Check(Skins.Current.Kind == SkinKind.Ink, "Single candidate was not respected."); }
            restored.RandomSkin = false; Skins.Select(SkinKind.Pixel); Skins.SelectForCapture(restored);
            Check(Skins.Current.Kind == SkinKind.Pixel, "Disabled random mode changed the skin.");
            restored.RandomSkinMask = 0; restored.Save(path);
            Check(AppSettings.Load(path).RandomSkinMask == 0, "Disabled random candidates were not preserved.");
            restored.RandomSkin = true;
            bool rejected = false;
            try { restored.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Empty random candidates passed validation.");
            Skins.Select(SkinKind.Classic);
            Console.WriteLine("PASS: random candidates, legacy defaults, persistence, clone isolation, single selection and repeat avoidance");
        }
        private static void TestPreload()
        {
            var selected = Skins.Current.Kind;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var warm = SkinIcons.Preload(Skins.AllMask); warm.GetAwaiter().GetResult();
            Check(Skins.Current.Kind == selected && object.ReferenceEquals(warm, SkinIcons.Preload(Skins.AllMask)), "Preloading changed the active skin or queued duplicate work.");
            var cache = (System.Collections.IDictionary)typeof(SkinIcons).GetField("icons", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            int count = cache.Count;
            watch.Restart();
            foreach (var palette in Skins.All)
            {
                Skins.Select(palette.Kind);
                foreach (var name in Names)
                {
                    using (var first = ToolbarIcons.Create(name))
                    using (var second = ToolbarIcons.Create(name))
                    {
                        Check(!object.ReferenceEquals(first, second) && Signature(first) == Signature(second), "Cached icons do not have independent ownership or matching pixels.");
                        first.SetPixel(0, 0, Color.Magenta);
                        using (var third = ToolbarIcons.Create(name)) Check(Signature(second) == Signature(third), "A caller modified the shared icon cache.");
                    }
                    using (ToolbarIcons.Create(name, ToolAppearance.Muted)) { }
                }
            }
            Check(cache.Count == count, "Warm icon requests grew the bounded cache.");
            var atlases = (System.Collections.IDictionary)typeof(SkinIcons).GetField("atlases", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            Check(atlases.Count == 0, "Preloaded skins retained full-size atlases or decoded them again.");
            Console.WriteLine("PASS: background preload, stable active skin, bounded cache, independent images and released atlases; warm icon checks " + watch.ElapsedMilliseconds + " ms");
            Skins.Select(selected);
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
