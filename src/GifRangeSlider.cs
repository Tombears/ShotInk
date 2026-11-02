using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class GifRangeSlider : Control
    {
        private int first, last;
        private bool dragging;
        internal int Maximum { get; private set; }
        internal int First { get { return first; } }
        internal int Last { get { return last; } }
        internal bool EditingStart { get; private set; }
        internal event Action<int> RangeChanged;
        internal GifRangeSlider(int maximum)
        {
            Maximum = maximum; last = maximum; EditingStart = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            TabStop = true; AccessibleName = "GIF 起点与终点区段"; Cursor = Cursors.Hand; BackColor = Color.White;
        }
        internal void SelectBoundary(bool start) { EditingStart = start; Focus(); Invalidate(); }
        internal void SetRange(int start, int end)
        {
            if (start < 0 || end < start || end > Maximum) throw new ArgumentOutOfRangeException("start");
            first = start; last = end; Invalidate(); if (RangeChanged != null) RangeChanged(EditingStart ? first : last);
        }
        internal void AdjustBoundary(int delta)
        { if (EditingStart) SetRange(Math.Max(0, Math.Min(last, first + delta)), last); else SetRange(first, Math.Max(first, Math.Min(Maximum, last + delta))); }
        private float Pixel(int value) { return 12 + (Width - 24) * (Maximum == 0 ? 0 : value / (float)Maximum); }
        private int Frame(int x) { return Maximum == 0 ? 0 : Math.Max(0, Math.Min(Maximum, (int)Math.Round((x - 12) * Maximum / (double)Math.Max(1, Width - 24)))); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
            EditingStart = Math.Abs(e.X - Pixel(first)) < Math.Abs(e.X - Pixel(last)) || (first == last && e.X <= Pixel(first));
            Focus(); dragging = true; Capture = true; MoveHandle(e.X);
        }
        private void MoveHandle(int x)
        { int at = Frame(x); if (EditingStart) SetRange(Math.Min(at, last), last); else SetRange(first, Math.Max(at, first)); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) MoveHandle(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) { if (dragging) MoveHandle(e.X); dragging = false; Capture = false; } }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) dragging = false; }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ToolAppearance.DrawSmooth(e.Graphics, ClientSize, delegate(Graphics g)
            {
            float y = Height / 2F, a = Pixel(first), b = Pixel(last);
            using (var path = ToolAppearance.Rounded(new RectangleF(5, y - 8, Math.Max(14, Width - 10), 16), 7))
            using (var brush = new SolidBrush(Color.FromArgb(232, 236, 243))) g.FillPath(brush, path);
            using (var brush = new SolidBrush(Color.FromArgb(186, 213, 255))) g.FillRectangle(brush, a, y - 8, Math.Max(1, b - a), 16);
            DrawHandle(g, a, y, true); DrawHandle(g, b, y, false);
            });
            TextRenderer.DrawText(e.Graphics, "起点", Font, new Point(4, Height / 2 - 32), ToolAppearance.Ink);
            var size = TextRenderer.MeasureText("终点", Font); TextRenderer.DrawText(e.Graphics, "终点", Font, new Point(Math.Max(4, Width - size.Width - 4), Height / 2 - 32), ToolAppearance.Ink);
        }
        private void DrawHandle(Graphics g, float x, float y, bool start)
        {
            var color = start ? Color.FromArgb(47, 111, 220) : Color.FromArgb(124, 79, 202);
            using (var path = ToolAppearance.Rounded(new RectangleF(x - 6, y - 15, 12, 30), 4))
            using (var brush = new SolidBrush(color))
            { g.FillPath(brush, path); if (Focused && EditingStart == start) using (var pen = new Pen(ToolAppearance.Ink, 2)) g.DrawPath(pen, path); }
            using (var pen = new Pen(Color.White, 1.5F)) g.DrawLine(pen, x, y - 5, x, y + 5);
        }
    }
}
