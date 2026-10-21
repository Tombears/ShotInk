using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal static class Theme
    {
        internal static readonly Color Background = Color.FromArgb(245, 247, 251);
        internal static readonly Color Ink = Color.FromArgb(29, 40, 62);
        internal static readonly Color Muted = Color.FromArgb(110, 122, 144);
        internal static readonly Color Accent = Color.FromArgb(82, 95, 239);

        internal static Button Button(string text, bool primary, int width)
        {
            var button = new Button {
                Text = text, Width = width, Height = 38, FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White,
                ForeColor = primary ? Color.White : Ink,
                Font = new Font("Microsoft YaHei UI", 9F), Cursor = Cursors.Hand,
                Margin = new Padding(5), TabStop = true
            };
            button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(220, 225, 236);
            return button;
        }

        internal static Label Label(string text, float size, Color color)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = color,
                Font = new Font("Microsoft YaHei UI", size), BackColor = Color.Transparent };
        }
    }
}
