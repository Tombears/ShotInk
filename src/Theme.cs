using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal static class Theme
    {
        internal static Color Background { get { return Skins.Current.Background; } }
        internal static Color Ink { get { return Skins.Current.Kind == SkinKind.Classic ? Color.FromArgb(29, 40, 62) : Skins.Current.Ink; } }
        internal static Color Muted { get { return Skins.Current.Muted; } }
        internal static Color Accent { get { return Skins.Current.Accent; } }

        internal static Button Button(string text, bool primary, int width)
        {
            var button = new SkinButton { Primary = primary,
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
