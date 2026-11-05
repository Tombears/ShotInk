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
        private readonly Control[] edgeTargets = new Control[4];
        private readonly Timer edgeHold = new Timer { Interval = 350 };
        private int keyboardEdge = -1;
        private Point anchor;
        private Point pointer;
        private Rectangle selection;
        private Rectangle dragOriginal;
        private int dragMode = -2; // -2: new selection, -1: move, 0..7: resize handles.
        private bool dragging;
        private Rectangle[] windowBounds = new Rectangle[0];
        private Rectangle pressedWindow;
        private long selectionPressTime;
        private bool manualSelection;
        private readonly Timer selectionHold = new Timer { Interval = 350 };
        private const string RegionHint = "单击窗口进入编辑 · 长按或拖动框选 · Esc / 右键取消";
        private bool ready;
        private bool completed;
        private bool rightCancelPending;
        private Bitmap longDragPreview;
        private AnnotationCanvas canvas;
        private CaptureToolbar toolbar;
#if !SHOTINK_LITE
        private OcrSearchPanel textSearch;
#endif
#if !SHOTINK_LITE
        private TextRecognitionSession recognition;
#endif
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
            SetStyle(ControlStyles.Opaque, true);
            KeyPreview = true;
            Cursor = Cursors.Cross;
            sizeHint = new Label { AutoSize = true, ForeColor = Color.White, BackColor = Color.FromArgb(25, 28, 33),
                Font = new Font("Consolas", 10), Padding = new Padding(4, 2, 4, 2), Visible = false };
            Controls.Add(sizeHint);
            Disposed += delegate { selectionHold.Dispose(); edgeHold.Dispose(); };
            edgeHold.Tick += delegate { edgeHold.Stop(); if (dragging && selection == dragOriginal && dragMode >= 0) { keyboardEdge = dragMode; Invalidate(); } };
            selectionHold.Tick += delegate
            {
                selectionHold.Stop();
                if (dragging && !ready && dragMode == -2 && !manualSelection) { manualSelection = true; selection = Rectangle.Empty; Invalidate(); }
            };
            pointer = new Point(Cursor.Position.X - bounds.X, Cursor.Position.Y - bounds.Y);
            SetupSelectionModes();
            for (int i = 0; i < edgeTargets.Length; i++)
            {
                int handle = i * 2 + 1;
                var edge = new Panel { Visible = false, BackColor = Color.FromArgb(38, 139, 255), Cursor = HandleCursor(handle), TabStop = false };
                edge.MouseDown += delegate(object sender, MouseEventArgs e)
                {
                    if (e.Button == MouseButtons.Right) { BeginRightCancel(); return; }
                    if (e.Button == MouseButtons.Left) BeginAdjust(handle, PointToClient(((Control)sender).PointToScreen(e.Location)));
                };
                edgeTargets[i] = edge; Controls.Add(edge);
            }
            for (int i = 0; i < grips.Length; i++)
            {
                int index = i;
                var grip = new ResizeGrip { Visible = false, Size = new Size(9, 9), Cursor = HandleCursor(i) };
                grip.MouseDown += delegate(object sender, MouseEventArgs e)
                {
                    if (e.Button == MouseButtons.Right) { BeginRightCancel(); return; }
                    if (e.Button == MouseButtons.Left) BeginAdjust(index, PointToClient(((Control)sender).PointToScreen(e.Location)));
                };
                grips[i] = grip; Controls.Add(grip);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            EnsurePaintCache();
            g.DrawImage(shadedDesktop, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);
            if (dragging && dragMode == -3) { PaintFreeStroke(g); return; }
            if (selection.Width > 0 && selection.Height > 0)
            {
                var surfaceState = g.Save();
                if (selectionOutline != null)
                    using (var outline = FreeSelectionGeometry.Path(selectionOutline, selection)) g.SetClip(outline, CombineMode.Intersect);
                g.DrawImage(desktop, selection, selection, GraphicsUnit.Pixel);
                if (dragging && longDragPreview != null) g.DrawImageUnscaled(longDragPreview, selection.Location);
                if (dragging && ready && canvas != null && longViewport == null)
                {
                    var state = g.Save(); g.SetClip(selection, CombineMode.Intersect);
                    var shift = dragMode == -1 ? Point.Empty : new Point(dragOriginal.Left - selection.Left, dragOriginal.Top - selection.Top);
                    g.TranslateTransform(selection.Left + shift.X, selection.Top + shift.Y); canvas.DrawAnnotations(g); g.Restore(state);
                }
                g.Restore(surfaceState);
                using (var pen = new Pen(Color.FromArgb(38, 139, 255), 2))
                {
                    if (selectionOutline == null) g.DrawRectangle(pen, selection.Left - 1, selection.Top - 1, selection.Width + 1, selection.Height + 1);
                    else using (var outline = FreeSelectionGeometry.Path(selectionOutline, selection)) { g.SmoothingMode = SmoothingMode.AntiAlias; g.DrawPath(pen, outline); }
                }
                if (keyboardEdge >= 0)
                {
                    var points = SelectionGeometry.Handles(selection);
                    using (var pen = new Pen(Color.DarkOrange, 3)) g.DrawLine(pen, points[keyboardEdge - 1], points[(keyboardEdge + 1) % 8]);
                }
                if (dragging)
                {
                    string size = SelectionSizeText(selection);
                    var hint = DragHintBounds(selection);
                    using (var brush = new SolidBrush(sizeHint.BackColor)) g.FillRectangle(brush, hint);
                    TextRenderer.DrawText(g, size, sizeHint.Font, hint, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsClipping);
                    if (ready && longViewport == null) foreach (var point in SelectionGeometry.Handles(selection))
                    { g.FillEllipse(Brushes.DodgerBlue, point.X - 4, point.Y - 4, 8, 8); g.DrawEllipse(Pens.White, point.X - 4, point.Y - 4, 8, 8); }
                }
            }
            if (!ready && !dragging)
            {
                using (var pen = new Pen(Color.FromArgb(180, Color.White)))
                { pen.DashStyle = DashStyle.Dot; g.DrawLine(pen, pointer.X, 0, pointer.X, Height); g.DrawLine(pen, 0, pointer.Y, Width, pointer.Y); }
                DrawHint(g, scrolling ? "长截图 · 拖选滚动内容区域，避开固定导航栏 · Esc 取消" : freeSelectionMode ? FreeHint : RegionHint, new Point(pointer.X + 18, pointer.Y + 24));
            }
        }

        private void DrawHint(Graphics g, string text, Point at)
        {
            var hint = PointerHintBounds(at);
            using (var brush = new SolidBrush(Color.FromArgb(235, 26, 34, 51))) g.FillRectangle(brush, hint);
            g.DrawString(text, pointerHintFont, Brushes.White, hint.X + 10, hint.Y + 6);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { BeginRightCancel(); return; }
            if (e.Button != MouseButtons.Left) return;
            // The capture region stays fixed until the scrolling session ends.
            // Clicking the surrounding overlay must not clear or restart that session.
            if (scrollSession != null || gifSession != null) return;
            if (ready && SelectionContains(e.Location)) { BeginAdjust(-1, e.Location); return; }
            if (freeSelectionMode) { BeginFreeStroke(e.Location); return; }
            ClearSelection();
            modePicker.Hide();
            pressedWindow = WindowSelectionAt(e.Location);
            selectionPressTime = System.Diagnostics.Stopwatch.GetTimestamp(); manualSelection = scrolling;
            selectionHold.Start();
            anchor = e.Location; pointer = e.Location; dragMode = -2; dragging = true; selection = manualSelection ? Rectangle.Empty : pressedWindow; Capture = true;
            // Remove the hover crosshair and hint before drawing the first selection frame.
            Invalidate(); Update();
        }

        private void BeginRightCancel()
        {
            // Keep the overlay alive and capture the matching release, even if the pointer moves.
            rightCancelPending = true; Capture = true;
        }

        private void BeginAdjust(int handle, Point location)
        {
            if (!ready || scrollSession != null || gifSession != null || (longViewport != null && handle != -1)) return;
            edgeHold.Stop(); keyboardEdge = -1;
            if (handle >= 0 && handle % 2 == 1) edgeHold.Start();
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
            foreach (var edge in edgeTargets) edge.Hide();
            if (toolbar != null) toolbar.Hide();
            Focus();
            Capture = true; Invalidate(true);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var previousPointer = pointer;
            var previousSelection = selection;
            pointer = e.Location;
            if (dragging)
            {
                if (dragMode == -3) { AddFreePoint(e.Location, true); return; }
                if (keyboardEdge >= 0) return;
                if (dragMode == -2)
                {
                    if (Math.Abs(e.X - anchor.X) >= SystemInformation.DragSize.Width / 2 || Math.Abs(e.Y - anchor.Y) >= SystemInformation.DragSize.Height / 2) manualSelection = true;
                    if (!manualSelection) return;
                    selection = Rectangle.FromLTRB(Math.Max(0, Math.Min(anchor.X, e.X)), Math.Max(0, Math.Min(anchor.Y, e.Y)), Math.Min(Width, Math.Max(anchor.X, e.X)), Math.Min(Height, Math.Max(anchor.Y, e.Y)));
                }
                else
                {
                    var delta = new Point(e.X - anchor.X, e.Y - anchor.Y);
                    if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 3) edgeHold.Stop();
                    selection = dragMode == -1 ? SelectionGeometry.Move(dragOriginal, delta, ClientRectangle) : SelectionGeometry.Resize(dragOriginal, dragMode, delta, ClientRectangle);
                }
                if (selection != previousSelection) InvalidateDrag(previousSelection);
            }
            else if (!ready && previousPointer != pointer)
            {
                selection = scrolling || freeSelectionMode ? Rectangle.Empty : WindowSelectionAt(pointer);
                if (previousSelection != selection) Invalidate();
                InvalidatePointer(previousPointer);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (rightCancelPending && e.Button == MouseButtons.Right)
            { rightCancelPending = false; Close(); return; }
            if (!dragging || e.Button != MouseButtons.Left) return;
            if (dragMode == -3)
            {
                AddFreePoint(e.Location, false);
                if (!CompleteFreeStroke()) { dragging = false; Capture = false; ClearSelection(); Invalidate(true); }
                return;
            }
            edgeHold.Stop();
            selectionHold.Stop();
            if (!ready && dragMode == -2 && System.Diagnostics.Stopwatch.GetTimestamp() - selectionPressTime >= System.Diagnostics.Stopwatch.Frequency * 350 / 1000) manualSelection = true;
            // Process the final pointer position even when Windows coalesces mouse-move events.
            OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, e.X, e.Y, 0));
            keyboardEdge = -1;
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
            RememberSelection();
            UpdateSelectionChrome();
            toolbar.Show(); PositionToolbar(); Invalidate(true);
            if (scrolling && longViewport == null) StartLongCapture();
        }

        internal void EnableWindowSelection(Rectangle[] windows)
        {
            windowBounds = windows ?? new Rectangle[0];
            selection = scrolling || freeSelectionMode ? Rectangle.Empty : WindowSelectionAt(pointer);
        }
        private Rectangle WindowSelectionAt(Point point)
        {
            foreach (var bounds in windowBounds) if (bounds.Contains(point)) return bounds;
            return Rectangle.Empty;
        }
        internal void SelectRegion(Rectangle globalRegion)
        {
            var local = globalRegion; local.Offset(-screenBounds.X, -screenBounds.Y);
            if (local.Width < 8 || local.Height < 8 || !ClientRectangle.Contains(local)) throw new ArgumentException("截图区域已不在当前屏幕内，请重新框选。");
            selection = local; modePicker.Hide(); PrepareEditor(); UpdateSelectionChrome(); toolbar.Show(); PositionToolbar(); Invalidate();
        }
        private void RememberSelection()
        {
            if (scrolling || longViewport != null || selectionOutline != null) return;
            var region = selection; region.Offset(screenBounds.Location);
            if (settings.LastCaptureRegion != region) { settings.LastCaptureRegion = region; settings.NotifyChanged(); }
        }

        private void PrepareEditor()
        {
            ready = true;
            canvas = new AnnotationCanvas(desktop.Clone(selection, desktop.PixelFormat)) { Location = selection.Location, Tool = DrawTool.None, Cursor = Cursors.SizeAll };
            canvas.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right) { BeginRightCancel(); return; }
                if (e.Button == MouseButtons.Left && canvas.Tool == DrawTool.None && !canvas.HasTextRecognition && longViewport == null) BeginAdjust(-1, PointToClient(canvas.PointToScreen(e.Location)));
            };
            canvas.MouseDoubleClick += delegate { if (canvas.Tool == DrawTool.None && !canvas.HasTextRecognition) Finish(CaptureAction.Copy); };
            Controls.Add(canvas);
#if !SHOTINK_LITE
            textSearch = new OcrSearchPanel(); textSearch.Bind(canvas);
            textSearch.VisibleChanged += delegate { PositionToolbar(); };
            Controls.Add(textSearch);
#endif
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
            toolbar.ScrollFinishClicked += delegate { if (gifSession != null) { toolbar.SetScrollFinishing(); gifSession.Finish(); } else FinishLongCapture(); };
            toolbar.GifClicked += StartGifCapture;
            toolbar.ToolbarMoved += delegate { PositionToolbar(); UpdateRecordingRegion(); };
#if !SHOTINK_LITE
            toolbar.RecognitionClicked += async delegate { await recognition.RecognizeAsync(); };
            toolbar.RecognitionHovered += delegate { RapidTextRecognizer.Shared.WarmUpAsync(); };
            toolbar.CopySelectedText += delegate { canvas.CopyRecognizedText(false); };
            toolbar.CopyAllText += delegate { canvas.CopyRecognizedText(true); };
            canvas.TextSelectionChanged += delegate { toolbar.SetTextSelection(canvas.HasTextRecognition, canvas.HasSelectedText); };
#endif
            canvas.Changed += delegate { toolbar.UpdateHistory(canvas.CanUndo, canvas.CanRedo); };
            Controls.Add(toolbar);
            SyncSettings();
            canvas.Focus();
        }

        private void EnsureCaptureUiContext()
        {
            if (InvokeRequired) throw new InvalidOperationException("请在截图窗口线程启动录制。");
            var context = System.Threading.SynchronizationContext.Current;
            if (context == null || context.GetType() == typeof(System.Threading.SynchronizationContext))
                System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        }
        private void SyncSettings() { toolbar.ApplyTo(canvas); }
        private void PositionToolbar()
        {
            if (toolbar == null) return;
            var global = selection; global.Offset(screenBounds.Location);
            var work = Screen.FromRectangle(global).WorkingArea;
            work.Offset(-screenBounds.X, -screenBounds.Y);
            work = Rectangle.Intersect(work, ClientRectangle);
            toolbar.Location = toolbar.UserPlaced ? CaptureToolbar.Clamp(toolbar.Location, work, toolbar.Size) : CaptureToolbar.Place(selection, work, toolbar.Size);
            toolbar.BringToFront();
#if !SHOTINK_LITE
            if (textSearch != null && textSearch.Visible)
            {
                textSearch.Location = new Point(Math.Max(work.Left + 4, Math.Min(selection.Right - textSearch.Width - 8, work.Right - textSearch.Width - 4)),
                    Math.Max(work.Top + 4, Math.Min(selection.Top + 8, work.Bottom - textSearch.Height - 4)));
                textSearch.BringToFront();
            }
#endif
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
            for (int i = 0; i < edgeTargets.Length; i++)
            {
                var edge = edgeTargets[i];
                edge.Visible = ready && longViewport == null && scrollSession == null && gifSession == null;
                edge.BackColor = keyboardEdge == i * 2 + 1 ? Color.DarkOrange : Color.FromArgb(38, 139, 255);
                edge.Bounds = i == 0 ? new Rectangle(selection.Left + 5, selection.Top - 1, Math.Max(1, selection.Width - 10), 2)
                    : i == 1 ? new Rectangle(selection.Right - 1, selection.Top + 5, 2, Math.Max(1, selection.Height - 10))
                    : i == 2 ? new Rectangle(selection.Left + 5, selection.Bottom - 1, Math.Max(1, selection.Width - 10), 2)
                    : new Rectangle(selection.Left - 1, selection.Top + 5, 2, Math.Max(1, selection.Height - 10));
                edge.BringToFront();
            }
            for (int i = 0; i < grips.Length; i++)
            {
                grips[i].Visible = ready && longViewport == null && scrollSession == null && gifSession == null;
                grips[i].Location = new Point(points[i].X - 4, points[i].Y - 4);
                grips[i].BringToFront();
            }
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (dragging && !Capture)
            {
                if (dragMode == -3) { dragging = false; ClearSelection(); Invalidate(true); return; }
                selectionHold.Stop();
                edgeHold.Stop();
                keyboardEdge = -1;
                dragging = false;
                if (ready)
                {
                    selection = dragOriginal;
                    if (longViewport != null) { longViewport.Bounds = selection; ClearLongDragPreview(); longViewport.Show(); }
                    if (canvas != null) canvas.Show();
                    UpdateSelectionChrome(); if (toolbar != null) { toolbar.Show(); PositionToolbar(); }
                }
                else { selection = Rectangle.Empty; modePicker.Visible = !scrolling; }
                Invalidate(true);
            }
        }

        private void ClearSelection()
        {
            selectionOutline = null; freePoints.Clear();
            edgeHold.Stop(); keyboardEdge = -1;
            foreach (var edge in edgeTargets) edge.Hide();
            ClearLongDragPreview();
            if (scrollSession != null) { scrollSession.Dispose(); scrollSession = null; ResetCaptureRegion(); }
            ready = false;
#if !SHOTINK_LITE
            if (recognition != null) { recognition.Dispose(); recognition = null; }
            if (textSearch != null) { Controls.Remove(textSearch); textSearch.Dispose(); textSearch = null; }
#endif
            if (canvas != null) { Controls.Remove(canvas); canvas.Dispose(); canvas = null; }
            if (longViewport != null) { Controls.Remove(longViewport); longViewport.Dispose(); longViewport = null; }
            longLimitReached = false; longAlignmentLost = false;
            if (toolbar != null) { Controls.Remove(toolbar); toolbar.Dispose(); toolbar = null; }
            selection = Rectangle.Empty; sizeHint.Hide();
            if (modePicker != null) { modePicker.Visible = !scrolling; modePicker.BringToFront(); }
            foreach (var grip in grips) grip.Hide();
        }

        private void Finish(CaptureAction action)
        {
            if (!ready || completed || scrollSession != null || gifSession != null) return;
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
            RememberSelection();
            completed = true;
            Hide(); Close();
            if (Completed != null) Completed(image, action); else image.Dispose();
        }

        private static bool KeyboardEditorFocused(Control control)
        {
            if ((control is TextBoxBase || control is NumericUpDown || control is OvalSlider) && control.ContainsFocus) return true;
            foreach (Control child in control.Controls) if (KeyboardEditorFocused(child)) return true;
            return false;
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (NudgeSelection(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private bool NudgeSelection(Keys key)
        {
            if (!ready || scrollSession != null || gifSession != null || longViewport != null || KeyboardEditorFocused(this)
                || (key != Keys.Left && key != Keys.Right && key != Keys.Up && key != Keys.Down)) return false;
            if (dragging && keyboardEdge < 0) return false;
            int dx = key == Keys.Left ? -1 : key == Keys.Right ? 1 : 0;
            int dy = key == Keys.Up ? -1 : key == Keys.Down ? 1 : 0;
            var previous = selection;
            var next = keyboardEdge < 0 ? SelectionGeometry.Move(previous, new Point(dx, dy), ClientRectangle)
                : SelectionGeometry.Resize(previous, keyboardEdge, new Point(dx, dy), ClientRectangle);
            if (next != previous)
            {
                selection = next;
                canvas.Reframe(desktop.Clone(selection, desktop.PixelFormat), keyboardEdge < 0 ? Point.Empty : new Point(previous.Left - selection.Left, previous.Top - selection.Top));
                canvas.Location = selection.Location;
                if (dragging) dragOriginal = selection;
                RememberSelection();
            }
            UpdateSelectionChrome(); PositionToolbar(); Invalidate(true); Update();
            return true;
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyData == Keys.L && !KeyboardEditorFocused(this) && !dragging && scrollSession == null && gifSession == null && longViewport == null)
            { SetSelectionMode(!freeSelectionMode); e.SuppressKeyPress = true; return; }
            if (NudgeSelection(e.KeyData)) { e.SuppressKeyPress = true; return; }
            if (gifSession != null)
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.Enter) { toolbar.SetScrollFinishing(); gifSession.Finish(); }
                e.SuppressKeyPress = true; return;
            }
#if !SHOTINK_LITE
            if (textSearch != null && textSearch.HandleKey(e)) return;
#endif
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
        protected override void Dispose(bool disposing) { if (disposing) { DisposePaintCache(); ClearLongDragPreview(); if (gifSession != null) gifSession.Dispose(); if (scrollWheel != null) { scrollWheel.Dispose(); scrollWheel = null; } if (scrollSession != null) scrollSession.Dispose();
#if !SHOTINK_LITE
            if (recognition != null) recognition.Dispose();
#endif
            desktop.Dispose(); } base.Dispose(disposing); }

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
