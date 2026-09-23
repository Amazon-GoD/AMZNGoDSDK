using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AMZNGoDSDK.Editor
{
    /// <summary>Resumes enabled-module setup after imports without retrying failed downloads on every reload.</summary>
    [InitializeOnLoad]
    internal static class SdkDependencyBootstrap
    {
        private const string StatePath = "Library/AmznGoDSDK/DependencyBootstrap.json";
        private const string EdmPackage = "com.google.external-dependency-manager";
        private static bool _queued = true;
        private static bool _running;
        private static bool _retry;
        private static bool _startupReady;
        private static BootstrapState _state;

        [Serializable]
        private sealed class BootstrapState
        {
            public string Fingerprint;
            public string Stage = "edm";
            public string Status = "Ожидание настройки зависимостей SDK…";
            public int Attempts;
        }

        internal static bool IsBusy => _running || _queued;
        internal static string Status => _state?.Status ?? "Ожидание настройки зависимостей SDK…";

        static SdkDependencyBootstrap()
        {
            // Initial asmdef reconciliation also runs through delayCall and may request compilation.
            EditorApplication.delayCall += () => EditorApplication.delayCall += () => _startupReady = true;
            EditorApplication.update += Tick;
        }

        internal static void RequestInstall() => _queued = true;

        internal static void Retry()
        {
            if (_running) return;
            _retry = true;
            _queued = true;
        }

        private static async void Tick()
        {
            if (!_startupReady || !_queued || _running || BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || DependencyInstaller.IsBusy ||
                AppLovinPackageInstaller.IsBusy || FirebasePackageInstaller.IsBusy ||
                SessionState.GetBool(SdkPackageExporter.ExportInProgressKey, false)) return;
#if UNITY_ANDROID
            if (AndroidToolchainInstaller.IsBusy) return;
#endif
            _running = true;
            try
            {
                if (_state == null) _state = ReadState();
                var settings = ModuleDefineManager.ConfigFileExists() ? SdkSettingsManager.LoadSettings() : null;
                if (settings == null || !settings.Enabled)
                {
                    _queued = false;
                    _state.Status = "Автоустановка ожидает сохранения включённых модулей SDK.";
                    return;
                }
                string fingerprint = Fingerprint(settings);
                if (_retry || _state.Fingerprint != fingerprint)
                {
                    _retry = false;
                    _state = new BootstrapState { Fingerprint = fingerprint };
                    SaveState();
                }
                if (_state.Stage == "done" && !RequirementsReady(settings))
                {
                    // The saved requirements can remain unchanged after packages or tools are removed.
                    _state = new BootstrapState { Fingerprint = fingerprint };
                    SaveState();
                }
                if (_state.Stage == "done" || _state.Stage == "failed")
                {
                    _queued = false;
                    return;
                }
                await RunStage(settings);
            }
            catch (Exception ex)
            {
                if (_state == null) _state = new BootstrapState();
                _state.Stage = "failed";
                _state.Status = "Настройка SDK остановлена: " + ex.Message + " Повторите установку в SDK Settings.";
                _queued = false;
                try { SaveState(); }
                catch (Exception writeError) { Debug.LogError("[AMZN GoD SDK] Не удалось сохранить состояние установки: " + writeError.Message); }
                Debug.LogError("[AMZN GoD SDK] " + _state.Status);
            }
            finally
            {
                _running = false;
                foreach (var window in Resources.FindObjectsOfTypeAll<SDKSettingsWindow>()) window.Repaint();
            }
        }

        private static async Task RunStage(SdkSettingsData settings)
        {
            switch (_state.Stage)
            {
                case "edm":
                    EditorApplication.LockReloadAssemblies();
                    try
                    {
                        if (!IsEdmReady())
                        {
                            BeginAttempt("Установка External Dependency Manager…");
                            await DependencyInstaller.EnsureRequiredDependenciesAsync();
                            // Client.List observes the completed UPM request even before the registered-package cache refreshes.
                            if (!await DependencyInstaller.AllDependenciesAreInstalled())
                                throw new IOException("External Dependency Manager не подтвердил требуемую версию.");
                        }
                        Advance("max", "Проверка AppLovin MAX и адаптеров…");
                    }
                    finally { EditorApplication.UnlockReloadAssemblies(); }
                    break;
                case "max":
                    if (settings.AppLovin != null && settings.AppLovin.Enabled && !AppLovinPackageInstaller.IsRequiredSetInstalled)
                    {
                        BeginAttempt("Установка AppLovin MAX и всех разрешённых адаптеров…");
                        await AppLovinPackageInstaller.EnsureRequiredAsync();
                    }
                    Advance("firebase", "Проверка Firebase…");
                    break;
                case "firebase":
                    EditorApplication.LockReloadAssemblies();
                    try
                    {
                        if (settings.Firebase != null && settings.Firebase.Enabled)
                        {
                            if (!FirebasePackageInstaller.IsRequiredInstallationReady)
                                BeginAttempt("Установка Firebase Analytics, Remote Config и Crashlytics…");
                            await FirebasePackageInstaller.EnsureRequiredAsync();
                        }
                        Advance("tools", "Проверка инструментов Android…");
                    }
                    finally { EditorApplication.UnlockReloadAssemblies(); }
                    break;
                case "tools":
#if UNITY_ANDROID
                    var errors = new List<string>();
                    AndroidToolchainSettings.CollectValidationErrors(errors);
                    if (errors.Count > 0)
                    {
                        BeginAttempt("Подготовка инструментов Android…");
                        await AndroidToolchainInstaller.EnsureInstalledAsync();
                        errors.Clear();
                        AndroidToolchainSettings.CollectValidationErrors(errors);
                        if (errors.Count > 0) throw new IOException(string.Join("\n", errors));
                    }
#endif
                    Advance("apply", "Применение настроек модулей…");
                    break;
                case "apply":
                    // Save the next stage before changing defines, which can reload this assembly.
                    Advance("resolve", "Разрешение нативных зависимостей…");
                    ModuleDefineManager.UpdateDefineSymbolsFromSettings();
                    EdmDependencyGenerator.Regenerate();
                    break;
                case "resolve":
#if UNITY_ANDROID
                    BeginAttempt("Разрешение нативных Android-зависимостей…");
                    EditorApplication.LockReloadAssemblies();
                    try
                    {
                        await ResolveAndroidAsync();
                        Advance("done", "Зависимости включённых модулей SDK установлены.");
                    }
                    finally { EditorApplication.UnlockReloadAssemblies(); }
#else
                    Advance("done", "Зависимости включённых модулей SDK установлены.");
#endif
                    Debug.Log("[AMZN GoD SDK] " + _state.Status);
                    break;
                default:
                    throw new IOException("Неизвестный этап установки: " + _state.Stage);
            }
        }

        private static void BeginAttempt(string status)
        {
            // Exceptions persist Stage=failed immediately. Only an interrupted domain/process can resume here.
            if (_state.Attempts >= 3)
                throw new IOException("Этап «" + _state.Stage + "» трижды прерван до подтверждения результата.");
            _state.Attempts++;
            _state.Status = status;
            SaveState();
        }

        private static void Advance(string stage, string status)
        {
            _state.Stage = stage;
            _state.Status = status;
            _state.Attempts = 0;
            SaveState();
        }

        private static bool IsEdmReady()
        {
            var package = PackageInfo.GetAllRegisteredPackages().FirstOrDefault(item => item.name == EdmPackage);
            return package != null && Version.TryParse(package.version, out var version) &&
                   version >= new Version(DependencyInstaller.ExternalDependencyManagerVersion) &&
                   !string.IsNullOrEmpty(package.resolvedPath) && File.Exists(Path.Combine(package.resolvedPath, "package.json"));
        }

        private static bool RequirementsReady(SdkSettingsData settings)
        {
            if (!IsEdmReady()) return false;
            if (settings.AppLovin?.Enabled == true && !AppLovinPackageInstaller.IsRequiredSetInstalled) return false;
            if (settings.Firebase?.Enabled == true && !FirebasePackageInstaller.IsRequiredInstallationReady) return false;
#if UNITY_ANDROID
            var errors = new List<string>();
            AndroidToolchainSettings.CollectValidationErrors(errors);
            if (errors.Count != 0) return false;
#endif
            return true;
        }

        private static string Fingerprint(SdkSettingsData settings)
        {
            var requirements = new List<string>
            {
                "bootstrap-api24-1", Application.unityVersion, EditorUserBuildSettings.activeBuildTarget.ToString(),
                "edm:" + DependencyInstaller.ExternalDependencyManagerVersion,
                "bundled:adjust-5.8.0:appmetrica-6.10.0-android-8.5.1",
                "modules:" + string.Join(",", new[] { settings.Adjust?.Enabled == true, settings.AppMetrica?.Enabled == true,
                    settings.CrossPromo?.Enabled == true, settings.InAppPurchase?.Enabled == true,
                    settings.InternetConnection?.Enabled == true, settings.DebugConsole?.Enabled == true,
                    settings.Analytics?.Enabled == true })
            };
            if (settings.AppLovin?.Enabled == true)
                requirements.AddRange(AppLovinPackageInstaller.AllowedAdapterPackageIds()
                    .Concat(new[] { AppLovinPackageInstaller.MaxPluginPackageId }).OrderBy(id => id, StringComparer.Ordinal)
                    .Select(AppLovinPackageInstaller.PackageSpec));
            if (settings.Firebase?.Enabled == true)
                requirements.Add("firebase:" + FirebasePackageInstaller.UnityVersion + ":" + settings.Firebase.EnableAnalytics +
                    ":" + settings.Firebase.EnableCrashlytics + ":" + settings.Firebase.EnableRemoteConfig);
#if UNITY_ANDROID
            requirements.Add("android:" + AndroidToolchainSettings.GradleVersion + ":" +
                AndroidToolchainSettings.AndroidGradlePluginVersion + ":" + AndroidToolchainSettings.CompileSdk + ":" +
                AndroidToolchainSettings.BuildToolsVersion + ":" + AndroidToolchainSettings.JavaMajorVersion);
#endif
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", requirements)))).Replace("-", "");
        }

        private static BootstrapState ReadState()
        {
            if (!File.Exists(StatePath)) return new BootstrapState();
            try { return JsonUtility.FromJson<BootstrapState>(File.ReadAllText(StatePath)) ?? new BootstrapState(); }
            catch (Exception ex)
            {
                Debug.LogWarning("[AMZN GoD SDK] Состояние установки не прочитано: " + ex.Message);
                return new BootstrapState();
            }
        }

        private static void SaveState()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
            string temporary = StatePath + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(_state, true));
            if (File.Exists(StatePath)) File.Replace(temporary, StatePath, null);
            else File.Move(temporary, StatePath);
        }

#if UNITY_ANDROID
        private static async Task ResolveAndroidAsync()
        {
            // No compile-time EDM reference: the SDK must compile before its first installation.
            var resolver = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("GooglePlayServices.PlayServicesResolver", false))
                .FirstOrDefault(type => type != null);
            var method = resolver?.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(Action), typeof(bool), typeof(Action<bool>) }, null);
            if (method == null) throw new IOException("Android Resolver ещё не загружен. Дождитесь импорта EDM и повторите установку.");
            // Enable Unity's templates before Resolve, avoiding EDM's upgrade confirmation on old projects.
            var templates = resolver.Assembly.GetType("GooglePlayServices.GradleTemplateResolver", false);
            var ensureTemplate = templates?.GetMethod("EnsureGradleTemplateEnabled", BindingFlags.Public | BindingFlags.Static);
            if (ensureTemplate == null) throw new IOException("В EDM отсутствует поддержка Gradle-шаблонов.");
            Directory.CreateDirectory("Assets/Plugins/Android");
            foreach (string name in new[] { "mainTemplate.gradle", "gradleTemplate.properties", "settingsTemplate.gradle" })
                if (!(bool)ensureTemplate.Invoke(null, new object[] { name }))
                    throw new IOException("Не удалось подготовить Gradle-шаблон " + name);
            var completion = new TaskCompletionSource<bool>();
            method.Invoke(null, new object[] { null, true, new Action<bool>(success => completion.TrySetResult(success)) });
            if (await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromMinutes(10))) != completion.Task)
                throw new TimeoutException("EDM не ответил за 10 минут. Блокировка перезагрузки снята; " +
                    "предыдущее разрешение зависимостей ещё может выполняться в фоне. Проверьте Unity Console перед повтором.");
            if (!await completion.Task) throw new IOException("EDM не смог разрешить Android-зависимости; подробности в Unity Console.");
        }
#endif
    }
}
