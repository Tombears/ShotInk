using System;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ShotInk
{
    [DataContract]
    internal sealed class UpdatePart
    {
        [DataMember(Name = "url")] internal string Url;
        [DataMember(Name = "size")] internal long Size;
        [DataMember(Name = "sha256")] internal string Sha256;
    }

    internal static partial class UpdateService
    {
        internal static async Task DownloadArchiveAsync(UpdateInfo info, string archive, CancellationToken token,
            IProgress<int> progress, Func<UpdatePart, string, IProgress<int>, Task> fetch)
        {
            info.Validate();
            if (info.Parts == null)
            {
                await fetch(new UpdatePart { Url = info.Url, Size = info.Size, Sha256 = info.Sha256 }, archive, progress);
                return;
            }
            long completed = 0;
            for (int index = 0; index < info.Parts.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var part = info.Parts[index];
                string path = archive + "." + (index + 1).ToString("000", System.Globalization.CultureInfo.InvariantCulture);
                await fetch(part, path, new PartProgress(progress, completed, part.Size, info.Size));
                token.ThrowIfCancellationRequested();
                bool valid = await Task.Run(delegate {
                    return new FileInfo(path).Length == part.Size && string.Equals(Digest(path), part.Sha256, StringComparison.OrdinalIgnoreCase);
                }, token);
                if (!valid) throw new InvalidDataException("第 " + (index + 1) + " 个更新分段校验失败，请重新下载。");
                completed += part.Size;
            }
            await Task.Run(delegate {
                using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[65536];
                    for (int index = 0; index < info.Parts.Length; index++)
                    {
                        string path = archive + "." + (index + 1).ToString("000", System.Globalization.CultureInfo.InvariantCulture);
                        using (var input = File.OpenRead(path))
                        {
                            int count;
                            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                token.ThrowIfCancellationRequested();
                                output.Write(buffer, 0, count);
                            }
                        }
                    }
                }
                for (int index = 0; index < info.Parts.Length; index++)
                    File.Delete(archive + "." + (index + 1).ToString("000", System.Globalization.CultureInfo.InvariantCulture));
            }, token);
        }

        private sealed class PartProgress : IProgress<int>
        {
            private readonly IProgress<int> target;
            private readonly long completed, size, total;
            internal PartProgress(IProgress<int> target, long completed, long size, long total)
            { this.target = target; this.completed = completed; this.size = size; this.total = total; }
            public void Report(int value)
            {
                if (target != null) target.Report((int)Math.Min(100, (completed + size * Math.Max(0, Math.Min(100, value)) / 100) * 100 / total));
            }
        }
    }
}
