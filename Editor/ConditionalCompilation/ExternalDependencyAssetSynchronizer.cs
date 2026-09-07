using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Внешний Firebase SDK принадлежит consumer-проекту, но при включении через
    /// AMZN module его EDM XML не должен продолжать добавлять Android/iOS зависимости
    /// после выключения модуля. Файлы не удаляются: расширение меняется обратимо.
    /// </summary>
    public static class ExternalDependencyAssetSynchronizer
    {
        private const string DisabledSuffix = FirebaseUnityPackageUtility.DisabledDependencySuffix;
        private static bool? _pendingFirebaseState;

        private static readonly string[] FirebaseDependencyFiles =
        {
            "Assets/Firebase/Editor/AppDependencies.xml",
            "Assets/Firebase/Editor/AnalyticsDependencies.xml",
            "Assets/Firebase/Editor/RemoteConfigDependencies.xml",
            "Assets/Firebase/Editor/CrashlyticsDependencies.xml",
        };

        public static void SynchronizeFirebase(bool enabled)
        {
            if (FirebasePackageInstaller.IsBusy)
            {
                _pendingFirebaseState = enabled;
                EditorApplication.update -= ApplyPendingFirebaseState;
                EditorApplication.update += ApplyPendingFirebaseState;
                return;
            }
            _pendingFirebaseState = null;
            EditorApplication.update -= ApplyPendingFirebaseState;
            foreach (string activePath in FirebaseDependencyFiles)
            {
                string disabledPath = activePath + DisabledSuffix;
                string source = enabled ? disabledPath : activePath;
                string destination = enabled ? activePath : disabledPath;
                if (!System.IO.File.Exists(source))
                    continue;
                if (System.IO.File.Exists(destination))
                {
                    Debug.LogError($"[AMZN GoD SDK] Нельзя переключить Firebase dependency: " +
                                   $"оба файла существуют ({source}, {destination}).");
                    continue;
                }

                string error = AssetDatabase.MoveAsset(source, destination);
                if (!string.IsNullOrEmpty(error))
                    Debug.LogError($"[AMZN GoD SDK] Не удалось переместить {source}: {error}");
            }
        }

        private static void ApplyPendingFirebaseState()
        {
            if (FirebasePackageInstaller.IsBusy || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (_pendingFirebaseState.HasValue) SynchronizeFirebase(_pendingFirebaseState.Value);
            else EditorApplication.update -= ApplyPendingFirebaseState;
        }

        public static IEnumerable<string> ActiveFirebaseDependencyFiles()
        {
            foreach (string path in FirebaseDependencyFiles)
            {
                if (System.IO.File.Exists(path))
                    yield return path;
            }
        }
    }
}
