using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class ShortcutBox : TextBox
    {
        private Keys value;
        internal Keys ShortcutValue { get { return value; } set { this.value = value; Text = Shortcut.Display(value); } }
        internal ShortcutBox() { ReadOnly = true; BackColor = Color.White; }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete) ShortcutValue = Keys.None;
            else if (e.KeyCode != Keys.ControlKey && e.KeyCode != Keys.ShiftKey && e.KeyCode != Keys.Menu && e.KeyCode != Keys.LWin && e.KeyCode != Keys.RWin)
                ShortcutValue = e.KeyData;
            base.OnKeyDown(e);
        }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings original;
        private readonly ShortcutBox capture;
        private readonly ShortcutBox pin;
        private readonly ShortcutBox scroll;
        private readonly TextBox folder;
        private readonly CheckBox ask;
        private readonly CheckBox startup;
        private readonly CheckBox checkUpdates;
        private readonly Label error;
        private readonly Label[] shortcutStatus = new Label[3];
        private readonly Timer statusTimer;
        private readonly Func<Keys, bool> shortcutAvailable;
        private readonly UpdateController updates;
        private readonly Label updateStatus;
        private readonly Button updateCheck, updateDetails;
        internal SettingsForm(AppSettings current, Func<AppSettings, string> apply, Func<Keys, bool> shortcutAvailable = null, UpdateController updates = null, Action showUpdateDetails = null, bool checkVersion = false)
        {
            this.updates = updates;
            this.shortcutAvailable = shortcutAvailable ?? delegate(Keys keys) { return Native.ShortcutAvailable(Handle, keys); };
            original = current; Text = "设置 · ShotInk"; AutoScaleMode = AutoScaleMode.Dpi;
            MainForm.UseAppIcon(this);
            Font = new Font("Microsoft YaHei UI", 9); ClientSize = new Size(650, 616); MinimumSize = new Size(640, 644);
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false; FormBorderStyle = FormBorderStyle.FixedDialog;
            BackColor = Color.White;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 3, RowCount = 14 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            foreach (int height in new[] { 38, 38, 38, 35, 42, 34, 35, 34, 36, 32, 36, 34, 42, 50 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            capture = AddShortcut(layout, "区域截图", 0, current.CaptureShortcut);
            pin = AddShortcut(layout, "剪贴板贴图", 1, current.PinShortcut);
            scroll = AddShortcut(layout, "滚动长截图", 2, current.ScrollShortcut);
            var hint = new Label { Text = "点击输入框后按快捷键；Backspace 清除。占用状态每秒刷新。", Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
            layout.Controls.Add(hint, 0, 3); layout.SetColumnSpan(hint, 3);
            layout.Controls.Add(new Label { Text = "默认保存文件夹", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
            folder = new TextBox { Name = "save-directory", Dock = DockStyle.Fill, Text = current.SaveDirectory, Margin = new Padding(3, 8, 3, 3) }; layout.Controls.Add(folder, 1, 4);
            var browse = new Button { Text = "浏览…", Dock = DockStyle.Fill, Margin = new Padding(6, 5, 0, 5) };
            browse.Click += delegate { using (var dialog = new FolderBrowserDialog { Description = "选择图片默认保存文件夹", SelectedPath = folder.Text }) if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath; };
            layout.Controls.Add(browse, 2, 4);
            ask = new CheckBox { Name = "ask-save-location", Text = "每次保存时询问保存位置", Checked = current.AskSaveLocation, Dock = DockStyle.Fill };
            layout.Controls.Add(ask, 0, 5); layout.SetColumnSpan(ask, 3);
            var description = new Label { Text = "取消勾选后，保存按钮和 Ctrl+S 直接保存到上方文件夹。", Dock = DockStyle.Fill, ForeColor = Theme.Muted };
            layout.Controls.Add(description, 0, 6); layout.SetColumnSpan(description, 3);
            startup = new CheckBox { Name = "start-with-windows", Text = "开机启动", Checked = current.StartWithWindows, Dock = DockStyle.Fill };
            layout.Controls.Add(startup, 0, 7); layout.SetColumnSpan(startup, 3);
            var startupHint = new Label { Text = "登录 Windows 后自动启动并驻留托盘。移动程序后，请重新保存此设置。", Dock = DockStyle.Fill, ForeColor = Theme.Muted };
            layout.Controls.Add(startupHint, 0, 8); layout.SetColumnSpan(startupHint, 3);
            checkUpdates = new CheckBox { Name = "check-updates", Text = "每小时自动检查更新（Gitee）", Checked = current.CheckUpdates, Dock = DockStyle.Fill };
            layout.Controls.Add(checkUpdates, 0, 9); layout.SetColumnSpan(checkUpdates, 3);
            var version = new Label { Text = "当前版本 " + Application.ProductVersion, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            layout.Controls.Add(version, 0, 10); layout.SetColumnSpan(version, 2);
            updateCheck = new Button { Name = "check-version", Text = "检查更新", Dock = DockStyle.Fill, Enabled = updates != null };
            updateCheck.Click += async delegate { if (updates != null) await updates.CheckAsync(); }; layout.Controls.Add(updateCheck, 2, 10);
            updateStatus = new Label { Name = "update-status", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            layout.Controls.Add(updateStatus, 0, 11); layout.SetColumnSpan(updateStatus, 2);
            updateDetails = new Button { Name = "view-update", Text = "查看并更新", Dock = DockStyle.Fill, Enabled = false };
            updateDetails.Click += delegate { if (showUpdateDetails != null) showUpdateDetails(); }; layout.Controls.Add(updateDetails, 2, 11);
            error = new Label { Dock = DockStyle.Fill, ForeColor = Color.Firebrick, Text = current.LoadWarning ?? "" }; layout.Controls.Add(error, 0, 12); layout.SetColumnSpan(error, 3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var save = new Button { Text = "保存", Width = 85, Height = 30 };
            save.Click += delegate
            {
                try { var candidate = ReadSettings(); string message = apply(candidate); if (message != null) { error.Text = message; return; } DialogResult = DialogResult.OK; Close(); }
                catch (Exception ex) { error.Text = ex.Message; }
            };
            var cancel = new Button { Text = "取消", Width = 85, Height = 30, DialogResult = DialogResult.Cancel };
            var reset = new Button { Text = "恢复默认快捷键", Width = 130, Height = 30 };
            reset.Click += delegate { var defaults = new AppSettings(); capture.ShortcutValue = defaults.CaptureShortcut; pin.ShortcutValue = defaults.PinShortcut; scroll.ShortcutValue = defaults.ScrollShortcut; };
            buttons.Controls.Add(save); buttons.Controls.Add(cancel); buttons.Controls.Add(reset);
            layout.Controls.Add(buttons, 0, 13); layout.SetColumnSpan(buttons, 3);
            Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
            statusTimer = new Timer { Interval = 1000 };
            statusTimer.Tick += delegate { RefreshShortcutStatus(); };
            foreach (var box in new[] { capture, pin, scroll })
                box.TextChanged += delegate { if (IsHandleCreated) RefreshShortcutStatus(); };
            if (updates != null) updates.Changed += RefreshUpdateStatus;
            RefreshUpdateStatus();
            if (checkVersion) Shown += async delegate { if (updates != null) await updates.CheckAsync(); };
        }
        private void RefreshUpdateStatus()
        {
            if (IsDisposed) return;
            updateStatus.Text = updates == null ? "尚未检查更新。" : updates.Status;
            updateStatus.ForeColor = updates != null && updates.Available != null ? Color.ForestGreen : ToolAppearance.Muted;
            updateDetails.Text = updates != null && updates.NeedsVerification ? "完成验证" : "查看并更新";
            updateDetails.Enabled = updates != null && (updates.Available != null || updates.NeedsVerification);
            updateCheck.Enabled = updates != null && !updates.Checking;
        }
        protected override void OnShown(EventArgs e)
        { base.OnShown(e); RefreshShortcutStatus(); statusTimer.Start(); }
        internal void RefreshShortcutStatus()
        {
            var keys = new[] { capture.ShortcutValue, pin.ShortcutValue, scroll.ShortcutValue };
            for (int i = 0; i < keys.Length; i++)
            {
                string state; Color color = Theme.Muted;
                if (keys[i] == Keys.None) state = "未设置";
                else if (!Shortcut.Valid(keys[i])) { state = "无效快捷键"; color = Color.Firebrick; }
                else if (Array.FindAll(keys, delegate(Keys key) { return key == keys[i]; }).Length > 1)
                { state = "本页重复"; color = Color.Firebrick; }
                else
                {
                    try
                    {
                        bool available = shortcutAvailable(keys[i]);
                        state = available ? "未占用" : "已被占用";
                        color = available ? Color.ForestGreen : Color.Firebrick;
                    }
                    catch (System.ComponentModel.Win32Exception) { state = "检测失败"; color = Color.Firebrick; }
                }
                shortcutStatus[i].Text = state; shortcutStatus[i].ForeColor = color;
            }
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { if (statusTimer != null) { statusTimer.Stop(); statusTimer.Dispose(); } if (updates != null) updates.Changed -= RefreshUpdateStatus; } base.Dispose(disposing); }
        private ShortcutBox AddShortcut(TableLayoutPanel layout, string title, int row, Keys value)
        {
            layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            var box = new ShortcutBox { Name = "hotkey-" + row, ShortcutValue = value, Dock = DockStyle.Fill, Margin = new Padding(3, 6, 3, 3) };
            layout.Controls.Add(box, 1, row);
            shortcutStatus[row] = new Label { Name = "hotkey-status-" + row, Text = "待检测", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(10, 0, 0, 0) };
            layout.Controls.Add(shortcutStatus[row], 2, row); return box;
        }
        internal AppSettings ReadSettings()
        {
            var candidate = original.Clone(); candidate.CaptureShortcut = capture.ShortcutValue; candidate.PinShortcut = pin.ShortcutValue; candidate.ScrollShortcut = scroll.ShortcutValue;
            candidate.SaveDirectory = folder.Text.Trim(); candidate.AskSaveLocation = ask.Checked; candidate.StartWithWindows = startup.Checked; candidate.CheckUpdates = checkUpdates.Checked; candidate.Validate(); return candidate;
        }
    }
}
