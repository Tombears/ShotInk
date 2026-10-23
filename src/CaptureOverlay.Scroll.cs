using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed partial class CaptureOverlay
    {
        private ScrollCaptureSession scrollSession;
        private Panel longViewport;
        private bool longLimitReached;
        private bool scrollEscapeRegistered;
        private ScrollWheelRouter scrollWheel;
        private bool longAlignmentLost;
        internal Func<Rectangle, Bitmap> CaptureScrollFrame = Native.Capture;
        internal bool InterceptScrollWheel = true;
        private void ResetRecognitionSession()
        {
            if (recognition != null) recognition.Dispose();
            recognition = new TextRecognitionSession(this, delegate { return canvas.Render(); }, delegate { return canvas.Revision; }, toolbar.SetRecognitionBusy,
                delegate(OcrDocument document) { toolbar.SelectTool(DrawTool.None); canvas.SetTextRecognition(document); canvas.Focus(); });
        }
        private async void StartLongCapture() { await CaptureLongAsync(); }
        internal async Task CaptureLongAsync()
        {
            if (!ready || scrollSession != null || longViewport != null) return;
            if (selection.Width < 80 || selection.Height < 120)
            { MessageBox.Show(this, "长截图区域至少需要 80 × 120 像素。", "选区过小"); return; }
            var region = new Rectangle(selection.X + screenBounds.X, selection.Y + screenBounds.Y, selection.Width, selection.Height);
            var session = new ScrollCaptureSession(delegate { return CaptureScrollFrame(region); });
            scrollSession = session;
            try
            {
                toolbar.SelectTool(DrawTool.None); toolbar.SetScrollCapture(true); canvas.Hide(); sizeHint.Hide();
                foreach (var grip in grips) grip.Hide();
                PositionToolbar();
                bool excluded = Native.ExcludeFromCapture(Handle, true);
                if (toolbar.Bounds.IntersectsWith(selection) && !excluded)
                    throw new InvalidOperationException("请缩小选区，为完成按钮留出屏幕空间。");
                var surface = new Region(ClientRectangle); var hole = selection; hole.Inflate(5, 5);
                surface.Exclude(hole); surface.Union(toolbar.Bounds);
                var previous = Region; Region = surface; if (previous != null) previous.Dispose();
                Capture = false; Native.ReleaseCapture();
                scrollEscapeRegistered = Native.RegisterHotKey(Handle, 1001, 0x4000, (uint)Keys.Escape);
                Native.FocusAt(new Point(region.Left + region.Width / 2, region.Top + region.Height / 2));
                Native.RaiseWithoutActivation(Handle);
                await Task.Delay(60);
                if (IsDisposed || !object.ReferenceEquals(scrollSession, session)) return;
                Native.FlushDesktop();
                scrollWheel = new ScrollWheelRouter(Handle, region, session, InterceptScrollWheel);
                using (var result = await session.RunAsync())
                {
                    if (IsDisposed || !object.ReferenceEquals(scrollSession, session)) return;
                    ResetCaptureRegion();
                    longLimitReached = result.LimitReached; longAlignmentLost = result.AlignmentLost; ShowLongPreview(result.TakeImage());
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!IsDisposed) { ResetCaptureRegion(); MessageBox.Show(this, error.Message, "长截图未完成"); } }
            finally
            {
                session.Dispose();
                if (object.ReferenceEquals(scrollSession, session))
                {
                    scrollSession = null;
                    if (!IsDisposed)
                    {
                        ResetCaptureRegion(); if (canvas != null) canvas.Show();
                        toolbar.SetScrollCapture(false); toolbar.SetLongPreview(longViewport != null);
                        UpdateSelectionChrome(); PositionToolbar(); Activate(); Invalidate(true);
                    }
                }
            }
        }
        private void FinishLongCapture()
        { if (scrollSession != null) { toolbar.SetScrollFinishing(); scrollSession.Finish(); } }
        private void ResetCaptureRegion()
        {
            if (scrollWheel != null) { scrollWheel.Dispose(); scrollWheel = null; }
            if (IsDisposed || !IsHandleCreated) return;
            var previous = Region; Region = null; if (previous != null) previous.Dispose();
            Native.ExcludeFromCapture(Handle, false);
            if (scrollEscapeRegistered) { Native.UnregisterHotKey(Handle, 1001); scrollEscapeRegistered = false; }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x020A && scrollWheel != null)
            { scrollWheel.QueueWheel(unchecked((short)(message.WParam.ToInt64() >> 16))); return; }
            if (message.Msg == Native.HotKeyMessage && message.WParam.ToInt32() == 1001 && scrollSession != null) { Close(); return; }
            base.WndProc(ref message);
        }
        internal void ShowLongPreview(Bitmap image)
        {
            var previous = canvas;
            var next = new AnnotationCanvas(image) { Tool = DrawTool.None };
            foreach (var mark in previous.Marks) next.Marks.Add(mark.Clone());
            longViewport = new LongImageViewport { Name = "long-image-viewport", Bounds = selection, BackColor = Color.White };
            next.UseDocumentViewport(selection.Size);
            next.Location = Point.Empty; canvas = next; longViewport.Controls.Add(next); Controls.Add(longViewport);
            next.Changed += delegate { toolbar.UpdateHistory(canvas.CanUndo, canvas.CanRedo); };
            next.TextSelectionChanged += delegate { toolbar.SetTextSelection(canvas.HasTextRecognition, canvas.HasSelectedText); };
            next.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right) Close();
                else if (e.Button == MouseButtons.Left && canvas.Tool == DrawTool.None && !canvas.HasTextRecognition)
                    BeginAdjust(-1, PointToClient(canvas.PointToScreen(e.Location)));
            };
            next.MouseDoubleClick += delegate { if (canvas.Tool == DrawTool.None && !canvas.HasTextRecognition) Finish(CaptureAction.Copy); };
            Controls.Remove(previous); previous.Dispose(); ResetRecognitionSession();
            SyncSettings(); toolbar.SetLongPreview(true); toolbar.UpdateHistory(canvas.CanUndo, canvas.CanRedo);
            UpdateSelectionChrome(); PositionToolbar(); canvas.Focus();
        }
        private sealed class LongImageViewport : Panel
        {
            internal LongImageViewport() { DoubleBuffered = true; }
            protected override Point ScrollToControl(Control activeControl) { return DisplayRectangle.Location; }
        }
    }
}
