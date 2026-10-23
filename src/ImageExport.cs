using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace ShotInk
{
    internal static class ImageExport
    {
        internal static bool Save(IWin32Window owner, Bitmap image, AppSettings settings, out string path)
        {
            settings.Validate(); path = null;
            if (!settings.AskSaveLocation) { path = SaveToDirectory(image, settings.SaveDirectory); return true; }
            using (var dialog = new SaveFileDialog { Filter = "PNG 图片 (*.png)|*.png", FileName = Name(), DefaultExt = "png", AddExtension = true,
                RestoreDirectory = true, InitialDirectory = settings.SaveDirectory, OverwritePrompt = true })
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
                image.Save(dialog.FileName, ImageFormat.Png); path = dialog.FileName; return true;
            }
        }
        private static string Name() { return "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png"; }
        internal static string SaveToDirectory(Bitmap image, string directory)
        {
            Directory.CreateDirectory(directory); string stem = Path.GetFileNameWithoutExtension(Name());
            for (int i = 0; ; i++)
            {
                string path = Path.Combine(directory, stem + (i == 0 ? "" : "_" + i) + ".png");
                FileStream stream;
                try { stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
                catch (IOException) { if (File.Exists(path)) continue; throw; }
                try { using (stream) image.Save(stream, ImageFormat.Png); return path; }
                catch { try { File.Delete(path); } catch (IOException) { } throw; }
            }
        }
    }
}
