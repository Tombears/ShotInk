using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class PinForm : Form
    {
        private readonly Bitmap image;
        private Point dragStart;
        private Rectangle initialBounds;
        private float initialZoom;
        private int resizeEdges, hoverEdges;
        private bool dragging;
        private float zoom = 1;
        internal PinForm(Bitmap image)
        {
            this.image = image;
            Text = "贴图 · 拖动边缘缩放 / 右键菜单 / Esc 关闭";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            Cursor = Cursors.SizeAll;
            var work = Screen.FromPoint(Cursor.Position).WorkingArea;
            zoom = Math.Min(1, Math.Min(work.Width * 0.7F / image.Width, work.Height * 0.7F / image.Height));
            SetZoom(zoom);
            var menu = new ContextMenuStrip();
            menu.Items.Add("复制图片", null, delegate { Clipboard.SetDataObject(this.image, true, 5, 100); });
            menu.Items.Add("原始大小", null, delegate { SetZoom(1); });
            menu.Items.Add("半透明", null, delegate { Opacity = Opacity > 0.8 ? 0.55 : 1; });
            menu.Items.Add("关闭贴图", null, delegate { Close(); });
            ContextMenuStrip = menu;
        }
        private Size ZoomSize(float value) { return new Size(Math.Max(1, (int)Math.Round(image.Width * value)), Math.Max(1, (int)Math.Round(image.Height * value))); }
        private void SetZoom(float value) { zoom = Math.Max(0.05F, Math.Min(3, value)); ClientSize = ZoomSize(zoom); Invalidate(); }
        // Bits: left, right, top, bottom. Corner zones combine two edges.
        private int HitEdges(Point point)
        {
            int horizontal = Math.Min(8, Math.Max(1, Width / 3)), vertical = Math.Min(8, Math.Max(1, Height / 3));
            return (point.X < horizontal ? 1 : point.X >= Width - horizontal ? 2 : 0) |
                (point.Y < vertical ? 4 : point.Y >= Height - vertical ? 8 : 0);
        }
        private void ShowEdges(int edges)
        {
            if (hoverEdges != edges) { hoverEdges = edges; Invalidate(); }
            Cursor = edges == 5 || edges == 10 ? Cursors.SizeNWSE : edges == 6 || edges == 9 ? Cursors.SizeNESW :
                (edges & 3) != 0 ? Cursors.SizeWE : (edges & 12) != 0 ? Cursors.SizeNS : Cursors.SizeAll;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (ClientSize == image.Size) e.Graphics.DrawImageUnscaled(image, Point.Empty);
            else using (var attributes = new ImageAttributes())
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                e.Graphics.DrawImage(image, ClientRectangle, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            }
            if (Width < 4 || Height < 4) return;
            var state = e.Graphics.Save(); var graphics = e.Graphics; graphics.SmoothingMode = SmoothingMode.HighQuality;
                using (var pen = new Pen(Color.FromArgb(150, Theme.Accent), 1)) graphics.DrawRectangle(pen, 0.5F, 0.5F, Width - 1, Height - 1);
                using (var pen = new Pen(Theme.Accent, 3))
                {
                    if ((hoverEdges & 1) != 0) graphics.DrawLine(pen, 1.5F, 1.5F, 1.5F, Height - 1.5F);
                    if ((hoverEdges & 2) != 0) graphics.DrawLine(pen, Width - 1.5F, 1.5F, Width - 1.5F, Height - 1.5F);
                    if ((hoverEdges & 4) != 0) graphics.DrawLine(pen, 1.5F, 1.5F, Width - 1.5F, 1.5F);
                    if ((hoverEdges & 8) != 0) graphics.DrawLine(pen, 1.5F, Height - 1.5F, Width - 1.5F, Height - 1.5F);
                }
            graphics.Restore(state);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = true; dragStart = PointToScreen(e.Location); initialBounds = Bounds; initialZoom = zoom;
                resizeEdges = HitEdges(e.Location); ShowEdges(resizeEdges); Capture = true;
            }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging)
            {
                var now = PointToScreen(e.Location); int dx = now.X - dragStart.X, dy = now.Y - dragStart.Y;
                if (resizeEdges == 0) Location = new Point(initialBounds.X + dx, initialBounds.Y + dy);
                else
                {
                    float horizontal = ((resizeEdges & 1) != 0 ? -dx : dx) / (float)initialBounds.Width;
                    float vertical = ((resizeEdges & 4) != 0 ? -dy : dy) / (float)initialBounds.Height;
                    float change = (resizeEdges & 3) == 0 ? vertical : (resizeEdges & 12) == 0 ? horizontal : Math.Abs(horizontal) >= Math.Abs(vertical) ? horizontal : vertical;
                    zoom = Math.Max(0.05F, Math.Min(3, initialZoom * (1 + change))); var size = ZoomSize(zoom);
                    int x = (resizeEdges & 1) != 0 ? initialBounds.Right - size.Width : (resizeEdges & 2) != 0 ? initialBounds.Left : initialBounds.Left + (initialBounds.Width - size.Width) / 2;
                    int y = (resizeEdges & 4) != 0 ? initialBounds.Bottom - size.Height : (resizeEdges & 8) != 0 ? initialBounds.Top : initialBounds.Top + (initialBounds.Height - size.Height) / 2;
                    Bounds = new Rectangle(new Point(x, y), size); Invalidate();
                }
            }
            else ShowEdges(HitEdges(e.Location));
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && dragging) { OnMouseMove(e); dragging = false; Capture = false; ShowEdges(HitEdges(e.Location)); }
            base.OnMouseUp(e);
        }
        protected override void OnMouseLeave(EventArgs e) { if (!dragging) ShowEdges(0); base.OnMouseLeave(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) dragging = false; base.OnMouseCaptureChanged(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { SetZoom(zoom * (e.Delta > 0 ? 1.1F : 1 / 1.1F)); base.OnMouseWheel(e); }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { if (e.Button == MouseButtons.Left && HitEdges(e.Location) == 0) Close(); base.OnMouseDoubleClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); base.OnKeyDown(e); }
        protected override void Dispose(bool disposing) { if (disposing) { image.Dispose(); if (ContextMenuStrip != null) ContextMenuStrip.Dispose(); } base.Dispose(disposing); }
    }
}
