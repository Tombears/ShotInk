using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShotInk
{
    internal static class ToolAppearance
    {
        internal static readonly Color Ink = Color.FromArgb(49, 57, 72);
        internal static readonly Color Muted = Color.FromArgb(112, 121, 137);
        internal static readonly Color Border = Color.FromArgb(226, 230, 237);
        internal static readonly Font Font = new Font("Microsoft YaHei UI", 9F);
        internal static readonly Font HeadingFont = new Font(Font, FontStyle.Bold);

        internal static Color Accent(DrawTool tool)
        {
            switch (tool)
            {
                case DrawTool.Rectangle: case DrawTool.Ellipse: return Accent("shape");
                case DrawTool.Arrow: case DrawTool.Line: case DrawTool.Polyline: case DrawTool.ArrowPolyline: return Accent("arrow");
                case DrawTool.Pen: return Accent("pen");
                case DrawTool.Highlight: return Accent("highlight");
                case DrawTool.Mosaic: return Accent("mosaic");
                case DrawTool.Text: return Accent("text");
                case DrawTool.Eraser: return Accent("eraser");
                default: return Accent("move");
            }
        }
        internal static Color Accent(string name)
        {
            switch (name)
            {
                case "shape": case "copy": return Color.FromArgb(47, 111, 220);
                case "arrow": return Color.FromArgb(194, 101, 29);
                case "pen": return Color.FromArgb(211, 71, 87);
                case "highlight": return Color.FromArgb(157, 117, 12);
                case "mosaic": case "pin": return Color.FromArgb(124, 79, 202);
                case "text": return Color.FromArgb(21, 134, 103);
                case "eraser": return Color.FromArgb(190, 67, 140);
                case "ocr": return Color.FromArgb(8, 130, 161);
                case "scroll": return Color.FromArgb(87, 93, 206);
                case "save": return Color.FromArgb(32, 137, 92);
                case "cancel": return Color.FromArgb(209, 67, 67);
                default: return Color.FromArgb(89, 108, 133);
            }
        }
        internal static Color Tint(Color color, float white)
        { return Color.FromArgb((int)(color.R + (255 - color.R) * white), (int)(color.G + (255 - color.G) * white), (int)(color.B + (255 - color.B) * white)); }

        internal static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            float diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure(); return path;
        }
    }

    internal sealed class ToolOptionButton : Button
    {
        internal Color AccentColor = ToolAppearance.Accent(DrawTool.None);
        internal bool Selected;
        internal bool IsSwatch;
        private bool hovering;
        internal ToolOptionButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand; Font = ToolAppearance.Font; BackColor = Color.White;
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovering = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovering = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent == null ? Color.White : Parent.BackColor);
            if (IsSwatch)
            {
                var circle = new RectangleF(Width / 2F - 8, Height / 2F - 8, 16, 16);
                using (var fill = new SolidBrush(BackColor)) g.FillEllipse(fill, circle);
                using (var border = new Pen(Color.FromArgb(180, 187, 198))) g.DrawEllipse(border, circle);
                if (Selected || hovering)
                {
                    circle.Inflate(4, 4);
                    using (var ring = new Pen(Selected ? AccentColor : ToolAppearance.Border, Selected ? 2 : 1)) g.DrawEllipse(ring, circle);
                }
            }
            else
            {
                Color accent = Enabled ? AccentColor : ToolAppearance.Muted;
                using (var path = ToolAppearance.Rounded(new RectangleF(0.5F, 0.5F, Width - 1, Height - 1), 6))
                using (var fill = new SolidBrush(Selected ? ToolAppearance.Tint(accent, 0.90F) : hovering ? Color.FromArgb(246, 248, 251) : Color.White))
                using (var border = new Pen(Selected ? accent : ToolAppearance.Border))
                { g.FillPath(fill, path); g.DrawPath(border, path); }
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, !Enabled ? SystemColors.GrayText : Selected ? accent : ToolAppearance.Ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            if (Focused && ShowFocusCues)
            { var focus = ClientRectangle; focus.Inflate(-4, -4); ControlPaint.DrawFocusRectangle(g, focus); }
        }
    }
}
