using System;
using System.Drawing;
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
        private readonly SliderValueBox valueInput;
        private readonly RoundedInputFrame valueInputFrame;
        internal event EventHandler ValueChanged;
        internal int SmallChange = 1;
        internal int LargeChange = 4;
        private Color accentColor = Color.FromArgb(91, 155, 235);
        internal Color AccentColor
        {
            get { return accentColor; }
            set { accentColor = value; if (valueInputFrame != null) { valueInputFrame.AccentColor = value; valueInputFrame.Invalidate(); } Invalidate(); }
        }

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
        private float TrackLeft { get { return Math.Min(18, Width / 2F); } }
        private float TrackRight { get { return Math.Max(TrackLeft, Width - 56); } }
        internal RectangleF ThumbBounds
        {
            get
            {
                float ratio = maximum == minimum ? 0 : (float)((double)(value - minimum) / ((double)maximum - minimum));
                return new RectangleF(TrackLeft + (TrackRight - TrackLeft) * ratio - 9, SliderAppearance.TrackY(Height) - 9, 18, 18);
            }
        }

        internal OvalSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            Size = new Size(216, SliderAppearance.Height);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.Slider;
            valueInput = new SliderValueBox(delegate { return value; }, delegate { return minimum; }, delegate { return maximum; }, SetValue) { Name = "slider-value", AccessibleName = "实际数值" };
            valueInputFrame = new RoundedInputFrame(valueInput, AccentColor); Controls.Add(valueInputFrame); PositionInput();
        }
        private void PositionInput()
        { if (valueInputFrame != null) valueInputFrame.Location = new Point(Math.Max(0, Width - valueInputFrame.Width), (int)SliderAppearance.TrackY(Height) - valueInputFrame.Height / 2); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); PositionInput(); }
        private void SetValue(int next)
        {
            next = Math.Max(minimum, Math.Min(maximum, next));
            if (value == next) { if (valueInput != null) valueInput.RefreshValue(); return; }
            value = next; Invalidate();
            if (valueInput != null) valueInput.RefreshValue();
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
            ToolAppearance.DrawSmooth(e.Graphics, ClientSize, delegate(Graphics g)
            {
            float y = SliderAppearance.TrackY(Height);
            var thumb = ThumbBounds;
            float x = thumb.Left + thumb.Width / 2;
            SliderAppearance.Track(g, TrackLeft, TrackRight, y, TrackLeft, x, AccentColor, Enabled);
            SliderAppearance.Bubble(g, value.ToString(), x, y, Width - 50, AccentColor, Enabled);
            SliderAppearance.Thumb(g, x, y, AccentColor, Enabled, hovering, Focused, dragging);
            });
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
