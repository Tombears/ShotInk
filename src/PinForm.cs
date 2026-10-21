using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class PinForm : Form
    {
        private readonly Bitmap image;
        private Point dragStart;
        private Point initialLocation;
        private bool dragging;
        private float zoom = 1;
        internal PinForm(Bitmap image)
        {
            this.image = image;
            Text = "贴图 · 右键菜单 / Esc 关闭";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
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
        private void SetZoom(float value) { zoom = Math.Max(0.05F, Math.Min(3, value)); ClientSize = new Size(Math.Max(10, (int)(image.Width * zoom)), Math.Max(10, (int)(image.Height * zoom))); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.DrawImage(image, ClientRectangle); using (var p = new Pen(Theme.Accent, 2)) e.Graphics.DrawRectangle(p, 1, 1, Width - 2, Height - 2); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; dragStart = Cursor.Position; initialLocation = Location; Capture = true; } base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) { var now = Cursor.Position; Location = new Point(initialLocation.X + now.X - dragStart.X, initialLocation.Y + now.Y - dragStart.Y); } base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; base.OnMouseUp(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { SetZoom(zoom * (e.Delta > 0 ? 1.1F : 1 / 1.1F)); base.OnMouseWheel(e); }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { Close(); base.OnMouseDoubleClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); base.OnKeyDown(e); }
        protected override void Dispose(bool disposing) { if (disposing) { image.Dispose(); if (ContextMenuStrip != null) ContextMenuStrip.Dispose(); } base.Dispose(disposing); }
    }
}
