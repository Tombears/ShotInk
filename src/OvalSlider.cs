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
        private Panel expandedPanel;
        private OvalSlider expandedSlider;
        private Action collapseExpansion;
        private bool expansionCloseQueued;
        private float expandedTrackLeft = -1, expandedTrackRight = -1;
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
        private float TrackLeft { get { return expandedTrackLeft >= 0 ? expandedTrackLeft : Math.Min(18, Width / 2F); } }
        private float TrackRight { get { return expandedTrackRight >= 0 ? expandedTrackRight : Math.Max(TrackLeft, Width - 56); } }
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
            if (expandedSlider != null && expandedSlider.Value != next) expandedSlider.Value = next;
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
            if (collapseExpansion == null && Width < 210 && BeginExpandedDrag(e)) return;
            Focus();
            var thumb = ThumbBounds;
            grabOffset = thumb.Contains(e.Location) ? e.X - (thumb.Left + thumb.Width / 2) : 0;
            dragging = true; Capture = true;
            SetFromPointer(e.X - grabOffset); Invalidate();
        }
        private bool BeginExpandedDrag(MouseEventArgs e)
        {
            var form = FindForm();
            if (form == null || form.ClientSize.Width < 240) return false;
            if (!ThumbBounds.Contains(e.Location)) SetFromPointer(e.X);
            var pointer = PointToScreen(e.Location);
            var anchor = form.PointToClient(pointer);
            int width = Math.Min(320, form.ClientSize.Width - 16);
            expandedSlider = new OvalSlider { Minimum = minimum, Maximum = maximum, Value = value, AccentColor = AccentColor,
                SmallChange = SmallChange, LargeChange = LargeChange, Size = new Size(width - 12, SliderAppearance.Height), Location = new Point(6, 6), AccessibleName = AccessibleName };
            expandedSlider.collapseExpansion = CloseExpansion;
            var thumb = expandedSlider.ThumbBounds;
            int left = (int)Math.Round(anchor.X - 6 - thumb.Left - thumb.Width / 2);
            int top = (int)Math.Round(anchor.Y - 6 - SliderAppearance.TrackY(expandedSlider.Height));
            var panel = new ExpandedSliderPanel { Size = new Size(width, SliderAppearance.Height + 12) };
            panel.Location = new Point(Math.Max(8, Math.Min(form.ClientSize.Width - width - 8, left)),
                Math.Max(8, Math.Min(form.ClientSize.Height - panel.Height - 8, top)));
            expandedPanel = panel; panel.Controls.Add(expandedSlider);
            var liveSlider = expandedSlider;
            expandedSlider.ValueChanged += delegate { Value = liveSlider.Value; };
            expandedSlider.MouseUp += delegate { QueueCloseExpansion(); };
            expandedSlider.MouseCaptureChanged += delegate { if (expandedSlider != null && !expandedSlider.Capture) QueueCloseExpansion(); };
            form.Controls.Add(panel); panel.BringToFront(); expandedSlider.Focus();
            var local = expandedSlider.PointToClient(pointer);
            // Keep the current value under the pointer when the window edge limits placement.
            double ratio = maximum == minimum ? 0 : (value - (double)minimum) / (maximum - (double)minimum);
            double span = expandedSlider.TrackRight - expandedSlider.TrackLeft;
            if (ratio > 0) span = Math.Min(span, (local.X - 9) / ratio);
            if (ratio < 1) span = Math.Min(span, (expandedSlider.Width - 46 - local.X) / (1 - ratio));
            span = Math.Max(1, span);
            expandedSlider.expandedTrackLeft = (float)(local.X - span * ratio);
            expandedSlider.expandedTrackRight = (float)(local.X + span * (1 - ratio));
            expandedSlider.grabOffset = 0;
            expandedSlider.dragging = true; expandedSlider.Capture = true; expandedSlider.Invalidate();
            return true;
        }
        private void QueueCloseExpansion()
        {
            if (expandedPanel == null || expansionCloseQueued || IsDisposed || Disposing) return;
            expansionCloseQueued = true;
            BeginInvoke(new Action(delegate { expansionCloseQueued = false; CloseExpansion(); }));
        }
        private void CloseExpansion()
        {
            var panel = expandedPanel; expandedPanel = null; expandedSlider = null;
            if (panel != null)
            {
                var parent = panel.Parent; var bounds = panel.Bounds;
                if (parent != null) parent.Controls.Remove(panel);
                panel.Dispose();
                if (parent != null && !parent.IsDisposed) parent.Invalidate(bounds, true);
            }
            if (!IsDisposed && !Disposing) { Focus(); Invalidate(); }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (collapseExpansion != null)
            {
                if (keyData == Keys.Escape) { collapseExpansion(); return true; }
                if (IsInputKey(keyData)) { OnKeyDown(new KeyEventArgs(keyData)); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && expandedPanel != null) CloseExpansion();
            base.Dispose(disposing);
        }
        private sealed class ExpandedSliderPanel : Panel
        {
            internal ExpandedSliderPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                ToolAppearance.DrawSmooth(e.Graphics, ClientSize, delegate(Graphics g)
                {
                    using (var path = ToolAppearance.Rounded(new RectangleF(0.5F, 0.5F, Width - 1, Height - 1), 8))
                    using (var fill = new SolidBrush(Color.White))
                    using (var border = new Pen(ToolAppearance.Border)) { g.FillPath(fill, path); g.DrawPath(border, path); }
                });
            }
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
