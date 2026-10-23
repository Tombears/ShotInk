using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed partial class AnnotationCanvas
    {
        private HScrollBar documentHorizontal;
        private Size documentClient;
        internal float DocumentOffsetX { get; private set; }
        internal void UseDocumentViewport(Size frame)
        {
            documentFrame = frame;
            documentScroll = new VScrollBar { Name = "long-image-scrollbar" };
            documentHorizontal = new HScrollBar { Name = "long-image-horizontal-scrollbar" };
            documentScroll.ValueChanged += delegate { DocumentOffsetY = documentScroll.Value / Zoom; Invalidate(); };
            documentHorizontal.ValueChanged += delegate { DocumentOffsetX = documentHorizontal.Value / Zoom; Invalidate(); };
            Controls.Add(documentScroll); Controls.Add(documentHorizontal);
            // Keep the screenshot's pixels intact; scrollbars must never shrink the document.
            SetZoom(1);
        }
        private void UpdateDocumentViewport(float offsetX, float offsetY)
        {
            int barWidth = SystemInformation.VerticalScrollBarWidth, barHeight = SystemInformation.HorizontalScrollBarHeight;
            int imageWidth = (int)Math.Ceiling(Image.Width * Zoom), imageHeight = (int)Math.Ceiling(Image.Height * Zoom);
            bool vertical = imageHeight > Height;
            bool horizontal = imageWidth > Width - (vertical ? barWidth : 0);
            vertical = imageHeight > Height - (horizontal ? barHeight : 0);
            horizontal = imageWidth > Width - (vertical ? barWidth : 0);
            documentClient = new Size(Math.Max(1, Width - (vertical ? barWidth : 0)), Math.Max(1, Height - (horizontal ? barHeight : 0)));
            documentScroll.SetBounds(Width - barWidth, 0, barWidth, documentClient.Height);
            documentHorizontal.SetBounds(0, Height - barHeight, documentClient.Width, barHeight);
            documentScroll.LargeChange = documentClient.Height; documentScroll.SmallChange = 20;
            documentHorizontal.LargeChange = documentClient.Width; documentHorizontal.SmallChange = 20;
            documentScroll.Maximum = Math.Max(0, imageHeight - 1); documentHorizontal.Maximum = Math.Max(0, imageWidth - 1);
            SetScrollValue(documentScroll, (int)Math.Round(offsetY * Zoom));
            SetScrollValue(documentHorizontal, (int)Math.Round(offsetX * Zoom));
            DocumentOffsetY = documentScroll.Value / Zoom; DocumentOffsetX = documentHorizontal.Value / Zoom;
            documentScroll.Visible = vertical; documentHorizontal.Visible = horizontal;
        }
        private static void SetScrollValue(ScrollBar bar, int value)
        { bar.Value = Math.Max(0, Math.Min(Math.Max(0, bar.Maximum - bar.LargeChange + 1), value)); }
        internal void ScrollDocument(int pixels)
        {
            if (documentScroll == null) return;
            CommitTextEdit(); SetScrollValue(documentScroll, documentScroll.Value + pixels);
        }
        internal void ScrollDocumentHorizontal(int pixels)
        {
            if (documentHorizontal == null) return;
            CommitTextEdit(); SetScrollValue(documentHorizontal, documentHorizontal.Value + pixels);
        }
    }
}
