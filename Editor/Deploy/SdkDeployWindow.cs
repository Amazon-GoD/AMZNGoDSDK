using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor.Deploy
{
    /// <summary>
    /// Окно "AMZN GoD/Deploy SDK...". Две секции:
    /// 1) Release (UPM) — сборка релизного дерева из git HEAD текущей ветки,
    ///    dry-run/локальный коммит в ветку Releases + тег. Push НЕ выполняется —
    ///    кнопка только показывает команду (замок: требуется команда пользователя).
    /// 2) Legacy .unitypackage → zip → Яндекс.Диск (как раньше).
    /// Настройки в EditorPrefs (per-user).
    /// </summary>
    internal sealed class SdkDeployWindow : EditorWindow
    {
        // Ссылка на приложение Яндекса под OAuth (публичный ClientID — не секрет).
        private const string YandexClientId = "ec5cc5de770a43d09cb2e8c3f3d2362b";
        private const string YandexAuthUrlTemplate =
            "https://oauth.yandex.ru/authorize?response_type=token&client_id={0}";

        private string _localRoot;
        private string _remoteDir;
        private string _token;
        private bool   _uploadEnabled;

        private string _releaseVersion;
        private string _releaseNote;

        private Vector2 _logScroll;
        private readonly List<string> _log = new List<string>();
        private string _testConnectionStatus;

        private SdkDeployPipeline.Result _lastResult;
        private SdkReleasePipeline.Result _lastRelease;

        [MenuItem("AMZN GoD/Deploy SDK...", false, 51)]
        public static void Open()
        {
            var w = GetWindow<SdkDeployWindow>(true, "Deploy SDK", true);
            w.minSize = new Vector2(520, 640);
            w.Show();
        }

        private void OnEnable()
        {
            _localRoot      = SdkDeployUserPrefs.LocalRoot;
            _remoteDir      = SdkDeployUserPrefs.RemoteDir;
            _token          = SdkDeployUserPrefs.YandexToken;
            _uploadEnabled  = SdkDeployUserPrefs.UploadEnabled;
            _releaseVersion = SdkDeployUserPrefs.ReleaseVersion;
            _releaseNote    = SdkDeployUserPrefs.ReleaseNote;
        }

        private void OnGUI()
        {
            DrawReleaseSection();
            EditorGUILayout.Space(10);
            DrawLegacySection();
            EditorGUILayout.Space(6);
            DrawLog();

            if (GUI.changed)
                PersistPrefs();
        }

        // ------------------------------------------------------------------
        // Release (UPM)
        // ------------------------------------------------------------------

        private void DrawReleaseSection()
        {
            EditorGUILayout.LabelField("Release (UPM) — Releases branch", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Builds a clean package tree from the committed HEAD of the current branch, " +
                "verifies it and commits it to the local 'Releases' branch with tag vX.Y.Z. " +
                "Nothing is pushed — push is a separate manual command.",
                MessageType.None);

            _releaseVersion = EditorGUILayout.TextField("Version (X.Y.Z)", _releaseVersion);
            EditorGUILayout.LabelField("Changelog note (goes into the release commit message)");
            _releaseNote = EditorGUILayout.TextArea(_releaseNote, GUILayout.MinHeight(40));

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_releaseVersion)))
                {
                    if (GUILayout.Button("Dry Run (build + verify)"))
                        RunRelease(dryRun: true);

                    if (GUILayout.Button("Release (local commit + tag)"))
                        RunRelease(dryRun: false);
                }

                using (new EditorGUI.DisabledScope(_lastRelease == null || !_lastRelease.Ok || string.IsNullOrEmpty(_lastRelease.PushCommand)))
                {
                    if (GUILayout.Button("Push..."))
                        ShowPushCommand();
                }
            }

            if (_lastRelease != null && _lastRelease.Ok && !string.IsNullOrEmpty(_lastRelease.CommitSha))
            {
                EditorGUILayout.LabelField("Last release", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(
                    $"{_lastRelease.Tag}  commit {_lastRelease.CommitSha}",
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private void RunRelease(bool dryRun)
        {
            PersistPrefs();
            _lastRelease = null;
            AppendLog($"=== Release {(dryRun ? "dry-run" : "publish")} v{_releaseVersion} started ===");

            SdkReleasePipeline.Result result;
            EditorUtility.DisplayProgressBar("Release (UPM)",
                dryRun ? "Building and verifying release tree..." : "Building, verifying and committing release...",
                0.3f);
            try
            {
                result = SdkReleasePipeline.Run(new SdkReleasePipeline.Request
                {
                    Version = _releaseVersion,
                    Note = _releaseNote,
                    DryRun = dryRun,
                    KeepStaging = dryRun, // dry-run оставляет дерево для инспекции
                });
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (!result.Ok)
            {
                AppendLog($"[release] FAILED: {result.Error}");
                EditorUtility.DisplayDialog("Release failed", result.Error, "OK");
                return;
            }

            if (result.WorkingTreeDirty)
                AppendLog("[release] WARNING: working tree has uncommitted changes — release is built from committed HEAD only.");

            AppendLog($"[release] branch {result.Branch} @ {result.HeadSha}");
            AppendLog($"[release] files in tree: {result.FileCount}; excluded: {string.Join("; ", result.ExcludedPaths)}");

            if (dryRun)
            {
                AppendLog($"[release] dry-run OK, verification passed. Tree: {result.StagingTree}");
                if (!string.IsNullOrEmpty(result.StagingTree))
                    EditorUtility.RevealInFinder(result.StagingTree);
            }
            else
            {
                AppendLog($"[release] committed {result.CommitSha} to Releases, tag {result.Tag}");
                AppendLog($"[release] to publish run manually: {result.PushCommand}");
            }

            _lastRelease = result;
            Repaint();
        }

        private void ShowPushCommand()
        {
            if (_lastRelease == null || string.IsNullOrEmpty(_lastRelease.PushCommand))
                return;

            // Замок: push выполняется ТОЛЬКО руками пользователя.
            EditorGUIUtility.systemCopyBuffer = _lastRelease.PushCommand;
            AppendLog($"[push] command copied to clipboard: {_lastRelease.PushCommand}");
            EditorUtility.DisplayDialog(
                "Push (manual)",
                "Push requires an explicit user command and is never executed by this window.\n\n" +
                "Run in the SDK repository (Assets/AMZNGoDSDK):\n\n" +
                _lastRelease.PushCommand +
                "\n\n(The command has been copied to the clipboard.)",
                "OK");
        }

        // ------------------------------------------------------------------
        // Legacy .unitypackage / zip / Yandex.Disk
        // ------------------------------------------------------------------

        private void DrawLegacySection()
        {
            EditorGUILayout.LabelField("Legacy .unitypackage export", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _localRoot = EditorGUILayout.TextField("Root folder", _localRoot);
                if (GUILayout.Button("...", GUILayout.Width(28)))
                {
                    string picked = EditorUtility.OpenFolderPanel("Choose export root", _localRoot, "");
                    if (!string.IsNullOrEmpty(picked))
                        _localRoot = picked;
                }
            }
            EditorGUILayout.HelpBox(
                "Each export creates a timestamped subfolder here with .unitypackage + .zip.",
                MessageType.None);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Yandex.Disk deploy", EditorStyles.boldLabel);
            _uploadEnabled = EditorGUILayout.ToggleLeft(
                "Upload .zip to Yandex.Disk after export",
                _uploadEnabled);

            using (new EditorGUI.DisabledScope(!_uploadEnabled))
            {
                _remoteDir = EditorGUILayout.TextField("Remote folder", _remoteDir);

                EditorGUILayout.LabelField("OAuth token");
                using (new EditorGUILayout.HorizontalScope())
                {
                    _token = EditorGUILayout.PasswordField(_token);
                    if (GUILayout.Button("Get token", GUILayout.Width(90)))
                    {
                        Application.OpenURL(string.Format(YandexAuthUrlTemplate, YandexClientId));
                        AppendLog("Opened Yandex OAuth page in browser. Copy access_token from URL after allow.");
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Test connection"))
                        TestConnection();

                    GUILayout.FlexibleSpace();
                    if (!string.IsNullOrEmpty(_testConnectionStatus))
                        EditorGUILayout.LabelField(_testConnectionStatus, EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(10);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_localRoot)))
            {
                var big = new GUIStyle(GUI.skin.button) { fixedHeight = 34, fontStyle = FontStyle.Bold };
                if (GUILayout.Button(_uploadEnabled ? "Export & Deploy SDK" : "Export SDK", big))
                    RunDeploy();
            }

            if (_lastResult != null && _lastResult.Ok)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Last export", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(_lastResult.ZipPath, EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open folder in Explorer"))
                        EditorUtility.RevealInFinder(_lastResult.ZipPath);

                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_lastResult.RemoteViewUrl)))
                    {
                        if (GUILayout.Button("Open on Yandex.Disk"))
                            Application.OpenURL(_lastResult.RemoteViewUrl);
                    }
                }
            }
        }

        private void DrawLog()
        {
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
            using (var sv = new EditorGUILayout.ScrollViewScope(_logScroll,
                GUILayout.MinHeight(140)))
            {
                _logScroll = sv.scrollPosition;
                foreach (var line in _log)
                    EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void PersistPrefs()
        {
            SdkDeployUserPrefs.LocalRoot      = _localRoot;
            SdkDeployUserPrefs.RemoteDir      = _remoteDir;
            SdkDeployUserPrefs.YandexToken    = _token;
            SdkDeployUserPrefs.UploadEnabled  = _uploadEnabled;
            SdkDeployUserPrefs.ReleaseVersion = _releaseVersion;
            SdkDeployUserPrefs.ReleaseNote    = _releaseNote;
        }

        private void TestConnection()
        {
            _testConnectionStatus = "checking...";
            Repaint();
            var uploader = new YandexDiskUploader();
            if (uploader.CheckAuth(_token, out string err, out string login))
            {
                _testConnectionStatus = string.IsNullOrEmpty(login) ? "ok" : $"ok as {login}";
                AppendLog($"[auth] ok{(string.IsNullOrEmpty(login) ? "" : $" as {login}")}");
            }
            else
            {
                _testConnectionStatus = "failed";
                AppendLog($"[auth] {err}");
            }
        }

        private void RunDeploy()
        {
            PersistPrefs();
            _lastResult = null;
            AppendLog($"=== Deploy started at {DateTime.Now:HH:mm:ss} ===");

            // Фаза 1 (main thread): SDK export.
            EditorUtility.DisplayProgressBar("Deploy SDK", "Exporting .unitypackage...", 0f);
            SdkDeployPipeline.Result result;
            try
            {
                result = SdkDeployPipeline.PrepareAndExport(new SdkDeployPipeline.Request
                {
                    LocalRoot     = _localRoot,
                    UploadEnabled = _uploadEnabled,
                    YandexToken   = _token,
                    RemoteDir     = _remoteDir,
                });
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (!result.Ok)
            {
                AppendLog($"[export] {result.Error}");
                EditorUtility.DisplayDialog("Deploy failed", result.Error, "OK");
                return;
            }
            AppendLog($"[export] {Path.GetFileName(result.UnitypackagePath)}");

            // Фаза 2 (background): zip + upload с прогрессом.
            var progress = new SdkDeployPipeline.Progress();
            var cts = new CancellationTokenSource();
            var request = new SdkDeployPipeline.Request
            {
                LocalRoot     = _localRoot,
                UploadEnabled = _uploadEnabled,
                YandexToken   = _token,
                RemoteDir     = _remoteDir,
            };

            var task = Task.Run(() => SdkDeployPipeline.PackAndUpload(request, result, progress, cts.Token));

            try
            {
                while (!task.IsCompleted)
                {
                    string title = _uploadEnabled ? "Deploy SDK" : "Packing SDK";
                    string msg = string.IsNullOrEmpty(progress.Message) ? "Working..." : progress.Message;
                    if (EditorUtility.DisplayCancelableProgressBar(title, msg, progress.Value))
                        cts.Cancel();
                    Thread.Sleep(50);
                }
                task.Wait();
            }
            catch (AggregateException ae)
            {
                result.Ok = false;
                result.Error = ae.GetBaseException().Message;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                cts.Dispose();
            }

            if (!result.Ok)
            {
                AppendLog($"[fail] {result.Error}");
                EditorUtility.DisplayDialog("Deploy failed", result.Error, "OK");
                return;
            }

            AppendLog($"[zip]  {Path.GetFileName(result.ZipPath)}");
            if (!string.IsNullOrEmpty(result.RemoteZipPath))
                AppendLog($"[disk] {result.RemoteZipPath}");

            _lastResult = result;
            Repaint();
        }

        private void AppendLog(string line)
        {
            _log.Add($"{DateTime.Now:HH:mm:ss}  {line}");
            if (_log.Count > 400)
                _log.RemoveRange(0, _log.Count - 400);
            _logScroll = new Vector2(0, float.MaxValue);
            Repaint();
        }
    }
}
