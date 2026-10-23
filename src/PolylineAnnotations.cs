using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed partial class AnnotationCanvas
    {
        private static bool IsPolyline(DrawTool value) { return value == DrawTool.Polyline || value == DrawTool.ArrowPolyline; }
        private bool HasPolyline { get { return active != null && IsPolyline(active.Tool); } }
        private bool PolylineMouseDown(MouseEventArgs e)
        {
            if (!IsPolyline(Tool)) return false;
            if (e.Button == MouseButtons.Right && HasPolyline) { CompletePolyline(); return true; }
            if (e.Button != MouseButtons.Left) return false;
            Focus(); var point = ImagePoint(e.Location);
            if (!HasPolyline)
            {
                active = new Mark { Tool = Tool, Color = Ink, Width = Stroke };
                active.Points.Add(point); active.Points.Add(point);
            }
            else if (e.Clicks > 1) CompletePolyline();
            else
            {
                active.Points[active.Points.Count - 1] = point;
                if (point != active.Points[active.Points.Count - 2]) active.Points.Add(point);
            }
            Invalidate(); return true;
        }
        private void CompletePolyline()
        {
            if (!HasPolyline) return;
            var line = active; active = null;
            line.Points.RemoveAt(line.Points.Count - 1);
            if (line.Points.Count > 1) AddMark(line);
            Invalidate();
        }
        internal bool HandleGestureKey(KeyEventArgs e)
        {
            if (!HasPolyline) return false;
            if (e.KeyCode == Keys.Enter) CompletePolyline();
            else if (e.KeyCode == Keys.Escape) CancelGesture();
            else if (e.KeyCode == Keys.Back || (e.Control && e.KeyCode == Keys.Z))
            {
                if (active.Points.Count > 2) active.Points.RemoveAt(active.Points.Count - 2);
                else CancelGesture();
                Invalidate();
            }
            else return false;
            e.SuppressKeyPress = true; return true;
        }
    }
}
