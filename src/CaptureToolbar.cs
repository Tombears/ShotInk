using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShotInk
{
    internal enum CaptureAction { Copy, Save, Pin }

    internal sealed class CaptureToolbar : UserControl
    {
        private readonly ToolStrip strip;
        private readonly AnnotationOptions options;
        private readonly ToolStripButton undo;
        private readonly ToolStripButton redo;
        private readonly ToolStripButton recognize;
        private readonly ToolStripButton scroll;
        private readonly Button finishScroll;
        private readonly OcrSelectionActions textActions;
        private DrawTool currentTool = DrawTool.None;
        private bool textSelection;
        private readonly Dictionary<DrawTool, ToolStripItem> tools = new Dictionary<DrawTool, ToolStripItem>();
        private readonly List<Image> icons = new List<Image>();
        internal void ApplyTo(AnnotationCanvas canvas) { options.ApplyTo(canvas); }
        internal event Action<DrawTool> ToolChanged;
        internal event Action SettingsChanged;
        internal event Action UndoClicked;
        internal event Action RedoClicked;
        internal event Action CancelClicked;
        internal event Action<CaptureAction> FinishClicked;
        internal event Action LayoutChanged;
        internal event Action RecognitionClicked;
        internal event Action RecognitionHovered;
        internal event Action CopySelectedText;
        internal event Action CopyAllText;
        internal event Action ScrollClicked;
        internal event Action ScrollFinishClicked;

        internal CaptureToolbar(AppSettings settings = null)
        {
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.White;
            Font = ToolAppearance.Font;
            strip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.None, AutoSize = false,
                Location = new Point(1, 1), Height = 32, Padding = new Padding(4, 1, 4, 1), CanOverflow = false,
                BackColor = BackColor, Renderer = new ToolStripProfessionalRenderer(new ToolbarColors()) };
            strip.Renderer = new ToolbarRenderer();
            AddTool("move", "移动选区", DrawTool.None);
            var shape = Button("shape", "形状：单击使用矩形，下面可切换椭圆", delegate { ActivateTool(DrawTool.Rectangle); });
            tools.Add(DrawTool.Rectangle, shape); tools.Add(DrawTool.Ellipse, shape);
            var arrow = Button("arrow", "线条：箭头 / 直线，单击加点，右键或双击完成", delegate { ActivateTool(DrawTool.Arrow); });
            tools.Add(DrawTool.Arrow, arrow); tools.Add(DrawTool.Line, arrow);
            tools.Add(DrawTool.Polyline, arrow); tools.Add(DrawTool.ArrowPolyline, arrow);
            AddTool("pen", "画笔", DrawTool.Pen);
            AddTool("highlight", "荧光笔", DrawTool.Highlight);
            AddTool("mosaic", "马赛克", DrawTool.Mosaic);
            AddTool("text", "文字：单击输入，拖边框移动，拖角点缩放，顶部手柄旋转，双击 / F2 编辑", DrawTool.Text);
            AddTool("eraser", "橡皮擦：画笔 / 选框", DrawTool.Eraser);
            recognize = Button("ocr", "文字识别：在图片上选择并复制文字", delegate { if (RecognitionClicked != null) RecognitionClicked(); });
            recognize.Name = "recognize-text";
            recognize.MouseEnter += delegate { if (RecognitionHovered != null) RecognitionHovered(); };
            scroll = Button("scroll", "滚动长截图：使用当前选框，手动滚动后点击完成", delegate { if (ScrollClicked != null) ScrollClicked(); });
            scroll.Name = "scroll-capture";
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
            options = new AnnotationOptions(settings) { Location = new Point(1, 36), Width = width, Visible = false };
            options.SettingsChanged += delegate { if (SettingsChanged != null) SettingsChanged(); };
            options.ToolChanged += SelectTool;
            options.SizeChanged += delegate {
                if (!options.Visible || textSelection || !strip.Visible) return;
                Height = options.Bottom + 3;
                if (LayoutChanged != null) LayoutChanged();
            };
            Controls.Add(options);
            textActions = new OcrSelectionActions { Location = new Point(1, 36), Width = width, Visible = false };
            textActions.CopySelected += delegate { if (CopySelectedText != null) CopySelectedText(); };
            textActions.CopyAll += delegate { if (CopyAllText != null) CopyAllText(); };
            textActions.ExitSelection += delegate { SelectTool(DrawTool.None); };
            Controls.Add(textActions);
            finishScroll = new ToolOptionButton { Name = "finish-scroll-capture", Text = "完成", Size = new Size(100, 30), Visible = false,
                AccentColor = ToolAppearance.Accent("scroll"), Selected = true };
            finishScroll.Click += delegate { if (ScrollFinishClicked != null) ScrollFinishClicked(); }; Controls.Add(finishScroll);
            Size = new Size(width + 2, 35);
        }
        internal void SetScrollCapture(bool capturing)
        {
            finishScroll.Visible = capturing; finishScroll.Enabled = true; finishScroll.Text = "完成";
            Width = capturing ? 108 : strip.Width + 2;
            strip.Visible = !capturing; options.Hide(); textActions.Hide();
            if (capturing) { Height = 38; finishScroll.Location = new Point((Width - finishScroll.Width) / 2, 4); finishScroll.BringToFront(); }
            else SelectTool(DrawTool.None);
            if (LayoutChanged != null) LayoutChanged();
        }
        internal void SetScrollFinishing() { finishScroll.Enabled = false; finishScroll.Text = "完成中…"; }
        internal void SetLongPreview(bool enabled) { scroll.Enabled = !enabled; }

        private ToolStripButton Button(string icon, string hint, Action clicked)
        {
            var normal = Icon(icon); var active = ToolbarIcons.Create(icon, ToolAppearance.Accent(icon)); icons.Add(active);
            var button = new ToolStripButton { Image = normal, Tag = ToolAppearance.Accent(icon), ToolTipText = hint, DisplayStyle = ToolStripItemDisplayStyle.Image,
                ImageScaling = ToolStripItemImageScaling.None, AutoSize = false, Width = 30, Height = 29, Padding = Padding.Empty };
            button.CheckedChanged += delegate { button.Image = button.Checked ? active : normal; strip.Invalidate(); };
            button.MouseEnter += delegate { if (button.Enabled) button.Image = active; };
            button.MouseLeave += delegate { button.Image = button.Checked ? active : normal; };
            button.Click += delegate { clicked(); };
            strip.Items.Add(button); return button;
        }
        private void ActivateTool(DrawTool tool) { options.UseDefaultMode(tool); SelectTool(tool); }
        private void AddTool(string icon, string hint, DrawTool tool) { tools.Add(tool, Button(icon, hint, delegate { ActivateTool(tool); })); }
        private Image Icon(string name) { var image = ToolbarIcons.Create(name); icons.Add(image); return image; }
        internal void SelectTool(DrawTool tool)
        {
            currentTool = tool; textSelection = false; textActions.Hide(); recognize.Checked = false;
            foreach (var pair in tools)
            {
                pair.Value.BackColor = Color.Transparent;
                var button = pair.Value as ToolStripButton; if (button != null) button.Checked = false;
            }
            if (tools.ContainsKey(tool))
            {
                var button = tools[tool] as ToolStripButton; if (button != null) button.Checked = true;
            }
            options.ShowTool(tool);
            Height = tool == DrawTool.None ? 35 : options.Bottom + 3;
            if (ToolChanged != null) ToolChanged(tool);
            if (LayoutChanged != null) LayoutChanged();
        }
        internal void SetRecognitionBusy(bool busy)
        { recognize.Enabled = !busy; recognize.ToolTipText = busy ? "正在识别文字…" : "文字识别：在图片上选择并复制文字"; }
        internal void SetTextSelection(bool enabled, bool hasSelection)
        {
            textActions.SetSelection(hasSelection);
            if (enabled)
            {
                foreach (var pair in tools)
                { pair.Value.BackColor = Color.Transparent; ((ToolStripButton)pair.Value).Checked = false; }
                textSelection = true; recognize.Checked = true; options.Hide(); textActions.Show(); Height = textActions.Bottom + 3;
            }
            else if (textSelection)
            {
                textSelection = false; recognize.Checked = false; textActions.Hide(); options.ShowTool(currentTool);
                if (tools.ContainsKey(currentTool)) ((ToolStripButton)tools[currentTool]).Checked = true;
                Height = currentTool == DrawTool.None ? 35 : options.Bottom + 3;
            }
            if (LayoutChanged != null) LayoutChanged();
        }
        internal void UpdateHistory(bool canUndo, bool canRedo) { undo.Enabled = canUndo; redo.Enabled = canRedo; }
        private void Finish(CaptureAction action) { if (FinishClicked != null) FinishClicked(action); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(ToolAppearance.Border))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                if (Height > 38) e.Graphics.DrawLine(pen, 1, 34, Width - 2, 34);
            }
        }
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
            public override Color ToolStripGradientBegin { get { return Color.White; } }
            public override Color ToolStripGradientMiddle { get { return ToolStripGradientBegin; } }
            public override Color ToolStripGradientEnd { get { return ToolStripGradientBegin; } }
            public override Color ToolStripBorder { get { return ToolStripGradientBegin; } }
            public override Color ButtonSelectedHighlight { get { return Color.FromArgb(222, 236, 255); } }
            public override Color ButtonSelectedBorder { get { return Color.FromArgb(124, 177, 246); } }
        }
        private sealed class ToolbarRenderer : ToolStripProfessionalRenderer
        {
            internal ToolbarRenderer() : base(new ToolbarColors()) { RoundedEdges = false; }
            protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
            {
                var button = e.Item as ToolStripButton;
                if (button == null || !button.Enabled || (!button.Checked && !button.Selected && !button.Pressed)) return;
                Color accent = button.Tag is Color ? (Color)button.Tag : ToolAppearance.Accent(DrawTool.None);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = ToolAppearance.Rounded(new RectangleF(1.5F, 1.5F, button.Width - 3, button.Height - 3), 5))
                using (var fill = new SolidBrush(ToolAppearance.Tint(accent, button.Checked || button.Pressed ? 0.88F : 0.95F)))
                {
                    e.Graphics.FillPath(fill, path);
                    if (button.Checked)
                        using (var border = new Pen(ToolAppearance.Tint(accent, 0.50F))) e.Graphics.DrawPath(border, path);
                }
            }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
        }
    }

    internal static class ToolbarIcons
    {
        internal static Bitmap Create(string name)
        { return Create(name, Color.FromArgb(65, 68, 74)); }

        internal static Bitmap Create(string name, Color color)
        {
            var image = new Bitmap(20, 20);
            using (var g = Graphics.FromImage(image))
            using (var pen = new Pen(color, 1.7F))
            using (var brush = new SolidBrush(pen.Color))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                switch (name)
                {
                    case "scroll":
                        g.DrawLines(pen, new[] { new Point(6, 2), new Point(3, 2), new Point(3, 18), new Point(6, 18) });
                        g.DrawLines(pen, new[] { new Point(14, 2), new Point(17, 2), new Point(17, 18), new Point(14, 18) });
                        g.DrawLine(pen, 7, 4, 13, 4);
                        g.DrawLine(pen, 10, 8, 10, 16);
                        g.DrawLines(pen, new[] { new Point(7, 13), new Point(10, 16), new Point(13, 13) }); break;
                    case "move":
                        g.DrawLine(pen, 10, 2, 10, 18); g.DrawLine(pen, 2, 10, 18, 10);
                        g.DrawLines(pen, new[] { new Point(7, 5), new Point(10, 2), new Point(13, 5) });
                        g.DrawLines(pen, new[] { new Point(7, 15), new Point(10, 18), new Point(13, 15) });
                        g.DrawLines(pen, new[] { new Point(5, 7), new Point(2, 10), new Point(5, 13) });
                        g.DrawLines(pen, new[] { new Point(15, 7), new Point(18, 10), new Point(15, 13) }); break;
                    case "shape":
                        g.DrawLines(pen, new[] { new Point(7, 13), new Point(3, 13), new Point(3, 3), new Point(13, 3), new Point(13, 7) });
                        g.DrawEllipse(pen, 8, 9, 9, 8); break;
                    case "arrow":
                        g.DrawLine(pen, 3, 17, 17, 3);
                        g.DrawLines(pen, new[] { new Point(9, 3), new Point(17, 3), new Point(17, 11) }); break;
                    case "pen":
                        g.DrawPolygon(pen, new[] { new Point(3, 17), new Point(4, 12), new Point(13, 3), new Point(17, 7), new Point(8, 16) });
                        g.DrawLine(pen, 11, 5, 15, 9); g.DrawLine(pen, 4, 12, 8, 16); break;
                    case "highlight":
                        g.DrawPolygon(pen, new[] { new Point(5, 10), new Point(11, 3), new Point(17, 8), new Point(11, 14) });
                        g.DrawLines(pen, new[] { new Point(5, 10), new Point(3, 15), new Point(8, 15), new Point(11, 14) });
                        g.DrawLine(pen, 7, 8, 13, 12);
                        g.FillRectangle(brush, 3, 17, 14, 2); break;
                    case "mosaic":
                        // Separate pixels read as mosaic rather than a checkerboard.
                        using (var mid = new SolidBrush(ToolAppearance.Tint(color, 0.34F)))
                        using (var light = new SolidBrush(ToolAppearance.Tint(color, 0.66F)))
                        {
                            g.FillRectangle(brush, 3, 3, 4, 4); g.FillRectangle(mid, 8, 3, 4, 4); g.FillRectangle(light, 13, 3, 4, 4);
                            g.FillRectangle(mid, 3, 8, 4, 4); g.FillRectangle(light, 8, 8, 4, 4); g.FillRectangle(brush, 13, 8, 4, 4);
                            g.FillRectangle(light, 3, 13, 4, 4); g.FillRectangle(brush, 8, 13, 4, 4); g.FillRectangle(mid, 13, 13, 4, 4);
                        }
                        break;
                    case "text": g.DrawLine(pen, 4, 4, 16, 4); g.DrawLine(pen, 10, 4, 10, 16); g.DrawLine(pen, 7, 16, 13, 16); break;
                    case "eraser":
                        g.DrawPolygon(pen, new[] { new Point(3, 12), new Point(11, 4), new Point(17, 10), new Point(10, 17), new Point(8, 17) });
                        g.DrawLine(pen, 7, 8, 13, 14); g.DrawLine(pen, 10, 17, 18, 17); break;
                    case "ocr":
                        g.DrawLines(pen, new[] { new Point(6, 2), new Point(2, 2), new Point(2, 6) });
                        g.DrawLines(pen, new[] { new Point(14, 2), new Point(18, 2), new Point(18, 6) });
                        g.DrawLines(pen, new[] { new Point(2, 14), new Point(2, 18), new Point(6, 18) });
                        g.DrawLines(pen, new[] { new Point(14, 18), new Point(18, 18), new Point(18, 14) });
                        // Four strokes of 文 keep the small icon legible at any
                        // system font setting and avoid font fallback glyphs.
                        g.DrawLine(pen, 10, 4, 11, 5);
                        g.DrawLine(pen, 5, 7, 15, 7);
                        g.DrawBezier(pen, 12, 8, 12, 10, 9, 13, 5, 15);
                        g.DrawBezier(pen, 7, 8, 8, 11, 11, 14, 15, 15);
                        break;
                    case "undo": case "redo":
                        if (name == "redo") { g.TranslateTransform(20, 0); g.ScaleTransform(-1, 1); }
                        g.DrawBezier(pen, 3, 8, 17, 5, 19, 15, 11, 17);
                        g.DrawLines(pen, new[] { new Point(7, 3), new Point(3, 8), new Point(8, 12) }); break;
                    case "cancel": g.DrawLine(pen, 5, 5, 15, 15); g.DrawLine(pen, 15, 5, 5, 15); break;
                    case "pin":
                        g.DrawPolygon(pen, new[] { new Point(12, 3), new Point(17, 8), new Point(14, 8), new Point(11, 12), new Point(11, 15), new Point(5, 9), new Point(8, 9), new Point(12, 6) });
                        g.DrawLine(pen, 8, 12, 3, 17); break;
                    case "save":
                        g.DrawPolygon(pen, new[] { new Point(3, 3), new Point(14, 3), new Point(17, 6), new Point(17, 17), new Point(3, 17) });
                        g.DrawRectangle(pen, 6, 3, 7, 5); g.DrawRectangle(pen, 6, 12, 8, 5); break;
                    case "copy":
                        g.DrawRectangle(pen, 7, 7, 10, 10);
                        g.DrawLines(pen, new[] { new Point(4, 13), new Point(3, 13), new Point(3, 3), new Point(13, 3), new Point(13, 4) }); break;
                }
            }
            return image;
        }
    }
}
