#if UNITY_ANDROID
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>Removes prohibited dependencies and their known carriers from generated Android inputs.</summary>
    public class AppLovinGradleExclusions : IPostGenerateGradleAndroidProject
    {
        // MAX 8.x injects Quality Service at int.MaxValue - 10, after ordinary SDK hooks.
        public int callbackOrder => int.MaxValue - 1;
        private const string MarkerBegin = "// >>> AMZN GoD SDK: forbidden ad networks — do not edit by hand";
        private const string MarkerEnd = "// <<< AMZN GoD SDK: forbidden ad networks";
        private const string Tag = "[AMZNGoDSDK][Android cleanup] ";
        private static readonly Regex Quoted = new Regex(@"['""]([^'""\r\n]+)['""]");
        private static readonly Regex Dependency = new Regex(
            @"^\s*(?:implementation|api|compile|compileOnly|runtimeOnly|runtime|[A-Za-z_]\w*(?:Implementation|Api|CompileOnly|RuntimeOnly))\b");
        private static readonly Regex Exclusion = new Regex(
            @"\Gexclude\b\s*(?:\(\s*)?(?:(?:group|module)\s*:\s*['""][^'""\r\n]+['""]\s*(?:,\s*)?)+\)?");
        private static readonly Regex Closure = new Regex(@"(?m)^[ \t]*(?:applovin|safedk)\s*\{");
        private static readonly Regex PluginLine = new Regex(
            @"^\s*(?:apply\s*(?:\(\s*)?plugin\s*:\s*['""](?:applovin-quality-service|com\.applovin\.quality|safedk|com\.safedk)['""]\s*\)?|" +
            @"id\s*\(?\s*['""](?:com\.applovin\.quality|applovin-quality-service|safedk|com\.safedk)['""]\s*\)?(?:\s+version\s+['""][^'""]+['""])?(?:\s+apply\s+(?:false|true))?|" +
            @"classpath\s*\(?\s*['""](?:com\.applovin\.quality:AppLovinQualityServiceGradlePlugin:|com\.safedk:SafeDKGradlePlugin:)[^'""]+['""]\s*\)?)\s*;?\s*(?://[^\r\n]*)?$");
        private static readonly Regex QualityToken = new Regex(
            @"applovin-quality-service|com\.applovin\.quality|com\.safedk|SafeDKGradlePlugin|\bsafedk\s*\{|\bapplovin\s*\{", RegexOptions.IgnoreCase);
        private static readonly HashSet<string> OutputDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "build", ".gradle", ".safedk", ".cxx", ".externalNativeBuild", ".kotlin", ".idea", ".git", "out"
        };

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var settings = SdkSettingsManager.LoadRuntimeSettings();
            if (settings == null || !settings.Enabled) return;
            // EDM can repopulate source templates in OnPostProcessScene, after PreProcess.
            // Reapply the same synchronous pass after resolution as well as to exported files.
            CleanProjectInputs(Directory.GetCurrentDirectory());
            CleanGeneratedProject(path);
        }

        /// <summary>Synchronously cleans existing custom templates before this build exports Gradle.</summary>
        internal static void CleanProjectInputs(string projectRoot)
        {
            string root = Path.GetFullPath(projectRoot);
            var originals = new Dictionary<string, byte[]>();
            var edits = new Dictionary<string, byte[]>();
            var removedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int removedDeclarations = 0;
            foreach (string template in new[] { "mainTemplate.gradle", "launcherTemplate.gradle", "baseProjectTemplate.gradle" })
            {
                string relative = Path.Combine("Assets", "Plugins", "Android", template);
                string file = Path.Combine(root, relative);
                // Missing templates use Unity's defaults; the generated-project pass still covers them.
                if (!File.Exists(file)) continue;
                byte[] bytes = File.ReadAllBytes(file);
                string original;
                using (var stream = new MemoryStream(bytes))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true)) original = reader.ReadToEnd();
                string content = TransformGradleText(original, file, removedNames, ref removedDeclarations,
                    true, template == "baseProjectTemplate.gradle");
                if (content == original) continue;
                originals[relative] = bytes;
                edits[relative] = EncodePreservingEncoding(bytes, content);
            }
            if (edits.Count == 0) return;

            // Every transform is validated before touching project files. Save all original
            // bytes before the first write so even a later I/O failure remains recoverable.
            string backup = Path.Combine(root, "Library", "AmznGoDSDK", "AndroidCleanup",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            foreach (var original in originals)
            {
                string destination = Path.Combine(backup, original.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.WriteAllBytes(destination, original.Value);
            }
            try
            {
                foreach (var edit in edits) File.WriteAllBytes(Path.Combine(root, edit.Key), edit.Value);
            }
            catch (Exception failure)
            {
                var restoreErrors = new List<string>();
                foreach (var original in originals)
                {
                    try { File.WriteAllBytes(Path.Combine(root, original.Key), original.Value); }
                    catch (Exception) { restoreErrors.Add(original.Key); }
                }
                throw Failure("Не удалось записать Gradle-шаблоны. Резервная копия: " + backup +
                    (restoreErrors.Count == 0 ? ". Исходные файлы восстановлены. " :
                        ". Не удалось восстановить: " + string.Join(", ", restoreErrors) + ". ") + failure.Message);
            }
            Debug.Log(Tag + "Gradle-шаблоны очищены перед сборкой: " + edits.Count +
                "; удалено объявлений: " + removedDeclarations + ". Резервная копия: " + backup);
        }

        internal static void CleanGeneratedProject(string path)
        {
            string module = Path.GetFullPath(path);
            string root = Directory.GetParent(module)?.FullName;
            if (root == null) throw Failure("Не найден корень Gradle-проекта.");
            string rootBuild = Path.Combine(root, "build.gradle");
            string libraryBuild = Path.Combine(module, "build.gradle");
            string launcherBuild = Path.Combine(root, "launcher", "build.gradle");
            foreach (string required in new[] { rootBuild, libraryBuild, launcherBuild })
                if (!File.Exists(required)) throw Failure("Не найден обязательный файл: " + required);

            var inputs = EnumerateInputs(root).ToArray();
            var removedArchives = inputs.Where(IsRemovableLocalLibrary).ToArray();
            var removedNames = new HashSet<string>(removedArchives.Select(Path.GetFileNameWithoutExtension), StringComparer.OrdinalIgnoreCase);
            var edits = new Dictionary<string, string>();
            int removedDeclarations = 0;
            // Validate every edit first: unknown syntax must not silently survive or damage Gradle.
            foreach (string file in inputs.Where(file => file.EndsWith(".gradle", StringComparison.OrdinalIgnoreCase)))
            {
                string original = File.ReadAllText(file);
                bool addExclusions = string.Equals(file, rootBuild, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(file, libraryBuild, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(file, launcherBuild, StringComparison.OrdinalIgnoreCase);
                string content = TransformGradleText(original, file, removedNames, ref removedDeclarations,
                    addExclusions, string.Equals(file, rootBuild, StringComparison.OrdinalIgnoreCase));
                if (content != original) edits[file] = content;
            }
            foreach (var edit in edits) WritePreservingEncoding(edit.Key, edit.Value);
            foreach (string archive in removedArchives)
            {
                File.Delete(archive); // Only copies inside the generated project, never source Assets.
                Debug.Log(Tag + "Удалена копия запрещённой библиотеки: " + archive);
            }
            Debug.Log(Tag + "Применены исключения для всего Gradle-проекта; удалено объявлений: " +
                removedDeclarations + ", локальных библиотек: " + removedArchives.Length + ".");
        }

        private static string TransformGradleText(string original, string file, HashSet<string> removedNames,
            ref int removedDeclarations, bool addExclusions, bool root)
        {
            string newline = original.Contains("\r\n") ? "\r\n" : original.Contains("\n") ? "\n" : original.Contains("\r") ? "\r" : "\n";
            string content = RemoveMarkedBlock(original, file);
            content = RemoveQualityClosures(content, file);
            content = CleanDeclarations(content, file, removedNames, ref removedDeclarations);
            AuditRemainingDeclarations(content, file, removedNames);
            return addExclusions ? content.TrimEnd('\r', '\n') + newline + newline + BuildExclusionBlock(newline, root) : content;
        }

        private static string RemoveMarkedBlock(string content, string file)
        {
            while (true)
            {
                int begin = content.IndexOf(MarkerBegin, StringComparison.Ordinal);
                int end = content.IndexOf(MarkerEnd, StringComparison.Ordinal);
                if (begin < 0 && end < 0) return content;
                if (begin < 0 || end < begin + MarkerBegin.Length || content.IndexOf(MarkerBegin, begin + MarkerBegin.Length,
                        end - begin - MarkerBegin.Length, StringComparison.Ordinal) >= 0)
                    throw Failure("Повреждён блок исключений SDK: " + file);
                int after = end + MarkerEnd.Length;
                if (after < content.Length && content[after] == '\r') after++;
                if (after < content.Length && content[after] == '\n') after++;
                content = content.Remove(begin, after - begin);
            }
        }

        private static string RemoveQualityClosures(string content, string file)
        {
            Match match;
            while ((match = Closure.Match(MaskComments(content))).Success)
            {
                string code = MaskComments(content);
                int opening = code.IndexOf('{', match.Index);
                int closing = FindClosingBrace(content, opening, file);
                int end = EndOfStatement(content, closing + 1, file);
                var result = content.ToCharArray();
                BlankCode(result, code, match.Index, end);
                content = new string(result);
            }
            return content;
        }

        private static string CleanDeclarations(string content, string file, HashSet<string> removedNames, ref int count)
        {
            var output = content.ToCharArray();
            var lines = Regex.Matches(content, @"[^\r\n]*(?:\r\n|\n|\r|$)");
            int skipUntil = 0;
            string commentsMasked = MaskComments(content);
            foreach (Match line in lines)
            {
                if (line.Length == 0 || line.Index < skipUntil) continue;
                string code = commentsMasked.Substring(line.Index, line.Length).TrimEnd('\r', '\n');
                if (PluginLine.IsMatch(code))
                {
                    BlankCode(output, commentsMasked, line.Index, line.Index + line.Length);
                    count++;
                    continue;
                }
                Match dependency = Dependency.Match(code);
                if (!dependency.Success) continue;
                int headEnd = DependencyHeadEnd(commentsMasked, line.Index + dependency.Length);
                string head = commentsMasked.Substring(line.Index, headEnd - line.Index);
                if (HasForbiddenReference(head, removedNames))
                {
                    if (!IsSingleDependency(head))
                        throw Failure("Неподдерживаемое объявление запрещённой зависимости: " + file);
                    int brace = headEnd;
                    while (brace < commentsMasked.Length && char.IsWhiteSpace(commentsMasked[brace])) brace++;
                    if (brace < commentsMasked.Length && commentsMasked[brace] == '{')
                    {
                        int closing = FindClosingBrace(content, brace, file);
                        skipUntil = EndOfStatement(content, closing + 1, file);
                    }
                    else skipUntil = EndOfStatement(content, headEnd, file);
                    BlankCode(output, commentsMasked, line.Index, skipUntil);
                    count++;
                }
            }
            return new string(output);
        }

        // A dependency's identity ends before its configuration closure. Parentheses and
        // comma continuations keep multiline Maven maps together as one expression.
        private static int DependencyHeadEnd(string code, int offset)
        {
            int depth = 0;
            char previous = '\0';
            for (int i = offset; i < code.Length; i++)
            {
                char c = code[i];
                if (c == '\'' || c == '"')
                {
                    i = QuotedEnd(code, i);
                    previous = '\'';
                    continue;
                }
                if (c == '(' || c == '[') depth++;
                if (c == ')' || c == ']') depth--;
                if (depth == 0 && (c == '{' || c == ';' ||
                    ((c == '\r' || c == '\n') && previous != ',' && previous != '\\'))) return i;
                if (!char.IsWhiteSpace(c)) previous = c;
            }
            return code.Length;
        }

        private static bool IsSingleDependency(string code)
        {
            string body = Dependency.Replace(code, "", 1).Trim().TrimEnd(';').Trim();
            if (body.IndexOfAny(new[] { '{', '}', ';' }) >= 0) return false;
            if (body.StartsWith("(", StringComparison.Ordinal) && body.EndsWith(")", StringComparison.Ordinal))
                body = body.Substring(1, body.Length - 2).Trim();
            return Regex.IsMatch(body, @"^['""][^'""]+['""]$")
                || Regex.IsMatch(body, @"^(?:(?:group|name|version|ext)\s*:\s*['""][^'""]+['""]\s*,?\s*)+$")
                || Regex.IsMatch(body, @"^files\s*\(\s*['""][^'""]+['""]\s*\)$");
        }

        private static bool HasForbiddenReference(string code, HashSet<string> removedNames)
        {
            // Maven coordinates are atomic: a module name from a different group must
            // never be treated as the filename of a prohibited local archive.
            foreach (Match quoted in Quoted.Matches(code))
            {
                string value = quoted.Groups[1].Value;
                string[] coordinate = value.Split(':');
                if (coordinate.Length >= 2 && ForbiddenAdNetworks.MatchMavenCoordinate(coordinate[0], coordinate[1]) != null)
                    return true;
            }
            var group = Regex.Match(code, @"\bgroup\s*:\s*['""]([^'""]+)['""]");
            var module = Regex.Match(code, @"\bname\s*:\s*['""]([^'""]+)['""]");
            if (group.Success && module.Success &&
                ForbiddenAdNetworks.MatchMavenCoordinate(group.Groups[1].Value, module.Groups[1].Value) != null) return true;

            // EDM also emits flatDir notation, implementation(name: 'archive', ext: 'aar').
            if (!group.Success && module.Success && Regex.IsMatch(code, @"\bext\s*:\s*['""](?:aar|jar)['""]", RegexOptions.IgnoreCase)
                && IsForbiddenLocalReference(module.Groups[1].Value, removedNames)) return true;
            foreach (Match files in Regex.Matches(code, @"\bfiles\s*\(([^)]*)\)"))
                foreach (Match quoted in Quoted.Matches(files.Groups[1].Value))
                    if (IsForbiddenLocalReference(quoted.Groups[1].Value, removedNames)) return true;
            return false;
        }

        private static bool IsForbiddenLocalReference(string value, HashSet<string> removedNames) =>
            removedNames.Contains(value) || IsForbiddenLibraryName(value);

        private static void AuditRemainingDeclarations(string content, string file, HashSet<string> removedNames)
        {
            string code = MaskExclusions(MaskComments(content));
            if (QualityToken.IsMatch(code)) throw Failure("Неподдерживаемое объявление Quality Service / SafeDK в " + file +
                ". Уберите плагин и его конфигурацию из Gradle template.");
            var remainder = code.ToCharArray();
            int skipUntil = 0;
            foreach (Match line in Regex.Matches(code, @"[^\r\n]*(?:\r\n|\n|\r|$)"))
            {
                if (line.Length == 0 || line.Index < skipUntil) continue;
                Match dependency = Dependency.Match(line.Value);
                if (!dependency.Success) continue;
                int end = DependencyHeadEnd(code, line.Index + dependency.Length);
                if (HasForbiddenReference(code.Substring(line.Index, end - line.Index), removedNames))
                    throw UnsupportedDependency(file);
                BlankCode(remainder, code, line.Index, end);
                skipUntil = end;
            }
            foreach (string line in new string(remainder).Split('\n'))
            {
                if (HasForbiddenReference(line, removedNames))
                    throw UnsupportedDependency(file);
            }
        }

        private static BuildFailedException UnsupportedDependency(string file) =>
            Failure("Не удалось безопасно удалить объявление запрещённой зависимости в " + file +
                ". Вынесите зависимость в отдельную стандартную Gradle-инструкцию.");

        // Exclusions remove transitive dependencies; they are not imports. Mask only
        // explicit group/module arguments, leaving other statements available to audit.
        private static string MaskExclusions(string code)
        {
            var result = code.ToCharArray();
            for (int i = 0; i < code.Length; i++)
            {
                if (code[i] == '\'' || code[i] == '"') { i = QuotedEnd(code, i); continue; }
                if (code[i] != 'e' || (i > 0 && (char.IsLetterOrDigit(code[i - 1]) || code[i - 1] == '_'))) continue;
                Match match = Exclusion.Match(code, i);
                if (!match.Success) continue;
                BlankCode(result, code, i, i + match.Length);
                i += match.Length - 1;
            }
            return new string(result);
        }

        private static int QuotedEnd(string code, int opening)
        {
            for (int i = opening + 1; i < code.Length; i++)
            {
                if (code[i] == '\\') { i++; continue; }
                if (code[i] == code[opening]) return i;
            }
            return code.Length - 1;
        }

        // Keep complete comment tokens, including a block comment that starts on a
        // removed statement and ends on a later line. Offsets and line endings stay stable.
        private static void BlankCode(char[] result, string commentsMasked, int start, int end)
        {
            for (int i = start; i < end; i++)
                if (!char.IsWhiteSpace(commentsMasked[i])) result[i] = ' ';
        }

        internal static bool IsRemovableLocalLibrary(string path)
        {
            string extension = Path.GetExtension(path);
            return (extension.Equals(".aar", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jar", StringComparison.OrdinalIgnoreCase))
                && IsForbiddenLibraryName(Path.GetFileNameWithoutExtension(path));
        }

        private static bool IsForbiddenLibraryName(string value)
        {
            string name = value.Replace('\\', '/');
            name = name.Substring(name.LastIndexOf('/') + 1);
            if (name.StartsWith("jetified-", StringComparison.OrdinalIgnoreCase)) name = name.Substring("jetified-".Length);
            if (name.EndsWith(".aar", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            foreach (var artifact in ForbiddenAdNetworks.AllArtifacts())
            {
                // Exact artifact name, optionally with the EDM group prefix and numeric version.
                string pattern = @"^(?:" + Regex.Escape(artifact.Group) + @"[.-])?" + Regex.Escape(artifact.Module) + @"(?:-\d[\w.+-]*)?$";
                if (Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase)) return true;
            }
            foreach (string group in ForbiddenAdNetworks.AllMavenGroups())
                if (Regex.IsMatch(name, "^" + Regex.Escape(group) + @"\.[A-Za-z][\w.-]*-\d[\w.+-]*$", RegexOptions.IgnoreCase)) return true;
            return false;
        }

        private static string BuildExclusionBlock(string newline, bool root)
        {
            var lines = new List<string> { MarkerBegin };
            if (root) lines.Add("allprojects {");
            string indent = root ? "    " : "";
            lines.Add(indent + "configurations.configureEach {");
            foreach (string group in ForbiddenAdNetworks.AllMavenGroups().Distinct())
                lines.Add(indent + "    exclude group: '" + group + "'");
            foreach (var artifact in ForbiddenAdNetworks.AllArtifacts())
                lines.Add(indent + "    exclude group: '" + artifact.Group + "', module: '" + artifact.Module + "'");
            lines.Add(indent + "}");
            if (root) lines.Add("}");
            lines.Add(MarkerEnd);
            return string.Join(newline, lines) + newline;
        }

        private static IEnumerable<string> EnumerateInputs(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string file in Directory.GetFiles(directory))
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) yield return file;
                foreach (string child in Directory.GetDirectories(directory))
                    if (!OutputDirectories.Contains(Path.GetFileName(child)) &&
                        (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
            }
        }

        // Blanks comments without moving offsets; quoted literals remain readable.
        private static string MaskComments(string content)
        {
            char[] result = content.ToCharArray();
            char quote = '\0';
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if (quote != '\0')
                {
                    if (c == '\\') { i++; continue; }
                    if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '\'' || c == '"') { quote = c; continue; }
                if (c != '/' || i + 1 >= content.Length) continue;
                if (content[i + 1] == '/')
                {
                    while (i < content.Length && content[i] != '\n' && content[i] != '\r') result[i++] = ' ';
                    i--;
                }
                else if (content[i + 1] == '*')
                {
                    result[i++] = ' ';
                    result[i] = ' ';
                    while (++i < content.Length)
                    {
                        if (content[i] == '*' && i + 1 < content.Length && content[i + 1] == '/')
                        { result[i] = result[++i] = ' '; break; }
                        if (content[i] != '\n' && content[i] != '\r') result[i] = ' ';
                    }
                }
            }
            return new string(result);
        }

        private static int FindClosingBrace(string content, int opening, string file)
        {
            string code = MaskComments(content);
            int depth = 0;
            char quote = '\0';
            for (int i = opening; i < code.Length; i++)
            {
                char c = code[i];
                if (quote != '\0')
                {
                    if (c == '\\') { i++; continue; }
                    if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '\'' || c == '"') { quote = c; continue; }
                if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return i;
            }
            throw Failure("Незакрытый Gradle-блок: " + file);
        }

        private static int EndOfStatement(string content, int offset, string file)
        {
            int end = content.IndexOfAny(new[] { '\r', '\n' }, offset);
            if (end < 0) end = content.Length;
            string suffix = MaskComments(content).Substring(offset, end - offset).Trim();
            if (suffix.Length > 0 && suffix != ";") throw Failure("Дополнительный код после Gradle-блока: " + file);
            if (end < content.Length && content[end] == '\r') end++;
            if (end < content.Length && content[end] == '\n') end++;
            return end;
        }

        private static void WritePreservingEncoding(string file, string content)
        {
            byte[] bytes = File.ReadAllBytes(file);
            File.WriteAllBytes(file, EncodePreservingEncoding(bytes, content));
        }

        private static byte[] EncodePreservingEncoding(byte[] bytes, string content)
        {
            Encoding encoding = new UTF8Encoding(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf);
            if (bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xfe && bytes[2] == 0 && bytes[3] == 0)
                encoding = new UTF32Encoding(false, true);
            else if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xfe && bytes[3] == 0xff)
                encoding = new UTF32Encoding(true, true);
            else if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) encoding = new UnicodeEncoding(false, true);
            else if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) encoding = new UnicodeEncoding(true, true);
            byte[] preamble = encoding.GetPreamble();
            byte[] encoded = encoding.GetBytes(content);
            byte[] result = new byte[preamble.Length + encoded.Length];
            Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
            Buffer.BlockCopy(encoded, 0, result, preamble.Length, encoded.Length);
            return result;
        }

        private static BuildFailedException Failure(string message) => new BuildFailedException(Tag + message);
    }
}
#endif
