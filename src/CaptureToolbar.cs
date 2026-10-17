using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SnipasteLite
{
    internal enum CaptureAction { Copy, Save, Pin }

    internal sealed class CaptureToolbar : UserControl
    {
        private readonly ToolStrip strip;
        private readonly FlowLayoutPanel options;
        private readonly ToolStripButton undo;
        private readonly ToolStripButton redo;
        private readonly Dictionary<DrawTool, ToolStripItem> tools = new Dictionary<DrawTool, ToolStripItem>();
        private readonly List<Image> icons = new List<Image>();
        private DrawTool selected = DrawTool.None;
        internal Color Ink = Color.FromArgb(239, 64, 76);
        internal float Stroke = 3;
        internal event Action<DrawTool> ToolChanged;
        internal event Action SettingsChanged;
        internal event Action UndoClicked;
        internal event Action RedoClicked;
        internal event Action CancelClicked;
        internal event Action<CaptureAction> FinishClicked;
        internal event Action LayoutChanged;

        internal CaptureToolbar()
        {
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(250, 250, 250);
            strip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.None, AutoSize = false,
                Location = new Point(1, 1), Height = 32, Padding = new Padding(4, 1, 4, 1), CanOverflow = false,
                BackColor = BackColor, Renderer = new ToolStripProfessionalRenderer(new ToolbarColors()) };
            strip.Renderer = new ToolStripProfessionalRenderer(new ToolbarColors()) { RoundedEdges = false };
            var shape = Dropdown("shape", "形状：矩形 / 椭圆", new[] { DrawTool.Rectangle, DrawTool.Ellipse }, new[] { "矩形", "椭圆" });
            tools.Add(DrawTool.Rectangle, shape); tools.Add(DrawTool.Ellipse, shape);
            var arrow = Dropdown("arrow", "线条：箭头 / 直线", new[] { DrawTool.Arrow, DrawTool.Line }, new[] { "箭头", "直线" });
            tools.Add(DrawTool.Arrow, arrow); tools.Add(DrawTool.Line, arrow);
            AddTool("pen", "画笔", DrawTool.Pen);
            AddTool("highlight", "荧光笔", DrawTool.Highlight);
            AddTool("mosaic", "马赛克", DrawTool.Mosaic);
            AddTool("text", "文字", DrawTool.Text);
            AddTool("eraser", "橡皮擦：擦除标注", DrawTool.Eraser);
            strip.Items.Add(new ToolStripSeparator());
            undo = Button("undo", "撤销 (Ctrl+Z)", delegate { if (UndoClicked != null) UndoClicked(); });
            redo = Button("redo", "重做 (Ctrl+Y)", delegate { if (RedoClicked != null) RedoClicked(); });
            undo.Enabled = false; redo.Enabled = false;
            strip.Items.Add(new ToolStripSeparator());
            Button("cancel", "取消 (Esc)", delegate { if (CancelClicked != null) CancelClicked(); });
            Button("pin", "贴到桌面", delegate { Finish(CaptureAction.Pin); });
            Button("save", "保存 PNG (Ctrl+S)", delegate { Finish(CaptureAction.Save); });
            Button("copy", "复制并完成 (Enter / Ctrl+C)", delegate { Finish(CaptureAction.Copy); });
            int width = strip.Padding.Horizontal + 2;
            foreach (ToolStripItem item in strip.Items) width += item is ToolStripSeparator ? 6 : item.Width;
            strip.Width = width;
            Controls.Add(strip);
            options = new FlowLayoutPanel { Location = new Point(5, 35), Height = 30, Width = width - 8, WrapContents = false, Visible = false };
            foreach (var color in new[] { Color.FromArgb(239, 64, 76), Color.FromArgb(255, 174, 38), Color.FromArgb(250, 216, 49), Color.FromArgb(37, 181, 114), Color.FromArgb(47, 135, 246), Color.FromArgb(150, 89, 224), Color.Black, Color.White })
            {
                var swatch = new Button { Width = 19, Height = 19, Margin = new Padding(3, 4, 3, 0), BackColor = color, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Tag = color };
                swatch.FlatAppearance.BorderColor = Color.Gray;
                swatch.Click += delegate(object sender, EventArgs e) { Ink = (Color)((Control)sender).Tag; NotifySettings(); };
                options.Controls.Add(swatch);
            }
            var custom = new Button { Text = "…", Width = 27, Height = 23, Margin = new Padding(3, 2, 8, 0), FlatStyle = FlatStyle.Flat };
            custom.Click += delegate { using (var dialog = new ColorDialog { Color = Ink, FullOpen = true }) if (dialog.ShowDialog(FindForm()) == DialogResult.OK) { Ink = dialog.Color; NotifySettings(); } };
            options.Controls.Add(custom);
            options.Controls.Add(new Label { Text = "粗细", AutoSize = true, Margin = new Padding(1, 6, 3, 0), ForeColor = Color.DimGray });
            var stroke = new NumericUpDown { Minimum = 1, Maximum = 12, Value = 3, Width = 45, Margin = new Padding(1, 3, 0, 0) };
            stroke.ValueChanged += delegate { Stroke = (float)stroke.Value; NotifySettings(); };
            options.Controls.Add(stroke);
            Controls.Add(options);
            Size = new Size(width + 2, 35);
        }

        private ToolStripDropDownButton Dropdown(string icon, string hint, DrawTool[] values, string[] labels)
        {
            var button = new ToolStripDropDownButton { Image = Icon(icon), ToolTipText = hint, DisplayStyle = ToolStripItemDisplayStyle.Image,
                ImageScaling = ToolStripItemImageScaling.None, AutoSize = false, Width = 37, Height = 29, Padding = Padding.Empty };
            for (int i = 0; i < values.Length; i++)
            {
                var tool = values[i];
                button.DropDownItems.Add(labels[i], null, delegate { SelectTool(tool); });
            }
            button.DropDownItems.Add(new ToolStripSeparator());
            button.DropDownItems.Add("移动选区", null, delegate { SelectTool(DrawTool.None); });
            strip.Items.Add(button);
            return button;
        }
        private ToolStripButton Button(string icon, string hint, Action clicked)
        {
            var button = new ToolStripButton { Image = Icon(icon), ToolTipText = hint, DisplayStyle = ToolStripItemDisplayStyle.Image,
                ImageScaling = ToolStripItemImageScaling.None, AutoSize = false, Width = 30, Height = 29, Padding = Padding.Empty };
            button.Click += delegate { clicked(); };
            strip.Items.Add(button); return button;
        }
        private void AddTool(string icon, string hint, DrawTool tool) { tools.Add(tool, Button(icon, hint, delegate { SelectTool(selected == tool ? DrawTool.None : tool); })); }
        private Image Icon(string name) { var image = ToolbarIcons.Create(name); icons.Add(image); return image; }
        internal void SelectTool(DrawTool tool)
        {
            selected = tool;
            foreach (var pair in tools)
            {
                pair.Value.BackColor = Color.Transparent;
                var button = pair.Value as ToolStripButton; if (button != null) button.Checked = false;
            }
            if (tools.ContainsKey(tool))
            {
                tools[tool].BackColor = Color.FromArgb(222, 236, 255);
                var button = tools[tool] as ToolStripButton; if (button != null) button.Checked = true;
            }
            options.Visible = tool != DrawTool.None && tool != DrawTool.Eraser;
            Height = options.Visible ? 66 : 35;
            if (ToolChanged != null) ToolChanged(tool);
            if (LayoutChanged != null) LayoutChanged();
        }
        internal void UpdateHistory(bool canUndo, bool canRedo) { undo.Enabled = canUndo; redo.Enabled = canRedo; }
        private void NotifySettings() { if (SettingsChanged != null) SettingsChanged(); }
        private void Finish(CaptureAction action) { if (FinishClicked != null) FinishClicked(action); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (var pen = new Pen(Color.FromArgb(46, 137, 248))) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) foreach (var image in icons) image.Dispose(); }

        internal static Point Place(Rectangle selection, Rectangle work, Size toolbar)
        {
            int x = Math.Max(work.Left, Math.Min(selection.Right - toolbar.Width, work.Right - toolbar.Width));
            int y = selection.Bottom + 9;
            if (y + toolbar.Height > work.Bottom) y = selection.Top - toolbar.Height - 9;
            y = Math.Max(work.Top, Math.Min(y, work.Bottom - toolbar.Height));
            return new Point(x, y);
        }
        private sealed class ToolbarColors : ProfessionalColorTable
        {
            public override Color ToolStripGradientBegin { get { return Color.FromArgb(250, 250, 250); } }
            public override Color ToolStripGradientMiddle { get { return ToolStripGradientBegin; } }
            public override Color ToolStripGradientEnd { get { return ToolStripGradientBegin; } }
            public override Color ToolStripBorder { get { return ToolStripGradientBegin; } }
            public override Color ButtonSelectedHighlight { get { return Color.FromArgb(222, 236, 255); } }
            public override Color ButtonSelectedBorder { get { return Color.FromArgb(124, 177, 246); } }
        }
    }

    internal static class ToolbarIcons
    {
        internal static Bitmap Create(string name)
        {
            var image = new Bitmap(20, 20);
            using (var g = Graphics.FromImage(image))
            using (var pen = new Pen(Color.FromArgb(65, 68, 74), 1.6F))
            using (var brush = new SolidBrush(pen.Color))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                switch (name)
                {
                    case "shape": g.DrawRectangle(pen, 3, 4, 11, 9); g.FillEllipse(Brushes.White, 9, 10, 8, 6); g.DrawEllipse(pen, 9, 10, 8, 6); break;
                    case "arrow": g.DrawLines(pen, new[] { new Point(3, 14), new Point(8, 8), new Point(11, 11), new Point(17, 4) }); g.DrawLines(pen, new[] { new Point(12, 4), new Point(17, 4), new Point(17, 9) }); break;
                    case "pen": g.DrawPolygon(pen, new[] { new Point(4, 13), new Point(13, 4), new Point(16, 7), new Point(7, 16), new Point(3, 17) }); g.DrawLine(pen, 11, 6, 14, 9); break;
                    case "highlight": g.DrawPolygon(pen, new[] { new Point(5, 12), new Point(12, 3), new Point(17, 8), new Point(9, 15) }); g.DrawLines(pen, new[] { new Point(5, 12), new Point(3, 16), new Point(8, 16), new Point(9, 15) }); g.DrawLine(pen, 5, 18, 15, 18); break;
                    case "mosaic":
                        g.DrawRectangle(pen, 4, 4, 12, 12); g.FillRectangle(brush, 4, 4, 6, 6); g.FillRectangle(brush, 10, 10, 6, 6); break;
                    case "text": g.DrawLine(pen, 4, 4, 16, 4); g.DrawLine(pen, 10, 4, 10, 16); g.DrawLine(pen, 7, 16, 13, 16); break;
                    case "eraser": g.DrawPolygon(pen, new[] { new Point(3, 12), new Point(11, 4), new Point(17, 10), new Point(10, 17), new Point(8, 17) }); g.DrawLine(pen, 7, 8, 13, 14); break;
                    case "undo": case "redo":
                        if (name == "redo") { g.TranslateTransform(20, 0); g.ScaleTransform(-1, 1); }
                        g.DrawBezier(pen, 5, 7, 15, 5, 17, 10, 14, 16); g.FillPolygon(brush, new[] { new Point(2, 8), new Point(8, 3), new Point(8, 12) }); break;
                    case "cancel": g.DrawLine(pen, 6, 6, 14, 14); g.DrawLine(pen, 14, 6, 6, 14); break;
                    case "pin": g.FillPolygon(brush, new[] { new Point(12, 3), new Point(17, 8), new Point(14, 9), new Point(11, 13), new Point(11, 16), new Point(4, 9), new Point(7, 9), new Point(11, 6) }); g.DrawLine(pen, 8, 12, 3, 17); break;
                    case "save": g.DrawPolygon(pen, new[] { new Point(4, 3), new Point(14, 3), new Point(17, 6), new Point(17, 17), new Point(4, 17) }); g.DrawRectangle(pen, 7, 3, 6, 5); g.DrawRectangle(pen, 7, 12, 7, 5); break;
                    case "copy": g.DrawRectangle(pen, 7, 6, 9, 11); g.DrawLines(pen, new[] { new Point(5, 14), new Point(3, 14), new Point(3, 3), new Point(12, 3), new Point(12, 4) }); break;
                }
            }
            return image;
        }
    }
}
