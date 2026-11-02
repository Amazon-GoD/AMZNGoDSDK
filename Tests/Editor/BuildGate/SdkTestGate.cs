using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.TestTools.TestRunner.Api;
using Debug = UnityEngine.Debug;

namespace AMZNGoDSDK.Editor.Tests
{
    /// <summary>
    /// Runs the SDK EditMode tests synchronously: before every Player build
    /// (<see cref="SdkTestsBuildGate"/>), from the menu and from batchmode
    /// (<see cref="RunFromCommandLine"/>), so all three share one code path.
    ///
    /// Lives in the test assembly on purpose: AMZNGoDSDK.Editor must not reference
    /// UnityEditor.TestRunner (a partner project without the Test Framework would lose the whole
    /// SDK editor assembly), and a UPM package compiles its test assemblies only when listed in
    /// "testables" — so the gate is active in the SDK dev project and never blocks partner builds.
    ///
    /// Batchmode:
    /// <code>
    /// Unity.exe -batchmode -nographics -quit -projectPath "D:\path\to\project"
    ///   -executeMethod AMZNGoDSDK.Editor.Tests.SdkTestGate.RunFromCommandLine -logFile tests.log
    /// </code>
    /// Exit code 0 means every test passed and the build gate is registered, 1 otherwise.
    /// </summary>
    public static class SdkTestGate
    {
        internal const string SkipEnvVariable = "AMZN_SKIP_SDK_TESTS";
        internal const string ResultsRelativePath = "Logs/amzn-sdk-tests.xml";

        private const string LogTag = "[AMZN GoD SDK] [Tests]";
        private const string SkipPrefKey = "AMZNGoDSDK.Tests.SkipPreBuild";
        private const string RunMenuPath = "AMZN GoD/Tests/Run SDK Tests";
        private const string SkipMenuPath = "AMZN GoD/Tests/Skip Pre-Build Tests";
        private const string AssemblyPrefix = "AMZNGoDSDK.";
        private const string AssemblySuffix = ".Tests";
        private const int MaxReportedFailures = 20;

        internal sealed class RunResult
        {
            public string[] Assemblies = Array.Empty<string>();
            public bool Finished;
            public int Passed;
            public int Failed;
            public int Skipped;
            public double Seconds;
            public readonly List<string> Failures = new List<string>();
            public string Xml;

            // A run that never reported its end is treated as failed: the gate is fail-closed.
            public bool Succeeded => Finished && Failed == 0 && Assemblies.Length > 0;
        }

        /// <summary>
        /// Every compiled SDK test assembly. Module test assemblies share their module's
        /// defineConstraints, so a disabled module drops out of the run together with its code.
        /// </summary>
        internal static string[] FindTestAssemblies()
        {
            return CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                .Select(assembly => assembly.name)
                .Where(name => name.StartsWith(AssemblyPrefix, StringComparison.Ordinal)
                               && name.EndsWith(AssemblySuffix, StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        internal static RunResult Run()
        {
            var result = new RunResult { Assemblies = FindTestAssemblies() };
            if (result.Assemblies.Length == 0)
                return result;

            var stopwatch = Stopwatch.StartNew();
            var api = UnityEngine.ScriptableObject.CreateInstance<TestRunnerApi>();
            var collector = new Collector(result);
            api.RegisterCallbacks(collector);
            try
            {
                var filter = new Filter
                {
                    testMode = TestMode.EditMode,
                    assemblyNames = result.Assemblies
                };

                // runSynchronously: Execute() returns only after the run has finished
                // (EditMode only; multi-frame [UnityTest] tests are filtered out by the runner).
                api.Execute(new ExecutionSettings(filter) { runSynchronously = true });
            }
            finally
            {
                // Callbacks are global: leaving them registered would hook every later run
                // started from the Test Runner window.
                api.UnregisterCallbacks(collector);
                UnityEngine.Object.DestroyImmediate(api);
            }

            result.Seconds = stopwatch.Elapsed.TotalSeconds;
            WriteResults(result);
            return result;
        }

        internal static bool IsSkipped(out string reason)
        {
            string env = Environment.GetEnvironmentVariable(SkipEnvVariable);
            if (!string.IsNullOrEmpty(env) && env != "0")
            {
                reason = $"переменная окружения {SkipEnvVariable}={env}";
                return true;
            }

            if (EditorPrefs.GetBool(SkipPrefKey, false))
            {
                reason = $"включено меню «{SkipMenuPath}»";
                return true;
            }

            reason = null;
            return false;
        }

        internal static string Describe(RunResult result)
        {
            var text = new StringBuilder();

            if (result.Assemblies.Length == 0)
            {
                text.Append($"{LogTag} Не найдено ни одной тестовой сборки {AssemblyPrefix}*{AssemblySuffix}.");
                return text.ToString();
            }

            if (!result.Finished)
            {
                text.AppendLine($"{LogTag} Прогон тестов не завершился (Test Runner не сообщил о конце прогона).");
                text.Append("Сборки: ").Append(string.Join(", ", result.Assemblies));
                return text.ToString();
            }

            text.Append($"{LogTag} Тесты SDK: {result.Passed} passed, {result.Failed} failed, {result.Skipped} skipped " +
                        $"за {result.Seconds:0.0} с. Сборки: {string.Join(", ", result.Assemblies)}.");

            if (result.Failed == 0)
                return text.ToString();

            text.AppendLine();
            foreach (string failure in result.Failures.Take(MaxReportedFailures))
                text.AppendLine("  • " + failure);
            if (result.Failures.Count > MaxReportedFailures)
                text.AppendLine($"  … и ещё {result.Failures.Count - MaxReportedFailures}.");
            text.Append($"Подробности: Window → General → Test Runner или {ResultsRelativePath}.");
            return text.ToString();
        }

        [MenuItem(RunMenuPath, false, 400)]
        private static void RunFromMenu()
        {
            var result = Run();
            string summary = Describe(result);
            if (result.Succeeded)
                Debug.Log(summary);
            else
                Debug.LogError(summary);

            EditorUtility.DisplayDialog(
                result.Succeeded ? "SDK tests passed" : "SDK tests failed",
                result.Finished
                    ? $"Passed: {result.Passed}\nFailed: {result.Failed}\nSkipped: {result.Skipped}\n\nДетали — в Console."
                    : "Прогон не завершился. Детали — в Console.",
                "OK");
        }

        [MenuItem(SkipMenuPath, false, 401)]
        private static void ToggleSkip()
        {
            bool skip = !EditorPrefs.GetBool(SkipPrefKey, false);
            EditorPrefs.SetBool(SkipPrefKey, skip);
            if (skip)
                Debug.LogWarning($"{LogTag} Pre-build тесты SDK отключены для этого редактора. Не забудь включить обратно.");
            else
                Debug.Log($"{LogTag} Pre-build тесты SDK снова включены.");
        }

        [MenuItem(SkipMenuPath, true)]
        private static bool ToggleSkipValidate()
        {
            Menu.SetChecked(SkipMenuPath, EditorPrefs.GetBool(SkipPrefKey, false));
            return true;
        }

        /// <summary>Batchmode entry point, see the class summary.</summary>
        public static void RunFromCommandLine()
        {
            bool gateRegistered = TypeCache.GetTypesDerivedFrom<IPreprocessBuildWithReport>()
                .Contains(typeof(SdkTestsBuildGate));
            if (!gateRegistered)
                Debug.LogError($"{LogTag} {nameof(SdkTestsBuildGate)} не найден среди IPreprocessBuildWithReport — билды пойдут без тестов.");

            var result = Run();
            string summary = Describe(result);
            if (result.Succeeded)
                Debug.Log(summary);
            else
                Debug.LogError(summary);

            EditorApplication.Exit(result.Succeeded && gateRegistered ? 0 : 1);
        }

        private static void WriteResults(RunResult result)
        {
            if (string.IsNullOrEmpty(result.Xml))
                return;

            try
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(), ResultsRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, result.Xml, Encoding.UTF8);
            }
            catch (Exception e)
            {
                // The report file is a convenience; failing to write it must not decide the build.
                Debug.LogWarning($"{LogTag} Не удалось записать {ResultsRelativePath}: {e.Message}");
            }
        }

        private sealed class Collector : ICallbacks
        {
            private readonly RunResult _result;

            public Collector(RunResult result)
            {
                _result = result;
            }

            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                _result.Finished = true;
                _result.Xml = result.ToXml().OuterXml;
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                // Fixtures and assemblies also report results; count leaf tests only.
                if (result.HasChildren)
                    return;

                switch (result.TestStatus)
                {
                    case TestStatus.Passed:
                        _result.Passed++;
                        break;
                    case TestStatus.Failed:
                        _result.Failed++;
                        _result.Failures.Add($"{result.FullName}: {FirstLine(result.Message)}");
                        break;
                    default:
                        _result.Skipped++;
                        break;
                }
            }

            private static string FirstLine(string message)
            {
                if (string.IsNullOrEmpty(message))
                    return "(no message)";

                int newline = message.IndexOfAny(new[] { '\r', '\n' });
                return newline < 0 ? message : message.Substring(0, newline);
            }
        }
    }

    /// <summary>
    /// Fails any Player build (release and development, Editor button, one-click APK, CLI) when an
    /// SDK test fails. Explicit bypass only: <see cref="SdkTestGate.SkipEnvVariable"/> or the
    /// "AMZN GoD/Tests/Skip Pre-Build Tests" menu, both logged as a warning.
    /// </summary>
    internal sealed class SdkTestsBuildGate : IPreprocessBuildWithReport
    {
        // Before the SDK guards (-1000), so the test report is the first build error. Unity 2022.3 still
        // calls the remaining preprocessors after a BuildFailedException, but the build result is Failed.
        public int callbackOrder => -2000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (SdkTestGate.IsSkipped(out string reason))
            {
                Debug.LogWarning($"[AMZN GoD SDK] [Tests] Pre-build тесты SDK пропущены: {reason}.");
                return;
            }

            var result = SdkTestGate.Run();
            string summary = SdkTestGate.Describe(result);
            if (result.Succeeded)
            {
                Debug.Log(summary);
                return;
            }

            throw new BuildFailedException(
                "Сборка остановлена: регрессия в тестах SDK.\n" + summary +
                $"\nОбход (только осознанно): {SdkTestGate.SkipEnvVariable}=1 или меню AMZN GoD/Tests/Skip Pre-Build Tests.");
        }
    }
}
