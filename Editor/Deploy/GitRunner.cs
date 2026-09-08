using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace AMZNGoDSDK.Editor.Deploy
{
    /// <summary>
    /// Запуск git через Process (паттерн как у ZipArchiver/YandexDiskUploader:
    /// синхронно, out error, без исключений наружу). Никаких абсолютных путей
    /// машины — только рабочая директория, переданная вызывающим кодом.
    /// </summary>
    internal static class GitRunner
    {
        public sealed class GitResult
        {
            public bool   Ok;
            public int    ExitCode;
            public string StdOut = "";
            public string StdErr = "";
        }

        private const int DefaultTimeoutMs = 120 * 1000;

        /// <summary>
        /// Выполняет "git {arguments}" в workingDir. env — дополнительные
        /// переменные окружения процесса (например GIT_INDEX_FILE), может быть null.
        /// </summary>
        public static GitResult Run(
            string workingDir,
            string arguments,
            IDictionary<string, string> env = null,
            int timeoutMs = DefaultTimeoutMs)
        {
            var result = new GitResult();
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                if (env != null)
                {
                    foreach (var kv in env)
                        psi.EnvironmentVariables[kv.Key] = kv.Value;
                }

                using (var process = new Process { StartInfo = psi })
                {
                    process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                    process.ErrorDataReceived  += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); } catch { /* already exited */ }
                        result.ExitCode = -1;
                        result.StdErr = $"git {arguments}: timed out after {timeoutMs} ms";
                        return result;
                    }

                    // Second WaitForExit flushes the async output buffers.
                    process.WaitForExit();

                    result.ExitCode = process.ExitCode;
                    result.StdOut = stdout.ToString().TrimEnd('\r', '\n');
                    result.StdErr = stderr.ToString().TrimEnd('\r', '\n');
                    result.Ok = process.ExitCode == 0;
                    return result;
                }
            }
            catch (Exception e)
            {
                result.ExitCode = -1;
                result.StdErr = $"git {arguments}: {e.Message}";
                return result;
            }
        }

        /// <summary>Удобный хелпер: успех → stdout, провал → false + error.</summary>
        public static bool TryRun(
            string workingDir,
            string arguments,
            out string output,
            out string error,
            IDictionary<string, string> env = null,
            int timeoutMs = DefaultTimeoutMs)
        {
            var r = Run(workingDir, arguments, env, timeoutMs);
            output = r.StdOut;
            error = r.Ok ? null : $"git {arguments} failed (exit {r.ExitCode}): {(string.IsNullOrEmpty(r.StdErr) ? r.StdOut : r.StdErr)}";
            return r.Ok;
        }
    }
}
