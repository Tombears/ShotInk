using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class SliderValueBox : TextBox
    {
        private readonly Func<int> read, minimum, maximum;
        private readonly Action<int> write;
        private bool syncing, selectOnClick;
        internal SliderValueBox(Func<int> read, Func<int> minimum, Func<int> maximum, Action<int> write)
        {
            this.read = read; this.minimum = minimum; this.maximum = maximum; this.write = write;
            TextAlign = HorizontalAlignment.Center; BorderStyle = BorderStyle.FixedSingle;
            Font = ToolAppearance.Font; ForeColor = ToolAppearance.Ink; BackColor = Color.White;
            Width = 44; RefreshValue(true);
        }
        internal void RefreshValue(bool force = false)
        {
            if (Focused && !force) return;
            syncing = true; Text = read().ToString(); syncing = false;
        }
        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e); int value;
            if (!syncing && int.TryParse(Text, out value) && value >= minimum() && value <= maximum()) write(value);
        }
        private void Commit()
        {
            int value;
            if (int.TryParse(Text, out value)) write(Math.Max(minimum(), Math.Min(maximum(), value)));
            RefreshValue(true);
        }
        protected override void OnLostFocus(EventArgs e) { Commit(); base.OnLostFocus(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); SelectAll(); selectOnClick = true; }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (selectOnClick) { SelectAll(); selectOnClick = false; } }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
            {
                if (e.KeyCode == Keys.Enter) Commit(); else RefreshValue(true);
                SelectAll(); e.Handled = true; e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }
    }
}
