using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace ShotInk
{
    internal sealed partial class VerticalStitcher
    {
        private static int FindTextSeam(byte[] before, byte[] after, int width, int height, int offset)
        {
            int overlap = height - offset;
            int first = Math.Max(3, overlap - Math.Min(160, Math.Max(32, height / 3)));
            int left = Math.Max(1, Math.Min(8, width / 40)), right = width - Math.Max(left, Math.Min(24, width / 20));
            for (int y = overlap - 3; y >= first; y--)
            {
                long variation = 0, mismatch = 0; int count = 0;
                for (int x = left; x < right; x++)
                {
                    int at = (y * width + x) * 3, previousAt = ((y + offset) * width + x) * 3;
                    for (int channel = 0; channel < 3; channel++)
                    {
                        int value = after[at + channel];
                        variation += Math.Abs(value - after[at + 3 + channel])
                            + Math.Abs(value - after[at - width * 3 + channel]) + Math.Abs(value - after[at + width * 3 + channel])
                            + Math.Abs(value - after[at - width * 6 + channel]) + Math.Abs(value - after[at + width * 6 + channel]);
                        mismatch += Math.Abs(value - before[previousAt + channel]); count++;
                    }
                }
                // Replace the overlapping tail from a quiet row, keeping each glyph in one frame.
                if (count > 0 && variation < count * 0.25 && mismatch < count * 0.25) return y;
            }
            return overlap;
        }
        private void TrimTail(int keepHeight)
        {
            int remove = Height - keepHeight;
            while (remove > 0)
            {
                int index = strips.Count - 1; var tail = strips[index];
                if (tail.Height <= remove) { remove -= tail.Height; strips.RemoveAt(index); tail.Dispose(); }
                else
                {
                    var kept = tail.Clone(new Rectangle(0, 0, tail.Width, tail.Height - remove), PixelFormat.Format24bppRgb);
                    strips[index] = kept; tail.Dispose(); remove = 0;
                }
            }
        }
        private void TrimHead(int remove)
        {
            while (remove > 0)
            {
                var first = strips[0];
                if (first.Height <= remove) { remove -= first.Height; strips.RemoveAt(0); first.Dispose(); }
                else
                {
                    strips[0] = first.Clone(new Rectangle(0, remove, first.Width, first.Height - remove), PixelFormat.Format24bppRgb);
                    first.Dispose(); remove = 0;
                }
            }
        }
    }
}
