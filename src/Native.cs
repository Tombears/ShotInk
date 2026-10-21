using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShotInk
{
    internal static class Native
    {
        internal const int HotKeyMessage = 0x0312;
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, int data, UIntPtr extra);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

        internal static void EnableDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; }
            catch (EntryPointNotFoundException) { }
            SetProcessDPIAware();
        }

        internal static Bitmap Capture(Rectangle region)
        {
            var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format24bppRgb);
            try
            {
                using (var graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy);
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }

        internal static void FocusAt(Point point)
        {
            var window = GetAncestor(WindowFromPoint(point), 2);
            if (window != IntPtr.Zero) SetForegroundWindow(window);
        }

        internal static void ScrollDown(Point point)
        {
            Cursor.Position = point;
            mouse_event(0x0800, 0, 0, -240, UIntPtr.Zero);
        }
    }
}
