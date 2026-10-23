using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShotInk
{
    // Route real wheel input to the page, including when the pointer stays over the toolbar.
    // Wait for sampled frames to settle before forwarding another notch.
    internal sealed class ScrollWheelRouter : IDisposable
    {
        private delegate IntPtr MouseCallback(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)]
        private struct MouseData { internal Point Point; internal uint Data, Flags, Time; internal UIntPtr Extra; }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, MouseCallback callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string name);
        private readonly MouseCallback callback;
        private readonly IntPtr overlay, target;
        private readonly Rectangle region;
        private readonly ScrollCaptureSession session;
        private readonly Timer timer;
        private IntPtr hook;
        private int pending, sentAt;
        private bool disposed;

        internal ScrollWheelRouter(IntPtr overlay, Rectangle region, ScrollCaptureSession session)
        {
            this.overlay = overlay; this.region = region; this.session = session;
            target = Native.WindowAt(Center);
            if (target == IntPtr.Zero || Native.RootWindow(target) == overlay)
                throw new InvalidOperationException("未找到选框下方的页面，请重新选择截图区域。");
            callback = OnMouse;
            hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            sentAt = Environment.TickCount;
            timer = new Timer { Interval = 10 }; timer.Tick += ForwardPending; timer.Start();
        }
        private Point Center { get { return new Point(region.Left + region.Width / 2, region.Top + region.Height / 2); } }
        private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
        {
            if (!disposed && code >= 0 && message.ToInt32() == 0x020A)
            {
                var mouse = (MouseData)Marshal.PtrToStructure(data, typeof(MouseData));
                if (region.Contains(mouse.Point) || Native.RootWindow(Native.WindowAt(mouse.Point)) == overlay)
                {
                    int delta = unchecked((short)(mouse.Data >> 16));
                    pending = Math.Max(-6000, Math.Min(6000, pending + delta));
                    return new IntPtr(1);
                }
            }
            return CallNextHookEx(hook, code, message, data);
        }
        private void ForwardPending(object sender, EventArgs args)
        {
            if (disposed || session.IsFinishing) return;
            int elapsed = unchecked(Environment.TickCount - sentAt);
            if (session.AlignmentLost && elapsed >= 300 && ((session.IsCaughtUp && session.StableMilliseconds >= 160) || elapsed >= 1000))
            { pending = 0; session.Finish(); return; }
            bool sampled = session.IsCaughtUp && session.StableMilliseconds >= 32;
            // A video or blinking caret must not prevent the page from receiving wheel input forever.
            if (!sampled) sampled = elapsed >= 180 && session.Frames > 0 && session.PendingFrames <= 1;
            if (pending == 0 || elapsed < 70 || !sampled || session.AlignmentLost) return;
            int delta = Math.Sign(pending) * Math.Min(120, Math.Abs(pending));
            if (!Native.PostWheel(target, Center, delta)) { pending = 0; session.Finish(); return; }
            pending -= delta; sentAt = Environment.TickCount;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            timer.Stop(); timer.Dispose();
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            GC.KeepAlive(callback);
        }
    }
}
