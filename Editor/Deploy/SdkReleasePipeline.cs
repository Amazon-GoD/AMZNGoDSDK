using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AMZNGoDSDK.Editor.Deploy
{
    /// <summary>
    /// Режим «Release (UPM)»: собирает чистое релизное дерево пакета из
    /// git-дерева ТЕКУЩЕЙ ветки (git archive HEAD), применяет exclusion-список,
    /// прячет sample-папку (AmznGoDSDK → AmznGoDSDK~), проставляет версию,
    /// верифицирует дерево и (вне dry-run) создаёт коммит в ветке Releases +
    /// аннотированный тег vX.Y.Z. Рабочее дерево dev-проекта НЕ трогается:
    /// коммит делается через временный index (GIT_INDEX_FILE) с --work-tree на
    /// временную папку. Push НЕ выполняется никогда — только команда для юзера.
    /// </summary>
    internal static class SdkReleasePipeline
    {
        /// <summary>
        /// Папки, исключаемые из релизного дерева (относительно корня пакета).
        /// Паттерн — как ExcludedFolders в SdkPackageExporter.
        /// </summary>
        public static readonly string[] ExcludedFolders =
        {
            "Runtime/Modules/InAppPurchase/Testing", // тестовый IAP-контент (инцидент 2026-08-20)
            "Editor/Deploy",                          // сам деплой-инструмент — внутренний
        };

        public const string SampleVisibleFolder = "AmznGoDSDK";
        public const string SampleHiddenFolder = "AmznGoDSDK~";
        public const string SampleVisiblePath = "AmznGoDSDK/SDKPrefab";
        public const string SampleHiddenPath = "AmznGoDSDK~/SDKPrefab";

        public sealed class Request
        {
            public string Version;      // "1.0.0"
            public string Note;         // строка для changelog-заметки в теле коммита
            public bool   DryRun;
            /// <summary>Куда складывать временное дерево; null → системный temp.</summary>
            public string StagingRoot;
            /// <summary>Оставить staging-папку после прогона (для инспекции dry-run).</summary>
            public bool   KeepStaging;
        }

        public sealed class Result
        {
            public bool   Ok;
            public string Error;
            public string Branch;          // ветка, из HEAD которой собран релиз
            public string HeadSha;
            public bool   WorkingTreeDirty;
            public string StagingTree;     // путь к собранному дереву (если KeepStaging)
            public int    FileCount;       // файлов в релизном дереве
            public List<string> ExcludedPaths = new List<string>();   // что реально удалили
            public string CommitSha;       // коммит в Releases (не dry-run)
            public string Tag;             // vX.Y.Z (не dry-run)
            public string PushCommand;     // команда для ручного push
        }

        private static readonly Regex VersionRegex = new Regex(@"^(\d+)\.(\d+)\.(\d+)$", RegexOptions.Compiled);
        private static readonly Regex TagRegex = new Regex(@"^v(\d+)\.(\d+)\.(\d+)$", RegexOptions.Compiled);

        /// <summary>Абсолютный путь к корню SDK-репозитория (dev: Assets/AMZNGoDSDK).</summary>
        public static string RepoRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "AMZNGoDSDK"));

        public static Result Run(Request req)
        {
            var result = new Result();

            if (req == null || string.IsNullOrWhiteSpace(req.Version))
            {
                result.Error = "Release version is not set.";
                return result;
            }

            string version = req.Version.Trim();
            if (!VersionRegex.IsMatch(version))
            {
                result.Error = $"Version '{version}' is not a valid semver (expected X.Y.Z).";
                return result;
            }

            string repoRoot = RepoRoot;
            if (!Directory.Exists(Path.Combine(repoRoot, ".git")))
            {
                result.Error = $"Git repository not found at {repoRoot}.";
                return result;
            }

            // --- Git-состояние ---
            if (!GitRunner.TryRun(repoRoot, "rev-parse --abbrev-ref HEAD", out string branch, out string err))
            {
                result.Error = err;
                return result;
            }
            result.Branch = branch;

            if (!GitRunner.TryRun(repoRoot, "rev-parse HEAD", out string headSha, out err))
            {
                result.Error = err;
                return result;
            }
            result.HeadSha = headSha;

            if (GitRunner.TryRun(repoRoot, "status --porcelain", out string statusOut, out _))
                result.WorkingTreeDirty = !string.IsNullOrEmpty(statusOut);

            // --- Версия должна быть выше последнего релизного тега ---
            if (!ValidateVersionAgainstTags(repoRoot, version, out err))
            {
                result.Error = err;
                return result;
            }

            string tagName = "v" + version;
            if (GitRunner.Run(repoRoot, $"rev-parse -q --verify refs/tags/{tagName}").Ok)
            {
                result.Error = $"Tag {tagName} already exists.";
                return result;
            }

            // --- Staging ---
            string stagingRoot = string.IsNullOrWhiteSpace(req.StagingRoot)
                ? Path.Combine(Path.GetTempPath(), "AmznGoDSdkRelease_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"))
                : req.StagingRoot;
            string tree = Path.Combine(stagingRoot, "tree");

            try
            {
                BuildReleaseTree(repoRoot, stagingRoot, tree, version, result);

                // --- Верификация ПЕРЕД коммитом ---
                if (!SdkReleaseTreeVerifier.Verify(tree, version, out string verifyError))
                {
                    result.Error = verifyError;
                    return result;
                }

                result.FileCount = CountFiles(tree);

                if (req.DryRun)
                {
                    result.Ok = true;
                    return result;
                }

                // --- Публикация: коммит в Releases + тег (БЕЗ push) ---
                if (!CommitToReleases(repoRoot, tree, stagingRoot, version, req.Note, tagName, result))
                    return result;

                result.Ok = true;
                result.Tag = tagName;
                result.PushCommand = $"git push origin Releases {tagName}";
                return result;
            }
            catch (Exception e)
            {
                result.Error = e.Message;
                return result;
            }
            finally
            {
                if (req.KeepStaging)
                {
                    result.StagingTree = tree;
                }
                else
                {
                    try { if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true); }
                    catch { /* staging в temp, ОС приберёт */ }
                }
            }
        }

        /// <summary>Собирает релизное дерево: git archive HEAD → exclusions → sample~ → версия.</summary>
        private static void BuildReleaseTree(string repoRoot, string stagingRoot, string tree, string version, Result result)
        {
            Directory.CreateDirectory(stagingRoot);
            if (Directory.Exists(tree))
                Directory.Delete(tree, recursive: true);
            Directory.CreateDirectory(tree);

            // git archive пишет ТОЛЬКО закоммиченное состояние HEAD текущей ветки.
            string zipPath = Path.Combine(stagingRoot, "src.zip");
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            if (!GitRunner.TryRun(repoRoot, $"archive --format=zip -o \"{zipPath}\" HEAD", out _, out string err))
                throw new Exception(err);

            ZipFile.ExtractToDirectory(zipPath, tree);
            File.Delete(zipPath);

            // Exclusion-список: папка + её .meta.
            foreach (var rel in ExcludedFolders)
            {
                string dir = Path.Combine(tree, rel.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                    result.ExcludedPaths.Add(rel + "/");
                }

                string meta = dir + ".meta";
                if (File.Exists(meta))
                {
                    File.Delete(meta);
                    result.ExcludedPaths.Add(rel + ".meta");
                }
            }

            // Sample: AmznGoDSDK → AmznGoDSDK~ (скрыта для Unity, доступна как UPM sample).
            // .meta самой папки удаляется — скрытым записям meta не положен
            // (инцидент 2026-08-20: пары "~ + ~.meta" утекали пустыми folder-энтри).
            // Внутренние .meta остаются — GUID сохраняются при импорте sample.
            string visibleSample = Path.Combine(tree, SampleVisibleFolder);
            string hiddenSample = Path.Combine(tree, SampleHiddenFolder);
            if (Directory.Exists(visibleSample))
            {
                Directory.Move(visibleSample, hiddenSample);
                string sampleMeta = visibleSample + ".meta";
                if (File.Exists(sampleMeta))
                    File.Delete(sampleMeta);
                result.ExcludedPaths.Add(SampleVisibleFolder + "/ -> " + SampleHiddenFolder + "/ (hidden)");
            }

            // package.json релизного дерева: версия из UI + скрытый sample path.
            // Dev-дерево НЕ меняется.
            PatchPackageJson(Path.Combine(tree, "package.json"), version);
        }

        private static void PatchPackageJson(string packageJsonPath, string version)
        {
            if (!File.Exists(packageJsonPath))
                throw new Exception("package.json not found in release tree.");

            string text = File.ReadAllText(packageJsonPath);

            var versionField = new Regex("(\"version\"\\s*:\\s*\")[^\"]+(\")");
            if (!versionField.IsMatch(text))
                throw new Exception("package.json has no version field to patch.");
            text = versionField.Replace(text, "${1}" + version + "${2}", 1);

            text = text.Replace("\"" + SampleVisiblePath + "\"", "\"" + SampleHiddenPath + "\"");

            File.WriteAllText(packageJsonPath, text, new UTF8Encoding(false));
        }

        /// <summary>
        /// Требование: новая версия строго больше максимального существующего
        /// релизного тега vX.Y.Z (если тегов нет — это первый релиз, ок).
        /// </summary>
        private static bool ValidateVersionAgainstTags(string repoRoot, string version, out string error)
        {
            error = null;

            if (!GitRunner.TryRun(repoRoot, "tag --list", out string tagsOut, out string err))
            {
                error = err;
                return false;
            }

            var newVersion = ParseVersion(version);
            int[] maxVersion = null;
            string maxTag = null;

            foreach (var line in tagsOut.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var m = TagRegex.Match(line.Trim());
                if (!m.Success)
                    continue;

                var v = new[]
                {
                    int.Parse(m.Groups[1].Value),
                    int.Parse(m.Groups[2].Value),
                    int.Parse(m.Groups[3].Value),
                };

                if (maxVersion == null || Compare(v, maxVersion) > 0)
                {
                    maxVersion = v;
                    maxTag = line.Trim();
                }
            }

            if (maxVersion != null && Compare(newVersion, maxVersion) <= 0)
            {
                error = $"Version {version} must be greater than the latest release tag {maxTag}.";
                return false;
            }

            return true;
        }

        private static int[] ParseVersion(string version)
        {
            var m = VersionRegex.Match(version);
            return new[]
            {
                int.Parse(m.Groups[1].Value),
                int.Parse(m.Groups[2].Value),
                int.Parse(m.Groups[3].Value),
            };
        }

        private static int Compare(int[] a, int[] b)
        {
            for (int i = 0; i < 3; i++)
            {
                if (a[i] != b[i])
                    return a[i].CompareTo(b[i]);
            }
            return 0;
        }

        /// <summary>
        /// Коммит релизного дерева в ветку Releases через временный index:
        /// add -A (work-tree = staging) → write-tree → commit-tree
        /// (первый релиз — orphan без родителя, дальше родитель — tip Releases)
        /// → update-ref → tag -a. Рабочее дерево и index dev-проекта не трогаются.
        /// </summary>
        private static bool CommitToReleases(
            string repoRoot,
            string tree,
            string stagingRoot,
            string version,
            string note,
            string tagName,
            Result result)
        {
            string gitDir = Path.Combine(repoRoot, ".git");
            string tempIndex = Path.Combine(stagingRoot, "release-index");
            if (File.Exists(tempIndex))
                File.Delete(tempIndex);

            var env = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = tempIndex };
            string baseArgs = $"--git-dir=\"{gitDir}\" --work-tree=\"{tree}\"";

            if (!GitRunner.TryRun(tree, $"{baseArgs} add -A .", out _, out string err, env))
            {
                result.Error = err;
                return false;
            }

            if (!GitRunner.TryRun(tree, $"{baseArgs} write-tree", out string treeSha, out err, env))
            {
                result.Error = err;
                return false;
            }

            // Родитель: существующий tip Releases (если ветка уже есть).
            string parentArg = "";
            var parentProbe = GitRunner.Run(repoRoot, "rev-parse -q --verify refs/heads/Releases");
            if (parentProbe.Ok && !string.IsNullOrEmpty(parentProbe.StdOut))
                parentArg = $"-p {parentProbe.StdOut.Trim()} ";

            // Сообщение — через файл (безопасно для переводов строк).
            string messageFile = Path.Combine(stagingRoot, "release-msg.txt");
            var message = new StringBuilder();
            message.AppendLine($"Release v{version}");
            if (!string.IsNullOrWhiteSpace(note))
            {
                message.AppendLine();
                message.AppendLine(note.Trim());
            }
            File.WriteAllText(messageFile, message.ToString(), new UTF8Encoding(false));

            if (!GitRunner.TryRun(repoRoot, $"commit-tree {treeSha.Trim()} {parentArg}-F \"{messageFile}\"", out string commitSha, out err))
            {
                result.Error = err;
                return false;
            }
            commitSha = commitSha.Trim();

            if (!GitRunner.TryRun(repoRoot, $"update-ref refs/heads/Releases {commitSha}", out _, out err))
            {
                result.Error = err;
                return false;
            }

            if (!GitRunner.TryRun(repoRoot, $"tag -a {tagName} -m \"Release {tagName}\" {commitSha}", out _, out err))
            {
                result.Error = $"Releases updated to {commitSha}, but tagging failed: {err}";
                return false;
            }

            result.CommitSha = commitSha;
            return true;
        }

        private static int CountFiles(string tree)
        {
            int count = 0;
            foreach (var _ in Directory.EnumerateFiles(tree, "*", SearchOption.AllDirectories))
                count++;
            return count;
        }
    }
}
