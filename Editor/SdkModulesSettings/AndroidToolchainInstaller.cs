#if UNITY_ANDROID
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AMZNGoDSDK.Editor
{
    /// <summary>Installs build tools per project, without replacing Unity's JDK or SDK.</summary>
    public static class AndroidToolchainInstaller
    {
        private const string GradleSha256 = "20f1b1176237254a6fc204d8434196fa11a4cfb387567519c61556e8710aed78";
        private static Task<Paths> _installation;
        private static CancellationTokenSource _cancellation;
        private static bool _reloadLocked;
        private static volatile string _status = "";
        public static bool IsBusy => _installation != null;
        public static string Status => _status;

        private sealed class Paths { public string Java, Sdk, Gradle; }
        private sealed class Request
        {
            public Paths Current;
            public string Root, UnitySdk, Os, Architecture;
            public string[] AndroidPlayers;
        }
        [Serializable] private sealed class JavaAssets { public JavaAsset[] items; }
        [Serializable] private sealed class JavaAsset { public JavaBinary binary; }
        [Serializable] private sealed class JavaBinary { public JavaPackage package; }
        [Serializable] private sealed class JavaPackage { public string link, checksum; }

        public static async Task EnsureInstalledAsync()
        {
            var task = Begin();
            if (task == null) return;
            try
            {
                while (!task.IsCompleted)
                {
                    ShowProgress();
                    await Task.Delay(100);
                }
                Complete(await task);
            }
            catch (Exception ex) { _status = "Подготовка Android не завершена: " + ex.Message; throw; }
            finally { End(task); }
        }

        /// <summary>Build callbacks use the same worker, with progress and cancellation.</summary>
        public static void EnsureInstalled()
        {
            var task = Begin();
            if (task == null) return;
            try
            {
                while (!task.IsCompleted) { ShowProgress(); Thread.Sleep(100); }
                Complete(task.GetAwaiter().GetResult());
            }
            catch (Exception ex) { _status = "Подготовка Android не завершена: " + ex.Message; throw; }
            finally { End(task); }
        }

        private static Task<Paths> Begin()
        {
            if (_installation != null) return _installation;
            var errors = new List<string>();
            AndroidToolchainSettings.CollectValidationErrors(errors);
            if (errors.Count == 0) { _status = "Инструменты Android готовы."; return null; }
            string playerRoot = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer");
            var candidates = new List<string> { playerRoot };
            // Unity Hub installs other editors beside the current one on all three desktop platforms.
            var editorVersion = new DirectoryInfo(EditorApplication.applicationContentsPath).Parent?.Parent;
            string hubEditors = editorVersion?.Parent?.FullName;
            if (hubEditors != null && Directory.Exists(hubEditors))
                foreach (string version in Directory.GetDirectories(hubEditors))
                    candidates.Add(Path.Combine(version, Application.platform == RuntimePlatform.OSXEditor
                        ? "Unity.app/Contents/PlaybackEngines/AndroidPlayer" : "Editor/Data/PlaybackEngines/AndroidPlayer"));
            var request = new Request
            {
                Current = new Paths { Java = AndroidToolchainSettings.GradleJavaHome,
                    Sdk = AndroidToolchainSettings.GradleSdkRoot, Gradle = AndroidToolchainSettings.GradleHome },
                Root = Path.GetFullPath("Library/AmznGoDSDK/AndroidToolchain"),
                UnitySdk = AndroidExternalToolsSettings.sdkRootPath,
                Os = Application.platform == RuntimePlatform.WindowsEditor ? "windows" :
                    Application.platform == RuntimePlatform.OSXEditor ? "mac" : "linux",
                Architecture = SystemInfo.processorType.IndexOf("ARM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    SystemInfo.processorType.IndexOf("Apple", StringComparison.OrdinalIgnoreCase) >= 0 ? "aarch64" : "x64",
                AndroidPlayers = candidates.Distinct().ToArray()
            };
            _cancellation = new CancellationTokenSource();
            EditorApplication.LockReloadAssemblies();
            _reloadLocked = true;
            _status = "Подготовка инструментов Android…";
            _installation = Task.Run(() => Provision(request, _cancellation.Token));
            return _installation;
        }

        private static void ShowProgress()
        {
            if (!Application.isBatchMode && EditorUtility.DisplayCancelableProgressBar(
                    "AMZN GoD: подготовка Android", _status, 0.5f)) _cancellation?.Cancel();
        }

        private static void Complete(Paths paths)
        {
            AndroidToolchainSettings.SaveLocalPaths(paths.Java, paths.Sdk, paths.Gradle);
            var errors = new List<string>();
            AndroidToolchainSettings.CollectValidationErrors(errors);
            if (errors.Count > 0) throw new IOException(string.Join("\n", errors));
            _status = "Инструменты Android готовы. Unity 2022.3 сохранена.";
            Debug.Log("[AMZNGoDSDK] " + _status);
        }

        private static void End(Task<Paths> task)
        {
            if (_installation != task) return;
            if (task.IsFaulted) _status = "Подготовка Android не завершена: " + task.Exception.GetBaseException().Message;
            if (task.IsCanceled) _status = "Подготовка Android отменена. Повторите её в AMZN GoD / Android Build Tools.";
            _installation = null;
            _cancellation.Dispose();
            _cancellation = null;
            EditorUtility.ClearProgressBar();
            if (_reloadLocked) { _reloadLocked = false; EditorApplication.UnlockReloadAssemblies(); }
        }

        private static Paths Provision(Request request, CancellationToken token)
        {
            Directory.CreateDirectory(request.Root);
            var paths = request.Current;
            var roots = request.AndroidPlayers;
            if (!JavaValid(paths.Java)) paths.Java = roots.Select(root => Path.Combine(root, "OpenJDK")).FirstOrDefault(JavaValid);
            if (!GradleValid(paths.Gradle)) paths.Gradle = roots.Select(root => Path.Combine(root, "Tools/gradle")).FirstOrDefault(GradleValid);
            if (!SdkValid(paths.Sdk)) paths.Sdk = new[] { request.UnitySdk }.Concat(roots.Select(root => Path.Combine(root, "SDK"))).FirstOrDefault(SdkValid);
            if (!JavaValid(paths.Java)) paths.Java = FindJava(Path.Combine(request.Root, "jdk17"));
            if (!GradleValid(paths.Gradle)) paths.Gradle = Path.Combine(request.Root, "gradle-8.13");
            if (!SdkValid(paths.Sdk)) paths.Sdk = Path.Combine(request.Root, "sdk");
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
            {
                if (!JavaValid(paths.Java))
                {
                    _status = "Загрузка JDK 17…";
                    string metadata = GetText(client, "https://api.adoptium.net/v3/assets/latest/17/hotspot?architecture=" +
                        request.Architecture + "&image_type=jdk&os=" + request.Os + "&vendor=eclipse", token);
                    var assets = JsonUtility.FromJson<JavaAssets>("{\"items\":" + metadata + "}");
                    var package = assets?.items?.FirstOrDefault()?.binary?.package;
                    if (package == null || string.IsNullOrEmpty(package.link) || string.IsNullOrEmpty(package.checksum))
                        throw new IOException("Adoptium не вернул проверяемый JDK 17 для этой системы.");
                    string destination = Path.Combine(request.Root, "jdk17");
                    InstallArchive(client, package.link, package.checksum, false, destination, request.Os, token);
                    paths.Java = FindJava(destination);
                    if (!JavaValid(paths.Java)) throw new IOException("Загруженный JDK не прошёл проверку версии 17.");
                }
                if (!GradleValid(paths.Gradle))
                {
                    _status = "Загрузка Gradle 8.13…";
                    InstallArchive(client, "https://services.gradle.org/distributions/gradle-8.13-bin.zip", GradleSha256,
                        false, paths.Gradle, request.Os, token);
                }
                if (!SdkValid(paths.Sdk))
                {
                    _status = "Загрузка Android SDK Platform 36 и Build Tools 36…";
                    var document = new XmlDocument { XmlResolver = null };
                    document.LoadXml(GetText(client, "https://dl.google.com/android/repository/repository2-1.xml", token));
                    InstallSdkPackage(client, document, "platforms;android-36", "Android SDK Platform 36",
                        Path.Combine(paths.Sdk, "platforms/android-36"), request.Os, token);
                    InstallSdkPackage(client, document, "build-tools;36.0.0", null,
                        Path.Combine(paths.Sdk, "build-tools/36.0.0"), request.Os, token);
                    string licenses = Path.Combine(request.UnitySdk ?? "", "licenses");
                    if (Directory.Exists(licenses))
                    {
                        Directory.CreateDirectory(Path.Combine(paths.Sdk, "licenses"));
                        foreach (string license in Directory.GetFiles(licenses))
                            File.Copy(license, Path.Combine(paths.Sdk, "licenses", Path.GetFileName(license)), true);
                    }
                }
            }
            return paths;
        }

        private static string Executable(string name) => Path.DirectorySeparatorChar == '\\' ? name + ".exe" : name;
        private static bool JavaValid(string path) => !string.IsNullOrEmpty(path) &&
            File.Exists(Path.Combine(path, "bin", Executable("java"))) &&
            File.Exists(Path.Combine(path, "bin", Executable("javac"))) && File.Exists(Path.Combine(path, "release")) &&
            File.ReadAllText(Path.Combine(path, "release")).Contains("JAVA_VERSION=\"17.");
        private static bool GradleValid(string path) => !string.IsNullOrEmpty(path) &&
            File.Exists(Path.Combine(path, "lib/gradle-launcher-8.13.jar")) &&
            File.Exists(Path.Combine(path, "bin", Path.DirectorySeparatorChar == '\\' ? "gradle.bat" : "gradle"));
        private static bool SdkValid(string path) => !string.IsNullOrEmpty(path) &&
            File.Exists(Path.Combine(path, "platforms/android-36/android.jar")) &&
            File.Exists(Path.Combine(path, "build-tools/36.0.0", Executable("aapt2"))) &&
            File.Exists(Path.Combine(path, "build-tools/36.0.0/lib/d8.jar")) &&
            File.Exists(Path.Combine(path, "build-tools/36.0.0", Executable("zipalign")));
        private static string FindJava(string directory)
        {
            if (!Directory.Exists(directory)) return null;
            return Directory.GetFiles(directory, "release", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName).FirstOrDefault(JavaValid);
        }

        private static string GetText(HttpClient client, string url, CancellationToken token)
        {
            using (var response = client.GetAsync(url, token).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
                return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
        }

        private static void InstallSdkPackage(HttpClient client, XmlDocument document, string id, string displayName,
            string destination, string os, CancellationToken token)
        {
            var package = document.SelectNodes("//*[local-name()='remotePackage']").Cast<XmlElement>()
                .FirstOrDefault(item => item.GetAttribute("path") == id &&
                    (displayName == null || item["display-name"]?.InnerText == displayName));
            var archive = package?.SelectNodes("archives/archive").Cast<XmlElement>().FirstOrDefault(item =>
                item["host-os"] == null || item["host-os"].InnerText == (os == "mac" ? "macosx" : os));
            var complete = archive?["complete"];
            if (complete == null) throw new IOException("Google не вернул Android SDK пакет " + id + " для " + os);
            string url = complete["url"].InnerText;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var absolute)) url = "https://dl.google.com/android/repository/" + url;
            else if (absolute.Scheme != "https" || absolute.Host != "dl.google.com") throw new IOException("Неожиданный адрес SDK.");
            InstallArchive(client, url, complete["checksum"].InnerText, true, destination, os, token);
        }

        private static void InstallArchive(HttpClient client, string url, string checksum, bool sha1,
            string destination, string os, CancellationToken token)
        {
            string parent = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(parent);
            string work = Path.Combine(parent, ".install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                string archive = Path.Combine(work, url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "download.zip" : "download.tar.gz");
                using (var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    downloadTimeout.CancelAfter(TimeSpan.FromMinutes(15));
                    using (var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, downloadTimeout.Token).GetAwaiter().GetResult())
                    {
                        response.EnsureSuccessStatusCode();
                        using (var input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                        using (var output = File.Create(archive))
                            input.CopyToAsync(output, 81920, downloadTimeout.Token).GetAwaiter().GetResult();
                    }
                }
                using (HashAlgorithm hash = sha1 ? (HashAlgorithm)SHA1.Create() : SHA256.Create())
                using (var stream = File.OpenRead(archive))
                    if (!string.Equals(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""), checksum.Trim(), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Контрольная сумма загрузки не совпала: " + url);
                string extracted = Path.Combine(work, "extracted");
                Directory.CreateDirectory(extracted);
                if (archive.EndsWith(".zip", StringComparison.Ordinal))
                {
                    using (var zip = ZipFile.OpenRead(archive))
                        foreach (var entry in zip.Entries)
                        {
                            token.ThrowIfCancellationRequested();
                            string output = Path.GetFullPath(Path.Combine(extracted, entry.FullName));
                            if (!output.StartsWith(extracted + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                                throw new IOException("Некорректный путь в архиве инструментов.");
                            if (entry.Name.Length == 0) { Directory.CreateDirectory(output); continue; }
                            Directory.CreateDirectory(Path.GetDirectoryName(output));
                            entry.ExtractToFile(output);
                            if (os != "windows" && ((entry.ExternalAttributes >> 16) & 73) != 0)
                                Run("chmod", "+x " + Quote(output));
                        }
                }
                else
                {
                    foreach (string entry in Run("tar", "-tzf " + Quote(archive)).Split('\n'))
                    {
                        if (string.IsNullOrWhiteSpace(entry)) continue;
                        string output = Path.GetFullPath(Path.Combine(extracted, entry.TrimEnd('\r')));
                        if (!output.StartsWith(extracted + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                            throw new IOException("Некорректный путь в архиве JDK.");
                    }
                    Run("tar", "-xzf " + Quote(archive) + " -C " + Quote(extracted));
                }
                token.ThrowIfCancellationRequested();
                string[] directories = Directory.GetDirectories(extracted);
                string source = directories.Length == 1 && Directory.GetFiles(extracted).Length == 0 ? directories[0] : extracted;
                // Existing incomplete installations are retained until a validated archive is extracted.
                string previous = destination + ".previous-" + Guid.NewGuid().ToString("N");
                bool moved = Directory.Exists(destination);
                if (moved) Directory.Move(destination, previous);
                try { Directory.Move(source, destination); }
                catch { if (moved) Directory.Move(previous, destination); throw; }
                if (moved) Directory.Delete(previous, true);
            }
            finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
        }

        private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        private static string Run(string file, string arguments)
        {
            using (var process = Process.Start(new ProcessStartInfo(file, arguments)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(300000)) { process.Kill(); throw new IOException(file + " не завершился за 5 минут."); }
                if (process.ExitCode != 0) throw new IOException(file + ": " + errors.GetAwaiter().GetResult());
                return output.GetAwaiter().GetResult();
            }
        }
    }
}
#endif
