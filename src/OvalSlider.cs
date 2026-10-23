using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class OvalSlider : Control
    {
        private int minimum;
        private int maximum = 100;
        private int value;
        private bool dragging;
        private bool hovering;
        private float grabOffset;
        internal event EventHandler ValueChanged;
        internal int SmallChange = 1;
        internal int LargeChange = 4;
        internal Color AccentColor = Color.FromArgb(91, 155, 235);

        internal int Minimum
        {
            get { return minimum; }
            set { minimum = value; maximum = Math.Max(maximum, minimum); SetValue(this.value); Invalidate(); }
        }
        internal int Maximum
        {
            get { return maximum; }
            set { maximum = value; minimum = Math.Min(minimum, maximum); SetValue(this.value); Invalidate(); }
        }
        internal int Value
        {
            get { return value; }
            set
            {
                if (value < minimum || value > maximum) throw new ArgumentOutOfRangeException("value");
                SetValue(value);
            }
        }
        private float TrackLeft { get { return Math.Min(14, Width / 2F); } }
        private float TrackRight { get { return Math.Max(TrackLeft, Width - 14); } }
        internal RectangleF ThumbBounds
        {
            get
            {
                float ratio = maximum == minimum ? 0 : (float)((double)(value - minimum) / ((double)maximum - minimum));
                return new RectangleF(TrackLeft + (TrackRight - TrackLeft) * ratio - 11, Height / 2F - 8, 22, 16);
            }
        }

        internal OvalSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            Size = new Size(216, 27);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.Slider;
        }
        private void SetValue(int next)
        {
            next = Math.Max(minimum, Math.Min(maximum, next));
            if (value == next) return;
            value = next; Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        }
        private void SetFromPointer(float x)
        {
            double ratio = TrackRight <= TrackLeft ? 0 : Math.Max(0, Math.Min(1, (x - TrackLeft) / (TrackRight - TrackLeft)));
            SetValue((int)Math.Round(minimum + ((double)maximum - minimum) * ratio));
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled || e.Button != MouseButtons.Left) return;
            Focus();
            var thumb = ThumbBounds;
            grabOffset = thumb.Contains(e.Location) ? e.X - (thumb.Left + thumb.Width / 2) : 0;
            dragging = true; Capture = true;
            SetFromPointer(e.X - grabOffset); Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = ThumbBounds.Contains(e.Location);
            if (hover != hovering) { hovering = hover; Invalidate(); }
            if (dragging && Enabled) SetFromPointer(e.X - grabOffset);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging || e.Button != MouseButtons.Left) return;
            SetFromPointer(e.X - grabOffset); dragging = false; Capture = false; Invalidate();
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        { base.OnMouseCaptureChanged(e); if (!Capture) { dragging = false; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovering = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e)
        { base.OnEnabledChanged(e); if (!Enabled) { dragging = false; Capture = false; } Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown: return true;
                default: return base.IsInputKey(keyData);
            }
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled) return;
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: SetValue(value - SmallChange); break;
                case Keys.Right: case Keys.Up: SetValue(value + SmallChange); break;
                case Keys.PageDown: SetValue(value - LargeChange); break;
                case Keys.PageUp: SetValue(value + LargeChange); break;
                case Keys.Home: SetValue(minimum); break;
                case Keys.End: SetValue(maximum); break;
                default: return;
            }
            e.Handled = true; e.SuppressKeyPress = true;
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Enabled && e.Delta != 0) SetValue(value + Math.Sign(e.Delta) * SmallChange);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float y = Height / 2F;
            var thumb = ThumbBounds;
            using (var track = new Pen(Enabled ? Color.FromArgb(189, 199, 211) : Color.FromArgb(215, 218, 223), 4))
            {
                track.StartCap = LineCap.Round; track.EndCap = LineCap.Round;
                g.DrawLine(track, TrackLeft, y, TrackRight, y);
                if (Enabled && value > minimum)
                { track.Color = AccentColor; g.DrawLine(track, TrackLeft, y, thumb.Left + thumb.Width / 2, y); }
            }
            var shadow = thumb; shadow.Offset(0, dragging ? 1 : 2);
            using (var shade = new SolidBrush(Color.FromArgb(Enabled ? 48 : 20, 38, 56, 77))) g.FillEllipse(shade, shadow);
            Color top = Enabled ? Color.White : Color.FromArgb(244, 245, 247);
            Color bottom = !Enabled ? Color.FromArgb(212, 216, 221) : ToolAppearance.Tint(AccentColor, dragging ? 0.62F : 0.84F);
            using (var face = new LinearGradientBrush(thumb, top, bottom, LinearGradientMode.Vertical)) g.FillEllipse(face, thumb);
            using (var rim = new Pen(Enabled && (hovering || dragging || Focused) ? AccentColor : Color.FromArgb(145, 155, 169))) g.DrawEllipse(rim, thumb);
            var highlight = thumb; highlight.Inflate(-2, -2);
            using (var light = new Pen(Color.FromArgb(205, Color.White))) g.DrawArc(light, highlight, 190, 150);
            if (Focused && ShowFocusCues)
            { var focus = Rectangle.Ceiling(thumb); focus.Inflate(3, 3); ControlPaint.DrawFocusRectangle(g, focus); }
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new SliderAccessibility(this); }
        private sealed class SliderAccessibility : ControlAccessibleObject
        {
            private readonly OvalSlider slider;
            internal SliderAccessibility(OvalSlider slider) : base(slider) { this.slider = slider; }
            public override string Value
            {
                get { return slider.Value.ToString(CultureInfo.InvariantCulture); }
                set { int parsed; if (int.TryParse(value, out parsed)) slider.SetValue(parsed); }
            }
        }
    }
}
