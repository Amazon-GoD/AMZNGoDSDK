#if UNITY_ANDROID
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Подменяет версию R8/D8 в корневом build.gradle сгенерированного Android-проекта.
    ///
    /// <para><b>Зачем.</b> AGP 7.4.2 — потолок Unity 2022.3 (bundled Gradle 7.5.1 + JDK 11;
    /// AGP 8.9 требует Gradle 8.11+ и JDK 17) — несёт R8 <b>4.0.48</b>. Его парсер Kotlin
    /// metadata понимает версии не выше 1.8, и на дексинге он падает:</para>
    /// <code>
    /// Execution failed for task ':launcher:mergeExtDexRelease'
    /// ERROR:D8: com.android.tools.r8.kotlin.H
    /// java.lang.NullPointerException ... Error while dexing.
    /// </code>
    /// <para>Ломает сборку не одна библиотека: <c>DexingWithClasspathTransform</c> дексит
    /// каждый артефакт вместе со всем runtime-classpath, поэтому в логе валится почти весь
    /// список AAR — applovin-sdk, moloco, unity-ads, inmobi, chartboost, ysonetwork,
    /// firebase-common, audience-network.</para>
    ///
    /// <para><b>Почему не лечится версиями зависимостей.</b> Прогон D8 показал, что 4.0.48
    /// не дексит даже kotlinx-coroutines 1.8.1 (Kotlin metadata 1.9), а её тянут
    /// lifecycle 2.7.0 и work-runtime 2.9.1 — то есть не только медиация. Пришлось бы
    /// откатывать весь стек к библиотекам 2023 года. Лечится обновлением дексера:
    /// R8 8.2.47 дексит всё перечисленное и работает на JDK 11 из Unity 2022.3.</para>
    ///
    /// <para><b>Почему инжектим, а не просим править шаблон.</b> Правка живёт в
    /// <c>Assets/Plugins/Android/baseProjectTemplate.gradle</c> — это файл ПРОЕКТА, он не
    /// входит в поставляемый .unitypackage (экспортируется только Assets/AMZNGoDSDK).
    /// Партнёр упёрся бы в тот же дексинг и потратил день на диагностику. Здесь правится
    /// сгенерированный проект, включать Custom Base Gradle Template не требуется.</para>
    /// </summary>
    public class AndroidR8Injector : IPostGenerateGradleAndroidProject
    {
        // После AppLovinGradleExclusions (2000): порядок между ними не важен, файлы разные,
        // но держим инжекторы SDK одной группой в конце очереди.
        public int callbackOrder => 2500;

        /// <summary>
        /// Минимальный скачок от 4.0.48, который дексит Kotlin 1.9/2.x и запускается на
        /// JDK 11 из Unity 2022.3. Более новые R8 (8.10+) могут требовать JDK 17, которого
        /// в редакторе нет. Проверено прогоном D8 — см. notes/config-android-r8-override.md.
        /// </summary>
        private const string R8Version = "8.2.47";

        /// <summary>
        /// AGP, начиная с которого подмена не нужна и вредна: там свой R8 новее нашего.
        /// </summary>
        private const int MinAgpMajorWithModernR8 = 8;

        private const string MarkerBegin = "// >>> AMZN GoD SDK: R8 override — do not edit by hand";
        private const string MarkerEnd = "// <<< AMZN GoD SDK: R8 override";
        private const string LogTag = "[AMZN GoD SDK] [AndroidR8Injector]";

        // id 'com.android.application' version '7.4.2' apply false
        private static readonly Regex AgpVersionRegex = new Regex(
            @"id\s+['""]com\.android\.application['""]\s+version\s+['""](?<version>[\d.]+)['""]",
            RegexOptions.Compiled);

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // Правим чужой build.gradle только когда SDK включён. С выключенным SDK его модулей
            // в сборке нет, Kotlin 2.x-зависимостей мы не приносим — а значит и трогать дексер
            // проекта не вправе: подмена R8 у партнёра, чья сборка и так работает, это регресс,
            // а не защита. Тот же гард стоит в AppLovinGradleExclusions и IapManifestInjector.
            var settings = SdkSettingsManager.LoadRuntimeSettings();
            if (settings == null || !settings.Enabled)
                return;

            // path — папка unityLibrary; корень Gradle-проекта на уровень выше.
            var root = Directory.GetParent(path);
            if (root == null)
            {
                Debug.LogWarning($"{LogTag} не удалось определить корень Gradle-проекта для '{path}' — пропускаю.");
                return;
            }

            string rootGradlePath = Path.Combine(root.FullName, "build.gradle");
            if (!File.Exists(rootGradlePath))
            {
                Debug.LogWarning($"{LogTag} {rootGradlePath} не найден — R8 не подменён. " +
                                 "Если сборка упадёт на mergeExtDex с 'com.android.tools.r8.kotlin.H', " +
                                 "пропиши classpath 'com.android.tools:r8' вручную.");
                return;
            }

            string content;
            try
            {
                content = File.ReadAllText(rootGradlePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogTag} не удалось прочитать {rootGradlePath}: {ex.Message}");
                return;
            }

            // Идемпотентность + уважение к чужой настройке: если R8 уже задан (нами или
            // руками в baseProjectTemplate), второй classpath ломает разрешение зависимостей.
            if (content.Contains(MarkerBegin) || content.Contains("com.android.tools:r8"))
                return;

            if (!TryGetAgpVersion(content, out Version agp))
            {
                Debug.Log($"{LogTag} версию AGP в {rootGradlePath} определить не удалось — не вмешиваюсь.");
                return;
            }

            if (agp.Major >= MinAgpMajorWithModernR8)
            {
                // Понизить R8 под современным AGP — верный способ сломать рабочую сборку.
                Debug.Log($"{LogTag} AGP {agp} несёт собственный актуальный R8 — подмена не нужна.");
                return;
            }

            try
            {
                File.WriteAllText(rootGradlePath, Inject(content));
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogTag} не удалось записать {rootGradlePath}: {ex.Message}");
                return;
            }

            Debug.Log($"{LogTag} AGP {agp} несёт R8 4.x, который не дексит Kotlin 1.9+ — " +
                      $"подменил на {R8Version} в {rootGradlePath}.");
        }

        private static bool TryGetAgpVersion(string content, out Version version)
        {
            version = null;

            var match = AgpVersionRegex.Match(content);
            if (!match.Success)
                return false;

            return Version.TryParse(match.Groups["version"].Value, out version);
        }

        /// <summary>
        /// Вставляет buildscript ПЕРЕД блоком plugins: Gradle разрешает раньше plugins {}
        /// единственный блок — buildscript {}. Если plugins {} в файле нет (нестандартный
        /// шаблон), дописываем в начало — там он тоже валиден.
        /// </summary>
        private static string Inject(string content)
        {
            string newLine = content.Contains("\r\n") ? "\r\n" : "\n";

            var block = new StringBuilder();
            block.Append(MarkerBegin).Append(newLine);
            block.Append("// AGP 7.x несёт R8 4.x: он не читает Kotlin metadata новее 1.8 и падает на дексинге").Append(newLine);
            block.Append("// с 'com.android.tools.r8.kotlin.H'. Подробности — AndroidR8Injector.cs.").Append(newLine);
            block.Append("buildscript {").Append(newLine);
            block.Append("    repositories {").Append(newLine);
            block.Append("        google()").Append(newLine);
            block.Append("        mavenCentral()").Append(newLine);
            block.Append("    }").Append(newLine);
            block.Append("    dependencies {").Append(newLine);
            block.Append($"        classpath 'com.android.tools:r8:{R8Version}'").Append(newLine);
            block.Append("    }").Append(newLine);
            block.Append("}").Append(newLine);
            block.Append(MarkerEnd).Append(newLine).Append(newLine);

            int pluginsIndex = content.IndexOf("plugins {", StringComparison.Ordinal);

            return pluginsIndex >= 0
                ? content.Insert(pluginsIndex, block.ToString())
                : block + content;
        }
    }
}
#endif
