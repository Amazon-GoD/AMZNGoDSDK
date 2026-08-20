using System;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor.Deploy
{
    /// <summary>
    /// Batchmode-вход релизного пайплайна (для CI/скриптовых прогонов):
    ///
    ///   Unity -batchmode -projectPath ... -executeMethod
    ///     AMZNGoDSDK.Editor.Deploy.SdkReleaseCli.Run
    ///     -amznReleaseVersion 1.0.0 [-amznReleaseNote "text"] [-amznDryRun]
    ///     [-amznStagingRoot /path] [-amznKeepStaging]
    ///
    /// Завершается EditorApplication.Exit: 0 — успех, 1 — ошибка
    /// (флаг -quit не нужен). Push НЕ выполняется никогда.
    /// </summary>
    internal static class SdkReleaseCli
    {
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();

            string version = GetArgValue(args, "-amznReleaseVersion");
            string note = GetArgValue(args, "-amznReleaseNote");
            string stagingRoot = GetArgValue(args, "-amznStagingRoot");
            bool dryRun = HasArg(args, "-amznDryRun");
            bool keepStaging = HasArg(args, "-amznKeepStaging");

            if (string.IsNullOrWhiteSpace(version))
            {
                Debug.LogError("[SdkReleaseCli] -amznReleaseVersion is required.");
                EditorApplication.Exit(1);
                return;
            }

            var result = SdkReleasePipeline.Run(new SdkReleasePipeline.Request
            {
                Version = version,
                Note = note,
                DryRun = dryRun,
                StagingRoot = stagingRoot,
                KeepStaging = keepStaging,
            });

            if (!result.Ok)
            {
                Debug.LogError($"[SdkReleaseCli] FAILED: {result.Error}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log(
                $"[SdkReleaseCli] OK ({(dryRun ? "dry-run" : "release")}): " +
                $"branch={result.Branch} head={result.HeadSha} files={result.FileCount} " +
                $"dirty={result.WorkingTreeDirty} excluded=[{string.Join("; ", result.ExcludedPaths)}]" +
                (dryRun
                    ? (result.StagingTree != null ? $" stagingTree={result.StagingTree}" : "")
                    : $" commit={result.CommitSha} tag={result.Tag} push=\"{result.PushCommand}\""));

            EditorApplication.Exit(0);
        }

        private static string GetArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        private static bool HasArg(string[] args, string name)
        {
            foreach (var arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
