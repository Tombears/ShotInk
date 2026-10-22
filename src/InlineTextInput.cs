using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ShotInk
{
    // Windows owns text input, selection, clipboard and IME. The canvas owns every
    // visible pixel, including the caret, so editing has no opaque child surface.
    internal sealed class InlineTextInput : TextBox
    {
        private readonly Timer blink;
        private bool notifying;
        internal string Composition = "";
        internal bool CaretVisible = true;
        internal event Action InputStateChanged;
        internal InlineTextInput()
        {
            Multiline = true; AcceptsReturn = true; WordWrap = false;
            BorderStyle = BorderStyle.None; ScrollBars = ScrollBars.None;
            Region = new Region(Rectangle.Empty);
            blink = new Timer { Interval = 500 };
            blink.Tick += delegate { CaretVisible = !CaretVisible; NotifyInputState(); };
        }
        private void NotifyInputState()
        {
            if (notifying || IsDisposed) return;
            notifying = true;
            try { if (InputStateChanged != null) InputStateChanged(); }
            finally { notifying = false; }
        }
        protected override void OnGotFocus(EventArgs e)
        { base.OnGotFocus(e); CaretVisible = true; blink.Start(); PositionIme(); NotifyInputState(); }
        protected override void OnLostFocus(EventArgs e)
        { blink.Stop(); base.OnLostFocus(e); NotifyInputState(); }
        protected override void WndProc(ref Message m)
        {
            int message = m.Msg;
            if (message == 0x10F)
            {
                var context = ImmGetContext(Handle);
                try
                {
                    if (context != IntPtr.Zero)
                    {
                        int bytes = ImmGetCompositionStringW(context, 8, null, 0);
                        if (bytes > 0)
                        {
                            var buffer = new byte[bytes]; ImmGetCompositionStringW(context, 8, buffer, bytes);
                            Composition = Encoding.Unicode.GetString(buffer);
                        }
                        else Composition = "";
                    }
                }
                finally { if (context != IntPtr.Zero) ImmReleaseContext(Handle, context); }
            }
            if (message == 0x10E || message == 0x10F && (m.LParam.ToInt64() & 0x800) != 0) Composition = "";
            base.WndProc(ref m);
            if (message == 0x100 || message == 0x101 || message == 0x102 || message == 0xB1 ||
                message == 0x10D || message == 0x10E || message == 0x10F || message == 0x302 || message == 0x303 || message == 0x304)
            { CaretVisible = true; NotifyInputState(); }
        }
        internal void PositionIme()
        {
            if (!IsHandleCreated || !Focused) return;
            var context = ImmGetContext(Handle);
            if (context == IntPtr.Zero) return;
            try
            {
                var composition = new CompositionForm { Style = 2, Position = Point.Empty };
                ImmSetCompositionWindow(context, ref composition);
                var candidate = new CandidateForm { Style = 0x80, Position = new Point(0, Height), Area = new NativeRect { Right = 1, Bottom = Height } };
                ImmSetCandidateWindow(context, ref candidate);
            }
            finally { ImmReleaseContext(Handle, context); }
        }
        protected override void Dispose(bool disposing) { if (disposing) blink.Dispose(); base.Dispose(disposing); }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct CompositionForm { internal int Style; internal Point Position; internal NativeRect Area; }
        [StructLayout(LayoutKind.Sequential)] private struct CandidateForm { internal int Index, Style; internal Point Position; internal NativeRect Area; }
        [DllImport("imm32.dll")] private static extern IntPtr ImmGetContext(IntPtr window);
        [DllImport("imm32.dll")] private static extern bool ImmReleaseContext(IntPtr window, IntPtr context);
        [DllImport("imm32.dll")] private static extern bool ImmSetCompositionWindow(IntPtr context, ref CompositionForm form);
        [DllImport("imm32.dll")] private static extern bool ImmSetCandidateWindow(IntPtr context, ref CandidateForm form);
        [DllImport("imm32.dll", CharSet = CharSet.Unicode)] private static extern int ImmGetCompositionStringW(IntPtr context, int index, byte[] buffer, int length);
    }
}
