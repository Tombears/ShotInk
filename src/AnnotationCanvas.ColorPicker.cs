using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed partial class AnnotationCanvas
    {
        private ColorPickerPreview colorPreview;
        private ToolCursors.NativeCursor samplingCursor;
        private SkinKind samplingCursorSkin;
        private Color samplingCursorColor = Color.FromArgb(91, 127, 242);
        private string copiedColors = "";
        internal event Action ColorPickerCancelled;

        private void SetSamplingCursor(Color color)
        {
            var skin = Skins.Current.Kind;
            if (samplingCursor != null && samplingCursorSkin == skin && samplingCursorColor.ToArgb() == color.ToArgb()) return;
            var previous = samplingCursor;
            samplingCursor = ToolCursors.CreateSamplingPen(color);
            samplingCursorColor = color;
            samplingCursorSkin = skin;
            Cursor = samplingCursor.View;
            if (previous != null) previous.Dispose();
        }

        private void DisposeSamplingCursor()
        {
            if (samplingCursor == null) return;
            if (ReferenceEquals(Cursor, samplingCursor.View)) Cursor = Cursors.Default;
            samplingCursor.Dispose();
            samplingCursor = null;
        }

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
            SetSamplingCursor(color);
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
        private const int LensSize = 108;
        private const int CapsuleWidth = 108;
        private const int WidthAt96Dpi = 120;
        private const int HeightAt96Dpi = 150;
        private const int LensLeft = (WidthAt96Dpi - LensSize) / 2;
        private static readonly Font HexFont = new Font("Consolas", 13, FontStyle.Bold, GraphicsUnit.Pixel);
        private static readonly Font StatusFont = new Font("Microsoft YaHei UI", 12, GraphicsUnit.Pixel);
        internal readonly Bitmap Pixels = new Bitmap(SampleSize, SampleSize);
        private float scale = 1;
        private Color color;
        private string hex = "";
        private string feedback = "";

        internal ColorPickerPreview()
        {
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
        }

        internal void Present(Form owner, Point pointer, int dpi, Color color, string hex, string feedback)
        {
            this.color = color;
            this.hex = hex;
            this.feedback = feedback;
            scale = dpi / 96F;
            int width = (int)Math.Ceiling(WidthAt96Dpi * scale);
            int height = (int)Math.Ceiling(HeightAt96Dpi * scale);
            int gap = (int)Math.Ceiling(29 * scale);
            var work = Screen.FromPoint(pointer).Bounds;
            int x = pointer.X + gap + width <= work.Right ? pointer.X + gap : pointer.X - gap - width;
            int y = pointer.Y - (int)(scale * (LensSize / 2 + 5));
            var location = new Point(Math.Max(work.Left, Math.Min(x, work.Right - width)),
                Math.Max(work.Top, Math.Min(y, work.Bottom - height)));
            if (!Visible)
            {
                Bounds = new Rectangle(location, new Size(width, height));
                Show(owner);
            }
            AccessibleName = hex;
            AccessibleDescription = feedback;
            using (var preview = RenderPreview())
                if (!Native.PresentLayer(Handle, preview, location)) Hide();
        }

        internal Bitmap RenderPreview()
        {
            var bitmap = new Bitmap((int)Math.Ceiling(WidthAt96Dpi * scale), (int)Math.Ceiling(HeightAt96Dpi * scale),
                PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.ScaleTransform(scale, scale);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                var circle = new Rectangle(LensLeft, 4, LensSize, LensSize);
                var image = Rectangle.Inflate(circle, -4, -4);
                using (var face = new SolidBrush(Color.FromArgb(32, 44, 65))) g.FillEllipse(face, circle);
                var state = g.Save();
                using (var clip = new GraphicsPath())
                {
                    clip.AddEllipse(image);
                    g.SetClip(clip);
                    g.FillEllipse(SystemBrushes.ControlDark, image);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(Pixels, image, 0, 0, SampleSize, SampleSize, GraphicsUnit.Pixel);
                }
                g.Restore(state);
                using (var rim = new Pen(Color.FromArgb(237, 243, 250))) g.DrawEllipse(rim, circle);
                DrawSamplingMark(g, image);

                var capsule = new RectangleF((WidthAt96Dpi - CapsuleWidth) / 2, 116, CapsuleWidth, 28);
                using (var path = ToolAppearance.Rounded(capsule, 14))
                using (var fill = new SolidBrush(Color.FromArgb(249, 251, 255)))
                using (var border = new Pen(Color.FromArgb(171, 183, 201)))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);
                }
                using (var fill = new SolidBrush(color)) g.FillEllipse(fill, capsule.Left + 12, 125, 10, 10);
                using (var border = new Pen(Color.FromArgb(128, 46, 58, 76))) g.DrawEllipse(border, capsule.Left + 12, 125, 10, 10);
                bool failed = feedback == "复制失败";
                using (var ink = new SolidBrush(failed ? Color.Firebrick : Color.FromArgb(22, 33, 53)))
                    g.DrawString(failed ? feedback : hex, failed ? StatusFont : HexFont, ink, capsule.Left + 27, 123);
                if (!failed && feedback.Length > 0)
                    using (var pen = new Pen(Color.FromArgb(27, 116, 84), 1.5F))
                        g.DrawLines(pen, new[] { new PointF(98, 130), new PointF(101, 133), new PointF(106, 127) });
            }
            return bitmap;
        }

        private static void DrawSamplingMark(Graphics g, Rectangle image)
        {
            float cell = (float)image.Width / SampleSize;
            float x = image.Left + Radius * cell + .6F, y = image.Top + Radius * cell + .6F;
            float size = cell - 1.2F;
            g.SmoothingMode = SmoothingMode.None;
            using (var outside = new Pen(Color.FromArgb(20, 29, 46), 2.5F)) g.DrawRectangle(outside, x, y, size, size);
            using (var inside = new Pen(Color.White)) g.DrawRectangle(inside, x, y, size, size);
            g.SmoothingMode = SmoothingMode.AntiAlias;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var value = base.CreateParams;
                value.ExStyle |= 0x08000000 | 0x80 | 0x80000 | 0x20; // NOACTIVATE, TOOLWINDOW, LAYERED, TRANSPARENT
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
