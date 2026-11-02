using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal enum GifEditScope { Current, Selected, All }
    internal sealed class GifScopeSelector : Control
    {
        private static readonly string[] Labels = { "当前帧", "选中帧", "全部帧" };
        internal GifEditScope Value { get; private set; }
        internal event Action ValueChanged;
        internal GifScopeSelector()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            Size = new Size(204, 30); Font = ToolAppearance.Font; TabStop = true; Cursor = Cursors.Hand;
            AccessibleName = "工具应用范围"; AccessibleRole = AccessibleRole.ButtonMenu; UpdateAccessibleValue();
        }
        internal void SelectScope(GifEditScope value)
        {
            if ((int)value < 0 || (int)value > 2) throw new ArgumentOutOfRangeException("value");
            if (Value == value) return; Value = value; UpdateAccessibleValue(); Invalidate(); if (ValueChanged != null) ValueChanged();
        }
        private void UpdateAccessibleValue() { AccessibleDescription = "当前选择：" + Labels[(int)Value]; }
        protected override void OnMouseDown(MouseEventArgs e)
        { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); SelectScope((GifEditScope)Math.Max(0, Math.Min(2, e.X * 3 / Math.Max(1, Width)))); } }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) { SelectScope((GifEditScope)Math.Max(0, Math.Min(2, (int)Value + (e.KeyCode == Keys.Left ? -1 : 1)))); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            ToolAppearance.DrawSmooth(e.Graphics, ClientSize, delegate(Graphics graphics)
            {
                using (var path = ToolAppearance.Rounded(new RectangleF(.5F, .5F, Width - 1, Height - 1), 8))
                using (var fill = new SolidBrush(Color.FromArgb(242, 245, 250)))
                using (var border = new Pen(ToolAppearance.Border)) { graphics.FillPath(fill, path); graphics.DrawPath(border, path); }
                float width = Width / 3F;
                using (var path = ToolAppearance.Rounded(new RectangleF((int)Value * width + 3, 3, width - 6, Height - 6), 6))
                using (var fill = new SolidBrush(Enabled ? Color.FromArgb(47, 111, 220) : Color.Gray)) graphics.FillPath(fill, path);
                using (var pen = new Pen(Color.FromArgb(218, 224, 234)))
                    for (int i = 1; i < 3; i++) graphics.DrawLine(pen, width * i, 9, width * i, Height - 9);
            });
            for (int index = 0; index < 3; index++)
            {
                var box = new Rectangle(index * Width / 3, 0, Width / 3, Height);
                TextRenderer.DrawText(e.Graphics, Labels[index], Font, box, Value == (GifEditScope)index ? Color.White : ToolAppearance.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(4, 4, Width - 8, Height - 8));
        }
    }
    internal sealed partial class AnnotationCanvas
    {
        internal void CommitFrameEdit() { CommitTextEdit(); CompletePolyline(); EndTextTransform(); }
        internal bool EditingInlineText { get { return textEditor != null && textEditor.ContainsFocus; } }
    }
}
