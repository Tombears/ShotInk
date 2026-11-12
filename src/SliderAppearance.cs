using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ShotInk
{
    internal static class SliderAppearance
    {
        internal const int Height = 70;
        internal static float TrackY(int height) { return Math.Max(14, height - 30); }
        internal static void Track(Graphics g, float left, float right, float y, float first, float last, Color accent, bool enabled)
        {
            using (var pen = new Pen(Color.FromArgb(223, 225, 234), 6))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                g.DrawLine(pen, left, y, right, y);
                pen.Color = enabled ? accent : Color.FromArgb(177, 181, 197);
                if (last > first) g.DrawLine(pen, first, y, last, y);
            }
        }
        internal static void Thumb(Graphics g, float x, float y, Color accent, bool enabled, bool hot, bool focused, bool dragging)
        {
            var color = enabled ? accent : Color.FromArgb(180, 184, 201);
            float radius = dragging ? 10 : 9;
            if (enabled && (hot || focused || dragging))
            {
                float halo = dragging ? 18 : 15;
                using (var glow = new SolidBrush(Color.FromArgb(dragging ? 38 : 26, accent))) g.FillEllipse(glow, x - halo, y - halo, halo * 2, halo * 2);
            }
            using (var shadow = new SolidBrush(Color.FromArgb(enabled ? 28 : 12, 35, 37, 59))) g.FillEllipse(shadow, x - radius - 1, y - radius + 2, radius * 2 + 2, radius * 2 + 2);
            using (var face = new SolidBrush(Color.White)) g.FillEllipse(face, x - radius, y - radius, radius * 2, radius * 2);
            using (var rim = new Pen(focused ? color : Color.FromArgb(219, 220, 231), focused ? 1.5F : 1)) g.DrawEllipse(rim, x - radius, y - radius, radius * 2, radius * 2);
            using (var dot = new SolidBrush(color)) g.FillEllipse(dot, x - 4, y - 4, 8, 8);
        }
        internal static void Bubble(Graphics g, string text, float x, float y, int width, Color accent, bool enabled)
        {
            float bubbleWidth = Math.Max(28, g.MeasureString(text, ToolAppearance.HeadingFont).Width + 12);
            bubbleWidth = Math.Min(bubbleWidth, Math.Max(1, width - 2));
            float left = Math.Max(1, Math.Min(width - bubbleWidth - 1, x - bubbleWidth / 2));
            var bounds = new RectangleF(left, y - 35, bubbleWidth, 21);
            var color = enabled ? accent : Color.FromArgb(171, 175, 192);
            using (var path = ToolAppearance.Rounded(bounds, 6))
            using (var fill = new LinearGradientBrush(bounds, ToolAppearance.Tint(color, 0.14F), color, LinearGradientMode.Vertical)) g.FillPath(fill, path);
            float pointer = Math.Max(left + 6, Math.Min(left + bubbleWidth - 6, x));
            using (var fill = new SolidBrush(color)) g.FillPolygon(fill, new[] { new PointF(pointer - 4, bounds.Bottom - 1), new PointF(pointer + 4, bounds.Bottom - 1), new PointF(x, y - 10) });
            using (var fill = new SolidBrush(Color.White))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString(text, ToolAppearance.HeadingFont, fill, bounds, format);
        }
        internal static void Ticks(Graphics g, float left, float right, float y, int minimum, int maximum, bool enabled)
        {
            int intervals = maximum == minimum ? 0 : right - left < 140 ? 2 : 4;
            int previous = int.MinValue;
            using (var pen = new Pen(enabled ? Color.FromArgb(146, 151, 173) : Color.FromArgb(204, 206, 216)))
            using (var text = new SolidBrush(enabled ? ToolAppearance.Muted : Color.FromArgb(180, 184, 197)))
            using (var format = new StringFormat { Alignment = StringAlignment.Center })
            {
                for (int index = 0; index <= intervals; index++)
                {
                    int value = intervals == 0 ? minimum : minimum + (int)Math.Round((maximum - (double)minimum) * index / intervals);
                    if (value == previous) continue;
                    previous = value;
                    float x = maximum == minimum ? left : left + (right - left) * (float)((value - (double)minimum) / (maximum - (double)minimum));
                    g.DrawLine(pen, x, y + 10, x, y + 14);
                    g.DrawString(value.ToString(), ToolAppearance.Font, text, x, y + 15, format);
                }
            }
        }
    }
}
