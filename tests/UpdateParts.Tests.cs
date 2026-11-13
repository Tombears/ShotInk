using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ShotInk
{
    internal static class UpdatePartsTests
    {
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        private static string Hash(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        internal static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "ShotInk-update-parts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string archive = Path.Combine(root, "ShotInk-portable.zip");
            byte[][] chunks = { new byte[] { 1, 2, 3 }, new byte[] { 4, 5, 6, 7, 8, 9, 10 } };
            byte[] complete = chunks.SelectMany(bytes => bytes).ToArray();
            var info = new UpdateInfo {
                Version = "1.13.0", Revision = new string('A', 64), Size = complete.Length, Sha256 = Hash(complete),
                Url = "https://gitee.com/clear_wind_and_rain/ShotInk/releases/download/v1.13.0/" + BuildFeatures.Archive,
                Parts = new UpdatePart[2]
            };
            for (int index = 0; index < chunks.Length; index++)
                info.Parts[index] = new UpdatePart { Url = info.Url + "." + (index + 1).ToString("000"), Size = chunks[index].Length, Sha256 = Hash(chunks[index]) };
            try
            {
                var reports = new List<int>();
                var decoded = UpdateService.ReadJson<UpdateInfo>(UpdateService.Json(info));
                decoded.Validate(); Assert(decoded.Parts.Length == 2, "Segment metadata was lost during JSON decoding.");
                int requested = 0;
                Func<UpdatePart, string, IProgress<int>, Task> fetch = delegate(UpdatePart part, string path, IProgress<int> progress) {
                    File.WriteAllBytes(path, chunks[requested++]); progress.Report(100); return Task.FromResult(true);
                };
                UpdateService.DownloadArchiveAsync(info, archive, CancellationToken.None, new Reports(reports), fetch).GetAwaiter().GetResult();
                Assert(requested == 2 && File.ReadAllBytes(archive).SequenceEqual(complete), "Downloaded segments did not reassemble in order.");
                Assert(reports.SequenceEqual(new[] { 30, 100 }) && Directory.GetFiles(root).Length == 1, "Segment progress or successful-download cleanup was incorrect.");
                File.Delete(archive); requested = 0;
                bool rejected = false;
                try
                {
                    UpdateService.DownloadArchiveAsync(info, archive, CancellationToken.None, null, delegate(UpdatePart part, string path, IProgress<int> progress) {
                        requested++; File.WriteAllBytes(path, new byte[] { 9, 9, 9 }); return Task.FromResult(true);
                    }).GetAwaiter().GetResult();
                }
                catch (InvalidDataException) { rejected = true; }
                Assert(rejected && requested == 1 && !File.Exists(archive), "Corrupt segments reached assembly or installation.");
                UpdateService.CleanupPackage(root);
                using (var cancellation = new CancellationTokenSource())
                {
                    requested = 0; bool cancelled = false;
                    try
                    {
                        UpdateService.DownloadArchiveAsync(info, archive, cancellation.Token, null, delegate(UpdatePart part, string path, IProgress<int> progress) {
                            File.WriteAllBytes(path, chunks[requested++]); cancellation.Cancel(); return Task.FromResult(true);
                        }).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) { cancelled = true; }
                    Assert(cancelled && requested == 1 && !File.Exists(archive), "Cancellation downloaded another segment or assembled a package.");
                }
                UpdateService.CleanupPackage(root);
                string expected = info.Parts[1].Url; info.Parts[1].Url = info.Parts[0].Url;
                rejected = false; try { info.Validate(); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected, "Duplicate or reordered segment URLs were accepted."); info.Parts[1].Url = expected;
                info.Parts[1].Size++;
                rejected = false; try { info.Validate(); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected, "Inconsistent segment totals were accepted."); info.Parts[1].Size--;
                info.Parts = null; requested = 0;
                UpdateService.DownloadArchiveAsync(info, archive, CancellationToken.None, null, delegate(UpdatePart part, string path, IProgress<int> progress) {
                    requested++; File.WriteAllBytes(path, complete); return Task.FromResult(true);
                }).GetAwaiter().GetResult();
                Assert(requested == 1 && UpdateService.Digest(archive) == info.Sha256, "Legacy single-package download stopped working.");
                Console.WriteLine("PASS segmented update metadata, ordered assembly, per-part hashes, progress, cancellation, cleanup and legacy download");
            }
            finally { Directory.Delete(root, true); }
        }
        private sealed class Reports : IProgress<int>
        {
            private readonly List<int> values;
            internal Reports(List<int> values) { this.values = values; }
            public void Report(int value) { values.Add(value); }
        }
    }
}
