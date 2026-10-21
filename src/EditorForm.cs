using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ShotInk
{
    internal enum DrawTool { Rectangle, Ellipse, Arrow, Pen, Text, Mosaic, Highlight, Eraser, None, Line }
    internal enum ApplyMode { Brush, Rectangle }

    internal sealed class Mark
    {
        internal DrawTool Tool;
        internal Color Color;
        internal float Width;
        internal string Text;
        internal bool Freehand;
        internal int MosaicSize;
        internal float BrushSize = 24;
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

    internal sealed class AnnotationCanvas : Control
    {
        internal Bitmap Image { get; private set; }
        internal readonly List<Mark> Marks = new List<Mark>();
        private DrawTool tool = DrawTool.Rectangle;
        internal DrawTool Tool
        {
            get { return tool; }
            set { CancelGesture(); tool = value; UpdateToolCursor(); }
        }
        internal ApplyMode MosaicMode = ApplyMode.Brush;
        internal ApplyMode EraserMode = ApplyMode.Brush;
        internal int MosaicSize = 14;
        internal float MosaicBrushSize = 24;
        internal float EraserBrushSize = 24;
        internal Color Ink = Color.FromArgb(244, 75, 95);
        internal float Stroke = 3;
        internal float Zoom = 1;
        private Mark active;
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
            Zoom = Math.Max(0.05F, Math.Min(3F, value));
            Size = new Size(Math.Max(1, (int)Math.Ceiling(Image.Width * Zoom)), Math.Max(1, (int)Math.Ceiling(Image.Height * Zoom)));
            Invalidate();
        }

        private PointF ImagePoint(Point p) { return new PointF(Math.Max(0, Math.Min(Image.Width - 1, p.X / Zoom)), Math.Max(0, Math.Min(Image.Height - 1, p.Y / Zoom))); }

        private bool BrushTool
        { get { return Tool == DrawTool.Mosaic && MosaicMode == ApplyMode.Brush || Tool == DrawTool.Eraser && EraserMode == ApplyMode.Brush; } }
        private float CurrentBrushSize { get { return Tool == DrawTool.Eraser ? EraserBrushSize : MosaicBrushSize; } }
        internal void UpdateToolCursor() { Cursor = Tool == DrawTool.None ? Cursors.SizeAll : Cursors.Cross; Invalidate(); }
        private void CancelGesture() { active = null; Capture = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || Tool == DrawTool.None) return;
            var point = ImagePoint(e.Location);
            active = new Mark { Tool = Tool, Color = Ink, Width = Stroke, Freehand = BrushTool, MosaicSize = MosaicSize, BrushSize = CurrentBrushSize };
            active.Points.Add(point);
            if (Tool == DrawTool.Text)
            {
                var text = TextPrompt.Ask(FindForm());
                if (!string.IsNullOrWhiteSpace(text)) { active.Text = text; AddMark(active); }
                active = null;
                return;
            }
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
            hoverPoint = ImagePoint(e.Location); hovering = true;
            if (active != null) MoveGesture(hoverPoint);
            if (active != null || BrushTool) Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e)
        { base.OnMouseLeave(e); hovering = false; Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && active != null) { active = null; Invalidate(); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (active == null || e.Button != MouseButtons.Left) return;
            MoveGesture(ImagePoint(e.Location));
            var mark = active; active = null; Capture = false;
            if ((mark.Tool == DrawTool.Mosaic || mark.Tool == DrawTool.Eraser) && !mark.Freehand && (mark.Bounds.Width <= 0 || mark.Bounds.Height <= 0)) { Invalidate(); return; }
            AddMark(mark);
        }

        internal void AddMark(Mark mark)
        {
            SaveUndo(); Marks.Add(mark); NotifyChanged();
        }
        private void SaveUndo() { undo.Push(new List<Mark>(Marks)); redo.Clear(); }
        internal void Undo()
        {
            CancelGesture();
            if (!CanUndo) return;
            redo.Push(new List<Mark>(Marks));
            if (undo.Count > 0) { var state = undo.Pop(); Marks.Clear(); Marks.AddRange(state); }
            else Marks.RemoveAt(Marks.Count - 1);
            NotifyChanged();
        }
        internal void Redo()
        {
            CancelGesture();
            if (redo.Count == 0) return;
            undo.Push(new List<Mark>(Marks)); var state = redo.Pop(); Marks.Clear(); Marks.AddRange(state); NotifyChanged();
        }
        internal void Reframe(Bitmap image, Point shift)
        {
            var unique = new HashSet<Mark>(Marks);
            foreach (var state in undo) unique.UnionWith(state);
            foreach (var state in redo) unique.UnionWith(state);
            foreach (var mark in unique)
                for (int i = 0; i < mark.Points.Count; i++) mark.Points[i] = new PointF(mark.Points[i].X + shift.X, mark.Points[i].Y + shift.Y);
            var previous = Image; Image = image; previous.Dispose(); SetZoom(Zoom);
        }
        private void NotifyChanged() { Invalidate(); if (Changed != null) Changed(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.ScaleTransform(Zoom, Zoom);
            e.Graphics.DrawImage(Image, new Rectangle(0, 0, Image.Width, Image.Height));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var mark in Marks) DrawMark(e.Graphics, mark);
            if (active != null)
            {
                DrawMark(e.Graphics, active);
                if ((active.Tool == DrawTool.Mosaic || active.Tool == DrawTool.Eraser) && !active.Freehand)
                    using (var outline = new Pen(Color.DodgerBlue, 1 / Zoom))
                    { outline.DashStyle = DashStyle.Dash; var bounds = active.Bounds; e.Graphics.DrawRectangle(outline, bounds.X, bounds.Y, bounds.Width, bounds.Height); }
            }
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
            using (var path = new GraphicsPath())
            {
                var points = new List<PointF>();
                foreach (var point in mark.Points)
                    if (points.Count == 0 || points[points.Count - 1] != point) points.Add(point);
                float diameter = Math.Max(1, mark.BrushSize), radius = diameter / 2;
                if (points.Count > 1)
                {
                    path.AddLines(points.ToArray());
                    using (var pen = new Pen(Color.Black, diameter) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) path.Widen(pen);
                }
                // Widen cannot cover a stationary click; explicit caps also cover the endpoints.
                if (points.Count > 0)
                {
                    var first = points[0]; var last = points[points.Count - 1];
                    path.AddEllipse(first.X - radius, first.Y - radius, diameter, diameter);
                    path.AddEllipse(last.X - radius, last.Y - radius, diameter, diameter);
                }
                path.FillMode = FillMode.Winding;
                return new Region(path);
            }
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
                    int block = mark.MosaicSize > 0 ? mark.MosaicSize : Math.Max(8, (int)mark.Width * 5);
                    var bounds = mark.Bounds;
                    var clipped = Rectangle.Intersect(Rectangle.FromLTRB((int)Math.Floor(bounds.Left), (int)Math.Floor(bounds.Top), (int)Math.Ceiling(bounds.Right), (int)Math.Ceiling(bounds.Bottom)), new Rectangle(Point.Empty, Image.Size));
                    for (int y = clipped.Top; y < clipped.Bottom; y += block)
                        for (int x = clipped.Left; x < clipped.Right; x += block)
                        {
                            var cell = new Rectangle(x, y, Math.Min(block, clipped.Right - x), Math.Min(block, clipped.Bottom - y));
                            if (!region.IsVisible(cell)) continue;
                            var color = Image.GetPixel(Math.Min(Image.Width - 1, x + block / 2), Math.Min(Image.Height - 1, y + block / 2));
                            using (var brush = new SolidBrush(color)) g.FillRectangle(brush, cell);
                        }
                }
            }
            finally { g.Restore(state); }
        }

        private void DrawMark(Graphics g, Mark mark)
        {
            if (mark.Points.Count == 0) return;
            if (mark.Tool == DrawTool.Mosaic || mark.Tool == DrawTool.Eraser) { DrawAppliedMark(g, mark); return; }
            var first = mark.Points[0];
            var last = mark.Points[mark.Points.Count - 1];
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
                    case DrawTool.Arrow:
                        using (var arrow = new AdjustableArrowCap(4, 5, true)) { pen.CustomEndCap = arrow; g.DrawLine(pen, first, last); }
                        break;
                    case DrawTool.Line:
                        g.DrawLine(pen, first, last);
                        break;
                    case DrawTool.Pen:
                        if (mark.Points.Count > 1) g.DrawLines(pen, mark.Points.ToArray());
                        break;
                    case DrawTool.Highlight:
                        pen.Color = Color.FromArgb(88, mark.Color); pen.Width = mark.Width * 6;
                        if (mark.Points.Count > 1) g.DrawLines(pen, mark.Points.ToArray());
                        break;
                    case DrawTool.Text:
                        using (var font = new Font("Microsoft YaHei UI", Math.Max(14, mark.Width * 6), FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var brush = new SolidBrush(mark.Color)) g.DrawString(mark.Text, font, brush, first);
                        break;
                }
            }
        }

        internal Bitmap Render()
        {
            var result = new Bitmap(Image.Width, Image.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(result))
            {
                g.DrawImageUnscaled(Image, 0, 0);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (var mark in Marks) DrawMark(g, mark);
            }
            return result;
        }

        protected override void Dispose(bool disposing) { if (disposing) Image.Dispose(); base.Dispose(disposing); }
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
        private readonly Action<Bitmap> pin;
        private bool dirty;

        internal EditorForm(Bitmap image, Action<Bitmap> pin)
        {
            this.pin = pin;
            Text = "截图编辑 · ShotInk";
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
                button.Click += delegate { SelectTool(selectedTool); };
                toolButtons.Add(button); tools.Controls.Add(button);
            }
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
            toolOptions = new AnnotationOptions { Location = new Point(9, 106), Width = 440 };
            toolOptions.SettingsChanged += delegate { toolOptions.ApplyTo(canvas); };
            toolOptions.ToolChanged += SelectTool;
            toolArea = new Panel { Dock = DockStyle.Top, BackColor = Color.White };
            toolArea.Controls.Add(tools); toolArea.Controls.Add(toolOptions);
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
            canvas.Tool = tool;
            toolOptions.ShowTool(tool); toolOptions.ApplyTo(canvas);
            toolArea.Height = 110 + toolOptions.Height;
            for (int i = 0; i < toolButtons.Count; i++)
            {
                bool chosen = i == (int)tool || tool == DrawTool.Line && i == (int)DrawTool.Arrow;
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
            using (var dialog = new SaveFileDialog { Filter = "PNG 图片 (*.png)|*.png", FileName = "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                using (var image = canvas.Render()) image.Save(dialog.FileName, ImageFormat.Png);
                dirty = false;
                UpdateStatus("已保存：" + Path.GetFileName(dialog.FileName));
                return true;
            }
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S) { SaveImage(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.C) { CopyImage(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.Z) { canvas.Undo(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.Y) { canvas.Redo(); e.SuppressKeyPress = true; }
            base.OnKeyDown(e);
        }
    }

    internal static class TextPrompt
    {
        internal static string Ask(IWin32Window owner)
        {
            using (var dialog = new Form { Text = "添加文字", ClientSize = new Size(390, 180), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, Font = new Font("Microsoft YaHei UI", 10) })
            {
                var input = new TextBox { Multiline = true, Location = new Point(14, 14), Size = new Size(362, 105) };
                var ok = Theme.Button("添加", true, 84); ok.Location = new Point(292, 130); ok.DialogResult = DialogResult.OK;
                dialog.Controls.Add(input); dialog.Controls.Add(ok); dialog.AcceptButton = ok;
                return dialog.ShowDialog(owner) == DialogResult.OK ? input.Text : null;
            }
        }
    }
}
