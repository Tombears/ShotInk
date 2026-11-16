using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Threading.Tasks;

namespace ShotInk
{
    internal static class SkinIcons
    {
        // Cell order follows the seven approved 7-by-7 design sheets.
        private static readonly string[] Names = {
            "move", "shape", "arrow", "pen", "highlight", "mosaic", "text",
            "eraser", "ocr", "scroll", "gif", "video", "undo", "redo",
            "cancel", "pin", "save", "copy", "shape-rectangle", "shape-ellipse", "line-straight",
            "line-arrow", "mode-brush", "mode-rectangle", "text-bold", "text-italic", "stroke-1", "stroke-3",
            "stroke-6", "mosaic-size", "mosaic-pixelate", "blur-radius", "mosaic-gaussian", "mosaic-brush-size", "eraser-brush-size",
            "custom-color", "lasso", "ocr-copy-selected", "ocr-copy-all", "ocr-return", "search", "search-previous",
            "search-next", "grip", "play", "pause", "microphone", "speaker", "pointer"
        };
        private static readonly Dictionary<SkinKind, Bitmap> atlases = new Dictionary<SkinKind, Bitmap>();
        private static readonly Dictionary<string, Color> accents = new Dictionary<string, Color>();
        private static readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>();
        private static readonly object gate = new object();
        private static readonly object preloadGate = new object();
        private static int preloadMask;
        private static Task preloadTask = Task.FromResult(true);
        private const int CellSize = 96;
        private static Bitmap Atlas(SkinKind kind)
        {
            Bitmap atlas;
            if (atlases.TryGetValue(kind, out atlas)) return atlas;
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("ShotInk.Skins." + kind + ".png"))
            {
                if (resource == null) throw new InvalidOperationException("皮肤图标资源缺失：" + kind);
                using (var decoded = Image.FromStream(resource))
                {
                    atlas = new Bitmap(decoded.Width, decoded.Height, PixelFormat.Format32bppPArgb);
                    using (var graphics = Graphics.FromImage(atlas))
                    { graphics.CompositingMode = CompositingMode.SourceCopy; graphics.DrawImageUnscaled(decoded, 0, 0); }
                }
            }
            atlases.Add(kind, atlas); return atlas;
        }
        private static Rectangle Cell(int index)
        { return new Rectangle(index % 7 * CellSize, index / 7 * CellSize, CellSize, CellSize); }
        internal static Bitmap Create(string name, Color color)
        {
            int index = Array.IndexOf(Names, name);
            var kind = Skins.Current.Kind;
            if (index < 0) return ToolbarIcons.CreateOutline(name, color);
            if (kind == SkinKind.Classic && name != "custom-color") return ClassicIcon(name, color);
            bool disabled = color.ToArgb() == Skins.All[(int)kind].Muted.ToArgb();
            if (kind == SkinKind.Classic) kind = SkinKind.Linear;
            return CachedIcon(kind, index, disabled);
        }
        private static Bitmap ClassicIcon(string name, Color color)
        {
            // User-selected annotation colors are unbounded. Cache only toolbar states.
            if (color != Color.FromArgb(65, 68, 74) && color != Skins.All[0].Muted && color != ToolAppearance.ClassicAccent(name))
                return ToolbarIcons.CreateOutline(name, color);
            string key = "Classic/" + name + "/" + color.ToArgb();
            lock (gate)
            {
                Bitmap icon;
                if (!icons.TryGetValue(key, out icon)) { icon = ToolbarIcons.CreateOutline(name, color); icons.Add(key, icon); }
                return (Bitmap)icon.Clone();
            }
        }
        private static Bitmap CachedIcon(SkinKind kind, int index, bool disabled)
        {
            string key = kind + "/" + index + "/" + disabled;
            lock (gate)
            {
                Bitmap icon;
                if (!icons.TryGetValue(key, out icon)) { icon = Render(kind, index, disabled); icons.Add(key, icon); }
                return (Bitmap)icon.Clone();
            }
        }
        private static Bitmap Render(SkinKind kind, int index, bool disabled)
        {
            return ToolAppearance.RenderSmooth(new Size(20, 20), delegate(Graphics graphics)
            {
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    if (disabled)
                    {
                        attributes.SetColorMatrix(new ColorMatrix(new[] {
                            new[] { .299F, .299F, .299F, 0, 0 }, new[] { .587F, .587F, .587F, 0, 0 },
                            new[] { .114F, .114F, .114F, 0, 0 }, new[] { 0F, 0, 0, .45F, 0 }, new[] { 0F, 0, 0, 0, 1 }
                        }));
                    }
                    graphics.InterpolationMode = kind == SkinKind.Pixel ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                    var rectangle = Cell(index);
                    graphics.DrawImage(Atlas(kind), new Rectangle(0, 0, 20, 20), rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, GraphicsUnit.Pixel, attributes);
                }
            });
        }
        internal static Color Accent(string name, Color fallback)
        {
            return Accent(Skins.Current.Kind, name, fallback);
        }
        private static Color Accent(SkinKind kind, string name, Color fallback)
        {
            int index = Array.IndexOf(Names, name);
            if (kind == SkinKind.Classic) return fallback;
            if (kind == SkinKind.Ink || index < 0) return Skins.All[(int)kind].Accent;
            string key = kind + "/" + name;
            lock (gate)
            {
                Color result;
                if (accents.TryGetValue(key, out result)) return result;
                var atlas = Atlas(kind); var rectangle = Cell(index);
                double red = 0, green = 0, blue = 0, weight = 0;
                for (int y = rectangle.Top; y < rectangle.Bottom; y++) for (int x = rectangle.Left; x < rectangle.Right; x++)
                {
                    var pixel = atlas.GetPixel(x, y);
                    if (pixel.A < 200 || pixel.GetSaturation() < .18F) continue;
                    double sample = pixel.GetSaturation(); weight += sample;
                    red += pixel.R * sample; green += pixel.G * sample; blue += pixel.B * sample;
                }
                result = weight > 0 ? Color.FromArgb((int)(red / weight), (int)(green / weight), (int)(blue / weight)) : Skins.All[(int)kind].Accent;
                accents.Add(key, result); return result;
            }
        }
        internal static Task Preload(int mask)
        {
            lock (preloadGate)
            {
                int pending = mask & Skins.AllMask & ~preloadMask;
                if (pending == 0) return preloadTask;
                preloadMask |= pending;
                preloadTask = preloadTask.ContinueWith(delegate(Task previous)
                {
                    // Observe an earlier failure. Normal icon loading can still retry it.
                    if (previous.IsFaulted) System.Diagnostics.Trace.WriteLine(previous.Exception);
                    foreach (var palette in Skins.All)
                    {
                        if ((pending & (1 << (int)palette.Kind)) == 0) continue;
                        foreach (var name in Names)
                        {
                            Accent(palette.Kind, name, ToolAppearance.ClassicAccent(name));
                            if (palette.Kind == SkinKind.Classic && name != "custom-color")
                            {
                                using (ClassicIcon(name, Color.FromArgb(65, 68, 74))) { }
                                using (ClassicIcon(name, palette.Muted)) { }
                                using (ClassicIcon(name, ToolAppearance.ClassicAccent(name))) { }
                            }
                            else
                            {
                                var kind = palette.Kind == SkinKind.Classic ? SkinKind.Linear : palette.Kind;
                                int index = Array.IndexOf(Names, name);
                                using (CachedIcon(kind, index, false)) { }
                                using (CachedIcon(kind, index, true)) { }
                            }
                        }
                        // Only small rendered icons are needed after a palette is warm.
                        lock (gate)
                        {
                            var kind = palette.Kind == SkinKind.Classic ? SkinKind.Linear : palette.Kind;
                            Bitmap atlas;
                            if (atlases.TryGetValue(kind, out atlas)) { atlases.Remove(kind); atlas.Dispose(); }
                        }
                    }
                }, TaskScheduler.Default);
                return preloadTask;
            }
        }
    }
}
