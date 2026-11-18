using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class CropActions : Panel
    {
        private readonly ToolOptionButton finish;
        private readonly ToolOptionButton cancel;
        internal event Action<bool> Finished;

        internal CropActions()
        {
            Size = new Size(124, 28);
            finish = new ToolOptionButton { Name = "finish-crop", Text = "完成", Bounds = new Rectangle(0, 0, 60, 28), Selected = true };
            cancel = new ToolOptionButton { Name = "cancel-crop", Text = "取消", Bounds = new Rectangle(64, 0, 60, 28) };
            finish.Click += delegate { if (Finished != null) Finished(true); };
            cancel.Click += delegate { if (Finished != null) Finished(false); };
            Controls.Add(finish);
            Controls.Add(cancel);
            RefreshSkin();
        }

        internal void RefreshSkin()
        {
            BackColor = Skins.Current.Surface;
            finish.AccentColor = cancel.AccentColor = ToolAppearance.Accent("crop");
            Invalidate(true);
        }
    }
}
