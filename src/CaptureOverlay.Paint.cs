using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ShotInk
{
    internal sealed partial class CaptureOverlay
    {
        private Bitmap shadedDesktop;
        private Font pointerHintFont;
        private SizeF pointerHintSize;
        private long lastPointerPaint;
        private Region paintDamage;
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
        [DllImport("user32.dll")] private static extern int GetUpdateRgn(IntPtr window, IntPtr region, bool erase);
        private Region CapturePaintDamage()
        {
            var handle = CreateRectRgn(0, 0, 0, 0);
            if (handle == IntPtr.Zero) return null;
            try { return GetUpdateRgn(Handle, handle, false) > 1 ? Region.FromHrgn(handle) : null; }
            finally { DeleteObject(handle); }
        }
        private readonly Dictionary<string, Bitmap> pointerHints = new Dictionary<string, Bitmap>();
        private Bitmap PointerHintImage(string text)
        {
            Bitmap image;
            if (pointerHints.TryGetValue(text, out image)) return image;
            image = new Bitmap(PointerHintBounds(Point.Empty).Width, PointerHintBounds(Point.Empty).Height, PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.FromArgb(235, 26, 34, 51));
                graphics.DrawString(text, pointerHintFont, Brushes.White, 10, 6);
            }
            pointerHints.Add(text, image);
            return image;
        }
        private void EnsurePaintCache()
        {
            if (shadedDesktop != null) return;
            shadedDesktop = new Bitmap(desktop.Width, desktop.Height, PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(shadedDesktop))
            using (var shade = new SolidBrush(Color.FromArgb(112, 10, 17, 33)))
            {
                graphics.DrawImageUnscaled(desktop, Point.Empty);
                graphics.FillRectangle(shade, new Rectangle(Point.Empty, shadedDesktop.Size));
                pointerHintFont = new Font("Microsoft YaHei UI", 10);
                pointerHintSize = graphics.MeasureString(scrolling ? "长截图 · 拖选滚动内容区域，避开固定导航栏 · Esc 取消" : RegionHint, pointerHintFont);
                if (!scrolling) { var freeHintSize = graphics.MeasureString(FreeHint, pointerHintFont); pointerHintSize = new SizeF(Math.Max(pointerHintSize.Width, freeHintSize.Width), Math.Max(pointerHintSize.Height, freeHintSize.Height)); }
            }
        }
        private static string SelectionSizeText(Rectangle bounds) { return bounds.Width + " × " + bounds.Height + " px"; }
        private Rectangle DragHintBounds(Rectangle bounds)
        {
            var extent = System.Windows.Forms.TextRenderer.MeasureText(SelectionSizeText(bounds), sizeHint.Font);
            return new Rectangle(Math.Max(0, Math.Min(bounds.Left, Width - extent.Width - 8)), Math.Max(0, bounds.Top - extent.Height - 7), extent.Width + 8, extent.Height + 4);
        }
        private Rectangle PointerHintBounds(Point at)
        {
            return new Rectangle(Math.Max(0, Math.Min(at.X, Width - (int)Math.Ceiling(pointerHintSize.Width) - 24)),
                Math.Max(0, Math.Min(at.Y, Height - (int)Math.Ceiling(pointerHintSize.Height) - 18)),
                (int)Math.Ceiling(pointerHintSize.Width) + 20, (int)Math.Ceiling(pointerHintSize.Height) + 12);
        }
        private void AddPointerDamage(Region dirty, Point at)
        {
            dirty.Union(new Rectangle(at.X - 2, 0, 5, Height));
            dirty.Union(new Rectangle(0, at.Y - 2, Width, 5));
            dirty.Union(PointerHintBounds(new Point(at.X + 18, at.Y + 24)));
        }
        private void InvalidatePointer(Point previous, Rectangle previousSelection)
        {
            EnsurePaintCache();
            using (var dirty = new Region(Rectangle.Empty))
            {
                AddPointerDamage(dirty, previous); AddPointerDamage(dirty, pointer);
                if (previousSelection != selection)
                {
                    using (var changed = new Region(previousSelection)) { changed.Xor(selection); dirty.Union(changed); }
                    AddSelectionBorder(dirty, previousSelection); AddSelectionBorder(dirty, selection);
                }
                Invalidate(dirty, false);
            }
            PaintPointerFrame();
        }
        private static void AddSelectionBorder(Region dirty, Rectangle bounds)
        {
            var outside = bounds; outside.Inflate(6, 6);
            using (var border = new Region(outside))
            {
                var inside = bounds; inside.Inflate(-6, -6);
                if (inside.Width > 0 && inside.Height > 0) border.Exclude(inside);
                dirty.Union(border);
            }
        }
        private void InvalidateDrag(Rectangle previous)
        {
            using (var dirty = new Region(previous))
            {
                // Only the changed exposed area and outlines need repainting for a new selection.
                // Existing annotations or long-image previews move with their entire viewport.
                if (ready) dirty.Union(selection); else dirty.Xor(selection);
                AddSelectionBorder(dirty, previous); AddSelectionBorder(dirty, selection);
                dirty.Union(DragHintBounds(previous)); dirty.Union(DragHintBounds(selection));
                Invalidate(dirty, false);
            }
            PaintPointerFrame();
        }
        private void PaintPointerFrame()
        {
            long now = Stopwatch.GetTimestamp();
            // WM_PAINT can otherwise wait behind a continuous stream of mouse input.
            // Paint the latest position during input, at most 120 times per second.
            if (Visible && now - lastPointerPaint >= Stopwatch.Frequency / 120)
            { lastPointerPaint = now; Update(); }
        }
        private void DisposePaintCache()
        {
            if (shadedDesktop != null) { shadedDesktop.Dispose(); shadedDesktop = null; }
            if (pointerHintFont != null) { pointerHintFont.Dispose(); pointerHintFont = null; }
            foreach (var image in pointerHints.Values) image.Dispose();
            pointerHints.Clear();
        }
    }
}
