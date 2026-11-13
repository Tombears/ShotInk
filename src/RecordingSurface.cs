using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class RecordingSurface : Panel
    {
        internal RecordingSurface()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Font = ToolAppearance.Font;
            Padding = new Padding(2);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Parent != null)
            {
                var state = e.Graphics.Save();
                e.Graphics.TranslateTransform(-Left, -Top);
                var background = new PaintEventArgs(e.Graphics, Bounds);
                InvokePaintBackground(Parent, background);
                InvokePaint(Parent, background);
                e.Graphics.Restore(state);
            }
            ToolAppearance.DrawSmooth(e.Graphics, ClientSize, delegate(Graphics graphics)
            {
                SkinPainter.Surface(graphics, new RectangleF(.5F, .5F, Width - 1, Height - 1), 6);
            });
        }
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            var previous = Bounds;
            base.SetBoundsCore(x, y, width, height, specified);
            if (Parent == null || previous == Bounds || !Visible) return;
            var damage = Rectangle.Union(previous, Bounds);
            damage.Inflate(2, 2);
            Parent.Invalidate(damage, true);
        }
    }

}
