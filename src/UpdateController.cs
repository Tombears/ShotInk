using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShotInk
{
    internal sealed class UpdateController : IDisposable
    {
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Func<CancellationToken, Task<UpdateInfo>> check;
        private readonly string installedVersion, installedRevision;
        private bool disposed;
        internal event Action Changed;
        internal string Status { get; private set; }
        internal bool Checking { get; private set; }
        internal bool NeedsVerification { get; private set; }
        internal UpdateInfo Available { get; private set; }
        internal UpdateController(string installedVersion, string installedRevision, Func<CancellationToken, Task<UpdateInfo>> check = null)
        {
            this.installedVersion = installedVersion; this.installedRevision = installedRevision;
            this.check = check ?? UpdateService.CheckAsync; Status = "尚未检查更新。";
        }
        internal async Task CheckAsync(Func<CancellationToken, Task<UpdateInfo>> verifiedCheck = null)
        {
            if (disposed || Checking) return;
            Checking = true; Available = null; NeedsVerification = false; Status = "正在检查更新…"; Notify();
            try
            {
                var info = await (verifiedCheck ?? check)(cancellation.Token);
                if (disposed) return;
                Available = info.IsNewer(installedVersion, installedRevision) ? info : null;
                Status = Available == null ? "当前已是最新版本。" : info.Version == installedVersion ? "当前版本有修复更新。" : "发现新版本 " + info.Version;
            }
            catch (VerificationRequiredException) { if (!disposed) { NeedsVerification = true; Status = "需要人机验证，请点击“完成验证”。"; } }
            catch (Exception error) { if (!disposed) Status = "检查失败：" + error.Message.Replace("Gitee", "更新服务"); }
            finally { Checking = false; if (!disposed) Notify(); }
        }
        private void Notify() { if (Changed != null) Changed(); }
        public void Dispose()
        { if (disposed) return; disposed = true; cancellation.Cancel(); cancellation.Dispose(); }
    }
}
