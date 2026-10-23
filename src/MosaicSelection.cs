using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed partial class AnnotationCanvas
    {
        private Mark selectedMosaic, mosaicOriginal;
        private List<Mark> mosaicUndo;
        private PointF mosaicAnchor;
        private int mosaicHandle;
        internal event Action<Mark> MosaicAnnotationSelected;
        private void SelectMosaic(Mark mark)
        { selectedMosaic = mark; if (MosaicAnnotationSelected != null) MosaicAnnotationSelected(mark); Invalidate(); }
        internal void ApplyMosaicSettings(MosaicEffect effect, int size, int radius)
        {
            MosaicEffect = effect; MosaicSize = size; BlurRadius = radius;
            if (MosaicMode != ApplyMode.Rectangle) { selectedMosaic = null; return; }
            if (selectedMosaic == null || Tool != DrawTool.Mosaic || !Marks.Contains(selectedMosaic)) return;
            if (selectedMosaic.MosaicEffect == effect && selectedMosaic.MosaicSize == size && selectedMosaic.BlurRadius == radius) return;
            SaveUndo(); selectedMosaic.MosaicEffect = effect; selectedMosaic.MosaicSize = size; selectedMosaic.BlurRadius = radius; NotifyChanged();
        }
        private static PointF[] MosaicHandles(RectangleF r)
        {
            float x = r.Left + r.Width / 2, y = r.Top + r.Height / 2;
            return new[] { new PointF(r.Left, r.Top), new PointF(x, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, y),
                new PointF(r.Right, r.Bottom), new PointF(x, r.Bottom), new PointF(r.Left, r.Bottom), new PointF(r.Left, y) };
        }
        private int HitMosaic(PointF point)
        {
            if (selectedMosaic == null) return -2;
            var handles = MosaicHandles(selectedMosaic.Bounds);
            for (int i = 0; i < handles.Length; i++) if (Math.Abs(point.X - handles[i].X) <= 5 / Zoom && Math.Abs(point.Y - handles[i].Y) <= 5 / Zoom) return i;
            return selectedMosaic.Bounds.Contains(point) ? -1 : -2;
        }
        private bool MosaicMouseDown(MouseEventArgs e)
        {
            if (Tool != DrawTool.Mosaic || MosaicMode != ApplyMode.Rectangle || e.Button != MouseButtons.Left) return false;
            var point = ImagePoint(e.Location); int hit = HitMosaic(point);
            if (hit == -2)
            {
                selectedMosaic = null;
                for (int i = Marks.Count - 1; i >= 0; i--)
                    if (Marks[i].Tool == DrawTool.Mosaic && !Marks[i].Freehand && Marks[i].Bounds.Contains(point)) { SelectMosaic(Marks[i]); hit = -1; break; }
            }
            if (hit == -2) { Invalidate(); return false; }
            Focus(); mosaicOriginal = selectedMosaic.Clone(); mosaicUndo = Snapshot(); mosaicAnchor = point; mosaicHandle = hit; Capture = true; return true;
        }
        private bool MosaicMouseMove(MouseEventArgs e)
        {
            if (Tool != DrawTool.Mosaic || MosaicMode != ApplyMode.Rectangle) return false;
            var point = ImagePoint(e.Location);
            if (mosaicOriginal != null)
            {
                var original = Rectangle.Round(mosaicOriginal.Bounds);
                var delta = new Point((int)Math.Round(point.X - mosaicAnchor.X), (int)Math.Round(point.Y - mosaicAnchor.Y));
                var bounds = mosaicHandle == -1 ? SelectionGeometry.Move(original, delta, new Rectangle(Point.Empty, Image.Size))
                    : SelectionGeometry.Resize(original, mosaicHandle, delta, new Rectangle(Point.Empty, Image.Size));
                selectedMosaic.Points.Clear(); selectedMosaic.Points.Add(bounds.Location); selectedMosaic.Points.Add(new PointF(bounds.Right, bounds.Bottom));
                Invalidate(); return true;
            }
            int hit = HitMosaic(point);
            Cursor = hit >= 0 ? MosaicHandleCursor(hit) : hit == -1 ? Cursors.SizeAll : Cursors.Cross;
            return false;
        }
        private static Cursor MosaicHandleCursor(int handle)
        {
            if (handle == 0 || handle == 4) return Cursors.SizeNWSE;
            if (handle == 2 || handle == 6) return Cursors.SizeNESW;
            return handle == 1 || handle == 5 ? Cursors.SizeNS : Cursors.SizeWE;
        }
        private bool FinishMosaicTransform(MouseEventArgs e)
        {
            if (mosaicOriginal == null || e.Button != MouseButtons.Left) return false;
            MosaicMouseMove(e); bool changed = selectedMosaic.Bounds != mosaicOriginal.Bounds;
            var previous = mosaicUndo; mosaicOriginal = null; mosaicUndo = null; Capture = false;
            if (changed) { undo.Push(previous); redo.Clear(); NotifyChanged(); }
            return true;
        }
        private void CancelMosaicTransform()
        {
            if (mosaicOriginal == null) return;
            selectedMosaic.Points.Clear(); selectedMosaic.Points.AddRange(mosaicOriginal.Points);
            mosaicOriginal = null; mosaicUndo = null; Invalidate();
        }
        private void DrawMosaicSelection(Graphics graphics)
        {
            if (selectedMosaic == null || Tool != DrawTool.Mosaic || MosaicMode != ApplyMode.Rectangle || !Marks.Contains(selectedMosaic)) return;
            var bounds = selectedMosaic.Bounds;
            using (var pen = new Pen(Color.FromArgb(220, 220, 220), 1 / Zoom))
            {
                pen.DashStyle = DashStyle.Dash; graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                pen.DashStyle = DashStyle.Solid;
                foreach (var point in MosaicHandles(bounds))
                {
                    float radius = 2.5F / Zoom; var box = new RectangleF(point.X - radius, point.Y - radius, radius * 2, radius * 2);
                    graphics.FillRectangle(Brushes.DimGray, box); graphics.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                }
            }
        }
    }
}
