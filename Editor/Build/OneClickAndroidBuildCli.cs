using System;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// CLI-вход кнопки «Собрать APK» для CI / batchmode.
    ///
    /// <code>
    /// Unity.exe -batchmode -nographics -projectPath "C:\path\to\project" -buildTarget Android
    ///   -executeMethod AMZNGoDSDK.Editor.OneClickAndroidBuildCli.Run
    ///   -amznAppType Free [-amznOutput Builds/Android/game.apk] [-amznDevelopment]
    ///   -logFile build.log
    /// </code>
    ///
    /// <para>Без <c>-quit</c>: шаги идут асинхронно и переживают перезагрузки домена (установка
    /// пакетов, смена define'ов). С <c>-quit</c> Unity закрылась бы сразу после возврата из Run,
    /// не начав сборку. Процесс завершается сам: код выхода 0 — APK собран, 1 — ошибка.</para>
    ///
    /// <para><c>-amznAppType</c> (Free | Paid) обязателен при включённом модуле Analytics:
    /// App Type сбрасывается при каждом запуске редактора, а у CI это каждый прогон.</para>
    /// </summary>
    public static class OneClickAndroidBuildCli
    {
        private const string LogTag = "[AMZN GoD SDK] [Build APK CLI]";

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();

            if (HasFlag(args, "-quit"))
            {
                Debug.LogError($"{LogTag} Запускайте без -quit: сборка идёт асинхронно, и Unity закрылась бы до её начала. " +
                               "Процесс завершится сам с кодом 0 или 1.");
                EditorApplication.Exit(1);
                return;
            }

            var options = new OneClickAndroidBuild.Options
            {
                Development = HasFlag(args, "-amznDevelopment"),
                OutputPath = GetValue(args, "-amznOutput"),
                ExitOnFinish = true
            };

            string appType = GetValue(args, "-amznAppType");
            if (!string.IsNullOrEmpty(appType))
            {
                if (string.Equals(appType, "Free", StringComparison.OrdinalIgnoreCase))
                    options.AppType = Runtime.AnalyticsAppType.Free;
                else if (string.Equals(appType, "Paid", StringComparison.OrdinalIgnoreCase))
                    options.AppType = Runtime.AnalyticsAppType.Paid;
                else
                {
                    Debug.LogError($"{LogTag} Неизвестное значение -amznAppType «{appType}». Допустимо: Free или Paid.");
                    EditorApplication.Exit(1);
                    return;
                }
            }

            if (!OneClickAndroidBuild.Start(options, out string error))
            {
                Debug.LogError($"{LogTag} {error}");
                EditorApplication.Exit(1);
            }
        }

        private static bool HasFlag(string[] args, string name)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string GetValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }
    }
}
