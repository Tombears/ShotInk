using System.Drawing;
using System.Drawing.Drawing2D;

namespace ShotInk
{
    internal static class SliderAppearance
    {
        internal const int Height = 36;
        internal const int RangeHeight = 52;
        internal static float TrackY(int height) { return height / 2F; }
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
    }
}
