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
        private readonly ComboBox skin, microphoneDevice;
        private readonly NumericUpDown microphoneGain;
        private readonly SkinKind previousSkin;
        private bool skinCommitted;
        private readonly ShortcutBox capture;
        private readonly ShortcutBox pin;
        private readonly ShortcutBox scroll, quickCopy, quickEdit, fullScreen;
        private readonly ShortcutBox gifDeleteCurrent, gifDeleteSelected, gifUndo;
        private readonly TextBox folder;
        private readonly CheckBox ask;
        private readonly CheckBox startup;
        private readonly NumericUpDown gifFps;
        private readonly Label error;
        private readonly Label[] shortcutStatus = new Label[6];
        private readonly Timer statusTimer;
        private readonly Func<Keys, bool> shortcutAvailable;
        private readonly UpdateController updates;
        private readonly Label updateStatus;
        private readonly Button updateCheck, updateDetails;
        internal SettingsForm(AppSettings current, Func<AppSettings, string> apply, Func<Keys, bool> shortcutAvailable = null, UpdateController updates = null, Action showUpdateDetails = null, bool checkVersion = false)
        {
            this.updates = updates;
            this.shortcutAvailable = shortcutAvailable ?? delegate(Keys keys) { return Native.ShortcutAvailable(Handle, keys); };
            previousSkin = Skins.Current.Kind;
            original = current; Text = "设置 · ShotInk"; AutoScaleMode = AutoScaleMode.Dpi;
            MainForm.UseAppIcon(this);
            Font = new Font("Microsoft YaHei UI", 9); ClientSize = new Size(680, 698); MinimumSize = new Size(690, 726);
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false; FormBorderStyle = FormBorderStyle.FixedDialog;
            BackColor = Color.White;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 3, RowCount = 17 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            foreach (int height in new[] { 38, 38, 38, 38, 38, 38, 35, 42, 34, 35, 34, 36, 0, 36, 34, 0, 0 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            capture = AddShortcut(layout, "区域截图", 0, current.CaptureShortcut);
            pin = AddShortcut(layout, "剪贴板贴图", 1, current.PinShortcut);
            scroll = AddShortcut(layout, "滚动长截图", 2, current.ScrollShortcut);
            quickCopy = AddShortcut(layout, "快速截图并复制", 3, current.QuickCopyShortcut);
            quickEdit = AddShortcut(layout, "快速截图并编辑", 4, current.QuickEditShortcut);
            fullScreen = AddShortcut(layout, "全屏截图", 5, current.FullScreenShortcut);
            var hint = new Label { Text = "点击输入框后按快捷键；Backspace 清除。占用状态每秒刷新。", Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
            layout.Controls.Add(hint, 0, 6); layout.SetColumnSpan(hint, 3);
            layout.Controls.Add(new Label { Text = "默认保存文件夹", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 7);
            folder = new TextBox { Name = "save-directory", Dock = DockStyle.Fill, Text = current.SaveDirectory, Margin = new Padding(3, 8, 3, 3) }; layout.Controls.Add(new RoundedInputFrame(folder, ToolAppearance.Accent("shape")), 1, 7);
            var browse = new SkinButton { Text = "浏览…", Dock = DockStyle.Fill, Margin = new Padding(6, 5, 0, 5) };
            browse.Click += delegate { using (var dialog = new FolderBrowserDialog { Description = "选择图片默认保存文件夹", SelectedPath = folder.Text }) if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath; };
            layout.Controls.Add(browse, 2, 7);
            ask = new CheckBox { Name = "ask-save-location", Text = "每次保存时询问保存位置", Checked = current.AskSaveLocation, Dock = DockStyle.Fill };
            layout.Controls.Add(ask, 0, 8); layout.SetColumnSpan(ask, 3);
            var description = new Label { Text = "取消勾选后，保存按钮和 Ctrl+S 直接保存到上方文件夹。", Dock = DockStyle.Fill, ForeColor = Theme.Muted };
            layout.Controls.Add(description, 0, 9); layout.SetColumnSpan(description, 3);
            startup = new CheckBox { Name = "start-with-windows", Text = "开机启动", Checked = current.StartWithWindows, Dock = DockStyle.Fill };
            layout.Controls.Add(startup, 0, 10); layout.SetColumnSpan(startup, 3);
            var startupHint = new Label { Text = "登录 Windows 后自动启动并驻留托盘。移动程序后，请重新保存此设置。", Dock = DockStyle.Fill, ForeColor = Theme.Muted };
            layout.Controls.Add(startupHint, 0, 11); layout.SetColumnSpan(startupHint, 3);
            var gifLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 2, RowCount = 5 };
            gifLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); gifLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 42, 42, 42, 42, 60 }) gifLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            gifLayout.Controls.Add(new Label { Text = "默认每秒帧数（1～50）", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            gifFps = new NumericUpDown { Name = "gif-fps", Minimum = 1, Maximum = 50, Value = current.GifFramesPerSecond, Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 3) };
            gifLayout.Controls.Add(new RoundedInputFrame(gifFps, ToolAppearance.Accent("gif")), 1, 0);
            gifDeleteCurrent = AddGifShortcut(gifLayout, "删除当前帧", "gif-delete-current", 1, current.GifDeleteCurrentShortcut);
            gifDeleteSelected = AddGifShortcut(gifLayout, "删除选中帧", "gif-delete-selected", 2, current.GifDeleteSelectedShortcut);
            gifUndo = AddGifShortcut(gifLayout, "撤销操作", "gif-undo", 3, current.GifUndoShortcut);
            var gifHint = new Label { Text = "快捷键在 GIF 剪辑窗口内生效。点击输入框后按快捷键，Backspace 清除。编辑文字时沿用文字的剪切与撤销。", Dock = DockStyle.Fill, ForeColor = Theme.Muted };
            gifLayout.Controls.Add(gifHint, 0, 4); gifLayout.SetColumnSpan(gifHint, 2);
            var version = new Label { Text = "当前版本 " + Application.ProductVersion + (BuildFeatures.Ocr ? " · 完整版" : " · 轻量版"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            layout.Controls.Add(version, 0, 13); layout.SetColumnSpan(version, 2);
            updateCheck = new SkinButton { Name = "check-version", Text = "检查更新", Dock = DockStyle.Fill, Enabled = updates != null };
            updateCheck.Click += async delegate { if (updates != null) await updates.CheckAsync(); }; layout.Controls.Add(updateCheck, 2, 13);
            updateStatus = new Label { Name = "update-status", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            layout.Controls.Add(updateStatus, 0, 14); layout.SetColumnSpan(updateStatus, 2);
            updateDetails = new SkinButton { Name = "view-update", Text = "查看并更新", Dock = DockStyle.Fill, Enabled = false };
            updateDetails.Click += delegate { if (showUpdateDetails != null) showUpdateDetails(); }; layout.Controls.Add(updateDetails, 2, 14);
            error = new Label { Dock = DockStyle.Fill, Padding = new Padding(22, 0, 22, 0), ForeColor = Color.Firebrick, Text = current.LoadWarning ?? "" };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(12, 0, 12, 0), FlowDirection = FlowDirection.RightToLeft };
            var save = new SkinButton { Text = "保存", Width = 85, Height = 30 };
            save.Click += delegate
            {
                try { var candidate = ReadSettings(); string message = apply(candidate); if (message != null) { error.Text = message; return; } skinCommitted = true; DialogResult = DialogResult.OK; Close(); }
                catch (Exception ex) { error.Text = ex.Message; }
            };
            var cancel = new SkinButton { Text = "取消", Width = 85, Height = 30, DialogResult = DialogResult.Cancel };
            var reset = new SkinButton { Text = "恢复默认快捷键", Width = 130, Height = 30 };
            reset.Click += delegate { var defaults = new AppSettings(); capture.ShortcutValue = defaults.CaptureShortcut; pin.ShortcutValue = defaults.PinShortcut; scroll.ShortcutValue = defaults.ScrollShortcut; quickCopy.ShortcutValue = defaults.QuickCopyShortcut; quickEdit.ShortcutValue = defaults.QuickEditShortcut; fullScreen.ShortcutValue = defaults.FullScreenShortcut; gifDeleteCurrent.ShortcutValue = defaults.GifDeleteCurrentShortcut; gifDeleteSelected.ShortcutValue = defaults.GifDeleteSelectedShortcut; gifUndo.ShortcutValue = defaults.GifUndoShortcut; };
            buttons.Controls.Add(save); buttons.Controls.Add(cancel); buttons.Controls.Add(reset);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var generalTab = new TabPage("常规") { BackColor = Color.White }; generalTab.Controls.Add(layout);
            var gifTab = new TabPage("GIF") { BackColor = Color.White }; gifTab.Controls.Add(gifLayout);
            tabs.TabPages.Add(generalTab); tabs.TabPages.Add(gifTab);
            var appearance = new TabPage("外观") { Name = "appearance-tab", Padding = new Padding(22), AutoScroll = true };
            var skinLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            skinLayout.Controls.Add(new Label { Text = "界面皮肤", AutoSize = true, Margin = new Padding(0, 4, 0, 8) });
            skin = new ComboBox { Name = "skin-selector", AccessibleName = "界面皮肤", DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Margin = new Padding(0, 0, 0, 12) };
            foreach (var palette in Skins.All) skin.Items.Add(palette.Name);
            skin.SelectedIndex = (int)current.Skin;
            skinLayout.Controls.Add(skin);
            skinLayout.Controls.Add(new Label { Text = "将鼠标移入或按下按钮，体验当前皮肤的反馈。", AutoSize = true, Margin = new Padding(0, 8, 0, 12) });
            var preview = new Panel { Name = "skin-preview", Size = new Size(570, 160), Margin = new Padding(0, 0, 0, 16) };
            var previewTools = new CaptureToolbar(current.Clone()) { Location = new Point(8, 12) };
            previewTools.SelectTool(DrawTool.Pen); preview.Controls.Add(previewTools);
            var sample = new SkinButton { Name = "skin-preview-button", Text = "预览互动反馈", Primary = true, Bounds = new Rectangle(8, 100, 160, 36) };
            preview.Controls.Add(sample); skinLayout.Controls.Add(preview);
            appearance.Controls.Add(skinLayout); tabs.TabPages.Add(appearance);
            var recordingTab = new TabPage("录屏") { Padding = new Padding(22), AutoScroll = true };
            var audioLayout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 180, ColumnCount = 2, RowCount = 3 };
            audioLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); audioLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            audioLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); audioLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); audioLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            audioLayout.Controls.Add(new Label { Text = "麦克风设备", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            microphoneDevice = new ComboBox { Name = "record-microphone-device", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            foreach (var device in RecordingInputs.List(current.RecordMicrophoneDevice)) microphoneDevice.Items.Add(device);
            microphoneDevice.SelectedIndex = 0;
            for (int index = 0; index < microphoneDevice.Items.Count; index++) if (((RecordingInput)microphoneDevice.Items[index]).Id == current.RecordMicrophoneDevice) microphoneDevice.SelectedIndex = index;
            audioLayout.Controls.Add(microphoneDevice, 1, 0);
            audioLayout.Controls.Add(new Label { Text = "麦克风增益（%）", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            microphoneGain = new NumericUpDown { Name = "record-microphone-gain", Minimum = 100, Maximum = 400, Increment = 25, Value = current.RecordMicrophoneGain, Dock = DockStyle.Fill };
            audioLayout.Controls.Add(new RoundedInputFrame(microphoneGain, Skins.Current.Accent), 1, 1);
            var audioHint = new Label { Text = "录屏工具栏开启麦克风后生效。默认增益 200%，录制结束后自动平衡轻声并限制峰值。系统声音单独采集。", Dock = DockStyle.Fill, ForeColor = Theme.Muted, Padding = new Padding(0, 8, 0, 0) };
            audioLayout.Controls.Add(audioHint, 0, 2); audioLayout.SetColumnSpan(audioHint, 2);
            recordingTab.Controls.Add(audioLayout); tabs.TabPages.Add(recordingTab);
            skin.SelectedIndexChanged += delegate
            {
                Skins.Select((SkinKind)skin.SelectedIndex); Skins.Apply(this);
            };
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 80 }; footer.Controls.Add(error); footer.Controls.Add(buttons);
            Controls.Add(tabs); Controls.Add(footer); AcceptButton = save; CancelButton = cancel;
            statusTimer = new Timer { Interval = 1000 };
            statusTimer.Tick += delegate { RefreshShortcutStatus(); };
            foreach (var box in new[] { capture, pin, scroll, quickCopy, quickEdit, fullScreen })
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
            var keys = new[] { capture.ShortcutValue, pin.ShortcutValue, scroll.ShortcutValue, quickCopy.ShortcutValue, quickEdit.ShortcutValue, fullScreen.ShortcutValue };
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
                shortcutStatus[i].Text = state; shortcutStatus[i].ForeColor = Skins.Current.Kind == SkinKind.Ink ? Skins.Current.Ink : color;
            }
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { if (!skinCommitted) Skins.Select(previousSkin); if (statusTimer != null) { statusTimer.Stop(); statusTimer.Dispose(); } if (updates != null) updates.Changed -= RefreshUpdateStatus; } base.Dispose(disposing); }
        private ShortcutBox AddShortcut(TableLayoutPanel layout, string title, int row, Keys value)
        {
            layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            var box = new ShortcutBox { Name = "hotkey-" + row, ShortcutValue = value, Dock = DockStyle.Fill, Margin = new Padding(3, 6, 3, 3) };
            layout.Controls.Add(new RoundedInputFrame(box, ToolAppearance.Accent("shape")), 1, row);
            shortcutStatus[row] = new Label { Name = "hotkey-status-" + row, Text = "待检测", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(10, 0, 0, 0) };
            layout.Controls.Add(shortcutStatus[row], 2, row); return box;
        }
        private static ShortcutBox AddGifShortcut(TableLayoutPanel layout, string title, string name, int row, Keys value)
        {
            layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            var box = new ShortcutBox { Name = name, ShortcutValue = value, Dock = DockStyle.Fill, Margin = new Padding(3, 6, 3, 3) };
            layout.Controls.Add(new RoundedInputFrame(box, ToolAppearance.Accent("gif")), 1, row); return box;
        }
        internal AppSettings ReadSettings()
        {
            var candidate = original.Clone(); candidate.CaptureShortcut = capture.ShortcutValue; candidate.PinShortcut = pin.ShortcutValue; candidate.ScrollShortcut = scroll.ShortcutValue;
            candidate.QuickCopyShortcut = quickCopy.ShortcutValue; candidate.QuickEditShortcut = quickEdit.ShortcutValue; candidate.FullScreenShortcut = fullScreen.ShortcutValue;
            candidate.RecordMicrophoneDevice = ((RecordingInput)microphoneDevice.SelectedItem).Id; candidate.RecordMicrophoneGain = (int)microphoneGain.Value;
            candidate.Skin = (SkinKind)skin.SelectedIndex;
            candidate.GifFramesPerSecond = (int)gifFps.Value;
            candidate.GifDeleteCurrentShortcut = gifDeleteCurrent.ShortcutValue; candidate.GifDeleteSelectedShortcut = gifDeleteSelected.ShortcutValue; candidate.GifUndoShortcut = gifUndo.ShortcutValue;
            candidate.SaveDirectory = folder.Text.Trim(); candidate.AskSaveLocation = ask.Checked; candidate.StartWithWindows = startup.Checked; candidate.Validate(); return candidate;
        }
    }
}
