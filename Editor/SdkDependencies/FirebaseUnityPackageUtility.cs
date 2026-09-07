using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

namespace AMZNGoDSDK.Editor
{
    /// <summary>Читает официальный unitypackage в staging и заменяет только файлы Firebase.</summary>
    internal static class FirebaseUnityPackageUtility
    {
        private static readonly string[] Roots =
        {
            "Assets/Firebase", "Assets/Plugins/iOS/Firebase", "Assets/Plugins/tvOS/Firebase",
            "Assets/Editor Default Resources/Firebase", "Assets/Plugins/Android/FirebaseCrashlytics.androidlib"
        };
        private static readonly Regex ManifestName = new Regex(
            @"^Firebase(Analytics|RemoteConfig|Crashlytics)_version-\d+\.\d+\.\d+_manifest\.txt$");
        private const string GeneratedRoot = "Assets/GeneratedLocalRepo/Firebase";
        private const string CrashlyticsLibrary = "Assets/Plugins/Android/FirebaseCrashlytics.androidlib";
        internal const string DisabledDependencySuffix = ".amzngodsdk-disabled";
        private static readonly Regex DependencyPath = new Regex(
            @"^Assets/Firebase/Editor/(App|Analytics|RemoteConfig|Crashlytics)Dependencies\.xml$");

        private static string DisabledDependencyPath(string path)
        {
            bool meta = path.EndsWith(".meta", StringComparison.Ordinal);
            string asset = meta ? path.Substring(0, path.Length - 5) : path;
            return DependencyPath.IsMatch(asset) ? asset + DisabledDependencySuffix + (meta ? ".meta" : "") : null;
        }

        internal static string ResolveInstalledPath(string path)
        {
            string disabled = DisabledDependencyPath(path);
            if (disabled == null) return path;
            if (File.Exists(path) && File.Exists(disabled))
                throw new IOException("Оба варианта зависимости Firebase существуют: " + path + ", " + disabled);
            return File.Exists(disabled) ? disabled : path;
        }

        internal static void SetDependencyState(Dictionary<string, string> files, bool enabled)
        {
            if (enabled) return;
            foreach (string path in files.Keys.ToArray())
            {
                string disabled = DisabledDependencyPath(path);
                if (disabled == null) continue;
                files.Add(disabled, files[path]);
                files.Remove(path);
            }
        }
        internal static bool IsOwned(string path)
        {
            return Roots.Any(root => path == root || path == root + ".meta" ||
                path.StartsWith(root + "/", StringComparison.Ordinal));
        }

        internal static bool HasInstalledAssets()
        {
            return Roots.SelectMany(Files).Concat(Files(GeneratedRoot)).Any(path =>
            {
                if (IsConfiguration(path)) return false;
                string name = Path.GetFileName(path);
                return name.StartsWith("Firebase", StringComparison.OrdinalIgnoreCase) &&
                           (Regex.IsMatch(name, @"\.(dll|so|bundle|a|aar|srcaar)(\.meta)?$", RegexOptions.IgnoreCase) ||
                            name.IndexOf("_manifest.txt", StringComparison.OrdinalIgnoreCase) >= 0) ||
                       name.StartsWith("libFirebase", StringComparison.OrdinalIgnoreCase) ||
                       name.EndsWith("Dependencies.xml", StringComparison.OrdinalIgnoreCase) ||
                       name.EndsWith("Dependencies.xml" + DisabledDependencySuffix, StringComparison.OrdinalIgnoreCase) ||
                       path.IndexOf("/m2repository/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       path.StartsWith(CrashlyticsLibrary + "/", StringComparison.Ordinal);
            });
        }

        internal static string CheckedPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Contains('\\') || path.Split('/').Any(part =>
                    string.IsNullOrEmpty(part) || part == "." || part == ".." ||
                    part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal) ||
                    part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new IOException("Недопустимый путь Firebase: " + path);
            string full = Path.GetFullPath(path);
            string project = Path.GetFullPath(".") + Path.DirectorySeparatorChar;
            if (!full.StartsWith(project, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Путь за пределами проекта: " + path);
            for (string current = full; current != null && current.Length >= project.Length; current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Установка через symlink/junction запрещена: " + current);
            }
            return full;
        }

        internal static IEnumerable<string> Files(string root)
        {
            CheckedPath(root);
            if (!Directory.Exists(root))
                yield break;
            foreach (string file in Directory.GetFiles(root))
            {
                string path = file.Replace('\\', '/');
                CheckedPath(path);
                yield return path;
            }
            foreach (string directory in Directory.GetDirectories(root))
                foreach (string file in Files(directory.Replace('\\', '/')))
                    yield return file;
        }

        internal static void StagePackage(string archive, string staging,
            Dictionary<string, string> files, CancellationToken cancellation)
        {
            Directory.CreateDirectory(CheckedPath(staging));
            var header = new byte[512];
            var buffer = new byte[65536];
            long total = 0;
            using (var source = File.OpenRead(archive))
            using (var gzip = new GZipStream(source, CompressionMode.Decompress))
            {
                while (ReadBlock(gzip, header))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (header.All(value => value == 0))
                        continue;
                    long checksum = Octal(header, 148, 8);
                    long actual = header.Select((value, index) => index >= 148 && index < 156 ? 32 : (int)value).Sum();
                    if (checksum != actual)
                        throw new IOException("Повреждён tar header: " + archive);
                    string name = Text(header, 0, 100);
                    long size = Octal(header, 124, 12);
                    byte type = header[156];
                    if (type == '5' && Regex.IsMatch(name, @"^[a-fA-F0-9]{32}/?$") && size == 0)
                        continue;
                    if ((type != 0 && type != '0') || !Regex.IsMatch(name,
                            @"^[a-fA-F0-9]{32}/(asset|asset\.meta|pathname|preview\.png)$") ||
                        Text(header, 345, 155).Length != 0 || size > 1024L * 1024 * 1024 ||
                        (total += size) > 4L * 1024 * 1024 * 1024)
                        throw new IOException("Неожиданная tar-запись: " + name);
                    string output = CheckedPath(staging + "/" + name);
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    using (var destination = new FileStream(output, FileMode.CreateNew))
                        CopyBytes(gzip, destination, size, buffer, cancellation);
                    CopyBytes(gzip, Stream.Null, (512 - size % 512) % 512, buffer, cancellation);
                }
            }

            foreach (string directory in Directory.GetDirectories(staging))
            {
                cancellation.ThrowIfCancellationRequested();
                string pathname = Path.Combine(directory, "pathname");
                if (!File.Exists(pathname) || new FileInfo(pathname).Length > 4096)
                    throw new IOException("Отсутствует или повреждён pathname: " + directory);
                string path = File.ReadAllText(pathname).TrimEnd('\r', '\n', '\0');
                CheckedPath(path);
                if (path == "Assets/ExternalDependencyManager" || path.StartsWith("Assets/ExternalDependencyManager/", StringComparison.Ordinal))
                    continue;
                string metadata = Path.Combine(directory, "asset.meta");
                if (!File.Exists(metadata) || new FileInfo(metadata).Length > 1024 * 1024)
                    throw new IOException("Отсутствует metadata: " + path);
                string meta = File.ReadAllText(metadata);
                bool folder = Regex.IsMatch(meta, @"(?m)^folderAsset: yes\s*$");
                // Unity exports can include shared parent folders; их GUID принадлежат проекту.
                if (folder && Roots.Any(root => root.StartsWith(path + "/", StringComparison.Ordinal)))
                    continue;
                if (!IsOwned(path) || IsConfiguration(path))
                    throw new IOException("Пакет содержит неожиданный файл: " + path);
                if (!Regex.IsMatch(meta, @"(?m)^guid: " + Path.GetFileName(directory) + @"\s*$"))
                    throw new IOException("GUID не совпадает с tar entry: " + path);
                AddFile(files, path + ".meta", metadata);
                if (folder)
                    continue;
                string asset = Path.Combine(directory, "asset");
                if (!File.Exists(asset))
                    throw new IOException("В пакете нет asset: " + path);
                if (path.EndsWith("_manifest.txt", StringComparison.Ordinal))
                {
                    if (!ManifestName.IsMatch(Path.GetFileName(path)))
                        throw new IOException("Неизвестный продукт в архиве: " + path);
                    // Не отдаём Version Handler ссылки на вложенную копию EDM4U.
                    var lines = File.ReadAllLines(asset).Where(line => IsOwned(line.Trim()));
                    File.WriteAllLines(asset, lines, new UTF8Encoding(false));
                }
                AddFile(files, path, asset);
            }
        }

        private static void AddFile(Dictionary<string, string> files, string path, string source)
        {
            if (files.TryGetValue(path, out string previous))
            {
                if (!SameFile(previous, source))
                    throw new IOException("Общие файлы пакетов различаются: " + path);
                return;
            }
            files.Add(path, source);
        }

        internal static bool SameFile(string first, string second)
        {
            if (new FileInfo(first).Length != new FileInfo(second).Length)
                return false;
            using (var hash = SHA256.Create())
            using (var a = File.OpenRead(first))
            using (var b = File.OpenRead(second))
                return hash.ComputeHash(a).SequenceEqual(hash.ComputeHash(b));
        }

        internal static void ValidatePinned(Func<string, string> resolve)
        {
            var expected = new Dictionary<string, string>
            {
                { "App", "firebase-common:21.0.0" }, { "Analytics", "firebase-analytics:22.4.0" },
                { "RemoteConfig", "firebase-config:22.1.0" }, { "Crashlytics", "firebase-crashlytics-ndk:19.4.2" }
            };
            foreach (var product in expected)
            {
                string dependencies = resolve("Assets/Firebase/Editor/" + product.Key + "Dependencies.xml");
                var xml = new XmlDocument { XmlResolver = null };
                using (var reader = XmlReader.Create(dependencies, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
                    xml.Load(reader);
                var specs = xml.SelectNodes("/dependencies/androidPackages/androidPackage").Cast<XmlNode>()
                    .Select(node => node.Attributes?["spec"]?.Value).ToArray();
                if (!specs.Contains("com.google.firebase:" + product.Value))
                    throw new IOException("Неверная Android-зависимость: " + product.Value);
                string unityArtifact = product.Key == "RemoteConfig" ? "config" : product.Key.ToLowerInvariant();
                if (!specs.Contains("com.google.firebase:firebase-" + unityArtifact + "-unity:" + FirebasePackageInstaller.UnityVersion))
                    throw new IOException("Неверная Android-версия Firebase Unity: " + product.Key);
                string meta = File.ReadAllText(resolve("Assets/Firebase/Plugins/Firebase." + product.Key + ".dll.meta"));
                if (!Regex.IsMatch(meta, @"(?m)^- gvh_version-" + Regex.Escape(FirebasePackageInstaller.UnityVersion) + @"\s*$"))
                    throw new IOException("Неверная версия Firebase." + product.Key + ".dll");
                if (!File.Exists(resolve("Assets/Firebase/Plugins/Firebase." + product.Key + ".dll")))
                    throw new IOException("Отсутствует Firebase." + product.Key + ".dll");
                if (product.Key == "App") continue;
                string manifest = resolve("Assets/Firebase/Editor/Firebase" + product.Key +
                    "_version-" + FirebasePackageInstaller.UnityVersion + "_manifest.txt");
                foreach (string line in File.ReadLines(manifest))
                {
                    string path = line.Trim();
                    CheckedPath(path);
                    if (!IsOwned(path) || IsConfiguration(path))
                        throw new IOException("Неожиданный путь в manifest: " + path);
                    string asset = resolve(path);
                    // После импорта EDM4U может активировать srcaar, переименовав его в aar.
                    if (!File.Exists(asset) && path.EndsWith(".srcaar", StringComparison.Ordinal))
                        asset = resolve(path.Substring(0, path.Length - 7) + ".aar");
                    if (!File.Exists(asset) || !File.Exists(resolve(path + ".meta")) && !File.Exists(asset + ".meta"))
                        throw new IOException("Неполный комплект Firebase: " + path);
                }
            }
        }

        internal static HashSet<string> ExistingFiles(IReadOnlyDictionary<string, string> incoming)
        {
            var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = Roots.SelectMany(Files).Concat(Files(GeneratedRoot)).ToArray();
            foreach (string manifest in current.Where(path => path.EndsWith("_manifest.txt", StringComparison.Ordinal)))
            {
                if (!ManifestName.IsMatch(Path.GetFileName(manifest)))
                    throw new IOException("Обнаружен другой продукт Firebase: " + manifest + ". Удалите его штатным способом перед сменой версии.");
                owned.Add(manifest);
                owned.Add(manifest + ".meta");
                foreach (string line in File.ReadLines(manifest))
                {
                    string path = line.Trim();
                    if (path.Length == 0) continue;
                    CheckedPath(path);
                    if (!IsOwned(path) || IsConfiguration(path))
                        continue;
                    if (!Directory.Exists(path))
                    {
                        owned.Add(path);
                        owned.Add(path + ".meta");
                        string disabled = DisabledDependencyPath(path);
                        if (disabled != null)
                        {
                            owned.Add(disabled);
                            owned.Add(disabled + ".meta");
                        }
                    }
                    // EDM4U переименовывает srcaar после разрешения Android dependencies.
                    if (path.EndsWith(".srcaar", StringComparison.Ordinal))
                    {
                        string aar = path.Substring(0, path.Length - 7) + ".aar";
                        owned.Add(aar);
                        owned.Add(aar + ".meta");
                    }
                    if (Regex.IsMatch(path, @"^Assets/Firebase/m2repository/com/google/firebase/firebase-(app|analytics|config|crashlytics)-unity/\d+\.\d+\.\d+/[^/]+\.(pom|srcaar)$"))
                    {
                        string generated = path.Replace("Assets/Firebase/", GeneratedRoot + "/").Replace(".srcaar", ".aar");
                        owned.Add(generated);
                        owned.Add(generated + ".meta");
                    }
                }
            }
            // Эти файлы генерирует Firebase.Crashlytics.Editor; остальное содержимое androidlib сохраняется.
            foreach (string relative in new[] { "AndroidManifest.xml", "project.properties",
                         "res/values/crashlytics_unity_version.xml", "res/values/crashlytics_build_id.xml" })
            {
                owned.Add(CrashlyticsLibrary + "/" + relative);
                owned.Add(CrashlyticsLibrary + "/" + relative + ".meta");
            }
            foreach (string path in current)
            {
                if (owned.Contains(path) || incoming.ContainsKey(path) || IsConfiguration(path))
                    continue;
                // Не удаляем неизвестные файлы: останавливаемся, если они могут смешать версии SDK.
                if (Regex.IsMatch(path, @"\.(dll|so|bundle|a|aar|srcaar|pom)$", RegexOptions.IgnoreCase) ||
                    path.EndsWith("Dependencies.xml", StringComparison.Ordinal) ||
                    path.EndsWith("Dependencies.xml" + DisabledDependencySuffix, StringComparison.Ordinal))
                    throw new IOException("Неизвестный файл Firebase вне manifest: " + path + ". Нужна ручная проверка перед установкой.");
            }
            return owned;
        }

        internal static void Commit(Dictionary<string, string> files, HashSet<string> previous, string backup)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in previous)
                for (string parent = Path.GetDirectoryName(path)?.Replace('\\', '/'); parent != null &&
                     (IsOwned(parent) || parent == GeneratedRoot || parent.StartsWith(GeneratedRoot + "/", StringComparison.Ordinal));
                     parent = Path.GetDirectoryName(parent)?.Replace('\\', '/'))
                    candidates.Add(parent);
            var obsoleteDirectories = new List<string>();
            foreach (string directory in candidates.OrderByDescending(path => path.Length))
            {
                CheckedPath(directory);
                if (Directory.Exists(directory) && !files.Keys.Any(path => path.StartsWith(directory + "/", StringComparison.Ordinal)) &&
                    Directory.EnumerateFileSystemEntries(directory).All(entry =>
                        obsoleteDirectories.Contains(entry.Replace('\\', '/')) || previous.Contains(entry.Replace('\\', '/'))))
                {
                    obsoleteDirectories.Add(directory);
                    previous.Add(directory + ".meta");
                }
            }
            string[] affected = previous.Concat(files.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !IsConfiguration(path)).ToArray();
            var existed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var createdDirectories = new List<string>();
            foreach (string path in affected)
            {
                CheckedPath(path);
                if (Directory.Exists(path))
                    throw new IOException("Файл конфликтует с папкой: " + path);
                if (!File.Exists(path))
                    continue;
                string saved = CheckedPath(backup + "/" + path);
                Directory.CreateDirectory(Path.GetDirectoryName(saved));
                File.Copy(path, saved, false);
                existed.Add(path);
            }
            // Список полезен и для ручного восстановления после аварийного завершения Editor.
            Directory.CreateDirectory(CheckedPath(backup));
            File.WriteAllLines(backup + "/files-before.txt", existed);
            File.WriteAllLines(backup + "/files-affected.txt", affected);
            try
            {
                foreach (string path in affected)
                {
                    if (files.TryGetValue(path, out string source))
                    {
                        string directory = Path.GetDirectoryName(path);
                        for (string parent = directory; !Directory.Exists(parent); parent = Path.GetDirectoryName(parent))
                            if (!createdDirectories.Contains(parent))
                                createdDirectories.Add(parent);
                        Directory.CreateDirectory(directory);
                        File.Copy(source, path, true);
                    }
                    else if (File.Exists(path))
                        File.Delete(path);
                }
                foreach (var file in files)
                    if (!SameFile(file.Value, file.Key))
                        throw new IOException("Файл не прошёл проверку после записи: " + file.Key);
                ValidatePinned(ResolveInstalledPath);
                foreach (string directory in obsoleteDirectories)
                    Directory.Delete(directory);
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                foreach (string path in affected)
                {
                    try
                    {
                        if (existed.Contains(path))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(path));
                            File.Copy(backup + "/" + path, path, true);
                        }
                        else if (File.Exists(path))
                            File.Delete(path);
                    }
                    catch (Exception restoreFailure) { errors.Add(restoreFailure); }
                }
                foreach (string directory in createdDirectories.OrderByDescending(path => path.Length))
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory);
                throw new IOException("Установка не завершена. " + (errors.Count == 1 ? "Исходные файлы восстановлены. " :
                    "Автоматический откат неполон. ") + "Резервная копия: " + backup, new AggregateException(errors));
            }
        }

        private static bool IsConfiguration(string path)
        {
            string name = Path.GetFileName(path);
            return name.Equals("google-services.json", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("GoogleService-Info.plist", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("google-services.json.meta", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("GoogleService-Info.plist.meta", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ReadBlock(Stream stream, byte[] block)
        {
            int offset = 0;
            while (offset < block.Length)
            {
                int count = stream.Read(block, offset, block.Length - offset);
                if (count == 0)
                {
                    if (offset == 0) return false;
                    throw new EndOfStreamException("Неполный tar header.");
                }
                offset += count;
            }
            return true;
        }

        private static void CopyBytes(Stream input, Stream output, long size, byte[] buffer, CancellationToken cancellation)
        {
            while (size > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                int count = input.Read(buffer, 0, (int)Math.Min(size, buffer.Length));
                if (count == 0) throw new EndOfStreamException("Неполная tar-запись.");
                output.Write(buffer, 0, count);
                size -= count;
            }
        }

        private static string Text(byte[] bytes, int start, int length)
        {
            return Encoding.ASCII.GetString(bytes, start, length).TrimEnd('\0', ' ');
        }

        private static long Octal(byte[] bytes, int start, int length)
        {
            return Convert.ToInt64(Text(bytes, start, length).Trim(), 8);
        }
    }
}
