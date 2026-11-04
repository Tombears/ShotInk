using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class CaptureFeedback : Form
    {
        private readonly Timer lifetime = new Timer { Interval = 1500 };
        internal CaptureFeedback()
        {
            SetStyle(ControlStyles.Selectable, false);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(30, 40, 58); ClientSize = new Size(210, 48);
            var message = new Label { Text = "✓  截图已复制", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 11), UseMnemonic = false };
            Controls.Add(message);
            var work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(work.Right - Width - 24, work.Bottom - Height - 24);
            lifetime.Tick += delegate { Close(); };
            Shown += delegate { Native.RaiseWithoutActivation(Handle); Native.ExcludeFromCapture(Handle, true); lifetime.Start(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        { get { var value = base.CreateParams; value.ExStyle |= 0x08000000 | 0x80 | 0x20; return value; } }
        protected override void Dispose(bool disposing)
        { if (disposing) lifetime.Dispose(); base.Dispose(disposing); }
    }
}
