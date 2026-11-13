using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace ShotInk
{
    internal sealed class RecordingMedia : IDisposable
    {
        internal readonly string DirectoryPath;
        internal readonly List<string> AudioPaths = new List<string>();
        private readonly List<AudioTrack> tracks = new List<AudioTrack>();
        internal RecordingMedia()
        {
            DirectoryPath = Path.Combine(GifExport.CacheDirectory, "video-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }
        internal void StartAudio(bool microphone, bool speaker)
        {
            try
            {
                if (microphone)
                {
                    try { AddTrack(new WasapiCapture(), "microphone.wav"); }
                    catch (Exception error) { throw new InvalidOperationException("无法打开麦克风，请检查 Windows 默认输入设备及麦克风权限，或关闭麦克风开关后重试。", error); }
                }
                if (speaker)
                {
                    try { AddTrack(new WasapiLoopbackCapture(), "speaker.wav"); }
                    catch (Exception error) { throw new InvalidOperationException("无法打开系统声音，请检查 Windows 默认输出设备，或关闭扬声器开关后重试。", error); }
                }
            }
            catch { Dispose(); throw; }
        }
        private void AddTrack(IWaveIn device, string name)
        {
            string path = Path.Combine(DirectoryPath, name);
            var track = new AudioTrack(device, path);
            tracks.Add(track);
            track.Start();
            AudioPaths.Add(path);
        }
        internal async Task StopAudio()
        { foreach (var track in tracks) { await track.Stop(); track.Dispose(); } tracks.Clear(); }
        public void Dispose()
        {
            foreach (var track in tracks) track.Dispose(); tracks.Clear();
            try { if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        private sealed class AudioTrack : IDisposable
        {
            private readonly IWaveIn device;
            private readonly WaveFileWriter writer;
            private readonly Stopwatch clock = new Stopwatch();
            private readonly TaskCompletionSource<bool> stopped = new TaskCompletionSource<bool>();
            private readonly object gate = new object();
            private bool disposed, started;
            private Exception failure;
            internal AudioTrack(IWaveIn device, string path)
            {
                this.device = device;
                try { writer = new WaveFileWriter(path, device.WaveFormat); }
                catch { device.Dispose(); throw; }
                device.DataAvailable += delegate(object sender, WaveInEventArgs args)
                {
                    lock (gate)
                    {
                        if (disposed) return;
                        // Loopback emits no packets during silence. Preserve its wall-clock position.
                        long desired = Math.Max(0, clock.ElapsedMilliseconds * writer.WaveFormat.AverageBytesPerSecond / 1000 - args.BytesRecorded);
                        desired -= desired % writer.WaveFormat.BlockAlign;
                        long gap = desired - writer.Length;
                        if (gap > writer.WaveFormat.AverageBytesPerSecond / 10)
                        {
                            var silence = new byte[4096];
                            while (gap > 0) { int count = (int)Math.Min(silence.Length, gap); writer.Write(silence, 0, count); gap -= count; }
                        }
                        writer.Write(args.Buffer, 0, args.BytesRecorded);
                    }
                };
                device.RecordingStopped += delegate(object sender, StoppedEventArgs args)
                { failure = args.Exception; stopped.TrySetResult(true); };
            }
            internal void Start() { clock.Start(); device.StartRecording(); started = true; }
            internal async Task Stop()
            {
                if (started) { device.StopRecording(); await stopped.Task; }
                lock (gate) { if (!disposed) writer.Flush(); }
                if (failure != null) throw new IOException("音频录制中断。", failure);
            }
            public void Dispose()
            {
                if (disposed) return;
                device.Dispose(); lock (gate) { disposed = true; writer.Dispose(); }
            }
        }
    }

    internal static class VideoExport
    {
        internal static string EncoderPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recording", "ffmpeg.exe"); } }
        internal static async Task RunEncoder(string arguments, CancellationToken token)
        {
            var info = new ProcessStartInfo(EncoderPath, "-hide_banner -loglevel error -y " + arguments)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            using (var process = Process.Start(info))
            using (token.Register(delegate { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } }))
            {
                string error = await process.StandardError.ReadToEndAsync();
                await Task.Run(delegate { process.WaitForExit(); }); token.ThrowIfCancellationRequested();
                if (process.ExitCode != 0) throw new IOException("视频生成失败。" + error);
            }
        }
        internal static string Quote(string value) { return "\"" + value.Replace("\"", "") + "\""; }
        private static string Seconds(int value) { return (value / 1000.0).ToString("0.000", CultureInfo.InvariantCulture); }
        internal static async Task<string> Encode(GifClip clip, int first, int last, CancellationToken token)
        {
            string directory = Path.Combine(clip.Recording.DirectoryPath, "export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var list = new StringBuilder();
                for (int index = first; index <= last; index++)
                {
                    token.ThrowIfCancellationRequested();
                    string name = "frame-" + index + ".png";
                    using (var image = clip.Frames[index].Decode()) image.Save(Path.Combine(directory, name));
                    list.AppendLine("file '" + name + "'");
                    list.AppendLine("duration " + Seconds(clip.EndTime(index) - clip.Frames[index].Time));
                }
                list.AppendLine("file 'frame-" + last + ".png'");
                string manifest = Path.Combine(directory, "frames.txt"); File.WriteAllText(manifest, list.ToString(), new UTF8Encoding(false));
                var args = new StringBuilder("-f concat -safe 0 -i " + Quote(manifest));
                foreach (string audio in clip.Recording.AudioPaths) args.Append(" -i " + Quote(audio));
                if (clip.Recording.AudioPaths.Count > 0)
                {
                    var filter = new StringBuilder();
                    int count = clip.Recording.AudioPaths.Count;
                    for (int track = 0; track < count; track++)
                    {
                        int segment = 0;
                        for (int index = first; index <= last;)
                        {
                            int start = clip.Frames[index].SourceTime, end = start + clip.Frames[index].SourceDuration;
                            int duration = clip.EndTime(index) - clip.Frames[index].Time;
                            index++;
                            while (index <= last && Math.Abs(clip.Frames[index].SourceTime - end) <= 3)
                            { duration += clip.EndTime(index) - clip.Frames[index].Time; end = clip.Frames[index].SourceTime + clip.Frames[index].SourceDuration; index++; }
                            filter.AppendFormat(CultureInfo.InvariantCulture, "[{0}:a]atrim=start={1}:end={2},asetpts=PTS-STARTPTS,apad,atrim=duration={3}[t{4}s{5}];", track + 1, Seconds(start), Seconds(start + duration), Seconds(duration), track, segment++);
                        }
                        for (int part = 0; part < segment; part++) filter.AppendFormat("[t{0}s{1}]", track, part);
                        filter.AppendFormat("concat=n={0}:v=0:a=1[a{1}];", segment, track);
                    }
                    for (int track = 0; track < count; track++) filter.Append("[a" + track + "]");
                    filter.Append("amix=inputs=" + count + ":duration=longest:normalize=0[audio]");
                    string script = Path.Combine(directory, "audio.txt"); File.WriteAllText(script, filter.ToString(), new UTF8Encoding(false));
                    args.Append(" -/filter_complex " + Quote(script) + " -map 0:v -map [audio] -c:a aac -b:a 192k");
                }
                string output = Path.Combine(clip.Recording.DirectoryPath, "edited.mp4");
                args.Append(" -vf pad=ceil(iw/2)*2:ceil(ih/2)*2 -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -fps_mode vfr -t " + Seconds(clip.EndTime(last) - clip.Frames[first].Time) + " -movflags +faststart " + Quote(output));
                await RunEncoder(args.ToString(), token); return output;
            }
            finally { Directory.Delete(directory, true); }
        }
    }

    internal static class RecordingCursor
    {
        [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { internal int Size, Flags; internal IntPtr Handle; internal Point Position; }
        [StructLayout(LayoutKind.Sequential)] private struct IconInfo { internal bool Icon; internal int X, Y; internal IntPtr Mask, Color; }
        [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
        [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
        [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, int step, IntPtr brush, int flags);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
        internal static void Draw(Bitmap image, Rectangle region)
        {
            var cursor = new CursorInfo { Size = Marshal.SizeOf(typeof(CursorInfo)) }; IconInfo icon;
            if (!GetCursorInfo(ref cursor) || cursor.Flags != 1 || !GetIconInfo(cursor.Handle, out icon)) return;
            try
            {
                using (var graphics = Graphics.FromImage(image))
                { var dc = graphics.GetHdc(); try { DrawIconEx(dc, cursor.Position.X - region.X - icon.X, cursor.Position.Y - region.Y - icon.Y, cursor.Handle, 0, 0, 0, IntPtr.Zero, 3); } finally { graphics.ReleaseHdc(dc); } }
            }
            finally { if (icon.Mask != IntPtr.Zero) DeleteObject(icon.Mask); if (icon.Color != IntPtr.Zero) DeleteObject(icon.Color); }
        }
    }
}
