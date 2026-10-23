using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ShotInk
{
    internal enum StitchStatus { Added, Unchanged, Unmatched, LimitReached }

    // Only stores the first frame and newly exposed rows. Full frames are not repeatedly concatenated.
    internal sealed class VerticalStitcher : IDisposable
    {
        private readonly List<Bitmap> strips = new List<Bitmap>();
        private byte[] previous;
        private int frameHeight;
        internal const int MaxHeight = 30000;
        internal const long MaxPixels = 80000000;
        private readonly int heightLimit;
        private readonly long pixelLimit;
        internal VerticalStitcher(int heightLimit = MaxHeight, long pixelLimit = MaxPixels)
        { this.heightLimit = heightLimit; this.pixelLimit = pixelLimit; }
        internal int Height { get; private set; }
        internal int Width { get; private set; }
        internal int Frames { get; private set; }
        internal double LastError { get; private set; }

        internal StitchStatus Add(Bitmap frame)
        {
            if (previous == null)
            {
                if (frame.Height > heightLimit || (long)frame.Width * frame.Height > pixelLimit) return StitchStatus.LimitReached;
                Width = frame.Width;
                Height = frame.Height;
                strips.Add(new Bitmap(frame));
                previous = ReadPixels(frame); frameHeight = frame.Height;
                Frames = 1;
                return StitchStatus.Added;
            }
            if (frame.Width != Width || frame.Height != frameHeight) throw new ArgumentException("截图区域尺寸发生变化。");
            var before = previous;
            var after = ReadPixels(frame);
            double unchanged = Error(before, after, Width, frame.Height, 0);
            if (unchanged < 0.8) return StitchStatus.Unchanged;
            int maximum = frame.Height - Math.Max(32, frame.Height / 10);
            if (maximum < 1) return StitchStatus.Unmatched;
            int best = 0;
            double bestError = double.MaxValue;
            // Search every pixel: jumping offsets can miss the only valid match on sharp text.
            for (int offset = 1; offset <= maximum; offset++)
            {
                double error = Error(before, after, Width, frame.Height, offset, Math.Min(12, bestError));
                if (error < bestError) { bestError = error; best = offset; }
            }
            LastError = bestError;
            // Animated content and unrelated windows must never be silently appended.
            if (best == 0 || bestError > 12 || bestError > unchanged * 0.65) return StitchStatus.Unmatched;
            if (Height + best > heightLimit || (long)Width * (Height + best) > pixelLimit) return StitchStatus.LimitReached;
            strips.Add(frame.Clone(new Rectangle(0, frame.Height - best, Width, best), PixelFormat.Format24bppRgb));
            Height += best;
            previous = after;
            Frames++;
            return StitchStatus.Added;
        }

        private static byte[] ReadPixels(Bitmap image)
        {
            if (image.PixelFormat == PixelFormat.Format24bppRgb) return Read24BitPixels(image);
            using (var normalized = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(normalized)) g.DrawImageUnscaled(image, 0, 0);
                return Read24BitPixels(normalized);
            }
        }
        private static byte[] Read24BitPixels(Bitmap image)
        {
            var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var pixels = new byte[image.Width * image.Height * 3];
                for (int y = 0; y < image.Height; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * image.Width * 3, image.Width * 3);
                return pixels;
            }
            finally { image.UnlockBits(data); }
        }

        private static double Error(byte[] before, byte[] after, int width, int height, int offset, double cutoff = double.MaxValue)
        {
            int marginY = Math.Min(24, Math.Max(1, (height - offset) / 12));
            int endY = height - offset - marginY;
            if (endY <= marginY) return double.MaxValue;
            int stepX = Math.Max(1, width / 64);
            int stepY = Math.Max(1, (endY - marginY) / 40);
            int marginX = Math.Max(1, width / 20);
            int samples = ((endY - marginY + stepY - 1) / stepY) * ((width - 2 * marginX + stepX - 1) / stepX) * 3;
            long sum = 0;
            int count = 0;
            for (int y = marginY; y < endY; y += stepY)
            {
                for (int x = marginX; x < width - marginX; x += stepX)
                {
                    int a = ((y + offset) * width + x) * 3;
                    int b = (y * width + x) * 3;
                    sum += Math.Abs(before[a] - after[b]) + Math.Abs(before[a + 1] - after[b + 1]) + Math.Abs(before[a + 2] - after[b + 2]);
                    count += 3;
                }
                // Once this lower bound exceeds the best candidate, the remaining rows cannot win.
                if (sum > cutoff * samples) return double.MaxValue;
            }
            return count == 0 ? double.MaxValue : (double)sum / count;
        }

        internal Bitmap Build()
        {
            if (strips.Count == 0) throw new InvalidOperationException("尚未捕获图片。");
            var result = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(result))
            {
                int y = 0;
                foreach (var strip in strips) { g.DrawImageUnscaled(strip, 0, y); y += strip.Height; }
            }
            return result;
        }

        public void Dispose()
        {
            previous = null;
            foreach (var strip in strips) strip.Dispose();
            strips.Clear();
        }
    }
}
