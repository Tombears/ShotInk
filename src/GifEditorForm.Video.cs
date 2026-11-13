using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
using NAudio.Wave;

namespace ShotInk
{
    internal sealed partial class GifEditorForm
    {
        private readonly List<AudioFileReader> videoReaders = new List<AudioFileReader>();
        private readonly List<WaveOutEvent> videoPlayers = new List<WaveOutEvent>();
        private int nextAudioTime = -1;
        private void PlayVideoAudio(int index)
        {
            if (clip.Recording == null) return;
            try
            {
                if (videoReaders.Count == 0)
                    foreach (string path in clip.Recording.AudioPaths)
                    {
                        var reader = new AudioFileReader(path); videoReaders.Add(reader);
                        var player = new WaveOutEvent(); videoPlayers.Add(player); player.Init(reader);
                    }
                var target = TimeSpan.FromMilliseconds(clip.Frames[index].SourceTime);
                for (int track = 0; track < videoReaders.Count; track++)
                {
                    if (videoPlayers[track].PlaybackState != PlaybackState.Playing || Math.Abs(nextAudioTime - clip.Frames[index].SourceTime) > 3 || Math.Abs((videoReaders[track].CurrentTime - target).TotalMilliseconds) > 350)
                        videoReaders[track].CurrentTime = target;
                    videoPlayers[track].Play();
                }
                nextAudioTime = clip.Frames[index].SourceTime + clip.Frames[index].SourceDuration;
            }
            catch (Exception error) { StopVideoAudio(true); SetStatus("音频预览未完成。" + error.Message); }
        }
        private void StopVideoAudio(bool release)
        {
            foreach (var player in videoPlayers) { player.Pause(); if (release) player.Dispose(); }
            if (release) { foreach (var reader in videoReaders) reader.Dispose(); videoPlayers.Clear(); videoReaders.Clear(); }
        }
        private async Task ExportVideoAsync(bool copying, int first, int last)
        {
            string destination = null;
            if (!copying)
            {
                string name = "录屏_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".mp4";
                if (settings.AskSaveLocation)
                {
                    using (var dialog = new SaveFileDialog { Filter = "MP4 视频 (*.mp4)|*.mp4", FileName = name, DefaultExt = "mp4", AddExtension = true, InitialDirectory = settings.SaveDirectory, RestoreDirectory = true })
                    { if (dialog.ShowDialog(this) != DialogResult.OK) { SetStatus("已取消保存。"); return; } destination = dialog.FileName; }
                }
                else { Directory.CreateDirectory(settings.SaveDirectory); destination = Path.Combine(settings.SaveDirectory, name); }
            }
            string path = await Task.Run(delegate { return VideoExport.Encode(clip, first, last, generation.Token); });
            if (IsDisposed) return;
            if (copying)
            {
                string cached = Path.Combine(GifExport.CacheDirectory, "录屏_" + Guid.NewGuid().ToString("N") + ".mp4");
                File.Copy(path, cached);
                var data = new DataObject(); data.SetFileDropList(new StringCollection { cached });
                Clipboard.SetDataObject(data, true, 5, 100); SetStatus("MP4 已复制，可粘贴到支持视频文件的应用。");
            }
            else { File.Copy(path, destination, true); SetStatus("MP4 已保存。"); }
        }
    }
}
