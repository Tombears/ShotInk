using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ShotInk
{
    internal static class Tests
    {
        private static int passed;
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Native.EnableDpiAwareness();
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (args.Length == 1 && args[0] == "--gif-scroll-tests")
                {
                    Run("GIF rendering leaves subsequent long capture responsive", delegate
                    {
                        Control.CheckForIllegalCrossThreadCalls = true;
                        GifToolsTests.Run(); System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Threading.SynchronizationContext()); TestIntegratedScroll();
                    }); return 0;
                }
                if (args.Length == 1 && args[0] == "--update-tests")
                { Run("Gitee updates validate Windows ZIP paths and reject traversal", TestUpdates); return 0; }
                if (args.Length == 1 && args[0] == "--verification-tests")
                { Run("Gitee verification retains browser cookies, resumes downloads and cancels safely", TestGiteeBrowser); return 0; }
                if (args.Length == 2 && args[0] == "--update-live")
                {
                    var info = UpdateService.CheckAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    var prepared = UpdateService.DownloadAsync(info, Path.Combine(args[1], "install"), System.Threading.CancellationToken.None,
                        new Progress<int>(delegate { }), args[1]).GetAwaiter().GetResult();
                    Console.WriteLine("PASS anonymous Gitee check, ZIP hash and component validation: " + prepared.Info.Version); return 0;
                }
                if (args.Length == 1 && args[0] == "--gif-tests")
                {
                    Run("GIF encoding preserves trimmed frames, colors and timing", TestGifEncoding);
                    Run("GIF editor trims and seeks one frame per arrow key", TestGifEditor);
                    Run("GIF deletion joins frames with original delays and resets the timeline", TestGifDeletion);
                    Run("GIF frame tools preserve scopes, independent images, undo and current-frame OCR", GifToolsTests.Run);
                    Run("GIF sampling stops and cancels cleanly", TestGifSampling);
                    Run("capture toolbar drags without moving the screenshot", TestToolbarDrag);
                    Run("GIF capture uses the chosen region and opens its editor", TestIntegratedGif);
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--scroll-tests")
                {
                    Run("manual scroll sampling and asynchronous stitching preserve pixels", TestScrollPipeline);
                    Run("scroll capture automatically completes at limits and cancels without hanging", TestScrollLimitAndCancel);
                    Run("integrated scroll capture keeps its selection when the pointer leaves", TestIntegratedScroll);
                    Run("page changes wait for completion and retain a partial-capture notice", TestScrollGap);
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--initial-preview")
                {
                    Run("initial selection rendering preserves frozen pixels and reuses its dimmed background", TestInitialSelectionPaint);
                    Run("hover and initial box drawing repaint without old crosshairs, hints or borders", TestLiveInitialSelection);
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--capture-tests") { CaptureWorkflowTests.Run(); return 0; }
                CaptureWorkflowTests.Run();
                Run("renamed app preserves existing history and settings", TestDataRoot);
                Run("several scroll offsets reconstruct the original pixels", TestStitch);
                Run("scroll reversals revisit existing rows without duplicating or losing new tail pixels", TestScrollReversals);
                Run("text pages with whitespace and small overlaps preserve every row", TestDocumentStitch);
                Run("text seams retain whole glyphs and narrow left-hand content", TestTextSeams);
                Run("unchanged frame does not duplicate content", TestUnchanged);
                Run("unrelated frames are rejected", TestUnrelated);
                Run("changed frame dimensions are rejected", TestDimensions);
                Run("height limit rejects oversized first frame", TestInitialLimit);
                Run("accumulated height is bounded", TestAccumulatedLimit);
                Run("annotations render at original resolution and undo", TestAnnotations);
                Run("inline text supports multiline input, settings, cancellation and export", TestInlineText);
                Run("typing stays transparent, compact and anchored to the image caret", TestTransparentTextInput);
                Run("text moves, scales and rotates with independent undo at different zooms", TestTextTransform);
                Run("mosaic covers partial edge blocks", TestMosaic);
                Run("mosaic averaging and scaled previews contain no white grid seams", TestMosaicPixels);
                Run("Gaussian blur has a smooth impulse response and stays within the mask", TestGaussianBlur);
                Run("mosaic rectangles remain editable with independent undo", TestEditableMosaic);
                Run("point-click polylines complete, cancel and undo as one annotation", TestPolylines);
                Run("application icon contains the supplied pen and colorful corners in transparent multi-size images", TestApplicationIcon);
                Run("toolbar corners and color circles have soft edges without losing their fill", TestToolRendering);
                Run("undo and redo restore additions and annotation erasing", TestUndoRedoErase);
                Run("tool buttons immediately activate defaults and expose independent settings", TestToolOptions);
                Run("tools retain separate colors and widths across tool changes and new editors", TestIndependentToolSettings);
                Run("settings preserve legacy history, shortcuts, tool styles and save preferences", TestSettings);
                Run("settings survive profile selection, restart and damaged or locked configuration", TestSettingsRecovery);
                Run("Updates run only on request, refresh settings silently and validate update packages", TestUpdates);
                Run("Gitee verification retains browser cookies, resumes downloads and cancels safely", TestGiteeBrowser);
                Run("pinned images detect edges and resize proportionally while preserving original pixels", TestPinResize);
                Run("settings refresh shortcut conflicts without saving or leaking probe registrations", TestShortcutStatus);
                Run("released shortcut conflicts recover automatically without unregistering healthy bindings", TestHotkeyRecovery);
                Run("startup registration handles quoted paths, disabling and save rollback without touching real startup entries", TestStartupRegistration);
                Run("startup mode stays hidden and can reopen the workbench", TestStartupTray);
                Run("history defaults to ten and its on-screen limit persists and prunes only history", TestHistoryLimit);
                Run("manual scroll sampling and asynchronous stitching preserve pixels", TestScrollPipeline);
                Run("GIF encoding preserves trimmed frames, colors and timing", TestGifEncoding);
                Run("GIF editor trims and seeks one frame per arrow key", TestGifEditor);
                Run("GIF deletion joins frames with original delays and resets the timeline", TestGifDeletion);
                Run("GIF frame tools preserve scopes, independent images, undo and current-frame OCR", GifToolsTests.Run);
                Run("GIF sampling stops and cancels cleanly", TestGifSampling);
                Run("capture toolbar drags without moving the screenshot", TestToolbarDrag);
                Run("GIF capture uses the chosen region and opens its editor", TestIntegratedGif);
                Run("slow stitching retains every queued overlap frame under pressure", TestScrollBackPressure);
                Run("scroll capture automatically completes at limits and cancels without hanging", TestScrollLimitAndCancel);
                Run("integrated scroll capture restores tools and edits within a fixed viewport", TestIntegratedScroll);
                Run("long preview preserves native pixels and moves its frame without altering content", TestLongPreviewMove);
                Run("settings suspend global shortcuts and prevent capture until the dialog closes", TestSettingsCaptureGuard);
                Run("page changes wait for completion and retain a partial-capture notice", TestScrollGap);
                Run("shortcut changes restore existing bindings on a registration conflict", TestHotkeyRollback);
                Run("configured save paths work without prompts in screenshots and the editor", TestSavePreferences);
                Run("OCR highlights form continuous bands without gaps or darker seams", TestOcrHighlight);
                Run("selection dragging repaints one surface and reframes once on release", TestSelectionDrag);
                Run("initial selection rendering preserves frozen pixels and reuses its dimmed background", TestInitialSelectionPaint);
                Run("oval sliders support bounded dragging and keyboard adjustments", TestOvalSlider);
                Run("OCR selection preserves columns, punctuation and screenshot pixels", TestOcrSelection);
                Run("OCR search locates matches throughout a long image without changing export or clipboard selection", TestOcrSearch);
                Run("large OCR responses preserve all character coordinates", TestLargeOcrResponse);
                Run("OCR requests suppress reentry and ignore stale or cancelled results", TestOcrSession);
                Run("mosaic brush follows continuous strokes and keeps sizes independent", TestMosaicBrush);
                Run("mosaic selection ignores brush size and respects pixel size", TestMosaicSelection);
                Run("eraser brush and selection restore only the requested pixels", TestEraserModes);
                Run("overlapping eraser strokes preserve coverage without deleting the whole line", TestEraserOverlap);
                Run("highlighter remains translucent and preserves the original", TestHighlighter);
                Run("floating selection stays open and exports only edited pixels", TestFloatingSelection);
                Run("selection resize and toolbar stay within display bounds", TestSelectionGeometry);
                Run("cancelled selection does not export a screenshot", TestSelectionCancel);
                Run("right-button cancellation waits for release across overlay, canvas and long preview", TestRightCancel);
                if (args.Length == 2 && args[0] == "--preview")
                {
                    Run("native screen capture reads a known test window", TestScreenCapture);
                    Run("manual scrolling reconstructs a real scrolling window", TestAutomaticCapture);
                    Preview(args[1]);
                }
                if (args.Length == 1 && args[0] == "--drag-preview") Run("live selection dragging clears old borders on screen", TestLiveDrag);
                if (args.Length == 1 && args[0] == "--drag-preview") Run("live long-image dragging erases its old viewport and scrollbars", TestLiveLongDrag);
                if (args.Length == 1 && args[0] == "--scroll-preview")
                { Run("manual scrolling reconstructs a real scrolling window", TestAutomaticCapture); Run("live integrated scroll capture excludes its own chrome", TestLiveIntegratedScroll); }
                if (args.Length == 1 && args[0] == "--interaction-preview")
                {
                    Run("hover and initial box drawing repaint without old crosshairs, hints or borders", TestLiveInitialSelection);
                    Run("real right-button cancellation does not reach the background window", TestLiveRightCancel);
                    Run("live selection dragging clears old borders on screen", TestLiveDrag);
                    Run("live long-image dragging erases its old viewport and scrollbars", TestLiveLongDrag);
                    Run("manual scrolling reconstructs a real scrolling window", TestAutomaticCapture);
                    Run("live integrated scroll capture excludes its own chrome", TestLiveIntegratedScroll);
                }
                Console.WriteLine("PASS: " + passed + " checks");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        }
        private static void TestDataRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "ShotInk-tests-" + Guid.NewGuid().ToString("N"));
            var current = Path.Combine(root, "ShotInk");
            var legacy = Path.Combine(root, "SnipasteLite");
            try
            {
                Assert(MainForm.ResolveDataRoot(root) == current, "Fresh installs use the wrong data directory.");
                Directory.CreateDirectory(Path.Combine(legacy, "captures"));
                var settings = Path.Combine(legacy, "settings.ini");
                File.WriteAllText(settings, "save=0");
                using (var image = new Bitmap(2, 2)) image.Save(Path.Combine(legacy, "captures", "saved.png"), ImageFormat.Png);
                Assert(MainForm.ResolveDataRoot(root) == legacy, "Upgrade did not reuse existing data.");
                using (var home = new MainForm(MainForm.ResolveDataRoot(root), false))
                {
                    Assert(!Field<CheckBox>(home, "autoSave").Checked, "Upgrade lost the auto-save preference.");
                    Assert(Field<ListView>(home, "history").Items.Count == 1, "Upgrade lost the screenshot history.");
                    Assert(home.Text.StartsWith("ShotInk"), "Window title still uses the previous name.");
                }
                Assert(File.ReadAllText(settings) == "save=0", "Opening the app changed the saved preference.");
                Directory.CreateDirectory(current);
                Assert(MainForm.ResolveDataRoot(root) == legacy, "An empty ShotInk directory hid the legacy settings.");
                File.WriteAllText(Path.Combine(current, "settings.ini"), "save=1");
                Assert(MainForm.ResolveDataRoot(root) == current, "An existing ShotInk settings file was ignored.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void Run(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
        private static void TestIndependentToolSettings()
        {
            var settings = new AppSettings();
            using (var options = new AnnotationOptions(settings) { Width = 440 })
            using (var canvas = new AnnotationCanvas(new Bitmap(250, 150)))
            {
                canvas.Tool = DrawTool.Pen; options.ShowTool(DrawTool.Pen); options.ApplyTo(canvas);
                options.Ink = Color.Blue; Option<NumericUpDown>(options, "tool-stroke").Value = 8; options.ApplyTo(canvas);
                canvas.Tool = DrawTool.Rectangle; options.ShowTool(DrawTool.Rectangle); options.ApplyTo(canvas);
                Assert(canvas.Stroke == 3 && canvas.Ink != Color.Blue, "Pen settings leaked into the rectangle tool.");
                options.Ink = Color.Green; Option<NumericUpDown>(options, "tool-stroke").Value = 2; options.ApplyTo(canvas);
                canvas.Tool = DrawTool.Ellipse; options.ShowTool(DrawTool.Ellipse); options.ApplyTo(canvas);
                Assert(canvas.Stroke == 3 && canvas.Ink != Color.Green, "Shape variants share one profile.");
                canvas.Tool = DrawTool.Pen; options.ShowTool(DrawTool.Pen); options.ApplyTo(canvas);
                Assert(canvas.Stroke == 8 && canvas.Ink == Color.Blue, "Switching tools lost the pen profile.");
                Gesture(canvas, new Point(10, 10), new Point(60, 50));
                Assert(canvas.Marks[0].Width == 8 && canvas.Marks[0].Color == Color.Blue, "New marks ignored their tool profile.");
                using (var editor = new EditorForm(new Bitmap(250, 150), delegate(Bitmap image) { image.Dispose(); }, settings))
                {
                    var next = Field<AnnotationOptions>(editor, "toolOptions");
                    next.ShowTool(DrawTool.Pen); Assert(next.Stroke == 8 && next.Ink == Color.Blue, "A new editor lost the shared preferences.");
                }
            }
        }
        private static void AddUpdateEntry(System.IO.Compression.ZipArchive zip, string name, byte[] bytes)
        { using (var stream = zip.CreateEntry(name).Open()) stream.Write(bytes, 0, bytes.Length); }
        private static void TestUpdates()
        {
            string root = Path.Combine(Path.GetTempPath(), "ShotInk-update-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                string revision = new string('A', 64), zipPath = Path.Combine(root, "update.zip");
                using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
                {
                    AddUpdateEntry(zip, "ShotInk.exe", File.ReadAllBytes(System.Reflection.Assembly.GetExecutingAssembly().Location));
                    AddUpdateEntry(zip, "ShotInk.build.sha256", System.Text.Encoding.UTF8.GetBytes(revision));
                    AddUpdateEntry(zip, "ShotInk.flavor", System.Text.Encoding.UTF8.GetBytes(BuildFeatures.Flavor));
                    AddUpdateEntry(zip, "update-portable.ps1", System.Text.Encoding.UTF8.GetBytes("test helper"));
                    AddUpdateEntry(zip, "ocr\\ShotInk.OcrHost.exe", new byte[] { 1, 2, 3 });
                    zip.CreateEntry("docs\\");
                    foreach (string name in new[] { "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll" }) AddUpdateEntry(zip, name, new byte[] { 1, 2, 3 });
                }
                var info = new UpdateInfo { Version = Application.ProductVersion, Revision = revision, Sha256 = UpdateService.Digest(zipPath), Size = new FileInfo(zipPath).Length,
                    Url = "https://gitee.com/clear_wind_and_rain/ShotInk/releases/download/v" + Application.ProductVersion + "/ShotInk-portable.zip", Notes = "更新说明" };
                info.Validate();
                Assert(!info.IsNewer(Application.ProductVersion, revision.ToLowerInvariant()) && info.IsNewer(Application.ProductVersion, new string('B', 64)), "Same-version build detection repeated an installed build or missed a patch.");
                Assert(info.IsNewer("1.0.0", revision) && !info.IsNewer("99.0.0", new string('B', 64)), "Update comparison allowed a downgrade or missed a new version.");
                var prepared = UpdateService.Prepare(zipPath, info, Path.Combine(root, "install"), Path.Combine(root, "stage"), int.MaxValue, System.Threading.CancellationToken.None);
                var plan = UpdateService.ReadJson<UpdatePlan>(File.ReadAllBytes(prepared.PlanPath));
                Assert(plan.Files.Length == 8 && File.Exists(prepared.HelperPath) && UpdateService.InstalledRevision(plan.Payload) == revision, "Update staging lost components or revision metadata.");
                foreach (string invalid in new[] { "../outside", "C:/outside", "/outside", "foo\\bar", "x/.. /outside", "aux.txt", "foo./bar" })
                {
                    bool rejected = false; try { UpdateService.SafePath(root, invalid); } catch (InvalidDataException) { rejected = true; }
                    Assert(rejected, "Unsafe update path accepted: " + invalid);
                }
                string validUrl = info.Url; info.Url = "https://github.com/other/release/ShotInk-portable.zip";
                bool unsafeUrl = false; try { info.Validate(); } catch (InvalidDataException) { unsafeUrl = true; }
                Assert(unsafeUrl, "Updater accepted a non-Gitee publisher URL."); info.Url = validUrl;
                string digest = info.Sha256; info.Sha256 = new string('0', 64);
                bool badHash = false; try { UpdateService.Prepare(zipPath, info, Path.Combine(root, "install"), Path.Combine(root, "bad-hash"), int.MaxValue, System.Threading.CancellationToken.None); } catch (InvalidDataException) { badHash = true; }
                Assert(badHash, "Corrupted package hash was accepted."); info.Sha256 = digest;
                foreach (string invalidEntry in new[] { "../escape.txt", "settings.ini", "captures/history.png", "ocr/../ShotInk.exe", "ocr\\..\\ShotInk.exe", "ocr\\..\\..\\escape.txt" })
                {
                    string malicious = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
                    using (var zip = System.IO.Compression.ZipFile.Open(malicious, System.IO.Compression.ZipArchiveMode.Create)) AddUpdateEntry(zip, invalidEntry, new byte[] { 1 });
                    info.Size = new FileInfo(malicious).Length; info.Sha256 = UpdateService.Digest(malicious);
                    bool rejected = false; try { UpdateService.Prepare(malicious, info, Path.Combine(root, "install"), Path.Combine(root, Guid.NewGuid().ToString("N")), int.MaxValue, System.Threading.CancellationToken.None); } catch (InvalidDataException) { rejected = true; }
                    Assert(rejected, "Update package could write user data or escape staging.");
                }
                info.Size = new FileInfo(zipPath).Length; info.Sha256 = digest;
                using (var form = new UpdateForm(delegate { return System.Threading.Tasks.Task.FromResult<string>("test"); }, info,
                    delegate { return System.Threading.Tasks.Task.FromResult(info); }))
                {
                    form.Show(); Application.DoEvents(); Assert(Field<Button>(form, "action").Text == "下载更新" && Field<Label>(form, "status").Text.Contains("修复更新"), "Update UI did not offer a same-version patch.");
                    using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-update-options.png")); }
                    form.Close();
                }
                int verifiedChecks = 0;
                using (var form = new UpdateForm(delegate { return System.Threading.Tasks.Task.FromResult<string>("test"); }, null,
                    delegate { throw new VerificationRequiredException(); }, delegate { verifiedChecks++; return System.Threading.Tasks.Task.FromResult(info); }))
                {
                    form.Show(); Application.DoEvents();
                    Assert(verifiedChecks == 1 && Field<Button>(form, "action").Enabled, "Update details could not continue after a metadata verification challenge."); form.Close();
                }
                int requests = 0, details = 0;
                using (var controller = new UpdateController("1.0.0", "",
                    delegate { requests++; return System.Threading.Tasks.Task.FromResult(info); }))
                using (var options = new SettingsForm(new AppSettings(), delegate { return null; }, delegate { return true; }, controller, delegate { details++; }))
                {
                    int windows = Application.OpenForms.Count;
                    options.Show(); Application.DoEvents();
                    Assert(requests == 0 && options.Controls.Find("check-updates", true).Length == 0, "Opening settings checked for updates or retained automatic checking.");
                    Assert(!typeof(UpdateController).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Any(field => field.FieldType == typeof(System.Windows.Forms.Timer)), "Update controller retained a polling timer.");
                    windows = Application.OpenForms.Count;
                    Field<Button>(options, "updateCheck").PerformClick(); Application.DoEvents();
                    Assert(requests == 1 && Application.OpenForms.Count == windows && details == 0, "Manual check used cached data or opened an update window.");
                    Assert(Field<Label>(options, "updateStatus").Text.Contains(info.Version) && Field<Button>(options, "updateDetails").Enabled, "Settings did not display the detected update.");
                    Field<Button>(options, "updateDetails").PerformClick();
                    Assert(details == 1, "Explicit update-details click did not open the update flow.");
                    using (var image = new Bitmap(options.Width, options.Height)) { options.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-settings-updates.png")); }
                    options.Close();
                    controller.CheckAsync().GetAwaiter().GetResult();
                    Assert(requests == 2 && Application.OpenForms.Count == windows - 1, "Closed settings broke explicit update checks.");
                }
                using (var controller = new UpdateController("1.0.0", "", delegate { throw new VerificationRequiredException(); }))
                using (var options = new SettingsForm(new AppSettings(), delegate { return null; }, delegate { return true; }, controller,
                    delegate { controller.CheckAsync(delegate { return System.Threading.Tasks.Task.FromResult(info); }).GetAwaiter().GetResult(); }))
                {
                    int windows = Application.OpenForms.Count; controller.CheckAsync().GetAwaiter().GetResult();
                    Assert(controller.NeedsVerification && Application.OpenForms.Count == windows, "Blocked background checks did not remain silent.");
                    options.Show(); Application.DoEvents();
                    Assert(Field<Button>(options, "updateDetails").Enabled && Field<Button>(options, "updateDetails").Text == "完成验证", "Settings did not expose an explicit verification action.");
                    Field<Button>(options, "updateDetails").PerformClick();
                    Assert(!controller.NeedsVerification && controller.Available != null, "Verified metadata did not restore the available update."); options.Close();
                }
                Assert(UpdateService.ResponseFailure(403, "text/plain", "403 Forbidden (Rate Limit Exceeded)") is UpdateRateLimitException &&
                    UpdateService.ResponseFailure(429, "application/json", "{}") is UpdateRateLimitException &&
                    UpdateService.ResponseFailure(403, "text/html", "<p>Rate limit exceeded</p><p>captcha</p>") is UpdateRateLimitException,
                    "Rate limiting was mistaken for an interactive challenge.");
                Assert(UpdateService.ResponseFailure(403, "text/html", "<h1>安全验证</h1><p>请输入验证码</p>") is VerificationRequiredException &&
                    !(UpdateService.ResponseFailure(403, "text/plain", "Forbidden") is VerificationRequiredException) &&
                    !(UpdateService.ResponseFailure(200, "text/html", "Service unavailable") is VerificationRequiredException), "Noninteractive errors opened verification.");
                using (var controller = new UpdateController("1.0.0", "", delegate { throw new UpdateRateLimitException(); }))
                {
                    controller.CheckAsync().GetAwaiter().GetResult();
                    Assert(!controller.NeedsVerification && controller.Status.Contains("稍后"), "Rate limiting exposed a verification action in settings.");
                }
                string settingsPath = Path.Combine(root, "settings.ini");
                File.WriteAllText(settingsPath, "check-updates=1\nsave-directory=" + root);
                var settings = AppSettings.Load(settingsPath); settings.Save(settingsPath);
                Assert(!File.ReadAllText(settingsPath).Contains("check-updates="), "Legacy settings restored automatic checks.");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void BrowserWait(Func<bool> condition, System.Threading.Tasks.Task task, string phase = "operation")
        {
            Console.WriteLine("WebView test: " + phase);
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                if (task.IsFaulted || task.IsCanceled) task.GetAwaiter().GetResult();
                Application.DoEvents(); System.Threading.Thread.Sleep(5);
            }
            Assert(condition(), "Embedded browser timed out during " + phase + ".");
        }
        private static void TestGiteeBrowser()
        {
            string root = Path.Combine(Path.GetTempPath(), "ShotInk-browser-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            byte[] payload = System.Text.Encoding.UTF8.GetBytes("mock portable ZIP payload");
            var info = new UpdateInfo { Version = Application.ProductVersion, Revision = new string('A', 64), Sha256 = new string('B', 64), Size = payload.Length,
                Url = "https://gitee.com/clear_wind_and_rain/ShotInk/releases/download/v" + Application.ProductVersion + "/ShotInk-portable.zip" };
            var envelope = System.Text.Encoding.UTF8.GetBytes("{\"encoding\":\"base64\",\"content\":\"" + Convert.ToBase64String(UpdateService.Json(info)) + "\"}");
            Assert(GiteeBrowserForm.Trusted(new Uri(info.Url)) && GiteeBrowserForm.Trusted(new Uri("https://foruda.gitee.com/file")) &&
                !GiteeBrowserForm.Trusted(new Uri("https://gitee.com.evil.test/file")) && !GiteeBrowserForm.Trusted(new Uri("http://gitee.com/file")), "Verification navigation trust boundary was incorrect.");
            try
            {
                using (var server = new MockGiteeServer(payload, envelope))
                using (var owner = new Form { Text = "Isolated update browser test" })
                {
                    owner.Show(); Application.DoEvents(); string profile = Path.Combine(root, "profile");
                    Func<Uri, bool> allowed = delegate(Uri uri) { return uri.Scheme == "http" && uri.IsLoopback && uri.Port == server.Port; };
                    string destination = Path.Combine(root, "download.zip");
                    var responseTask = (System.Threading.Tasks.Task<System.Net.HttpWebResponse>)typeof(UpdateService).GetMethod("ResponseAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                        .Invoke(null, new object[] { System.Net.WebRequest.Create(server.Url + "rate-limit") });
                    bool limited = false; try { responseTask.GetAwaiter().GetResult(); } catch (UpdateRateLimitException) { limited = true; }
                    Assert(limited, "HTTP 403 rate-limit response was not recognized before opening a browser.");
                    foreach (string route in new[] { "rate-limit", "limit-json", "denied" })
                    using (var browser = new GiteeBrowserForm(null, Path.Combine(root, route + ".json"), testUrl: server.Url + route, testAllowed: allowed, testProfile: profile))
                    {
                        var run = browser.RunAsync(owner, System.Threading.CancellationToken.None);
                        BrowserWait(delegate { return run.IsCompleted; }, System.Threading.Tasks.Task.FromResult(true), route + " response");
                        Exception failure = null; try { run.GetAwaiter().GetResult(); } catch (IOException error) { failure = error; }
                        Assert(failure != null && !(failure is VerificationRequiredException) && !browser.VerificationShown && owner.Enabled,
                            "A noninteractive rejection displayed a verification window.");
                        Assert(route == "denied" || failure is UpdateRateLimitException, "Browser rate limiting did not return a friendly retry message.");
                    }
                    using (var browser = new GiteeBrowserForm(info, destination, testUrl: server.Url + "download", testAllowed: allowed, testProfile: profile))
                    {
                        var run = browser.RunAsync(owner, System.Threading.CancellationToken.None);
                        BrowserWait(delegate { return browser.VerificationShown; }, run, "initial verification page");
                        Assert(browser.Visible && !owner.Enabled && !File.Exists(destination), "Verification did not show before downloading or failed to suspend its owner.");
                        var click = browser.Browser.CoreWebView2.ExecuteScriptAsync("document.getElementById('verify').click()");
                        BrowserWait(delegate { return run.IsCompleted; }, run, "verified download"); run.GetAwaiter().GetResult();
                        Assert(owner.Enabled && File.ReadAllBytes(destination).Length == payload.Length && server.VerifiedDownloads == 1, "Download did not retain the verified browser session or restore its owner.");
                    }
                    using (var browser = new GiteeBrowserForm(null, Path.Combine(root, "index.json"), testUrl: server.Url + "index", testAllowed: allowed, testProfile: profile))
                    {
                        var run = browser.RunAsync(owner, System.Threading.CancellationToken.None);
                        BrowserWait(delegate { return run.IsCompleted || browser.VerificationShown; }, run, "metadata navigation");
                        if (browser.VerificationShown) browser.Browser.CoreWebView2.ExecuteScriptAsync("document.getElementById('verify').click()");
                        BrowserWait(delegate { return run.IsCompleted; }, run, "verified metadata response"); run.GetAwaiter().GetResult();
                        Assert(UpdateService.DecodeIndex(browser.Metadata).Version == info.Version, "Verified metadata did not retain the browser session.");
                    }
                    string blocked = Path.Combine(root, "blocked"); File.WriteAllText(blocked, "not a directory");
                    using (var browser = new GiteeBrowserForm(info, Path.Combine(blocked, "download.zip"), testUrl: server.Url + "download", testAllowed: allowed, testProfile: profile))
                    {
                        var run = browser.RunAsync(owner, System.Threading.CancellationToken.None);
                        BrowserWait(delegate { return run.IsCompleted; }, System.Threading.Tasks.Task.FromResult(true), "download write failure");
                        Assert(run.IsFaulted && owner.Enabled && File.ReadAllText(blocked) == "not a directory", "A download write failure escaped the browser task or changed existing files.");
                        try { run.GetAwaiter().GetResult(); } catch (IOException) { }
                    }
                    using (var cancellation = new System.Threading.CancellationTokenSource())
                    using (var browser = new GiteeBrowserForm(info, Path.Combine(root, "cancel.zip"), testUrl: server.Url + "cancel", testAllowed: allowed, testProfile: profile))
                    {
                        var run = browser.RunAsync(owner, cancellation.Token);
                        BrowserWait(delegate { return browser.VerificationShown; }, run, "cancellation page"); cancellation.Cancel();
                        BrowserWait(delegate { return run.IsCompleted; }, System.Threading.Tasks.Task.FromResult(true), "cancellation completion");
                        Assert(run.IsCanceled && owner.Enabled && !File.Exists(Path.Combine(root, "cancel.zip")), "Cancelling verification did not leave the application and files intact.");
                    }
                    owner.Close();
                }
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch (IOException) { Console.WriteLine("Browser profile retained in isolated test cache until its process exits."); }
                catch (UnauthorizedAccessException) { Console.WriteLine("Browser profile retained in isolated test cache until its process exits."); }
            }
        }
        private sealed class MockGiteeServer : IDisposable
        {
            private readonly System.Net.Sockets.TcpListener server;
            private readonly System.Threading.Tasks.Task worker;
            private readonly byte[] payload, metadata;
            private volatile bool stopped;
            internal int VerifiedDownloads;
            internal int Port { get { return ((System.Net.IPEndPoint)server.LocalEndpoint).Port; } }
            internal string Url { get { return "http://localhost:" + Port + "/"; } }
            internal MockGiteeServer(byte[] payload, byte[] metadata)
            {
                this.payload = payload; this.metadata = metadata;
                server = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); server.Start();
                worker = System.Threading.Tasks.Task.Run((Action)Serve);
            }
            private void Serve()
            {
                while (!stopped)
                {
                    try
                    {
                        var client = server.AcceptTcpClient();
                        System.Threading.Tasks.Task.Run(delegate { Reply(client); });
                    }
                    catch (System.Net.Sockets.SocketException) { if (stopped) return; }
                    catch (ObjectDisposedException) { return; }
                }
            }
            private void Reply(System.Net.Sockets.TcpClient client)
            {
                try
                {
                    using (client)
                    using (var stream = client.GetStream())
                    {
                        client.ReceiveTimeout = client.SendTimeout = 5000;
                        var reader = new StreamReader(stream, System.Text.Encoding.ASCII, false, 4096, true);
                        string first = reader.ReadLine(), line; bool verified = false;
                        if (first == null) return;
                        while (!string.IsNullOrEmpty(line = reader.ReadLine())) if (line.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase) && line.Contains("verified=yes")) verified = true;
                        bool index = first.Contains(" /index "), cancel = first.Contains(" /cancel "), download = first.Contains(" /download ");
                        byte[] bytes; string type, disposition = "", status = "200 OK";
                        if (first.Contains(" /rate-limit ")) { bytes = System.Text.Encoding.UTF8.GetBytes("403 Forbidden (Rate Limit Exceeded)"); type = "text/plain"; status = "403 Forbidden"; }
                        else if (first.Contains(" /limit-json ")) { bytes = System.Text.Encoding.UTF8.GetBytes("{\"message\":\"Rate limit exceeded\"}"); type = "application/json"; status = "429 Too Many Requests"; }
                        else if (first.Contains(" /denied ")) { bytes = System.Text.Encoding.UTF8.GetBytes("Forbidden"); type = "text/plain"; status = "403 Forbidden"; }
                        else if (!index && !cancel && !download) { bytes = new byte[0]; type = "text/plain"; }
                        else if (!verified || cancel)
                        {
                            bytes = System.Text.Encoding.UTF8.GetBytes("<!doctype html><html><body><h2>Local verification fixture</h2><button id='verify' onclick=\"document.cookie='verified=yes; path=/; max-age=3600';location.reload()\">Verify test session</button></body></html>"); type = "text/html";
                        }
                        else if (index) { bytes = metadata; type = "application/json"; }
                        else { bytes = payload; type = "application/zip"; disposition = "Content-Disposition: attachment; filename=ShotInk-portable.zip\r\n"; System.Threading.Interlocked.Increment(ref VerifiedDownloads); }
                        byte[] header = System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + bytes.Length + "\r\n" + disposition + "Connection: close\r\n\r\n");
                        stream.Write(header, 0, header.Length); stream.Write(bytes, 0, bytes.Length); stream.Flush();
                    }
                }
                catch (IOException) { }
                catch (System.Net.Sockets.SocketException) { }
                catch (ObjectDisposedException) { }
            }
            public void Dispose() { stopped = true; server.Stop(); worker.Wait(6000); }
        }
        private static void PinDrag(PinForm pin, Point localStart, Point delta)
        {
            Point target = pin.PointToScreen(localStart); target.Offset(delta);
            Mouse(pin, "OnMouseDown", MouseButtons.Left, localStart.X, localStart.Y);
            Point move = pin.PointToClient(target); Mouse(pin, "OnMouseMove", MouseButtons.Left, move.X, move.Y);
            Point release = pin.PointToClient(target); Mouse(pin, "OnMouseUp", MouseButtons.Left, release.X, release.Y);
        }
        private static void TestPinResize()
        {
            var original = Pattern(200, 100, 709); int originalPixel = original.GetPixel(30, 20).ToArgb();
            using (var pin = new PinForm(original) { StartPosition = FormStartPosition.Manual, Location = new Point(200, 160) })
            {
                pin.Show(); Application.DoEvents();
                foreach (var entry in new[] {
                    new { Point = new Point(0, 50), Cursor = Cursors.SizeWE }, new { Point = new Point(199, 50), Cursor = Cursors.SizeWE },
                    new { Point = new Point(100, 0), Cursor = Cursors.SizeNS }, new { Point = new Point(100, 99), Cursor = Cursors.SizeNS },
                    new { Point = new Point(0, 0), Cursor = Cursors.SizeNWSE }, new { Point = new Point(199, 99), Cursor = Cursors.SizeNWSE },
                    new { Point = new Point(199, 0), Cursor = Cursors.SizeNESW }, new { Point = new Point(0, 99), Cursor = Cursors.SizeNESW } })
                { Mouse(pin, "OnMouseMove", MouseButtons.None, entry.Point.X, entry.Point.Y); Assert(pin.Cursor == entry.Cursor, "Pin edge/corner cursor was incorrect at " + entry.Point); }
                Rectangle before = pin.Bounds;
                PinDrag(pin, new Point(100, 50), new Point(25, 20));
                Assert(pin.Size == before.Size && pin.Location == new Point(before.Left + 25, before.Top + 20), "Dragging the pin center resized it instead of moving it.");
                before = pin.Bounds; PinDrag(pin, new Point(199, 50), new Point(100, 0));
                Assert(pin.Width == 300 && pin.Height == 150 && pin.Left == before.Left, "Right-edge resize stretched the image or moved its anchored edge.");
                pin.ContextMenuStrip.Items[1].PerformClick(); before = pin.Bounds;
                PinDrag(pin, new Point(0, 50), new Point(-80, 0));
                Assert(pin.Width == 280 && pin.Height == 140 && pin.Right == before.Right, "Left-edge resize did not anchor the opposite side.");
                pin.ContextMenuStrip.Items[1].PerformClick(); before = pin.Bounds;
                PinDrag(pin, new Point(100, 0), new Point(0, -50));
                Assert(pin.Width == 300 && pin.Height == 150 && pin.Bottom == before.Bottom, "Top-edge resize did not preserve aspect ratio and bottom anchor.");
                pin.ContextMenuStrip.Items[1].PerformClick(); before = pin.Bounds;
                PinDrag(pin, new Point(0, 0), new Point(-60, -30));
                Assert(pin.Width == 260 && pin.Height == 130 && pin.Right == before.Right && pin.Bottom == before.Bottom, "Corner resize moved its opposite corner or changed aspect ratio.");
                before = pin.Bounds;
                Mouse(pin, "OnMouseDown", MouseButtons.Left, pin.Width - 1, 50); pin.Capture = false;
                Mouse(pin, "OnMouseMove", MouseButtons.None, pin.Width + 100, 50);
                Assert(pin.Bounds == before, "Losing mouse capture left resizing active.");
                MouseWheel(pin, -120); Assert(pin.Width < before.Width && Math.Abs(pin.Width - pin.Height * 2) <= 1, "Wheel zoom no longer preserved the image ratio.");
                pin.ContextMenuStrip.Items[1].PerformClick();
                Assert(pin.ClientSize == original.Size && Field<Bitmap>(pin, "image").GetPixel(30, 20).ToArgb() == originalPixel, "Scaling changed original pixels or broke Original Size.");
                Mouse(pin, "OnMouseMove", MouseButtons.None, 0, 0);
                using (var preview = new Bitmap(pin.Width, pin.Height)) { pin.DrawToBitmap(preview, pin.ClientRectangle); preview.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-pin-resize-options.png")); }
                pin.Close();
            }
        }
        private static void TestSettingsRecovery()
        {
            string root = Path.Combine(Path.GetTempPath(), "ShotInk-settings-recovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "settings.ini");
            var original = new AppSettings { SaveDirectory = Path.Combine(root, "自定义图片"), AskSaveLocation = false, CaptureShortcut = Keys.Control | Keys.Q };
            try
            {
                original.Save(path);
                using (var home = new MainForm(root, false))
                {
                    Assert(Field<AppSettings>(home, "settings").SaveDirectory == original.SaveDirectory, "Cold start changed the saved folder.");
                }
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var blocked = AppSettings.Load(path);
                    Assert(!blocked.CanSave && blocked.LoadWarning != null, "A read failure silently accepted default settings.");
                    bool rejected = false; try { blocked.Clone().Save(path); } catch (IOException) { rejected = true; }
                    Assert(rejected && !File.Exists(path + ".new"), "Unread defaults were allowed to replace a locked configuration.");
                    using (var home = new MainForm(root, false))
                    {
                        Field<AppSettings>(home, "settings").NotifyChanged();
                        typeof(MainForm).GetMethod("PersistSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(home, null);
                        Assert(Field<Label>(home, "notice").Text.Contains("保护原设置"), "Load failure was not shown or did not block autosave.");
                    }
                }
                Assert(AppSettings.Load(path).SaveDirectory == original.SaveDirectory, "A failed read overwrote the original folder.");
                var next = AppSettings.Load(path); next.SaveDirectory = Path.Combine(root, "新图片"); next.Save(path);
                Assert(AppSettings.Load(path + ".bak").SaveDirectory == original.SaveDirectory, "Saving did not retain the last good configuration.");
                File.WriteAllText(path, "save-directory=invalid-relative-path");
                var recovered = AppSettings.Load(path);
                Assert(recovered.CanSave && recovered.LoadWarning != null && recovered.SaveDirectory == original.SaveDirectory && !recovered.AskSaveLocation,
                    "Corrupt settings did not recover all preferences from backup.");
                recovered.Save(path);
                Assert(AppSettings.Load(path).SaveDirectory == original.SaveDirectory && AppSettings.Load(path + ".bak").SaveDirectory == original.SaveDirectory,
                    "Recovery replaced the backup with corrupt settings.");
                File.Delete(path);
                Assert(AppSettings.Load(path).SaveDirectory == original.SaveDirectory, "Missing settings did not recover the backup.");
                string profiles = Path.Combine(root, "profiles"), legacy = Path.Combine(profiles, "SnipasteLite"), current = Path.Combine(profiles, "ShotInk");
                Directory.CreateDirectory(legacy); Directory.CreateDirectory(current);
                original.Save(Path.Combine(legacy, "settings.ini"));
                Assert(MainForm.ResolveDataRoot(profiles) == legacy && AppSettings.Load(Path.Combine(MainForm.ResolveDataRoot(profiles), "settings.ini")).SaveDirectory == original.SaveDirectory,
                    "Update selected an empty profile instead of the user's preferences.");
                File.Move(Path.Combine(legacy, "settings.ini"), Path.Combine(legacy, "settings.ini.bak"));
                Assert(MainForm.ResolveDataRoot(profiles) == legacy, "Profile selection ignored a recoverable backup.");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void TestSettings()
        {
            string root = Path.Combine(Path.GetTempPath(), "ShotInk-settings-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "settings.ini"); File.WriteAllText(path, "save=0");
                var settings = AppSettings.Load(path);
                Assert(!settings.AutoHistory && settings.AskSaveLocation && !settings.StartWithWindows && settings.CaptureShortcut == Keys.F1, "Legacy settings were not migrated with defaults.");
                settings.SaveDirectory = Path.Combine(root, "图片=测试"); settings.AskSaveLocation = false;
                settings.CaptureShortcut = Keys.Control | Keys.Shift | Keys.F8; settings.PinShortcut = Keys.None;
                settings.StartWithWindows = true;
                settings.Tool(DrawTool.Pen).Ink = Color.Blue; settings.Tool(DrawTool.Pen).Width = 7;
                settings.Tool(DrawTool.Text).FontSize = 42; settings.Tool(DrawTool.Mosaic).BrushSize = 76;
                settings.Tool(DrawTool.Mosaic).MosaicEffect = MosaicEffect.GaussianBlur; settings.Tool(DrawTool.Mosaic).BlurRadius = 12;
                settings.Save(path); var restored = AppSettings.Load(path);
                Assert(!restored.AutoHistory && !restored.AskSaveLocation && restored.SaveDirectory == settings.SaveDirectory, "Saving lost history or directory preferences.");
                Assert(restored.CaptureShortcut == settings.CaptureShortcut && restored.PinShortcut == Keys.None, "Shortcut preferences were not persisted.");
                Assert(restored.StartWithWindows && restored.Clone().StartWithWindows, "Startup preference was not persisted or cloned.");
                Assert(restored.Tool(DrawTool.Pen).Ink.ToArgb() == Color.Blue.ToArgb() && restored.Tool(DrawTool.Pen).Width == 7 && restored.Tool(DrawTool.Text).FontSize == 42 && restored.Tool(DrawTool.Mosaic).BrushSize == 76, "Tool preferences were not restored independently.");
                Assert(restored.Tool(DrawTool.Mosaic).MosaicEffect == MosaicEffect.GaussianBlur && restored.Tool(DrawTool.Mosaic).BlurRadius == 12, "Gaussian effect preferences were not restored.");
                using (var dialog = new SettingsForm(restored, delegate { return null; }))
                {
                    Key(Option<ShortcutBox>(dialog, "hotkey-0"), Keys.Control | Keys.Alt | Keys.F9);
                    Option<CheckBox>(dialog, "ask-save-location").Checked = true;
                    Option<CheckBox>(dialog, "start-with-windows").Checked = false;
                    var candidate = dialog.ReadSettings();
                    Assert(candidate.CaptureShortcut == (Keys.Control | Keys.Alt | Keys.F9) && candidate.AskSaveLocation && !restored.AskSaveLocation, "Editing the dialog mutated settings before save.");
                    Assert(!candidate.StartWithWindows && restored.StartWithWindows, "Editing startup changed the saved setting before Save.");
                    Option<ShortcutBox>(dialog, "hotkey-1").ShortcutValue = candidate.CaptureShortcut;
                    bool rejected = false; try { dialog.ReadSettings(); } catch (ArgumentException) { rejected = true; }
                    Assert(rejected, "Duplicate global shortcuts were accepted.");
                }
                using (var home = new MainForm(root, false))
                {
                    var candidate = restored.Clone(); candidate.PinShortcut = Keys.F7;
                    Assert(home.ApplySettings(candidate) == null && AppSettings.Load(path).PinShortcut == Keys.F7, "Applying settings did not persist the new shortcuts.");
                    Field<CheckBox>(home, "autoSave").Checked = true;
                }
                restored = AppSettings.Load(path);
                Assert(restored.AutoHistory && restored.PinShortcut == Keys.F7 && restored.Tool(DrawTool.Pen).Width == 7, "History checkbox overwrote other settings.");
                using (var dialog = new SettingsForm(restored, delegate { return null; }))
                {
                    dialog.Show(); Application.DoEvents(); using (var bitmap = new Bitmap(dialog.ClientSize.Width, dialog.ClientSize.Height))
                    { dialog.DrawToBitmap(bitmap, dialog.ClientRectangle); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-settings-options.png")); }
                    dialog.Close();
                }
            }
            finally { Directory.Delete(root, true); }
        }
        private static void TestStartupRegistration()
        {
            string registryPath = @"Software\ShotInk.Tests\Startup-" + Guid.NewGuid().ToString("N");
            string directory = Path.Combine(Path.GetTempPath(), "ShotInk-startup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using (var root = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registryPath))
                {
                    var registration = new StartupRegistration(root, "Run", @"E:\截图工具 文件夹\ShotInk.exe");
                    Assert(!registration.IsEnabled, "Startup was enabled by default.");
                    using (var key = root.CreateSubKey("Run")) key.SetValue("OtherApp", "keep");
                    registration.Apply(true, delegate { });
                    using (var key = root.OpenSubKey("Run"))
                        Assert((string)key.GetValue("ShotInk") == "\"E:\\截图工具 文件夹\\ShotInk.exe\" --startup", "Startup path was not quoted or did not request tray startup.");
                    var moved = new StartupRegistration(root, "Run", @"E:\新位置\ShotInk.exe");
                    bool failed = false;
                    try { moved.Apply(true, delegate { throw new IOException("save failed"); }); } catch (IOException) { failed = true; }
                    using (var key = root.OpenSubKey("Run"))
                        Assert(failed && (string)key.GetValue("ShotInk") == "\"E:\\截图工具 文件夹\\ShotInk.exe\" --startup", "Failed save did not restore the previous startup path.");
                    registration.Apply(false, delegate { });
                    using (var key = root.OpenSubKey("Run")) Assert(key.GetValue("ShotInk") == null && (string)key.GetValue("OtherApp") == "keep", "Disabling startup changed another application.");
                    using (var home = new MainForm(directory, false, false, registration))
                    {
                        var candidate = Field<AppSettings>(home, "settings").Clone(); candidate.StartWithWindows = true;
                        Assert(home.ApplySettings(candidate) == null && registration.IsEnabled && AppSettings.Load(Path.Combine(directory, "settings.ini")).StartWithWindows,
                            "Saving settings did not enable and persist startup.");
                        string blocker = Path.Combine(directory, "settings.ini.new"); Directory.CreateDirectory(blocker);
                        try
                        {
                            candidate.StartWithWindows = false;
                            Assert(home.ApplySettings(candidate) != null && registration.IsEnabled && Field<AppSettings>(home, "settings").StartWithWindows,
                                "A failed settings save left startup disabled or changed live settings.");
                        }
                        finally { Directory.Delete(blocker); }
                        Assert(home.ApplySettings(candidate) == null && !registration.IsEnabled, "Saving settings did not disable startup.");
                    }
                    // The actual OS entry is authoritative if preferences and Windows diverge.
                    var saved = AppSettings.Load(Path.Combine(directory, "settings.ini")); saved.StartWithWindows = true; saved.Save(Path.Combine(directory, "settings.ini"));
                    using (var home = new MainForm(directory, false, false, registration))
                        Assert(!Field<AppSettings>(home, "settings").StartWithWindows, "The UI ignored the missing Windows startup entry.");
                }
            }
            finally
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryPath, false);
                Directory.Delete(directory, true);
            }
        }
        private static void TestStartupTray()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ShotInk-tray-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var home = new MainForm(directory, false, true))
                {
                    int shown = 0; home.VisibleChanged += delegate { if (home.Visible) shown++; };
                    home.Show(); Application.DoEvents();
                    Assert(!home.Visible && home.IsHandleCreated && shown == 0, "Startup briefly displayed the workbench or failed to create its message window.");
                    home.Show(); Application.DoEvents(); Assert(home.Visible && shown == 1, "The workbench could not open after tray startup.");
                    home.Close();
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
        private static void WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (!condition() && DateTime.UtcNow < deadline) { Application.DoEvents(); System.Threading.Thread.Sleep(2); }
            Assert(condition(), "Asynchronous operation timed out.");
        }
        private static void TestHistoryLimit()
        {
            string root = Path.Combine(Path.GetTempPath(), "ShotInk-history-" + Guid.NewGuid().ToString("N"));
            string captures = Path.Combine(root, "captures"); Directory.CreateDirectory(captures);
            try
            {
                using (var image = new Bitmap(2, 2))
                {
                    for (int i = 0; i < 15; i++)
                    {
                        string path = Path.Combine(captures, "capture-" + i + ".png"); image.Save(path, ImageFormat.Png);
                        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-i));
                    }
                    image.Save(Path.Combine(root, "manual.png"), ImageFormat.Png);
                }
                using (var home = new MainForm(root, false))
                {
                    var number = Field<NumericUpDown>(home, "historyLimit");
                    Assert(number.Value == 10 && Field<ListView>(home, "history").Items.Count == 10, "History did not default to ten.");
                    number.Value = 3;
                    Assert(Directory.GetFiles(captures, "*.png").Length == 3 && Field<ListView>(home, "history").Items.Count == 3, "Changing the count did not apply immediately.");
                    Assert(File.Exists(Path.Combine(root, "manual.png")) && File.Exists(Path.Combine(captures, "capture-0.png")), "History pruning removed manual or newest images.");
                    home.Show(); Application.DoEvents();
                    using (var preview = new Bitmap(home.Width, home.Height)) { home.DrawToBitmap(preview, home.ClientRectangle); preview.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-history-options.png")); }
                    home.Close();
                }
                Assert(AppSettings.Load(Path.Combine(root, "settings.ini")).HistoryLimit == 3, "History count was not persisted.");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void TestToolRendering()
        {
            using (var host = new Form { BackColor = Color.FromArgb(40, 50, 70), ClientSize = new Size(700, 250) })
            using (var toolbar = new CaptureToolbar { Location = new Point(20, 20) })
            {
                host.Controls.Add(toolbar); host.Show(); Application.DoEvents();
                using (var bitmap = new Bitmap(toolbar.Width, toolbar.Height))
                {
                    toolbar.DrawToBitmap(bitmap, toolbar.ClientRectangle);
                    Assert(bitmap.GetPixel(0, 0).ToArgb() == host.BackColor.ToArgb(), "Toolbar corner was cut out or painted as a white square.");
                    var blends = new System.Collections.Generic.HashSet<int>();
                    for (int y = 0; y < 6; y++) for (int x = 0; x < 11; x++)
                    { var color = bitmap.GetPixel(x, y); if (color.R > 40 && color.R < 225) blends.Add(color.ToArgb()); }
                    Assert(blends.Count >= 6, "Toolbar corner did not retain graduated edge coverage.");
                }
                using (var swatch = new ToolOptionButton { Size = new Size(24, 26), IsSwatch = true, BackColor = Color.Red, Selected = true })
                using (var panel = new Panel { BackColor = Color.White })
                using (var bitmap = new Bitmap(24, 26))
                {
                    panel.Controls.Add(swatch); swatch.DrawToBitmap(bitmap, swatch.ClientRectangle);
                    Assert(bitmap.GetPixel(12, 13).R > 240 && bitmap.GetPixel(12, 13).G < 10, "Smoothing changed the swatch's center color.");
                    var blends = new System.Collections.Generic.HashSet<int>();
                    for (int y = 3; y < 23; y++) for (int x = 2; x < 22; x++)
                    { var color = bitmap.GetPixel(x, y); if (color.R > color.G + 30 && color.G > 0 && color.G < 220) blends.Add(color.ToArgb()); }
                    Assert(blends.Count >= 8, "Color circle had no graduated edge pixels.");
                }
                host.Close();
            }
        }
        private static GifClip GifFixture()
        {
            var clip = new GifClip { Duration = 400 }; int index = 0;
            foreach (var color in new[] { Color.Red, Color.Lime, Color.Blue })
            using (var image = new Bitmap(64, 48)) using (var stream = new MemoryStream())
            { using (var graphics = Graphics.FromImage(image)) graphics.Clear(color); image.Save(stream, ImageFormat.Png); clip.Frames.Add(new GifFrame { Png = stream.ToArray(), Time = new[] { 0, 80, 230 }[index++] }); }
            return clip;
        }
        private static void TestGifEncoding()
        {
            byte[] bytes = GifEncoder.Encode(GifFixture(), 1, 2);
            using (var stream = new MemoryStream(bytes)) using (var image = Image.FromStream(stream))
            {
                Assert(image.Width == 64 && image.Height == 48 && image.GetFrameCount(FrameDimension.Time) == 2, "GIF trim exported incorrect dimensions or frame count.");
                image.SelectActiveFrame(FrameDimension.Time, 0); using (var bitmap = new Bitmap(image)) Assert(bitmap.GetPixel(30, 20).G > 220 && bitmap.GetPixel(30, 20).R < 40, "GIF lost its first selected frame or palette.");
                image.SelectActiveFrame(FrameDimension.Time, 1); using (var bitmap = new Bitmap(image)) Assert(bitmap.GetPixel(30, 20).B > 220 && bitmap.GetPixel(30, 20).G < 40, "GIF lost its second frame or local palette.");
                var delays = image.GetPropertyItem(0x5100).Value;
                Assert(BitConverter.ToInt32(delays, 0) == 15 && BitConverter.ToInt32(delays, 4) == 17, "GIF timing did not preserve the trimmed interval.");
                Assert(BitConverter.ToUInt16(image.GetPropertyItem(0x5101).Value, 0) == 0, "GIF did not loop.");
            }
            string directory = Path.Combine(Path.GetTempPath(), "ShotInk-gif-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = GifExport.WriteNew(bytes, directory); var data = GifExport.ClipboardData(bytes, path);
                Assert(data.GetFileDropList()[0] == path && data.GetData("GIF") is MemoryStream && File.ReadAllBytes(path).Length == bytes.Length, "GIF clipboard did not retain an animated file and GIF data.");
                Assert(GifExport.Save(null, bytes, new AppSettings { AskSaveLocation = false, SaveDirectory = directory }) && Directory.GetFiles(directory, "*.gif").Length == 2, "GIF save ignored configured output or overwrote a file.");
                byte[] single = GifEncoder.Encode(GifFixture(), 2, 2);
                using (var stream = new MemoryStream(single)) using (var image = Image.FromStream(stream)) Assert(image.GetFrameCount(FrameDimension.Time) == 1, "Single-frame trim was invalid.");
            }
            finally { Directory.Delete(directory, true); }
        }
        private static void GifKey(GifEditorForm editor, Keys key)
        { typeof(GifEditorForm).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(editor, new object[] { new Message(), key }); }
        private static void TestGifEditor()
        {
            using (var editor = new GifEditorForm(GifFixture(), new AppSettings()))
            {
                editor.Show(); Application.DoEvents(); var range = Field<GifRangeSlider>(editor, "range");
                Assert(Field<Label>(editor, "status").Text == "" && !Field<Label>(editor, "status").Visible, "GIF editor retained the redundant drag instruction.");
                Field<PictureBox>(editor, "preview").Focus(); GifKey(editor, Keys.Right);
                Assert(Field<PictureBox>(editor, "preview").Focused && Field<int>(editor, "frameIndex") == 1 && range.First == 0 && range.Last == 2, "Right arrow did not seek exactly one frame without trimming.");
                GifKey(editor, Keys.Left); Assert(Field<int>(editor, "frameIndex") == 0, "Left arrow did not seek exactly one frame.");
                range.SelectBoundary(true); GifKey(editor, Keys.Right); Assert(range.First == 1 && range.Last == 2, "Start handle did not trim one frame independently.");
                range.SelectBoundary(false); GifKey(editor, Keys.Left); GifKey(editor, Keys.Left);
                Assert(range.Last == 1 && range.First == 1, "End handle crossed start or changed more than one frame.");
                range.SetRange(0, 2);
                Mouse(range, "OnMouseDown", MouseButtons.Left, 12, range.Height / 2);
                Mouse(range, "OnMouseMove", MouseButtons.Left, range.Width / 2, range.Height / 2);
                Mouse(range, "OnMouseUp", MouseButtons.Left, range.Width / 2, range.Height / 2);
                Assert(range.First == 1 && range.Last == 2, "Dragging the shared timeline's start handle changed the wrong endpoint.");
                Mouse(range, "OnMouseDown", MouseButtons.Left, range.Width - 12, range.Height / 2);
                Mouse(range, "OnMouseMove", MouseButtons.Left, 0, range.Height / 2);
                Mouse(range, "OnMouseUp", MouseButtons.Left, 0, range.Height / 2);
                Assert(range.First == 1 && range.Last == 1, "Dragging the end handle crossed the start handle.");
                range.SetRange(0, 2);
                Field<Button>(editor, "play").PerformClick(); WaitUntil(delegate { return Field<int>(editor, "frameIndex") == 1; });
                Field<PictureBox>(editor, "preview").Focus(); GifKey(editor, Keys.Right);
                Assert(!Field<System.Windows.Forms.Timer>(editor, "playback").Enabled && Field<int>(editor, "frameIndex") == 2, "Frame seek did not pause playback.");
                using (var image = new Bitmap(editor.Width, editor.Height)) { editor.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-gif-editor-options.png")); }
                editor.Close();
            }
        }
        private static void TestGifDeletion()
        {
            var clip = GifFixture(); clip.DeleteRange(1, 1);
            Assert(clip.Frames.Count == 2 && clip.Frames[0].Time == 0 && clip.Frames[1].Time == 80 && clip.Duration == 250, "Deleting the middle interval retained a time gap or changed surviving frame delays.");
            byte[] bytes = GifEncoder.Encode(clip, 0, 1);
            using (var stream = new MemoryStream(bytes)) using (var image = Image.FromStream(stream))
            {
                Assert(image.GetFrameCount(FrameDimension.Time) == 2, "Deleted frames remained in the exported GIF.");
                image.SelectActiveFrame(FrameDimension.Time, 0); using (var bitmap = new Bitmap(image)) Assert(bitmap.GetPixel(30, 20).R > 220, "Deletion changed the first surviving frame.");
                image.SelectActiveFrame(FrameDimension.Time, 1); using (var bitmap = new Bitmap(image)) Assert(bitmap.GetPixel(30, 20).B > 220, "Deletion changed the last surviving frame.");
                var delays = image.GetPropertyItem(0x5100).Value;
                Assert(BitConverter.ToInt32(delays, 0) == 8 && BitConverter.ToInt32(delays, 4) == 17, "Export did not preserve surviving frame durations.");
            }
            var prefix = GifFixture(); prefix.DeleteRange(0, 0);
            Assert(prefix.Frames.Count == 2 && prefix.Frames[0].Time == 0 && prefix.Frames[1].Time == 150 && prefix.Duration == 320, "Deleting initial frames did not rebase the timeline.");
            var suffix = GifFixture(); suffix.DeleteRange(2, 2);
            Assert(suffix.Frames.Count == 2 && suffix.Duration == 230 && suffix.EndTime(1) == 230, "Deleting final frames changed the previous frame duration.");
            clip = GifFixture();
            using (var editor = new GifEditorForm(clip, new AppSettings()))
            {
                editor.Show(); Application.DoEvents(); var range = Field<GifRangeSlider>(editor, "range"); var delete = Field<Button>(editor, "delete");
                Assert(delete.Text == "删除选中" && delete.ForeColor.ToArgb() == Color.Firebrick.ToArgb(), "Delete selection did not have red text.");
                range.SetRange(1, 1);
                typeof(GifEditorForm).GetField("encoded", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(editor, bytes);
                Field<Button>(editor, "play").PerformClick(); delete.PerformClick();
                Assert(clip.Frames.Count == 2 && range.Maximum == 1 && range.First == 0 && range.Last == 1 && range.EditingStart, "Deletion did not reset both range handles and their maximum.");
                Assert(!Field<System.Windows.Forms.Timer>(editor, "playback").Enabled && Field<int>(editor, "frameIndex") == 0 && Field<byte[]>(editor, "encoded") == null, "Deletion did not stop playback, reset preview and invalidate generated GIF data.");
                GifKey(editor, Keys.Right); Assert(Field<int>(editor, "frameIndex") == 1, "Per-frame seeking broke after deletion.");
                range.SetRange(1, 1); delete.PerformClick();
                Assert(clip.Frames.Count == 1 && clip.Duration == 80 && range.Maximum == 0 && range.First == 0 && range.Last == 0 && Field<Button>(editor, "save").Enabled, "Repeated deletion did not preserve a valid single-frame clip.");
                delete.PerformClick();
                Assert(clip.Frames.Count == 0 && clip.Duration == 0 && Field<PictureBox>(editor, "preview").Image == null && !delete.Enabled && !Field<Button>(editor, "play").Enabled && !Field<Button>(editor, "copy").Enabled && !Field<Button>(editor, "save").Enabled && !range.Enabled, "Deleting all frames left invalid playback or export controls enabled.");
                GifKey(editor, Keys.Left); GifKey(editor, Keys.Right); GifKey(editor, Keys.Control | Keys.C); GifKey(editor, Keys.Control | Keys.S);
                Assert(Field<Label>(editor, "position").Text == "暂无帧", "Empty preview did not remain safe for keyboard input."); editor.Close();
            }
        }
        private static void TestGifSampling()
        {
            int calls = 0;
            using (var session = new GifCaptureSession(delegate { System.Threading.Interlocked.Increment(ref calls); return new Bitmap(40, 30); }))
            {
                var task = session.RunAsync(); WaitUntil(delegate { return calls >= 3; }); session.Finish(); Pump(task);
                Assert(task.Result.Frames.Count >= 3 && task.Result.Duration > task.Result.Frames[task.Result.Frames.Count - 1].Time, "Recording lost frames or final duration.");
            }
            calls = 0;
            using (var session = new GifCaptureSession(delegate { System.Threading.Interlocked.Increment(ref calls); return new Bitmap(40, 30); }))
            { var task = session.RunAsync(); WaitUntil(delegate { return calls > 0; }); session.Dispose(); WaitUntil(delegate { return task.IsCompleted; }); Assert(task.IsCanceled, "Cancelled GIF capture continued running."); }
        }
        private static void TestToolbarDrag()
        {
            Point cursor = Cursor.Position;
            try
            {
                using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(20, 20, 900, 600)))
                {
                    overlay.Show(); Select(overlay); var toolbar = Field<CaptureToolbar>(overlay, "toolbar"); var selection = Field<Rectangle>(overlay, "selection");
                    var strip = Field<ToolStrip>(toolbar, "strip"); var gif = Field<ToolStripButton>(toolbar, "gif");
                    Assert(strip.Items.IndexOf(gif) == strip.Items.IndexOf(Field<ToolStripButton>(toolbar, "scroll")) + 1, "GIF button was not after long capture.");
                    Assert(toolbar.Height == 30, "Toolbar was not compact.");
                    var grip = Field<Control>(toolbar, "dragGrip"); var before = toolbar.Location;
                    Mouse(grip, "OnMouseDown", MouseButtons.Left, 4, 4);
                    Cursor.Position = new Point(cursor.X + 30, cursor.Y - 50); Mouse(grip, "OnMouseMove", MouseButtons.Left, 30, 0); Mouse(grip, "OnMouseUp", MouseButtons.Left, 30, 0);
                    Assert(toolbar.UserPlaced && toolbar.Location != before && Field<Rectangle>(overlay, "selection") == selection, "Dragging the toolbar moved the screenshot or failed to move.");
                    var moved = toolbar.Location; toolbar.SelectTool(DrawTool.Pen);
                    Assert(toolbar.Location == moved, "Changing tools reset the dragged toolbar location.");
                    ToolPreview(overlay, "compact-toolbar");
                    Application.DoEvents(); Native.FlushDesktop();
                    using (var screen = Native.Capture(overlay.RectangleToScreen(overlay.ClientRectangle))) screen.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-compact-toolbar-live.png"));
                    using (var image = new Bitmap(toolbar.Width, toolbar.Height)) { toolbar.DrawToBitmap(image, toolbar.ClientRectangle); image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-toolbar-only.png")); }
                    overlay.Close();
                }
            }
            finally { Cursor.Position = cursor; }
        }
        private static void TestIntegratedGif()
        {
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(20, 20, 900, 600)))
            {
                int frames = 0; Rectangle sampled = Rectangle.Empty;
                overlay.CaptureGifFrame = delegate(Rectangle region) { sampled = region; System.Threading.Interlocked.Increment(ref frames); return new Bitmap(700, 370); };
                overlay.Show(); Select(overlay); var toolbar = Field<CaptureToolbar>(overlay, "toolbar");
                var task = overlay.CaptureGifAsync(); WaitUntil(delegate { return frames >= 3; });
                Assert(!Field<AnnotationCanvas>(overlay, "canvas").Visible && Option<Button>(toolbar, "finish-scroll-capture").Text.StartsWith("停止"), "Recording did not hide the frozen screenshot or show Stop.");
                Option<Button>(toolbar, "finish-scroll-capture").PerformClick();
                Pump(task);
                GifEditorForm editor = null; foreach (Form form in Application.OpenForms) if (form is GifEditorForm) editor = (GifEditorForm)form;
                Assert(overlay.IsDisposed && editor != null && sampled == new Rectangle(100, 90, 700, 370), "Stopping GIF did not preserve the capture region or open trim editing.");
                editor.Close();
            }
        }
        private static void TestScrollPipeline()
        {
            using (var content = Pattern(100, 760, 221))
            {
                int offset = 0, calls = 0, thread = 0, ui = System.Threading.Thread.CurrentThread.ManagedThreadId;
                using (var session = new ScrollCaptureSession(delegate
                {
                    System.Threading.Interlocked.CompareExchange(ref thread, System.Threading.Thread.CurrentThread.ManagedThreadId, 0);
                    System.Threading.Interlocked.Increment(ref calls);
                    return content.Clone(new Rectangle(0, System.Threading.Volatile.Read(ref offset), 100, 200), PixelFormat.Format24bppRgb);
                }))
                {
                    var task = session.RunAsync(); WaitUntil(delegate { return session.Height == 200 && calls >= 5; });
                    Assert(!task.IsCompleted && session.Frames == 1 && thread != ui, "Manual capture stopped early, duplicated static frames or ran capture on the UI thread.");
                    foreach (int position in new[] { 35, 80, 120, 180, 220, 300, 360 })
                    { System.Threading.Volatile.Write(ref offset, position); WaitUntil(delegate { return session.Height == 200 + position; }); }
                    session.Finish(); Pump(task);
                    using (var result = task.Result)
                    { Assert(!result.LimitReached && result.Frames == 8, "Scroll frames were duplicated or lost."); EqualPixels(content, result.Image, 560); }
                    Assert(session.PeakPendingFrames <= ScrollCaptureSession.QueueCapacity && ScrollCaptureSession.PollInterval == 10, "Capture cadence or queue bound is incorrect.");
                }
            }
        }
        private static void TestScrollReversals()
        {
            using (var content = Pattern(320, 1800, 519))
            using (var stitcher = new VerticalStitcher())
            {
                int origin = 200, previous = origin, furthest = origin, earliest = origin;
                foreach (int top in new[] { 200, 280, 390, 310, 230, 150, 90, 170, 250, 350, 420, 510, 400, 300, 400, 500, 590, 680 })
                using (var frame = content.Clone(new Rectangle(0, top, 320, 300), PixelFormat.Format24bppRgb))
                {
                    var status = stitcher.Add(frame, Math.Sign(top - previous));
                    Assert(status != StitchStatus.Unmatched, "A valid reverse scroll lost alignment at " + top);
                    furthest = Math.Max(furthest, top);
                    earliest = Math.Min(earliest, top);
                    Assert(stitcher.Height == 300 + furthest - earliest, "Bidirectional capture lost new rows or duplicated revisited rows at " + top);
                    previous = top;
                }
                using (var expected = content.Clone(new Rectangle(0, earliest, 320, furthest - earliest + 300), PixelFormat.Format24bppRgb))
                using (var actual = stitcher.Build()) EqualPixels(expected, actual, expected.Height);
            }
            using (var content = Pattern(100, 1100, 526))
            {
                int top = 0;
                using (var session = new ScrollCaptureSession(delegate { return content.Clone(new Rectangle(0, System.Threading.Volatile.Read(ref top), 100, 240), PixelFormat.Format24bppRgb); }))
                {
                    var task = session.RunAsync(); WaitUntil(delegate { return session.Height == 240; });
                    int previous = 0, furthest = 0;
                    foreach (int next in new[] { 80, 160, 240, 160, 80, 160, 240, 320, 400 })
                    {
                        int version = session.ProcessedVersion;
                        session.NotifyWheel(next > previous ? -120 : 120);
                        System.Threading.Volatile.Write(ref top, next);
                        WaitUntil(delegate { return session.ProcessedVersion > version && session.IsCaughtUp; });
                        furthest = Math.Max(furthest, next);
                        Assert(!session.AlignmentLost && session.Height == 240 + furthest, "Asynchronous reversal duplicated content or lost alignment.");
                        previous = next;
                    }
                    session.Finish(); Pump(task);
                    using (var result = task.Result) EqualPixels(content, result.Image, 640);
                }
            }
        }
        private static void TestScrollBackPressure()
        {
            using (var content = Pattern(100, 960, 225))
            {
                int captures = 0;
                using (var session = new ScrollCaptureSession(delegate
                {
                    int index = Math.Min(19, System.Threading.Interlocked.Increment(ref captures) - 1);
                    return content.Clone(new Rectangle(0, index * 40, 100, 200), PixelFormat.Format24bppRgb);
                }))
                {
                    session.BeforeStitch = delegate { System.Threading.Thread.Sleep(45); };
                    var task = session.RunAsync(); WaitUntil(delegate { return captures >= 20; }); session.Finish(); Pump(task);
                    using (var result = task.Result)
                    {
                        Assert(result.Frames == 20 && !result.AlignmentLost, "Overload dropped an intermediate overlap frame.");
                        EqualPixels(content, result.Image, content.Height);
                    }
                    Assert(session.PeakPendingFrames > 2 && session.PeakPendingFrames <= ScrollCaptureSession.QueueCapacity, "The pressure test did not fill the bounded FIFO queue.");
                }
            }
        }
        private static void TestScrollLimitAndCancel()
        {
            using (var content = Pattern(100, 640, 222))
            {
                foreach (bool pixels in new[] { false, true })
                {
                    int offset = 0;
                    using (var session = new ScrollCaptureSession(delegate { return content.Clone(new Rectangle(0, System.Threading.Volatile.Read(ref offset), 100, 240), PixelFormat.Format24bppRgb); },
                        pixels ? VerticalStitcher.MaxHeight : 400, pixels ? 40000 : VerticalStitcher.MaxPixels))
                    {
                        var task = session.RunAsync(); WaitUntil(delegate { return session.Height == 240; });
                        System.Threading.Volatile.Write(ref offset, 100); WaitUntil(delegate { return session.Height == 340; });
                        System.Threading.Volatile.Write(ref offset, 160); Pump(task);
                        using (var result = task.Result) { Assert(result.LimitReached && result.Image.Height == 400, "Length/pixel limit did not auto-complete."); EqualPixels(content, result.Image, 400); }
                    }
                }
                using (var session = new ScrollCaptureSession(delegate { return content.Clone(new Rectangle(0, 0, 100, 240), PixelFormat.Format24bppRgb); }))
                {
                    var task = session.RunAsync(); WaitUntil(delegate { return session.Frames == 1; }); session.Dispose();
                    WaitUntil(delegate { return task.IsCompleted; }); Assert(task.IsCanceled, "Cancellation left background capture running.");
                }
                using (var session = new ScrollCaptureSession(delegate { throw new InvalidOperationException("capture failed"); }))
                {
                    var task = session.RunAsync(); WaitUntil(delegate { return task.IsCompleted; });
                    Assert(task.IsFaulted && task.Exception.InnerException.Message == "capture failed", "A failed frame source hung or hid its error.");
                }
            }
        }
        private static void TestIntegratedScroll()
        {
            using (var content = Pattern(700, 900, 223))
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(20, 20, 900, 600)))
            {
                int offset = 0, hovering = 0; Rectangle sampled = Rectangle.Empty;
                overlay.InterceptScrollWheel = false;
                overlay.CaptureScrollFrame = delegate(Rectangle region)
                {
                    sampled = region;
                    var frame = content.Clone(new Rectangle(0, System.Threading.Volatile.Read(ref offset), 700, 370), PixelFormat.Format24bppRgb);
                    if (System.Threading.Volatile.Read(ref hovering) != 0)
                        using (var graphics = Graphics.FromImage(frame)) graphics.FillRectangle(Brushes.White, 40, 40, 250, 120);
                    return frame;
                };
                overlay.Show(); Select(overlay);
                var selection = Field<Rectangle>(overlay, "selection"); var toolbar = Field<CaptureToolbar>(overlay, "toolbar");
                var strip = Field<ToolStrip>(toolbar, "strip"); var recognize = Field<ToolStripButton>(toolbar, "recognize"); var scroll = Field<ToolStripButton>(toolbar, "scroll");
                Assert(strip.Items.IndexOf(scroll) == strip.Items.IndexOf(recognize) + 1, "Long capture is not beside OCR.");
                var task = overlay.CaptureLongAsync();
                WaitUntil(delegate { var session = Field<ScrollCaptureSession>(overlay, "scrollSession"); return session != null && session.Height == 370; });
                Assert(!strip.Visible && Option<Button>(toolbar, "finish-scroll-capture").Visible && !Field<AnnotationCanvas>(overlay, "canvas").Visible, "Capture mode exposed editing tools or the frozen canvas.");
                Assert(!overlay.Region.IsVisible(selection.Left + 50, selection.Top + 50), "The selected area did not pass mouse input through to the original window.");
                var sessionBeforeLeave = Field<ScrollCaptureSession>(overlay, "scrollSession");
                System.Threading.Volatile.Write(ref hovering, 1);
                WaitUntil(delegate { return sessionBeforeLeave.AlignmentLost && sessionBeforeLeave.StableMilliseconds >= 1200; });
                Assert(!task.IsCompleted && !sessionBeforeLeave.IsFinishing, "A mouse hover change automatically completed capture.");
                Mouse(overlay, "OnMouseMove", MouseButtons.None, selection.Right + 20, selection.Bottom + 20);
                Mouse(overlay, "OnMouseDown", MouseButtons.Left, selection.Right + 20, selection.Bottom + 20);
                Mouse(overlay, "OnMouseUp", MouseButtons.Left, selection.Right + 20, selection.Bottom + 20);
                Mouse(overlay, "OnMouseMove", MouseButtons.None, selection.Left + 50, selection.Top + 50);
                Assert(!task.IsCompleted && object.ReferenceEquals(Field<ScrollCaptureSession>(overlay, "scrollSession"), sessionBeforeLeave)
                    && Field<Rectangle>(overlay, "selection") == selection && Field<CaptureToolbar>(overlay, "toolbar") == toolbar,
                    "Leaving or clicking outside the selection reset the active scrolling session.");
                System.Threading.Volatile.Write(ref hovering, 0);
                System.Threading.Volatile.Write(ref offset, 150);
                WaitUntil(delegate { return Field<ScrollCaptureSession>(overlay, "scrollSession").Height == 520; });
                Assert(!sessionBeforeLeave.AlignmentLost && !task.IsCompleted, "Capture did not recover after the hover change ended.");
                Option<Button>(toolbar, "finish-scroll-capture").PerformClick();
                Console.WriteLine("Long finish: finishing="+sessionBeforeLeave.IsFinishing+" frames="+sessionBeforeLeave.Frames+" pending="+sessionBeforeLeave.PendingFrames+" task="+task.Status+" context="+(System.Threading.SynchronizationContext.Current == null ? "null" : System.Threading.SynchronizationContext.Current.GetType().Name));
                Pump(task);
                var canvas = Field<AnnotationCanvas>(overlay, "canvas"); var viewport = Field<Panel>(overlay, "longViewport");
                Assert(sampled == new Rectangle(100, 90, 700, 370) && viewport.Bounds == selection && canvas.Size == selection.Size, "Long capture changed the selected scope or expanded its visible frame.");
                Assert(strip.Visible && Field<ScrollCaptureSession>(overlay, "scrollSession") == null && canvas.Image.Height == 520, "Completion did not restore all tools and the stitched image.");
                Assert(!Field<bool>(overlay, "scrollEscapeRegistered"), "Completion left the temporary Escape shortcut registered.");
                MouseWheel(canvas, -120); Assert(canvas.DocumentOffsetY > 0, "Mouse wheel did not pan the long screenshot.");
                float position = canvas.DocumentOffsetY;
                toolbar.SelectTool(DrawTool.Rectangle); Gesture(canvas, new Point(20, 20), new Point(80, 80));
                Assert(Math.Abs(canvas.Marks[0].Points[0].Y - position - 20 / canvas.Zoom) < 1, "Scrolled annotation used viewport rather than image coordinates.");
                toolbar.SelectTool(DrawTool.Text); Mouse(canvas, "OnMouseDown", MouseButtons.Left, 220, 100);
                Field<InlineTextInput>(canvas, "textEditor").Text = "滚动后的文字"; canvas.CommitTextEdit();
                Assert(Math.Abs(canvas.Marks[1].Points[0].Y - position - 100 / canvas.Zoom) < 1, "Text input ignored the long-image offset.");
                ToolPreview(overlay, "long-preview");
                using (var image = canvas.Render()) Assert(image.Size == new Size(700, 520), "Export returned only the visible part of the long screenshot.");
                overlay.Close();
            }
            using (var content = Pattern(700, 370, 224))
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(20, 20, 900, 600)))
            {
                int exports = 0, cancellations = 0;
                overlay.InterceptScrollWheel = false;
                overlay.Completed += delegate(Bitmap image, CaptureAction action) { exports++; image.Dispose(); };
                overlay.Cancelled += delegate { cancellations++; };
                overlay.CaptureScrollFrame = delegate { return new Bitmap(content); };
                overlay.Show(); Select(overlay);
                var task = overlay.CaptureLongAsync();
                WaitUntil(delegate { var session = Field<ScrollCaptureSession>(overlay, "scrollSession"); return session != null && session.Frames == 1; });
                Assert(Field<bool>(overlay, "scrollEscapeRegistered"), "Capture did not register Escape while the underlying window had focus.");
                SendMessage(overlay.Handle, Native.HotKeyMessage, new IntPtr(1001), IntPtr.Zero); Pump(task);
                Assert(overlay.IsDisposed && cancellations == 1 && exports == 0, "Escape failed to cancel background capture without exporting.");
                using (var probe = new Form())
                {
                    bool available = Native.RegisterHotKey(probe.Handle, 1001, 0x4000, (uint)Keys.Escape);
                    if (available) Native.UnregisterHotKey(probe.Handle, 1001);
                    Assert(available, "Cancellation left the temporary Escape shortcut registered.");
                }
            }
        }
        private static void TestLongPreviewMove()
        {
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(20, 20, 900, 600)))
            {
                overlay.Show(); Select(overlay); overlay.ShowLongPreview(Pattern(700, 1200, 229));
                var canvas = Field<AnnotationCanvas>(overlay, "canvas"); var toolbar = Field<CaptureToolbar>(overlay, "toolbar");
                var viewport = Field<Panel>(overlay, "longViewport"); var original = Field<Rectangle>(overlay, "selection");
                Assert(canvas.Zoom == 1, "Scrollbars shrank the long preview and blurred the text.");
                canvas.ScrollDocument(91); canvas.ScrollDocumentHorizontal(11);
                using (var visible = new Bitmap(canvas.Width, canvas.Height))
                {
                    canvas.DrawToBitmap(visible, canvas.ClientRectangle);
                    for (int y = 0; y < 200; y++) for (int x = 0; x < 300; x++)
                        Assert(visible.GetPixel(x, y).ToArgb() == canvas.Image.GetPixel(x + 11, y + 91).ToArgb(), "Preview resampled the original pixels.");
                }
                var image = canvas.Image; int revision = canvas.Revision;
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, 40, 50);
                Assert(Field<bool>(overlay, "dragging"), "Clicking the long image did not begin moving its frame.");
                Assert(!viewport.Visible && Field<Bitmap>(overlay, "longDragPreview") != null, "Dragging moved a visible child window instead of painting one surface.");
                Mouse(overlay, "OnMouseMove", MouseButtons.Left, original.X + 95, original.Y + 95);
                Mouse(overlay, "OnMouseUp", MouseButtons.Left, original.X + 95, original.Y + 95);
                var moved = original; moved.Offset(55, 45);
                Assert(Field<Rectangle>(overlay, "selection") == moved && viewport.Bounds == moved && canvas.Location == Point.Empty, "The long-image frame did not move with the pointer.");
                Assert(canvas.Image == image && canvas.Revision == revision && canvas.DocumentOffsetY == 91 && canvas.DocumentOffsetX == 11, "Moving the frame recaptured the desktop or reset the document.");
                toolbar.SelectTool(DrawTool.Rectangle); Gesture(canvas, new Point(20, 20), new Point(80, 80));
                Assert(canvas.Marks[0].Points[0] == new PointF(31, 111), "Mark coordinates changed after moving the frame.");
                toolbar.SelectTool(DrawTool.None);
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, 40, 50);
                Mouse(overlay, "OnMouseMove", MouseButtons.Left, moved.X + 20, moved.Y + 20);
                overlay.Capture = false;
                Assert(Field<Rectangle>(overlay, "selection") == moved && viewport.Bounds == moved, "Interrupted dragging left the frame at a different position.");
                ToolPreview(overlay, "long-moved");
                using (var exported = canvas.Render()) Assert(exported.Size == new Size(700, 1200), "Moving the frame cropped the exported long image.");
                overlay.Close();
            }
        }
        private static void TestSettingsCaptureGuard()
        {
            string root = Path.Combine(Path.GetTempPath(), "ShotInk-settings-guard-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var home = new MainForm(root, false))
                {
                    var live = new System.Collections.Generic.Dictionary<int, Keys>();
                    var bindings = new HotkeyBindings(delegate(int id, Keys key) { live.Add(id, key); return true; }, delegate(int id) { live.Remove(id); });
                    home.Show(); bindings.RegisterInitial(new AppSettings().Shortcuts);
                    typeof(MainForm).GetField("hotkeys", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(home, bindings);
                    for (int round = 0; round < 2; round++)
                    {
                        bool save = round == 1;
                        home.BeginInvoke(new Action(delegate
                        {
                            SettingsForm dialog = null;
                            foreach (Form form in Application.OpenForms) if (form is SettingsForm) dialog = (SettingsForm)form;
                            Assert(dialog != null && Field<bool>(home, "settingsOpen") && live.Count == 0, "Settings did not release the global shortcut registrations.");
                            try
                            {
                                Key(Option<ShortcutBox>(dialog, "hotkey-0"), Keys.F1);
                                Assert(Option<ShortcutBox>(dialog, "hotkey-0").ShortcutValue == Keys.F1, "The existing capture shortcut could not be recorded.");
                                for (int id = 1; id <= 6; id++) SendMessage(home.Handle, Native.HotKeyMessage, new IntPtr(id), IntPtr.Zero);
                                typeof(MainForm).GetMethod("BeginCapture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(home, new object[] { false });
                                Assert(!Field<bool>(home, "captureBusy") && dialog.Visible, "Entering a shortcut or using a capture entry started a screenshot inside settings.");
                                Key(Option<ShortcutBox>(dialog, "hotkey-0"), Keys.F6);
                                if (save) Assert(home.ApplySettings(dialog.ReadSettings()) == null && live.Count == 0, "Saving settings resumed shortcuts before closing the dialog.");
                            }
                            finally { dialog.Close(); }
                        }));
                        typeof(MainForm).GetMethod("OpenSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(home, null);
                        Assert(!Field<bool>(home, "settingsOpen") && live.Count == new AppSettings().Shortcuts.Length && live[1] == (save ? Keys.F6 : Keys.F1), "Closing settings failed to restore the saved or cancelled shortcuts.");
                    }
                    home.Close();
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        private static void TestScrollGap()
        {
            using (var before = Pattern(700, 370, 227))
            using (var after = Pattern(700, 370, 228))
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(20, 20, 900, 600)))
            {
                int changed = 0;
                overlay.InterceptScrollWheel = false;
                overlay.CaptureScrollFrame = delegate { return new Bitmap(System.Threading.Volatile.Read(ref changed) == 0 ? before : after); };
                overlay.Show(); Select(overlay);
                var task = overlay.CaptureLongAsync();
                WaitUntil(delegate { var session = Field<ScrollCaptureSession>(overlay, "scrollSession"); return session != null && session.Frames == 1; });
                System.Threading.Volatile.Write(ref changed, 1);
                var captureSession = Field<ScrollCaptureSession>(overlay, "scrollSession");
                WaitUntil(delegate { return captureSession.AlignmentLost && captureSession.StableMilliseconds >= 1200; });
                Assert(!task.IsCompleted && !captureSession.IsFinishing, "A page change automatically completed capture.");
                Option<Button>(Field<CaptureToolbar>(overlay, "toolbar"), "finish-scroll-capture").PerformClick(); Pump(task);
                Assert(Field<Label>(overlay, "sizeHint").Text.Contains("页面变化过大"), "A missing overlap was silently exported as a complete capture.");
                using (var result = Field<AnnotationCanvas>(overlay, "canvas").Render()) EqualPixels(before, result, before.Height);
                overlay.Close();
            }
        }
        private static void TestHotkeyRollback()
        {
            var live = new System.Collections.Generic.Dictionary<int, Keys>(); Keys reserved = Keys.F12;
            var bindings = new HotkeyBindings(delegate(int id, Keys key)
            {
                if (key == reserved || live.ContainsValue(key)) return false;
                live.Add(id, key); return true;
            }, delegate(int id) { live.Remove(id); });
            var defaults = new AppSettings(); Assert(bindings.RegisterInitial(defaults.Shortcuts) == "", "Initial shortcuts failed.");
            string error; Assert(!bindings.TryApply(new[] { Keys.F5, reserved, Keys.F6 }, out error) && error.Contains("F12"), "Conflict was not reported.");
            Assert(live.Count == new AppSettings().Shortcuts.Length && live[1] == Keys.F1 && live[2] == Keys.F3 && live[3] == defaults.ScrollShortcut, "Conflict discarded the previous shortcuts.");
            Assert(bindings.TryApply(new[] { Keys.F3, Keys.F1, Keys.None }, out error) && live.Count == 2 && live[1] == Keys.F3 && live[2] == Keys.F1, "Swapping or clearing shortcuts failed.");
            Assert(!bindings.TryApply(new[] { Keys.F7, Keys.F8, Keys.F9 }, delegate { throw new IOException("disk unavailable"); }, out error) &&
                live.Count == 2 && live[1] == Keys.F3 && live[2] == Keys.F1, "Failed settings persistence left the new shortcuts active.");
            bindings.Clear(); Assert(live.Count == 0, "Disposal leaked hotkey registrations.");
        }
        private static void TestShortcutStatus()
        {
            bool occupied = true;
            var settings = new AppSettings { CaptureShortcut = Keys.Control | Keys.Q, ScrollShortcut = Keys.None };
            using (var dialog = new SettingsForm(settings, delegate { throw new Exception("Detection saved settings."); },
                delegate(Keys key) { return key != settings.CaptureShortcut || !occupied; }))
            {
                dialog.Show(); Application.DoEvents();
                Assert(Option<Label>(dialog, "hotkey-status-0").Text == "已被占用" && Option<Label>(dialog, "hotkey-status-2").Text == "未设置", "Initial shortcut status is incorrect.");
                occupied = false;
                WaitUntil(delegate { return Option<Label>(dialog, "hotkey-status-0").Text == "未占用"; });
                Option<ShortcutBox>(dialog, "hotkey-1").ShortcutValue = settings.CaptureShortcut;
                Assert(Option<Label>(dialog, "hotkey-status-0").Text == "本页重复" && Option<Label>(dialog, "hotkey-status-1").Text == "本页重复", "Duplicate shortcuts were reported as available.");
                Option<ShortcutBox>(dialog, "hotkey-1").ShortcutValue = Keys.A;
                Assert(Option<Label>(dialog, "hotkey-status-1").Text == "无效快捷键" && settings.PinShortcut == Keys.F3, "Invalid detection mutated saved settings.");
                dialog.Close();
            }
            using (var owner = new Form())
            using (var probe = new Form())
            {
                Keys key = Keys.None;
                for (Keys candidate = Keys.F13; candidate <= Keys.F24; candidate++)
                {
                    var attempt = Keys.Control | Keys.Shift | candidate;
                    if (Native.RegisterHotKey(owner.Handle, 20902, Shortcut.Modifiers(attempt), (uint)candidate)) { key = attempt; break; }
                }
                Assert(key != Keys.None, "No free test shortcut was found.");
                try { Assert(!Native.ShortcutAvailable(probe.Handle, key), "Native detection missed an occupied key."); }
                finally { Native.UnregisterHotKey(owner.Handle, 20902); }
                Assert(Native.ShortcutAvailable(probe.Handle, key) && Native.ShortcutAvailable(owner.Handle, key), "Detection did not release its temporary registration.");
            }
        }
        private static void TestHotkeyRecovery()
        {
            string root = Path.Combine(Path.GetTempPath(), "ShotInk-hotkey-recovery-" + Guid.NewGuid().ToString("N"));
            bool blocked = true; int unregisters = 0;
            var live = new System.Collections.Generic.Dictionary<int, Keys>();
            var bindings = new HotkeyBindings(delegate(int id, Keys key)
            { if (id == 1 && blocked) return false; live.Add(id, key); return true; }, delegate(int id) { unregisters++; live.Remove(id); });
            try
            {
                using (var home = new MainForm(root, false))
                {
                    home.Show();
                    var keys = new AppSettings().Shortcuts;
                    string conflicts = bindings.RegisterInitial(keys);
                    typeof(MainForm).GetField("hotkeys", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(home, bindings);
                    typeof(MainForm).GetMethod("UpdateHotkeyNotice", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(home, new object[] { conflicts });
                    Assert(live.Count == keys.Length - 1 && conflicts.Contains("F1"), "Initial conflict was not retained.");
                    blocked = false;
                    WaitUntil(delegate { return live.Count == keys.Length; });
                    Assert(unregisters == 0 && Field<Label>(home, "notice").Text.Contains("已自动恢复"), "Automatic recovery reset a healthy key or left stale status.");
                    Assert(bindings.RetryMissing(keys) == "" && unregisters == 0, "Recovery registered an active key twice.");
                    home.Close();
                }
                Assert(live.Count == 0, "Closing the application leaked hotkeys.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        private static void TestSavePreferences()
        {
            string root = Path.Combine(Path.GetTempPath(), "ShotInk-save-" + Guid.NewGuid().ToString("N"));
            var settings = new AppSettings { SaveDirectory = root, AskSaveLocation = false };
            try
            {
                using (var image = Pattern(120, 100, 22))
                {
                    string first, second = null; Assert(ImageExport.Save(null, image, settings, out first) && ImageExport.Save(null, image, settings, out second), "Direct save failed.");
                    Assert(first != second && Path.GetDirectoryName(first) == root, "Repeated saves overwrote an image or ignored the chosen folder.");
                    using (var saved = new Bitmap(first)) EqualPixels(image, saved, 100);
                }
                using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(0, 0, 900, 600), settings))
                {
                    int count = Directory.GetFiles(root, "*.png").Length; bool completed = false;
                    overlay.Completed += delegate(Bitmap image, CaptureAction action) { completed = action == CaptureAction.Save; image.Dispose(); };
                    overlay.Show(); Select(overlay); Key(overlay, Keys.Control | Keys.S);
                    Assert(completed && Directory.GetFiles(root, "*.png").Length == count + 1, "Screenshot save did not honor the no-prompt preference.");
                }
                using (var editor = new EditorForm(new Bitmap(120, 100), delegate(Bitmap image) { image.Dispose(); }, settings))
                {
                    int count = Directory.GetFiles(root, "*.png").Length;
                    Field<AnnotationCanvas>(editor, "canvas").AddMark(RectangleMark());
                    bool saved = (bool)typeof(EditorForm).GetMethod("SaveImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(editor, null);
                    Assert(saved && Directory.GetFiles(root, "*.png").Length == count + 1, "History/long-image editor ignored save preferences.");
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        private static void TestOcrHighlight()
        {
            var line = new OcrLine { Text = "ab cd", Box = new[] { 10F, 10F, 55F, 14F }, Characters = new[] {
                new OcrCharacter { Text = "a", Start = 0, Length = 1, Box = new[] { 10F, 10F, 9F, 10F } },
                new OcrCharacter { Text = "b", Start = 1, Length = 1, Box = new[] { 22F, 12F, 9F, 10F } },
                new OcrCharacter { Text = "c", Start = 3, Length = 1, Box = new[] { 44F, 11F, 9F, 10F } },
                new OcrCharacter { Text = "d", Start = 4, Length = 1, Box = new[] { 56F, 10F, 9F, 10F } } } };
            var next = new OcrLine { Text = "ok", Box = new[] { 75F, 10F, 20F, 14F } };
            var column = new OcrLine { Text = "列", Box = new[] { 140F, 10F, 20F, 14F } };
            var document = new OcrDocument { Width = 180, Height = 50, Lines = new[] { line, next, column } };
            var model = new OcrSelectionModel(document); model.SelectAll();
            using (var bitmap = new Bitmap(180, 50)) using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White); model.Draw(g); int expected = bitmap.GetPixel(14, 16).ToArgb();
                for (int x = 11; x < 95; x++) for (int y = 11; y < 24; y++) Assert(bitmap.GetPixel(x, y).ToArgb() == expected, "OCR highlight contains character/word gaps or overlapping alpha seams.");
                Assert(bitmap.GetPixel(110, 16).ToArgb() == Color.White.ToArgb(), "OCR highlight filled the gap between distant columns.");
            }
            using (var canvas = new AnnotationCanvas(new Bitmap(180, 50)))
            {
                using (var g = Graphics.FromImage(canvas.Image)) g.Clear(Color.White);
                canvas.SetTextRecognition(document); Assert(canvas.HasSelectedText, "Recognition did not select all text immediately.");
                using (var result = canvas.Render()) EqualPixels(canvas.Image, result, 50);
            }
        }
        private static void DragSelection(CaptureOverlay overlay, int steps)
        {
            var canvas = Field<AnnotationCanvas>(overlay, "canvas");
            Mouse(canvas, "OnMouseDown", MouseButtons.Left, 200, 100);
            for (int i = 1; i <= steps; i++) { Mouse(overlay, "OnMouseMove", MouseButtons.Left, 280 - 10 * i, 170 + 6 * i); overlay.Refresh(); Application.DoEvents(); }
        }
        private static void TestSelectionDrag()
        {
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(20, 20, 900, 600)))
            {
                overlay.Show(); Select(overlay); var canvas = Field<AnnotationCanvas>(overlay, "canvas"); canvas.AddMark(RectangleMark());
                int revision = canvas.Revision; var originalImage = canvas.Image;
                DragSelection(overlay, 5);
                Assert(!canvas.Visible && canvas.Revision == revision && object.ReferenceEquals(originalImage, canvas.Image), "Dragging rebuilt or moved a visible child canvas on each frame.");
                Mouse(overlay, "OnMouseUp", MouseButtons.Left, 230, 200);
                Assert(canvas.Visible && canvas.Revision == revision + 1 && canvas.Location == new Point(30, 100), "Drag completion did not reframe once at the final location.");
                var selection = Field<Rectangle>(overlay, "selection"); revision = canvas.Revision; originalImage = canvas.Image;
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, 100, 100); Mouse(overlay, "OnMouseMove", MouseButtons.Left, 170, 230); overlay.Capture = false;
                Assert(canvas.Visible && Field<Rectangle>(overlay, "selection") == selection && canvas.Revision == revision && object.ReferenceEquals(canvas.Image, originalImage), "Lost capture left a hidden canvas or changed the screenshot.");
                overlay.Close();
            }
        }
        private static void TestLiveDrag()
        {
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(20, 20, 900, 600)))
            {
                overlay.Show(); overlay.Activate(); Select(overlay); DragSelection(overlay, 5); overlay.Refresh(); Application.DoEvents();
                using (var screenshot = Native.Capture(overlay.Bounds))
                {
                    foreach (var point in new[] { new Point(79, 180), new Point(780, 180), new Point(400, 69) })
                    { var color = screenshot.GetPixel(point.X, point.Y); Assert(!(color.B > 180 && color.R < 100), "A previous selection border remained on screen."); }
                    screenshot.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-drag-options.png"));
                }
                Mouse(overlay, "OnMouseUp", MouseButtons.Left, 230, 200); overlay.Close();
            }
        }
        private static void PaintSelectionPreview(CaptureOverlay overlay, Bitmap target, Rectangle clip)
        {
            using (var graphics = Graphics.FromImage(target))
            {
                graphics.SetClip(clip);
                overlay.GetType().GetMethod("OnPaint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(overlay,
                    new object[] { new PaintEventArgs(graphics, clip) });
            }
        }
        private static void TestInitialSelectionPaint()
        {
            using (var overlay = new CaptureOverlay(false, Pattern(900, 600, 71), new Rectangle(20, 20, 900, 600)))
            using (var preview = new Bitmap(900, 600))
            {
                PaintSelectionPreview(overlay, preview, overlay.ClientRectangle);
                var cache = Field<Bitmap>(overlay, "shadedDesktop");
                Mouse(overlay, "OnMouseMove", MouseButtons.None, 420, 250);
                Mouse(overlay, "OnMouseDown", MouseButtons.Left, 450, 300);
                foreach (var point in new[] { new Point(780, 500), new Point(300, 140), new Point(460, 310), new Point(200, 100) })
                {
                    Mouse(overlay, "OnMouseMove", MouseButtons.Left, point.X, point.Y);
                    PaintSelectionPreview(overlay, preview, overlay.ClientRectangle);
                    var selection = Field<Rectangle>(overlay, "selection");
                    var sample = new Point(selection.Left + selection.Width / 2, selection.Top + selection.Height / 2);
                    Assert(preview.GetPixel(sample.X, sample.Y).ToArgb() == Field<Bitmap>(overlay, "desktop").GetPixel(sample.X, sample.Y).ToArgb(), "Selection preview altered the frozen image.");
                    Assert(object.ReferenceEquals(cache, Field<Bitmap>(overlay, "shadedDesktop")), "Pointer movement rebuilt the full-screen shade cache.");
                }
                Mouse(overlay, "OnMouseUp", MouseButtons.Left, 200, 100);
                Assert(Field<Rectangle>(overlay, "selection") == new Rectangle(200, 100, 250, 200), "Reverse-direction selection lost its final coordinates.");
            }
        }
        private static void TestLiveInitialSelection()
        {
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(20, 20, 900, 600)))
            using (var expected = new Bitmap(900, 600))
            {
                overlay.Show(); overlay.Activate(); Application.DoEvents();
                Native.RaiseWithoutActivation(overlay.Handle);
                try { WaitUntil(delegate { using (var initial = Native.Capture(overlay.Bounds)) return initial.GetPixel(10, 590).R > 130 && initial.GetPixel(10, 590).R < 170 && initial.GetPixel(20, 590).ToArgb() == initial.GetPixel(10, 590).ToArgb(); }); }
                catch
                {
                    using (var initial = Native.Capture(overlay.Bounds)) initial.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-initial-unmapped.png"));
                    Console.WriteLine("Preview handle=" + overlay.Handle + ", window at center=" + Native.RootWindow(Native.WindowAt(overlay.PointToScreen(new Point(400, 300)))) + ", bounds=" + overlay.Bounds + ", visible=" + overlay.Visible);
                    throw;
                }
                foreach (var point in new[] { new Point(80, 100), new Point(650, 450), new Point(880, 580), new Point(420, 240) })
                {
                    Mouse(overlay, "OnMouseMove", MouseButtons.None, point.X, point.Y);
                    CheckLivePreview(overlay, expected);
                }
                Mouse(overlay, "OnMouseDown", MouseButtons.Left, 450, 300);
                foreach (var point in new[] { new Point(850, 550), new Point(700, 480), new Point(300, 140), new Point(200, 100), new Point(460, 310) })
                {
                    Mouse(overlay, "OnMouseMove", MouseButtons.Left, point.X, point.Y);
                    CheckLivePreview(overlay, expected);
                }
                using (var screenshot = Native.Capture(overlay.Bounds)) screenshot.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-initial-selection-options.png"));
                Mouse(overlay, "OnMouseUp", MouseButtons.Left, 750, 500);
                overlay.Close();
            }
        }
        private static void CheckLivePreview(CaptureOverlay overlay, Bitmap expected)
        {
            // Do not force a full refresh: compare the real incremental frame against a fresh full render.
            Application.DoEvents(); Native.FlushDesktop();
            using (var actual = Native.Capture(overlay.Bounds))
            {
                PaintSelectionPreview(overlay, expected, overlay.ClientRectangle);
                try
                {
                    var hint = Field<bool>(overlay, "dragging") ? (Rectangle)overlay.GetType().GetMethod("DragHintBounds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(overlay,
                        new object[] { Field<Rectangle>(overlay, "selection") }) : Rectangle.Empty;
                    for (int y = 0; y < expected.Height; y++) for (int x = 0; x < expected.Width; x++)
                    {
                        var a = actual.GetPixel(x, y); var b = expected.GetPixel(x, y);
                        // GDI ClearType weights differ slightly between a screen DC and a bitmap DC.
                        // Only the current size label permits this small color difference; old labels and all borders must match exactly.
                        int tolerance = hint.Contains(x, y) ? 24 : 0;
                        Assert(Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance,
                            "Incremental selection left stale pixels at " + x + ", " + y);
                    }
                }
                catch
                {
                    Console.WriteLine("Initial preview pointer=" + Field<Point>(overlay, "pointer") + ", selection=" + Field<Rectangle>(overlay, "selection"));
                    expected.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-initial-expected.png"));
                    actual.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-initial-actual.png"));
                    throw;
                }
            }
        }
        private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static Bitmap Pattern(int width, int height, int seed)
        {
            var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            var random = new Random(seed);
            var data = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[Math.Abs(data.Stride)];
                for (int y = 0; y < height; y++) { random.NextBytes(row); System.Runtime.InteropServices.Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), row.Length); }
            }
            finally { result.UnlockBits(data); }
            return result;
        }
        private static void EqualPixels(Bitmap expected, Bitmap actual, int height)
        {
            Assert(actual.Width == expected.Width && actual.Height == height, "Incorrect output dimensions: expected " + expected.Width + " x " + height + ", got " + actual.Width + " x " + actual.Height + ".");
            for (int y = 0; y < height; y++)
                for (int x = 0; x < actual.Width; x++)
                    Assert(expected.GetPixel(x, y).ToArgb() == actual.GetPixel(x, y).ToArgb(), "Pixel mismatch at " + x + ", " + y);
        }
        private static void TestDocumentStitch()
        {
            using (var content = new Bitmap(1280, 1800, PixelFormat.Format24bppRgb))
            using (var font = new Font("Microsoft YaHei UI", 13))
            using (var stitcher = new VerticalStitcher())
            {
                using (var graphics = Graphics.FromImage(content))
                {
                    graphics.Clear(Color.White);
                    for (int row = 0; row < 55; row++)
                    {
                        int y = 15 + row * 32;
                        graphics.DrawString("第 " + row + " 行：截图页面内容 / Document " + (row * 731) + "  " + new string((char)('A' + row % 26), 8 + row % 19), font, Brushes.Black, 75, y);
                        if (row % 7 == 0) graphics.FillRectangle(Brushes.SteelBlue, 920, y, 100 + row, 15);
                    }
                }
                var clock = System.Diagnostics.Stopwatch.StartNew();
                foreach (int offset in new[] { 0, 45, 135, 250, 365, 480, 600, 720, 840, 960, 1080 })
                    using (var frame = content.Clone(new Rectangle(0, offset, 1280, 720), PixelFormat.Format24bppRgb))
                        Assert(stitcher.Add(frame) == StitchStatus.Added, "A text page failed to match at offset " + offset + ".");
                Console.WriteLine("Text-page matching: " + clock.ElapsedMilliseconds + " ms for 11 frames at 1280 x 720.");
                using (var result = stitcher.Build()) EqualPixels(content, result, content.Height);
                using (var upward = new VerticalStitcher())
                {
                    foreach (int offset in new[] { 600, 480, 360, 240, 120, 0, 120, 240, 360, 480, 600, 720, 840, 960, 1080 })
                    using (var frame = content.Clone(new Rectangle(0, offset, 1280, 720), PixelFormat.Format24bppRgb))
                        Assert(upward.Add(frame) != StitchStatus.Unmatched, "Upward text capture lost alignment at " + offset);
                    using (var result = upward.Build()) EqualPixels(content, result, content.Height);
                }
                using (var revisit = new VerticalStitcher())
                {
                    int furthest = 0;
                    foreach (int offset in new[] { 0, 120, 240, 120, 0, 120, 240, 360 })
                    using (var frame = content.Clone(new Rectangle(0, offset, 1280, 720), PixelFormat.Format24bppRgb))
                    {
                        Assert(revisit.Add(frame) != StitchStatus.Unmatched, "A text-page reversal lost alignment.");
                        furthest = Math.Max(furthest, offset);
                        Assert(revisit.Height == 720 + furthest, "A text-page reversal appended repeated lines.");
                    }
                    using (var result = revisit.Build()) EqualPixels(content, result, 1080);
                }
            }
            using (var content = Pattern(180, 420, 226))
            using (var stitcher = new VerticalStitcher())
            {
                foreach (int offset in new[] { 0, 180 })
                    using (var frame = content.Clone(new Rectangle(0, offset, 180, 240), PixelFormat.Format24bppRgb))
                        Assert(stitcher.Add(frame) == StitchStatus.Added, "A valid narrow overlap was rejected.");
                using (var result = stitcher.Build()) EqualPixels(content, result, content.Height);
            }
        }
        private static void TestTextSeams()
        {
            using (var content = new Bitmap(480, 560, PixelFormat.Format24bppRgb))
            using (var stitcher = new VerticalStitcher())
            {
                using (var g = Graphics.FromImage(content))
                {
                    g.Clear(Color.FromArgb(14, 18, 24));
                    for (int row = 25; row < 210; row += 29) g.FillRectangle(Brushes.SlateBlue, 35 + row / 5, row, 120 + row / 3, 7);
                    g.FillRectangle(Brushes.White, 220, 228, 3, 31); g.FillRectangle(Brushes.White, 238, 228, 3, 31);
                    g.FillRectangle(Brushes.White, 220, 241, 21, 3);
                }
                using (var first = content.Clone(new Rectangle(0, 0, 480, 240), PixelFormat.Format24bppRgb))
                {
                    using (var g = Graphics.FromImage(first))
                    using (var background = new SolidBrush(Color.FromArgb(14, 18, 24)))
                    {
                        g.FillRectangle(background, 215, 225, 35, 15);
                        g.FillRectangle(Brushes.White, 219, 228, 3, 31); g.FillRectangle(Brushes.White, 237, 228, 3, 31);
                    }
                    stitcher.Add(first);
                }
                using (var next = content.Clone(new Rectangle(0, 61, 480, 240), PixelFormat.Format24bppRgb))
                    Assert(stitcher.Add(next) == StitchStatus.Added, "A small rasterization difference prevented overlap alignment.");
                using (var result = stitcher.Build()) EqualPixels(content, result, 301);
            }
            using (var content = new Bitmap(1024, 300, PixelFormat.Format24bppRgb))
            using (var stitcher = new VerticalStitcher())
            {
                using (var g = Graphics.FromImage(content))
                {
                    g.Clear(Color.Black);
                    var random = new Random(230);
                    for (int y = 0; y < 300; y += 7) g.FillRectangle(Brushes.White, 20, y, 5, random.Next(1, 6));
                }
                foreach (int offset in new[] { 0, 37 })
                    using (var frame = content.Clone(new Rectangle(0, offset, 1024, 200), PixelFormat.Format24bppRgb))
                        Assert(stitcher.Add(frame) == StitchStatus.Added, "Sparse content near the left edge was mistaken for a stationary page.");
                using (var result = stitcher.Build()) EqualPixels(content, result, 237);
            }
        }
        private static void TestStitch()
        {
            using (var content = Pattern(113, 1200, 17))
            using (var stitcher = new VerticalStitcher())
            {
                int frameHeight = 320;
                int[] positions = { 0, 73, 197, 198, 411, 580, 601 };
                foreach (int position in positions)
                    using (var frame = content.Clone(new Rectangle(0, position, content.Width, frameHeight), PixelFormat.Format24bppRgb))
                        Assert(stitcher.Add(frame) == StitchStatus.Added, "Valid frame was rejected at " + position);
                using (var result = stitcher.Build()) EqualPixels(content, result, positions[positions.Length - 1] + frameHeight);
                Assert(stitcher.Frames == positions.Length, "Wrong frame count.");
            }
        }
        private static void TestUnchanged()
        {
            using (var frame = Pattern(90, 200, 3))
            using (var stitcher = new VerticalStitcher())
            { stitcher.Add(frame); Assert(stitcher.Add(frame) == StitchStatus.Unchanged, "Duplicate accepted."); Assert(stitcher.Height == 200, "Height changed on duplicate."); }
        }
        private static void TestUnrelated()
        {
            using (var before = Pattern(80, 200, 3))
            using (var after = Pattern(80, 200, 99))
            using (var stitcher = new VerticalStitcher())
            { stitcher.Add(before); Assert(stitcher.Add(after) == StitchStatus.Unmatched, "Unrelated content accepted."); Assert(stitcher.Height == 200, "Rejected image changed output."); }
        }
        private static void TestDimensions()
        {
            using (var before = new Bitmap(80, 200))
            using (var after = new Bitmap(81, 200))
            using (var stitcher = new VerticalStitcher())
            {
                stitcher.Add(before); bool rejected = false;
                try { stitcher.Add(after); } catch (ArgumentException) { rejected = true; }
                Assert(rejected, "Size change accepted.");
            }
        }
        private static void TestInitialLimit()
        {
            using (var oversized = new Bitmap(1, VerticalStitcher.MaxHeight + 1))
            using (var stitcher = new VerticalStitcher())
            { Assert(stitcher.Add(oversized) == StitchStatus.LimitReached, "Oversized first frame accepted."); Assert(stitcher.Frames == 0, "Rejected initial frame stored."); }
        }
        private static void TestAccumulatedLimit()
        {
            using (var content = Pattern(8, 31000, 19))
            using (var stitcher = new VerticalStitcher())
            {
                bool reached = false;
                for (int y = 0; y < 30500; y += 200)
                {
                    using (var frame = content.Clone(new Rectangle(0, y, 8, 400), PixelFormat.Format24bppRgb))
                    {
                        var status = stitcher.Add(frame);
                        if (status == StitchStatus.LimitReached) { reached = true; break; }
                        Assert(status == StitchStatus.Added, "Limit test frame mismatch.");
                    }
                }
                Assert(reached && stitcher.Height <= VerticalStitcher.MaxHeight, "Accumulated limit not enforced.");
            }
        }
        private static void TestAnnotations()
        {
            using (var canvas = new AnnotationCanvas(new Bitmap(120, 100)))
            {
                using (var g = Graphics.FromImage(canvas.Image)) g.Clear(Color.White);
                var rectangle = new Mark { Tool = DrawTool.Rectangle, Color = Color.Red, Width = 3 };
                rectangle.Points.Add(new PointF(80, 80)); rectangle.Points.Add(new PointF(20, 20));
                canvas.AddMark(rectangle); canvas.SetZoom(0.25F);
                using (var rendered = canvas.Render())
                { Assert(rendered.Size == new Size(120, 100), "Zoom changed export dimensions."); Assert(rendered.GetPixel(20, 50).R > 240 && rendered.GetPixel(20, 50).G < 40, "Annotation missing."); }
                Assert(canvas.Image.GetPixel(20, 50).ToArgb() == Color.White.ToArgb(), "Source image mutated.");
                canvas.Undo(); Assert(canvas.Marks.Count == 0, "Undo failed.");
            }
        }
        private static void TestInlineText()
        {
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(30, 30, 900, 560)))
            {
                overlay.Show(); Select(overlay);
                var toolbar = Field<CaptureToolbar>(overlay, "toolbar"); var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                var options = Field<AnnotationOptions>(toolbar, "options"); toolbar.SelectTool(DrawTool.Text);
                Option<NumericUpDown>(options, "text-font-size").Value = 32;
                options.Ink = Color.Blue; options.ApplyTo(canvas);
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, 100, 80);
                var input = Field<TextBox>(canvas, "textEditor");
                Assert(input != null && input.Parent == canvas && input.Multiline, "Text tool did not open a native inline editor.");
                input.Text = "中文 Abc123\r\n第二行";
                Assert(canvas.Marks[0].FontSize == 32 && canvas.Marks[0].Color == Color.Blue, "Text ignored selected size or color.");
                Option<NumericUpDown>(options, "text-font-size").Value = 40;
                Assert(canvas.Marks[0].FontSize == 40 && input.Font.Size > 39, "Text settings did not update the active editor.");
                ToolPreview(overlay, "text-input");
                input.Focus(); var enter = new KeyEventArgs(Keys.Enter);
                Assert(canvas.HandleTextKey(enter) && !enter.SuppressKeyPress && !overlay.IsDisposed, "Enter exported the screenshot instead of allowing a newline.");
                var copy = new KeyEventArgs(Keys.Control | Keys.C);
                Assert(canvas.HandleTextKey(copy) && !copy.SuppressKeyPress, "Copy shortcut was intercepted while typing.");
                using (var rendered = canvas.Render())
                {
                    Assert(Field<TextBox>(canvas, "textEditor") == null && canvas.Marks.Count == 1, "Export lost or duplicated the pending text.");
                    Assert(canvas.Marks[0].Text.Contains("第二行"), "Export lost multiline text.");
                    canvas.Tool = DrawTool.None;
                    using (var withoutSelection = canvas.Render()) EqualPixels(rendered, withoutSelection, rendered.Height);
                }
                toolbar.SelectTool(DrawTool.Text);
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, 120, 90); Mouse(canvas, "OnMouseUp", MouseButtons.Left, 120, 90);
                canvas.Marks[0].TextAngle = 20; ToolPreview(overlay, "text-transform"); canvas.Marks[0].TextAngle = 0;
                canvas.Undo(); Assert(canvas.Marks.Count == 0, "Typing and changing settings were not undone as one edit.");
                canvas.Redo(); Assert(canvas.Marks.Count == 1 && canvas.Marks[0].FontSize == 40, "Redo lost text or formatting.");
                toolbar.SelectTool(DrawTool.Text);
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, 420, 250);
                input = Field<TextBox>(canvas, "textEditor"); input.Text = "取消"; input.Focus();
                canvas.HandleTextKey(new KeyEventArgs(Keys.Escape));
                Assert(canvas.Marks.Count == 1 && !overlay.IsDisposed, "Cancelling text closed the screenshot or retained the draft.");
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, 420, 250); canvas.CommitTextEdit();
                Assert(canvas.Marks.Count == 1, "Empty editor created a text annotation.");
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, 120, 90);
                Mouse(canvas, "OnMouseUp", MouseButtons.Left, 120, 90);
                Mouse(canvas, "OnMouseDoubleClick", MouseButtons.Left, 120, 90);
                input = Field<TextBox>(canvas, "textEditor"); input.Text = "修改后的内容"; input.Focus();
                canvas.HandleTextKey(new KeyEventArgs(Keys.Escape));
                Assert(canvas.Marks[0].Text.Contains("第二行"), "Cancelling re-edit did not restore the previous text.");
                overlay.Close();
            }
        }
        private static void TextGesture(AnnotationCanvas canvas, PointF from, PointF to)
        { Gesture(canvas, new Point((int)Math.Round(from.X * canvas.Zoom), (int)Math.Round(from.Y * canvas.Zoom)), new Point((int)Math.Round(to.X * canvas.Zoom), (int)Math.Round(to.Y * canvas.Zoom))); }
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr word, IntPtr value);
        private static void TestTransparentTextInput()
        {
            foreach (float zoom in new[] { 1F, 0.5F })
            using (var host = new Form { ClientSize = new Size(850, 500) })
            using (var canvas = new AnnotationCanvas(new Bitmap(850, 500)))
            {
                using (var g = Graphics.FromImage(canvas.Image))
                {
                    g.Clear(Color.FromArgb(30, 34, 40));
                    using (var pen = new Pen(Color.FromArgb(62, 70, 80)))
                    { for (int x = 0; x < 850; x += 20) g.DrawLine(pen, x, 0, x, 500); for (int y = 0; y < 500; y += 20) g.DrawLine(pen, 0, y, 850, y); }
                }
                host.Controls.Add(canvas); host.Show(); canvas.SetZoom(zoom); canvas.Tool = DrawTool.Text; canvas.Ink = Color.Red;
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, (int)(180 * zoom), (int)(120 * zoom));
                var input = Field<InlineTextInput>(canvas, "textEditor");
                foreach (char character in "中文  Abc") SendMessage(input.Handle, 0x102, new IntPtr(character), new IntPtr(1));
                Application.DoEvents();
                var mark = canvas.Marks[0];
                Assert(mark.Text == "中文  Abc", "Native character input was lost or duplicated.");
                var expectedCaret = TextGeometry.World(mark, TextGeometry.Caret(mark, mark.Text.Length));
                Assert(Math.Abs(input.Left - expectedCaret.X * zoom) <= 1 && Math.Abs(input.Top - expectedCaret.Y * zoom) <= 1,
                    "IME anchor moved to the canvas origin instead of following the visible caret.");
                var originalFont = input.Font;
                SendMessage(input.Handle, 0x102, new IntPtr('1'), new IntPtr(1));
                Assert(object.ReferenceEquals(input.Font, originalFont), "Typing recreated the font and interrupted IME composition.");
                Assert(TextGeometry.Size(mark).Width < 160, "Short text left a large empty tail.");
                var gap = TextGeometry.Caret(mark, 3); gap.Y += TextGeometry.LineHeight(mark) / 2;
                using (var screenshot = new Bitmap(canvas.Width, canvas.Height))
                {
                    canvas.DrawToBitmap(screenshot, canvas.ClientRectangle);
                    int x = (int)Math.Round((mark.Points[0].X + gap.X) * zoom), y = (int)Math.Round((mark.Points[0].Y + gap.Y) * zoom);
                    var pixel = screenshot.GetPixel(x, y);
                    Assert(pixel.R < 100 && pixel.G < 100 && pixel.B < 100, "Editing covered the screenshot with an opaque background.");
                    int redNearText = 0, redAtOrigin = 0;
                    for (int sy = 0; sy < screenshot.Height; sy++) for (int sx = 0; sx < screenshot.Width; sx++)
                    {
                        var color = screenshot.GetPixel(sx, sy);
                        if (color.R > 150 && color.G < 100 && color.B < 100)
                        { if (sx > 170 * zoom && sy > 110 * zoom) redNearText++; if (sx < 80 * zoom && sy < 80 * zoom) redAtOrigin++; }
                    }
                    Assert(redNearText > 15 && redAtOrigin == 0, "Typed text appeared at the top-left instead of its image position.");
                    if (zoom == 1) screenshot.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-text-transparent-options.png"));
                }
                input.Text = "长文字长文字长文字长文字长文字";
                float longWidth = TextGeometry.Size(mark).Width;
                Assert(Math.Abs(longWidth - input.Text.Length * mark.FontSize - 6) < 12, "CJK text measurement accumulated excess width: " + longWidth);
                input.Text = "短";
                Assert(TextGeometry.Size(mark).Width < 40, "Deleting text did not shrink the frame.");
                input.Text = "第一行\r\n第二行";
                Assert(Math.Abs(TextGeometry.Size(mark).Height - (2 * TextGeometry.LineHeight(mark) + 4)) < 1, "Multiline height did not fit its content.");
                var from = TextGeometry.World(mark, TextGeometry.Caret(mark, 1));
                var to = TextGeometry.World(mark, TextGeometry.Caret(mark, 2));
                from.Y += 10; to.Y += 10;
                TextGesture(canvas, from, to);
                Assert(input.SelectedText == "一", "Mouse selection did not track the painted characters.");
                input.Select(input.TextLength, 0); input.Composition = "ceshi";
                typeof(InlineTextInput).GetMethod("NotifyInputState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(input, null);
                var display = (Mark)typeof(AnnotationCanvas).GetMethod("EditingTextMark", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(canvas, null);
                Assert(display.Text.EndsWith("ceshi") && !mark.Text.Contains("ceshi"), "IME preview was not shown separately from committed text.");
                input.Composition = "";
                canvas.CommitTextEdit();
                using (var exported = canvas.Render()) Assert(exported.GetPixel(0, 0).ToArgb() == canvas.Image.GetPixel(0, 0).ToArgb(), "Input chrome leaked into export.");
                host.Close();
            }
        }
        private static void TestTextTransform()
        {
            foreach (float zoom in new[] { 1F, 0.5F })
            using (var host = new Form { ClientSize = new Size(900, 650) })
            using (var canvas = new AnnotationCanvas(new Bitmap(900, 650)))
            {
                host.Controls.Add(canvas); host.Show(); canvas.SetZoom(zoom); canvas.Tool = DrawTool.Text;
                using (var g = Graphics.FromImage(canvas.Image)) g.Clear(Color.White);
                var text = new Mark { Tool = DrawTool.Text, Text = "可编辑文字 Abc", Color = Color.Red, FontSize = 30, TextAngle = 0 };
                text.Points.Add(new PointF(200, 200)); text.TextSize = TextGeometry.Measure(text); canvas.AddMark(text);
                var center = TextGeometry.Center(text);
                TextGesture(canvas, center, new PointF(center.X + 35, center.Y + 25));
                Assert(Math.Abs(text.Points[0].X - 235) < 2 && Math.Abs(text.Points[0].Y - 225) < 2, "Text move ignored image coordinates.");
                canvas.Undo(); Assert(canvas.Marks[0].Points[0] == new PointF(200, 200), "Move corrupted an undo snapshot.");
                canvas.Redo(); text = canvas.Marks[0];
                TextGesture(canvas, TextGeometry.Center(text), TextGeometry.Center(text));
                var corners = TextGeometry.Corners(text); var size = TextGeometry.Size(text);
                TextGesture(canvas, corners[2], new PointF(corners[2].X + size.Width * 0.5F, corners[2].Y + size.Height * 0.5F));
                Assert(Math.Abs(text.FontSize - 45) < 1, "Corner scaling did not scale the text.");
                Assert(DistanceForTest(TextGeometry.Corners(text)[0], corners[0]) < 2, "Scaling moved the opposite corner.");
                canvas.Undo(); Assert(Math.Abs(canvas.Marks[0].FontSize - 30) < 0.1, "Scale undo lost the old font size.");
                canvas.Redo(); text = canvas.Marks[0]; TextGesture(canvas, TextGeometry.Center(text), TextGeometry.Center(text));
                center = TextGeometry.Center(text);
                var rotation = TextGeometry.World(text, new PointF(TextGeometry.Size(text).Width / 2, -24 / zoom));
                float radius = (float)DistanceForTest(center, rotation);
                TextGesture(canvas, rotation, new PointF(center.X + radius, center.Y));
                Assert(Math.Abs(text.TextAngle - 90) < 2, "Rotation handle did not rotate around the text center.");
                var local = new PointF(12, 14); var roundTrip = TextGeometry.Local(text, TextGeometry.World(text, local));
                Assert(DistanceForTest(local, roundTrip) < 0.01, "Rotated text hit-testing does not invert the drawing transform.");
                using (var rotated = canvas.Render())
                {
                    canvas.Tool = DrawTool.None;
                    using (var plain = canvas.Render()) EqualPixels(rotated, plain, plain.Height);
                    int red = 0;
                    for (int y = 0; y < rotated.Height; y++) for (int x = 0; x < rotated.Width; x++)
                    { var pixel = rotated.GetPixel(x, y); if (pixel.R > 180 && pixel.G < 100) red++; }
                    Assert(red > 100, "Rotated text was absent from exported pixels.");
                }
                canvas.Undo(); Assert(Math.Abs(canvas.Marks[0].TextAngle) < 0.1, "Rotation undo failed.");
                canvas.Redo(); Assert(Math.Abs(canvas.Marks[0].TextAngle - 90) < 2, "Rotation redo failed.");
                host.Close();
            }
        }
        private static double DistanceForTest(PointF a, PointF b)
        { return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }
        private static void TestMosaicPixels()
        {
            using (var source = new Bitmap(25, 16))
            {
                var dark = Color.FromArgb(13, 17, 23); var light = Color.FromArgb(242, 244, 248);
                using (var g = Graphics.FromImage(source))
                using (var brush = new SolidBrush(light))
                { g.Clear(dark); g.FillRectangle(brush, 8, 0, 8, 16); g.FillRectangle(Brushes.Blue, 16, 0, 9, 16); }
                source.SetPixel(16, 0, Color.Red);
                using (var pixels = ImageFilters.Pixelate(source, 8))
                {
                    Assert(pixels.GetPixel(3, 3).ToArgb() == dark.ToArgb(), "Mosaic changed a uniform dark background.");
                    Assert(pixels.GetPixel(11, 3).ToArgb() == light.ToArgb(), "Mosaic changed a uniform light background.");
                    Assert(pixels.GetPixel(20, 3).ToArgb() == Color.FromArgb(4, 0, 251).ToArgb(), "Mosaic did not average each source color channel.");
                    Assert(pixels.GetPixel(24, 3).ToArgb() == Color.Blue.ToArgb(), "Partial edge block lost the source color.");
                }
            }
            using (var source = new Bitmap(120, 100))
            {
                using (var g = Graphics.FromImage(source)) g.Clear(Color.FromArgb(32, 32, 32));
                for (int y = 0; y < 100; y++) for (int x = 0; x < 120; x++) if ((x + y) % 4 == 0) source.SetPixel(x, y, Color.White);
                using (var canvas = new AnnotationCanvas(new Bitmap(source)))
                {
                    canvas.Tool = DrawTool.Mosaic; canvas.MosaicMode = ApplyMode.Rectangle; canvas.MosaicSize = 8;
                    Gesture(canvas, new Point(8, 8), new Point(112, 88));
                    using (var exported = canvas.Render())
                    {
                        for (int y = 9; y < 88; y++) for (int x = 9; x < 112; x++)
                            Assert(exported.GetPixel(x, y).ToArgb() == Color.FromArgb(88, 88, 88).ToArgb(), "Block averaging left a white stripe or retained the center-pixel contrast.");
                    }
                    canvas.Tool = DrawTool.None;
                    foreach (float zoom in new[] { 0.65F, 1F, 1.25F })
                    {
                        canvas.SetZoom(zoom);
                        using (var visible = new Bitmap(canvas.Width, canvas.Height))
                        {
                            canvas.DrawToBitmap(visible, canvas.ClientRectangle);
                            for (int y = (int)(20 * zoom); y < (int)(75 * zoom); y++) for (int x = (int)(20 * zoom); x < (int)(100 * zoom); x++)
                                Assert(visible.GetPixel(x, y).R == 88, "Scaled preview introduced a gap between mosaic blocks.");
                        }
                    }
                }
            }
        }
        private static void TestGaussianBlur()
        {
            using (var impulse = new Bitmap(41, 41))
            {
                using (var g = Graphics.FromImage(impulse)) { g.Clear(Color.Black); g.FillRectangle(Brushes.White, 18, 18, 5, 5); }
                using (var blurred = ImageFilters.Gaussian(impulse, 2))
                {
                    int center = blurred.GetPixel(20, 20).R;
                    Assert(center > 30 && center < 255 && blurred.GetPixel(20, 16).R > 0 && blurred.GetPixel(20, 16).R < center, "Gaussian filtering did not spread an impulse smoothly.");
                    Assert(Math.Abs(blurred.GetPixel(16, 20).R - blurred.GetPixel(24, 20).R) <= 1, "Gaussian kernel was not symmetric.");
                }
                using (var canvas = new AnnotationCanvas(new Bitmap(impulse)))
                {
                    canvas.Tool = DrawTool.Mosaic; canvas.MosaicMode = ApplyMode.Rectangle; canvas.MosaicEffect = MosaicEffect.GaussianBlur; canvas.BlurRadius = 3;
                    Gesture(canvas, new Point(10, 10), new Point(31, 31));
                    using (var exported = canvas.Render())
                    {
                        Assert(exported.GetPixel(20, 20).R < 255 && exported.GetPixel(16, 20).R > 0, "The Gaussian selection did not blur its source.");
                        Assert(exported.GetPixel(9, 20).ToArgb() == impulse.GetPixel(9, 20).ToArgb(), "Blur escaped its selection mask.");
                    }
                    canvas.Undo(); using (var original = canvas.Render()) EqualPixels(impulse, original, impulse.Height);
                    canvas.Redo(); Assert(canvas.Marks[0].MosaicEffect == MosaicEffect.GaussianBlur && canvas.Marks[0].BlurRadius == 3, "Undo lost the blur parameters.");
                }
            }
            using (var flat = new Bitmap(80, 60))
            {
                using (var g = Graphics.FromImage(flat)) g.Clear(Color.FromArgb(47, 91, 137));
                using (var blurred = ImageFilters.Gaussian(flat, 16)) EqualPixels(flat, blurred, flat.Height);
            }
        }
        private static void TestEditableMosaic()
        {
            using (var canvas = new AnnotationCanvas(Pattern(240, 180, 233)))
            {
                canvas.Tool = DrawTool.Mosaic; canvas.MosaicMode = ApplyMode.Rectangle;
                Gesture(canvas, new Point(20, 20), new Point(100, 90));
                var original = canvas.Marks[0].Bounds;
                Gesture(canvas, new Point(60, 55), new Point(90, 70));
                Assert(canvas.Marks.Count == 1 && canvas.Marks[0].Bounds == new RectangleF(50, 35, 80, 70), "Moving the mosaic created a second annotation or changed its size.");
                Gesture(canvas, new Point(130, 105), new Point(160, 125));
                Assert(canvas.Marks[0].Bounds == new RectangleF(50, 35, 110, 90), "Mosaic resize handles did not update its area.");
                canvas.ApplyMosaicSettings(MosaicEffect.GaussianBlur, 14, 5);
                Assert(canvas.Marks[0].MosaicEffect == MosaicEffect.GaussianBlur, "Existing mosaic effect could not be changed.");
                canvas.Undo(); Assert(canvas.Marks[0].MosaicEffect == MosaicEffect.Pixelate, "Effect change did not undo independently.");
                canvas.Undo(); Assert(canvas.Marks[0].Bounds == new RectangleF(50, 35, 80, 70), "Resize did not undo independently.");
                canvas.Undo(); Assert(canvas.Marks[0].Bounds == original, "Move did not undo independently.");
                canvas.Redo(); Assert(canvas.Marks[0].Bounds == new RectangleF(50, 35, 80, 70), "Mosaic move did not redo.");
            }
        }
        private static void ClickVertex(AnnotationCanvas canvas, Point point)
        { Mouse(canvas, "OnMouseDown", MouseButtons.Left, point.X, point.Y); Mouse(canvas, "OnMouseUp", MouseButtons.Left, point.X, point.Y); }
        private static void TestPolylines()
        {
            using (var canvas = new AnnotationCanvas(new Bitmap(300, 220)))
            {
                using (var g = Graphics.FromImage(canvas.Image)) g.Clear(Color.White);
                canvas.Tool = DrawTool.Line; canvas.Ink = Color.Red; canvas.Stroke = 4;
                ClickVertex(canvas, new Point(20, 30)); ClickVertex(canvas, new Point(120, 30)); ClickVertex(canvas, new Point(120, 140));
                Assert(canvas.Marks.Count == 0, "Point clicks prematurely committed separate lines.");
                Mouse(canvas, "OnMouseMove", MouseButtons.None, 250, 200);
                Mouse(canvas, "OnMouseDown", MouseButtons.Right, 250, 200);
                Mouse(canvas, "OnMouseUp", MouseButtons.Right, 250, 200);
                Assert(canvas.Marks.Count == 1 && canvas.Marks[0].Points.Count == 3, "Polyline did not store its clicked vertices as one annotation.");
                using (var rendered = canvas.Render()) Assert(rendered.GetPixel(70, 30).R > 240 && rendered.GetPixel(70, 30).G < 20 && rendered.GetPixel(120, 90).G < 20 && rendered.GetPixel(70, 90).G > 240, "Polyline segments did not follow the clicked route.");
                canvas.Undo(); Assert(canvas.Marks.Count == 0, "Polyline needed multiple undo operations."); canvas.Redo();
                canvas.Tool = DrawTool.Arrow;
                ClickVertex(canvas, new Point(30, 170)); ClickVertex(canvas, new Point(130, 170)); ClickVertex(canvas, new Point(180, 100));
                canvas.HandleGestureKey(new KeyEventArgs(Keys.Back)); ClickVertex(canvas, new Point(230, 100));
                canvas.HandleGestureKey(new KeyEventArgs(Keys.Enter));
                Assert(canvas.Marks[1].Tool == DrawTool.Arrow && canvas.Marks[1].Points.Count == 3 && canvas.Marks[1].Points[2] == new PointF(230, 100), "Backspace did not remove one polyline vertex.");
                using (var rendered = canvas.Render())
                {
                    Assert(rendered.GetPixel(80, 170).G < 20 && rendered.GetPixel(180, 135).G < 20, "Arrow did not follow every clicked segment.");
                    Assert(rendered.GetPixel(130, 160).G > 240, "Arrow placed an extra arrowhead at a bend.");
                    canvas.Marks[1].Tool = DrawTool.Line;
                    using (var withoutHead = canvas.Render())
                    {
                        int headPixels = 0;
                        for (int y = 80; y < 130; y++) for (int x = 200; x < 250; x++)
                            if (rendered.GetPixel(x, y).G < 20 && withoutHead.GetPixel(x, y).G > 240) headPixels++;
                        Assert(headPixels > 20, "Arrowhead was missing from the final segment.");
                    }
                    canvas.Marks[1].Tool = DrawTool.Arrow;
                }
                ClickVertex(canvas, new Point(10, 10)); ClickVertex(canvas, new Point(30, 50)); canvas.HandleGestureKey(new KeyEventArgs(Keys.Escape));
                Assert(canvas.Marks.Count == 2, "Escape committed a cancelled polyline.");
                ClickVertex(canvas, new Point(30, 40)); ClickVertex(canvas, new Point(90, 80));
                canvas.GetType().GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(canvas,
                    new object[] { new MouseEventArgs(MouseButtons.Left, 2, 90, 80, 0) });
                Assert(canvas.Marks.Count == 3 && canvas.Marks[2].Points.Count == 2, "Double-click appended a duplicate vertex or failed to finish.");
            }
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(20, 20, 900, 600)))
            {
                overlay.Show(); Select(overlay);
                var toolbar = Field<CaptureToolbar>(overlay, "toolbar"); toolbar.SelectTool(DrawTool.Line);
                var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                ClickVertex(canvas, new Point(30, 30)); ClickVertex(canvas, new Point(140, 100));
                Key(overlay, Keys.Enter); Assert(!overlay.IsDisposed && canvas.Marks.Count == 1, "Enter exported the screenshot instead of finishing its polyline.");
                ClickVertex(canvas, new Point(200, 100)); ClickVertex(canvas, new Point(300, 160)); Key(overlay, Keys.Escape);
                Assert(!overlay.IsDisposed && canvas.Marks.Count == 1, "Escape closed the screenshot instead of cancelling the unfinished polyline.");
                ClickVertex(canvas, new Point(40, 160)); ClickVertex(canvas, new Point(150, 200));
                Mouse(canvas, "OnMouseDown", MouseButtons.Right, 250, 220);
                Mouse(canvas, "OnMouseUp", MouseButtons.Right, 250, 220);
                Assert(!overlay.IsDisposed && canvas.Marks.Count == 2 && canvas.Marks[1].Points[1] == new PointF(150, 200), "Right-click closed the screenshot or added a hover vertex.");
                ToolPreview(overlay, "polyline"); overlay.Close();
            }
        }
        private static void TestApplicationIcon()
        {
            using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("ShotInk.ApplicationIcon"))
            {
                Assert(stream != null, "Application branding was not embedded.");
                using (var reader = new BinaryReader(stream))
                { Assert(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1 && reader.ReadUInt16() == 7, "The ICO did not contain all seven Windows icon sizes."); }
            }
            using (var icon = MainForm.CreateIcon())
            using (var pixels = icon.ToBitmap())
            {
                int yellow = 0, blue = 0, green = 0, purple = 0, orange = 0;
                for (int y = 0; y < pixels.Height; y++) for (int x = 0; x < pixels.Width; x++)
                {
                    var color = pixels.GetPixel(x, y); if (color.A < 128) continue;
                    if (color.R > 180 && color.G > 150 && color.B < 100) yellow++;
                    if (color.B > 180 && color.R < 100) blue++;
                    if (color.G > 120 && color.R < 100 && color.B < 180) green++;
                    if (color.B > 160 && color.R > 100 && color.R < 180 && color.G < 130) purple++;
                    if (color.R > 200 && color.G > 70 && color.G < 180 && color.B < 100) orange++;
                }
                pixels.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-app-icon.png"));
                Assert(yellow > 40 && blue > 10 && green > 10 && purple > 10 && orange > 10 && pixels.GetPixel(0, 0).A == 0,
                    "The supplied icon lost its pen, colorful corners or transparency: " + pixels.Size + ", yellow=" + yellow + ", corner=" + pixels.GetPixel(0, 0));
            }
        }
        private static void TestLiveLongDrag()
        {
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(20, 20, 900, 600)))
            {
                overlay.Show(); overlay.Activate(); Select(overlay);
                var image = new Bitmap(700, 1200); using (var g = Graphics.FromImage(image)) g.Clear(Color.Magenta);
                overlay.ShowLongPreview(image); var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, 40, 50);
                Mouse(overlay, "OnMouseMove", MouseButtons.Left, 220, 180); overlay.Refresh(); Application.DoEvents(); Native.FlushDesktop();
                using (var screen = Native.Capture(overlay.Bounds))
                {
                    Assert(screen.GetPixel(90, 80).ToArgb() != Color.Magenta.ToArgb(), "Old long-image pixels remained after dragging.");
                    Assert(screen.GetPixel(240, 190).ToArgb() == Color.Magenta.ToArgb(), "The dragged long image was not painted on the parent surface.");
                    screen.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-long-drag-options.png"));
                }
                Mouse(overlay, "OnMouseUp", MouseButtons.Left, 220, 180); overlay.Close();
            }
        }
        private static void TestMosaic()
        {
            using (var canvas = new AnnotationCanvas(new Bitmap(90, 100)))
            {
                for (int y = 0; y < 100; y++) for (int x = 0; x < 90; x++) canvas.Image.SetPixel(x, y, (x + y) % 2 == 0 ? Color.Black : Color.White);
                var mark = new Mark { Tool = DrawTool.Mosaic, MosaicSize = 10, Width = 3 };
                mark.Points.Add(new PointF(10, 10)); mark.Points.Add(new PointF(32, 33)); canvas.AddMark(mark);
                using (var rendered = canvas.Render())
                {
                    Assert(rendered.GetPixel(31, 32).ToArgb() == Color.FromArgb(128, 128, 128).ToArgb(), "Partial edge block did not use the averaged grayscale mosaic.");
                    Assert(rendered.GetPixel(9, 9).ToArgb() == canvas.Image.GetPixel(9, 9).ToArgb(), "Mosaic modified pixels outside region.");
                }
            }
        }
        private static void TestScreenCapture()
        {
            using (var scene = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new Point(80, 80), Size = new Size(240, 180), BackColor = Color.FromArgb(37, 91, 163), TopMost = true, ShowInTaskbar = false })
            {
                scene.Show(); Application.DoEvents(); System.Threading.Thread.Sleep(250); Application.DoEvents();
                var region = scene.RectangleToScreen(new Rectangle(20, 20, 100, 80));
                using (var screenshot = Native.Capture(region))
                {
                    var color = screenshot.GetPixel(50, 40);
                    Assert(Math.Abs(color.R - 37) < 5 && Math.Abs(color.G - 91) < 5 && Math.Abs(color.B - 163) < 5, "Native capture did not match the test window.");
                }
                scene.Close();
            }
        }
        private static Mark RectangleMark()
        {
            var mark = new Mark { Tool = DrawTool.Rectangle, Color = Color.Red, Width = 3 };
            mark.Points.Add(new PointF(20, 20)); mark.Points.Add(new PointF(80, 80)); return mark;
        }
        private static void TestUndoRedoErase()
        {
            using (var canvas = new AnnotationCanvas(new Bitmap(120, 100)))
            {
                using (var g = Graphics.FromImage(canvas.Image)) g.Clear(Color.White);
                canvas.AddMark(RectangleMark());
                canvas.Undo(); Assert(canvas.Marks.Count == 0 && canvas.CanRedo, "Undo did not make redo available.");
                canvas.Redo(); Assert(canvas.Marks.Count == 1, "Redo did not restore the mark.");
                canvas.Tool = DrawTool.Eraser; canvas.EraserBrushSize = 12;
                Gesture(canvas, new Point(20, 50), new Point(20, 50));
                using (var image = canvas.Render())
                {
                    Assert(image.GetPixel(20, 50).ToArgb() == Color.White.ToArgb(), "Eraser did not restore original pixels.");
                    Assert(image.GetPixel(20, 25).R > 240 && image.GetPixel(20, 25).G < 40, "Eraser removed the entire annotation.");
                }
                canvas.Undo(); Assert(canvas.Marks.Count == 1, "Undo did not restore erased annotation.");
                using (var image = canvas.Render()) Assert(image.GetPixel(20, 50).G < 40, "Undo did not restore erased pixels.");
                canvas.Redo();
                using (var image = canvas.Render()) Assert(image.GetPixel(20, 50).ToArgb() == Color.White.ToArgb(), "Redo did not restore erasure.");
                canvas.Undo(); canvas.AddMark(RectangleMark()); Assert(!canvas.CanRedo, "New edits kept an obsolete redo branch.");
                Assert(canvas.Image.GetPixel(20, 50).ToArgb() == Color.White.ToArgb(), "Eraser changed the original screenshot.");
            }
        }
        private static void Gesture(AnnotationCanvas canvas, params Point[] points)
        {
            Mouse(canvas, "OnMouseDown", MouseButtons.Left, points[0].X, points[0].Y);
            for (int i = 1; i < points.Length - 1; i++) Mouse(canvas, "OnMouseMove", MouseButtons.Left, points[i].X, points[i].Y);
            var last = points[points.Length - 1]; Mouse(canvas, "OnMouseUp", MouseButtons.Left, last.X, last.Y);
        }
        private static T Option<T>(Control parent, string name) where T : Control
        { var found = parent.Controls.Find(name, true); if (found.Length == 0) throw new Exception("Missing option: " + name); return (T)found[0]; }
        private static void TestToolOptions()
        {
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(0, 0, 900, 600)))
            {
                overlay.Show(); Select(overlay);
                var toolbar = Field<CaptureToolbar>(overlay, "toolbar");
                var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                var tools = Field<System.Collections.Generic.Dictionary<DrawTool, ToolStripItem>>(toolbar, "tools");
                var options = Field<AnnotationOptions>(toolbar, "options");
                var strip = Field<ToolStrip>(toolbar, "strip");
                var recognize = Field<ToolStripButton>(toolbar, "recognize");
                Assert(strip.Items.IndexOf(recognize) == strip.Items.IndexOf(tools[DrawTool.Eraser]) + 1, "OCR button is not immediately right of the eraser.");
                int requests = 0;
                using (var isolated = new CaptureToolbar())
                {
                    isolated.RecognitionClicked += delegate { requests++; };
                    Field<ToolStripButton>(isolated, "recognize").PerformClick();
                }
                Assert(requests == 1, "OCR button did not request recognition.");
                toolbar.SetRecognitionBusy(true); Assert(!recognize.Enabled, "Busy OCR button allowed duplicate clicks.");
                toolbar.SetRecognitionBusy(false);
                tools[DrawTool.Mosaic].PerformClick();
                Assert(canvas.Tool == DrawTool.Mosaic && canvas.MosaicMode == ApplyMode.Brush && options.Visible, "Mosaic required a second click to activate the brush.");
                ToolPreview(overlay, "mosaic");
                var sliders = options.Controls.Find("mosaic-size", true);
                Assert(sliders.Length == 1 && Option<OvalSlider>(options, "mosaic-size").Parent.Top < Option<OvalSlider>(options, "mosaic-brush-size").Parent.Top, "Mosaic sliders are missing or out of order.");
                Option<OvalSlider>(options, "mosaic-size").Value = 22;
                Option<OvalSlider>(options, "mosaic-brush-size").Value = 50;
                Assert(canvas.MosaicSize == 22 && canvas.MosaicBrushSize == 50, "Mosaic sliders did not update separate settings.");
                Option<Button>(options, "mosaic-gaussian").PerformClick(); Option<OvalSlider>(options, "blur-radius").Value = 12;
                Assert(canvas.MosaicEffect == MosaicEffect.GaussianBlur && canvas.BlurRadius == 12 && canvas.MosaicSize == 22, "Gaussian options did not keep independent pixel-block settings.");
                Option<Button>(options, "mosaic-pixelate").PerformClick();
                Option<Button>(options, "mode-rectangle").PerformClick();
                Assert(canvas.MosaicMode == ApplyMode.Rectangle && !Option<OvalSlider>(options, "mosaic-brush-size").Enabled, "Mosaic selection mode did not activate.");
                tools[DrawTool.Mosaic].PerformClick();
                Assert(canvas.MosaicMode == ApplyMode.Brush && Option<OvalSlider>(options, "mosaic-brush-size").Enabled, "Mosaic main button did not immediately restore its default brush.");
                tools[DrawTool.Eraser].PerformClick();
                Assert(canvas.Tool == DrawTool.Eraser && canvas.EraserMode == ApplyMode.Brush && options.Visible, "Eraser required a second click to activate the brush.");
                ToolPreview(overlay, "eraser");
                Option<OvalSlider>(options, "eraser-brush-size").Value = 62;
                Assert(canvas.EraserBrushSize == 62 && canvas.MosaicBrushSize == 50 && canvas.MosaicSize == 22, "Eraser size changed mosaic settings.");
                Option<Button>(options, "mode-rectangle").PerformClick();
                Assert(canvas.EraserMode == ApplyMode.Rectangle, "Eraser selection mode did not activate.");
                tools[DrawTool.Eraser].PerformClick();
                Assert(canvas.EraserMode == ApplyMode.Brush && canvas.Cursor == Cursors.Cross, "Eraser main button did not immediately restore its default brush.");
                tools[DrawTool.Rectangle].PerformClick();
                Assert(canvas.Tool == DrawTool.Rectangle && options.Visible, "Shape button did not select its default tool.");
                Option<Button>(options, "shape-ellipse").PerformClick();
                Assert(canvas.Tool == DrawTool.Ellipse, "Shape options did not switch to ellipse.");
                tools[DrawTool.Rectangle].PerformClick();
                Assert(canvas.Tool == DrawTool.Rectangle && canvas.Cursor == Cursors.Cross, "Shape button did not immediately restore its default rectangle.");
                tools[DrawTool.Arrow].PerformClick();
                Assert(canvas.Tool == DrawTool.Arrow, "Arrow button did not select its default tool.");
                Assert(options.Controls.Find("line-polyline", true).Length == 0 && options.Controls.Find("line-polyline-arrow", true).Length == 0,
                    "Line options still expose separate polyline tools.");
                Option<Button>(options, "line-straight").PerformClick();
                Assert(canvas.Tool == DrawTool.Line, "Line options did not switch to straight line.");
                tools[DrawTool.Arrow].PerformClick();
                Assert(canvas.Tool == DrawTool.Arrow && canvas.Cursor == Cursors.Cross, "Line button did not immediately restore its default arrow.");
                tools[DrawTool.Pen].PerformClick(); tools[DrawTool.Pen].PerformClick();
                Assert(canvas.Tool == DrawTool.Pen && options.Visible, "Clicking an active tool disabled it instead of showing options.");
                tools[DrawTool.None].PerformClick();
                Assert(canvas.Tool == DrawTool.None && !options.Visible && canvas.Cursor == Cursors.SizeAll, "Move selection mode was lost.");
                overlay.Close();
            }
        }
        private static void TestOvalSlider()
        {
            using (var slider = new OvalSlider { Minimum = 4, Maximum = 48, Value = 14 })
            {
                int changes = 0; slider.ValueChanged += delegate { changes++; };
                var thumb = slider.ThumbBounds;
                Assert(thumb.Width > thumb.Height, "Slider thumb is not an ellipse.");
                Mouse(slider, "OnMouseDown", MouseButtons.Left, (int)(thumb.Left + thumb.Width / 2), (int)(thumb.Top + thumb.Height / 2));
                Mouse(slider, "OnMouseMove", MouseButtons.Left, 10000, 13);
                Mouse(slider, "OnMouseUp", MouseButtons.Left, 10000, 13);
                Assert(slider.Value == 48 && changes > 0 && !slider.Capture, "Slider drag did not clamp at its maximum or release capture.");
                Mouse(slider, "OnMouseDown", MouseButtons.Left, 0, 13); Mouse(slider, "OnMouseUp", MouseButtons.Left, 0, 13);
                Assert(slider.Value == 4, "Track click did not reach the minimum.");
                Key(slider, Keys.Right); Assert(slider.Value == 5, "Arrow key did not increment the slider.");
                Key(slider, Keys.PageUp); Assert(slider.Value == 9, "Page Up did not apply the large increment.");
                Key(slider, Keys.End); Assert(slider.Value == 48, "End key did not reach the maximum.");
                slider.Enabled = false; Mouse(slider, "OnMouseDown", MouseButtons.Left, 0, 13); Key(slider, Keys.Home);
                Assert(slider.Value == 48, "Disabled slider changed its value.");
                slider.Enabled = true; slider.Minimum = 7; slider.Maximum = 7;
                Assert(slider.Value == 7 && !float.IsNaN(slider.ThumbBounds.X), "Zero-range slider produced invalid geometry.");
            }
        }
        private static void Key(Control control, Keys key)
        { control.GetType().GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(control, new object[] { new KeyEventArgs(key) }); }
        private static OcrLine RecognizedLine(string text, int left, int top)
        {
            var characters = new OcrCharacter[text.Length];
            for (int i = 0; i < text.Length; i++) characters[i] = new OcrCharacter { Text = text[i].ToString(), Start = i, Length = 1, Box = new[] { (float)(left + i * 10), (float)top, 10F, 20F } };
            return new OcrLine { Text = text, Box = new[] { (float)left, (float)top, (float)(text.Length * 10), 20F }, Characters = characters };
        }
        private static OcrDocument SelectionDocument(int width, int height)
        {
            return new OcrDocument { Width = width, Height = height, Lines = new[] {
                RecognizedLine("编号A01", 10, 10), RecognizedLine("release-sit", 200, 10),
                RecognizedLine("编号B02", 10, 50), RecognizedLine("hotfix-20261021", 200, 50),
                RecognizedLine("中文123，A_B-9.50!", 10, 100) } };
        }
        private static void TestOcrSearch()
        {
            var spaced = new OcrSelectionModel(new OcrDocument { Lines = new[] {
                RecognizedLine("Collections.empty L i s t();", 15, 20),
                RecognizedLine("Ｌｉｓｔ", 15, 60), RecognizedLine("li\u200bst", 15, 100),
                RecognizedLine("Lis", 15, 140), RecognizedLine("t", 50, 140) } });
            spaced.Search(" l i s t "); Assert(spaced.MatchCount == 4, "Search missed OCR spaces, zero-width characters, full-width text or same-row fragments.");
            spaced.Search("emptylist"); Assert(spaced.MatchCount == 1, "Search could not match spaced letters inside source code.");
            Assert(spaced.AllText.Contains("L i s t") && spaced.AllText.Contains("Ｌｉｓｔ"), "Search normalization altered copied OCR text.");
            var separateRows = new OcrSelectionModel(new OcrDocument { Lines = new[] { RecognizedLine("li", 15, 20), RecognizedLine("st", 15, 60) } });
            separateRows.Search("list"); Assert(separateRows.MatchCount == 0, "Search incorrectly joined separate text rows.");
            var partial = RecognizedLine("List", 15, 20); partial.Characters = new[] { partial.Characters[0] };
            var missingBoxes = new OcrSelectionModel(new OcrDocument { Lines = new[] { partial } });
            missingBoxes.Search("ist"); Assert(missingBoxes.MatchCount == 1 && missingBoxes.CurrentMatch.Width > 0, "Missing character metadata prevented line-text search.");
            using (var owner = new Form { ClientSize = new Size(700, 450) })
            using (var canvas = new AnnotationCanvas(new Bitmap(600, 4500)))
            using (var search = new OcrSearchPanel())
            {
                using (var g = Graphics.FromImage(canvas.Image)) g.Clear(Color.White);
                canvas.UseDocumentViewport(new Size(600, 360)); owner.Controls.Add(canvas);
                search.Bind(canvas); search.Location = new Point(240, 10); owner.Controls.Add(search); owner.Show();
                canvas.SetTextRecognition(new OcrDocument { Width = 600, Height = 4500, Lines = new[] {
                    RecognizedLine("Needle 目标", 15, 20), RecognizedLine("another NEEDLE target", 15, 2200), RecognizedLine("needle 目标", 15, 4200) } });
                Assert(search.Visible, "OCR did not show the search box.");
                var query = Option<TextBox>(search, "ocr-search-query"); query.Text = "NEEDLE";
                Assert(canvas.SearchMatchCount == 3 && canvas.SearchMatchNumber == 1, "Case-insensitive search missed a match.");
                string selected = Field<OcrSelectionModel>(canvas, "recognition").SelectedText;
                canvas.MoveSearchMatch(1); Assert(canvas.DocumentOffsetY > 1900 && canvas.DocumentOffsetY < 2300, "Search did not reveal a middle long-image match.");
                canvas.MoveSearchMatch(1); Assert(canvas.DocumentOffsetY > 3900, "Search did not reveal the last match.");
                canvas.MoveSearchMatch(1); Assert(canvas.SearchMatchNumber == 1 && canvas.DocumentOffsetY == 0, "Next match did not wrap to the top.");
                canvas.MoveSearchMatch(-1); Assert(canvas.SearchMatchNumber == 3, "Previous match did not wrap to the bottom.");
                Assert(Field<OcrSelectionModel>(canvas, "recognition").SelectedText == selected, "Searching changed copied selection.");
                canvas.SetZoom(0.5F); canvas.SearchText("目标"); canvas.MoveSearchMatch(1);
                Assert(canvas.SearchMatchCount == 2 && canvas.DocumentOffsetY > 3500, "Zoomed Chinese search used viewport coordinates as image coordinates.");
                using (var exported = canvas.Render()) Assert(exported.GetPixel(16, 4201).ToArgb() == Color.White.ToArgb(), "Search highlight leaked into export.");
                query.Focus();
                var enter = new KeyEventArgs(Keys.Enter); Assert(search.HandleKey(enter) && enter.SuppressKeyPress, "Enter could complete the screenshot while searching.");
                var copy = new KeyEventArgs(Keys.Control | Keys.C); Assert(search.HandleKey(copy) && !copy.SuppressKeyPress, "Search box could not copy its own text.");
                query.Text = "absent"; Assert(canvas.SearchMatchCount == 0 && Option<Label>(search, "ocr-search-count").Text == "0/0", "No-match counter is stale.");
                canvas.Tool = DrawTool.Pen; Assert(!search.Visible, "Search remained visible after leaving OCR."); owner.Close();
            }
            using (var editor = new EditorForm(new Bitmap(700, 4500), delegate(Bitmap image) { image.Dispose(); }))
            {
                editor.Show(); var canvas = Field<AnnotationCanvas>(editor, "canvas"); canvas.SetZoom(1);
                canvas.SetTextRecognition(new OcrDocument { Width = 700, Height = 4500, Lines = new[] { RecognizedLine("bottom target", 20, 4200) } });
                var search = Field<OcrSearchPanel>(editor, "textSearch"); Point location = search.Location;
                canvas.SearchText("target");
                Assert(Field<Panel>(editor, "viewport").AutoScrollPosition.Y < -3500, "History editor search did not scroll to its result.");
                Assert(search.Location == location, "Editor search box scrolled out of the upper-right corner."); editor.Close();
            }
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(0, 0, 900, 600)))
            {
                overlay.Show(); Select(overlay); overlay.ShowLongPreview(new Bitmap(700, 4500));
                var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                canvas.SetTextRecognition(new OcrDocument { Width = 700, Height = 4500, Lines = new[] {
                    RecognizedLine("target", 20, 20), RecognizedLine("target", 20, 4200) } });
                var search = Field<OcrSearchPanel>(overlay, "textSearch");
                Assert(search.Visible && search.Right <= overlay.ClientSize.Width, "Long-image search is missing or outside the screen.");
                Key(overlay, Keys.Control | Keys.F); Option<TextBox>(search, "ocr-search-query").Text = "target";
                Key(overlay, Keys.Enter);
                Assert(!overlay.IsDisposed && canvas.SearchMatchNumber == 2 && canvas.DocumentOffsetY > 3800, "Enter in long-image search completed the screenshot instead of navigating.");
                Key(overlay, Keys.Escape);
                Assert(!overlay.IsDisposed && canvas.HasTextRecognition && !search.Visible, "Closing search also closed OCR or the screenshot.");
                Key(overlay, Keys.Control | Keys.F); Assert(search.Visible, "Ctrl+F could not reopen search.");
                Option<TextBox>(search, "ocr-search-query").Text = "target";
                overlay.Activate(); Application.DoEvents();
                if (Native.GetForegroundWindow() == overlay.Handle)
                    Assert(OcrSearchPanel.ForwardGlobalFindNext() && canvas.SearchMatchNumber == 2, "The default F3 pin hotkey swallowed OCR find-next.");
                overlay.Close();
            }
        }
        private static void TestLargeOcrResponse()
        {
            var lines = new OcrLine[120];
            for (int index = 0; index < lines.Length; index++) lines[index] = RecognizedLine(new string('a', 100), 0, index * 200);
            var document = new OcrDocument { Width = 1200, Height = 30000, Lines = lines };
            using (var stream = new MemoryStream())
            {
                var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(OcrDocument),
                    new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings { MaxItemsInObjectGraph = int.MaxValue });
                serializer.WriteObject(stream, document);
                var parsed = RapidTextRecognizer.Parse(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
                Assert(parsed.Lines.Length == 120 && parsed.Lines[119].Characters.Length == 100, "Large OCR response lost characters at the serializer quota.");
            }
            Assert(RapidTextRecognizer.RecognitionTimeout(1000, 30000) > 60000, "Full-height OCR still uses the short-screenshot timeout.");
        }
        private static void TestOcrSelection()
        {
            var model = new OcrSelectionModel(SelectionDocument(700, 370));
            model.SelectRectangle(new RectangleF(195, 0, 170, 80));
            Assert(model.SelectedText == "release-sit" + Environment.NewLine + "hotfix-20261021", "Column selection included neighboring table cells.");
            model.SelectWord(new PointF(225, 20), false);
            Assert(model.SelectedText == "release-sit", "Click did not select a punctuation-bearing English word.");
            model.SelectRectangle(new RectangleF(200, 60, 150, 0));
            Assert(model.SelectedText == "hotfix-20261021", "A horizontal drag with no vertical motion selected no text.");
            model.SelectWord(new PointF(35, 110), true);
            Assert(model.SelectedText == "中文123，A_B-9.50!", "Line selection lost Chinese, digits or punctuation.");
            model.SelectAll(); Assert(model.SelectedText.StartsWith("编号A01\trelease-sit"), "Copy-all did not preserve table columns.");
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(0, 0, 900, 600)))
            {
                Select(overlay);
                var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                canvas.SetTextRecognition(SelectionDocument(700, 370));
                var before = Field<Rectangle>(overlay, "selection");
                Gesture(canvas, new Point(195, 0), new Point(365, 80));
                Assert(canvas.HasSelectedText && Field<Rectangle>(overlay, "selection") == before, "Selecting text moved the screenshot selection.");
                var copyAll = new KeyEventArgs(Keys.Control | Keys.A);
                Assert(canvas.HandleRecognitionKey(copyAll) && copyAll.SuppressKeyPress, "OCR mode did not consume select-all.");
                using (var image = canvas.Render()) EqualPixels(canvas.Image, image, image.Height);
                canvas.SetZoom(0.5F);
                Gesture(canvas, new Point(98, 0), new Point(182, 40));
                Assert(Field<OcrSelectionModel>(canvas, "recognition").SelectedText == "release-sit" + Environment.NewLine + "hotfix-20261021", "Zoomed selection used screen coordinates as image coordinates.");
                canvas.GetType().GetMethod("OnMouseDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(canvas,
                    new object[] { new MouseEventArgs(MouseButtons.Left, 2, 18, 55, 0) });
                Mouse(canvas, "OnMouseDoubleClick", MouseButtons.Left, 18, 55); Mouse(canvas, "OnMouseUp", MouseButtons.Left, 18, 55);
                Assert(Field<OcrSelectionModel>(canvas, "recognition").SelectedText == "中文123，A_B-9.50!", "Mouse-up replaced double-click line selection with a single word.");
                Field<CaptureToolbar>(overlay, "toolbar").SelectTool(DrawTool.Pen);
                Assert(!canvas.HasTextRecognition && canvas.Tool == DrawTool.Pen, "Drawing tools did not exit OCR selection.");
            }
        }
        private sealed class FakeRecognizer : ITextRecognizer
        {
            internal readonly System.Threading.Tasks.TaskCompletionSource<OcrDocument> Completion = new System.Threading.Tasks.TaskCompletionSource<OcrDocument>();
            internal int Calls;
            internal System.Threading.CancellationToken Token;
            public System.Threading.Tasks.Task<OcrDocument> RecognizeAsync(Bitmap image, System.Threading.CancellationToken cancellation)
            { Calls++; Token = cancellation; Assert(image.Width == 20 && image.Height == 20, "OCR received the wrong snapshot."); return Completion.Task; }
        }
        private static void Pump(System.Threading.Tasks.Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
            Assert(task.IsCompleted, "OCR continuation did not complete."); task.GetAwaiter().GetResult();
        }
        private static void TestOcrSession()
        {
            using (var owner = new Form())
            {
                int revision = 1, results = 0, errors = 0; bool busy = false;
                var engine = new FakeRecognizer();
                using (var session = new TextRecognitionSession(owner, delegate { return new Bitmap(20, 20); }, delegate { return revision; }, delegate(bool state) { busy = state; }, engine,
                    delegate { results++; }, delegate { errors++; }))
                {
                    var request = session.RecognizeAsync(); session.RecognizeAsync().GetAwaiter().GetResult();
                    Assert(engine.Calls == 1 && busy, "OCR did not suppress duplicate requests.");
                    engine.Completion.SetResult(SelectionDocument(20, 20)); Pump(request);
                    Assert(results == 1 && errors == 0 && !busy, "OCR success did not restore the button and publish its result.");
                }
                engine = new FakeRecognizer();
                using (var session = new TextRecognitionSession(owner, delegate { return new Bitmap(20, 20); }, delegate { return revision; }, delegate(bool state) { busy = state; }, engine,
                    delegate { results++; }, delegate { errors++; }))
                {
                    var request = session.RecognizeAsync(); revision++;
                    engine.Completion.SetResult(SelectionDocument(20, 20)); Pump(request);
                    Assert(results == 1 && errors == 1 && !busy, "OCR result was applied to a changed screenshot.");
                }
                engine = new FakeRecognizer();
                var cancelled = new TextRecognitionSession(owner, delegate { return new Bitmap(20, 20); }, delegate { return revision; }, delegate(bool state) { busy = state; }, engine,
                    delegate { results++; }, delegate { errors++; });
                var pending = cancelled.RecognizeAsync(); cancelled.Dispose();
                Assert(engine.Token.IsCancellationRequested, "Closing OCR did not cancel its request.");
                engine.Completion.SetResult(SelectionDocument(20, 20)); Pump(pending);
                Assert(results == 1 && errors == 1, "Late OCR result escaped cancellation.");
                engine = new FakeRecognizer();
                using (var session = new TextRecognitionSession(owner, delegate { return new Bitmap(20, 20); }, delegate { return revision; }, delegate(bool state) { busy = state; }, engine,
                    delegate { results++; }, delegate { errors++; }))
                {
                    var failed = session.RecognizeAsync(); engine.Completion.SetException(new InvalidOperationException("model missing")); Pump(failed);
                    Assert(errors == 2 && !busy, "OCR failure left the UI busy.");
                }
            }
        }

        private static void TestMosaicBrush()
        {
            using (var canvas = new AnnotationCanvas(Pattern(160, 140, 31)))
            {
                canvas.Tool = DrawTool.Mosaic; canvas.MosaicBrushSize = 20; canvas.MosaicSize = 8; canvas.SetZoom(0.5F);
                Gesture(canvas, new Point(10, 20), new Point(60, 20), new Point(60, 50), new Point(10, 50));
                using (var image = canvas.Render())
                {
                    Assert(image.GetPixel(60, 40).ToArgb() != canvas.Image.GetPixel(60, 40).ToArgb(), "Fast brush movement left gaps in mosaic.");
                    Assert(image.GetPixel(120, 70).ToArgb() != canvas.Image.GetPixel(120, 70).ToArgb(), "Brush bounds ignored intermediate points.");
                    Assert(image.GetPixel(60, 70).ToArgb() == canvas.Image.GetPixel(60, 70).ToArgb(), "Mosaic filled the bounding rectangle instead of following the brush.");
                    Assert(image.GetPixel(60, 55).ToArgb() == canvas.Image.GetPixel(60, 55).ToArgb(), "Mosaic brush was larger than its configured size.");
                    canvas.MosaicSize = 32; canvas.MosaicBrushSize = 80;
                    using (var unchanged = canvas.Render()) EqualPixels(image, unchanged, image.Height);
                }
                Assert(canvas.Marks.Count == 1 && canvas.Marks[0].MosaicSize == 8 && canvas.Marks[0].BrushSize == 20, "Stroke settings were not captured independently.");
                canvas.Undo();
                using (var image = canvas.Render()) EqualPixels(canvas.Image, image, canvas.Image.Height);
                canvas.Redo(); Assert(canvas.Marks.Count == 1, "Mosaic stroke did not redo as one action.");
                canvas.SetZoom(1); canvas.MosaicSize = 8; canvas.MosaicBrushSize = 40;
                Gesture(canvas, new Point(20, 40), new Point(120, 40));
                using (var image = canvas.Render()) Assert(image.GetPixel(60, 55).ToArgb() != canvas.Image.GetPixel(60, 55).ToArgb(), "Brush size did not change the painted area.");
                Gesture(canvas, new Point(0, 0), new Point(0, 0));
                using (var image = canvas.Render()) Assert(image.GetPixel(0, 0).ToArgb() != canvas.Image.GetPixel(0, 0).ToArgb(), "Single click at image edge did not apply mosaic.");
            }
        }
        private static void ToolPreview(CaptureOverlay overlay, string tool)
        {
            Application.DoEvents();
            using (var image = new Bitmap(overlay.Width, overlay.Height))
            {
                overlay.DrawToBitmap(image, overlay.ClientRectangle);
                image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-" + tool + "-options.png"), ImageFormat.Png);
            }
        }
        private static void TestMosaicSelection()
        {
            using (var small = new AnnotationCanvas(Pattern(100, 80, 33)))
            using (var large = new AnnotationCanvas(Pattern(100, 80, 33)))
            {
                foreach (var canvas in new[] { small, large }) { canvas.Tool = DrawTool.Mosaic; canvas.MosaicMode = ApplyMode.Rectangle; canvas.MosaicSize = 8; }
                small.MosaicBrushSize = 4; large.MosaicBrushSize = 100;
                Gesture(small, new Point(80, 60), new Point(10, 10)); Gesture(large, new Point(80, 60), new Point(10, 10));
                using (var a = small.Render())
                using (var b = large.Render())
                {
                    EqualPixels(a, b, a.Height);
                    Assert(a.GetPixel(30, 30).ToArgb() == a.GetPixel(31, 31).ToArgb(), "Mosaic size did not produce blocks.");
                    Assert(a.GetPixel(9, 30).ToArgb() == small.Image.GetPixel(9, 30).ToArgb(), "Selection mosaic changed pixels outside the box.");
                    small.Undo(); small.MosaicSize = 24; Gesture(small, new Point(80, 60), new Point(10, 10));
                    using (var changed = small.Render()) Assert(changed.GetPixel(30, 30).ToArgb() != a.GetPixel(30, 30).ToArgb(), "Mosaic size did not affect pixelation.");
                }
            }
        }
        private static void TestEraserModes()
        {
            using (var canvas = new AnnotationCanvas(new Bitmap(160, 120)))
            {
                using (var g = Graphics.FromImage(canvas.Image)) g.Clear(Color.White);
                var line = new Mark { Tool = DrawTool.Line, Color = Color.Red, Width = 4 };
                line.Points.Add(new PointF(10, 50)); line.Points.Add(new PointF(150, 50)); canvas.AddMark(line);
                canvas.Tool = DrawTool.Eraser; canvas.EraserBrushSize = 20;
                Gesture(canvas, new Point(80, 10), new Point(80, 90));
                using (var image = canvas.Render())
                {
                    Assert(image.GetPixel(80, 50).ToArgb() == Color.White.ToArgb(), "Eraser skipped a fast stroke between events.");
                    Assert(image.GetPixel(100, 50).G < 40, "Small eraser removed pixels outside its brush.");
                }
                canvas.Undo(); Assert(canvas.Marks.Count == 1, "Eraser stroke was split into multiple undo steps.");
                canvas.EraserBrushSize = 60; Gesture(canvas, new Point(80, 10), new Point(80, 90));
                using (var image = canvas.Render()) Assert(image.GetPixel(100, 50).ToArgb() == Color.White.ToArgb(), "Eraser brush size did not affect coverage.");
                canvas.Undo(); canvas.EraserMode = ApplyMode.Rectangle;
                Gesture(canvas, new Point(75, 70), new Point(20, 30));
                using (var image = canvas.Render())
                {
                    Assert(image.GetPixel(40, 50).ToArgb() == Color.White.ToArgb(), "Selection eraser did not restore the selected area.");
                    Assert(image.GetPixel(100, 50).G < 40, "Selection eraser removed the whole mark.");
                }
                canvas.Undo(); canvas.Tool = DrawTool.Mosaic; canvas.MosaicMode = ApplyMode.Rectangle;
                // Verify erasing mosaic restores the frozen pixels, rather than painting a color.
                canvas.Reframe(Pattern(160, 120, 42), Point.Empty);
                Gesture(canvas, new Point(20, 20), new Point(140, 100));
                canvas.Tool = DrawTool.Eraser; Gesture(canvas, new Point(30, 30), new Point(70, 80));
                using (var image = canvas.Render()) Assert(image.GetPixel(50, 50).ToArgb() == canvas.Image.GetPixel(50, 50).ToArgb(), "Erasing mosaic did not restore the original screenshot.");
                canvas.Redo();
                canvas.Undo();
                using (var image = canvas.Render()) Assert(image.GetPixel(50, 50).ToArgb() != canvas.Image.GetPixel(50, 50).ToArgb(), "Undo did not restore erased mosaic.");
            }
        }

        private static void TestEraserOverlap()
        {
            using (var canvas = new AnnotationCanvas(new Bitmap(160, 120)))
            {
                using (var g = Graphics.FromImage(canvas.Image)) g.Clear(Color.White);
                var line = new Mark { Tool = DrawTool.Line, Color = Color.Red, Width = 6 };
                line.Points.Add(new PointF(10, 50)); line.Points.Add(new PointF(150, 50)); canvas.AddMark(line);
                canvas.Tool = DrawTool.Eraser; canvas.EraserBrushSize = 12;
                Gesture(canvas, new Point(60, 20), new Point(100, 80), new Point(60, 80), new Point(100, 20), new Point(60, 20));
                using (var image = canvas.Render())
                {
                    Assert(image.GetPixel(80, 50).ToArgb() == Color.White.ToArgb(), "Overlapping brush paths cancelled the erased intersection.");
                    Assert(image.GetPixel(20, 50).G < 40 && image.GetPixel(140, 50).G < 40, "Erasing an intersection removed the whole line.");
                }
                canvas.Undo();
                using (var image = canvas.Render()) Assert(image.GetPixel(80, 50).G < 40, "Undo did not restore the erased intersection.");
            }
        }

        private static void TestHighlighter()
        {
            using (var canvas = new AnnotationCanvas(new Bitmap(100, 80)))
            {
                using (var g = Graphics.FromImage(canvas.Image)) g.Clear(Color.White);
                var mark = new Mark { Tool = DrawTool.Highlight, Color = Color.Red, Width = 3 };
                mark.Points.Add(new PointF(20, 40)); mark.Points.Add(new PointF(80, 40)); canvas.AddMark(mark);
                using (var output = canvas.Render())
                { var pixel = output.GetPixel(50, 40); Assert(pixel.R > 240 && pixel.G > 100 && pixel.G < 220 && pixel.B == pixel.G, "Highlighter was opaque or absent."); }
                Assert(canvas.Image.GetPixel(50, 40).ToArgb() == Color.White.ToArgb(), "Highlighter changed original pixels.");
            }
        }
        private static void Mouse(Control control, string method, MouseButtons button, int x, int y)
        {
            control.GetType().GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(control, new object[] { new MouseEventArgs(button, 1, x, y, 0) });
        }
        private static void MouseWheel(Control control, int delta)
        { control.GetType().GetMethod("OnMouseWheel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(control, new object[] { new HandledMouseEventArgs(MouseButtons.None, 0, 10, 10, delta) }); }
        private static T Field<T>(object target, string name)
        { return (T)target.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(target); }
        private static void Select(CaptureOverlay overlay)
        {
            Mouse(overlay, "OnMouseDown", MouseButtons.Left, 80, 70);
            Mouse(overlay, "OnMouseMove", MouseButtons.Left, 780, 440);
            Mouse(overlay, "OnMouseUp", MouseButtons.Left, 780, 440);
        }
        private static Bitmap FlatDesktop()
        {
            var image = new Bitmap(900, 600);
            using (var g = Graphics.FromImage(image)) g.Clear(Color.White);
            return image;
        }
        private static void TestFloatingSelection()
        {
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(0, 0, 900, 600)))
            {
                Bitmap output = null; bool cancelled = false;
                overlay.Completed += delegate(Bitmap image, CaptureAction action) { Assert(action == CaptureAction.Copy, "Enter did not choose copy."); output = image; };
                overlay.Cancelled += delegate { cancelled = true; };
                Select(overlay);
                Assert(!overlay.IsDisposed && Field<bool>(overlay, "ready"), "Selecting immediately closed the overlay.");
                var toolbar = Field<CaptureToolbar>(overlay, "toolbar");
                Assert(toolbar != null && toolbar.Top > 440, "Toolbar was not placed beneath the selection.");
                var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                toolbar.SelectTool(DrawTool.Rectangle);
                Mouse(canvas, "OnMouseDown", MouseButtons.Left, 20, 20);
                Mouse(canvas, "OnMouseMove", MouseButtons.Left, 80, 80);
                Mouse(canvas, "OnMouseUp", MouseButtons.Left, 80, 80);
                overlay.GetType().GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(overlay, new object[] { new KeyEventArgs(Keys.Enter) });
                Assert(output != null && !cancelled, "Completion failed or emitted cancellation.");
                using (output)
                {
                    Assert(output.Size == new Size(700, 370), "Toolbar or border changed export dimensions.");
                    var pixel = output.GetPixel(20, 50); Assert(pixel.R > 220 && pixel.G < 100, "Edited annotation not exported.");
                    Assert(output.GetPixel(0, 0).ToArgb() == Color.White.ToArgb() && output.GetPixel(699, 369).ToArgb() == Color.White.ToArgb(), "Selection border or controls leaked into export.");
                }
            }
        }
        private static void TestSelectionGeometry()
        {
            var bounds = new Rectangle(0, 0, 900, 600);
            Assert(SelectionGeometry.Move(new Rectangle(80, 70, 700, 370), new Point(1000, 1000), bounds) == new Rectangle(200, 230, 700, 370), "Moving exceeded screen bounds.");
            Assert(SelectionGeometry.Resize(new Rectangle(80, 70, 700, 370), 0, new Point(1000, 1000), bounds).Size == new Size(8, 8), "Resize reversed the selection.");
            var work = new Rectangle(-1920, -200, 1920, 1080);
            var size = new Size(450, 66);
            var location = CaptureToolbar.Place(new Rectangle(-1000, 710, 990, 170), work, size);
            Assert(work.Contains(new Rectangle(location, size)) && location.Y < 710, "Toolbar overflowed a monitor with negative coordinates.");
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), bounds))
            {
                Select(overlay);
                var canvas = Field<AnnotationCanvas>(overlay, "canvas"); canvas.AddMark(RectangleMark());
                var grips = Field<Array>(overlay, "grips");
                var grip = (Control)grips.GetValue(0);
                Mouse(grip, "OnMouseDown", MouseButtons.Left, 4, 4);
                Mouse(overlay, "OnMouseMove", MouseButtons.Left, 90, 75);
                Mouse(overlay, "OnMouseUp", MouseButtons.Left, 90, 75);
                Assert(Field<Rectangle>(overlay, "selection") == new Rectangle(90, 75, 690, 365), "Resize grip did not change selection.");
                using (var output = canvas.Render()) Assert(output.GetPixel(10, 45).R > 240 && output.GetPixel(10, 45).G < 40, "Resizing lost the annotation's screen position.");
                canvas.Undo(); canvas.Redo(); Assert(canvas.Marks[0].Points[0] == new PointF(10, 15), "Resize corrupted undo/redo coordinates.");
            }
        }
        private static void TestSelectionCancel()
        {
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(0, 0, 900, 600)))
            {
                bool exported = false;
                overlay.Completed += delegate(Bitmap image, CaptureAction action) { exported = true; image.Dispose(); };
                Select(overlay);
                overlay.GetType().GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(overlay, new object[] { new KeyEventArgs(Keys.Escape) });
                Assert(!exported && overlay.IsDisposed, "Cancel exported or kept the overlay open.");
            }
        }
        private static void TestRightCancel()
        {
            for (int mode = 0; mode < 4; mode++)
            using (var overlay = new CaptureOverlay(false, FlatDesktop(), new Rectangle(20, 20, 900, 600)))
            {
                int cancelled = 0; overlay.Cancelled += delegate { cancelled++; };
                overlay.Show();
                if (mode != 0) Select(overlay);
                if (mode == 2) overlay.ShowLongPreview(new Bitmap(700, 1200));
                Control target = mode == 0 ? (Control)overlay : mode == 3 ? (Control)Field<Array>(overlay, "grips").GetValue(0) : Field<AnnotationCanvas>(overlay, "canvas");
                Mouse(target, "OnMouseDown", MouseButtons.Right, 30, 30);
                Assert(!overlay.IsDisposed && cancelled == 0 && overlay.Capture, "Right-down closed the overlay before it could consume the matching release.");
                Mouse(overlay, "OnMouseUp", MouseButtons.Right, 890, 590);
                Assert(overlay.IsDisposed && cancelled == 1, "Right-up did not cancel the overlay exactly once.");
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, uint x, uint y, int data, UIntPtr extra);
        private static void TestLiveRightCancel()
        {
            var previousCursor = Cursor.Position;
            try
            {
                using (var background = new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(20, 20, 900, 600) })
                using (var menu = new ContextMenuStrip())
                {
                    int releases = 0, menus = 0; bool held = false;
                    menu.Items.Add("background"); menu.Opening += delegate { menus++; };
                    background.ContextMenuStrip = menu;
                    background.MouseUp += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Right) releases++; };
                    background.Show(); background.Activate(); Application.DoEvents();
                    for (int mode = 0; mode < 3; mode++)
                    using (var overlay = new CaptureOverlay(false, FlatDesktop(), background.Bounds))
                    {
                        overlay.Show(); overlay.Activate();
                        if (mode != 0) Select(overlay);
                        if (mode == 2) overlay.ShowLongPreview(new Bitmap(700, 1200));
                        Cursor.Position = new Point(200, 200); Application.DoEvents();
                        try
                        {
                            mouse_event(0x0008, 0, 0, 0, UIntPtr.Zero); held = true;
                            Application.DoEvents();
                            Assert(!overlay.IsDisposed, "Real right-down closed the overlay early.");
                            Cursor.Position = new Point(250, 230);
                            mouse_event(0x0010, 0, 0, 0, UIntPtr.Zero); held = false;
                            WaitUntil(delegate { return overlay.IsDisposed; });
                            for (int i = 0; i < 10; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                            Assert(releases == 0 && menus == 0, "Cancelling opened the background context menu or leaked the right release.");
                        }
                        finally { if (held) { mouse_event(0x0010, 0, 0, 0, UIntPtr.Zero); held = false; } }
                    }
                    // Check the background probe really detects a normal right click.
                    background.Activate(); Cursor.Position = new Point(250, 230); Application.DoEvents();
                    mouse_event(0x0008, 0, 0, 0, UIntPtr.Zero); mouse_event(0x0010, 0, 0, 0, UIntPtr.Zero);
                    WaitUntil(delegate { return releases > 0 && menus > 0; }); menu.Close();
                    background.Close();
                }
            }
            finally { Cursor.Position = previousCursor; }
        }
        private static Bitmap Sample()
        {
            var image = new Bitmap(820, 470);
            using (var g = Graphics.FromImage(image))
            using (var titleFont = new Font("Microsoft YaHei UI", 24))
            using (var font = new Font("Microsoft YaHei UI", 13))
            {
                g.Clear(Theme.Background);
                g.FillRectangle(Brushes.White, 30, 30, 760, 410);
                g.DrawString("屏幕上的灵感，即刻收藏。", titleFont, Brushes.MidnightBlue, 65, 64);
                g.DrawString("区域截图  /  标注编辑  /  桌面贴图  /  滚动长截图", font, Brushes.SlateGray, 69, 133);
                using (var brush = new SolidBrush(Theme.Accent)) g.FillRectangle(brush, 70, 204, 290, 164);
                using (var brush = new SolidBrush(Color.FromArgb(229, 233, 255))) g.FillRectangle(brush, 382, 204, 330, 164);
                g.DrawString("想法 01", titleFont, Brushes.White, 100, 252);
                g.DrawString("Capture your ideas.", font, Brushes.MidnightBlue, 410, 265);
            }
            return image;
        }
        private static void TestAutomaticCapture()
        {
            var oldCursor = Cursor.Position;
            using (var scene = new ScrollTestWindow())
            {
                scene.WheelRenderDelay = 120;
                scene.Show(); scene.Activate(); scene.Refresh(); Application.DoEvents();
                WaitUntil(delegate
                {
                    using (var displayed = Native.Capture(scene.RectangleToScreen(scene.ClientRectangle)))
                        return displayed.GetPixel(15, 15).ToArgb() == scene.Content.GetPixel(15, 15).ToArgb()
                            && displayed.GetPixel(100, 200).ToArgb() == scene.Content.GetPixel(100, 200).ToArgb();
                });
                using (var session = new ScrollCaptureSession(delegate { return Native.Capture(scene.RectangleToScreen(scene.ClientRectangle)); }))
                {
                    var task = session.RunAsync(); int wheels = 0; var next = DateTime.UtcNow.AddMilliseconds(250);
                    var deadline = DateTime.UtcNow.AddSeconds(22);
                    while (!task.IsCompleted && DateTime.UtcNow < deadline)
                    {
                        Application.DoEvents(); System.Threading.Thread.Sleep(10);
                        if (DateTime.UtcNow < next) continue;
                        if (wheels < 4) { MouseWheel(scene, -120); wheels++; next = DateTime.UtcNow.AddMilliseconds(250); }
                        else session.Finish();
                    }
                    Assert(task.IsCompleted, "Manual capture timed out.");
                    if (task.IsFaulted) throw task.Exception;
                    using (var result = task.Result) EqualPixels(scene.Content, result.Image, scene.Content.Height);
                }
                scene.Close();
            }
            Cursor.Position = oldCursor;
        }
        private static void TestLiveIntegratedScroll()
        {
            foreach (int renderDelay in new[] { 0, 120 })
            using (var scene = new ScrollTestWindow())
            {
                scene.WheelRenderDelay = renderDelay;
                scene.Show(); scene.Activate(); scene.Refresh(); Application.DoEvents();
                var bounds = new Rectangle(20, 20, 900, 600);
                using (var overlay = new CaptureOverlay(false, Native.Capture(bounds), bounds))
                {
                    overlay.InterceptScrollWheel = false;
                    overlay.Show();
                    Mouse(overlay, "OnMouseDown", MouseButtons.Left, 80, 70); Mouse(overlay, "OnMouseUp", MouseButtons.Left, 360, 310);
                    var task = overlay.CaptureLongAsync();
                    WaitUntil(delegate { var session = Field<ScrollCaptureSession>(overlay, "scrollSession"); return session != null && session.Height == 240; });
                    var wheelClock = System.Diagnostics.Stopwatch.StartNew();
                    // Deliver native wheel messages without relying on the shared desktop's mouse position.
                    SendMessage(overlay.Handle, 0x020A, new IntPtr(-120 << 16), IntPtr.Zero);
                    WaitUntil(delegate { return task.IsCompleted || Field<ScrollCaptureSession>(overlay, "scrollSession").Height == 345; });
                    Assert(!task.IsCompleted, "Delayed first wheel ended capture: " + Field<Label>(overlay, "sizeHint").Text);
                    for (int i = 0; i < 3; i++) SendMessage(overlay.Handle, 0x020A, new IntPtr(-120 << 16), IntPtr.Zero);
                    WaitUntil(delegate { return task.IsCompleted || Field<ScrollCaptureSession>(overlay, "scrollSession").Height == 660; });
                    Assert(!task.IsCompleted, "Delayed wheel burst ended capture: " + Field<Label>(overlay, "sizeHint").Text);
                    Assert(Field<ScrollCaptureSession>(overlay, "scrollSession").Frames == 5, "Rapid native wheel input skipped a viewport before sampling it.");
                    Console.WriteLine("Four wheel steps including capture and stitching: " + wheelClock.ElapsedMilliseconds + " ms, page render delay=" + renderDelay + " ms.");
                    var captureSession = Field<ScrollCaptureSession>(overlay, "scrollSession");
                    foreach (int destination in new[] { 315, 210, 315, 420 })
                    {
                        int version = captureSession.ProcessedVersion;
                        int delta = destination > scene.Offset ? -120 : 120;
                        SendMessage(overlay.Handle, 0x020A, new IntPtr(delta << 16), IntPtr.Zero);
                        WaitUntil(delegate { return task.IsCompleted || scene.Offset == destination && captureSession.ProcessedVersion > version && captureSession.IsCaughtUp; });
                        Assert(!task.IsCompleted && captureSession.Height == 660 && captureSession.Frames == 5, "Revisiting old content appended duplicate rows or stopped live capture.");
                    }
                    // Reverse before queued wheel input is forwarded: upward input must win immediately.
                    SendMessage(overlay.Handle, 0x020A, new IntPtr(-360 << 16), IntPtr.Zero);
                    SendMessage(overlay.Handle, 0x020A, new IntPtr(120 << 16), IntPtr.Zero);
                    WaitUntil(delegate { return task.IsCompleted || scene.Offset == 315 && captureSession.IsCaughtUp; });
                    Assert(!task.IsCompleted && captureSession.Height == 660 && Field<int>(Field<ScrollWheelRouter>(overlay, "scrollWheel"), "pending") == 0,
                        "Reversal retained the old queued downward movement.");
                    Option<Button>(Field<CaptureToolbar>(overlay, "toolbar"), "finish-scroll-capture").PerformClick(); Pump(task);
                    var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                    using (var image = canvas.Render()) EqualPixels(scene.Content, image, 660);
                    MouseWheel(canvas, -120); Assert(canvas.DocumentOffsetY > 0 && canvas.Height == 240, "Live preview did not stay inside its frame.");
                    ToolPreview(overlay, "integrated-long"); overlay.Close();
                    Assert(Field<ScrollWheelRouter>(overlay, "scrollWheel") == null, "Completion left the wheel hook active.");
                }
                scene.Close();
            }
            var color = Color.FromArgb(37, 91, 163);
            using (var scene = new Form { Bounds = new Rectangle(20, 20, 900, 600), StartPosition = FormStartPosition.Manual, FormBorderStyle = FormBorderStyle.None,
                BackColor = color, TopMost = true, ShowInTaskbar = false })
            {
                scene.Show(); scene.Refresh(); Application.DoEvents();
                using (var overlay = new CaptureOverlay(false, Native.Capture(scene.Bounds), scene.Bounds))
                {
                    overlay.InterceptScrollWheel = false;
                    overlay.Show(); Mouse(overlay, "OnMouseDown", MouseButtons.Left, 0, 0); Mouse(overlay, "OnMouseUp", MouseButtons.Left, 900, 600);
                    var task = overlay.CaptureLongAsync();
                    WaitUntil(delegate { var session = Field<ScrollCaptureSession>(overlay, "scrollSession"); return session != null && session.Height == 600; });
                    var toolbar = Field<CaptureToolbar>(overlay, "toolbar"); var at = toolbar.Bounds;
                    Option<Button>(toolbar, "finish-scroll-capture").PerformClick(); Pump(task);
                    using (var image = Field<AnnotationCanvas>(overlay, "canvas").Render())
                        Assert(image.GetPixel(at.Left + 20, at.Top + 20).ToArgb() == color.ToArgb(), "The finish button contaminated a full-frame capture.");
                    overlay.Close();
                }
                scene.Close();
            }
        }
        private sealed class ScrollTestWindow : Form
        {
            internal readonly Bitmap Content = Pattern(280, 660, 41);
            internal int WheelRenderDelay;
            private int offset;
            internal int Offset { get { return offset; } }
            private int pendingOffset;
            private readonly Timer delayedWheel = new Timer();
            internal ScrollTestWindow()
            {
                AutoScaleMode = AutoScaleMode.None;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(100, 90);
                ClientSize = new Size(280, 240);
                DoubleBuffered = true;
                TopMost = true;
                ShowInTaskbar = false;
                delayedWheel.Tick += delegate { delayedWheel.Stop(); offset = Math.Max(0, Math.Min(Content.Height - ClientSize.Height, offset + pendingOffset)); pendingOffset = 0; Invalidate(); };
            }
            protected override void OnPaint(PaintEventArgs e) { e.Graphics.DrawImageUnscaled(Content, 0, -offset); }
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                int movement = e.Delta < 0 ? 105 : -105;
                if (WheelRenderDelay == 0) { offset = Math.Max(0, Math.Min(Content.Height - ClientSize.Height, offset + movement)); Invalidate(); }
                else { pendingOffset += movement; if (!delayedWheel.Enabled) { delayedWheel.Interval = WheelRenderDelay; delayedWheel.Start(); } }
                base.OnMouseWheel(e);
            }
            protected override void Dispose(bool disposing) { if (disposing) { delayedWheel.Dispose(); Content.Dispose(); } base.Dispose(disposing); }
        }
        private static void Preview(string root)
        {
            var storage = Path.Combine(root, "preview-data");
            Directory.CreateDirectory(Path.Combine(storage, "captures"));
            using (var sample = Sample()) sample.Save(Path.Combine(storage, "captures", "sample.png"), ImageFormat.Png);
            using (var home = new MainForm(storage, false))
            {
                home.Show(); home.Activate(); Application.DoEvents(); System.Threading.Thread.Sleep(200); Application.DoEvents();
                using (var bitmap = Native.Capture(home.Bounds)) bitmap.Save(Path.Combine(root, "preview-home.png"));
                home.Close();
            }
            using (var editor = new EditorForm(Sample(), delegate(Bitmap pin) { pin.Dispose(); }))
            {
                editor.Show(); editor.Activate(); Application.DoEvents(); System.Threading.Thread.Sleep(200); Application.DoEvents();
                using (var bitmap = Native.Capture(editor.Bounds)) bitmap.Save(Path.Combine(root, "preview-editor.png"));
                editor.Close();
            }
            using (var background = new Bitmap(1100, 700))
            {
                using (var g = Graphics.FromImage(background))
                using (var sample = Sample()) { g.Clear(Color.FromArgb(32, 37, 47)); g.DrawImageUnscaled(sample, 80, 30); }
                using (var overlay = new CaptureOverlay(false, new Bitmap(background), new Rectangle(40, 40, 1100, 700)))
                {
                    overlay.Show(); overlay.Activate(); Application.DoEvents();
                    Mouse(overlay, "OnMouseDown", MouseButtons.Left, 80, 50);
                    Mouse(overlay, "OnMouseMove", MouseButtons.Left, 900, 500);
                    Mouse(overlay, "OnMouseUp", MouseButtons.Left, 900, 500);
                    Application.DoEvents(); System.Threading.Thread.Sleep(180); Application.DoEvents();
                    using (var screenshot = Native.Capture(overlay.Bounds)) screenshot.Save(Path.Combine(root, "preview-floating-toolbar.png"));
                    Field<CaptureToolbar>(overlay, "toolbar").SelectTool(DrawTool.Arrow);
                    var canvas = Field<AnnotationCanvas>(overlay, "canvas");
                    Mouse(canvas, "OnMouseDown", MouseButtons.Left, 570, 160);
                    Mouse(canvas, "OnMouseMove", MouseButtons.Left, 400, 100);
                    Mouse(canvas, "OnMouseUp", MouseButtons.Left, 400, 100);
                    Application.DoEvents(); System.Threading.Thread.Sleep(180); Application.DoEvents();
                    using (var screenshot = Native.Capture(overlay.Bounds)) screenshot.Save(Path.Combine(root, "preview-floating-drawing.png"));
                    overlay.Close();
                }
            }
            Console.WriteLine("Previews: " + root);
        }
    }
}
