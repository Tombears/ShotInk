using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace SnipasteLite
{
    internal sealed class CaptureOverlay : Form
    {
        private readonly Bitmap desktop;
        private readonly Rectangle screenBounds;
        private readonly bool scrolling;
        private readonly Label sizeHint;
        private readonly ResizeGrip[] grips = new ResizeGrip[8];
        private Point anchor;
        private Point pointer;
        private Rectangle selection;
        private Rectangle dragOriginal;
        private int dragMode = -2; // -2: new selection, -1: move, 0..7: resize handles.
        private bool dragging;
        private bool ready;
        private bool completed;
        private AnnotationCanvas canvas;
        private CaptureToolbar toolbar;
        internal event Action<Rectangle, Bitmap> Selected;
        internal event Action<Bitmap, CaptureAction> Completed;
        internal event Action Cancelled;

        internal CaptureOverlay(bool scrolling) : this(scrolling, Native.Capture(SystemInformation.VirtualScreen), SystemInformation.VirtualScreen) { }
        internal CaptureOverlay(bool scrolling, Bitmap frozenDesktop, Rectangle bounds)
        {
            this.scrolling = scrolling;
            desktop = frozenDesktop;
            screenBounds = bounds;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = screenBounds;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            sizeHint = new Label { AutoSize = true, ForeColor = Color.White, BackColor = Color.FromArgb(25, 28, 33),
                Font = new Font("Consolas", 10), Padding = new Padding(4, 2, 4, 2), Visible = false };
            Controls.Add(sizeHint);
            for (int i = 0; i < grips.Length; i++)
            {
                int index = i;
                var grip = new ResizeGrip { Visible = false, Size = new Size(9, 9), Cursor = HandleCursor(i) };
                grip.MouseDown += delegate(object sender, MouseEventArgs e)
                {
                    if (e.Button == MouseButtons.Left) BeginAdjust(index, PointToClient(((Control)sender).PointToScreen(e.Location)));
                };
                grips[i] = grip; Controls.Add(grip);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(desktop, Point.Empty);
            using (var shade = new SolidBrush(Color.FromArgb(112, 10, 17, 33))) g.FillRectangle(shade, ClientRectangle);
            if (selection.Width > 0 && selection.Height > 0)
            {
                g.DrawImage(desktop, selection, selection, GraphicsUnit.Pixel);
                using (var pen = new Pen(Color.FromArgb(38, 139, 255), 2))
                    g.DrawRectangle(pen, selection.Left - 1, selection.Top - 1, selection.Width + 1, selection.Height + 1);
            }
            if (!ready && !dragging)
            {
                using (var pen = new Pen(Color.FromArgb(180, Color.White)))
                { pen.DashStyle = DashStyle.Dot; g.DrawLine(pen, pointer.X, 0, pointer.X, Height); g.DrawLine(pen, 0, pointer.Y, Width, pointer.Y); }
                DrawHint(g, scrolling ? "长截图 · 拖选滚动内容区域，避开固定导航栏 · Esc 取消" : "拖选截图区域 · 松开后显示工具栏 · Esc / 右键取消", new Point(pointer.X + 18, pointer.Y + 24));
            }
        }

        private void DrawHint(Graphics g, string text, Point at)
        {
            using (var font = new Font("Microsoft YaHei UI", 10))
            {
                var size = g.MeasureString(text, font);
                int x = Math.Max(0, Math.Min(at.X, ClientSize.Width - (int)size.Width - 24));
                int y = Math.Max(0, Math.Min(at.Y, ClientSize.Height - (int)size.Height - 18));
                using (var brush = new SolidBrush(Color.FromArgb(235, 26, 34, 51))) g.FillRectangle(brush, x, y, size.Width + 20, size.Height + 12);
                g.DrawString(text, font, Brushes.White, x + 10, y + 6);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            if (ready && selection.Contains(e.Location)) { BeginAdjust(-1, e.Location); return; }
            ClearSelection();
            anchor = e.Location; pointer = e.Location; dragMode = -2; dragging = true; Capture = true;
        }

        private void BeginAdjust(int handle, Point location)
        {
            if (!ready) return;
            anchor = location; dragOriginal = selection; dragMode = handle; dragging = true;
            if (toolbar != null) toolbar.Hide();
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            pointer = e.Location;
            if (dragging)
            {
                var previous = selection;
                if (dragMode == -2)
                    selection = Rectangle.FromLTRB(Math.Max(0, Math.Min(anchor.X, e.X)), Math.Max(0, Math.Min(anchor.Y, e.Y)), Math.Min(Width, Math.Max(anchor.X, e.X)), Math.Min(Height, Math.Max(anchor.Y, e.Y)));
                else
                {
                    var delta = new Point(e.X - anchor.X, e.Y - anchor.Y);
                    selection = dragMode == -1 ? SelectionGeometry.Move(dragOriginal, delta, ClientRectangle) : SelectionGeometry.Resize(dragOriginal, dragMode, delta, ClientRectangle);
                    if (selection != previous && canvas != null)
                    {
                        canvas.Reframe(desktop.Clone(selection, desktop.PixelFormat), dragMode == -1 ? Point.Empty : new Point(previous.Left - selection.Left, previous.Top - selection.Top));
                        canvas.Location = selection.Location;
                    }
                }
                UpdateSelectionChrome();
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging || e.Button != MouseButtons.Left) return;
            // Process the final pointer position even when Windows coalesces mouse-move events.
            OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, e.X, e.Y, 0));
            dragging = false; Capture = false;
            if (selection.Width < 8 || selection.Height < 8) { ClearSelection(); Invalidate(); return; }
            if (scrolling)
            {
                completed = true;
                var region = new Rectangle(selection.X + screenBounds.X, selection.Y + screenBounds.Y, selection.Width, selection.Height);
                Hide(); Close(); if (Selected != null) Selected(region, null);
                return;
            }
            if (!ready) PrepareEditor();
            UpdateSelectionChrome();
            toolbar.Show(); PositionToolbar();
        }

        private void PrepareEditor()
        {
            ready = true;
            canvas = new AnnotationCanvas(desktop.Clone(selection, desktop.PixelFormat)) { Location = selection.Location, Tool = DrawTool.None, Cursor = Cursors.SizeAll };
            canvas.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right) { Close(); return; }
                if (e.Button == MouseButtons.Left && canvas.Tool == DrawTool.None) BeginAdjust(-1, PointToClient(canvas.PointToScreen(e.Location)));
            };
            canvas.MouseDoubleClick += delegate { if (canvas.Tool == DrawTool.None) Finish(CaptureAction.Copy); };
            Controls.Add(canvas);
            toolbar = new CaptureToolbar();
            toolbar.ToolChanged += delegate(DrawTool tool) { canvas.Tool = tool; canvas.Cursor = tool == DrawTool.None ? Cursors.SizeAll : Cursors.Cross; SyncSettings(); canvas.Focus(); };
            toolbar.SettingsChanged += delegate { SyncSettings(); canvas.Focus(); };
            toolbar.UndoClicked += delegate { canvas.Undo(); };
            toolbar.RedoClicked += delegate { canvas.Redo(); };
            toolbar.CancelClicked += delegate { Close(); };
            toolbar.FinishClicked += Finish;
            toolbar.LayoutChanged += PositionToolbar;
            canvas.Changed += delegate { toolbar.UpdateHistory(canvas.CanUndo, canvas.CanRedo); };
            Controls.Add(toolbar);
            SyncSettings();
            canvas.Focus();
        }

        private void SyncSettings() { canvas.Ink = toolbar.Ink; canvas.Stroke = toolbar.Stroke; }
        private void PositionToolbar()
        {
            if (toolbar == null) return;
            var global = selection; global.Offset(screenBounds.Location);
            var work = Screen.FromRectangle(global).WorkingArea;
            work.Offset(-screenBounds.X, -screenBounds.Y);
            work = Rectangle.Intersect(work, ClientRectangle);
            toolbar.Location = CaptureToolbar.Place(selection, work, toolbar.Size);
            toolbar.BringToFront();
        }

        private void UpdateSelectionChrome()
        {
            sizeHint.Visible = selection.Width > 0 && selection.Height > 0;
            sizeHint.Text = selection.Width + " × " + selection.Height + " px";
            sizeHint.Location = new Point(Math.Max(0, Math.Min(selection.Left, Width - sizeHint.PreferredWidth)), Math.Max(0, selection.Top - sizeHint.PreferredHeight - 3));
            sizeHint.BringToFront();
            var points = SelectionGeometry.Handles(selection);
            for (int i = 0; i < grips.Length; i++)
            {
                grips[i].Visible = ready;
                grips[i].Location = new Point(points[i].X - 4, points[i].Y - 4);
                grips[i].BringToFront();
            }
        }

        private void ClearSelection()
        {
            ready = false;
            if (canvas != null) { Controls.Remove(canvas); canvas.Dispose(); canvas = null; }
            if (toolbar != null) { Controls.Remove(toolbar); toolbar.Dispose(); toolbar = null; }
            selection = Rectangle.Empty; sizeHint.Hide();
            foreach (var grip in grips) grip.Hide();
        }

        private void Finish(CaptureAction action)
        {
            if (!ready || completed) return;
            var image = canvas.Render();
            if (action == CaptureAction.Save)
            {
                try
                {
                    using (var dialog = new SaveFileDialog { Filter = "PNG 图片 (*.png)|*.png", FileName = "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) })
                    {
                        if (dialog.ShowDialog(this) != DialogResult.OK) { image.Dispose(); return; }
                        image.Save(dialog.FileName, ImageFormat.Png);
                    }
                }
                catch (Exception ex) { image.Dispose(); MessageBox.Show(this, ex.Message, "无法保存图片"); return; }
            }
            completed = true;
            Hide(); Close();
            if (Completed != null) Completed(image, action); else image.Dispose();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); return; }
            if (!ready) return;
            if ((e.Control && e.KeyCode == Keys.C) || e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Finish(CaptureAction.Copy); }
            if (e.Control && e.KeyCode == Keys.S) { e.SuppressKeyPress = true; Finish(CaptureAction.Save); }
            if (e.Control && e.KeyCode == Keys.Z) { canvas.Undo(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.Y) { canvas.Redo(); e.SuppressKeyPress = true; }
        }
        private static Cursor HandleCursor(int index)
        {
            if (index == 0 || index == 4) return Cursors.SizeNWSE;
            if (index == 2 || index == 6) return Cursors.SizeNESW;
            return index == 1 || index == 5 ? Cursors.SizeNS : Cursors.SizeWE;
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { if (!completed && Cancelled != null) Cancelled(); base.OnFormClosed(e); }
        protected override void Dispose(bool disposing) { if (disposing) desktop.Dispose(); base.Dispose(disposing); }

        private sealed class ResizeGrip : Control
        {
            internal ResizeGrip() { SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); BackColor = Color.Transparent; }
            protected override void OnPaint(PaintEventArgs e) { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using (var b = new SolidBrush(Color.FromArgb(38, 139, 255))) e.Graphics.FillEllipse(b, 1, 1, 7, 7); e.Graphics.DrawEllipse(Pens.White, 1, 1, 7, 7); }
        }
    }

    internal static class SelectionGeometry
    {
        internal static Rectangle Move(Rectangle original, Point delta, Rectangle bounds)
        {
            return new Rectangle(Math.Max(bounds.Left, Math.Min(original.Left + delta.X, bounds.Right - original.Width)), Math.Max(bounds.Top, Math.Min(original.Top + delta.Y, bounds.Bottom - original.Height)), original.Width, original.Height);
        }
        internal static Rectangle Resize(Rectangle original, int handle, Point delta, Rectangle bounds)
        {
            int left = original.Left, top = original.Top, right = original.Right, bottom = original.Bottom;
            if (handle == 0 || handle == 6 || handle == 7) left = Math.Max(bounds.Left, Math.Min(right - 8, left + delta.X));
            if (handle == 0 || handle == 1 || handle == 2) top = Math.Max(bounds.Top, Math.Min(bottom - 8, top + delta.Y));
            if (handle == 2 || handle == 3 || handle == 4) right = Math.Min(bounds.Right, Math.Max(left + 8, right + delta.X));
            if (handle == 4 || handle == 5 || handle == 6) bottom = Math.Min(bounds.Bottom, Math.Max(top + 8, bottom + delta.Y));
            return Rectangle.FromLTRB(left, top, right, bottom);
        }
        internal static Point[] Handles(Rectangle r)
        {
            int x = r.Left + r.Width / 2, y = r.Top + r.Height / 2;
            return new[] { new Point(r.Left, r.Top), new Point(x, r.Top), new Point(r.Right, r.Top), new Point(r.Right, y), new Point(r.Right, r.Bottom), new Point(x, r.Bottom), new Point(r.Left, r.Bottom), new Point(r.Left, y) };
        }
    }
}
