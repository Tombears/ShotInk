using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ShotInk
{
    internal sealed partial class AnnotationCanvas
    {
        private sealed class EffectPatch : IDisposable
        {
            internal Rectangle Bounds;
            internal MosaicEffect Effect;
            internal int Size;
            internal Bitmap Pixels;
            public void Dispose() { Pixels.Dispose(); }
        }
        private readonly Dictionary<Mark, EffectPatch> mosaicCache = new Dictionary<Mark, EffectPatch>();
        private void ClearMosaicCache() { foreach (var patch in mosaicCache.Values) patch.Dispose(); mosaicCache.Clear(); }
        private void DrawMosaicPixels(Graphics graphics, Mark mark)
        {
            int size = mark.MosaicEffect == MosaicEffect.GaussianBlur ? Math.Max(1, mark.BlurRadius) : Math.Max(1, mark.MosaicSize > 0 ? mark.MosaicSize : (int)mark.Width * 5);
            var area = mark.Bounds;
            int halo = mark.MosaicEffect == MosaicEffect.GaussianBlur ? size * 3 + 4 : 0;
            int left = (int)Math.Floor(area.Left) - halo, top = (int)Math.Floor(area.Top) - halo;
            int right = (int)Math.Ceiling(area.Right) + halo, bottom = (int)Math.Ceiling(area.Bottom) + halo;
            if (mark.MosaicEffect == MosaicEffect.Pixelate)
            {
                left = (int)Math.Floor((double)left / size) * size; top = (int)Math.Floor((double)top / size) * size;
                right = (int)Math.Ceiling((double)right / size) * size; bottom = (int)Math.Ceiling((double)bottom / size) * size;
            }
            var bounds = Rectangle.Intersect(Rectangle.FromLTRB(left, top, right, bottom), new Rectangle(Point.Empty, Image.Size));
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            EffectPatch patch;
            if (!mosaicCache.TryGetValue(mark, out patch) || patch.Bounds != bounds || patch.Size != size || patch.Effect != mark.MosaicEffect)
            {
                if (patch != null) { mosaicCache.Remove(mark); patch.Dispose(); }
                // Active brush bounds change frequently; release abandoned previews and undo states.
                var stale = new List<Mark>();
                foreach (var key in mosaicCache.Keys) if (!Marks.Contains(key) && key != active) stale.Add(key);
                foreach (var key in stale) { mosaicCache[key].Dispose(); mosaicCache.Remove(key); }
                using (var source = Image.Clone(bounds, PixelFormat.Format24bppRgb))
                    patch = new EffectPatch { Bounds = bounds, Size = size, Effect = mark.MosaicEffect,
                        Pixels = mark.MosaicEffect == MosaicEffect.GaussianBlur ? ImageFilters.Gaussian(source, size) : ImageFilters.Pixelate(source, size) };
                mosaicCache[mark] = patch;
            }
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(patch.Pixels, patch.Bounds, 0, 0, patch.Pixels.Width, patch.Pixels.Height, GraphicsUnit.Pixel);
        }
    }

    internal static class ImageFilters
    {
        private static byte[] Read(Bitmap image)
        {
            var pixels = new byte[image.Width * image.Height * 3];
            var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try { for (int y = 0; y < image.Height; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * image.Width * 3, image.Width * 3); }
            finally { image.UnlockBits(data); }
            return pixels;
        }
        private static Bitmap Write(byte[] pixels, int width, int height)
        {
            var image = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            try
            {
                var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                try { for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 3, IntPtr.Add(data.Scan0, y * data.Stride), width * 3); }
                finally { image.UnlockBits(data); }
                return image;
            }
            catch { image.Dispose(); throw; }
        }
        internal static Bitmap Pixelate(Bitmap source, int block)
        {
            int width = source.Width, height = source.Height; var pixels = Read(source);
            for (int y = 0; y < height; y += block) for (int x = 0; x < width; x += block)
            {
                int endX = Math.Min(width, x + block), endY = Math.Min(height, y + block);
                long red = 0, green = 0, blue = 0;
                for (int row = y; row < endY; row++) for (int column = x; column < endX; column++)
                { int at = (row * width + column) * 3; blue += pixels[at]; green += pixels[at + 1]; red += pixels[at + 2]; }
                long count = (endX - x) * (endY - y);
                byte gray = (byte)((red * 299 + green * 587 + blue * 114 + count * 500) / (count * 1000));
                for (int row = y; row < endY; row++) for (int column = x; column < endX; column++)
                { int at = (row * width + column) * 3; pixels[at] = pixels[at + 1] = pixels[at + 2] = gray; }
            }
            return Write(pixels, width, height);
        }
        private static Bitmap Resize(Bitmap source, int width, int height)
        {
            var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(result))
            using (var attributes = new ImageAttributes())
            {
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            return result;
        }
        internal static Bitmap Gaussian(Bitmap source, int sigma)
        {
            int scale = Math.Max(1, Math.Min(4, sigma / 4));
            using (var working = Resize(source, Math.Max(1, source.Width / scale), Math.Max(1, source.Height / scale)))
            {
                int width = working.Width, height = working.Height; float spread = (float)sigma / scale;
                int radius = (int)Math.Ceiling(spread * 3); var kernel = new float[radius * 2 + 1]; float total = 0;
                for (int i = -radius; i <= radius; i++) { float value = (float)Math.Exp(-i * i / (2 * spread * spread)); kernel[i + radius] = value; total += value; }
                for (int i = 0; i < kernel.Length; i++) kernel[i] /= total;
                var input = Read(working); var horizontal = new byte[input.Length]; var output = new byte[input.Length];
                Parallel.For(0, height, delegate(int y)
                {
                    for (int x = 0; x < width; x++) for (int channel = 0; channel < 3; channel++)
                    {
                        float value = 0;
                        for (int k = -radius; k <= radius; k++) value += input[(y * width + Math.Max(0, Math.Min(width - 1, x + k))) * 3 + channel] * kernel[k + radius];
                        horizontal[(y * width + x) * 3 + channel] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(value)));
                    }
                });
                Parallel.For(0, height, delegate(int y)
                {
                    for (int x = 0; x < width; x++) for (int channel = 0; channel < 3; channel++)
                    {
                        float value = 0;
                        for (int k = -radius; k <= radius; k++) value += horizontal[(Math.Max(0, Math.Min(height - 1, y + k)) * width + x) * 3 + channel] * kernel[k + radius];
                        output[(y * width + x) * 3 + channel] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(value)));
                    }
                });
                using (var blurred = Write(output, width, height)) return Resize(blurred, source.Width, source.Height);
            }
        }
    }
}
