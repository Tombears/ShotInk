using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed partial class CaptureOverlay : Form
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
        private Bitmap longDragPreview;
        private AnnotationCanvas canvas;
        private CaptureToolbar toolbar;
        private TextRecognitionSession recognition;
        private readonly AppSettings settings;
        internal event Action<Bitmap, CaptureAction> Completed;
        internal event Action Cancelled;

        internal CaptureOverlay(bool scrolling) : this(scrolling, Native.Capture(SystemInformation.VirtualScreen), SystemInformation.VirtualScreen) { }
        internal CaptureOverlay(bool scrolling, AppSettings settings) : this(scrolling, Native.Capture(SystemInformation.VirtualScreen), SystemInformation.VirtualScreen, settings) { }
        internal CaptureOverlay(bool scrolling, Bitmap frozenDesktop, Rectangle bounds, AppSettings settings = null)
        {
            this.settings = settings ?? new AppSettings();
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
                if (dragging && longDragPreview != null) g.DrawImageUnscaled(longDragPreview, selection.Location);
                if (dragging && ready && canvas != null && longViewport == null)
                {
                    var state = g.Save(); g.SetClip(selection, CombineMode.Intersect);
                    var shift = dragMode == -1 ? Point.Empty : new Point(dragOriginal.Left - selection.Left, dragOriginal.Top - selection.Top);
                    g.TranslateTransform(selection.Left + shift.X, selection.Top + shift.Y); canvas.DrawAnnotations(g); g.Restore(state);
                }
                using (var pen = new Pen(Color.FromArgb(38, 139, 255), 2))
                    g.DrawRectangle(pen, selection.Left - 1, selection.Top - 1, selection.Width + 1, selection.Height + 1);
                if (dragging)
                {
                    string size = selection.Width + " × " + selection.Height + " px";
                    var extent = TextRenderer.MeasureText(size, sizeHint.Font);
                    var hint = new Rectangle(Math.Max(0, Math.Min(selection.Left, Width - extent.Width - 8)), Math.Max(0, selection.Top - extent.Height - 7), extent.Width + 8, extent.Height + 4);
                    using (var brush = new SolidBrush(sizeHint.BackColor)) g.FillRectangle(brush, hint);
                    TextRenderer.DrawText(g, size, sizeHint.Font, hint, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    if (ready && longViewport == null) foreach (var point in SelectionGeometry.Handles(selection))
                    { g.FillEllipse(Brushes.DodgerBlue, point.X - 4, point.Y - 4, 8, 8); g.DrawEllipse(Pens.White, point.X - 4, point.Y - 4, 8, 8); }
                }
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
            if (!ready || scrollSession != null || (longViewport != null && handle != -1)) return;
            anchor = location; dragOriginal = selection; dragMode = handle; dragging = true;
            if (longViewport != null)
            {
                ClearLongDragPreview();
                longDragPreview = new Bitmap(longViewport.Width, longViewport.Height);
                longViewport.DrawToBitmap(longDragPreview, longViewport.ClientRectangle);
                longViewport.Hide();
            }
            if (canvas != null) { canvas.CommitTextEdit(); if (longViewport == null) canvas.Hide(); }
            sizeHint.Hide(); foreach (var grip in grips) grip.Hide();
            if (toolbar != null) toolbar.Hide();
            Capture = true; Invalidate(true);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            pointer = e.Location;
            if (dragging)
            {
                if (dragMode == -2)
                    selection = Rectangle.FromLTRB(Math.Max(0, Math.Min(anchor.X, e.X)), Math.Max(0, Math.Min(anchor.Y, e.Y)), Math.Min(Width, Math.Max(anchor.X, e.X)), Math.Min(Height, Math.Max(anchor.Y, e.Y)));
                else
                {
                    var delta = new Point(e.X - anchor.X, e.Y - anchor.Y);
                    selection = dragMode == -1 ? SelectionGeometry.Move(dragOriginal, delta, ClientRectangle) : SelectionGeometry.Resize(dragOriginal, dragMode, delta, ClientRectangle);
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
            if (!ready) PrepareEditor();
            else if (canvas != null)
            {
                if (longViewport != null) { longViewport.Bounds = selection; ClearLongDragPreview(); longViewport.Show(); }
                else
                {
                    if (selection != dragOriginal)
                        canvas.Reframe(desktop.Clone(selection, desktop.PixelFormat), dragMode == -1 ? Point.Empty : new Point(dragOriginal.Left - selection.Left, dragOriginal.Top - selection.Top));
                    canvas.Location = selection.Location;
                }
                canvas.Show();
            }
            UpdateSelectionChrome();
            toolbar.Show(); PositionToolbar(); Invalidate(true);
            if (scrolling && longViewport == null) StartLongCapture();
        }

        private void PrepareEditor()
        {
            ready = true;
            canvas = new AnnotationCanvas(desktop.Clone(selection, desktop.PixelFormat)) { Location = selection.Location, Tool = DrawTool.None, Cursor = Cursors.SizeAll };
            canvas.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right) { Close(); return; }
                if (e.Button == MouseButtons.Left && canvas.Tool == DrawTool.None && !canvas.HasTextRecognition && longViewport == null) BeginAdjust(-1, PointToClient(canvas.PointToScreen(e.Location)));
            };
            canvas.MouseDoubleClick += delegate { if (canvas.Tool == DrawTool.None && !canvas.HasTextRecognition) Finish(CaptureAction.Copy); };
            Controls.Add(canvas);
            toolbar = new CaptureToolbar(settings);
            toolbar.ToolChanged += delegate(DrawTool tool) { canvas.Tool = tool; canvas.Cursor = tool == DrawTool.None ? Cursors.SizeAll : Cursors.Cross; SyncSettings(); canvas.Focus(); };
            toolbar.SettingsChanged += delegate { SyncSettings(); };
            toolbar.UndoClicked += delegate { canvas.Undo(); };
            toolbar.RedoClicked += delegate { canvas.Redo(); };
            toolbar.CancelClicked += delegate { Close(); };
            toolbar.FinishClicked += Finish;
            toolbar.LayoutChanged += PositionToolbar;
            ResetRecognitionSession();
            toolbar.ScrollClicked += StartLongCapture;
            toolbar.ScrollFinishClicked += FinishLongCapture;
            toolbar.RecognitionClicked += async delegate { await recognition.RecognizeAsync(); };
            toolbar.RecognitionHovered += delegate { RapidTextRecognizer.Shared.WarmUpAsync(); };
            toolbar.CopySelectedText += delegate { canvas.CopyRecognizedText(false); };
            toolbar.CopyAllText += delegate { canvas.CopyRecognizedText(true); };
            canvas.TextSelectionChanged += delegate { toolbar.SetTextSelection(canvas.HasTextRecognition, canvas.HasSelectedText); };
            canvas.Changed += delegate { toolbar.UpdateHistory(canvas.CanUndo, canvas.CanRedo); };
            Controls.Add(toolbar);
            SyncSettings();
            canvas.Focus();
        }

        private void SyncSettings() { toolbar.ApplyTo(canvas); }
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
            if (dragging) return;
            sizeHint.Visible = selection.Width > 0 && selection.Height > 0;
            sizeHint.Text = (longViewport == null ? selection.Width : canvas.Image.Width) + " × " + (longViewport == null ? selection.Height : canvas.Image.Height) + " px";
            if (longLimitReached) sizeHint.Text += " · 达到上限，已自动完成";
            else if (longAlignmentLost) sizeHint.Text += " · 页面变化过大，已保留可拼接部分";
            sizeHint.Location = new Point(Math.Max(0, Math.Min(selection.Left, Width - sizeHint.PreferredWidth)), Math.Max(0, selection.Top - sizeHint.PreferredHeight - 3));
            sizeHint.BringToFront();
            var points = SelectionGeometry.Handles(selection);
            for (int i = 0; i < grips.Length; i++)
            {
                grips[i].Visible = ready && longViewport == null && scrollSession == null;
                grips[i].Location = new Point(points[i].X - 4, points[i].Y - 4);
                grips[i].BringToFront();
            }
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (dragging && !Capture)
            {
                dragging = false;
                if (ready)
                {
                    selection = dragOriginal;
                    if (longViewport != null) { longViewport.Bounds = selection; ClearLongDragPreview(); longViewport.Show(); }
                    if (canvas != null) canvas.Show();
                    UpdateSelectionChrome(); if (toolbar != null) { toolbar.Show(); PositionToolbar(); }
                }
                else selection = Rectangle.Empty;
                Invalidate(true);
            }
        }

        private void ClearSelection()
        {
            ClearLongDragPreview();
            if (scrollSession != null) { scrollSession.Dispose(); scrollSession = null; ResetCaptureRegion(); }
            ready = false;
            if (recognition != null) { recognition.Dispose(); recognition = null; }
            if (canvas != null) { Controls.Remove(canvas); canvas.Dispose(); canvas = null; }
            if (longViewport != null) { Controls.Remove(longViewport); longViewport.Dispose(); longViewport = null; }
            longLimitReached = false; longAlignmentLost = false;
            if (toolbar != null) { Controls.Remove(toolbar); toolbar.Dispose(); toolbar = null; }
            selection = Rectangle.Empty; sizeHint.Hide();
            foreach (var grip in grips) grip.Hide();
        }

        private void Finish(CaptureAction action)
        {
            if (!ready || completed || scrollSession != null) return;
            var image = canvas.Render();
            if (action == CaptureAction.Save)
            {
                try
                {
                    string path;
                    if (!ImageExport.Save(this, image, settings, out path)) { image.Dispose(); return; }
                }
                catch (Exception ex) { image.Dispose(); MessageBox.Show(this, ex.Message, "无法保存图片"); return; }
            }
            completed = true;
            Hide(); Close();
            if (Completed != null) Completed(image, action); else image.Dispose();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (scrollSession != null)
            {
                if (e.KeyCode == Keys.Enter) FinishLongCapture();
                else if (e.KeyCode == Keys.Escape) Close();
                e.SuppressKeyPress = true; return;
            }
            if (canvas != null && canvas.HandleGestureKey(e)) return;
            if (canvas != null && canvas.HandleTextKey(e)) return;
            base.OnKeyDown(e);
            if (canvas != null && canvas.HandleRecognitionKey(e)) return;
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
        private void ClearLongDragPreview() { if (longDragPreview != null) { longDragPreview.Dispose(); longDragPreview = null; } }
        protected override void Dispose(bool disposing) { if (disposing) { ClearLongDragPreview(); if (scrollWheel != null) { scrollWheel.Dispose(); scrollWheel = null; } if (scrollSession != null) scrollSession.Dispose(); if (recognition != null) recognition.Dispose(); desktop.Dispose(); } base.Dispose(disposing); }

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
