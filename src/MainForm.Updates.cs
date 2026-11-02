using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed partial class MainForm
    {
        private UpdateController updates;
        private UpdateForm updateWindow;
        private void SetupUpdateCheck()
        {
            updates = new UpdateController(Application.ProductVersion, UpdateService.InstalledRevision(AppDomain.CurrentDomain.BaseDirectory));
        }
        private async void ShowUpdates()
        {
            if (settingsOpen) { if (updates != null) await updates.CheckAsync(); return; }
            OpenSettingsCore(true);
        }
        private async void ShowUpdateDetails()
        {
            if (updates != null && updates.NeedsVerification)
            {
                await updates.CheckAsync(delegate(CancellationToken token) { return UpdateService.CheckWithBrowserAsync(Form.ActiveForm ?? this, token); }); return;
            }
            if (updateWindow != null && !updateWindow.IsDisposed) { updateWindow.Activate(); return; }
            using (var dialog = new UpdateForm(InstallUpdate, updates == null ? null : updates.Available))
            {
                updateWindow = dialog;
                try { dialog.ShowDialog(Form.ActiveForm ?? this); }
                finally { updateWindow = null; }
            }
        }
        private async Task<string> InstallUpdate(PreparedUpdate prepared, CancellationToken token)
        {
            if (captureBusy || Application.OpenForms.Cast<Form>().Any(form => form is CaptureOverlay || form is EditorForm || form is GifEditorForm || form is PinForm))
                return "请先完成并关闭截图、贴图或剪辑窗口，再安装更新。";
            if (!settings.CanSave) return settings.LoadWarning;
            Process helper = null;
            try
            {
                var dialog = Application.OpenForms.Cast<Form>().OfType<SettingsForm>().FirstOrDefault();
                if (dialog != null) { string error = ApplySettings(dialog.ReadSettings()); if (error != null) return error; }
                PersistSettings(); if (settingsDirty) return "设置尚未保存，请先处理设置保存错误。";
                string probe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".shotink-update-" + Guid.NewGuid().ToString("N"));
                using (File.Create(probe)) { } File.Delete(probe);
                string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
                string ready = Path.Combine(prepared.Folder, "update-ready.txt"), failure = Path.Combine(prepared.Folder, "update-error.txt");
                File.Delete(ready); File.Delete(failure);
                var start = new ProcessStartInfo(powershell, "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + prepared.HelperPath + "\" -Plan \"" + prepared.PlanPath + "\"")
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = prepared.Folder };
                helper = Process.Start(start); if (helper == null) return "无法启动更新程序。";
                using (token.Register(delegate { try { if (!helper.HasExited) helper.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }))
                {
                    var clock = Stopwatch.StartNew();
                    while (!File.Exists(ready))
                    {
                        token.ThrowIfCancellationRequested();
                        if (File.Exists(failure)) throw new IOException(File.ReadAllText(failure));
                        if (helper.HasExited) throw new IOException("更新助手未能启动，请检查系统 PowerShell 是否可用。");
                        if (clock.Elapsed.TotalSeconds > 30) throw new IOException("更新助手准备超时，当前程序未关闭。");
                        await Task.Delay(100, token);
                    }
                    token.ThrowIfCancellationRequested();
                    BeginInvoke(new Action(delegate { quitting = true; Application.Exit(); })); return null;
                }
            }
            catch (Exception error)
            {
                if (helper != null) try { if (!helper.HasExited) helper.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                return error is OperationCanceledException ? "已取消更新。" : "无法开始更新：" + error.Message;
            }
            finally { if (helper != null) helper.Dispose(); }
        }
        private void DisposeUpdates()
        {
            if (updates != null) updates.Dispose();
        }
    }
}
