using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed partial class CaptureOverlay
    {
        public bool PreFilterMessage(ref Message message)
        {
            const int setCursor = 0x20, mouseMove = 0x200, leftDown = 0x201, leftDoubleClick = 0x203;
            if (message.Msg != setCursor && message.Msg != mouseMove && message.Msg != leftDown && message.Msg != leftDoubleClick) return false;
            if (!Visible || !ready || dragging || !CanReframeSelection || scrollSession != null || gifSession != null) return false;
            if (canvas != null && canvas.Capture) return false;
            var target = Control.FromHandle(message.Msg == setCursor ? message.WParam : message.HWnd);
            // Toolbar controls and text inputs keep their own mouse handling.
            if (target != this && target != canvas) return false;
            Point point;
            if (message.Msg == setCursor) point = PointToClient(Cursor.Position);
            else
            {
                long packed = message.LParam.ToInt64();
                var local = new Point(unchecked((short)(packed & 0xffff)), unchecked((short)((packed >> 16) & 0xffff)));
                point = PointToClient(target.PointToScreen(local));
            }
            int radius = Math.Max(6, (int)Math.Round(6 * DeviceDpi / 96.0));
            int handle = SelectionGeometry.ResizeHandleAt(selection, point, radius);
            if (handle < 0) return false;
            if (message.Msg == setCursor || message.Msg == mouseMove) Cursor.Current = HandleCursor(handle);
            else BeginAdjust(handle, point);
            return true;
        }
    }
}
