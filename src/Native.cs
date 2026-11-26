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
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        internal static bool ShortcutAvailable(IntPtr window, Keys keys)
        {
            const int probeId = 0x6001;
            if (!RegisterHotKey(window, probeId, Shortcut.Modifiers(keys), (uint)(keys & Keys.KeyCode)))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 1409) return false;
                throw new System.ComponentModel.Win32Exception(error);
            }
            try { return true; }
            finally { UnregisterHotKey(window, probeId); }
        }
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, int data, UIntPtr extra);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [StructLayout(LayoutKind.Sequential)]
        private struct AlphaBlend
        {
            internal byte Operation, Flags, Opacity, Format;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct DibHeader
        {
            internal int Size, Width, Height;
            internal ushort Planes, BitsPerPixel;
            internal int Compression, ImageSize, HorizontalResolution, VerticalResolution, ColorsUsed, ColorsImportant;
        }
        [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr targetDc,
            ref Point destination, ref Size size, IntPtr sourceDc, ref Point source, int colorKey, ref AlphaBlend blend, int flags);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref DibHeader bitmapInfo,
            uint usage, out IntPtr pixels, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr handle);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyMemory(IntPtr destination, IntPtr source, UIntPtr length);
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
        internal static bool PresentLayer(IntPtr window, Bitmap image, Point location)
        {
            var dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return false;
            try
            {
                // GetHbitmap composites transparent pixels over a background. Copy into
                // a top-down DIB so UpdateLayeredWindow receives the original premultiplied alpha.
                var header = new DibHeader { Size = Marshal.SizeOf(typeof(DibHeader)), Width = image.Width,
                    Height = -image.Height, Planes = 1, BitsPerPixel = 32 };
                IntPtr pixels;
                var bitmap = CreateDIBSection(dc, ref header, 0, out pixels, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero) return false;
                try
                {
                    var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                    try
                    {
                        int stride = image.Width * 4;
                        if (data.Stride == stride) CopyMemory(pixels, data.Scan0, new UIntPtr((uint)(stride * image.Height)));
                        else for (int y = 0; y < image.Height; y++)
                            CopyMemory(IntPtr.Add(pixels, y * stride), IntPtr.Add(data.Scan0, y * data.Stride), new UIntPtr((uint)stride));
                    }
                    finally { image.UnlockBits(data); }
                    var previous = SelectObject(dc, bitmap);
                    if (previous == IntPtr.Zero || previous == new IntPtr(-1)) return false;
                    try
                    {
                        var source = Point.Empty;
                        var size = image.Size;
                        var blend = new AlphaBlend { Opacity = 255, Format = 1 };
                        return UpdateLayeredWindow(window, IntPtr.Zero, ref location, ref size, dc, ref source, 0, ref blend, 2);
                    }
                    finally { SelectObject(dc, previous); }
                }
                finally { DeleteObject(bitmap); }
            }
            finally { DeleteDC(dc); }
        }
        internal static bool ExcludeFromCapture(IntPtr window, bool excluded)
        { try { return SetWindowDisplayAffinity(window, excluded ? 0x11U : 0U); } catch (EntryPointNotFoundException) { return false; } }

        private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out WindowRect rect, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int GetCloaked(IntPtr window, int attribute, out int value, int size);
        [StructLayout(LayoutKind.Sequential)]
        private struct WindowRect { internal int Left, Top, Right, Bottom; }
        internal static Rectangle[] CaptureWindowBounds(Rectangle desktopBounds)
        {
            var windows = new System.Collections.Generic.List<Rectangle>();
            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                if (!IsWindowVisible(window) || IsIconic(window)) return true;
                int cloaked;
                if (GetCloaked(window, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                WindowRect rect;
                if (DwmGetWindowAttribute(window, 9, out rect, Marshal.SizeOf(typeof(WindowRect))) != 0 && !GetWindowRect(window, out rect)) return true;
                var bounds = Rectangle.Intersect(desktopBounds, Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
                if (bounds.Width >= 8 && bounds.Height >= 8) { bounds.Offset(-desktopBounds.X, -desktopBounds.Y); windows.Add(bounds); }
                return true;
            }, IntPtr.Zero);
            return windows.ToArray();
        }

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
