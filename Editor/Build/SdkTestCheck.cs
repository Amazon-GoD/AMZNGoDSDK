using System;
using System.Collections.Generic;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Runs the SDK EditMode tests from editor code that must not reference the Test Framework.
    ///
    /// AMZNGoDSDK.Editor cannot reference UnityEditor.TestRunner (a partner project without the Test
    /// Framework would lose the whole SDK editor assembly), so the test assembly AMZNGoDSDK.Editor.Tests
    /// registers <see cref="Runner"/> on load. In a partner project the test assembly is not compiled and
    /// <see cref="Runner"/> stays null.
    /// </summary>
    internal static class SdkTestCheck
    {
        internal const string SkipEnvVariable = "AMZN_SKIP_SDK_TESTS";

        internal enum Purpose
        {
            // One-click APK: fail fast before minutes of dependency work. The pre-build gate still runs in BuildPlayer.
            PlayerBuild = 0,
            // Export / Deploy / Release of the SDK itself: a regression must not reach partners.
            SdkRelease = 1
        }

        internal sealed class Result
        {
            public bool Passed;
            public string Summary;
            public readonly List<string> Failures = new List<string>();
        }

        /// <summary>Registered by AMZNGoDSDK.Editor.Tests; null when the test assembly is not compiled.</summary>
        internal static Func<Purpose, Result> Runner;

        /// <summary>
        /// Player builds: no test assembly means a partner project, nothing to run.
        /// SDK release: fail-closed — releasing without the tests is allowed only through <see cref="SkipEnvVariable"/>.
        /// </summary>
        internal static Result Run(Purpose purpose)
        {
            if (Runner != null)
                return Runner(purpose);

            var result = new Result { Passed = true, Summary = "Тестовая сборка SDK не скомпилирована — тесты не запускались." };
            if (purpose != Purpose.SdkRelease)
                return result;

            string env = Environment.GetEnvironmentVariable(SkipEnvVariable);
            if (!string.IsNullOrEmpty(env) && env != "0")
            {
                result.Summary = $"Тесты SDK пропущены: переменная окружения {SkipEnvVariable}={env}.";
                return result;
            }

            result.Passed = false;
            result.Summary = "Выпуск SDK без тестов запрещён: тестовая сборка AMZNGoDSDK.Editor.Tests не скомпилирована " +
                             "(нет пакета com.unity.test-framework или ошибки компиляции в Tests/). " +
                             $"Осознанный обход: {SkipEnvVariable}=1.";
            return result;
        }

        /// <summary>Summary plus the first failures, for an error dialog or a pipeline error string.</summary>
        internal static string Describe(Result result, int maxFailures = 10)
        {
            if (result.Failures.Count == 0)
                return result.Summary;

            var lines = new List<string> { result.Summary };
            for (int i = 0; i < result.Failures.Count && i < maxFailures; i++)
                lines.Add("  • " + result.Failures[i]);
            if (result.Failures.Count > maxFailures)
                lines.Add($"  … и ещё {result.Failures.Count - maxFailures}.");
            return string.Join("\n", lines);
        }
    }
}
