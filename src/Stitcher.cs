using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ShotInk
{
    internal enum StitchStatus { Added, Unchanged, Unmatched, LimitReached }

    // Only stores the first frame and newly exposed rows. Full frames are not repeatedly concatenated.
    internal sealed partial class VerticalStitcher : IDisposable
    {
        private readonly List<Bitmap> strips = new List<Bitmap>();
        private byte[] previous;
        private byte[] frontier;
        private int framePosition;
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

        internal StitchStatus Add(Bitmap frame, int direction = 0)
        {
            if (previous == null)
            {
                if (frame.Height > heightLimit || (long)frame.Width * frame.Height > pixelLimit) return StitchStatus.LimitReached;
                Width = frame.Width;
                Height = frame.Height;
                strips.Add(new Bitmap(frame));
                previous = ReadPixels(frame); frameHeight = frame.Height;
                frontier = previous; framePosition = 0;
                Frames = 1;
                return StitchStatus.Added;
            }
            if (frame.Width != Width || frame.Height != frameHeight) throw new ArgumentException("截图区域尺寸发生变化。");
            var before = previous;
            var after = ReadPixels(frame);
            double unchanged = Error(before, after, Width, frame.Height, 0, double.MaxValue, 1);
            if (unchanged < 0.15) return StitchStatus.Unchanged;
            int maximum = frame.Height - Math.Max(32, frame.Height / 10);
            if (maximum < 1) return StitchStatus.Unmatched;
            int best = 0;
            double bestError = double.MaxValue;
            // Search every pixel: jumping offsets can miss the only valid match on sharp text.
            int preferred = direction < 0 ? -1 : 1;
            for (int distance = 1; distance <= maximum; distance++)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    int offset = distance * preferred * (pass == 0 ? 1 : -1);
                    double error = Error(before, after, Width, frame.Height, offset, Math.Min(12, bestError));
                    if (error < bestError) { bestError = error; best = offset; }
                }
            }
            if (best == 0 || bestError > 12 || bestError > unchanged * 0.65) return StitchStatus.Unmatched;
            // Validate the alignment at full width; sparse sampling can miss a one-pixel text shift.
            int coarse = best;
            bestError = Error(before, after, Width, frame.Height, best, 3, 1);
            foreach (int delta in new[] { -1, 1, -2, 2 })
            {
                int candidate = coarse + delta; if (candidate == 0 || Math.Abs(candidate) > maximum) continue;
                double error = Error(before, after, Width, frame.Height, candidate, Math.Min(3, bestError), 1);
                if (error < bestError) { bestError = error; best = candidate; }
            }
            LastError = bestError;
            if (bestError > 3 || bestError > unchanged * 0.65) return StitchStatus.Unmatched;
            int position = framePosition + best;
            int added = position + frameHeight - Height;
            if (added <= 0)
            {
                previous = after; framePosition = position;
                return StitchStatus.Unchanged;
            }
            // Returning through previously captured content must not append it again.
            // Validate any new tail against the furthest captured viewport, not the last revisited frame.
            if (added > maximum || Error(frontier, after, Width, frameHeight, added, 3, 1) > 3) return StitchStatus.Unmatched;
            if (Height + added > heightLimit || (long)Width * (Height + added) > pixelLimit) return StitchStatus.LimitReached;
            int seam = FindTextSeam(frontier, after, Width, frame.Height, added);
            TrimTail(Height - (frame.Height - added - seam));
            strips.Add(frame.Clone(new Rectangle(0, seam, Width, frame.Height - seam), PixelFormat.Format24bppRgb));
            Height += added;
            previous = after; frontier = after; framePosition = position;
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

        private static double Error(byte[] before, byte[] after, int width, int height, int offset, double cutoff = double.MaxValue, int pixelStep = 0)
        {
            int displacement = Math.Abs(offset);
            int beforeY = Math.Max(0, offset), afterY = Math.Max(0, -offset);
            int marginY = Math.Min(24, Math.Max(1, (height - displacement) / 12));
            int endY = height - displacement - marginY;
            if (endY <= marginY) return double.MaxValue;
            int stepX = pixelStep > 0 ? pixelStep : Math.Max(1, width / 192);
            int stepY = pixelStep > 0 ? 1 : Math.Max(1, height / 160);
            int marginX = Math.Max(1, Math.Min(8, width / 40));
            int right = width - Math.Max(marginX, Math.Min(24, width / 20));
            int samples = ((endY - marginY + stepY - 1) / stepY) * ((right - marginX + stepX - 1) / stepX) * 3;
            long sum = 0;
            int count = 0;
            for (int y = marginY; y < endY; y += stepY)
            {
                for (int x = marginX; x < right; x += stepX)
                {
                    int a = ((y + beforeY) * width + x) * 3;
                    int b = ((y + afterY) * width + x) * 3;
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
            frontier = null;
            foreach (var strip in strips) strip.Dispose();
            strips.Clear();
        }
    }
}
