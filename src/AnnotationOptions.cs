using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class AnnotationOptions : FlowLayoutPanel
    {
        private readonly AppSettings settings;
        private DrawTool currentTool = DrawTool.Rectangle;
        internal Color Ink { get { return settings.Tool(currentTool).Ink; } set { settings.Tool(currentTool).Ink = value; } }
        internal float Stroke { get { return settings.Tool(currentTool).Width; } set { settings.Tool(currentTool).Width = value; } }
        internal float TextFontSize { get { return settings.Tool(DrawTool.Text).FontSize; } set { settings.Tool(DrawTool.Text).FontSize = value; } }
        internal FontStyle TextFontStyle { get { return settings.Tool(DrawTool.Text).FontStyle; } set { settings.Tool(DrawTool.Text).FontStyle = value; } }
        private AnnotationCanvas boundCanvas;
        internal ApplyMode MosaicMode = ApplyMode.Brush;
        internal ApplyMode EraserMode = ApplyMode.Brush;
        internal int MosaicSize { get { return settings.Tool(DrawTool.Mosaic).MosaicSize; } set { settings.Tool(DrawTool.Mosaic).MosaicSize = value; } }
        internal int MosaicBrushSize { get { return settings.Tool(DrawTool.Mosaic).BrushSize; } set { settings.Tool(DrawTool.Mosaic).BrushSize = value; } }
        internal int EraserBrushSize { get { return settings.Tool(DrawTool.Eraser).BrushSize; } set { settings.Tool(DrawTool.Eraser).BrushSize = value; } }
        internal MosaicEffect MosaicEffect { get { return settings.Tool(DrawTool.Mosaic).MosaicEffect; } set { settings.Tool(DrawTool.Mosaic).MosaicEffect = value; } }
        internal int BlurRadius { get { return settings.Tool(DrawTool.Mosaic).BlurRadius; } set { settings.Tool(DrawTool.Mosaic).BlurRadius = value; } }
        internal event Action SettingsChanged;
        internal event Action<DrawTool> ToolChanged;

        internal AnnotationOptions(AppSettings settings = null)
        {
            this.settings = settings ?? new AppSettings();
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            Margin = Padding.Empty;
            Padding = new Padding(12, 8, 12, 8);
            BackColor = Color.White;
            Font = ToolAppearance.Font;
            ForeColor = ToolAppearance.Ink;
        }

        internal void ApplyTo(AnnotationCanvas canvas)
        {
            if (boundCanvas != canvas)
            {
                if (boundCanvas != null) boundCanvas.TextAnnotationSelected -= ReadTextSettings;
                if (boundCanvas != null) boundCanvas.MosaicAnnotationSelected -= ReadMosaicSettings;
                boundCanvas = canvas; canvas.TextAnnotationSelected += ReadTextSettings;
                canvas.MosaicAnnotationSelected += ReadMosaicSettings;
            }
            canvas.Ink = Ink; canvas.Stroke = Stroke;
            canvas.ApplyTextSettings(TextFontSize, TextFontStyle, Ink);
            canvas.MosaicMode = MosaicMode; canvas.EraserMode = EraserMode;
            canvas.MosaicSize = MosaicSize; canvas.MosaicBrushSize = MosaicBrushSize; canvas.EraserBrushSize = EraserBrushSize;
            canvas.ApplyMosaicSettings(MosaicEffect, MosaicSize, BlurRadius);
            canvas.UpdateToolCursor();
        }
        private void ReadTextSettings(Mark mark)
        {
            currentTool = DrawTool.Text;
            Ink = mark.Color; TextFontSize = TextGeometry.FontSize(mark); TextFontStyle = mark.TextStyle;
            boundCanvas.Ink = Ink; boundCanvas.TextFontSize = TextFontSize; boundCanvas.TextFontStyle = TextFontStyle;
            settings.NotifyChanged();
            ShowTool(DrawTool.Text);
        }
        private void ReadMosaicSettings(Mark mark)
        {
            currentTool = DrawTool.Mosaic; MosaicEffect = mark.MosaicEffect; MosaicSize = mark.MosaicSize; BlurRadius = mark.BlurRadius;
            boundCanvas.MosaicEffect = MosaicEffect; boundCanvas.MosaicSize = MosaicSize; boundCanvas.BlurRadius = BlurRadius;
            settings.NotifyChanged(); ShowTool(DrawTool.Mosaic);
        }

        internal void UseDefaultMode(DrawTool tool)
        {
            if (tool == DrawTool.Mosaic) MosaicMode = ApplyMode.Brush;
            if (tool == DrawTool.Eraser) EraserMode = ApplyMode.Brush;
        }

        internal void ShowTool(DrawTool tool)
        {
            currentTool = tool;
            SuspendLayout();
            while (Controls.Count > 0) { var control = Controls[0]; Controls.Remove(control); control.Dispose(); }
            if (tool != DrawTool.None) Header(tool);
            if (tool == DrawTool.Mosaic || tool == DrawTool.Eraser)
            {
                bool mosaic = tool == DrawTool.Mosaic;
                var mode = mosaic ? MosaicMode : EraserMode;
                var modes = Row();
                Caption(modes, "作用方式");
                Choice(modes, "涂抹画笔", "mode-brush", mode == ApplyMode.Brush, delegate { SetMode(tool, ApplyMode.Brush); });
                Choice(modes, "矩形选区", "mode-rectangle", mode == ApplyMode.Rectangle, delegate { SetMode(tool, ApplyMode.Rectangle); });
                if (mosaic)
                {
                    var effects = Row(); Caption(effects, "处理效果");
                    Choice(effects, "像素块", "mosaic-pixelate", MosaicEffect == MosaicEffect.Pixelate, delegate { MosaicEffect = MosaicEffect.Pixelate; NotifySettings(); ShowTool(tool); });
                    Choice(effects, "高斯模糊", "mosaic-gaussian", MosaicEffect == MosaicEffect.GaussianBlur, delegate { MosaicEffect = MosaicEffect.GaussianBlur; NotifySettings(); ShowTool(tool); });
                    if (MosaicEffect == MosaicEffect.Pixelate) Slider("像素大小", "mosaic-size", 4, 48, MosaicSize, true, delegate(int value) { MosaicSize = value; NotifySettings(); });
                    else Slider("模糊强度", "blur-radius", 1, 24, BlurRadius, true, delegate(int value) { BlurRadius = value; NotifySettings(); });
                }
                Slider("画笔大小", mosaic ? "mosaic-brush-size" : "eraser-brush-size", 4, 160, mosaic ? MosaicBrushSize : EraserBrushSize, mode == ApplyMode.Brush,
                    delegate(int value) { if (mosaic) MosaicBrushSize = value; else EraserBrushSize = value; NotifySettings(); });
                if (mode == ApplyMode.Rectangle) Hint("拖出矩形即可处理该区域，无需设置画笔大小");
            }
            else if (tool != DrawTool.None)
            {
                if (tool == DrawTool.Text)
                {
                    var text = Row(); Caption(text, "文字样式");
                    var size = new NumericUpDown { Name = "text-font-size", AccessibleName = "文字字号", Minimum = 6, Maximum = 300,
                        DecimalPlaces = 1, Value = Math.Max(6, Math.Min(300, (decimal)TextFontSize)), Width = 72, Font = Font,
                        BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 6, 6, 0) };
                    size.ValueChanged += delegate { TextFontSize = (float)size.Value; NotifySettings(); };
                    text.Controls.Add(size);
                    text.Controls.Add(new Label { Text = "pt", Width = 24, Height = 32, TextAlign = ContentAlignment.MiddleLeft, ForeColor = ToolAppearance.Muted, Margin = Padding.Empty });
                    Choice(text, "粗体", "text-bold", (TextFontStyle & FontStyle.Bold) != 0, delegate { TextFontStyle ^= FontStyle.Bold; NotifySettings(); ShowTool(tool); });
                    Choice(text, "斜体", "text-italic", (TextFontStyle & FontStyle.Italic) != 0, delegate { TextFontStyle ^= FontStyle.Italic; NotifySettings(); ShowTool(tool); });
                }
                if (tool == DrawTool.Rectangle || tool == DrawTool.Ellipse)
                {
                    var modes = Row();
                    Caption(modes, "形状");
                    Choice(modes, "矩形", "shape-rectangle", tool == DrawTool.Rectangle, delegate { ChooseTool(DrawTool.Rectangle); });
                    Choice(modes, "椭圆", "shape-ellipse", tool == DrawTool.Ellipse, delegate { ChooseTool(DrawTool.Ellipse); });
                }
                if (tool == DrawTool.Arrow || tool == DrawTool.Line || tool == DrawTool.Polyline || tool == DrawTool.ArrowPolyline)
                {
                    var modes = Row();
                    Caption(modes, "线条");
                    Choice(modes, "箭头", "line-arrow", tool == DrawTool.Arrow, delegate { ChooseTool(DrawTool.Arrow); });
                    Choice(modes, "直线", "line-straight", tool == DrawTool.Line, delegate { ChooseTool(DrawTool.Line); });
                }
                var colors = Row();
                Caption(colors, "颜色");
                foreach (var color in new[] { Color.FromArgb(239, 64, 76), Color.FromArgb(255, 174, 38), Color.FromArgb(250, 216, 49), Color.FromArgb(37, 181, 114), Color.FromArgb(47, 135, 246), Color.FromArgb(150, 89, 224), Color.Black, Color.White })
                {
                    var swatch = new ToolOptionButton { Width = 29, Height = 32, Margin = new Padding(0, 0, 3, 0), BackColor = color, Tag = color,
                        IsSwatch = true, AccentColor = ToolAppearance.Accent(tool), Selected = color.ToArgb() == Ink.ToArgb(), AccessibleName = "颜色 " + color.Name };
                    swatch.Click += delegate(object sender, EventArgs e) { Ink = (Color)((Control)sender).Tag; NotifySettings(); ShowTool(tool); };
                    colors.Controls.Add(swatch);
                }
                var custom = new ToolOptionButton { Text = "自定", Name = "custom-color", AccessibleName = "自定义颜色", Width = 54, Height = 30, Margin = new Padding(4, 1, 0, 0), AccentColor = ToolAppearance.Accent(tool) };
                custom.Click += delegate { using (var dialog = new ColorDialog { Color = Ink, FullOpen = true }) if (dialog.ShowDialog(FindForm()) == DialogResult.OK) { Ink = dialog.Color; NotifySettings(); ShowTool(tool); } };
                colors.Controls.Add(custom);
                if (tool != DrawTool.Text)
                {
                    var widths = Row(); Caption(widths, "线宽");
                    var stroke = new NumericUpDown { Name = "tool-stroke", AccessibleName = "线条宽度", Minimum = 1, Maximum = 12, Value = (decimal)Stroke,
                        Width = 58, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(6, 6, 5, 0) };
                    var presets = new[] { 1, 3, 6 };
                    foreach (int value in presets)
                    {
                        int width = value;
                        Choice(widths, value == 1 ? "细 1" : value == 3 ? "中 3" : "粗 6", "stroke-" + value, Stroke == value, delegate { stroke.Value = width; });
                    }
                    stroke.ValueChanged += delegate {
                        Stroke = (float)stroke.Value; NotifySettings();
                        foreach (Control control in widths.Controls)
                        {
                            var choice = control as ToolOptionButton;
                            if (choice != null) { choice.Selected = choice.Name == "stroke-" + stroke.Value; choice.Invalidate(); }
                        }
                    };
                    widths.Controls.Add(stroke);
                    widths.Controls.Add(new Label { Text = "px", Width = 24, Height = 32, TextAlign = ContentAlignment.MiddleLeft, ForeColor = ToolAppearance.Muted, Margin = Padding.Empty });
                }
                if (tool == DrawTool.Text) Hint("单击图片输入文字 · Ctrl+Enter 完成 · 双击文字再次编辑");
                if (tool == DrawTool.Arrow || tool == DrawTool.Line || tool == DrawTool.Polyline || tool == DrawTool.ArrowPolyline)
                    Hint("单击添加起点和折点 · 右键或双击完成 · Backspace 退点");
            }
            int height = Padding.Vertical;
            foreach (Control row in Controls) height += row.Height + row.Margin.Vertical;
            Height = height;
            Visible = tool != DrawTool.None;
            ResumeLayout();
        }

        private FlowLayoutPanel Row()
        {
            var row = new FlowLayoutPanel { Height = 36, Width = Math.Max(0, ClientSize.Width - Padding.Horizontal), WrapContents = false,
                Margin = new Padding(0, 0, 0, 3), Padding = Padding.Empty, BackColor = Color.White };
            Controls.Add(row); return row;
        }
        private void Header(DrawTool tool)
        {
            string title, description;
            switch (tool)
            {
                case DrawTool.Rectangle: case DrawTool.Ellipse: title = "形状"; description = "用轮廓框出重点"; break;
                case DrawTool.Arrow: case DrawTool.Line: case DrawTool.Polyline: case DrawTool.ArrowPolyline: title = "线条"; description = "指明方向，连接关键位置"; break;
                case DrawTool.Pen: title = "画笔"; description = "自由书写与手绘标记"; break;
                case DrawTool.Highlight: title = "荧光笔"; description = "半透明涂抹，突出内容"; break;
                case DrawTool.Text: title = "文字"; description = "为截图添加文字说明"; break;
                case DrawTool.Mosaic: title = "马赛克"; description = "遮挡截图中的敏感信息"; break;
                default: title = "橡皮擦"; description = "擦除标注，恢复原图"; break;
            }
            var header = Row(); header.Height = 30; header.Margin = new Padding(0, 0, 0, 6);
            header.Controls.Add(new Label { Text = title, Width = 72, Height = 28, Font = ToolAppearance.HeadingFont, ForeColor = ToolAppearance.Accent(tool), TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty });
            header.Controls.Add(new Label { Text = description, AutoSize = false, Width = 320, Height = 28, ForeColor = ToolAppearance.Muted, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty });
        }
        private void Hint(string text)
        {
            var row = Row(); row.Height = 25; row.Margin = Padding.Empty;
            row.Controls.Add(new Label { Text = text, AutoEllipsis = true, Width = row.Width, Height = 25, ForeColor = ToolAppearance.Muted, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty });
        }
        private static void Caption(FlowLayoutPanel row, string text)
        {
            row.Controls.Add(new Label { Text = text, Width = 72, Height = 32, ForeColor = ToolAppearance.Muted, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty });
        }
        private void Choice(FlowLayoutPanel row, string text, string name, bool selected, Action clicked)
        {
            var button = new ToolOptionButton { Name = name, Text = text, Width = text.Length > 3 ? 88 : 64, Height = 30, Margin = new Padding(0, 1, 6, 0),
                AccentColor = ToolAppearance.Accent(currentTool), Selected = selected, AccessibleName = text };
            button.Click += delegate { clicked(); };
            row.Controls.Add(button);
        }
        private void Slider(string label, string name, int minimum, int maximum, int value, bool enabled, Action<int> changed)
        {
            var row = Row();
            Caption(row, label); row.Controls[0].Enabled = enabled;
            var slider = new OvalSlider { Name = name, AccessibleName = label, Minimum = minimum, Maximum = maximum, Value = value,
                Width = Math.Max(80, row.Width - 140), Height = 32, AccentColor = ToolAppearance.Accent(currentTool),
                SmallChange = 1, LargeChange = 4, Margin = Padding.Empty, Enabled = enabled };
            var number = new Label { Text = value + " px", Width = 64, Height = 28, TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(4, 2, 0, 0), ForeColor = ToolAppearance.Accent(currentTool), BackColor = ToolAppearance.Tint(ToolAppearance.Accent(currentTool), 0.94F), Enabled = enabled };
            slider.ValueChanged += delegate { number.Text = slider.Value + " px"; changed(slider.Value); };
            row.Controls.Add(slider); row.Controls.Add(number);
        }
        private void SetMode(DrawTool tool, ApplyMode mode)
        {
            if (tool == DrawTool.Mosaic) MosaicMode = mode; else EraserMode = mode;
            NotifySettings(); ShowTool(tool);
        }
        private void NotifySettings() { settings.NotifyChanged(); if (SettingsChanged != null) SettingsChanged(); }
        private void ChooseTool(DrawTool tool) { if (ToolChanged != null) ToolChanged(tool); }
    }
}
