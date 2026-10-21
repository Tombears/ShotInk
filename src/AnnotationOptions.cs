using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class AnnotationOptions : FlowLayoutPanel
    {
        internal Color Ink = Color.FromArgb(239, 64, 76);
        internal float Stroke = 3;
        internal ApplyMode MosaicMode = ApplyMode.Brush;
        internal ApplyMode EraserMode = ApplyMode.Brush;
        internal int MosaicSize = 14;
        internal int MosaicBrushSize = 24;
        internal int EraserBrushSize = 24;
        internal event Action SettingsChanged;
        internal event Action<DrawTool> ToolChanged;

        internal AnnotationOptions()
        {
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            Margin = Padding.Empty;
            BackColor = Color.FromArgb(250, 250, 250);
        }

        internal void ApplyTo(AnnotationCanvas canvas)
        {
            canvas.Ink = Ink; canvas.Stroke = Stroke;
            canvas.MosaicMode = MosaicMode; canvas.EraserMode = EraserMode;
            canvas.MosaicSize = MosaicSize; canvas.MosaicBrushSize = MosaicBrushSize; canvas.EraserBrushSize = EraserBrushSize;
            canvas.UpdateToolCursor();
        }

        internal void ShowTool(DrawTool tool)
        {
            SuspendLayout();
            while (Controls.Count > 0) { var control = Controls[0]; Controls.Remove(control); control.Dispose(); }
            if (tool == DrawTool.Mosaic || tool == DrawTool.Eraser)
            {
                bool mosaic = tool == DrawTool.Mosaic;
                var mode = mosaic ? MosaicMode : EraserMode;
                var modes = Row();
                Choice(modes, "画笔", "mode-brush", mode == ApplyMode.Brush, delegate { SetMode(tool, ApplyMode.Brush); });
                Choice(modes, "选框", "mode-rectangle", mode == ApplyMode.Rectangle, delegate { SetMode(tool, ApplyMode.Rectangle); });
                if (mosaic) Slider("马赛克大小", "mosaic-size", 4, 48, MosaicSize, true, delegate(int value) { MosaicSize = value; NotifySettings(); });
                Slider("画笔大小", mosaic ? "mosaic-brush-size" : "eraser-brush-size", 4, 160, mosaic ? MosaicBrushSize : EraserBrushSize, mode == ApplyMode.Brush,
                    delegate(int value) { if (mosaic) MosaicBrushSize = value; else EraserBrushSize = value; NotifySettings(); });
            }
            else if (tool != DrawTool.None)
            {
                if (tool == DrawTool.Rectangle || tool == DrawTool.Ellipse)
                {
                    var modes = Row();
                    Choice(modes, "矩形", "shape-rectangle", tool == DrawTool.Rectangle, delegate { ChooseTool(DrawTool.Rectangle); });
                    Choice(modes, "椭圆", "shape-ellipse", tool == DrawTool.Ellipse, delegate { ChooseTool(DrawTool.Ellipse); });
                }
                if (tool == DrawTool.Arrow || tool == DrawTool.Line)
                {
                    var modes = Row();
                    Choice(modes, "箭头", "line-arrow", tool == DrawTool.Arrow, delegate { ChooseTool(DrawTool.Arrow); });
                    Choice(modes, "直线", "line-straight", tool == DrawTool.Line, delegate { ChooseTool(DrawTool.Line); });
                }
                var colors = Row();
                foreach (var color in new[] { Color.FromArgb(239, 64, 76), Color.FromArgb(255, 174, 38), Color.FromArgb(250, 216, 49), Color.FromArgb(37, 181, 114), Color.FromArgb(47, 135, 246), Color.FromArgb(150, 89, 224), Color.Black, Color.White })
                {
                    var swatch = new Button { Width = 19, Height = 19, Margin = new Padding(2, 5, 2, 0), BackColor = color, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Tag = color };
                    swatch.FlatAppearance.BorderColor = color.ToArgb() == Ink.ToArgb() ? Color.DodgerBlue : Color.Gray;
                    swatch.Click += delegate(object sender, EventArgs e) { Ink = (Color)((Control)sender).Tag; NotifySettings(); ShowTool(tool); };
                    colors.Controls.Add(swatch);
                }
                var custom = new Button { Text = "…", Width = 27, Height = 24, Margin = new Padding(3, 2, 6, 0), FlatStyle = FlatStyle.Flat };
                custom.Click += delegate { using (var dialog = new ColorDialog { Color = Ink, FullOpen = true }) if (dialog.ShowDialog(FindForm()) == DialogResult.OK) { Ink = dialog.Color; NotifySettings(); } };
                colors.Controls.Add(custom);
                colors.Controls.Add(new Label { Text = "粗细", AutoSize = true, Margin = new Padding(1, 7, 3, 0) });
                var stroke = new NumericUpDown { Minimum = 1, Maximum = 12, Value = (decimal)Stroke, Width = 45, Margin = new Padding(1, 3, 0, 0) };
                stroke.ValueChanged += delegate { Stroke = (float)stroke.Value; NotifySettings(); };
                colors.Controls.Add(stroke);
            }
            Height = Controls.Count * 30;
            Visible = tool != DrawTool.None;
            ResumeLayout();
        }

        private FlowLayoutPanel Row()
        {
            var row = new FlowLayoutPanel { Height = 30, Width = Width, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
            Controls.Add(row); return row;
        }
        private static void Choice(FlowLayoutPanel row, string text, string name, bool selected, Action clicked)
        {
            var button = new Button { Name = name, Text = text, Width = 58, Height = 25, Margin = new Padding(2, 2, 3, 0), FlatStyle = FlatStyle.Flat,
                BackColor = selected ? Color.FromArgb(222, 236, 255) : Color.White, ForeColor = Color.FromArgb(50, 60, 74), Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = selected ? Color.DodgerBlue : Color.LightGray;
            button.Click += delegate { clicked(); };
            row.Controls.Add(button);
        }
        private void Slider(string label, string name, int minimum, int maximum, int value, bool enabled, Action<int> changed)
        {
            var row = Row();
            row.Controls.Add(new Label { Text = label, Width = 80, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(3, 3, 0, 0), Enabled = enabled });
            var slider = new TrackBar { Name = name, Minimum = minimum, Maximum = maximum, Value = value, Width = 216, Height = 27,
                AutoSize = false, TickStyle = TickStyle.None, SmallChange = 1, LargeChange = 4, Margin = new Padding(0, 1, 0, 0), Enabled = enabled };
            var number = new Label { Text = value + " px", Width = 56, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(1, 3, 0, 0), Enabled = enabled };
            slider.ValueChanged += delegate { number.Text = slider.Value + " px"; changed(slider.Value); };
            row.Controls.Add(slider); row.Controls.Add(number);
        }
        private void SetMode(DrawTool tool, ApplyMode mode)
        {
            if (tool == DrawTool.Mosaic) MosaicMode = mode; else EraserMode = mode;
            NotifySettings(); ShowTool(tool);
        }
        private void NotifySettings() { if (SettingsChanged != null) SettingsChanged(); }
        private void ChooseTool(DrawTool tool) { if (ToolChanged != null) ToolChanged(tool); }
    }
}
