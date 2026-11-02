using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ShotInk
{
    // Verification is performed by the user. The browser retains its own cookies
    // and performs the subsequent download in that same browser session.
    internal sealed class GiteeBrowserForm : Form
    {
        private readonly WebView2 browser;
        private readonly Label status;
        private readonly Button retry;
        private readonly TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly UpdateInfo package;
        private readonly string url, destination, profile;
        private readonly Func<Uri, bool> allowed;
        private readonly IProgress<int> progress;
        private CoreWebView2DownloadOperation download;
        private Form owner;
        private bool ownerDisabled, disposed, metadataResponse;
        internal byte[] Metadata { get; private set; }
        internal bool VerificationShown { get; private set; }
        internal WebView2 Browser { get { return browser; } }
        internal GiteeBrowserForm(UpdateInfo package, string destination, IProgress<int> progress = null, string testUrl = null, Func<Uri, bool> testAllowed = null, string testProfile = null)
        {
            this.package = package; this.destination = destination; this.progress = progress;
            url = testUrl ?? (package == null ? UpdateService.IndexUrl : package.Url);
            allowed = testAllowed ?? Trusted; profile = testProfile ?? Path.Combine(UpdateService.CacheRoot, "browser");
            Text = "Gitee 安全验证 · ShotInk"; MainForm.UseAppIcon(this); Font = ToolAppearance.Font;
            ClientSize = new Size(880, 660); MinimumSize = new Size(660, 480); StartPosition = FormStartPosition.CenterParent;
            status = new Label { Text = "请在下方完成 Gitee 验证，完成后将继续更新。", Dock = DockStyle.Top, Height = 42, Padding = new Padding(12) };
            retry = new Button { Text = "已完成验证，继续", Dock = DockStyle.Bottom, Height = 38 };
            browser = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(browser); Controls.Add(status); Controls.Add(retry);
            retry.Click += delegate { if (browser.CoreWebView2 != null && download == null) browser.CoreWebView2.Navigate(url); };
            FormClosing += delegate { if (!completion.Task.IsCompleted) completion.TrySetCanceled(); };
        }
        internal static bool Trusted(Uri uri)
        { return uri != null && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && (uri.Host == "gitee.com" || uri.Host.EndsWith(".gitee.com", StringComparison.OrdinalIgnoreCase)); }
        internal async Task RunAsync(Form owner, CancellationToken token)
        {
            this.owner = owner;
            using (token.Register(delegate { completion.TrySetCanceled(); }))
            {
                try
                {
                    Directory.CreateDirectory(profile); var handle = Handle;
                    var environment = await CoreWebView2Environment.CreateAsync(null, profile);
                    token.ThrowIfCancellationRequested();
                    await browser.EnsureCoreWebView2Async(environment);
                    token.ThrowIfCancellationRequested();
                    var core = browser.CoreWebView2;
                    core.Settings.AreDevToolsEnabled = false; core.Settings.AreHostObjectsAllowed = false;
                    core.Settings.AreDefaultContextMenusEnabled = false;
                    core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e)
                    { e.Handled = true; string requested = e.Uri; Uri uri; if (Uri.TryCreate(requested, UriKind.Absolute, out uri) && allowed(uri)) BeginInvoke(new Action(delegate { core.Navigate(requested); })); };
                    core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
                    core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                    {
                        Uri uri; if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out uri) || !allowed(uri))
                        { e.Cancel = true; Fail(new InvalidDataException("验证页面跳转到了非 Gitee 地址。")); }
                    };
                    core.DownloadStarting += DownloadStarting;
                    core.WebResourceResponseReceived += MetadataResponseReceived;
                    core.NavigationCompleted += NavigationCompleted;
                    core.ProcessFailed += delegate { Fail(new IOException("Gitee 验证页面进程已退出，请重新下载。")); };
                    core.Navigate(url);
                    await completion.Task;
                }
                catch (WebView2RuntimeNotFoundException)
                { throw new IOException("安全验证需要 Microsoft Edge WebView2 Runtime，请安装后重试，或从 Gitee 发行页手动下载更新包。"); }
                finally
                {
                    if (download != null && download.State == CoreWebView2DownloadState.InProgress) download.Cancel();
                    if (!IsDisposed) Hide(); RestoreOwner();
                }
            }
        }
        private async void NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (disposed || completion.Task.IsCompleted || download != null) return;
            if (metadataResponse || !e.IsSuccess && e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;
            try
            {
                if (package == null && e.IsSuccess)
                {
                    string body = await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('pre') ? document.querySelector('pre').textContent : (document.body ? document.body.innerText : '')");
                    if (disposed || completion.Task.IsCompleted) return;
                    string text = UpdateService.ReadJson<string>(Encoding.UTF8.GetBytes(body));
                    byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
                    if (bytes.Length > 256 * 1024) throw new InvalidDataException("Gitee 更新信息超过允许大小。");
                    try { UpdateService.DecodeIndex(bytes); Metadata = bytes; completion.TrySetResult(true); return; }
                    catch (System.Runtime.Serialization.SerializationException) { }
                    catch (InvalidDataException) { }
                    catch (FormatException) { }
                }
                if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    status.Text = "页面加载失败（" + e.WebErrorStatus + "），可点击下方按钮重试。";
                BeginInvoke(new Action(ShowVerification));
            }
            catch (Exception error) { Fail(error); }
        }
        private async void MetadataResponseReceived(object sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            if (package != null || disposed || completion.Task.IsCompleted) return;
            Uri actual, expected = new Uri(url);
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out actual) || actual.Host != expected.Host || actual.AbsolutePath != expected.AbsolutePath ||
                !e.Response.Headers.Contains("Content-Type") || !e.Response.Headers.GetHeader("Content-Type").ToLowerInvariant().Contains("json")) return;
            metadataResponse = true;
            try
            {
                using (var input = await e.Response.GetContentAsync())
                using (var output = new MemoryStream())
                {
                    byte[] buffer = new byte[8192]; int count;
                    while ((count = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    { if (output.Length + count > 256 * 1024) throw new InvalidDataException("Gitee 更新信息超过允许大小。"); output.Write(buffer, 0, count); }
                    if (disposed || completion.Task.IsCompleted) return;
                    byte[] bytes = output.ToArray(); UpdateService.DecodeIndex(bytes); Metadata = bytes; completion.TrySetResult(true);
                }
            }
            catch (Exception error) { if (!disposed && !completion.Task.IsCompleted) Fail(error); }
        }
        private void ShowVerification()
        {
            if (disposed || completion.Task.IsCompleted || download != null) return;
            VerificationShown = true;
            if (owner != null && !owner.IsDisposed && owner.Enabled) { owner.Enabled = false; ownerDisabled = true; }
            if (!Visible) { if (owner != null && !owner.IsDisposed) Show(owner); else Show(); }
            Activate();
        }
        private void DownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            try
            {
                Uri uri;
                if (disposed || completion.Task.IsCompleted || download != null || !Uri.TryCreate(e.DownloadOperation.Uri, UriKind.Absolute, out uri) || !allowed(uri))
                { e.Cancel = true; return; }
                long maximum = package == null ? 256 * 1024 : package.Size;
                if (e.DownloadOperation.TotalBytesToReceive.HasValue && e.DownloadOperation.TotalBytesToReceive.Value > (ulong)maximum) { e.Cancel = true; Fail(new InvalidDataException("Gitee 下载数据超过允许大小。")); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                e.ResultFilePath = destination; e.Handled = true; download = e.DownloadOperation;
                retry.Enabled = false; status.Text = "验证已通过，正在下载并校验更新数据…";
                download.BytesReceivedChanged += delegate
                {
                    if (disposed || completion.Task.IsCompleted) return;
                    if (download.BytesReceived > maximum) { download.Cancel(); Fail(new InvalidDataException("Gitee 下载数据超过允许大小。")); return; }
                    if (progress != null) progress.Report((int)Math.Min(100, download.BytesReceived * 100 / maximum));
                };
                download.StateChanged += DownloadStateChanged;
                DownloadStateChanged(download, EventArgs.Empty);
            }
            catch (Exception error) { e.Cancel = true; Fail(error); }
        }
        private void DownloadStateChanged(object sender, object e)
        {
            if (disposed || completion.Task.IsCompleted) return;
            if (download.State == CoreWebView2DownloadState.Completed)
            {
                try
                {
                    if (package == null) { Metadata = File.ReadAllBytes(destination); UpdateService.DecodeIndex(Metadata); }
                    completion.TrySetResult(true);
                }
                catch (Exception error) { Fail(error); }
            }
            else if (download.State == CoreWebView2DownloadState.Interrupted)
                Fail(new IOException("Gitee 下载中断：" + download.InterruptReason));
        }
        private void Fail(Exception error) { completion.TrySetException(error); }
        private void RestoreOwner()
        { if (ownerDisabled && owner != null && !owner.IsDisposed) owner.Enabled = true; ownerDisabled = false; }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true; completion.TrySetCanceled(); RestoreOwner();
                if (download != null) { download.StateChanged -= DownloadStateChanged; if (download.State == CoreWebView2DownloadState.InProgress) download.Cancel(); }
                browser.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
