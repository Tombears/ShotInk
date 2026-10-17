using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace SnipasteLite
{
    internal enum DrawTool { Rectangle, Ellipse, Arrow, Pen, Text, Mosaic, Highlight, Eraser, None, Line }

    internal sealed class Mark
    {
        internal DrawTool Tool;
        internal Color Color;
        internal float Width;
        internal string Text;
        internal readonly List<PointF> Points = new List<PointF>();
        internal RectangleF Bounds
        {
            get
            {
                var a = Points[0]; var b = Points[Points.Count - 1];
                return RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
            }
        }
    }

    internal sealed class AnnotationCanvas : Control
    {
        internal Bitmap Image { get; private set; }
        internal readonly List<Mark> Marks = new List<Mark>();
        internal DrawTool Tool = DrawTool.Rectangle;
        internal Color Ink = Color.FromArgb(244, 75, 95);
        internal float Stroke = 3;
        internal float Zoom = 1;
        private Mark active;
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (Tool == DrawTool.None) return;
            var point = ImagePoint(e.Location);
            if (Tool == DrawTool.Eraser) { Capture = true; EraseAt(point); return; }
            active = new Mark { Tool = Tool, Color = Ink, Width = Stroke };
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
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Tool == DrawTool.Eraser && Capture) { EraseAt(ImagePoint(e.Location)); return; }
            if (active == null) return;
            var point = ImagePoint(e.Location);
            if (active.Tool == DrawTool.Pen || active.Tool == DrawTool.Highlight) active.Points.Add(point);
            else active.Points[active.Points.Count - 1] = point;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (Tool == DrawTool.Eraser) { Capture = false; return; }
            if (active == null || e.Button != MouseButtons.Left) return;
            var point = ImagePoint(e.Location);
            if (active.Tool == DrawTool.Pen || active.Tool == DrawTool.Highlight) active.Points.Add(point);
            else active.Points[active.Points.Count - 1] = point;
            var mark = active;
            active = null;
            Capture = false;
            AddMark(mark);
        }

        internal void AddMark(Mark mark)
        {
            SaveUndo(); Marks.Add(mark); NotifyChanged();
        }
        private void SaveUndo() { undo.Push(new List<Mark>(Marks)); redo.Clear(); }
        internal void Undo()
        {
            if (!CanUndo) return;
            redo.Push(new List<Mark>(Marks));
            if (undo.Count > 0) { var state = undo.Pop(); Marks.Clear(); Marks.AddRange(state); }
            else Marks.RemoveAt(Marks.Count - 1);
            NotifyChanged();
        }
        internal void Redo()
        {
            if (redo.Count == 0) return;
            undo.Push(new List<Mark>(Marks)); var state = redo.Pop(); Marks.Clear(); Marks.AddRange(state); NotifyChanged();
        }
        internal bool EraseAt(PointF point)
        {
            for (int i = Marks.Count - 1; i >= 0; i--)
            {
                if (!HitMark(Marks[i], point)) continue;
                SaveUndo(); Marks.RemoveAt(i); NotifyChanged(); return true;
            }
            return false;
        }
        private bool HitMark(Mark mark, PointF point)
        {
            if (mark.Points.Count == 0) return false;
            var bounds = mark.Bounds;
            if (mark.Tool == DrawTool.Text)
            {
                using (var g = Graphics.FromImage(Image))
                using (var font = new Font("Microsoft YaHei UI", Math.Max(14, mark.Width * 6), FontStyle.Bold, GraphicsUnit.Pixel))
                    return new RectangleF(mark.Points[0], g.MeasureString(mark.Text, font)).Contains(point);
            }
            if (mark.Tool == DrawTool.Mosaic) return bounds.Contains(point);
            using (var path = new GraphicsPath())
            using (var pen = new Pen(Color.Black, Math.Max(12, mark.Width * (mark.Tool == DrawTool.Highlight ? 6 : 2))))
            {
                if (mark.Tool == DrawTool.Rectangle && bounds.Width > 0 && bounds.Height > 0) path.AddRectangle(bounds);
                else if (mark.Tool == DrawTool.Ellipse && bounds.Width > 0 && bounds.Height > 0) path.AddEllipse(bounds);
                else if (mark.Points.Count > 1) path.AddLines(mark.Points.ToArray());
                return path.PointCount > 0 && path.IsOutlineVisible(point, pen);
            }
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
            if (active != null) DrawMark(e.Graphics, active);
        }

        private void DrawMark(Graphics g, Mark mark)
        {
            if (mark.Points.Count == 0) return;
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
                    case DrawTool.Mosaic:
                        int block = Math.Max(8, (int)mark.Width * 5);
                        var clipped = Rectangle.Intersect(Rectangle.Ceiling(bounds), new Rectangle(Point.Empty, Image.Size));
                        // Opaque blocks cover all pixels, including partial edge blocks.
                        for (int y = clipped.Top; y < clipped.Bottom; y += block)
                            for (int x = clipped.Left; x < clipped.Right; x += block)
                            {
                                var color = Image.GetPixel(Math.Min(Image.Width - 1, x + block / 2), Math.Min(Image.Height - 1, y + block / 2));
                                using (var brush = new SolidBrush(color)) g.FillRectangle(brush, x, y, Math.Min(block, clipped.Right - x), Math.Min(block, clipped.Bottom - y));
                            }
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
        private readonly Action<Bitmap> pin;
        private bool dirty;

        internal EditorForm(Bitmap image, Action<Bitmap> pin)
        {
            this.pin = pin;
            Text = "截图编辑 · Snipaste Lite";
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
            var color = Theme.Button("颜色", false, 66);
            color.Click += delegate { using (var dialog = new ColorDialog { Color = canvas.Ink, FullOpen = true }) if (dialog.ShowDialog(this) == DialogResult.OK) canvas.Ink = dialog.Color; };
            tools.Controls.Add(color);
            var width = new NumericUpDown { Minimum = 1, Maximum = 12, Value = 3, Width = 47, Margin = new Padding(5, 13, 5, 5) };
            width.ValueChanged += delegate { canvas.Stroke = (float)width.Value; };
            tools.Controls.Add(width);
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
            Controls.Add(viewport); Controls.Add(status); Controls.Add(tools);
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
            for (int i = 0; i < toolButtons.Count; i++)
            { toolButtons[i].BackColor = i == (int)tool ? Theme.Accent : Color.White; toolButtons[i].ForeColor = i == (int)tool ? Color.White : Theme.Ink; }
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
