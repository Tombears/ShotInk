using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class ScrollCapture : Form
    {
        private readonly Rectangle region;
        private readonly Label status;
        private readonly ComboBox mode;
        private readonly NumericUpDown delay;
        private readonly Button start;
        private readonly Button next;
        private readonly Button finish;
        private readonly Button cancel;
        private readonly VerticalStitcher stitcher = new VerticalStitcher();
        private bool running;
        private bool busy;
        private bool cancelled;
        private bool finished;
        private bool pendingFinish;
        internal event Action<Bitmap> Completed;
        internal event Action Cancelled;
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000; return cp; }
        }

        internal ScrollCapture(Rectangle region)
        {
            this.region = region;
            Text = "长截图";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(660, 122);
            BackColor = Theme.Background;
            status = Theme.Label("将鼠标移到目标窗口；准备好后开始。", 9, Theme.Ink);
            status.SetBounds(14, 12, 620, 24);
            Controls.Add(status);
            var bar = new FlowLayoutPanel { Location = new Point(8, 42), Size = new Size(650, 52), WrapContents = false };
            mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(5, 12, 3, 0) };
            mode.Items.AddRange(new object[] { "自动滚动", "手动滚动" });
            mode.SelectedIndex = 0;
            delay = new NumericUpDown { Minimum = 500, Maximum = 5000, Increment = 250, Value = 1000, Width = 65, Margin = new Padding(3, 12, 1, 0) };
            var unit = Theme.Label("ms", 9, Theme.Muted); unit.Margin = new Padding(0, 13, 3, 0);
            start = Theme.Button("开始", true, 66);
            next = Theme.Button("采集一屏", false, 88); next.Enabled = false;
            finish = Theme.Button("完成", false, 66); finish.Enabled = false;
            cancel = Theme.Button("取消", false, 66);
            bar.Controls.AddRange(new Control[] { mode, delay, unit, start, next, finish, cancel });
            Controls.Add(bar);
            var note = Theme.Label("手动模式：在原窗口向下滚动一小段，再点「采集一屏」。", 8, Theme.Muted);
            note.Location = new Point(14, 99); Controls.Add(note);
            StartPosition = FormStartPosition.Manual;
            var work = Screen.FromRectangle(region).WorkingArea;
            int y = region.Top - Height - 8 >= work.Top ? region.Top - Height - 8 : region.Bottom + 8;
            Location = new Point(Math.Max(work.Left, Math.Min(region.Left, work.Right - Width)), Math.Max(work.Top, Math.Min(y, work.Bottom - Height)));
            start.Click += async delegate { await StartCapture(); };
            next.Click += async delegate { await CaptureFrame(false); };
            finish.Click += delegate { Finish(); };
            cancel.Click += delegate { cancelled = true; running = false; Close(); };
            FormClosing += delegate { running = false; cancelled = !finished; };
        }

        private async Task StartCapture()
        {
            if (running || busy) return;
            running = true;
            start.Enabled = false; mode.Enabled = false; delay.Enabled = false;
            status.Text = "准备中… 请保持目标窗口可见。";
            var target = new Point(region.Left + region.Width / 2, region.Top + region.Height / 2);
            Native.FocusAt(target);
            await Task.Delay(500);
            if (cancelled || IsDisposed) return;
            await CaptureFrame(false);
            if (mode.SelectedIndex == 1) { next.Enabled = true; return; }
            int unchanged = 0;
            while (running && !cancelled && !IsDisposed)
            {
                Native.ScrollDown(target);
                await Task.Delay((int)delay.Value);
                if (!running || cancelled || IsDisposed) break;
                var result = await CaptureFrame(true);
                if (cancelled || finished || IsDisposed) break;
                if (result == StitchStatus.Unchanged) unchanged++; else unchanged = 0;
                if (result == StitchStatus.Unmatched || result == StitchStatus.LimitReached)
                {
                    running = false;
                    status.Text = result == StitchStatus.LimitReached ? "已到安全尺寸上限，请点「完成」保存已采集内容。" : "内容未能可靠对齐，已暂停。请点「完成」保存已采集内容。";
                }
                if (unchanged >= 3) { Finish(); break; }
            }
        }

        private async Task<StitchStatus> CaptureFrame(bool automatic)
        {
            if (busy || cancelled || IsDisposed) return StitchStatus.Unchanged;
            busy = true;
            next.Enabled = false;
            try
            {
                Hide();
                await Task.Delay(140);
                if (cancelled || IsDisposed) return StitchStatus.Unchanged;
                StitchStatus result;
                using (var frame = Native.Capture(region)) result = stitcher.Add(frame);
                Show();
                finish.Enabled = stitcher.Frames > 0;
                status.Text = "已采集 " + stitcher.Frames + " 屏 · " + stitcher.Width + " × " + stitcher.Height + " px";
                if (result == StitchStatus.Unchanged) status.Text += " · 画面未变化";
                if (result == StitchStatus.Unmatched) status.Text += " · 对齐失败，请减少滚动距离后重试";
                if (result == StitchStatus.LimitReached) status.Text += " · 已到尺寸上限";
                return result;
            }
            catch (Exception ex)
            {
                running = false;
                if (!IsDisposed) { Show(); status.Text = "采集失败：" + ex.Message; }
                return StitchStatus.Unmatched;
            }
            finally
            {
                busy = false;
                if (cancelled || IsDisposed) stitcher.Dispose();
                else if (pendingFinish) Finish();
                else next.Enabled = mode.SelectedIndex == 1 && running;
            }
        }

        private void Finish()
        {
            running = false;
            if (busy) { pendingFinish = true; return; }
            if (finished || stitcher.Frames == 0) return;
            var result = stitcher.Build();
            finished = true;
            Close();
            if (Completed != null) Completed(result); else result.Dispose();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (!busy) stitcher.Dispose();
            if (!finished && Cancelled != null) Cancelled();
            base.OnFormClosed(e);
        }
    }
}
