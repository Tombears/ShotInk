using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class WechatGifPreview : Control
    {
        private Bitmap thumbnail;
        internal bool IsFile { get; private set; }
        internal Size FrameSize { get; private set; }

        internal WechatGifPreview()
        {
            Name = "gif-wechat-preview";
            Text = "GIF 大小\n计算中…";
            AccessibleName = "微信预览";
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        internal void SetFrame(Image image, int scalePercent = 100)
        {
            var previous = thumbnail; thumbnail = null;
            FrameSize = image == null ? Size.Empty : GifEncoder.OutputSize(image.Size, scalePercent);
            IsFile = FrameSize.Width > 980 || FrameSize.Height > 980;
            if (image != null && !IsFile)
            {
                float scale = Math.Min(1F, Math.Min(160F / image.Width, 100F / image.Height));
                thumbnail = new Bitmap(Math.Max(1, (int)Math.Round(image.Width * scale)), Math.Max(1, (int)Math.Round(image.Height * scale)));
                using (var graphics = Graphics.FromImage(thumbnail))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(image, new Rectangle(Point.Empty, thumbnail.Size));
                }
            }
            if (previous != null) previous.Dispose();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            const int contentTop = 40;
            int titleWidth = TextRenderer.MeasureText(e.Graphics, "微信预览", Font).Width;
            TextRenderer.DrawText(e.Graphics, "微信预览", Font, new Rectangle(8, 0, titleWidth, 36), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            using (var reminderFont = new Font(Font.FontFamily, 7.5F))
                TextRenderer.DrawText(e.Graphics, Text, reminderFont, new Rectangle(8 + titleWidth, 0, Math.Max(1, Width - titleWidth - 16), 36), ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            if (FrameSize.IsEmpty) return;
            if (IsFile)
            {
                var card = new Rectangle(8, contentTop, Math.Max(1, Width - 16), 76);
                using (var background = new SolidBrush(Color.FromArgb(52, 52, 52))) e.Graphics.FillRectangle(background, card);
                TextRenderer.DrawText(e.Graphics, "录屏.gif", Font, new Rectangle(card.X + 10, card.Y + 12, card.Width - 48, 24), Color.White,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                TextRenderer.DrawText(e.Graphics, FrameSize.Width + " × " + FrameSize.Height + " px", Font,
                    new Rectangle(card.X + 10, card.Y + 45, card.Width - 12, 22), Color.Silver, TextFormatFlags.SingleLine);
                var icon = new Rectangle(card.Right - 36, card.Top + 14, 26, 32);
                using (var background = new SolidBrush(Color.FromArgb(98, 112, 140))) e.Graphics.FillRectangle(background, icon);
                TextRenderer.DrawText(e.Graphics, "GIF", Font, icon, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            if (thumbnail != null) e.Graphics.DrawImageUnscaled(thumbnail, 8, contentTop);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && thumbnail != null) { thumbnail.Dispose(); thumbnail = null; }
            base.Dispose(disposing);
        }
    }
}
