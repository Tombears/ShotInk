using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ShotInk
{
    internal static class FreeSelectionGeometry
    {
        internal static GraphicsPath Path(PointF[] outline, RectangleF bounds)
        {
            var points = new PointF[outline.Length];
            for (int i = 0; i < points.Length; i++) points[i] = new PointF(bounds.X + outline[i].X * bounds.Width, bounds.Y + outline[i].Y * bounds.Height);
            var path = new GraphicsPath(FillMode.Alternate); path.AddPolygon(points); return path;
        }
        internal static bool TryCreate(List<PointF> points, out Rectangle bounds, out PointF[] outline)
        {
            bounds = Rectangle.Empty; outline = null;
            if (points.Count < 3) return false;
            using (var path = new GraphicsPath(FillMode.Alternate))
            {
                path.AddPolygon(points.ToArray());
                var extent = path.GetBounds();
                bounds = Rectangle.FromLTRB((int)Math.Floor(extent.Left), (int)Math.Floor(extent.Top), (int)Math.Ceiling(extent.Right), (int)Math.Ceiling(extent.Bottom));
                if (bounds.Width < 8 || bounds.Height < 8) return false;
                double area = 0;
                using (var region = new Region(path)) using (var matrix = new Matrix())
                    foreach (var scan in region.GetRegionScans(matrix)) area += scan.Width * scan.Height;
                if (area < 64) return false;
            }
            var frame = bounds;
            outline = points.ConvertAll(p => new PointF((p.X - frame.X) / frame.Width, (p.Y - frame.Y) / frame.Height)).ToArray();
            return true;
        }
        internal static void Mask(Bitmap image, PointF[] outline)
        {
            using (var mask = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(mask)) using (var path = Path(outline, new Rectangle(Point.Empty, image.Size)))
                { g.SmoothingMode = SmoothingMode.AntiAlias; g.FillPath(Brushes.White, path); }
                var bounds = new Rectangle(Point.Empty, image.Size);
                var pixels = image.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                try
                {
                    var coverage = mask.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        var row = new byte[image.Width * 4]; var alpha = new byte[row.Length];
                        for (int y = 0; y < image.Height; y++)
                        {
                            var at = IntPtr.Add(pixels.Scan0, y * pixels.Stride);
                            Marshal.Copy(at, row, 0, row.Length); Marshal.Copy(IntPtr.Add(coverage.Scan0, y * coverage.Stride), alpha, 0, alpha.Length);
                            for (int x = 0; x < row.Length; x += 4)
                            {
                                row[x + 3] = (byte)((row[x + 3] * alpha[x + 3] + 127) / 255);
                                if (row[x + 3] == 0) row[x] = row[x + 1] = row[x + 2] = 0;
                            }
                            Marshal.Copy(row, 0, at, row.Length);
                        }
                    }
                    finally { mask.UnlockBits(coverage); }
                }
                finally { image.UnlockBits(pixels); }
            }
        }
    }

    internal sealed partial class AnnotationCanvas
    {
        private PointF[] captureOutline;
        internal void SetCaptureOutline(PointF[] outline)
        { captureOutline = outline == null ? null : (PointF[])outline.Clone(); UpdateCaptureRegion(); Invalidate(); }
        private void UpdateCaptureRegion()
        {
            Region next = null;
            if (captureOutline != null && Width > 0 && Height > 0)
                using (var path = FreeSelectionGeometry.Path(captureOutline, ClientRectangle)) next = new Region(path);
            var previous = Region; Region = next; if (previous != null) previous.Dispose();
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if (captureOutline != null) UpdateCaptureRegion(); }
    }
}
