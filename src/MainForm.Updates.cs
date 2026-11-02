using System;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed partial class MainForm
    {
        private UpdateController updates;
        private void SetupUpdateCheck()
        {
            updates = new UpdateController(delegate { return settings.CheckUpdates; }, Application.ProductVersion, UpdateService.InstalledRevision(AppDomain.CurrentDomain.BaseDirectory));
            updates.Start();
        }
        private async void ShowUpdates()
        {
            if (settingsOpen) { if (updates != null) await updates.CheckAsync(); return; }
            OpenSettingsCore(true);
        }
        private void DisposeUpdates()
        {
            if (updates != null) updates.Dispose();
        }
    }
}
