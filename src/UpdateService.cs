using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ShotInk
{
    [DataContract]
    internal sealed class UpdateInfo
    {
        [DataMember(Name = "version")] internal string Version;
        [DataMember(Name = "revision")] internal string Revision;
        [DataMember(Name = "sha256")] internal string Sha256;
        [DataMember(Name = "size")] internal long Size;
        [DataMember(Name = "url")] internal string Url;
        [DataMember(Name = "notes")] internal string Notes;
        internal void Validate()
        {
            System.Version version;
            if (Version == null || !System.Text.RegularExpressions.Regex.IsMatch(Version, @"^\d+\.\d+\.\d+$") || !System.Version.TryParse(Version, out version) || version.Major < 1 || !Hash(Revision) || !Hash(Sha256) || Size < 1 || Size > 512L * 1024 * 1024)
                throw new InvalidDataException("Gitee 更新信息无效。");
            Uri uri;
            if (!Uri.TryCreate(Url, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.Host != "gitee.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
                !uri.AbsolutePath.StartsWith("/clear_wind_and_rain/ShotInk/releases/download/", StringComparison.Ordinal) || !uri.AbsolutePath.EndsWith("/ShotInk-portable.zip", StringComparison.Ordinal))
                throw new InvalidDataException("更新下载地址不是 ShotInk 的 Gitee 便携包。");
        }
        private static bool Hash(string value) { return value != null && value.Length == 64 && value.All(Uri.IsHexDigit); }
        internal bool IsNewer(string installedVersion, string installedRevision)
        {
            Validate(); int comparison = new System.Version(Version).CompareTo(new System.Version(installedVersion));
            return comparison > 0 || comparison == 0 && !string.Equals(Revision, installedRevision, StringComparison.OrdinalIgnoreCase);
        }
    }
    [DataContract] internal sealed class GiteeContent { [DataMember(Name = "content")] internal string Content; [DataMember(Name = "encoding")] internal string Encoding; }
    internal static class UpdateService
    {
        internal const string ReleasePage = "https://gitee.com/clear_wind_and_rain/ShotInk/releases";
        internal const string IndexUrl = "https://gitee.com/api/v5/repos/clear_wind_and_rain/ShotInk/contents/updates/latest.json?ref=main";
        internal static T ReadJson<T>(byte[] bytes)
        { using (var stream = new MemoryStream(bytes)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream); }
        internal static string InstalledRevision(string directory)
        {
            try { return File.ReadAllText(Path.Combine(directory, "ShotInk.build.sha256")).Trim(); }
            catch (IOException) { return ""; } catch (UnauthorizedAccessException) { return ""; }
        }
        internal static async Task<UpdateInfo> CheckAsync(CancellationToken token)
        {
            using (var stream = new MemoryStream())
            {
                await FetchAsync(IndexUrl, stream, 256 * 1024, token, null).ConfigureAwait(false);
                var envelope = ReadJson<GiteeContent>(stream.ToArray());
                if (envelope.Encoding != "base64" || envelope.Content == null) throw new InvalidDataException("Gitee 更新信息格式无效。");
                var info = ReadJson<UpdateInfo>(Convert.FromBase64String(envelope.Content)); info.Validate(); return info;
            }
        }
        private static async Task FetchAsync(string url, Stream output, long maximum, CancellationToken token, IProgress<int> progress)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url); request.UserAgent = "ShotInk-Updater"; request.Timeout = 20000; request.ReadWriteTimeout = 30000;
            request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(progress == null ? TimeSpan.FromSeconds(20) : TimeSpan.FromMinutes(10));
                using (timeout.Token.Register(request.Abort))
                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                using (var input = response.GetResponseStream())
                {
                    if (response.ResponseUri.Scheme != "https") throw new InvalidDataException("更新连接必须使用 HTTPS。");
                    if (response.ContentLength > maximum) throw new InvalidDataException("更新数据超过允许的大小。");
                    var buffer = new byte[65536]; long total = 0; int count;
                    while ((count = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) != 0)
                    {
                        total += count; if (total > maximum) throw new InvalidDataException("更新数据超过允许的大小。");
                        await output.WriteAsync(buffer, 0, count, timeout.Token).ConfigureAwait(false);
                        if (progress != null) progress.Report((int)Math.Min(100, total * 100 / maximum));
                    }
                }
            }
        }
        internal static void OpenDownload(UpdateInfo info, Action<System.Diagnostics.ProcessStartInfo> open = null)
        {
            info.Validate();
            var start = new System.Diagnostics.ProcessStartInfo(info.Url) { UseShellExecute = true };
            if (open != null) open(start); else System.Diagnostics.Process.Start(start);
        }
    }
}
