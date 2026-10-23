using System;
using Microsoft.Win32;

namespace ShotInk
{
    internal sealed class StartupRegistration
    {
        private readonly RegistryKey root;
        private readonly string keyPath;
        private readonly string command;
        internal StartupRegistration(string executable) : this(Registry.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Run", executable) { }
        internal StartupRegistration(RegistryKey root, string keyPath, string executable)
        {
            this.root = root; this.keyPath = keyPath;
            command = "\"" + executable + "\" --startup";
        }
        internal bool IsEnabled { get { return !string.IsNullOrEmpty(ReadCommand()); } }
        private string ReadCommand()
        { using (var key = root.OpenSubKey(keyPath)) return key == null ? null : key.GetValue("ShotInk") as string; }
        private void WriteCommand(string value)
        {
            if (value == null)
            { using (var key = root.OpenSubKey(keyPath, true)) if (key != null) key.DeleteValue("ShotInk", false); }
            else using (var key = root.CreateSubKey(keyPath)) key.SetValue("ShotInk", value, RegistryValueKind.String);
        }
        internal void Apply(bool enabled, Action persist)
        {
            string previous = ReadCommand(), next = enabled ? command : null;
            bool changed = !string.Equals(previous, next, StringComparison.Ordinal);
            if (changed) WriteCommand(next);
            try { persist(); }
            catch (Exception error)
            {
                if (changed)
                {
                    try { WriteCommand(previous); }
                    catch (Exception rollback) { throw new InvalidOperationException("设置保存失败，开机启动状态恢复失败：" + rollback.Message, error); }
                }
                throw;
            }
        }
    }
}
