using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ShotInk
{
    internal enum DrawTool { Rectangle, Ellipse, Arrow, Pen, Text, Mosaic, Highlight, Eraser, None, Line, Polyline, ArrowPolyline }
    internal enum ApplyMode { Brush, Rectangle }
    internal enum MosaicEffect { Pixelate, GaussianBlur }

    internal sealed class Mark
    {
        internal DrawTool Tool;
        internal Color Color;
        internal float Width;
        internal string Text;
        internal bool Freehand;
        internal int MosaicSize;
        internal MosaicEffect MosaicEffect;
        internal int BlurRadius = 8;
        internal float BrushSize = 24;
        internal float FontSize;
        internal FontStyle TextStyle = FontStyle.Regular;
        internal SizeF TextSize;
        internal float TextAngle;
        internal Mark Clone()
        {
            var copy = new Mark { Tool = Tool, Color = Color, Width = Width, Text = Text, Freehand = Freehand,
                MosaicSize = MosaicSize, MosaicEffect = MosaicEffect, BlurRadius = BlurRadius, BrushSize = BrushSize, FontSize = FontSize, TextStyle = TextStyle,
                TextSize = TextSize, TextAngle = TextAngle };
            copy.Points.AddRange(Points); return copy;
        }
        internal readonly List<PointF> Points = new List<PointF>();
        internal RectangleF Bounds
        {
            get
            {
                if (Points.Count == 0) return RectangleF.Empty;
                float left = Points[0].X, top = Points[0].Y, right = left, bottom = top;
                foreach (var point in Points)
                { left = Math.Min(left, point.X); top = Math.Min(top, point.Y); right = Math.Max(right, point.X); bottom = Math.Max(bottom, point.Y); }
                var bounds = RectangleF.FromLTRB(left, top, right, bottom);
                if (Freehand) bounds.Inflate(BrushSize / 2, BrushSize / 2);
                return bounds;
            }
        }
    }

    internal sealed partial class AnnotationCanvas : Control
    {
        internal Bitmap Image { get; private set; }
        internal readonly List<Mark> Marks = new List<Mark>();
        private DrawTool tool = DrawTool.Rectangle;
        internal DrawTool Tool
        {
            get { return tool; }
            set { CommitTextEdit(); CompletePolyline(); CancelMosaicTransform(); selectedMosaic = null; EndTextTransform(); selectedText = null; CancelGesture(); tool = value; ClearTextRecognition(); UpdateToolCursor(); }
        }
        internal ApplyMode MosaicMode = ApplyMode.Brush;
        internal ApplyMode EraserMode = ApplyMode.Brush;
        internal int MosaicSize = 14;
        internal MosaicEffect MosaicEffect;
        internal int BlurRadius = 8;
        internal float MosaicBrushSize = 24;
        internal float EraserBrushSize = 24;
        internal Color Ink = Color.FromArgb(244, 75, 95);
        internal float Stroke = 3;
        internal float Zoom = 1;
        private Size documentFrame;
        private VScrollBar documentScroll;
        internal float DocumentOffsetY { get; private set; }
        private Mark active;
        private OcrSelectionModel recognition;
        private bool selectingText;
        private bool draggedText;
        private PointF textAnchor;
        internal int Revision { get; private set; }
        internal bool HasTextRecognition { get { return recognition != null; } }
        internal bool HasSelectedText { get { return recognition != null && recognition.HasSelection; } }
        internal string RecognizedText { get { return recognition == null ? "" : recognition.AllText; } }
        internal event Action TextSelectionChanged;
        internal void SetTextRecognition(OcrDocument document)
        {
            if (document.Width != Image.Width || document.Height != Image.Height) throw new InvalidOperationException("文字坐标与图片尺寸不匹配。");
            CommitTextEdit(); selectedText = null; CancelGesture(); tool = DrawTool.None; recognition = new OcrSelectionModel(document); recognition.SelectAll(); UpdateToolCursor(); NotifyTextSelection();
        }
        private void ClearTextRecognition()
        { if (recognition == null) return; recognition = null; selectingText = false; NotifyTextSelection(); }
        private void NotifyTextSelection() { Invalidate(); if (TextSelectionChanged != null) TextSelectionChanged(); }
        internal void CopyRecognizedText(bool all)
        {
            string text = recognition == null ? "" : all ? recognition.AllText : recognition.SelectedText;
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetDataObject(text, true, 5, 100); }
            catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(FindForm(), "剪贴板正在使用，请稍后重试。", "复制文字"); }
        }
        internal bool HandleRecognitionKey(KeyEventArgs e)
        {
            if (recognition == null) return false;
            if (e.Control && e.KeyCode == Keys.C) CopyRecognizedText(false);
            else if (e.Control && e.KeyCode == Keys.A) { recognition.SelectAll(); NotifyTextSelection(); }
            else if (e.KeyCode == Keys.Escape) Tool = DrawTool.None;
            else return false;
            e.SuppressKeyPress = true; return true;
        }
        private PointF hoverPoint;
        private bool hovering;
        private readonly Stack<List<Mark>> undo = new Stack<List<Mark>>();
        private readonly Stack<List<Mark>> redo = new Stack<List<Mark>>();
        internal bool CanUndo { get { return Marks.Count > 0 || undo.Count > 0; } }
        internal bool CanRedo { get { return redo.Count > 0; } }
        internal event Action Changed;

        internal AnnotationCanvas(Bitmap image)
        {
            Image = image;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            BackColor = Color.White;
            SetZoom(1);
        }

        internal void SetZoom(float value)
        {
            CommitTextEdit();
            float offset = DocumentOffsetY;
            Zoom = Math.Max(0.05F, Math.Min(3F, value));
            Size = documentFrame.IsEmpty ? new Size(Math.Max(1, (int)Math.Ceiling(Image.Width * Zoom)), Math.Max(1, (int)Math.Ceiling(Image.Height * Zoom))) : documentFrame;
            if (documentScroll != null)
                UpdateDocumentViewport(DocumentOffsetX, offset);
            Invalidate();
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (documentScroll != null)
            {
                if ((ModifierKeys & Keys.Shift) != 0) ScrollDocumentHorizontal(-e.Delta / 120 * 80);
                else ScrollDocument(-e.Delta / 120 * 80);
                var handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true;
            }
            base.OnMouseWheel(e);
        }
        private PointF ImagePoint(Point p) { return new PointF(Math.Max(0, Math.Min(Image.Width - 1, p.X / Zoom + DocumentOffsetX)), Math.Max(0, Math.Min(Image.Height - 1, p.Y / Zoom + DocumentOffsetY))); }

        private bool BrushTool
        { get { return Tool == DrawTool.Mosaic && MosaicMode == ApplyMode.Brush || Tool == DrawTool.Eraser && EraserMode == ApplyMode.Brush; } }
        private float CurrentBrushSize { get { return Tool == DrawTool.Eraser ? EraserBrushSize : MosaicBrushSize; } }
        internal void UpdateToolCursor() { Cursor = HasTextRecognition || Tool == DrawTool.Text ? Cursors.IBeam : Tool == DrawTool.None ? Cursors.SizeAll : Cursors.Cross; Invalidate(); }
        private void CancelGesture() { active = null; selectingText = false; Capture = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (MosaicMouseDown(e)) return;
            if (PolylineMouseDown(e)) return;
            if (e.Button == MouseButtons.Left && Tool == DrawTool.Text) { TextMouseDown(e); return; }
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && recognition != null)
            {
                Focus();
                if (e.Clicks > 1) { selectingText = false; Capture = false; recognition.SelectWord(ImagePoint(e.Location), true); NotifyTextSelection(); return; }
                textAnchor = ImagePoint(e.Location); selectingText = true; draggedText = false; Capture = true; return;
            }
            if (e.Button != MouseButtons.Left || Tool == DrawTool.None) return;
            var point = ImagePoint(e.Location);
            active = new Mark { Tool = Tool, Color = Ink, Width = Stroke, Freehand = BrushTool, MosaicSize = MosaicSize, MosaicEffect = MosaicEffect, BlurRadius = BlurRadius, BrushSize = CurrentBrushSize };
            active.Points.Add(point);
            active.Points.Add(point);
            Capture = true;
            Invalidate();
        }

        private void MoveGesture(PointF point)
        {
            if (active.Freehand || active.Tool == DrawTool.Pen || active.Tool == DrawTool.Highlight) active.Points.Add(point);
            else active.Points[active.Points.Count - 1] = point;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (MosaicMouseMove(e)) return;
            if (Tool == DrawTool.Text) { TextMouseMove(e); return; }
            hoverPoint = ImagePoint(e.Location); hovering = true;
            if (selectingText && recognition != null)
            {
                if (Math.Abs(hoverPoint.X - textAnchor.X) + Math.Abs(hoverPoint.Y - textAnchor.Y) > 3 / Zoom) draggedText = true;
                if (draggedText) { recognition.SelectRectangle(RectangleF.FromLTRB(Math.Min(textAnchor.X, hoverPoint.X), Math.Min(textAnchor.Y, hoverPoint.Y), Math.Max(textAnchor.X, hoverPoint.X), Math.Max(textAnchor.Y, hoverPoint.Y))); NotifyTextSelection(); }
                return;
            }
            if (active != null) MoveGesture(hoverPoint);
            if (active != null || BrushTool) Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e)
        { base.OnMouseLeave(e); hovering = false; Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) CancelMosaicTransform();
            if (!Capture) EndTextTransform();
            if (!Capture) { selectingText = false; if (active != null && !IsPolyline(active.Tool)) { active = null; Invalidate(); } }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (FinishMosaicTransform(e)) return;
            if (IsPolyline(Tool)) { Capture = false; return; }
            if (Tool == DrawTool.Text && e.Button == MouseButtons.Left) { TextMouseUp(e); return; }
            if (selectingText && recognition != null && e.Button == MouseButtons.Left)
            {
                OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, e.X, e.Y, 0));
                if (!draggedText) recognition.SelectWord(ImagePoint(e.Location), false);
                selectingText = false; Capture = false; NotifyTextSelection(); return;
            }
            if (active == null || e.Button != MouseButtons.Left) return;
            MoveGesture(ImagePoint(e.Location));
            var mark = active; active = null; Capture = false;
            if ((mark.Tool == DrawTool.Mosaic || mark.Tool == DrawTool.Eraser) && !mark.Freehand && (mark.Bounds.Width <= 0 || mark.Bounds.Height <= 0)) { Invalidate(); return; }
            AddMark(mark);
            if (mark.Tool == DrawTool.Mosaic && !mark.Freehand) SelectMosaic(mark);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (Tool == DrawTool.Text && e.Button == MouseButtons.Left && selectedText != null) BeginTextEdit(selectedText);
            if (recognition != null && e.Button == MouseButtons.Left) { recognition.SelectWord(ImagePoint(e.Location), true); NotifyTextSelection(); }
        }

        internal void AddMark(Mark mark)
        {
            SaveUndo(); Marks.Add(mark); NotifyChanged();
        }
        private List<Mark> Snapshot() { return Marks.ConvertAll(delegate(Mark mark) { return mark.Tool == DrawTool.Text || mark.Tool == DrawTool.Mosaic ? mark.Clone() : mark; }); }
        private void SaveUndo() { undo.Push(Snapshot()); redo.Clear(); }
        internal void Undo()
        {
            if (HasPolyline) { CancelGesture(); return; }
            CancelMosaicTransform(); selectedMosaic = null;
            CommitTextEdit(); EndTextTransform(); selectedText = null;
            CancelGesture();
            if (!CanUndo) return;
            redo.Push(Snapshot());
            if (undo.Count > 0) { var state = undo.Pop(); Marks.Clear(); Marks.AddRange(state); }
            else Marks.RemoveAt(Marks.Count - 1);
            NotifyChanged();
        }
        internal void Redo()
        {
            CancelMosaicTransform(); selectedMosaic = null;
            CommitTextEdit(); EndTextTransform(); selectedText = null;
            CancelGesture();
            if (redo.Count == 0) return;
            undo.Push(Snapshot()); var state = redo.Pop(); Marks.Clear(); Marks.AddRange(state); NotifyChanged();
        }
        internal void Reframe(Bitmap image, Point shift)
        {
            CommitTextEdit(); EndTextTransform(); selectedText = null;
            Revision++; ClearTextRecognition();
            var unique = new HashSet<Mark>(Marks);
            foreach (var state in undo) unique.UnionWith(state);
            foreach (var state in redo) unique.UnionWith(state);
            foreach (var mark in unique)
                for (int i = 0; i < mark.Points.Count; i++) mark.Points[i] = new PointF(mark.Points[i].X + shift.X, mark.Points[i].Y + shift.Y);
            ClearMosaicCache(); var previous = Image; Image = image; previous.Dispose(); SetZoom(Zoom);
        }
        private void NotifyChanged() { Revision++; ClearTextRecognition(); Invalidate(); if (Changed != null) Changed(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (documentScroll != null) e.Graphics.SetClip(new Rectangle(Point.Empty, documentClient));
            e.Graphics.ScaleTransform(Zoom, Zoom);
            e.Graphics.TranslateTransform(-DocumentOffsetX, -DocumentOffsetY);
            if (Zoom == 1) e.Graphics.DrawImageUnscaled(Image, Point.Empty);
            else e.Graphics.DrawImage(Image, new Rectangle(0, 0, Image.Width, Image.Height));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var mark in Marks) DrawMark(e.Graphics, textEditor != null && mark == selectedText ? EditingTextMark() : mark);
            DrawEditingText(e.Graphics);
            DrawTextSelection(e.Graphics);
            if (active != null)
            {
                DrawMark(e.Graphics, active);
                if ((active.Tool == DrawTool.Mosaic || active.Tool == DrawTool.Eraser) && !active.Freehand)
                    using (var outline = new Pen(Color.LightGray, 1 / Zoom))
                    { outline.DashStyle = DashStyle.Dash; var bounds = active.Bounds; e.Graphics.DrawRectangle(outline, bounds.X, bounds.Y, bounds.Width, bounds.Height); }
            }
            if (recognition != null) recognition.Draw(e.Graphics);
            DrawMosaicSelection(e.Graphics);
            if (hovering && BrushTool)
            {
                float radius = CurrentBrushSize / 2;
                var circle = new RectangleF(hoverPoint.X - radius, hoverPoint.Y - radius, radius * 2, radius * 2);
                using (var white = new Pen(Color.White, 3 / Zoom)) e.Graphics.DrawEllipse(white, circle);
                using (var black = new Pen(Color.Black, 1 / Zoom)) e.Graphics.DrawEllipse(black, circle);
            }
        }

        private static Region AppliedRegion(Mark mark)
        {
            if (!mark.Freehand) return new Region(mark.Bounds);
            var region = new Region(); region.MakeEmpty();
            try
            {
                float diameter = Math.Max(1, mark.BrushSize), radius = diameter / 2;
                using (var path = new GraphicsPath())
                {
                    for (int i = 0; i < mark.Points.Count; i++)
                    {
                        var point = mark.Points[i];
                        path.Reset(); path.AddEllipse(point.X - radius, point.Y - radius, diameter, diameter); region.Union(path);
                        if (i == 0) continue;
                        var previous = mark.Points[i - 1];
                        float dx = point.X - previous.X, dy = point.Y - previous.Y;
                        float length = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (length <= 0) continue;
                        float nx = -dy * radius / length, ny = dx * radius / length;
                        path.Reset();
                        path.AddPolygon(new[] { new PointF(previous.X + nx, previous.Y + ny), new PointF(point.X + nx, point.Y + ny),
                            new PointF(point.X - nx, point.Y - ny), new PointF(previous.X - nx, previous.Y - ny) });
                        // Union the swept area so overlapping strokes never cancel coverage.
                        region.Union(path);
                    }
                }
                return region;
            }
            catch { region.Dispose(); throw; }
        }

        private void DrawAppliedMark(Graphics g, Mark mark)
        {
            var state = g.Save();
            try
            {
                using (var region = AppliedRegion(mark))
                {
                    g.SetClip(region, CombineMode.Intersect);
                    if (mark.Tool == DrawTool.Eraser)
                    {
                        // Restore the frozen screenshot only inside the brush or selection.
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImage(Image, new Rectangle(Point.Empty, Image.Size), 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel);
                        return;
                    }
                    DrawMosaicPixels(g, mark);
                }
            }
            finally { g.Restore(state); }
        }

        private void DrawMark(Graphics g, Mark mark)
        {
            if (mark.Points.Count == 0) return;
            if (mark.Tool == DrawTool.Mosaic || mark.Tool == DrawTool.Eraser) { DrawAppliedMark(g, mark); return; }
            var bounds = mark.Bounds;
            using (var pen = new Pen(mark.Color, mark.Width))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                switch (mark.Tool)
                {
                    case DrawTool.Rectangle:
                        if (bounds.Width > 0 && bounds.Height > 0) g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                        break;
                    case DrawTool.Ellipse:
                        if (bounds.Width > 0 && bounds.Height > 0) g.DrawEllipse(pen, bounds);
                        break;
                    case DrawTool.Line:
                    case DrawTool.Polyline:
                        if (mark.Points.Count > 1) g.DrawLines(pen, mark.Points.ToArray());
                        break;
                    case DrawTool.Arrow:
                    case DrawTool.ArrowPolyline:
                        if (mark.Points.Count > 1)
                            using (var arrow = new AdjustableArrowCap(4, 5, true))
                            using (var path = new GraphicsPath())
                            { pen.CustomEndCap = arrow; path.AddLines(mark.Points.ToArray()); g.DrawPath(pen, path); }
                        break;
                    case DrawTool.Pen:
                        if (mark.Points.Count > 1) g.DrawLines(pen, mark.Points.ToArray());
                        break;
                    case DrawTool.Highlight:
                        pen.Color = Color.FromArgb(88, mark.Color); pen.Width = mark.Width * 6;
                        if (mark.Points.Count > 1) g.DrawLines(pen, mark.Points.ToArray());
                        break;
                    case DrawTool.Text:
                        TextGeometry.Draw(g, mark);
                        break;
                }
            }
        }

        internal Bitmap Render()
        {
            CommitTextEdit(); CompletePolyline(); EndTextTransform();
            var result = new Bitmap(Image.Width, Image.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(result))
            {
                g.DrawImageUnscaled(Image, 0, 0);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (var mark in Marks) DrawMark(g, mark);
            }
            return result;
        }
        internal void DrawAnnotations(Graphics graphics)
        { graphics.SmoothingMode = SmoothingMode.AntiAlias; foreach (var mark in Marks) DrawMark(graphics, mark); }

        protected override void Dispose(bool disposing) { if (disposing) { ClearMosaicCache(); if (textEditorFont != null) { textEditorFont.Dispose(); textEditorFont = null; } Image.Dispose(); } base.Dispose(disposing); }
    }

    internal sealed class EditorForm : Form
    {
        private readonly AnnotationCanvas canvas;
        private readonly Panel viewport;
        private readonly Label status;
        private readonly Label zoomLabel;
        private readonly List<Button> toolButtons = new List<Button>();
        private readonly AnnotationOptions toolOptions;
        private readonly Panel toolArea;
        private readonly OcrSelectionActions textActions;
        private readonly TextRecognitionSession recognition;
        private readonly Action<Bitmap> pin;
        private readonly AppSettings settings;
        private bool dirty;

        internal EditorForm(Bitmap image, Action<Bitmap> pin, AppSettings settings = null)
        {
            this.settings = settings ?? new AppSettings();
            this.pin = pin;
            Text = "截图编辑 · ShotInk";
            MainForm.UseAppIcon(this);
            Size = new Size(1100, 760);
            MinimumSize = new Size(850, 500);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background;
            Font = new Font("Microsoft YaHei UI", 9);
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
            var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 106, Padding = new Padding(9), BackColor = Color.White, WrapContents = true };
            string[] labels = { "矩形", "椭圆", "箭头", "画笔", "文字", "马赛克", "荧光笔", "橡皮擦" };
            for (int i = 0; i < labels.Length; i++)
            {
                var selectedTool = (DrawTool)i;
                var button = Theme.Button(labels[i], i == 0, 69);
                button.Click += delegate { toolOptions.UseDefaultMode(selectedTool); SelectTool(selectedTool); };
                toolButtons.Add(button); tools.Controls.Add(button);
            }
            var recognize = Theme.Button("文字识别", false, 88);
            recognize.Click += async delegate { await recognition.RecognizeAsync(); };
            recognize.MouseEnter += delegate { RapidTextRecognizer.Shared.WarmUpAsync(); };
            tools.Controls.Add(recognize);
            AddButton(tools, "撤销", false, 66, delegate { canvas.Undo(); });
            AddButton(tools, "重做", false, 66, delegate { canvas.Redo(); });
            AddButton(tools, "复制", false, 66, delegate { CopyImage(); });
            AddButton(tools, "贴图", false, 66, delegate { this.pin(canvas.Render()); });
            AddButton(tools, "保存 PNG", true, 100, delegate { SaveImage(); });
            AddButton(tools, "−", false, 40, delegate { Zoom(canvas.Zoom / 1.25F); });
            zoomLabel = Theme.Label("100%", 9, Theme.Muted); zoomLabel.Margin = new Padding(4, 16, 4, 0); tools.Controls.Add(zoomLabel);
            AddButton(tools, "+", false, 40, delegate { Zoom(canvas.Zoom * 1.25F); });
            AddButton(tools, "适应宽度", false, 95, delegate { Fit(); });
            viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20), BackColor = Color.FromArgb(229, 233, 241) };
            canvas = new AnnotationCanvas(image) { Location = new Point(20, 20) };
            canvas.Changed += delegate { dirty = true; UpdateStatus("已编辑"); };
            viewport.Controls.Add(canvas);
            status = Theme.Label("", 9, Theme.Muted); status.Dock = DockStyle.Bottom; status.Height = 34; status.Padding = new Padding(12, 8, 0, 0);
            toolOptions = new AnnotationOptions(this.settings) { Location = new Point(9, 106), Width = 440 };
            toolOptions.SettingsChanged += delegate { toolOptions.ApplyTo(canvas); };
            toolOptions.ToolChanged += SelectTool;
            toolArea = new Panel { Dock = DockStyle.Top, BackColor = Color.White };
            textActions = new OcrSelectionActions { Location = new Point(9, 106), Visible = false };
            textActions.CopySelected += delegate { canvas.CopyRecognizedText(false); };
            textActions.CopyAll += delegate { canvas.CopyRecognizedText(true); };
            textActions.ExitSelection += delegate { SelectTool(DrawTool.None); };
            recognition = new TextRecognitionSession(this, canvas.Render, delegate { return canvas.Revision; },
                delegate(bool busy) { recognize.Enabled = !busy; recognize.Text = busy ? "识别中…" : "文字识别"; },
                delegate(OcrDocument document) { SelectTool(DrawTool.None); canvas.SetTextRecognition(document); canvas.Focus(); });
            canvas.TextSelectionChanged += delegate
            {
                textActions.SetSelection(canvas.HasSelectedText);
                textActions.Visible = canvas.HasTextRecognition;
                if (canvas.HasTextRecognition) { toolOptions.Hide(); toolArea.Height = 110 + textActions.Height; }
                else { toolOptions.ShowTool(canvas.Tool); toolArea.Height = 110 + toolOptions.Height; }
            };
            toolArea.Controls.Add(tools); toolArea.Controls.Add(toolOptions); toolArea.Controls.Add(textActions);
            Controls.Add(viewport); Controls.Add(status); Controls.Add(toolArea);
            SelectTool(DrawTool.Rectangle);
            Shown += delegate { Fit(); UpdateStatus("截图已复制；编辑后可再次复制或保存"); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!dirty) return;
                var choice = MessageBox.Show(this, "标注尚未导出，是否保存后关闭？", "保存标注", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (choice == DialogResult.Cancel) e.Cancel = true;
                if (choice == DialogResult.Yes && !SaveImage()) e.Cancel = true;
            };
        }

        private void AddButton(FlowLayoutPanel panel, string text, bool primary, int width, Action action)
        { var b = Theme.Button(text, primary, width); b.Click += delegate { action(); }; panel.Controls.Add(b); }
        private void SelectTool(DrawTool tool)
        {
            canvas.Tool = tool; textActions.Hide();
            toolOptions.ShowTool(tool); toolOptions.ApplyTo(canvas);
            toolArea.Height = 110 + toolOptions.Height;
            for (int i = 0; i < toolButtons.Count; i++)
            {
                bool chosen = i == (int)tool || (tool == DrawTool.Line || tool == DrawTool.Polyline || tool == DrawTool.ArrowPolyline) && i == (int)DrawTool.Arrow;
                toolButtons[i].BackColor = chosen ? Theme.Accent : Color.White; toolButtons[i].ForeColor = chosen ? Color.White : Theme.Ink;
            }
            canvas.Focus();
        }
        private void Zoom(float value) { canvas.SetZoom(value); zoomLabel.Text = Math.Round(canvas.Zoom * 100) + "%"; }
        private void Fit() { Zoom(Math.Min(1, (viewport.ClientSize.Width - 60F) / canvas.Image.Width)); }
        private void UpdateStatus(string message) { status.Text = canvas.Image.Width + " × " + canvas.Image.Height + " px  ·  " + message + "  ·  Ctrl+S 保存 / Ctrl+C 复制 / Ctrl+Z 撤销"; }
        private void CopyImage()
        {
            using (var image = canvas.Render()) Clipboard.SetDataObject(image, true, 5, 100);
            dirty = false;
            UpdateStatus("已复制编辑后的图片");
        }
        private bool SaveImage()
        {
            try
            {
                string path;
                using (var image = canvas.Render()) if (!ImageExport.Save(this, image, settings, out path)) return false;
                dirty = false;
                UpdateStatus("已保存：" + Path.GetFileName(path));
                return true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法保存图片"); return false; }
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (canvas.HandleGestureKey(e)) return;
            if (canvas.HandleTextKey(e)) return;
            if (canvas.HandleRecognitionKey(e)) return;
            if (e.Control && e.KeyCode == Keys.S) { SaveImage(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.C) { CopyImage(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.Z) { canvas.Undo(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.Y) { canvas.Redo(); e.SuppressKeyPress = true; }
            base.OnKeyDown(e);
        }
        protected override void Dispose(bool disposing)
        { if (disposing && recognition != null) recognition.Dispose(); base.Dispose(disposing); }
    }

}
