using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
#if UNITY_ANDROID
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
#endif

namespace AMZNGoDSDK.Editor
{
    /// <summary>Локальный профиль Gradle; JDK, SDK и NDK самого Unity остаются прежними.</summary>
    [InitializeOnLoad]
    public static class AndroidToolchainSettings
    {
        public const int CompileSdk = 36;
        public const int MinimumSdkWithAppLovin = 24;
        public const int JavaMajorVersion = 17;
        public const string AndroidGradlePluginVersion = "8.13.2";
        public const string GradleVersion = "8.13";
        public const string BuildToolsVersion = "36.0.0";
        public const string JavaHomeEnvironmentVariable = "AMZNGOD_ANDROID_JAVA_HOME";
        public const string SdkRootEnvironmentVariable = "AMZNGOD_ANDROID_SDK_ROOT";
        public const string GradleHomeEnvironmentVariable = "AMZNGOD_ANDROID_GRADLE_HOME";

        private static readonly string PreferencePrefix = CreatePreferencePrefix();
        public static string GradleJavaHome => ReadPath(JavaHomeEnvironmentVariable, "JavaHome");
        public static string GradleSdkRoot => ReadPath(SdkRootEnvironmentVariable, "SdkRoot");
        public static string GradleHome => ReadPath(GradleHomeEnvironmentVariable, "GradleHome");

        static AndroidToolchainSettings()
        {
#if UNITY_ANDROID
            // В том числе восстанавливаем выбор после исключения в другом build callback.
            EditorApplication.update += RestoreGradleWhenIdle;
            EditorApplication.quitting += RestoreGradle;
#endif
        }

        public static void SaveLocalPaths(string javaHome, string sdkRoot, string gradleHome)
        {
            EditorPrefs.SetString(PreferencePrefix + "JavaHome", NormalizePath(javaHome));
            EditorPrefs.SetString(PreferencePrefix + "SdkRoot", NormalizePath(sdkRoot));
            EditorPrefs.SetString(PreferencePrefix + "GradleHome", NormalizePath(gradleHome));
        }

        public static void CollectValidationErrors(List<string> errors)
        {
            ValidateJavaHome(GradleJavaHome, errors);
            ValidateSdkRoot(GradleSdkRoot, errors);
            ValidateGradleHome(GradleHome, errors);
        }

        public static void ValidateJavaHome(string directory, List<string> errors)
        {
            if (!IsAbsoluteDirectory(directory) || !File.Exists(Path.Combine(directory, "bin", Executable("java"))) ||
                !File.Exists(Path.Combine(directory, "bin", Executable("javac"))) || !File.Exists(Path.Combine(directory, "release")))
            {
                errors.Add("Для Gradle не найден JDK 17: " + DisplayPath(directory));
                return;
            }
            string release = File.ReadAllText(Path.Combine(directory, "release"));
            if (!Regex.IsMatch(release, @"(?m)^JAVA_VERSION=""17(?:[.\-+""_])"))
                errors.Add("Gradle требует JDK 17. Проверьте " + Path.Combine(directory, "release") + ".");
        }

        public static void ValidateSdkRoot(string directory, List<string> errors)
        {
            if (!IsAbsoluteDirectory(directory) || !File.Exists(Path.Combine(directory, "platforms", "android-" + CompileSdk, "android.jar")))
            {
                errors.Add("В Android SDK для Gradle отсутствует платформа android-" + CompileSdk + ": " + DisplayPath(directory));
                return;
            }
            string buildTools = Path.Combine(directory, "build-tools", BuildToolsVersion);
            if (!File.Exists(Path.Combine(buildTools, Executable("aapt2"))) ||
                !File.Exists(Path.Combine(buildTools, "lib", "d8.jar")) ||
                !File.Exists(Path.Combine(buildTools, Executable("zipalign"))))
                errors.Add("В Android SDK для Gradle отсутствует полный Build Tools " + BuildToolsVersion + ": " + directory);
        }

        public static void ValidateGradleHome(string directory, List<string> errors)
        {
            string launcher = Path.Combine(directory ?? "", "lib", "gradle-launcher-" + GradleVersion + ".jar");
            string executable = Application.platform == RuntimePlatform.WindowsEditor ? "gradle.bat" : "gradle";
            if (!IsAbsoluteDirectory(directory) || !File.Exists(launcher) || !File.Exists(Path.Combine(directory, "bin", executable)))
                errors.Add("Не найден Gradle " + GradleVersion + ": " + DisplayPath(directory));
        }

        private static string ReadPath(string environmentVariable, string preference)
        {
            string value = Environment.GetEnvironmentVariable(environmentVariable);
            return NormalizePath(string.IsNullOrWhiteSpace(value) ? EditorPrefs.GetString(PreferencePrefix + preference, "") : value);
        }

        private static string NormalizePath(string value) => (value ?? "").Trim().Trim('"').Replace('\\', '/').TrimEnd('/');
        private static string DisplayPath(string path) => string.IsNullOrEmpty(path) ? "путь ещё не настроен автоматически" : path;
        private static string Executable(string name) => Application.platform == RuntimePlatform.WindowsEditor ? name + ".exe" : name;
        private static bool IsAbsoluteDirectory(string path) => !string.IsNullOrEmpty(path) &&
            path.IndexOfAny(new[] { '\r', '\n', '\0' }) < 0 && Path.IsPathRooted(path) && Directory.Exists(path);

        private static string CreatePreferencePrefix()
        {
            string project = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            if (Application.platform == RuntimePlatform.WindowsEditor) project = project.ToLowerInvariant();
            using (var hash = SHA256.Create())
                return "AMZNGoDSDK.AndroidToolchain." + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(project))).Replace("-", "") + ".";
        }

#if UNITY_ANDROID
        public static void UseGradleForBuild()
        {
            string key = PreferencePrefix + "PreviousGradle";
            if (!SessionState.GetBool(key + ".Pending", false))
            {
                SessionState.SetString(key, AndroidExternalToolsSettings.gradlePath);
                // Unity 2022 возвращает из gradlePath уже раскрытый путь; публичного getter режима нет.
                // Этот ключ читает AndroidGradleRoot. Пустой путь в публичном setter включает bundled Gradle.
                SessionState.SetBool(key + ".Embedded", EditorPrefs.GetBool("GradleUseEmbedded", true));
                SessionState.SetBool(key + ".Pending", true);
            }
            try
            {
                AndroidExternalToolsSettings.gradlePath = GradleHome;
                if (!string.Equals(NormalizePath(AndroidExternalToolsSettings.gradlePath), NormalizePath(GradleHome),
                        Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new BuildFailedException("Unity не принял подготовленный Gradle " + GradleVersion + ": " + GradleHome);
            }
            catch
            {
                RestoreGradle();
                throw;
            }
        }

        private static void RestoreGradleWhenIdle()
        {
            if (!BuildPipeline.isBuildingPlayer)
                RestoreGradle();
        }

        internal static void RestoreGradle()
        {
            string key = PreferencePrefix + "PreviousGradle";
            if (!SessionState.GetBool(key + ".Pending", false)) return;
            try
            {
                AndroidExternalToolsSettings.gradlePath = SessionState.GetBool(key + ".Embedded", false)
                    ? string.Empty : SessionState.GetString(key, "");
                SessionState.EraseBool(key + ".Pending");
                SessionState.EraseBool(key + ".Embedded");
                SessionState.EraseString(key);
            }
            catch (Exception ex)
            {
                // Исходный путь остаётся в SessionState для диагностики, сообщение выводим один раз.
                Debug.LogWarning("[AMZN GoD SDK] Не удалось восстановить Gradle в External Tools: " + ex.Message);
                SessionState.EraseBool(key + ".Pending");
            }
        }

#endif
    }
#if UNITY_ANDROID
    public sealed class AndroidToolchainRestoreAfterBuild : IPostprocessBuildWithReport
    {
        public int callbackOrder => int.MaxValue;
        public void OnPostprocessBuild(BuildReport report) => AndroidToolchainSettings.RestoreGradle();
    }
#endif
}
