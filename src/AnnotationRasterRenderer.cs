using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ShotInk
{
    internal sealed partial class AnnotationRasterRenderer : IDisposable
    {
        private readonly Bitmap Image;
        private readonly Func<Mark, bool> retain;
        internal AnnotationRasterRenderer(Bitmap image, Func<Mark, bool> retain) { Image = image; this.retain = retain; }
        internal Bitmap Render(IEnumerable<Mark> marks)
        {
            var result = new Bitmap(Image.Width, Image.Height, PixelFormat.Format24bppRgb);
            try
            {
                using (var graphics = Graphics.FromImage(result))
                { graphics.DrawImageUnscaled(Image, 0, 0); graphics.SmoothingMode = SmoothingMode.AntiAlias; foreach (var mark in marks) DrawMark(graphics, mark); }
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        public void Dispose() { ClearMosaicCache(); }
        private static Region AppliedRegion(Mark mark)
        {
            if (!mark.Freehand) return new Region(mark.Bounds);
            var region = new Region(); region.MakeEmpty();
            try
            {
                float diameter = Math.Max(1, mark.BrushSize), radius = diameter / 2;
                using (var path = new GraphicsPath())
                {
                    for (int i = 0; i < mark.Points.Count; i++)
                    {
                        var point = mark.Points[i];
                        path.Reset(); path.AddEllipse(point.X - radius, point.Y - radius, diameter, diameter); region.Union(path);
                        if (i == 0) continue;
                        var previous = mark.Points[i - 1];
                        float dx = point.X - previous.X, dy = point.Y - previous.Y;
                        float length = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (length <= 0) continue;
                        float nx = -dy * radius / length, ny = dx * radius / length;
                        path.Reset();
                        path.AddPolygon(new[] { new PointF(previous.X + nx, previous.Y + ny), new PointF(point.X + nx, point.Y + ny),
                            new PointF(point.X - nx, point.Y - ny), new PointF(previous.X - nx, previous.Y - ny) });
                        // Union the swept area so overlapping strokes never cancel coverage.
                        region.Union(path);
                    }
                }
                return region;
            }
            catch { region.Dispose(); throw; }
        }

        private void DrawAppliedMark(Graphics g, Mark mark)
        {
            var state = g.Save();
            try
            {
                using (var region = AppliedRegion(mark))
                {
                    g.SetClip(region, CombineMode.Intersect);
                    if (mark.Tool == DrawTool.Eraser)
                    {
                        // Restore the frozen screenshot only inside the brush or selection.
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImage(Image, new Rectangle(Point.Empty, Image.Size), 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel);
                        return;
                    }
                    DrawMosaicPixels(g, mark);
                }
            }
            finally { g.Restore(state); }
        }

        internal void DrawMark(Graphics g, Mark mark)
        {
            if (mark.Points.Count == 0) return;
            if (mark.Tool == DrawTool.Mosaic || mark.Tool == DrawTool.Eraser) { DrawAppliedMark(g, mark); return; }
            var bounds = mark.Bounds;
            using (var pen = new Pen(mark.Color, mark.Width))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                switch (mark.Tool)
                {
                    case DrawTool.Rectangle:
                        if (bounds.Width > 0 && bounds.Height > 0) g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                        break;
                    case DrawTool.Ellipse:
                        if (bounds.Width > 0 && bounds.Height > 0) g.DrawEllipse(pen, bounds);
                        break;
                    case DrawTool.Line:
                    case DrawTool.Polyline:
                        if (mark.Points.Count > 1) g.DrawLines(pen, mark.Points.ToArray());
                        break;
                    case DrawTool.Arrow:
                    case DrawTool.ArrowPolyline:
                        if (mark.Points.Count > 1)
                            using (var arrow = new AdjustableArrowCap(4, 5, true))
                            using (var path = new GraphicsPath())
                            { pen.CustomEndCap = arrow; path.AddLines(mark.Points.ToArray()); g.DrawPath(pen, path); }
                        break;
                    case DrawTool.Pen:
                        if (mark.Points.Count > 1) g.DrawLines(pen, mark.Points.ToArray());
                        break;
                    case DrawTool.Highlight:
                        pen.Color = Color.FromArgb(88, mark.Color); pen.Width = mark.Width * 6;
                        if (mark.Points.Count > 1) g.DrawLines(pen, mark.Points.ToArray());
                        break;
                    case DrawTool.Text:
                        TextGeometry.Draw(g, mark);
                        break;
                }
            }
        }

    }
}
