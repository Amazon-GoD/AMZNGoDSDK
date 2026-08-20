using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AMZNGoDSDK.Editor.Deploy
{
    /// <summary>
    /// Клиент Yandex.Disk REST API v1. Всё синхронно (методы блокируют вызывающий
    /// поток) — пайплайн запускает их из Task.Run, а UI-поток отдельно рисует
    /// прогресс-бар. Никаких зависимостей кроме .NET + Unity JsonUtility.
    /// </summary>
    internal sealed class YandexDiskUploader
    {
        private const string ApiBase = "https://cloud-api.yandex.net/v1/disk";
        private const int TimeoutMs = 30 * 1000;

        static YandexDiskUploader()
        {
            // Яндекс всегда TLS 1.2+, а Mono под Unity 2022 может по умолчанию
            // не включать TLS12 — форсируем.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        /// <summary>Пингует API, чтобы убедиться что токен валиден.</summary>
        public bool CheckAuth(string token, out string error, out string userLogin)
        {
            error = null;
            userLogin = null;

            if (string.IsNullOrWhiteSpace(token))
            {
                error = "Token is empty.";
                return false;
            }

            try
            {
                var req = BuildRequest(ApiBase, "GET", token);
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var body = new StreamReader(resp.GetResponseStream() ?? Stream.Null))
                {
                    string json = body.ReadToEnd();
                    var info = JsonUtility.FromJson<DiskInfo>(json);
                    userLogin = info != null ? info.user?.login : null;
                    return true;
                }
            }
            catch (WebException we)
            {
                error = ExtractApiError(we);
                return false;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>
        /// Создаёт папку по пути remoteDir, рекурсивно (каждый уровень). Если
        /// папка уже есть — не ошибка. remoteDir должен начинаться с '/'.
        /// </summary>
        public bool EnsureDirectory(string token, string remoteDir, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(remoteDir) || remoteDir == "/")
                return true;

            remoteDir = remoteDir.Replace('\\', '/').TrimEnd('/');
            if (!remoteDir.StartsWith("/"))
                remoteDir = "/" + remoteDir;

            // Создаём каждый уровень отдельно. Пути в API — от корня Диска.
            var segments = remoteDir.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string current = string.Empty;

            foreach (var seg in segments)
            {
                current += "/" + seg;
                if (!CreateSingleDir(token, current, out error))
                    return false;
            }

            return true;
        }

        private static bool CreateSingleDir(string token, string path, out string error)
        {
            error = null;
            string url = $"{ApiBase}/resources?path={UrlEncode(path)}";

            try
            {
                var req = BuildRequest(url, "PUT", token);
                req.ContentLength = 0;
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    // 201 Created — папка создана. Всё ок.
                    return true;
                }
            }
            catch (WebException we)
            {
                // 409 Conflict = папка уже есть, для нас это успех.
                if (we.Response is HttpWebResponse http && http.StatusCode == HttpStatusCode.Conflict)
                    return true;

                error = ExtractApiError(we);
                return false;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>
        /// Загружает localPath на Диск в remotePath. progress вызывается из
        /// фонового потока (значение 0..1). cancelToken позволяет прервать заливку.
        /// </summary>
        public bool UploadFile(
            string token,
            string localPath,
            string remotePath,
            Action<float> progress,
            CancellationToken cancelToken,
            out string error)
        {
            error = null;

            if (!File.Exists(localPath))
            {
                error = $"Local file not found: {localPath}";
                return false;
            }

            remotePath = remotePath.Replace('\\', '/');
            if (!remotePath.StartsWith("/"))
                remotePath = "/" + remotePath;

            // 1. Спрашиваем у Диска, куда лить.
            string uploadHref;
            if (!GetUploadHref(token, remotePath, out uploadHref, out error))
                return false;

            // 2. Льём файл на выданный URL. Никакого auth header тут — URL уже presigned.
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(uploadHref);
                req.Method = "PUT";
                req.Timeout = System.Threading.Timeout.Infinite;
                req.ReadWriteTimeout = TimeoutMs;
                req.AllowWriteStreamBuffering = false;
                req.SendChunked = false;

                var fi = new FileInfo(localPath);
                req.ContentLength = fi.Length;

                using (var reqStream = req.GetRequestStream())
                using (var fileStream = File.OpenRead(localPath))
                {
                    byte[] buffer = new byte[81920];
                    long total = fi.Length;
                    long written = 0;
                    int read;
                    while ((read = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (cancelToken.IsCancellationRequested)
                        {
                            req.Abort();
                            error = "Upload cancelled.";
                            return false;
                        }

                        reqStream.Write(buffer, 0, read);
                        written += read;
                        progress?.Invoke(total > 0 ? (float)written / total : 0f);
                    }
                }

                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    // 201 Created — файл принят и уже виден на Диске.
                    // 202 Accepted — принят, но всё ещё копируется на серверах Яндекса;
                    // для нашего случая это тоже успех.
                    if (resp.StatusCode == HttpStatusCode.Created ||
                        resp.StatusCode == HttpStatusCode.Accepted)
                        return true;

                    error = $"Unexpected upload status: {(int)resp.StatusCode} {resp.StatusCode}";
                    return false;
                }
            }
            catch (WebException we) when (cancelToken.IsCancellationRequested)
            {
                error = "Upload cancelled.";
                return false;
            }
            catch (WebException we)
            {
                error = ExtractApiError(we);
                return false;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private static bool GetUploadHref(string token, string remotePath, out string href, out string error)
        {
            href = null;
            error = null;
            string url = $"{ApiBase}/resources/upload?path={UrlEncode(remotePath)}&overwrite=true";

            try
            {
                var req = BuildRequest(url, "GET", token);
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var body = new StreamReader(resp.GetResponseStream() ?? Stream.Null))
                {
                    string json = body.ReadToEnd();
                    var parsed = JsonUtility.FromJson<UploadLink>(json);
                    if (parsed == null || string.IsNullOrEmpty(parsed.href))
                    {
                        error = "Yandex.Disk did not return an upload URL.";
                        return false;
                    }
                    href = parsed.href;
                    return true;
                }
            }
            catch (WebException we)
            {
                error = ExtractApiError(we);
                return false;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private static HttpWebRequest BuildRequest(string url, string method, string token)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;
            req.Headers["Authorization"] = "OAuth " + token;
            req.Accept = "application/json";
            req.UserAgent = "GoDSDKDeploy/1.0 (Unity Editor)";
            return req;
        }

        private static string UrlEncode(string s) => Uri.EscapeDataString(s);

        private static string ExtractApiError(WebException we)
        {
            if (we.Response is HttpWebResponse http)
            {
                try
                {
                    using (var body = new StreamReader(http.GetResponseStream() ?? Stream.Null))
                    {
                        string json = body.ReadToEnd();
                        var err = JsonUtility.FromJson<ApiError>(json);
                        if (err != null && !string.IsNullOrEmpty(err.message))
                            return $"{(int)http.StatusCode} {http.StatusCode}: {err.message}";
                        return $"{(int)http.StatusCode} {http.StatusCode}: {json}";
                    }
                }
                catch
                {
                    return $"{(int)http.StatusCode} {http.StatusCode}";
                }
            }
            return we.Message;
        }

        [Serializable] private class UploadLink { public string href; public string method; }
        [Serializable] private class ApiError { public string message; public string description; public string error; }
        [Serializable] private class DiskInfo { public DiskUser user; public long total_space; public long used_space; }
        [Serializable] private class DiskUser { public string login; public string display_name; }
    }
}
