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
        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr word, IntPtr value);
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShellFileInfo
        {
            internal IntPtr Icon;
            internal int IconIndex;
            internal uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] internal string TypeName;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHUpdateImage(string path, int iconIndex, uint flags, int imageIndex);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(uint change, uint flags, string path, IntPtr other);
        internal static void RefreshExecutableIcon(string path)
        {
            ShellFileInfo info;
            if (SHGetFileInfo(path, 0, out info, (uint)Marshal.SizeOf(typeof(ShellFileInfo)), 0x4000) == IntPtr.Zero) return;
            // Updating the same EXE path can leave Explorer's previous image cached.
            SHUpdateImage(path, 0, 0, info.IconIndex);
            SHChangeNotify(0x2000, 0x1005, path, IntPtr.Zero);
        }
        internal static void FlushDesktop() { DwmFlush(); }
        internal static IntPtr WindowAt(Point point) { return WindowFromPoint(point); }
        internal static IntPtr RootWindow(IntPtr window) { return GetAncestor(window, 2); }
        internal static bool PostWheel(IntPtr window, Point point, int delta)
        { return PostMessage(window, 0x020A, new IntPtr(unchecked(delta << 16)), new IntPtr(unchecked((point.Y << 16) | (point.X & 0xFFFF)))); }
        internal static void RaiseWithoutActivation(IntPtr window) { SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x13); }
        internal static bool ExcludeFromCapture(IntPtr window, bool excluded)
        { try { return SetWindowDisplayAffinity(window, excluded ? 0x11U : 0U); } catch (EntryPointNotFoundException) { return false; } }

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
