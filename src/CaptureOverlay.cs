using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SnipasteLite
{
    internal sealed class CaptureOverlay : Form
    {
        private readonly Bitmap desktop;
        private readonly Rectangle screenBounds;
        private Point anchor;
        private Point pointer;
        private Rectangle selection;
        private bool dragging;
        private bool completed;
        private readonly bool scrolling;
        internal event Action<Rectangle, Bitmap> Selected;
        internal event Action Cancelled;

        internal CaptureOverlay(bool scrolling)
        {
            this.scrolling = scrolling;
            screenBounds = SystemInformation.VirtualScreen;
            desktop = Native.Capture(screenBounds);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = screenBounds;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            AutoScaleMode = AutoScaleMode.None;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(desktop, Point.Empty);
            using (var shade = new SolidBrush(Color.FromArgb(112, 10, 17, 33))) g.FillRectangle(shade, ClientRectangle);
            if (selection.Width > 0 && selection.Height > 0)
            {
                g.DrawImage(desktop, selection, selection, GraphicsUnit.Pixel);
                using (var pen = new Pen(Color.FromArgb(112, 163, 255), 2)) g.DrawRectangle(pen, selection);
                DrawHint(g, selection.Width + " × " + selection.Height + " px", new Point(selection.Left, Math.Max(4, selection.Top - 32)));
            }
            else
            {
                using (var pen = new Pen(Color.FromArgb(180, Color.White)))
                {
                    pen.DashStyle = DashStyle.Dot;
                    g.DrawLine(pen, pointer.X, 0, pointer.X, Height);
                    g.DrawLine(pen, 0, pointer.Y, Width, pointer.Y);
                }
            }
            DrawHint(g, scrolling ? "长截图 · 拖选滚动内容区域，避开固定导航栏 · Esc 取消" : "区域截图 · 按住鼠标左键拖选 · Esc / 右键取消", new Point(Math.Max(12, pointer.X + 18), Math.Max(12, pointer.Y + 24)));
        }

        private void DrawHint(Graphics g, string text, Point at)
        {
            using (var font = new Font("Microsoft YaHei UI", 10))
            {
                var size = g.MeasureString(text, font);
                int x = Math.Max(0, Math.Min(at.X, ClientSize.Width - (int)size.Width - 24));
                int y = Math.Max(0, Math.Min(at.Y, ClientSize.Height - (int)size.Height - 18));
                using (var brush = new SolidBrush(Color.FromArgb(235, 26, 34, 51)))
                    g.FillRectangle(brush, x, y, size.Width + 20, size.Height + 12);
                g.DrawString(text, font, Brushes.White, x + 10, y + 6);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            anchor = e.Location;
            pointer = e.Location;
            dragging = true;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            pointer = e.Location;
            if (dragging)
                selection = Rectangle.FromLTRB(Math.Max(0, Math.Min(anchor.X, e.X)), Math.Max(0, Math.Min(anchor.Y, e.Y)), Math.Min(Width, Math.Max(anchor.X, e.X)), Math.Min(Height, Math.Max(anchor.Y, e.Y)));
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false;
            Capture = false;
            if (selection.Width < 8 || selection.Height < 8) { selection = Rectangle.Empty; Invalidate(); return; }
            completed = true;
            var region = new Rectangle(selection.X + screenBounds.X, selection.Y + screenBounds.Y, selection.Width, selection.Height);
            var snapshot = scrolling ? null : CopyRegion(region);
            Hide();
            Close();
            if (Selected != null) Selected(region, snapshot);
            else if (snapshot != null) snapshot.Dispose();
        }

        internal Bitmap CopyRegion(Rectangle globalRegion)
        {
            return desktop.Clone(new Rectangle(globalRegion.X - screenBounds.X, globalRegion.Y - screenBounds.Y, globalRegion.Width, globalRegion.Height), desktop.PixelFormat);
        }

        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); base.OnKeyDown(e); }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (!completed && Cancelled != null) Cancelled();
            base.OnFormClosed(e);
        }
        protected override void Dispose(bool disposing) { if (disposing) desktop.Dispose(); base.Dispose(disposing); }
    }
}
