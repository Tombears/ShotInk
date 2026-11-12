using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class GifRangeSlider : Control
    {
        private int first, last;
        private bool dragging;
        private bool hoverStart, hoverEnd;
        private readonly SliderValueBox firstInput, lastInput;
        private readonly RoundedInputFrame firstInputFrame, lastInputFrame;
        private readonly Label firstLabel, lastLabel;
        internal bool EditingValue { get { return firstInput.ContainsFocus || lastInput.ContainsFocus; } }
        internal int Maximum { get; private set; }
        internal int First { get { return first; } }
        internal int Last { get { return last; } }
        internal bool EditingStart { get; private set; }
        internal event Action<int> RangeChanged;
        internal GifRangeSlider(int maximum)
        {
            Maximum = maximum; last = maximum; EditingStart = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            TabStop = true; AccessibleName = "GIF 起点与终点区段"; AccessibleRole = AccessibleRole.Slider; Cursor = Cursors.Hand; BackColor = Color.White;
            firstLabel = new Label { Text = "起点", AutoSize = true, Font = ToolAppearance.Font };
            lastLabel = new Label { Text = "终点", AutoSize = true, Font = ToolAppearance.Font };
            firstInput = new SliderValueBox(delegate { return first + 1; }, delegate { return 1; }, delegate { return last + 1; }, delegate(int value) { EditingStart = true; SetRange(value - 1, last); }) { Name = "gif-first-frame", AccessibleName = "起点帧" };
            lastInput = new SliderValueBox(delegate { return last + 1; }, delegate { return first + 1; }, delegate { return Maximum + 1; }, delegate(int value) { EditingStart = false; SetRange(first, value - 1); }) { Name = "gif-last-frame", AccessibleName = "终点帧" };
            firstInputFrame = new RoundedInputFrame(firstInput, ToolAppearance.Accent("gif")); lastInputFrame = new RoundedInputFrame(lastInput, ToolAppearance.Accent("gif"));
            Controls.Add(firstLabel); Controls.Add(firstInputFrame); Controls.Add(lastLabel); Controls.Add(lastInputFrame); PositionInputs();
        }
        private void PositionInputs()
        {
            if (firstInputFrame == null) return;
            firstLabel.Location = new Point(0, 4); firstInputFrame.Location = new Point(36, 0);
            lastInputFrame.Location = new Point(Math.Max(0, Width - 80), 0); lastLabel.Location = new Point(Math.Max(0, Width - 32), 4);
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); PositionInputs(); }
        internal void SelectBoundary(bool start) { EditingStart = start; Focus(); Invalidate(); }
        internal void Reset(int maximum)
        {
            if (maximum < 0) throw new ArgumentOutOfRangeException("maximum");
            dragging = false; Capture = false; Maximum = maximum; EditingStart = true; SetRange(0, maximum);
        }
        internal void SetRange(int start, int end)
        {
            if (start < 0 || end < start || end > Maximum) throw new ArgumentOutOfRangeException("start");
            first = start; last = end; firstInput.RefreshValue(); lastInput.RefreshValue(); Invalidate(); if (RangeChanged != null) RangeChanged(EditingStart ? first : last);
        }
        internal void AdjustBoundary(int delta)
        { if (EditingStart) SetRange(Math.Max(0, Math.Min(last, first + delta)), last); else SetRange(first, Math.Max(first, Math.Min(Maximum, last + delta))); }
        private float Pixel(int value) { return 18 + Math.Max(0, Width - 36) * (Maximum == 0 ? 0 : value / (float)Maximum); }
        private int Frame(int x) { return Maximum == 0 ? 0 : Math.Max(0, Math.Min(Maximum, (int)Math.Round((x - 18) * Maximum / (double)Math.Max(1, Width - 36)))); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (!Enabled || e.Button != MouseButtons.Left) return;
            EditingStart = Math.Abs(e.X - Pixel(first)) < Math.Abs(e.X - Pixel(last)) || (first == last && e.X <= Pixel(first));
            Focus(); dragging = true; Capture = true; MoveHandle(e.X);
        }
        private void MoveHandle(int x)
        { int at = Frame(x); if (EditingStart) SetRange(Math.Min(at, last), last); else SetRange(first, Math.Max(at, first)); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool start = Math.Abs(e.X - Pixel(first)) <= 14 && Math.Abs(e.Y - SliderAppearance.TrackY(Height)) <= 14;
            bool end = Math.Abs(e.X - Pixel(last)) <= 14 && Math.Abs(e.Y - SliderAppearance.TrackY(Height)) <= 14;
            if (start != hoverStart || end != hoverEnd) { hoverStart = start; hoverEnd = end; Invalidate(); }
            if (dragging && Enabled) MoveHandle(e.X);
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverStart = hoverEnd = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); if (!Enabled) { dragging = false; Capture = false; } Invalidate(); }
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
            float y = SliderAppearance.TrackY(Height), a = Pixel(first), b = Pixel(last);
            var color = ToolAppearance.Accent("gif");
            SliderAppearance.Track(g, 18, Math.Max(18, Width - 18), y, a, b, color, Enabled);
            if (b - a < 115)
            {
                float middle = (a + b) / 2;
                if (middle > 130 && middle < Width - 130) SliderAppearance.Bubble(g, "起 " + (first + 1) + " · 止 " + (last + 1), middle, y, Width, color, Enabled);
            }
            else
            {
                if (a > 110 && a < Width - 110) SliderAppearance.Bubble(g, "起 " + (first + 1), a, y, Width, color, Enabled);
                if (b > 110 && b < Width - 110) SliderAppearance.Bubble(g, "止 " + (last + 1), b, y, Width, color, Enabled);
            }
            SliderAppearance.Thumb(g, a, y, color, Enabled, hoverStart, Focused && EditingStart, dragging && EditingStart);
            SliderAppearance.Thumb(g, b, y, color, Enabled, hoverEnd, Focused && !EditingStart, dragging && !EditingStart);
            });
        }
    }
}
