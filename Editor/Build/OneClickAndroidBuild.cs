using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Сборка debug-APK одной кнопкой: проходит весь чек-лист подготовки проекта и только
    /// потом вызывает <see cref="BuildPipeline.BuildPlayer(BuildPlayerOptions)"/>.
    ///
    /// <para>Почему state machine, а не один метод: переключение платформы, установка MAX и
    /// Firebase и смена define'ов перезагружают домен. Всё, что было в памяти, теряется,
    /// поэтому шаг и его результат хранятся в <see cref="SessionState"/> (переживает reload,
    /// но не перезапуск редактора), а работа продолжается из <see cref="EditorApplication.update"/>.</para>
    ///
    /// <para>Проверки не дублируются: используются те же методы, что и в build-хуках
    /// (<see cref="AnalyticsAppTypeBuildGuard"/>, <see cref="IapSubscriptionTermBuildGuard"/>,
    /// <see cref="DisabledModuleBuildGuard"/>, AndroidBuildPreflight). Хуки всё равно отработают
    /// внутри BuildPlayer — кнопка лишь ловит ошибки заранее и показывает их списком.</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class OneClickAndroidBuild
    {
        internal enum StepKind
        {
            Preconditions = 0,
            AppType = 1,
            BuildTarget = 2,
            Dependencies = 3,
            Validation = 4,
            Build = 5
        }

        internal enum StepStatus
        {
            Pending = 0,
            Running = 1,
            Done = 2,
            Failed = 3
        }

        internal sealed class Options
        {
            public bool Development;
            public Runtime.AnalyticsAppType AppType = Runtime.AnalyticsAppType.None;
            public string OutputPath;
            public bool ExitOnFinish;
        }

        [Serializable]
        private sealed class State
        {
            public bool Active;
            public bool Finished;
            public bool Succeeded;
            public int Step;
            public int[] Statuses = new int[StepCount];
            public string[] Messages = new string[StepCount];
            public List<string> Errors = new List<string>();
            public List<string> Warnings = new List<string>();
            public bool Development;
            public int AppType;
            public string OutputPath;
            public bool ExitOnFinish;
            public bool TargetSwitchRequested;
            public double TargetSwitchRequestedAt;
            public bool DependenciesRequested;
            // The project's own values, persisted so an interrupted build can still restore them.
            public bool BuildSettingsOverridden;
            public bool SavedCustomKeystore;
            public bool SavedBuildAppBundle;
            public bool SavedExportProject;
            public double StepStartedAt;
            public long StartedAtUtcTicks;
            public string ArtifactPath;
            public long ArtifactSize;
            public double DurationSeconds;
        }

        internal const int StepCount = 6;

        internal static readonly string[] StepTitles =
        {
            "Проверка проекта",
            "App Type (Analytics)",
            "Платформа Android",
            "Зависимости SDK, инструменты Android, EDM Resolve",
            "Проверка настроек сборки",
            "Сборка APK"
        };

        private const string StateKey = "AMZNGoDSDK.OneClickAndroidBuild.State";
        private const string DevelopmentPrefKey = "AMZNGoDSDK.OneClickAndroidBuild.Development";
        private const string LogTag = "[AMZN GoD SDK] [Build APK]";
        private const string DefaultOutputDirectory = "Builds/Android";
        private const int MaxCollectedErrors = 30;
        private const int MaxErrorLength = 2000;
        private const double AppTypeResetWaitSeconds = 60;
        private const double TargetSwitchGraceSeconds = 30;

        // Per-step limits: downloads of JDK/Gradle/Firebase and EDM resolve are the slow parts.
        private static readonly double[] StepTimeoutSeconds = { 300, 300, 3600, 3600, 600, double.MaxValue };

        // static readonly instead of const: avoids CS0162 (unreachable code) in each target's compilation.
#if UNITY_ANDROID
        private static readonly bool CompiledForAndroid = true;
#else
        private static readonly bool CompiledForAndroid = false;
#endif

        private static State _state;
        private static bool _ticking;

        /// <summary>Растёт при каждом изменении состояния — окно перерисовывается только по нему.</summary>
        internal static int Revision { get; private set; }

        internal static bool IsActive => _state != null && _state.Active;
        internal static bool HasReport => _state != null && (_state.Active || _state.Finished);
        internal static bool Finished => _state != null && _state.Finished;
        internal static bool Succeeded => _state != null && _state.Succeeded;
        internal static string ArtifactPath => _state?.ArtifactPath;
        internal static long ArtifactSize => _state?.ArtifactSize ?? 0;
        internal static double DurationSeconds => _state?.DurationSeconds ?? 0;
        internal static IReadOnlyList<string> Errors => (IReadOnlyList<string>)_state?.Errors ?? Array.Empty<string>();
        internal static IReadOnlyList<string> Warnings => (IReadOnlyList<string>)_state?.Warnings ?? Array.Empty<string>();

        internal static bool DevelopmentPreference
        {
            get => EditorPrefs.GetBool(DevelopmentPrefKey, false);
            set => EditorPrefs.SetBool(DevelopmentPrefKey, value);
        }

        static OneClickAndroidBuild()
        {
            _state = LoadState();
            EditorApplication.update += Tick;
        }

        internal static StepStatus GetStatus(int step) =>
            _state == null || step < 0 || step >= StepCount ? StepStatus.Pending : (StepStatus)_state.Statuses[step];

        internal static string GetMessage(int step) =>
            _state == null || step < 0 || step >= StepCount ? null : _state.Messages[step];

        /// <summary>Запускает чек-лист. Не блокирует: шаги выполняются из EditorApplication.update.</summary>
        internal static bool Start(Options options, out string error)
        {
            if (IsActive)
            {
                error = "Сборка APK уже выполняется.";
                return false;
            }

            if (BuildPipeline.isBuildingPlayer)
            {
                error = "Unity уже собирает проект. Дождитесь окончания текущей сборки.";
                return false;
            }

            _state = new State
            {
                Active = true,
                Development = options.Development,
                AppType = (int)options.AppType,
                OutputPath = options.OutputPath,
                ExitOnFinish = options.ExitOnFinish,
                StepStartedAt = EditorApplication.timeSinceStartup,
                StartedAtUtcTicks = DateTime.UtcNow.Ticks
            };
            SaveState();
            Debug.Log($"{LogTag} Запущена сборка APK ({(options.Development ? "Development" : "Release")}, debug-подпись).");
            error = null;
            return true;
        }

        internal static void Cancel()
        {
            if (!IsActive)
                return;

            Fail("Отменено пользователем.");
        }

        private static void Tick()
        {
            if (_state == null || !_state.Active || _ticking)
                return;
            // Package imports, compilation and another build make the project state transient.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
                return;

            _ticking = true;
            try
            {
                RunCurrentStep();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Fail("Непредвиденная ошибка: " + exception.Message);
            }
            finally
            {
                _ticking = false;
            }
        }

        private static void RunCurrentStep()
        {
            var step = (StepKind)_state.Step;

            // BuildPlayer runs synchronously inside one tick, so the Build step can be Running at the
            // start of a tick only after a domain reload or crash interrupted it. Never start it again.
            if (step == StepKind.Build && GetStatus((int)step) == StepStatus.Running)
            {
                RestoreBuildSettings();
                Fail("Сборка прервана перезагрузкой редактора. Подробности в Console.");
                return;
            }

            if (GetStatus((int)step) == StepStatus.Pending)
            {
                _state.Statuses[(int)step] = (int)StepStatus.Running;
                _state.StepStartedAt = EditorApplication.timeSinceStartup;
                SaveState();
            }

            if (EditorApplication.timeSinceStartup - _state.StepStartedAt > StepTimeoutSeconds[(int)step])
            {
                Fail("Превышено время ожидания шага. Текущее состояние: " + (GetMessage((int)step) ?? "нет данных") + ".");
                return;
            }

            switch (step)
            {
                case StepKind.Preconditions:
                    RunPreconditions();
                    break;
                case StepKind.AppType:
                    RunAppType();
                    break;
                case StepKind.BuildTarget:
                    RunBuildTarget();
                    break;
                case StepKind.Dependencies:
                    RunDependencies();
                    break;
                case StepKind.Validation:
                    RunValidation();
                    break;
                case StepKind.Build:
                    RunBuild();
                    break;
                default:
                    Fail("Неизвестный шаг сборки: " + _state.Step);
                    break;
            }
        }

        private static void RunPreconditions()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Fail("Выйдите из Play Mode и запустите сборку снова.");
                return;
            }

            if (EditorUtility.scriptCompilationFailed)
            {
                Fail("В проекте есть ошибки компиляции. Исправьте их (Console) и запустите сборку снова.");
                return;
            }

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Fail("В Unity не установлен модуль Android Build Support. Добавьте его: Unity Hub → Installs → " +
                     Application.unityVersion + " → Add modules → Android Build Support (с OpenJDK и Android SDK & NDK Tools).");
                return;
            }

            if (!ModuleDefineManager.ConfigFileExists())
            {
                Fail("Конфиг SDK не найден (" + SdkSettingsManager.ConfigAssetPath + "). Откройте AMZN GoD → SDK Settings и нажмите Save Settings.");
                return;
            }

            int sceneCount = GetEnabledScenes().Length;
            if (sceneCount == 0)
            {
                Fail("В File → Build Settings нет ни одной включённой сцены. Добавьте стартовую сцену в Scenes In Build.");
                return;
            }

            CompleteStep("Сцен в сборке: " + sceneCount + ".");
        }

        private static void RunAppType()
        {
            // A fresh editor resets App Type via delayCall. Writing before that reset would be overwritten.
            if (!SessionState.GetBool(AnalyticsAppTypeSessionReset.SessionKey, false))
            {
                if (EditorApplication.timeSinceStartup - _state.StepStartedAt > AppTypeResetWaitSeconds)
                {
                    Fail("Сброс App Type при старте редактора не завершился: конфиг SDK не прочитан " +
                         "(предупреждение [AMZNGoDSDK][Analytics] в Console). Исправьте конфиг и запустите сборку снова.");
                    return;
                }

                SetMessage("Ожидание инициализации настроек Analytics…");
                return;
            }

            var settings = SdkSettingsManager.LoadRuntimeSettings();
            if (settings == null || !settings.Enabled || settings.Analytics == null || !settings.Analytics.Enabled)
            {
                CompleteStep("Модуль Analytics выключен — App Type не требуется.");
                return;
            }

            var requested = (Runtime.AnalyticsAppType)_state.AppType;
            if (requested != Runtime.AnalyticsAppType.None && settings.Analytics.AppType != requested)
            {
                settings.Analytics.AppType = requested;
                SdkSettingsManager.WriteRuntimeSettings(settings);
                SDKSettingsWindow.ReloadOpenWindows();
            }

            string error = AnalyticsAppTypeBuildGuard.GetBuildError(settings);
            if (error != null)
            {
                Fail(error);
                return;
            }

            CompleteStep("App Type: " + settings.Analytics.AppType + ".");
        }

        private static void RunBuildTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                // Android-only hooks and installers exist only after the domain recompiles with UNITY_ANDROID.
                if (!CompiledForAndroid)
                {
                    SetMessage("Ожидание перекомпиляции скриптов под Android…");
                    return;
                }

                CompleteStep(_state.TargetSwitchRequested ? "Платформа переключена на Android." : "Платформа уже Android.");
                return;
            }

            if (_state.TargetSwitchRequested)
            {
                // The switch may settle a few ticks later without isCompiling/isUpdating being set yet.
                if (EditorApplication.timeSinceStartup - _state.TargetSwitchRequestedAt < TargetSwitchGraceSeconds)
                    return;

                Fail("Unity не переключила платформу на Android. Переключите её вручную: File → Build Settings → Android → Switch Platform.");
                return;
            }

            // Saved before the switch: it reimports assets and reloads the domain.
            _state.TargetSwitchRequested = true;
            _state.TargetSwitchRequestedAt = EditorApplication.timeSinceStartup;
            _state.Messages[(int)StepKind.BuildTarget] = "Переключение платформы на Android (переимпорт ассетов может занять время)…";
            SaveState();

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                Fail("Unity не смогла переключить платформу на Android. Подробности в Console.");
        }

        private static void RunDependencies()
        {
            var settings = SdkSettingsManager.LoadRuntimeSettings();
            if (settings == null || !settings.Enabled)
            {
                CompleteStep("SDK выключен — зависимости не требуются.");
                return;
            }

            if (!_state.DependenciesRequested)
            {
                // Retry is ignored while a pass is running; wait for it so the full pass starts from scratch.
                if (SdkDependencyBootstrap.IsRunning)
                {
                    SetMessage(SdkDependencyManager.Status);
                    return;
                }

                // Retry runs every bootstrap stage: missing packages and tools are installed,
                // present ones are verified, and the stage "resolve" performs EDM Force Resolve.
                _state.DependenciesRequested = true;
                _state.Messages[(int)StepKind.Dependencies] = "Запуск проверки зависимостей…";
                SaveState();
                SdkDependencyManager.Retry();
                return;
            }

            if (IsAnyInstallerBusy())
            {
                SetMessage(SdkDependencyManager.Status);
                return;
            }

            if (SdkDependencyBootstrap.HasFailed)
            {
                Fail(SdkDependencyManager.Status);
                return;
            }

            if (!SdkDependencyBootstrap.HasCompleted)
            {
                Fail("Установка зависимостей завершилась без подтверждения результата. " + SdkDependencyManager.Status);
                return;
            }

            CompleteStep(SdkDependencyManager.Status);
        }

        private static void RunValidation()
        {
            // Saving settings and the dependency pass change define symbols after the first check;
            // a module combination that does not compile must stop here, not inside BuildPlayer.
            if (EditorUtility.scriptCompilationFailed)
            {
                Fail("После применения настроек модулей в проекте есть ошибки компиляции. " +
                     "Исправьте их (Console) или проверьте набор включённых модулей и запустите сборку снова.");
                return;
            }

            // The build hook synchronizes MAX settings at -100; do it here so the AdMob App ID check sees the same value.
            AppLovinSettingsSynchronizer.Synchronize();

            var settings = SdkSettingsManager.LoadRuntimeSettings();
            var errors = new List<string>();
            var warnings = new List<string>();

            if (settings != null && settings.Enabled)
            {
                AddIfPresent(errors, AnalyticsAppTypeBuildGuard.GetBuildError(settings));
                AddIfPresent(errors, IapSubscriptionTermBuildGuard.GetBuildError(settings));
#if UNITY_ANDROID
                AddIfPresent(errors, AndroidBuildPreflight.GetDependencyError(settings));
                AndroidBuildPreflight.CollectProjectIssues(settings, errors, warnings);
#endif
                var native = NativeDependencyValidator.ValidateFromSettings(SdkSettingsManager.LoadSettings());
                if (native.HasDuplicates)
                {
                    warnings.Add("Найдены дубликаты нативных библиотек: " +
                                 string.Join(", ", native.Duplicates.Select(duplicate => duplicate.FileName)) +
                                 ". Подробности: AMZN GoD → Debug → Check Native Duplicates.");
                }
            }

            DisabledModuleBuildGuard.CollectErrors(errors);

            _state.Warnings = warnings;
            foreach (string warning in warnings)
                Debug.LogWarning($"{LogTag} {warning}");

            if (errors.Count > 0)
            {
                _state.Errors = errors;
                Fail("Найдено проблем: " + errors.Count + ". Исправьте их и запустите сборку снова.");
                return;
            }

            CompleteStep(warnings.Count == 0 ? "Ошибок нет." : "Ошибок нет, предупреждений: " + warnings.Count + ".");
        }

        private static void RunBuild()
        {
            string outputPath = ResolveOutputPath();
            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var buildOptions = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = _state.Development ? BuildOptions.Development : BuildOptions.None
            };

            _state.OutputPath = outputPath;
            _state.Messages[(int)StepKind.Build] = "Сборка " + outputPath + "…";
            SaveState();

            var buildErrors = new List<string>();
            void CollectBuildError(string condition, string stackTrace, LogType type)
            {
                if (type != LogType.Error && type != LogType.Exception)
                    return;
                if (buildErrors.Count >= MaxCollectedErrors || string.IsNullOrWhiteSpace(condition))
                    return;
                string text = condition.Length > MaxErrorLength ? condition.Substring(0, MaxErrorLength) + "…" : condition;
                if (!buildErrors.Contains(text))
                    buildErrors.Add(text);
            }

            // Saved into State before overriding: an interrupted build restores them on the next tick.
            _state.SavedCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            _state.SavedBuildAppBundle = EditorUserBuildSettings.buildAppBundle;
            _state.SavedExportProject = EditorUserBuildSettings.exportAsGoogleAndroidProject;
            _state.BuildSettingsOverridden = true;
            SaveState();

            BuildReport report = null;
            Exception buildException = null;
            Application.logMessageReceived += CollectBuildError;
            try
            {
                // Debug signing and a plain APK are this button's contract; the project's own choice is restored below.
                PlayerSettings.Android.useCustomKeystore = false;
                EditorUserBuildSettings.buildAppBundle = false;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;

                report = BuildPipeline.BuildPlayer(buildOptions);
            }
            catch (Exception exception)
            {
                // Keep the errors collected so far instead of reducing them to the exception text.
                buildException = exception;
            }
            finally
            {
                Application.logMessageReceived -= CollectBuildError;
                RestoreBuildSettings();
            }

            if (buildException == null && report != null &&
                report.summary.result == BuildResult.Succeeded && File.Exists(outputPath))
            {
                _state.ArtifactPath = Path.GetFullPath(outputPath);
                _state.ArtifactSize = new FileInfo(outputPath).Length;
                CompleteStep("APK собран: " + _state.ArtifactPath);
                return;
            }

            if (buildException != null && !buildErrors.Contains(buildException.Message))
                buildErrors.Add(buildException.Message);

            _state.Errors = buildErrors.Count > 0
                ? buildErrors
                : new List<string> { "Сборка завершилась с результатом " + (report != null ? report.summary.result.ToString() : "unknown") + ". Подробности в Console." };

            if (report != null && report.summary.result == BuildResult.Cancelled)
                Fail("Сборка отменена.");
            else
                Fail("Сборка не удалась, ошибок: " + Math.Max(report != null ? report.summary.totalErrors : 0, buildErrors.Count) + ".");
        }

        private static void RestoreBuildSettings()
        {
            if (_state == null || !_state.BuildSettingsOverridden)
                return;

            PlayerSettings.Android.useCustomKeystore = _state.SavedCustomKeystore;
            EditorUserBuildSettings.buildAppBundle = _state.SavedBuildAppBundle;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = _state.SavedExportProject;
            _state.BuildSettingsOverridden = false;
            SaveState();
        }

        private static bool IsAnyInstallerBusy()
        {
            if (SdkDependencyManager.IsBusy || DependencyInstaller.IsBusy ||
                AppLovinPackageInstaller.IsBusy || FirebasePackageInstaller.IsBusy)
                return true;
#if UNITY_ANDROID
            if (AndroidToolchainInstaller.IsBusy)
                return true;
#endif
            return false;
        }

        private static string[] GetEnabledScenes() =>
            EditorBuildSettings.scenes
                .Where(scene => scene != null && scene.enabled && !string.IsNullOrEmpty(scene.path) && File.Exists(scene.path))
                .Select(scene => scene.path)
                .ToArray();

        /// <summary>
        /// Путь из опций: файл .apk берётся как есть, иначе это папка. По умолчанию —
        /// Builds/Android/&lt;product&gt;_&lt;version&gt;_&lt;время&gt;.apk в корне проекта.
        /// </summary>
        private static string ResolveOutputPath()
        {
            string fileName = SanitizeFileName(PlayerSettings.productName) + "_" +
                              SanitizeFileName(PlayerSettings.bundleVersion) + "_" +
                              DateTime.Now.ToString("yyyyMMdd-HHmmss") +
                              (_state.Development ? "_dev" : string.Empty) + ".apk";

            string requested = _state.OutputPath;
            if (string.IsNullOrWhiteSpace(requested))
                return Path.Combine(DefaultOutputDirectory, fileName);
            if (requested.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                return requested;
            return Path.Combine(requested, fileName);
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "build";

            var builder = new StringBuilder(value.Length);
            var invalid = Path.GetInvalidFileNameChars();
            foreach (char character in value.Trim())
                builder.Append(Array.IndexOf(invalid, character) >= 0 || char.IsWhiteSpace(character) ? '_' : character);
            return builder.ToString();
        }

        private static void AddIfPresent(List<string> errors, string error)
        {
            if (!string.IsNullOrEmpty(error))
                errors.Add(error);
        }

        private static void SetMessage(string message)
        {
            if (_state.Messages[_state.Step] == message)
                return;

            _state.Messages[_state.Step] = message;
            SaveState();
        }

        private static void CompleteStep(string message)
        {
            _state.Statuses[_state.Step] = (int)StepStatus.Done;
            _state.Messages[_state.Step] = message;
            Debug.Log($"{LogTag} {StepTitles[_state.Step]}: {message}");

            _state.Step++;
            _state.StepStartedAt = EditorApplication.timeSinceStartup;
            if (_state.Step >= StepCount)
            {
                Finish(true);
                return;
            }

            SaveState();
        }

        private static void Fail(string message)
        {
            if (_state == null || !_state.Active)
                return;

            int step = Mathf.Clamp(_state.Step, 0, StepCount - 1);
            _state.Statuses[step] = (int)StepStatus.Failed;
            _state.Messages[step] = message;
            Finish(false);
        }

        private static void Finish(bool succeeded)
        {
            _state.Active = false;
            _state.Finished = true;
            _state.Succeeded = succeeded;
            _state.DurationSeconds = (DateTime.UtcNow - new DateTime(_state.StartedAtUtcTicks, DateTimeKind.Utc)).TotalSeconds;
            SaveState();

            if (succeeded)
            {
                Debug.Log($"{LogTag} Готово за {FormatDuration(_state.DurationSeconds)}: {_state.ArtifactPath} " +
                          $"({_state.ArtifactSize / (1024f * 1024f):0.0} MB).");
            }
            else
            {
                var message = new StringBuilder();
                int step = Mathf.Clamp(_state.Step, 0, StepCount - 1);
                message.AppendLine($"{LogTag} Сборка остановлена на шаге «{StepTitles[step]}»: {_state.Messages[step]}");
                foreach (string error in _state.Errors)
                    message.AppendLine("  • " + error);
                Debug.LogError(message.ToString());
            }

            if (_state.ExitOnFinish)
                EditorApplication.Exit(succeeded ? 0 : 1);
        }

        internal static string FormatDuration(double seconds)
        {
            var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
        }

        private static State LoadState()
        {
            string json = SessionState.GetString(StateKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                var state = JsonUtility.FromJson<State>(json);
                if (state == null || state.Statuses == null || state.Statuses.Length != StepCount ||
                    state.Messages == null || state.Messages.Length != StepCount)
                    return null;
                state.Errors ??= new List<string>();
                state.Warnings ??= new List<string>();
                return state;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{LogTag} Состояние сборки не прочитано: {exception.Message}");
                return null;
            }
        }

        private static void SaveState()
        {
            SessionState.SetString(StateKey, JsonUtility.ToJson(_state));
            Revision++;
        }
    }
}
