using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Явная установка единого набора Firebase; версии Android соответствуют BoM 33.11.0.
    /// https://firebase.google.com/support/release-notes/unity#version_1280_-_march_27_2025
    /// https://firebase.google.com/support/release-notes/android#bom_v33-11-0
    /// </summary>
    [InitializeOnLoad]
    public static class FirebasePackageInstaller
    {
        public const string UnityVersion = "12.8.0";
        public const int MinimumAndroidSdk = 23;
        private const string StateKey = "AMZNGoDSDK.FirebaseInstaller.Status";
        private const string PendingKey = "AMZNGoDSDK.FirebaseInstaller.Pending";
        private const string EdmPackage = "com.google.external-dependency-manager";
        private static readonly string[] Products = { "Analytics", "RemoteConfig", "Crashlytics" };
        private static CancellationTokenSource _cancellation;
        private static string _installedStatus;
        private static double _statusCheckedAt;

        public static bool IsBusy => _cancellation != null || SessionState.GetString(PendingKey, "").StartsWith("verify:", StringComparison.Ordinal);
        public static string Status => SessionState.GetString(StateKey, "");

        static FirebasePackageInstaller()
        {
            string pending = SessionState.GetString(PendingKey, "");
            if (pending.StartsWith("verify:", StringComparison.Ordinal))
                EditorApplication.update += FinishImport;
            else if (pending.Length != 0)
            {
                SetStatus("Установка прервана перезагрузкой Editor. " + pending);
                SessionState.EraseString(PendingKey);
            }
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
        }

        public static string InstalledStatus
        {
            get
            {
                if (_installedStatus != null && EditorApplication.timeSinceStartup - _statusCheckedAt < 2)
                    return _installedStatus;
                _statusCheckedAt = EditorApplication.timeSinceStartup;
                try
                {
                    _installedStatus = string.Join("; ", Products.Select(product =>
                    {
                        string path = "Assets/Firebase/Plugins/Firebase." + product + ".dll";
                        if (!File.Exists(path) || !File.Exists(path + ".meta"))
                            return product + ": отсутствует";
                        var version = Regex.Match(File.ReadAllText(path + ".meta"), @"gvh_version-([\d.]+)");
                        return product + ": " + (version.Success ? version.Groups[1].Value : "неизвестная версия");
                    }));
                    if (PackageInfo.GetAllRegisteredPackages().Any(IsFirebasePackage))
                        _installedStatus += "; найден Firebase через UPM (автозамена недоступна)";
                }
                catch (Exception ex) { _installedStatus = "Не удалось определить версию: " + ex.Message; }
                return _installedStatus;
            }
        }

        [MenuItem("AMZN GoD/Firebase/Install Firebase 12.8.0", false, 310)]
        public static void InstallFirebaseMenu()
        {
            if (IsBusy || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (!EditorUtility.DisplayDialog("Firebase " + UnityVersion,
                    "Будут загружены официальные Firebase Analytics, Remote Config и Crashlytics " + UnityVersion +
                    ".\n\nУстановленный комплект Firebase будет заменён этой версией. Исходные файлы сохранятся в " +
                    "Library/AmznGoDSDK/Firebase. Конфигурация google-services сохраняется.\n\n" +
                    "Firebase Unity требует Android minSdk 23. Настройки проекта не меняются. " +
                    "После установки Unity импортирует файлы и перекомпилирует скрипты.", "Установить", "Отмена"))
                return;
            _ = InstallAsync();
        }

        public static void Cancel() => _cancellation?.Cancel();

        private static bool IsFirebasePackage(PackageInfo package)
        {
            return package.name.StartsWith("com.google.firebase", StringComparison.OrdinalIgnoreCase);
        }

        private static void CheckProject()
        {
            var packages = PackageInfo.GetAllRegisteredPackages();
            if (packages.Any(IsFirebasePackage))
                throw new IOException("Firebase установлен через UPM. Сначала удалите его в Package Manager, чтобы не смешивать версии.");
            if (!packages.Any(package => package.name == EdmPackage))
                throw new IOException("Сначала установите External Dependency Manager кнопкой Install Miss Dependencies в SDK Settings. " +
                    "После завершения установки повторите загрузку Firebase.");
        }

        private static async Task InstallAsync()
        {
            if (IsBusy) return;
            _cancellation = new CancellationTokenSource();
            CancellationToken cancellation = _cancellation.Token;
            string operation = "Library/AmznGoDSDK/Firebase/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
            string work = operation + "/staging";
            try
            {
                CheckProject();
                // Проверяем текущий комплект до скачивания; повторяем проверку непосредственно перед записью.
                FirebaseUnityPackageUtility.ExistingFiles(new Dictionary<string, string>());
                Directory.CreateDirectory(FirebaseUnityPackageUtility.CheckedPath(work));
                SessionState.SetString(PendingKey, "Загрузка была прервана; файлы проекта не изменялись.");
                var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < Products.Length; i++)
                {
                    string product = Products[i];
                    string archive = work + "/Firebase" + product + ".unitypackage";
                    using (var request = new UnityWebRequest("https://dl.google.com/firebase/sdk/unity/dotnet4/Firebase" + product + "_" + UnityVersion + ".unitypackage", "GET"))
                    {
                        request.downloadHandler = new DownloadHandlerFile(archive) { removeFileOnAbort = true };
                        request.timeout = 300;
                        var download = request.SendWebRequest();
                        while (!download.isDone)
                        {
                            Progress("Загрузка " + product, (i + request.downloadProgress * 0.7f) / Products.Length, cancellation);
                            await Task.Delay(100);
                        }
                        cancellation.ThrowIfCancellationRequested();
                        if (request.result != UnityWebRequest.Result.Success)
                            throw new IOException("Не удалось скачать " + product + ": " + request.error);
                    }
                    var extraction = Task.Run(() => FirebaseUnityPackageUtility.StagePackage(archive, work + "/" + product, files, cancellation), cancellation);
                    // При отмене обязательно дожидаемся завершения записи staging перед его удалением.
                    while (!extraction.IsCompleted)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar("Firebase " + UnityVersion, "Проверка " + product,
                                (i + 0.8f) / Products.Length))
                            _cancellation.Cancel();
                        SetStatus("Проверка " + product);
                        await Task.Delay(100);
                    }
                    await extraction;
                }
                FirebaseUnityPackageUtility.ValidatePinned(path => files.TryGetValue(path, out string source) ? source :
                    throw new IOException("В комплекте нет файла " + path));
                CheckProject();
                var previous = FirebaseUnityPackageUtility.ExistingFiles(files);
                Progress("Комплект проверен. Установка файлов", 1, cancellation);
                string backup = operation + "/backup";
                EditorApplication.LockReloadAssemblies();
                try
                {
                    AssetDatabase.DisallowAutoRefresh();
                    try
                    {
                        SessionState.SetString(PendingKey, "Проверьте резервную копию: " + backup);
                        FirebaseUnityPackageUtility.Commit(files, previous, backup);
                        SessionState.SetString(PendingKey, "verify:" + backup);
                        SetStatus("Файлы Firebase " + UnityVersion + " проверены. Ожидание импорта и компиляции Unity…");
                    }
                    finally { AssetDatabase.AllowAutoRefresh(); }
                }
                finally
                {
                    try { AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); }
                    finally { EditorApplication.UnlockReloadAssemblies(); }
                }
                EditorApplication.update -= FinishImport;
                EditorApplication.update += FinishImport;
            }
            catch (OperationCanceledException)
            {
                SessionState.EraseString(PendingKey);
                SetStatus("Загрузка отменена. Файлы Firebase не изменены.");
            }
            catch (Exception ex)
            {
                SessionState.EraseString(PendingKey);
                SetStatus("Ошибка установки: " + ex.Message);
                Debug.LogError("[FirebaseInstaller] " + ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _cancellation.Dispose();
                _cancellation = null;
                _installedStatus = null;
                try
                {
                    FirebaseUnityPackageUtility.CheckedPath(work);
                    // Все дочерние пути созданы установщиком; проверка запрещает подмену junction/symlink.
                    foreach (string file in FirebaseUnityPackageUtility.Files(work)) { }
                    if (Directory.Exists(work)) Directory.Delete(work, true);
                }
                catch (Exception ex) { Debug.LogWarning("[FirebaseInstaller] Не удалось удалить staging: " + ex.Message); }
            }
        }

        private static void Progress(string message, float progress, CancellationToken cancellation)
        {
            if (EditorUtility.DisplayCancelableProgressBar("Firebase " + UnityVersion, message, progress))
                _cancellation.Cancel();
            cancellation.ThrowIfCancellationRequested();
            SetStatus(message);
        }

        private static void SetStatus(string message)
        {
            SessionState.SetString(StateKey, message);
            foreach (var window in Resources.FindObjectsOfTypeAll<SDKSettingsWindow>())
                window.Repaint();
        }

        private static void FinishImport()
        {
            if (_cancellation != null || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            EditorApplication.update -= FinishImport;
            string pending = SessionState.GetString(PendingKey, "");
            if (!pending.StartsWith("verify:", StringComparison.Ordinal)) return;
            SessionState.EraseString(PendingKey);
            try
            {
                FirebaseUnityPackageUtility.ValidatePinned(path => path);
                ModuleDefineManager.UpdateDefineSymbolsFromSettings();
                SetStatus("Установлены файлы Firebase " + UnityVersion + ": Analytics, Remote Config, Crashlytics. Резервная копия: " + pending.Substring(7));
                Debug.Log("[FirebaseInstaller] " + Status);
            }
            catch (Exception ex)
            {
                SetStatus("Проверка после импорта не пройдена: " + ex.Message + ". Резервная копия: " + pending.Substring(7));
                Debug.LogError("[FirebaseInstaller] " + Status);
            }
        }
    }
}
