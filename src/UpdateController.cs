using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class UpdateController : IDisposable
    {
        internal const int Hour = 60 * 60 * 1000;
        private readonly System.Windows.Forms.Timer timer;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Func<bool> autoEnabled;
        private readonly Func<CancellationToken, Task<UpdateInfo>> check;
        private readonly string installedVersion, installedRevision;
        private bool disposed;
        internal event Action Changed;
        internal string Status { get; private set; }
        internal bool Checking { get; private set; }
        internal UpdateInfo Available { get; private set; }
        internal UpdateController(Func<bool> autoEnabled, string installedVersion, string installedRevision, Func<CancellationToken, Task<UpdateInfo>> check = null)
        {
            this.autoEnabled = autoEnabled; this.installedVersion = installedVersion; this.installedRevision = installedRevision;
            this.check = check ?? UpdateService.CheckAsync; Status = "尚未检查更新。";
            timer = new System.Windows.Forms.Timer { Interval = 10000 };
            timer.Tick += async delegate
            {
                timer.Stop();
                try { if (autoEnabled()) await CheckAsync(); }
                finally { if (!disposed) { timer.Interval = Hour; timer.Start(); } }
            };
        }
        internal void Start() { timer.Start(); }
        internal async Task CheckAsync()
        {
            if (disposed || Checking) return;
            Checking = true; Available = null; Status = "正在从 Gitee 检查更新…"; Notify();
            try
            {
                var info = await check(cancellation.Token);
                if (disposed) return;
                Available = info.IsNewer(installedVersion, installedRevision) ? info : null;
                Status = Available == null ? "当前已是最新版本。" : info.Version == installedVersion ? "当前版本有修复更新。" : "发现新版本 " + info.Version;
            }
            catch (Exception error) { if (!disposed) Status = "检查失败：" + error.Message; }
            finally { Checking = false; if (!disposed) Notify(); }
        }
        private void Notify() { if (Changed != null) Changed(); }
        public void Dispose()
        { if (disposed) return; disposed = true; timer.Dispose(); cancellation.Cancel(); cancellation.Dispose(); }
    }
}
