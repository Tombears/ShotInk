using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class CaptureFeedback : Form
    {
        private readonly Timer lifetime = new Timer { Interval = 1500 };
        private readonly Timer animation = new Timer { Interval = 20 };
        private int elapsed;
        private Point destination;
        internal CaptureFeedback()
        {
            SetStyle(ControlStyles.Selectable, false);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Skins.Current.Kind == SkinKind.Classic ? Color.FromArgb(30, 40, 58) : Skins.Current.Surface; ClientSize = new Size(210, 48);
            var message = new Label { Text = "✓  截图已复制", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Skins.Current.Kind == SkinKind.Classic ? Color.White : Skins.Current.Ink, Font = new Font("Microsoft YaHei UI", 11), UseMnemonic = false, BackColor = Color.Transparent };
            Controls.Add(message);
            var work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(work.Right - Width - 24, work.Bottom - Height - 24);
            destination = Location; DoubleBuffered = true;
            animation.Tick += delegate
            {
                elapsed += animation.Interval;
                float entry = Math.Min(1, elapsed / 180F);
                float exit = Math.Max(0, (elapsed - 1220) / 280F);
                Opacity = Math.Max(.05, Math.Min(entry, 1 - exit));
                int offset = (int)((1 - entry) * 12);
                if (Skins.Current.Kind == SkinKind.Pixel) offset = offset / 4 * 4;
                Location = new Point(destination.X, destination.Y + offset); Invalidate();
            };
            lifetime.Tick += delegate { Close(); };
            Shown += delegate { Native.RaiseWithoutActivation(Handle); Native.ExcludeFromCapture(Handle, true); lifetime.Start(); if (Skins.Current.Kind != SkinKind.Classic) { Opacity = .05; animation.Start(); } };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Skins.Current.Kind == SkinKind.Classic) return;
            SkinPainter.Button(e.Graphics, new RectangleF(1, 1, Width - 3, Height - 3), Skins.Current.Accent, true, true, false, false, Math.Max(0, 1 - elapsed / 600F));
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        { get { var value = base.CreateParams; value.ExStyle |= 0x08000000 | 0x80 | 0x20; return value; } }
        protected override void Dispose(bool disposing)
        { if (disposing) { lifetime.Dispose(); animation.Dispose(); } base.Dispose(disposing); }
    }
}
