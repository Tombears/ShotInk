using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class RoundedInputFrame : Panel
    {
        private readonly Control input;
        internal Color AccentColor;
        internal RoundedInputFrame(Control input, Color accent)
        {
            this.input = input; AccentColor = accent;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Bounds = input.Bounds; Margin = input.Margin; Dock = input.Dock; Anchor = input.Anchor; TabIndex = input.TabIndex;
            BackColor = Color.Transparent; TabStop = false;
            var text = input as TextBoxBase; if (text != null) text.BorderStyle = BorderStyle.None;
            var number = input as UpDownBase; if (number != null) number.BorderStyle = BorderStyle.None;
            input.Dock = DockStyle.None; input.Anchor = AnchorStyles.Top | AnchorStyles.Left; input.Margin = Padding.Empty;
            input.Enter += delegate { Invalidate(); }; input.Leave += delegate { Invalidate(); };
            input.EnabledChanged += delegate { Invalidate(); };
            Controls.Add(input); PositionInput();
        }
        private void PositionInput()
        {
            if (input == null) return;
            int height = Math.Min(Math.Max(1, Height - 4), input.PreferredSize.Height);
            input.Bounds = new Rectangle(5, Math.Max(2, (Height - height) / 2), Math.Max(1, Width - 10), height);
        }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); PositionInput(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ToolAppearance.DrawSmooth(e.Graphics, ClientSize, delegate(Graphics g)
            {
                using (var path = ToolAppearance.Rounded(new RectangleF(0.5F, 0.5F, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 6))
                using (var fill = new SolidBrush(input.BackColor))
                using (var border = new Pen(ContainsFocus ? AccentColor : ToolAppearance.Border, ContainsFocus ? 1.5F : 1))
                { g.FillPath(fill, path); g.DrawPath(border, path); }
            });
        }
    }
}
