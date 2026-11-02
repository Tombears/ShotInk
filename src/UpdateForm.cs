using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class UpdateForm : Form
    {
        private readonly Label status;
        private readonly TextBox notes;
        private readonly ProgressBar progress;
        private readonly Button action, check;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Func<PreparedUpdate, CancellationToken, Task<string>> install;
        private readonly Func<CancellationToken, Task<UpdateInfo>> checkForUpdates;
        private readonly Func<CancellationToken, Task<UpdateInfo>> verifiedCheck;
        private UpdateInfo info;
        private PreparedUpdate prepared;
        private bool busy, disposed;
        internal UpdateForm(Func<PreparedUpdate, CancellationToken, Task<string>> install, UpdateInfo known = null, Func<CancellationToken, Task<UpdateInfo>> checkForUpdates = null, Func<CancellationToken, Task<UpdateInfo>> verifiedCheck = null)
        {
            this.checkForUpdates = checkForUpdates ?? UpdateService.CheckAsync;
            this.verifiedCheck = verifiedCheck ?? delegate(CancellationToken token) { return UpdateService.CheckWithBrowserAsync(this, token); };
            this.install = install; info = known;
            Text = "版本更新 · ShotInk"; MainForm.UseAppIcon(this); Font = ToolAppearance.Font;
            ClientSize = new Size(620, 430); MinimumSize = new Size(580, 450); StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 6, ColumnCount = 1 };
            foreach (int height in new[] { 30, 40 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 26, 44, 38 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.Controls.Add(new Label { Text = "当前版本 " + Application.ProductVersion, Dock = DockStyle.Fill }, 0, 0);
            status = new Label { Name = "update-status", Dock = DockStyle.Fill }; layout.Controls.Add(status, 0, 1);
            notes = new TextBox { Name = "update-notes", ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.White }; layout.Controls.Add(notes, 0, 2);
            progress = new ProgressBar { Dock = DockStyle.Fill, Visible = false }; layout.Controls.Add(progress, 0, 3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            check = new Button { Text = "检查更新", Width = 100, Height = 32 }; check.Click += async delegate { await CheckAsync(); };
            action = new Button { Text = "下载更新", Width = 170, Height = 32, Enabled = false }; action.Click += async delegate { await ActAsync(); };
            buttons.Controls.Add(check); buttons.Controls.Add(action); layout.Controls.Add(buttons, 0, 4);
            layout.Controls.Add(new Label { Text = "校验下载包后再确认更新并重启，保留保存目录、快捷键和截图历史。", ForeColor = Color.DimGray, Dock = DockStyle.Fill }, 0, 5);
            Controls.Add(layout);
            Shown += async delegate { if (info != null) ShowInfo(); else await CheckAsync(); };
        }
        private void ShowInfo()
        {
            bool available = info.IsNewer(Application.ProductVersion, UpdateService.InstalledRevision(AppDomain.CurrentDomain.BaseDirectory));
            status.Text = available ? (info.Version == Application.ProductVersion ? "发现当前版本的修复更新。" : "发现新版本 " + info.Version) : "当前已是最新版本。";
            notes.Text = (info.Notes ?? "").Replace("\n", Environment.NewLine);
            action.Enabled = available; action.Text = "下载更新";
        }
        private async Task CheckAsync()
        {
            if (busy) return; busy = true; check.Enabled = action.Enabled = false; status.Text = "正在检查更新…";
            try
            {
                bool verification = false;
                try { info = await checkForUpdates(cancellation.Token); }
                catch (VerificationRequiredException) { verification = true; }
                if (verification) { status.Text = "请在验证窗口完成人机验证…"; info = await verifiedCheck(cancellation.Token); }
                if (!IsDisposed) { prepared = null; ShowInfo(); }
            }
            catch (Exception error) { if (!IsDisposed) status.Text = "检查失败：" + error.Message; }
            finally { if (!IsDisposed) { busy = false; check.Enabled = true; } }
        }
        private async Task ActAsync()
        {
            if (busy || info == null) return;
            if (prepared != null)
            {
                busy = true; action.Enabled = check.Enabled = false; status.Text = "正在准备更新并重启…";
                try
                {
                    string error = await install(prepared, cancellation.Token);
                    if (IsDisposed) return;
                    if (error != null) status.Text = error; else Close();
                }
                finally { if (!IsDisposed) { busy = false; action.Enabled = check.Enabled = true; } }
                return;
            }
            busy = true; check.Enabled = action.Enabled = false; progress.Visible = true; progress.Value = 0; status.Text = "正在下载并校验更新包…";
            try
            {
                var downloadProgress = new Progress<int>(delegate(int value) { if (!IsDisposed) progress.Value = value; });
                bool verification = false;
                try { prepared = await UpdateService.DownloadAsync(info, AppDomain.CurrentDomain.BaseDirectory, cancellation.Token, downloadProgress); }
                catch (VerificationRequiredException) { verification = true; }
                if (verification)
                {
                    status.Text = "需要人机验证，请在验证窗口完成操作…";
                    prepared = await UpdateService.DownloadWithBrowserAsync(info, AppDomain.CurrentDomain.BaseDirectory, this, cancellation.Token, downloadProgress);
                }
                if (!IsDisposed) { status.Text = "更新包已校验。请先关闭其他截图、贴图或剪辑窗口，再确认更新。"; action.Text = "确认更新并重启"; action.Enabled = true; }
            }
            catch (Exception error) { if (!IsDisposed) { status.Text = "下载失败：" + error.Message; action.Enabled = true; } }
            finally { if (!IsDisposed) { busy = false; check.Enabled = true; } }
        }
        protected override void Dispose(bool disposing)
        { if (disposing && !disposed) { disposed = true; cancellation.Cancel(); cancellation.Dispose(); } base.Dispose(disposing); }
    }
}
