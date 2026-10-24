using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed partial class AnnotationCanvas
    {
        internal event Action SearchRequested;
        internal int SearchMatchCount { get { return recognition == null ? 0 : recognition.MatchCount; } }
        internal int SearchMatchNumber { get { return SearchMatchCount == 0 ? 0 : recognition.MatchIndex + 1; } }
        internal void SearchText(string query)
        {
            if (recognition == null) return;
            recognition.Search(query); RevealSearchMatch(); NotifyTextSelection();
        }
        internal void MoveSearchMatch(int direction)
        {
            if (recognition == null) return;
            recognition.MoveMatch(direction); RevealSearchMatch(); NotifyTextSelection();
        }
        private void RevealSearchMatch()
        {
            var bounds = recognition.CurrentMatch;
            if (bounds.IsEmpty) return;
            if (documentScroll != null)
            {
                SetScrollValue(documentScroll, (int)((bounds.Top + bounds.Height / 2) * Zoom - documentClient.Height / 2F));
                SetScrollValue(documentHorizontal, (int)((bounds.Left + bounds.Width / 2) * Zoom - documentClient.Width / 2F));
            }
            else
            {
                var viewport = Parent as ScrollableControl;
                if (viewport == null || !viewport.AutoScroll) return;
                int x = (int)((bounds.Left + bounds.Width / 2) * Zoom) + Left - viewport.AutoScrollPosition.X - viewport.ClientSize.Width / 2;
                int y = (int)((bounds.Top + bounds.Height / 2) * Zoom) + Top - viewport.AutoScrollPosition.Y - viewport.ClientSize.Height / 2;
                viewport.AutoScrollPosition = new Point(Math.Max(0, x), Math.Max(0, y));
            }
        }
    }
}
