using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed partial class AnnotationCanvas
    {
        private static readonly Lazy<Cursor> ColorPickerCursor = new Lazy<Cursor>(delegate
        {
            using (var stream = typeof(AnnotationCanvas).Assembly.GetManifestResourceStream("ShotInk.ColorPickerCursor"))
                return new Cursor(stream);
        });
        private ColorPickerPreview colorPreview;
        private string copiedColors = "";
        internal event Action ColorPickerCancelled;

        internal bool CancelColorPicker()
        {
            if (Tool != DrawTool.ColorPicker) return false;
            if (ColorPickerCancelled != null) ColorPickerCancelled();
            else Tool = DrawTool.None;
            return true;
        }

        private void HideColorPreview()
        {
            if (colorPreview != null) colorPreview.Hide();
        }

        private void PreviewColorAt(Point location, bool copy, bool append = false)
        {
            var visible = documentScroll == null ? ClientRectangle : new Rectangle(Point.Empty, documentClient);
            if (!visible.Contains(location) || (Region != null && !Region.IsVisible(location)))
            {
                HideColorPreview();
                return;
            }

            var point = Point.Truncate(ImagePoint(location));
            var owner = FindForm();
            if (owner == null || !owner.Visible) return;
            if (colorPreview == null) colorPreview = new ColorPickerPreview();
            RenderColorSample(colorPreview.Pixels, point);
            var color = colorPreview.Pixels.GetPixel(ColorPickerPreview.Radius, ColorPickerPreview.Radius);
            string hex = string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
            string feedback = "";
            if (copy)
            {
                string text = append && copiedColors.Length > 0 ? copiedColors + Environment.NewLine + hex : hex;
                try
                {
                    Clipboard.SetDataObject(text, true, 5, 50);
                    copiedColors = text;
                    feedback = append ? "已追加" : "已复制";
                }
                catch (ExternalException) { feedback = "复制失败"; }
            }
            colorPreview.Present(owner, PointToScreen(location), DeviceDpi, color, hex, feedback);
        }

        private void RenderColorSample(Bitmap sample, Point center)
        {
            int radius = ColorPickerPreview.Radius;
            var area = new Rectangle(center.X - radius, center.Y - radius, sample.Width, sample.Height);
            var source = Rectangle.Intersect(ImageBounds, area);
            using (var g = Graphics.FromImage(sample))
            {
                g.Clear(Color.Transparent);
                g.TranslateTransform(-area.Left, -area.Top);
                g.SetClip(source);
                if (captureOutline != null)
                    using (var path = FreeSelectionGeometry.Path(captureOutline, ImageBounds)) g.SetClip(path, CombineMode.Intersect);
                using (var background = new SolidBrush(BackColor)) g.FillRectangle(background, source);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(Image, source, source, GraphicsUnit.Pixel);
                g.PixelOffsetMode = PixelOffsetMode.Default;
                DrawAnnotations(g);
            }
        }

        protected override void OnLostFocus(EventArgs e)
        {
            HideColorPreview();
            base.OnLostFocus(e);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            if (!Visible) HideColorPreview();
            base.OnVisibleChanged(e);
        }
    }

    internal sealed class ColorPickerPreview : Form
    {
        internal const int Radius = 5;
        private const int SampleSize = Radius * 2 + 1;
        internal readonly Bitmap Pixels = new Bitmap(SampleSize, SampleSize);
        private int cell = 12;
        private int padding = 6;
        private int row = 23;
        private Color color;
        private string hex = "";
        private string feedback = "";

        internal ColorPickerPreview()
        {
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            DoubleBuffered = true;
        }

        internal void Present(Form owner, Point pointer, int dpi, Color color, string hex, string feedback)
        {
            this.color = color;
            this.hex = hex;
            this.feedback = feedback;
            cell = Math.Max(8, 12 * dpi / 96);
            padding = Math.Max(4, 6 * dpi / 96);
            row = Math.Max(19, 23 * dpi / 96);
            int width = cell * SampleSize + 2 * padding;
            var work = Screen.FromPoint(pointer).Bounds;
            int height = width + row;
            int gap = 3 * padding;
            int x = pointer.X + gap + width <= work.Right ? pointer.X + gap : pointer.X - gap - width;
            int y = pointer.Y + gap + height <= work.Bottom ? pointer.Y + gap : pointer.Y - gap - height;
            Bounds = new Rectangle(Math.Max(work.Left, Math.Min(x, work.Right - width)),
                Math.Max(work.Top, Math.Min(y, work.Bottom - height)), width, height);
            if (!Visible) Show(owner);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var skin = Skins.Current;
            g.Clear(skin.Surface);
            using (var border = new Pen(skin.Border)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            var image = new Rectangle(padding, padding, cell * SampleSize, cell * SampleSize);
            g.FillRectangle(SystemBrushes.ControlDark, image);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(Pixels, image, 0, 0, SampleSize, SampleSize, GraphicsUnit.Pixel);
            g.PixelOffsetMode = PixelOffsetMode.Default;
            int start = padding + Radius * cell;
            g.DrawRectangle(Pens.Black, start, start, cell - 1, cell - 1);
            g.DrawRectangle(Pens.White, start + 1, start + 1, cell - 3, cell - 3);

            int top = image.Bottom + padding;
            var swatch = new Rectangle(padding, top + (row - cell) / 2, cell, cell);
            using (var fill = new SolidBrush(color)) g.FillRectangle(fill, swatch);
            using (var border = new Pen(skin.Border)) g.DrawRectangle(border, swatch);
            var text = new Rectangle(swatch.Right + padding, top, image.Right - swatch.Right - padding, row);
            TextRenderer.DrawText(g, hex, ToolAppearance.Font, text, skin.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, feedback, ToolAppearance.Font, text, skin.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var value = base.CreateParams;
                value.ExStyle |= 0x08000000 | 0x80; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                return value;
            }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84) { message.Result = new IntPtr(-1); return; } // WM_NCHITTEST / HTTRANSPARENT
            base.WndProc(ref message);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Pixels.Dispose();
            base.Dispose(disposing);
        }
    }
}
