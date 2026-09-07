using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;

namespace AMZNGoDSDK.Editor
{
    /// <summary>План миграции legacy MAX: пользовательские ресурсы остаются на прежних путях.</summary>
    internal sealed class AppLovinLegacyInstallation
    {
        internal const string Root = "Assets/MaxSdk";
        private readonly HashSet<string> _delete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _backup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _existed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal readonly HashSet<string> AdapterPins = new HashSet<string>();
        internal string BackupPath { get; private set; }

        internal static bool HasCore => Files().Any(path => !Preserve(path) && IsCorePath(path) &&
            (IsSdkFile(path) || Regex.IsMatch(path, @"\.(dll|aar|jar|so|a|asmdef)$", RegexOptions.IgnoreCase)));

        internal static string Description
        {
            get
            {
                if (!HasCore) return null;
                string script = Root + "/Scripts/MaxSdk.cs";
                var version = File.Exists(script) ? Regex.Match(File.ReadAllText(script), "_version\\s*=\\s*\"([^\"]+)\"") : Match.Empty;
                return "legacy Assets/MaxSdk" + (version.Success ? " " + version.Groups[1].Value : " (версия неизвестна)");
            }
        }

        private static IEnumerable<string> Files() => FirebaseUnityPackageUtility.Files(Root);

        private static bool Preserve(string path)
        {
            return path.StartsWith(Root + "/Resources/", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("Settings.asset", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("Settings.asset.meta", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCorePath(string path)
        {
            return path.StartsWith(Root + "/Scripts/", StringComparison.Ordinal) ||
                path.StartsWith(Root + "/AppLovin/", StringComparison.Ordinal) ||
                path.StartsWith(Root + "/Editor/", StringComparison.Ordinal) ||
                path.StartsWith(Root + "/Plugins/", StringComparison.Ordinal) ||
                Path.GetDirectoryName(path)?.Replace('\\', '/') == Root && Path.GetFileName(path).StartsWith("MaxSdk", StringComparison.Ordinal);
        }

        private static bool IsSdkFile(string path)
        {
            if (path.EndsWith(".meta", StringComparison.Ordinal)) path = path.Substring(0, path.Length - 5);
            if (!File.Exists(path)) return false;
            string metadata = path + ".meta";
            if (File.Exists(metadata) && File.ReadAllText(metadata).Contains("al_max_export_path-" + path.Substring("Assets/".Length)))
                return true;
            // Имена старых core-файлов до появления export labels. Пользовательские *.cs сюда не попадают.
            return Regex.IsMatch(Path.GetFileName(path),
                @"^(MaxSdk(|Base|Android|iOS|Callbacks|Utils|Logger|UnityEditor)|MaxEventExecutor|MaxEvents|MaxEventSystemChecker|MaxCmpService|MaxWebRequest|MaxSegmentCollection|MaxUserSegment)\.cs$") ||
                Regex.IsMatch(Path.GetFileName(path), @"^MaxSdk(\.Scripts|\.IntegrationManager\.Editor|\.Editor|\.Runtime)?\.asmdef$") ||
                Regex.IsMatch(Path.GetFileName(path), @"^MAUnity(Plugin|AdManager)\.(h|m|mm)$") ||
                Path.GetFileName(path) == "applovin-max-unity-plugin.aar" ||
                path == Root + "/AppLovin/Editor/Dependencies.xml";
        }

        internal static AppLovinLegacyInstallation Inspect(bool replace, IEnumerable<string> installedAdapters)
        {
            var plan = new AppLovinLegacyInstallation();
            var settings = new HashSet<string>(AssetDatabase.FindAssets("t:AppLovinSettings", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath), StringComparer.OrdinalIgnoreCase);
            // SDK с перемещёнными export paths нельзя безопасно смешивать с новым UPM core.
            var marked = AssetDatabase.FindAssets("l:al_max", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Regex.IsMatch(path, @"\.(cs|dll|aar|jar|so|a|m|mm|h|asmdef)$|Dependencies\.xml$", RegexOptions.IgnoreCase));
            var signatures = AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) &&
                (Path.GetFileName(path) == "MaxSdk.cs" || Path.GetFileName(path) == "MaxSdkBase.cs" ||
                 Regex.IsMatch(Path.GetFileName(path), @"^(applovin.*|MaxSdk.*)\.(aar|jar|dll|so)$", RegexOptions.IgnoreCase) || Path.GetFileName(path) == "MAUnityPlugin.mm" ||
                 Regex.IsMatch(Path.GetFileName(path), @"^MaxSdk.*\.asmdef$")));
            foreach (string path in marked.Concat(signatures).Distinct())
            {
                FirebaseUnityPackageUtility.CheckedPath(path);
                if (!path.StartsWith(Root + "/", StringComparison.Ordinal) && !settings.Contains(path) && !Preserve(path) && File.Exists(path))
                    throw new IOException("Обнаружен перемещённый legacy MAX: " + path + ". Перенесите SDK в Assets/MaxSdk перед заменой.");
                if (File.Exists(path) && !IsCorePath(path) && !path.StartsWith(Root + "/Mediation/", StringComparison.Ordinal) &&
                    !settings.Contains(path) && !Preserve(path))
                    throw new IOException("Неизвестный путь legacy MAX: " + path + ". Проверьте его вручную перед заменой.");
            }
            foreach (string setting in settings)
            {
                plan._backup.Add(setting);
                plan._backup.Add(setting + ".meta");
            }
            string[] files = Files().ToArray();
            var pinnedRoots = new HashSet<string>();
            foreach (string dependency in files.Where(path => path.StartsWith(Root + "/Mediation/", StringComparison.Ordinal) &&
                         path.EndsWith("Dependencies.xml", StringComparison.Ordinal)))
            {
                var xml = new XmlDocument { XmlResolver = null };
                using (var reader = XmlReader.Create(dependency, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
                    xml.Load(reader);
                foreach (XmlNode node in xml.SelectNodes("/dependencies/androidPackages/androidPackage"))
                {
                    string spec = node.Attributes?["spec"]?.Value ?? "";
                    var forbidden = ForbiddenAdNetworks.MatchByGroup(spec);
                    if (forbidden != null)
                        throw new IOException("Установлен запрещённый адаптер " + forbidden.DisplayName + ": " + dependency + ". Удалите его явно перед установкой.");
                    var match = Regex.Match(spec, @"^com\.applovin\.mediation:([a-z0-9-]+)-adapter:");
                    if (!match.Success) continue;
                    string network = match.Groups[1].Value;
                    if (network == "ogury-presage") network = "ogurypresage";
                    if (network == "yso-network") network = "ysonetwork";
                    string id = "com.applovin.mediation.adapters." + network + ".android";
                    if (replace && AppLovinPackageInstaller.Pins.ContainsKey(id))
                    {
                        if (xml.SelectNodes("/dependencies/iosPods/iosPod").Count != 0)
                            throw new IOException("Legacy " + match.Groups[1].Value + " содержит iOS-зависимости. Переведите этот адаптер в UPM перед заменой, чтобы сохранить обе платформы.");
                        plan.AdapterPins.Add(id);
                        pinnedRoots.Add(Root + "/Mediation/" + dependency.Substring((Root + "/Mediation/").Length).Split('/')[0] + "/");
                        plan._delete.Add(dependency);
                        plan._delete.Add(dependency + ".meta");
                    }
                    else if (installedAdapters.Contains(id))
                        throw new IOException("Адаптер установлен одновременно в Assets и UPM: " + id + ". Сначала оставьте одну копию.");
                }
            }
            foreach (string path in files)
            {
                plan._backup.Add(path);
                if (Preserve(path) || settings.Contains(path) || settings.Contains(path.EndsWith(".meta", StringComparison.Ordinal) ? path.Substring(0, path.Length - 5) : path))
                    continue;
                if (!replace || (!IsCorePath(path) && !pinnedRoots.Any(root => path.StartsWith(root, StringComparison.Ordinal))))
                    continue;
                if (IsSdkFile(path))
                {
                    plan._delete.Add(path);
                    if (!path.EndsWith(".meta", StringComparison.Ordinal)) plan._delete.Add(path + ".meta");
                }
                else if (Regex.IsMatch(path, @"\.(dll|aar|jar|so|a|m|mm|asmdef)$", RegexOptions.IgnoreCase) ||
                         path.EndsWith(".cs", StringComparison.Ordinal) && Regex.IsMatch(File.ReadAllText(path), @"\b(class|namespace|struct|enum|interface)\s+(Max[A-Z]|AppLovin)"))
                    throw new IOException("Неизвестный код legacy MAX: " + path + ". Проверьте его вручную перед заменой.");
            }
            return plan;
        }

        internal void Backup(string destination)
        {
            BackupPath = destination;
            _backup.Add("Packages/manifest.json");
            _backup.Add("Packages/packages-lock.json");
            foreach (string path in _backup.Concat(_delete).Distinct())
            {
                FirebaseUnityPackageUtility.CheckedPath(path);
                if (!File.Exists(path)) continue;
                string target = FirebaseUnityPackageUtility.CheckedPath(destination + "/" + path);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(path, target, false);
                _existed.Add(path);
            }
            File.WriteAllLines(destination + "/files-before.txt", _existed);
        }

        internal void RemoveLegacy()
        {
            foreach (string path in _delete)
            {
                FirebaseUnityPackageUtility.CheckedPath(path);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        internal void Restore()
        {
            var errors = new List<Exception>();
            foreach (string path in _backup.Concat(_delete).Distinct())
            {
                try
                {
                    RestoreFile(path);
                }
                catch (Exception ex) { errors.Add(ex); }
            }
            if (errors.Count > 0)
                throw new AggregateException("Откат файлов неполон. Резервная копия: " + BackupPath, errors);
        }

        internal void RestorePackageFiles()
        {
            RestoreFile("Packages/manifest.json");
            RestoreFile("Packages/packages-lock.json");
        }

        private void RestoreFile(string path)
        {
            FirebaseUnityPackageUtility.CheckedPath(path);
            if (_existed.Contains(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.Copy(BackupPath + "/" + path, path, true);
            }
            else if (File.Exists(path)) File.Delete(path);
        }
    }
}
