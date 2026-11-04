using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ShotInk
{
    internal sealed class VerificationRequiredException : IOException
    { internal VerificationRequiredException() : base("更新需要人机验证，请在验证窗口完成操作。") { } }
    internal sealed class UpdateRateLimitException : IOException
    { internal UpdateRateLimitException() : base("更新服务暂时限制请求，请稍后再试。") { } }
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
                throw new InvalidDataException("更新信息无效。");
            Uri uri;
            if (!Uri.TryCreate(Url, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.Host != "gitee.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
                !uri.AbsolutePath.StartsWith("/clear_wind_and_rain/ShotInk/releases/download/", StringComparison.Ordinal) || !uri.AbsolutePath.EndsWith("/" + BuildFeatures.Archive, StringComparison.Ordinal))
                throw new InvalidDataException("更新下载地址不是受信任的 ShotInk 便携包。");
        }
        private static bool Hash(string value) { return value != null && value.Length == 64 && value.All(Uri.IsHexDigit); }
        internal bool IsNewer(string installedVersion, string installedRevision)
        {
            Validate(); int comparison = new System.Version(Version).CompareTo(new System.Version(installedVersion));
            return comparison > 0 || comparison == 0 && !string.Equals(Revision, installedRevision, StringComparison.OrdinalIgnoreCase);
        }
    }
    [DataContract] internal sealed class GiteeContent { [DataMember(Name = "content")] internal string Content; [DataMember(Name = "encoding")] internal string Encoding; }
    [DataContract] internal sealed class UpdateFile
    {
        [DataMember] internal string Path;
        [DataMember] internal string Sha256;
    }
    [DataContract] internal sealed class UpdatePlan
    {
        [DataMember] internal string Target;
        [DataMember] internal string Payload;
        [DataMember] internal string Backup;
        [DataMember] internal int ProcessId;
        [DataMember] internal string Version;
        [DataMember] internal string Flavor;
        [DataMember] internal UpdateFile[] Files;
    }
    internal sealed class PreparedUpdate
    {
        internal string Folder, PlanPath, HelperPath;
        internal UpdateInfo Info;
    }
    internal static class UpdateService
    {
        internal const string IndexUrl = "https://gitee.com/api/v5/repos/clear_wind_and_rain/ShotInk/contents/updates/" + BuildFeatures.Index + "?ref=main";
        internal static string CacheRoot { get { return Directory.Exists(@"E:\DevData") ? @"E:\DevData\ShotInk\updates" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShotInk-updates"); } }
        internal static T ReadJson<T>(byte[] bytes)
        { using (var stream = new MemoryStream(bytes)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream); }
        internal static byte[] Json<T>(T value)
        { using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); return stream.ToArray(); } }
        internal static string InstalledRevision(string directory)
        {
            try { return File.ReadAllText(Path.Combine(directory, "ShotInk.build.sha256")).Trim(); }
            catch (IOException) { return ""; } catch (UnauthorizedAccessException) { return ""; }
        }
        internal static string Digest(string path)
        { using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        internal static async Task<UpdateInfo> CheckAsync(CancellationToken token)
        {
            using (var stream = new MemoryStream())
            {
                await FetchAsync(IndexUrl, stream, 256 * 1024, token, null).ConfigureAwait(false);
                return DecodeIndex(stream.ToArray());
            }
        }
        internal static UpdateInfo DecodeIndex(byte[] bytes)
        {
            var envelope = ReadJson<GiteeContent>(bytes);
            if (envelope.Encoding != "base64" || envelope.Content == null) throw new InvalidDataException("更新信息格式无效。");
            var info = ReadJson<UpdateInfo>(Convert.FromBase64String(envelope.Content)); info.Validate(); return info;
        }
        internal static async Task<UpdateInfo> CheckWithBrowserAsync(System.Windows.Forms.Form owner, CancellationToken token)
        {
            string folder = Path.Combine(CacheRoot, Guid.NewGuid().ToString("N"));
            using (var browser = new GiteeBrowserForm(null, Path.Combine(folder, "index.json")))
            { await browser.RunAsync(owner, token); return DecodeIndex(browser.Metadata); }
        }
        internal static Exception ResponseFailure(int status, string contentType, string body)
        {
            string text = (body ?? "").ToLowerInvariant();
            bool html = (contentType ?? "").IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0;
            if (status == 429 || (status >= 400 || html || (contentType ?? "").StartsWith("text/plain", StringComparison.OrdinalIgnoreCase)) &&
                (text.Contains("rate limit") || text.Contains("too many requests") || text.Contains("请求过于频繁") || text.Contains("访问过于频繁")))
                return new UpdateRateLimitException();
            if (html && (text.Contains("captcha") || text.Contains("verification") || text.Contains("challenge") || text.Contains("人机验证") || text.Contains("安全验证") || text.Contains("验证码") || text.Contains("人机校验")))
                return new VerificationRequiredException();
            if (status >= 400) return new IOException("更新服务暂时无法访问（HTTP " + status + "），请稍后重试。");
            return html ? new IOException("更新服务返回了无法识别的页面，请稍后重试。") : null;
        }
        private static async Task<string> ResponsePreviewAsync(Stream stream)
        {
            using (var buffer = new MemoryStream())
            {
                byte[] bytes = new byte[4096]; int count;
                while (buffer.Length < 32768 && (count = await stream.ReadAsync(bytes, 0, Math.Min(bytes.Length, 32768 - (int)buffer.Length)).ConfigureAwait(false)) > 0)
                    buffer.Write(bytes, 0, count);
                return Encoding.UTF8.GetString(buffer.ToArray());
            }
        }
        private static async Task<HttpWebResponse> ResponseAsync(HttpWebRequest request)
        {
            HttpWebResponse response;
            try { return (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false); }
            catch (WebException error)
            {
                response = error.Response as HttpWebResponse;
                if (response == null) throw;
            }
            using (response) using (var input = response.GetResponseStream())
                throw ResponseFailure((int)response.StatusCode, response.ContentType, await ResponsePreviewAsync(input).ConfigureAwait(false)) ?? new IOException("更新请求未完成，请稍后重试。");
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
                using (var response = await ResponseAsync(request).ConfigureAwait(false))
                using (var input = response.GetResponseStream())
                {
                    if (response.ResponseUri.Scheme != "https") throw new InvalidDataException("更新连接必须使用 HTTPS。");
                    if (response.ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) || response.ContentType.StartsWith("application/xhtml", StringComparison.OrdinalIgnoreCase))
                        throw ResponseFailure((int)response.StatusCode, response.ContentType, await ResponsePreviewAsync(input).ConfigureAwait(false));
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
        internal static async Task<PreparedUpdate> DownloadAsync(UpdateInfo info, string target, CancellationToken token, IProgress<int> progress, string cacheRoot = null)
        {
            info.Validate(); string folder = Path.Combine(cacheRoot ?? CacheRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            string archive = Path.Combine(folder, "ShotInk-portable.zip");
            try
            {
                using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                    await FetchAsync(info.Url, output, info.Size, token, progress).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return await Task.Run(delegate { return Prepare(archive, info, target, folder, System.Diagnostics.Process.GetCurrentProcess().Id, token); }, token).ConfigureAwait(false);
            }
            catch { CleanupPackage(folder); throw; }
        }
        internal static async Task<PreparedUpdate> DownloadWithBrowserAsync(UpdateInfo info, string target, System.Windows.Forms.Form owner, CancellationToken token, IProgress<int> progress, string cacheRoot = null)
        {
            info.Validate(); string folder = Path.Combine(cacheRoot ?? CacheRoot, Guid.NewGuid().ToString("N"));
            string archive = Path.Combine(folder, "ShotInk-portable.zip");
            try
            {
                using (var browser = new GiteeBrowserForm(info, archive, progress)) await browser.RunAsync(owner, token);
                token.ThrowIfCancellationRequested();
                return await Task.Run(delegate { return Prepare(archive, info, target, folder, System.Diagnostics.Process.GetCurrentProcess().Id, token); }, token);
            }
            catch { CleanupPackage(folder); throw; }
        }
        internal static void CleanupPackage(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;
            try
            {
                string root = Path.GetFullPath(folder);
                File.Delete(SafePath(root, "ShotInk-portable.zip"));
                string payload = SafePath(root, "payload");
                if (Directory.Exists(payload)) Directory.Delete(payload, true);
            }
            catch (IOException error) { RecordCleanupFailure(folder, error); }
            catch (UnauthorizedAccessException error) { RecordCleanupFailure(folder, error); }
        }
        private static void RecordCleanupFailure(string folder, Exception error)
        {
            try { File.WriteAllText(Path.Combine(folder, "update-cleanup-error.txt"), error.Message); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        internal static string SafePath(string root, string relative)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.Contains(":") || relative.Contains("\\") || relative.Split('/').Any(part => part == ".." || part == "."))
                throw new InvalidDataException("更新包包含不安全的路径。");
            foreach (string part in relative.TrimEnd('/').Split('/'))
                if (part.Length == 0 || part.TrimEnd(' ', '.') != part || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    System.Text.RegularExpressions.Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\.)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    throw new InvalidDataException("更新包包含无效文件名。");
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string result = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新文件超出目标目录。");
            return result;
        }
        internal static PreparedUpdate Prepare(string archive, UpdateInfo info, string target, string folder, int processId, CancellationToken token)
        {
            info.Validate();
            if (new FileInfo(archive).Length != info.Size || !string.Equals(Digest(archive), info.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新包校验失败，请重新下载。");
            string payload = Path.Combine(folder, "payload"); Directory.CreateDirectory(payload);
            var files = new List<UpdateFile>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0;
            using (var zip = ZipFile.OpenRead(archive)) foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested(); string name = entry.FullName.Replace('\\', '/'); string destination = SafePath(payload, name);
                if ((entry.ExternalAttributes & 0xF0000000) == 0xA0000000) throw new InvalidDataException("更新包不能包含符号链接。");
                if (name.EndsWith("/", StringComparison.Ordinal)) continue;
                if (!names.Add(name) || names.Count > 6000 || (expanded += entry.Length) > 1536L * 1024 * 1024) throw new InvalidDataException("更新包内容异常。");
                string lower = name.ToLowerInvariant();
                if (!BuildFeatures.Ocr && lower.StartsWith("ocr/")) throw new InvalidDataException("轻量版更新包不能包含 OCR 组件。");
                if (lower.Contains("settings.ini") || lower.StartsWith("captures/") || lower.StartsWith("updates/")) throw new InvalidDataException("更新包不能覆盖用户设置或历史。");
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                using (var input = entry.Open()) using (var output = new FileStream(destination, FileMode.CreateNew)) input.CopyTo(output);
                files.Add(new UpdateFile { Path = name, Sha256 = Digest(destination) });
            }
            var requiredFiles = new List<string> { "ShotInk.exe", "ShotInk.build.sha256", "ShotInk.flavor", "update-portable.ps1", "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll" };
            if (BuildFeatures.Ocr) requiredFiles.Add("ocr/ShotInk.OcrHost.exe");
            foreach (string required in requiredFiles)
                if (!names.Contains(required)) throw new InvalidDataException("更新包缺少必要组件：" + required);
            if (File.ReadAllText(Path.Combine(payload, "ShotInk.flavor")).Trim() != BuildFeatures.Flavor) throw new InvalidDataException("更新包类型与当前版本不一致。");
            if (!string.Equals(InstalledRevision(payload), info.Revision, StringComparison.OrdinalIgnoreCase) || System.Reflection.AssemblyName.GetAssemblyName(Path.Combine(payload, "ShotInk.exe")).Version.ToString(3) != info.Version)
                throw new InvalidDataException("更新包版本与发布信息不一致。");
            var plan = new UpdatePlan { Target = Path.GetFullPath(target), Payload = payload, Backup = Path.Combine(folder, "backup"), ProcessId = processId, Version = info.Version, Flavor = BuildFeatures.Flavor, Files = files.ToArray() };
            string planPath = Path.Combine(folder, "update-plan.json"); File.WriteAllBytes(planPath, Json(plan));
            string helper = Path.Combine(folder, "update-portable.ps1"); File.Copy(Path.Combine(payload, "update-portable.ps1"), helper);
            if (string.Equals(Path.GetFullPath(archive), Path.GetFullPath(Path.Combine(folder, "ShotInk-portable.zip")), StringComparison.OrdinalIgnoreCase)) File.Delete(archive);
            return new PreparedUpdate { Folder = folder, PlanPath = planPath, HelperPath = helper, Info = info };
        }
    }
}
