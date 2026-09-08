using System;
using System.IO;
using System.Text;
using System.Threading;

using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor.Deploy
{
    /// <summary>
    /// Оркестрация деплоя: подготовить папку с датой в LocalRoot, вызвать
    /// SDK-экспортер, дописать манифест, упаковать в zip, опционально залить
    /// на Яндекс.Диск. Фаза с AssetDatabase (Prepare) — только с главного потока,
    /// фаза с zip/http (PackAndUpload) — из Task.Run.
    /// </summary>
    internal static class SdkDeployPipeline
    {
        public sealed class Request
        {
            public string LocalRoot;
            public bool   UploadEnabled;
            public string YandexToken;
            public string RemoteDir;
        }

        public sealed class Result
        {
            public bool   Ok;
            public string Error;
            public string TimestampFolder;   // A:\GoDSDK\AMZNGoDSDK_.../
            public string UnitypackagePath;  // ...\AMZNGoDSDK.unitypackage
            public string ZipPath;           // A:\GoDSDK\AMZNGoDSDK_....zip
            public string RemoteZipPath;     // /SDK/AMZNGoDSDK_....zip (если аплоад успешен)
            public string RemoteViewUrl;     // https://disk.yandex.ru/client/disk/SDK  (для "Open on Disk")
        }

        public sealed class Progress
        {
            private int _pct100;      // 0..10000
            private string _msg = "";

            public string Message
            {
                get { lock (this) return _msg; }
                set { lock (this) _msg = value ?? ""; }
            }

            public float Value => Interlocked.CompareExchange(ref _pct100, 0, 0) / 10000f;

            public void Report(float value01)
            {
                int clamped = Mathf.Clamp((int)(value01 * 10000f), 0, 10000);
                Interlocked.Exchange(ref _pct100, clamped);
            }

            public void ReportStep(string message, float value01)
            {
                Message = message;
                Report(value01);
            }
        }

        /// <summary>
        /// Главный поток: создаёт папку с датой в LocalRoot и вызывает
        /// SdkPackageExporter.ExportPackageToPath. По успеху заполняет
        /// TimestampFolder + UnitypackagePath.
        /// </summary>
        public static Result PrepareAndExport(Request req)
        {
            var result = new Result();

            if (req == null || string.IsNullOrWhiteSpace(req.LocalRoot))
            {
                result.Error = "Local export root is not set.";
                return result;
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string baseName = $"AMZNGoDSDK_{timestamp}";
            string timestampFolder = Path.Combine(req.LocalRoot, baseName);

            try
            {
                Directory.CreateDirectory(req.LocalRoot);

                if (Directory.Exists(timestampFolder))
                {
                    result.Error = $"Target folder already exists: {timestampFolder}";
                    return result;
                }
                Directory.CreateDirectory(timestampFolder);
            }
            catch (Exception e)
            {
                result.Error = $"Failed to prepare folder: {e.Message}";
                return result;
            }

            string unitypackagePath = Path.Combine(timestampFolder, "AMZNGoDSDK.unitypackage");

            if (!SdkPackageExporter.ExportPackageToPath(unitypackagePath, out string exportError))
            {
                result.Error = $"SDK export failed: {exportError}";
                TryCleanupOnFail(timestampFolder);
                return result;
            }

            try
            {
                File.WriteAllText(
                    Path.Combine(timestampFolder, "manifest.json"),
                    BuildManifest(timestamp, unitypackagePath),
                    Encoding.UTF8);
            }
            catch (Exception e)
            {
                // Манифест — просто метаданные, без него релиз всё равно валидный.
                Debug.LogWarning($"[GoDSDKDeploy] Manifest write failed (non-fatal): {e.Message}");
            }

            result.Ok = true;
            result.TimestampFolder = timestampFolder;
            result.UnitypackagePath = unitypackagePath;
            return result;
        }

        /// <summary>
        /// Фоновый поток: пакует TimestampFolder в zip рядом; если UploadEnabled —
        /// создаёт папку на Диске, льёт zip, формирует RemoteZipPath/RemoteViewUrl.
        /// </summary>
        public static void PackAndUpload(
            Request req,
            Result result,
            Progress progress,
            CancellationToken cancelToken)
        {
            if (!result.Ok)
                return;

            string baseName = Path.GetFileName(result.TimestampFolder);
            string zipPath = Path.Combine(req.LocalRoot, baseName + ".zip");

            progress.ReportStep("Packing zip...", 0.05f);

            if (!ZipArchiver.CreateFromDirectory(result.TimestampFolder, zipPath, out string zipError))
            {
                result.Ok = false;
                result.Error = $"Zip failed: {zipError}";
                return;
            }
            result.ZipPath = zipPath;

            if (cancelToken.IsCancellationRequested)
            {
                result.Ok = false;
                result.Error = "Cancelled after zip.";
                return;
            }

            if (!req.UploadEnabled)
            {
                progress.ReportStep("Done (no upload).", 1f);
                return;
            }

            if (string.IsNullOrWhiteSpace(req.YandexToken))
            {
                result.Ok = false;
                result.Error = "Yandex OAuth token is empty — set it in the window or disable upload.";
                return;
            }

            var uploader = new YandexDiskUploader();

            progress.ReportStep("Yandex.Disk: preparing folder...", 0.1f);
            if (!uploader.EnsureDirectory(req.YandexToken, req.RemoteDir, out string dirError))
            {
                result.Ok = false;
                result.Error = $"Yandex.Disk mkdir failed: {dirError}";
                return;
            }

            string remoteZip = TrimEndSlash(req.RemoteDir) + "/" + Path.GetFileName(zipPath);

            progress.ReportStep($"Uploading {Path.GetFileName(zipPath)}...", 0.15f);

            bool uploaded = uploader.UploadFile(
                req.YandexToken,
                zipPath,
                remoteZip,
                v => progress.Report(0.15f + v * 0.85f),
                cancelToken,
                out string uploadError);

            if (!uploaded)
            {
                result.Ok = false;
                result.Error = $"Upload failed: {uploadError}";
                return;
            }

            result.RemoteZipPath = remoteZip;
            result.RemoteViewUrl = $"https://disk.yandex.ru/client/disk{TrimEndSlash(req.RemoteDir)}";
            progress.ReportStep("Done.", 1f);
        }

        private static string TrimEndSlash(string s) =>
            string.IsNullOrEmpty(s) ? "/" : ("/" + s.Replace('\\', '/').Trim('/'));

        private static string BuildManifest(string timestamp, string unitypackagePath)
        {
            long size = 0;
            try { size = new FileInfo(unitypackagePath).Length; } catch { }

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"timestamp\": \"{timestamp}\",\n");
            sb.Append($"  \"unityVersion\": \"{Application.unityVersion}\",\n");
            sb.Append($"  \"unitypackage\": \"{Path.GetFileName(unitypackagePath)}\",\n");
            sb.Append($"  \"sizeBytes\": {size}\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static void TryCleanupOnFail(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch { /* лучше оставить, чем потерять полезное */ }
        }
    }
}
