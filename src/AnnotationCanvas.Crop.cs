using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed partial class AnnotationCanvas
    {
        // Keep annotations, mosaic samples and the source bitmap in their original coordinates.
        private Rectangle appliedCrop;
        private Rectangle cropPreview, cropDragStart;
        private Point cropAnchor;
        private float cropAnchorOffsetX, cropAnchorOffsetY;
        private int cropHandle = -1;
        internal bool IsCropping { get { return !cropPreview.IsEmpty; } }
        internal Rectangle ImageBounds { get { return appliedCrop.IsEmpty ? new Rectangle(Point.Empty, Image.Size) : appliedCrop; } }
        internal event Action ImageBoundsChanged;

        internal void BeginCrop()
        {
            if (documentScroll == null || IsCropping) return;
            Tool = Tool;
            Revision++; // Invalidate OCR requests started before entering crop mode.
            cropPreview = ImageBounds;
            Cursor = Cursors.Default;
            Focus();
            Invalidate();
        }

        internal void EndCrop(bool apply)
        {
            if (!IsCropping) return;
            var next = cropPreview;
            cropPreview = Rectangle.Empty;
            cropHandle = -1;
            Capture = false;
            if (apply && next != ImageBounds)
            {
                SaveUndo();
                SetCrop(next);
                NotifyChanged();
            }
            UpdateToolCursor();
        }

        private void SetCrop(Rectangle crop)
        {
            if (appliedCrop == crop) return;
            var previous = ImageBounds;
            appliedCrop = crop;
            UpdateDocumentViewport(DocumentOffsetX + previous.Left - ImageBounds.Left, DocumentOffsetY + previous.Top - ImageBounds.Top);
            if (ImageBoundsChanged != null) ImageBoundsChanged();
        }

        private Rectangle CropClientBounds(Rectangle bounds)
        {
            var image = ImageBounds;
            int left = (int)Math.Round((bounds.Left - image.Left - DocumentOffsetX) * Zoom);
            int top = (int)Math.Round((bounds.Top - image.Top - DocumentOffsetY) * Zoom);
            int right = (int)Math.Round((bounds.Right - image.Left - DocumentOffsetX) * Zoom);
            int bottom = (int)Math.Round((bounds.Bottom - image.Top - DocumentOffsetY) * Zoom);
            return Rectangle.FromLTRB(left, top, Math.Max(left, right - 1), Math.Max(top, bottom - 1));
        }

        private int CropHandleAt(Point point)
        {
            var frame = CropClientBounds(cropPreview);
            int radius = Math.Max(6, (int)Math.Round(6 * DeviceDpi / 96.0));
            frame.Inflate(radius, radius);
            if (!frame.Contains(point)) return -1;
            frame.Inflate(-radius, -radius);
            int[] distances = { Math.Abs(point.Y - frame.Top), Math.Abs(point.X - frame.Right),
                Math.Abs(point.Y - frame.Bottom), Math.Abs(point.X - frame.Left) };
            int handle = -1, nearest = radius + 1;
            for (int i = 0; i < distances.Length; i++)
                if (distances[i] < nearest) { nearest = distances[i]; handle = 2 * i + 1; }
            return handle;
        }

        private void CropMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            cropHandle = CropHandleAt(e.Location);
            if (cropHandle < 0) return;
            Focus();
            cropAnchor = e.Location;
            cropAnchorOffsetX = DocumentOffsetX;
            cropAnchorOffsetY = DocumentOffsetY;
            cropDragStart = cropPreview;
            Capture = true;
        }

        private void CropMouseMove(Point location)
        {
            int handle = cropHandle >= 0 ? cropHandle : CropHandleAt(location);
            Cursor = handle < 0 ? Cursors.Default : handle == 1 || handle == 5 ? Cursors.SizeNS : Cursors.SizeWE;
            if (cropHandle < 0) return;
            var delta = new Point((int)Math.Round((location.X - cropAnchor.X) / Zoom + DocumentOffsetX - cropAnchorOffsetX),
                (int)Math.Round((location.Y - cropAnchor.Y) / Zoom + DocumentOffsetY - cropAnchorOffsetY));
            var next = SelectionGeometry.Resize(cropDragStart, cropHandle, delta, ImageBounds, 1);
            if (next == cropPreview) return;
            cropPreview = next;
            Invalidate();
        }

        private void PaintCropPreview(Graphics graphics)
        {
            var frame = CropClientBounds(cropPreview);
            var image = CropClientBounds(ImageBounds);
            image.Width++; image.Height++;
            using (var shade = new Region(image))
            using (var fill = new SolidBrush(Color.FromArgb(155, 0, 0, 0)))
            {
                var interior = frame; interior.Width++; interior.Height++;
                shade.Exclude(interior);
                graphics.FillRegion(fill, shade);
            }
            using (var outline = new Pen(Color.FromArgb(175, Color.Black), 3))
            using (var accent = new SolidBrush(ToolAppearance.Accent("crop")))
            {
                graphics.DrawRectangle(outline, frame);
                graphics.DrawRectangle(Pens.White, frame);
                var points = SelectionGeometry.Handles(frame);
                for (int i = 1; i < points.Length; i += 2)
                {
                    bool horizontal = i == 1 || i == 5;
                    int halfWidth = horizontal ? Math.Min(9, frame.Width / 3) : 2;
                    int halfHeight = horizontal ? 2 : Math.Min(9, frame.Height / 3);
                    var grip = new Rectangle(points[i].X - halfWidth, points[i].Y - halfHeight, 2 * halfWidth + 1, 2 * halfHeight + 1);
                    graphics.FillRectangle(accent, grip);
                    graphics.DrawRectangle(Pens.White, grip);
                }
            }
        }
    }
}
