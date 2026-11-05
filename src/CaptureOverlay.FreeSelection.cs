using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed partial class CaptureOverlay
    {
        private const string FreeHint = "画笔圈选 · 靠近起点自动吸附完成 · 松开自动闭合 · L 切换矩形 · Esc 取消";
        private bool freeSelectionMode;
        private PointF[] selectionOutline;
        private readonly List<PointF> freePoints = new List<PointF>();
        private double freeDistance;
        private Rectangle freeDamage;
        private SelectionModePicker modePicker;
        private void SetupSelectionModes()
        {
            modePicker = new SelectionModePicker();
            modePicker.ModeChanged += SetSelectionMode;
            var work = Screen.FromPoint(Cursor.Position).WorkingArea; work.Offset(-screenBounds.X, -screenBounds.Y);
            modePicker.Location = CaptureToolbar.Clamp(new Point(work.Left + (work.Width - modePicker.Width) / 2, work.Top + 16), ClientRectangle, modePicker.Size);
            Controls.Add(modePicker); modePicker.Visible = !scrolling;
        }
        private void SetSelectionMode(bool free)
        {
            if (scrolling || scrollSession != null || gifSession != null || longViewport != null) return;
            ClearSelection(); freeSelectionMode = free;
            modePicker.SetMode(free); modePicker.Show(); modePicker.BringToFront();
            Focus(); Invalidate(true);
        }
        private void BeginFreeStroke(Point point)
        {
            ClearSelection(); modePicker.Hide(); freeDistance = 0;
            freeDamage = new Rectangle(point, new Size(1, 1));
            freePoints.Add(ClampFreePoint(point)); dragMode = -3; dragging = true; Capture = true;
            Invalidate(true); Update();
        }
        private PointF ClampFreePoint(Point point)
        { return new PointF(Math.Max(0, Math.Min(Width, point.X)), Math.Max(0, Math.Min(Height, point.Y))); }
        private static double Distance(PointF a, PointF b)
        { return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }
        private void AddFreePoint(Point point, bool snap)
        {
            if (freePoints.Count == 0) return;
            var next = ClampFreePoint(point); double step = Distance(freePoints[freePoints.Count - 1], next);
            if (snap && freeDistance + step >= 60 && freePoints.Count >= 4 && Distance(next, freePoints[0]) <= 10)
            { if (CompleteFreeStroke()) return; }
            if (step >= 2) { freeDistance += step; freePoints.Add(next); }
            freeDamage = Rectangle.Union(freeDamage, new Rectangle((int)next.X, (int)next.Y, 1, 1));
            var damage = freeDamage; damage.Inflate(8, 8);
            Invalidate(damage); PaintPointerFrame();
        }
        private bool CompleteFreeStroke()
        {
            Rectangle bounds; PointF[] outline;
            if (!FreeSelectionGeometry.TryCreate(freePoints, out bounds, out outline)) return false;
            selection = bounds; selectionOutline = outline;
            dragging = false; Capture = false; freePoints.Clear();
            PrepareEditor(); canvas.SetCaptureOutline(outline); toolbar.SetFreeSelection(true);
            modePicker.Hide(); UpdateSelectionChrome(); toolbar.Show(); PositionToolbar(); canvas.Focus(); Invalidate(true);
            return true;
        }
        private bool SelectionContains(Point point)
        {
            if (!selection.Contains(point)) return false;
            if (selectionOutline == null) return true;
            using (var path = FreeSelectionGeometry.Path(selectionOutline, selection)) return path.IsVisible(point);
        }
        private void PaintFreeStroke(Graphics g)
        {
            if (freePoints.Count == 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(38, 139, 255), 2))
            {
                if (freePoints.Count >= 3)
                    using (var path = new GraphicsPath(FillMode.Alternate))
                    {
                        path.AddPolygon(freePoints.ToArray()); var state = g.Save(); g.SetClip(path, CombineMode.Intersect);
                        g.DrawImageUnscaled(desktop, 0, 0); g.Restore(state);
                    }
                if (freePoints.Count >= 2)
                {
                    g.DrawLines(pen, freePoints.ToArray()); pen.DashStyle = DashStyle.Dash;
                    g.DrawLine(pen, freePoints[freePoints.Count - 1], freePoints[0]);
                }
                var first = freePoints[0];
                g.FillEllipse(Brushes.White, first.X - 5, first.Y - 5, 10, 10);
                pen.DashStyle = DashStyle.Solid; g.DrawEllipse(pen, first.X - 5, first.Y - 5, 10, 10);
            }
        }
    }

    internal sealed class SelectionModePicker : UserControl
    {
        private readonly ToolOptionButton rectangle, free;
        private readonly ToolTip hints = new ToolTip();
        internal event Action<bool> ModeChanged;
        internal SelectionModePicker()
        {
            Name = "capture-selection-mode"; Size = new Size(70, 34); AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.White;
            rectangle = Add("selection-rectangle", "shape-rectangle", "矩形选框 (L 切换)", 5, false);
            free = Add("selection-free", "lasso", "自定义选框：画笔圈选，靠近起点吸附完成 (L 切换)", 37, true);
            SetMode(false);
        }
        private ToolOptionButton Add(string name, string icon, string hint, int x, bool value)
        {
            var button = new ToolOptionButton { Name = name, IconName = icon, AccessibleName = hint, Bounds = new Rectangle(x, 4, 28, 26), AccentColor = ToolAppearance.Accent("ocr") };
            hints.SetToolTip(button, hint); button.Click += delegate { if (ModeChanged != null) ModeChanged(value); };
            Controls.Add(button); return button;
        }
        internal void SetMode(bool value) { rectangle.Selected = !value; free.Selected = value; Invalidate(true); }
        protected override void Dispose(bool disposing) { if (disposing) hints.Dispose(); base.Dispose(disposing); }
    }
}
