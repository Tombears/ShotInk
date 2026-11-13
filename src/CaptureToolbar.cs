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
        private readonly ToolStripButton gif, video;
        private readonly Control dragGrip;
        private bool movingToolbar;
        private Point moveAnchor, moveOrigin;
        private Bitmap surface;
        internal bool UserPlaced { get; private set; }
        private readonly Button finishScroll;
#if !SHOTINK_LITE
        private readonly OcrSelectionActions textActions;
#else
        private readonly Control textActions;
#endif
        private DrawTool currentTool = DrawTool.None;
        private bool textSelection;
        private readonly Dictionary<DrawTool, ToolStripItem> tools = new Dictionary<DrawTool, ToolStripItem>();
        private readonly Dictionary<ToolStripButton, string> iconNames = new Dictionary<ToolStripButton, string>();
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
        internal event Action GifClicked;
        internal event Action VideoClicked;
        internal event Action ToolbarMoved;

        internal CaptureToolbar(AppSettings settings = null)
        {
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Font = ToolAppearance.Font;
            strip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.None, AutoSize = false,
                Location = new Point(15, 1), Height = 28, Padding = new Padding(1, 0, 1, 0), CanOverflow = false,
                BackColor = Skins.Current.Surface, Renderer = new ToolStripProfessionalRenderer(new ToolbarColors()) };
            strip.Renderer = new ToolbarRenderer();
            strip.Tag = new SkinInteraction(strip);
            dragGrip = new ToolbarGrip { Name = "toolbar-drag", Location = new Point(1, 1), Size = new Size(14, 26), Cursor = Cursors.SizeAll };
            dragGrip.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { movingToolbar = true; moveAnchor = MousePosition; moveOrigin = Location; dragGrip.Capture = true; } };
            dragGrip.MouseMove += delegate { if (movingToolbar && Parent != null) { var delta = new Point(MousePosition.X - moveAnchor.X, MousePosition.Y - moveAnchor.Y); Location = Clamp(new Point(moveOrigin.X + delta.X, moveOrigin.Y + delta.Y), Parent.ClientRectangle, Size); UserPlaced = true; if (ToolbarMoved != null) ToolbarMoved(); } };
            dragGrip.MouseUp += delegate { movingToolbar = false; dragGrip.Capture = false; };
            dragGrip.MouseCaptureChanged += delegate { if (!dragGrip.Capture) movingToolbar = false; };
            Controls.Add(dragGrip);
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
#if !SHOTINK_LITE
            recognize = Button("ocr", "文字识别：在图片上选择并复制文字", delegate { if (RecognitionClicked != null) RecognitionClicked(); });
            recognize.Name = "recognize-text";
            recognize.MouseEnter += delegate { if (RecognitionHovered != null) RecognitionHovered(); };
#endif
            scroll = Button("scroll", "滚动长截图：使用当前选框，手动滚动后点击完成", delegate { if (ScrollClicked != null) ScrollClicked(); });
            scroll.Name = "scroll-capture";
            gif = Button("gif", "GIF 录制：使用当前选框，停止后裁剪区段", delegate { if (GifClicked != null) GifClicked(); });
            gif.Name = "gif-capture";
            video = Button("video", "录屏：选区、声音与指针设置", delegate { if (VideoClicked != null) VideoClicked(); });
            video.Name = "video-capture";
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
            foreach (ToolStripItem item in strip.Items) { item.Margin = Padding.Empty; if (item is ToolStripSeparator) { item.AutoSize = false; item.Width = 5; } width += item.Width; }
            strip.Width = width;
            Controls.Add(strip);
            options = new AnnotationOptions(settings) { Location = new Point(1, 30), Width = width + 14, Visible = false };
            options.SettingsChanged += delegate { if (SettingsChanged != null) SettingsChanged(); };
            options.ToolChanged += SelectTool;
            options.SizeChanged += delegate {
                if (!options.Visible || textSelection || !strip.Visible) return;
                Height = options.Bottom + 1;
                if (LayoutChanged != null) LayoutChanged();
            };
            Controls.Add(options);
#if !SHOTINK_LITE
            textActions = new OcrSelectionActions { Location = new Point(1, 30), Width = width + 14, Visible = false };
            textActions.CopySelected += delegate { if (CopySelectedText != null) CopySelectedText(); };
            textActions.CopyAll += delegate { if (CopyAllText != null) CopyAllText(); };
            textActions.ExitSelection += delegate { SelectTool(DrawTool.None); };
            Controls.Add(textActions);
#else
            recognize = null; textActions = new Panel { Visible = false }; Controls.Add(textActions);
#endif
            finishScroll = new ToolOptionButton { Name = "finish-scroll-capture", Text = "完成", Size = new Size(100, 30), Visible = false,
                AccentColor = ToolAppearance.Accent("scroll"), Selected = true };
            finishScroll.Click += delegate { if (ScrollFinishClicked != null) ScrollFinishClicked(); }; Controls.Add(finishScroll);
            Size = new Size(width + 16, 30);
            Skins.Changed += RefreshSkin;
        }
        internal void SetScrollCapture(bool capturing)
        {
            finishScroll.Visible = capturing; finishScroll.Enabled = true; finishScroll.Text = "完成";
            Width = capturing ? 128 : strip.Width + 16;
            strip.Visible = !capturing; options.Hide(); textActions.Hide();
            if (capturing) { Height = 36; finishScroll.Location = new Point(22, 3); finishScroll.BringToFront(); }
            else SelectTool(DrawTool.None);
            if (LayoutChanged != null) LayoutChanged();
        }
        internal void SetScrollFinishing() { finishScroll.Enabled = false; finishScroll.Text = "完成中…"; }
        internal void SetLongPreview(bool enabled) { scroll.Enabled = !enabled; gif.Enabled = video.Enabled = !enabled; }
        internal void SetFreeSelection(bool enabled) { scroll.Enabled = gif.Enabled = video.Enabled = !enabled; }
        internal void SetRecordingText(string text) { finishScroll.Text = text; }
        internal void ConfigureForGif(bool videoEditor = false)
        {
            var keep = new HashSet<ToolStripItem>(tools.Values); if (recognize != null && !videoEditor) keep.Add(recognize); keep.Add(undo); keep.Add(redo);
            for (int index = strip.Items.Count - 1; index >= 0; index--)
                if (!keep.Contains(strip.Items[index])) { var item = strip.Items[index]; strip.Items.RemoveAt(index); if (item != recognize) item.Dispose(); }
            var separator = new ToolStripSeparator { AutoSize = false, Width = 5, Margin = Padding.Empty };
            strip.Items.Insert(strip.Items.IndexOf(undo), separator);
            int width = strip.Padding.Horizontal + 2; foreach (ToolStripItem item in strip.Items) width += item.Width;
            dragGrip.Hide(); strip.Location = new Point(1, 1); strip.Width = width; Width = Math.Max(440, width + 2);
            options.Width = textActions.Width = Width - 2; if (recognize != null) recognize.ToolTipText = "文字识别：仅处理当前帧";
            tools[DrawTool.None].ToolTipText = "查看当前帧";
        }

        private ToolStripButton Button(string icon, string hint, Action clicked)
        {
            var button = new ToolStripButton { Tag = ToolAppearance.Accent(icon), ToolTipText = hint, DisplayStyle = ToolStripItemDisplayStyle.Image,
                ImageScaling = ToolStripItemImageScaling.None, AutoSize = false, Width = 25, Height = 26, Padding = Padding.Empty, Margin = Padding.Empty };
            iconNames.Add(button, icon); UpdateIcon(button, false);
            button.CheckedChanged += delegate { UpdateIcon(button, button.Selected); strip.Invalidate(); };
            button.MouseEnter += delegate { UpdateIcon(button, true); };
            button.MouseLeave += delegate { UpdateIcon(button, false); };
            button.EnabledChanged += delegate { UpdateIcon(button, false); };
            button.Click += delegate { clicked(); };
            strip.Items.Add(button); return button;
        }
        private void ActivateTool(DrawTool tool) { options.UseDefaultMode(tool); SelectTool(tool); }
        private void AddTool(string icon, string hint, DrawTool tool) { tools.Add(tool, Button(icon, hint, delegate { ActivateTool(tool); })); }
        private void UpdateIcon(ToolStripButton button, bool hot)
        {
            string name = iconNames[button];
            Color color = !button.Enabled ? ToolAppearance.Muted : button.Checked || hot ? ToolAppearance.Accent(name) : Skins.Current.Kind == SkinKind.Classic ? Color.FromArgb(65, 68, 74) : ToolAppearance.Accent(name);
            var previous = button.Image; button.Image = ToolbarIcons.Create(name, color);
            if (previous != null) previous.Dispose();
        }
        private void RefreshSkin()
        {
            if (surface != null) { surface.Dispose(); surface = null; }
            strip.BackColor = Skins.Current.Surface;
            foreach (var pair in iconNames)
            {
                if (pair.Key.IsDisposed) continue;
                pair.Key.Tag = ToolAppearance.Accent(pair.Value); UpdateIcon(pair.Key, false);
            }
            Skins.Apply(options); Skins.Apply(textActions); Invalidate(true);
        }
        internal void SelectTool(DrawTool tool)
        {
            currentTool = tool; textSelection = false; textActions.Hide(); if (recognize != null) recognize.Checked = false;
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
            Height = tool == DrawTool.None ? 30 : options.Bottom + 1;
            if (ToolChanged != null) ToolChanged(tool);
            if (LayoutChanged != null) LayoutChanged();
        }
        internal void SetRecognitionBusy(bool busy)
        { if (recognize != null) { recognize.Enabled = !busy; recognize.ToolTipText = busy ? "正在识别文字…" : "文字识别：在图片上选择并复制文字"; } }
        internal void SetTextSelection(bool enabled, bool hasSelection)
        {
#if !SHOTINK_LITE
            textActions.SetSelection(hasSelection);
            if (enabled)
            {
                foreach (var pair in tools)
                { pair.Value.BackColor = Color.Transparent; ((ToolStripButton)pair.Value).Checked = false; }
                textSelection = true; recognize.Checked = true; options.Hide(); textActions.Show(); Height = textActions.Bottom + 1;
            }
            else if (textSelection)
            {
                textSelection = false; if (recognize != null) recognize.Checked = false; textActions.Hide(); options.ShowTool(currentTool);
                if (tools.ContainsKey(currentTool)) ((ToolStripButton)tools[currentTool]).Checked = true;
                Height = currentTool == DrawTool.None ? 30 : options.Bottom + 1;
            }
            if (LayoutChanged != null) LayoutChanged();
#endif
        }
        internal void UpdateHistory(bool canUndo, bool canRedo) { undo.Enabled = canUndo; redo.Enabled = canRedo; }
        private void Finish(CaptureAction action) { if (FinishClicked != null) FinishClicked(action); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Height > 38) using (var pen = new Pen(ToolAppearance.Border)) e.Graphics.DrawLine(pen, 1, 29, Width - 2, 29);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            if (surface == null) surface = ToolAppearance.RenderSmooth(ClientSize, delegate(Graphics graphics)
            {
                SkinPainter.Surface(graphics, new RectangleF(.5F, .5F, Width - 1, Height - 1), 9);
            });
            e.Graphics.DrawImageUnscaled(surface, Point.Empty);
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (surface != null) { surface.Dispose(); surface = null; }
            if (Width < 4 || Height < 4) return;
            // Child windows stay inside the solid fill; the parent paints the soft
            // alpha fringe without a binary window region cutting off its pixels.
            foreach (var child in new Control[] { strip, options, textActions })
            {
                if (child == null) continue;
                using (var path = ToolAppearance.Rounded(new RectangleF(1.5F - child.Left, 1.5F - child.Top, Width - 3, Height - 3), 8))
                { var previous = child.Region; child.Region = new Region(path); if (previous != null) previous.Dispose(); }
            }
            Invalidate();
        }
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            var previous = Bounds;
            base.SetBoundsCore(x, y, width, height, specified);
            if (Parent == null || previous == Bounds || !Visible) return;
            // Transparent child windows can retain copied pixels when moved. Repaint
            // both the overlay and overlapping canvas, including the old option row.
            var damage = Rectangle.Union(previous, Bounds); damage.Inflate(2, 2);
            Parent.Invalidate(damage, true);
            if (IsHandleCreated) Parent.Update();
        }
        internal static Point Clamp(Point point, Rectangle work, Size size)
        { return new Point(Math.Max(work.Left, Math.Min(point.X, work.Right - size.Width)), Math.Max(work.Top, Math.Min(point.Y, work.Bottom - size.Height))); }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) { Skins.Changed -= RefreshSkin; if (surface != null) { surface.Dispose(); surface = null; } foreach (var button in iconNames.Keys) if (button.Image != null) { button.Image.Dispose(); button.Image = null; } } }

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
            public override Color ToolStripGradientBegin { get { return Skins.Current.Surface; } }
            public override Color ToolStripGradientMiddle { get { return ToolStripGradientBegin; } }
            public override Color ToolStripGradientEnd { get { return ToolStripGradientBegin; } }
            public override Color ToolStripBorder { get { return ToolStripGradientBegin; } }
            public override Color ButtonSelectedHighlight { get { return Color.FromArgb(222, 236, 255); } }
            public override Color ButtonSelectedBorder { get { return Color.FromArgb(124, 177, 246); } }
        }
        private sealed class ToolbarGrip : Control
        {
            internal ToolbarGrip() { SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
            protected override void OnPaint(PaintEventArgs e)
            {
                if (Skins.Current.Kind != SkinKind.Classic)
                {
                    using (var image = ToolbarIcons.Create("grip"))
                        e.Graphics.DrawImageUnscaled(image, (Width - image.Width) / 2, (Height - image.Height) / 2);
                    return;
                }
                ToolAppearance.DrawSmooth(e.Graphics, ClientSize, delegate(Graphics graphics)
                { using (var brush = new SolidBrush(ToolAppearance.Muted)) for (int x = 4; x <= 10; x += 6) for (int y = 6; y <= 22; y += 5) graphics.FillEllipse(brush, x, y, 2, 2); });
            }
        }
        private sealed class ToolbarRenderer : ToolStripProfessionalRenderer
        {
            internal ToolbarRenderer() : base(new ToolbarColors()) { RoundedEdges = false; }
            protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
            {
                var button = e.Item as ToolStripButton;
                if (button == null || !button.Enabled || (!button.Checked && !button.Selected && !button.Pressed)) return;
                Color accent = button.Tag is Color ? (Color)button.Tag : ToolAppearance.Accent(DrawTool.None);
                ToolAppearance.DrawSmooth(e.Graphics, button.Size, delegate(Graphics graphics)
                {
                SkinPainter.Button(graphics, new RectangleF(1.5F, 1.5F, button.Width - 3, button.Height - 3), accent,
                    button.Checked, button.Selected, button.Pressed, false, ((SkinInteraction)e.ToolStrip.Tag).Pulse);
                });
            }
            protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
            {
                var state = e.Graphics.Save();
                var button = e.Item as ToolStripButton;
                if (button != null) e.Graphics.TranslateTransform(0, SkinPainter.PressOffset(button.Pressed));
                if (Skins.Current.Kind != SkinKind.Classic && e.Image != null)
                    e.Graphics.DrawImageUnscaled(e.Image, e.ImageRectangle.Location);
                else base.OnRenderItemImage(e);
                e.Graphics.Restore(state);
            }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
        }
    }

    internal static class ToolbarIcons
    {
        internal static Bitmap Create(string name)
        { return Create(name, Skins.Current.Kind == SkinKind.Classic ? Color.FromArgb(65, 68, 74) : ToolAppearance.Accent(name)); }

        internal static Bitmap Create(string name, Color color)
        { return SkinIcons.Create(name, color); }

        internal static Bitmap CreateOutline(string name, Color color)
        {
            return ToolAppearance.RenderSmooth(new Size(20, 20), delegate(Graphics g)
            {
            using (var pen = new Pen(color, 1.7F))
            using (var brush = new SolidBrush(pen.Color))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                switch (name)
                {
                    case "lasso":
                        g.DrawBezier(pen, 5, 14, -2, 12, 2, 2, 10, 3);
                        g.DrawBezier(pen, 10, 3, 20, 3, 20, 15, 10, 15);
                        g.DrawLine(pen, 10, 15, 7, 13); g.DrawBezier(pen, 7, 13, 3, 11, 1, 18, 8, 18); break;
                    case "ocr-copy-selected":
                        g.DrawLines(pen, new[] { new Point(5, 2), new Point(2, 2), new Point(2, 7) });
                        g.DrawLines(pen, new[] { new Point(15, 2), new Point(18, 2), new Point(18, 7) });
                        g.DrawLines(pen, new[] { new Point(2, 13), new Point(2, 18), new Point(5, 18) });
                        g.DrawLines(pen, new[] { new Point(18, 13), new Point(18, 18), new Point(15, 18) });
                        g.DrawLine(pen, 6, 7, 14, 7); g.DrawLine(pen, 6, 11, 14, 11); g.DrawLine(pen, 6, 15, 11, 15); break;
                    case "ocr-copy-all":
                        g.DrawRectangle(pen, 6, 5, 11, 13);
                        g.DrawLines(pen, new[] { new Point(3, 14), new Point(2, 14), new Point(2, 2), new Point(13, 2) });
                        g.DrawLine(pen, 9, 9, 14, 9); g.DrawLine(pen, 9, 12, 14, 12); g.DrawLine(pen, 9, 15, 12, 15); break;
                    case "ocr-return":
                        g.DrawLines(pen, new[] { new Point(7, 2), new Point(2, 7), new Point(7, 12) });
                        g.DrawBezier(pen, 2, 7, 15, 3, 20, 12, 11, 17); break;
                    case "search": g.DrawEllipse(pen, 3, 2, 11, 11); g.DrawLine(pen, 12, 12, 18, 18); break;
                    case "search-previous": case "search-next":
                        if (name == "search-next") { g.TranslateTransform(0, 20); g.ScaleTransform(1, -1); }
                        g.DrawLine(pen, 10, 3, 10, 17); g.DrawLines(pen, new[] { new Point(4, 9), new Point(10, 3), new Point(16, 9) }); break;
                    case "shape-rectangle": case "mode-rectangle": g.DrawRectangle(pen, 3, 4, 14, 12); break;
                    case "shape-ellipse": g.DrawEllipse(pen, 3, 4, 14, 12); break;
                    case "mode-brush": g.DrawLine(pen, 5, 15, 15, 4); g.FillEllipse(brush, 3, 13, 5, 5); break;
                    case "line-arrow": g.DrawLine(pen, 3, 17, 17, 3); g.DrawLines(pen, new[] { new Point(8, 3), new Point(17, 3), new Point(17, 12) }); break;
                    case "line-straight": g.DrawLine(pen, 3, 17, 17, 3); break;
                    case "text-bold":
                        using (var thick = new Pen(color, 2.6F)) { g.DrawLine(thick, 5, 3, 5, 17); g.DrawBezier(thick, 5, 3, 18, 1, 18, 11, 5, 10); g.DrawBezier(thick, 5, 10, 20, 7, 18, 20, 5, 17); } break;
                    case "text-italic": g.DrawLine(pen, 8, 3, 16, 3); g.DrawLine(pen, 12, 3, 8, 17); g.DrawLine(pen, 4, 17, 12, 17); break;
                    case "stroke-1": case "stroke-3": case "stroke-6":
                        float diameter = name == "stroke-1" ? 3 : name == "stroke-3" ? 6 : 10;
                        g.FillEllipse(brush, 10 - diameter / 2, 10 - diameter / 2, diameter, diameter); break;
                    case "mosaic-size": case "mosaic-pixelate":
                        for (int i = 3; i < 17; i += 5) for (int j = 3; j < 17; j += 5) g.FillRectangle(brush, i, j, 3, 3); break;
                    case "blur-radius": case "mosaic-gaussian":
                        for (int i = 15; i >= 3; i -= 4) using (var haze = new SolidBrush(Color.FromArgb(65, color))) g.FillEllipse(haze, 10 - i / 2F, 10 - i / 2F, i, i); break;
                    case "mosaic-brush-size": case "eraser-brush-size": g.DrawEllipse(pen, 3, 3, 14, 14); g.FillEllipse(brush, 8, 8, 4, 4); break;
                    case "custom-color":
                        using (var red = new SolidBrush(Color.Coral)) g.FillEllipse(red, 2, 2, 10, 10);
                        using (var blue = new SolidBrush(Color.DodgerBlue)) g.FillEllipse(blue, 8, 3, 10, 10);
                        using (var green = new SolidBrush(Color.MediumSeaGreen)) g.FillEllipse(green, 5, 9, 10, 10); break;
                    case "grip":
                        for (int x = 7; x <= 13; x += 6) for (int y = 5; y <= 15; y += 5) g.FillEllipse(brush, x, y, 2, 2);
                        break;
                    case "play":
                        g.FillPolygon(brush, new[] { new Point(6, 3), new Point(6, 17), new Point(17, 10) }); break;
                    case "pause":
                        g.FillRectangle(brush, 5, 3, 4, 14); g.FillRectangle(brush, 12, 3, 4, 14); break;
                    case "microphone":
                        g.DrawEllipse(pen, 7, 2, 6, 10); g.DrawArc(pen, 4, 5, 12, 10, 0, 180); g.DrawLine(pen, 10, 15, 10, 18); break;
                    case "speaker":
                        g.DrawPolygon(pen, new[] { new Point(3, 7), new Point(7, 7), new Point(12, 3), new Point(12, 17), new Point(7, 13), new Point(3, 13) }); g.DrawArc(pen, 10, 4, 8, 12, -70, 140); break;
                    case "pointer":
                        g.DrawPolygon(pen, new[] { new Point(4, 2), new Point(16, 11), new Point(11, 12), new Point(9, 18) }); break;
                    case "video":
                        g.DrawRectangle(pen, 2, 5, 11, 11); g.DrawPolygon(pen, new[] { new Point(13, 8), new Point(18, 5), new Point(18, 16), new Point(13, 13) }); break;
                    case "gif":
                        using (var path = ToolAppearance.Rounded(new RectangleF(2, 3, 16, 14), 3)) g.DrawPath(pen, path);
                        g.FillPolygon(brush, new[] { new Point(8, 6), new Point(8, 14), new Point(14, 10) });
                        g.DrawLine(pen, 4, 1, 7, 1); g.DrawLine(pen, 13, 19, 16, 19);
                        break;
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
            });
        }
    }
}
