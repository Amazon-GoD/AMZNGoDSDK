#if UNITY_ANDROID
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>Применяет современный Android toolchain только к сгенерированному Gradle-проекту.</summary>
    public sealed class AndroidGradleToolchain : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 2500;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly Regex PluginVersion = new Regex(
            @"(id\s*\(?\s*['""]com\.android\.(?:application|library)['""]\s*\)?\s*version\s*['""])[^'""]+(['""])");
        private static readonly Regex PluginClasspath = new Regex(
            @"(com\.android\.tools\.build:gradle:)[^'""\s]+(?=['""])");

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var settings = SdkSettingsManager.LoadRuntimeSettings();
            if (settings == null || !settings.Enabled) return;

            var errors = new List<string>();
            AndroidToolchainSettings.CollectValidationErrors(errors);
            if (errors.Count > 0) throw new BuildFailedException(string.Join("\n", errors));
            var parent = Directory.GetParent(path);
            if (parent == null) throw new BuildFailedException("Не найден корень Gradle-проекта: " + path);
            string root = parent.FullName;
            string rootBuild = Path.Combine(root, "build.gradle");
            string settingsFile = Path.Combine(root, "settings.gradle");
            if (!File.Exists(rootBuild) || !File.Exists(settingsFile))
                throw new BuildFailedException("Ожидались build.gradle и settings.gradle в " + root);

            // Сначала готовим все изменения: неизвестный формат шаблона не оставит частично обновлённый проект.
            var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string rootContent = RemoveLegacyOverrides(File.ReadAllText(rootBuild));
            if (!PluginVersion.IsMatch(rootContent) && !PluginClasspath.IsMatch(rootContent))
                throw new BuildFailedException("Не удалось определить Android Gradle Plugin в " + rootBuild + ". Проверьте Custom Base Gradle Template.");
            rootContent = PluginVersion.Replace(rootContent, match => match.Groups[1].Value +
                AndroidToolchainSettings.AndroidGradlePluginVersion + match.Groups[2].Value);
            rootContent = PluginClasspath.Replace(rootContent, match => match.Groups[1].Value + AndroidToolchainSettings.AndroidGradlePluginVersion);
            changes[rootBuild] = rootContent;

            string settingsContent = File.ReadAllText(settingsFile);
            foreach (string directory in IncludedModules(root, settingsContent))
                PrepareModule(directory, changes);

            string propertiesPath = Path.Combine(root, "gradle.properties");
            string properties = File.Exists(propertiesPath) ? File.ReadAllText(propertiesPath) : "";
            properties = RemoveProperty(properties, "android.aapt2FromMavenOverride");
            properties = SetProperty(properties, "org.gradle.java.home", AndroidToolchainSettings.GradleJavaHome);
            // Сохраняем поведение ресурсов и BuildConfig, на которое рассчитаны Unity 2022 и старые androidlib.
            properties = SetProperty(properties, "android.defaults.buildfeatures.buildconfig", "true");
            properties = SetProperty(properties, "android.nonTransitiveRClass", "false");
            properties = SetProperty(properties, "android.nonFinalResIds", "false");
            changes[propertiesPath] = properties;
            string localPath = Path.Combine(root, "local.properties");
            changes[localPath] = SetProperty(File.Exists(localPath) ? File.ReadAllText(localPath) : "",
                "sdk.dir", AndroidToolchainSettings.GradleSdkRoot);

            foreach (var change in changes)
                File.WriteAllText(change.Key, change.Value, Utf8);
            Debug.Log("[AMZN GoD SDK] Android Gradle Plugin " + AndroidToolchainSettings.AndroidGradlePluginVersion +
                ", Gradle " + AndroidToolchainSettings.GradleVersion + ", JDK 17, compileSdk " + AndroidToolchainSettings.CompileSdk +
                ": подготовлен " + root);
        }

        private static IEnumerable<string> IncludedModules(string root, string settings)
        {
            var modules = new HashSet<string>(StringComparer.Ordinal) { ":launcher", ":unityLibrary" };
            foreach (Match include in Regex.Matches(settings, @"(?m)^\s*include\s*(?:\([^\r\n]*|[^\r\n]*)"))
                foreach (Match name in Regex.Matches(include.Value, "['\"](?<name>:?[^'\"]+)['\"]"))
                    modules.Add(":" + name.Groups["name"].Value.TrimStart(':'));
            foreach (string module in modules)
            {
                if (module != ":launcher" && module != ":unityLibrary" && !module.EndsWith(".androidlib", StringComparison.Ordinal)) continue;
                string relative = module.TrimStart(':').Replace(':', Path.DirectorySeparatorChar);
                string directory = Path.GetFullPath(Path.Combine(root, relative));
                if (!directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new BuildFailedException("Android-модуль находится вне сгенерированного проекта: " + module);
                if (!File.Exists(Path.Combine(directory, "build.gradle")))
                    throw new BuildFailedException("Не найден build.gradle включённого Android-модуля " + module + ": " + directory);
                yield return directory;
            }
        }

        private static void PrepareModule(string directory, Dictionary<string, string> changes)
        {
            string build = Path.Combine(directory, "build.gradle");
            string content = RemoveLegacyOverrides(File.ReadAllText(build));
            if (!Regex.IsMatch(content, @"\bandroid\s*\{"))
                throw new BuildFailedException("Не найден блок android в " + build);
            content = SetAndroidLine(content, @"compileSdk(?:Version)?", "compileSdkVersion " + AndroidToolchainSettings.CompileSdk);
            content = SetAndroidLine(content, "buildToolsVersion", "buildToolsVersion '" + AndroidToolchainSettings.BuildToolsVersion + "'");
            // AGP 8 defaults to NDK 27. Match the explicit Unity NDK path instead.
            var ndkPath = Regex.Match(content, @"(?m)^[ \t]*ndkPath\s+['""](?<path>[^'""]+)['""]");
            if (ndkPath.Success)
            {
                string properties = Path.Combine(ndkPath.Groups["path"].Value.Replace("\\\\", "\\"), "source.properties");
                var revision = File.Exists(properties)
                    ? Regex.Match(File.ReadAllText(properties), @"(?m)^Pkg\.Revision\s*=\s*([\d.]+)") : Match.Empty;
                if (!revision.Success) throw new BuildFailedException("Не удалось определить версию NDK: " + properties);
                content = SetAndroidLine(content, "ndkVersion", "ndkVersion '" + revision.Groups[1].Value + "'");
            }

            string manifestPath = Path.Combine(directory, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath)) manifestPath = Path.Combine(directory, "AndroidManifest.xml");
            string manifest = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : null;
            string package = null;
            if (manifest != null)
            {
                var xml = new XmlDocument { XmlResolver = null };
                using (var reader = XmlReader.Create(new StringReader(manifest), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
                    xml.Load(reader);
                package = xml.DocumentElement?.GetAttribute("package");
            }
            if (!Regex.IsMatch(content, @"(?m)^\s*namespace(?:\s|=|\()"))
            {
                if (string.IsNullOrEmpty(package) || !Regex.IsMatch(package, @"^[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+$"))
                    throw new BuildFailedException("Нельзя определить namespace для " + build + ": нужен namespace или package в AndroidManifest.xml.");
                content = InsertAndroidLine(content, "namespace '" + package + "'");
            }
            if (!string.IsNullOrEmpty(package))
            {
                // AGP 8 больше не принимает package в исходном manifest библиотеки; namespace уже сохранён выше.
                changes[manifestPath] = Regex.Replace(manifest, @"<manifest\b[^>]*>", match =>
                    Regex.Replace(match.Value, @"\s+package\s*=\s*(['""])[^'""]*\1", ""), RegexOptions.Singleline);
            }
            changes[build] = content;
        }

        private static string SetAndroidLine(string content, string property, string statement)
        {
            var expression = new Regex(@"(?m)^[ \t]*" + property + @"\b[^\r\n]*");
            return expression.IsMatch(content) ? expression.Replace(content, "    " + statement) : InsertAndroidLine(content, statement);
        }

        private static string InsertAndroidLine(string content, string statement)
        {
            var android = Regex.Match(content, @"\bandroid\s*\{");
            string newline = content.Contains("\r\n") ? "\r\n" : "\n";
            return content.Insert(android.Index + android.Length, newline + "    " + statement);
        }

        private static string RemoveLegacyOverrides(string content)
        {
            content = Regex.Replace(content,
                @"(?ms)^[ \t]*// >>> AMZN GoD SDK: R8 override[^\r\n]*\r?\n.*?^[ \t]*// <<< AMZN GoD SDK: R8 override[^\r\n]*(?:\r?\n)?", "");
            content = Regex.Replace(content, @"(?m)^[ \t]*classpath\s+['""]com\.android\.tools:r8:8\.2\.47['""][ \t]*;?[ \t]*(?://[^\r\n]*)?(?:\r?\n)?", "");
            return Regex.Replace(content,
                @"(?m)^[ \t]*force\s+['""]org\.jetbrains\.kotlin:(?:kotlin-stdlib(?:-jdk[78])?|kotlin-parcelize-runtime):1\.8\.22['""][ \t]*;?[ \t]*(?://[^\r\n]*)?(?:\r?\n)?", "");
        }

        private static string RemoveProperty(string content, string key) => Regex.Replace(content,
            @"(?m)^[ \t]*" + Regex.Escape(key) + @"[ \t]*[=:][^\r\n]*(?:\r?\n)?", "");

        private static string SetProperty(string content, string key, string value)
        {
            string newline = content.Contains("\r\n") ? "\r\n" : "\n";
            content = RemoveProperty(content, key).TrimEnd('\r', '\n');
            var escaped = new StringBuilder();
            foreach (char character in value)
            {
                if (character < 32 || character > 126) escaped.Append("\\u").Append(((int)character).ToString("x4"));
                else if (character == '\\' || character == ':' || character == '=' || character == ' ') escaped.Append('\\').Append(character);
                else escaped.Append(character);
            }
            return (content.Length == 0 ? "" : content + newline) + key + "=" + escaped + newline;
        }
    }
}
#endif
