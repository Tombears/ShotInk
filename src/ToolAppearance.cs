using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ShotInk
{
    internal static class ToolAppearance
    {
        internal static Color Ink { get { return Skins.Current.Ink; } }
        internal static Color Muted { get { return Skins.Current.Muted; } }
        internal static Color Border { get { return Skins.Current.Border; } }
        internal static readonly Font Font = new Font("Microsoft YaHei UI", 9F);
        internal static readonly Font HeadingFont = new Font(Font, FontStyle.Bold);
        internal static Bitmap RenderSmooth(Size size, Action<Graphics> paint)
        {
            const int scale = 4;
            using (var large = new Bitmap(size.Width * scale, size.Height * scale, PixelFormat.Format32bppPArgb))
            {
                using (var graphics = Graphics.FromImage(large))
                {
                    graphics.Clear(Color.Transparent); graphics.ScaleTransform(scale, scale);
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    paint(graphics);
                }
                var image = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                using (var graphics = Graphics.FromImage(image))
                using (var attributes = new ImageAttributes())
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(large, new Rectangle(Point.Empty, size), 0, 0, large.Width, large.Height, GraphicsUnit.Pixel, attributes);
                }
                return image;
            }
        }
        internal static void DrawSmooth(Graphics target, Size size, Action<Graphics> paint)
        {
            if (size.Width < 1 || size.Height < 1) return;
            using (var image = RenderSmooth(size, paint)) target.DrawImageUnscaled(image, Point.Empty);
        }

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
            return SkinIcons.Accent(name, ClassicAccent(name));
        }
        private static Color ClassicAccent(string name)
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
                case "video": return Color.FromArgb(211, 71, 87);
                case "gif": return Color.FromArgb(209, 67, 112);
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
        internal string IconName;
        private Bitmap glyph;
        private Color glyphColor;
        private readonly SkinInteraction interaction;
        private SkinKind glyphSkin;
        internal ToolOptionButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand; Font = ToolAppearance.Font; BackColor = Skins.Current.Surface;
            interaction = new SkinInteraction(this);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var background = Parent == null ? Skins.Current.Surface : Parent.BackColor;
            if (background.A == 0) background = Skins.Current.Surface;
            e.Graphics.Clear(background);
            ToolAppearance.DrawSmooth(e.Graphics, ClientSize, delegate(Graphics g)
            {
            if (IsSwatch)
            {
                var circle = new RectangleF(Width / 2F - 7, Height / 2F - 7, 14, 14);
                using (var fill = new SolidBrush(BackColor)) g.FillEllipse(fill, circle);
                using (var border = new Pen(Color.FromArgb(180, 187, 198))) g.DrawEllipse(border, circle);
                if (Selected || interaction.Hot)
                {
                    circle.Inflate(3, 3);
                    using (var ring = new Pen(Selected ? AccentColor : ToolAppearance.Border, Selected ? 2 : 1)) g.DrawEllipse(ring, circle);
                }
            }
            else
            {
                Color accent = Enabled ? AccentColor : ToolAppearance.Muted;
                SkinPainter.Button(g, new RectangleF(.5F, .5F, Width - 1, Height - 1), accent, Selected,
                    Enabled && interaction.Hot, Enabled && interaction.Pressed, Focused && ShowFocusCues, interaction.Pulse);
            }
            });
            if (IconName != null)
            {
                var color = Enabled ? AccentColor : ToolAppearance.Muted;
                if (glyph == null || glyphColor != color || glyphSkin != Skins.Current.Kind)
                {
                    if (glyph != null) glyph.Dispose();
                    glyph = ToolbarIcons.Create(IconName, color); glyphColor = color; glyphSkin = Skins.Current.Kind;
                }
                e.Graphics.DrawImageUnscaled(glyph, (Width - glyph.Width) / 2, (Height - glyph.Height) / 2 + SkinPainter.PressOffset(interaction.Pressed));
            }
            else if (!IsSwatch)
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, !Enabled ? ToolAppearance.Muted : Selected ? Skins.Accent(AccentColor) : ToolAppearance.Ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
            { var focus = ClientRectangle; focus.Inflate(-4, -4); ControlPaint.DrawFocusRectangle(e.Graphics, focus); }
        }
        protected override void Dispose(bool disposing) { if (disposing && glyph != null) glyph.Dispose(); base.Dispose(disposing); }
    }
}
