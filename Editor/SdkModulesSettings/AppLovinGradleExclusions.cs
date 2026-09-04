#if UNITY_ANDROID
using System.IO;
using System.Text;
using UnityEditor.Android;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Дописывает в сгенерированный build.gradle исключения запрещённых рекламных сеток
    /// (<see cref="ForbiddenAdNetworks"/>).
    /// <para>
    /// Второй рубеж после <see cref="AppLovinNetworkGuard"/> и единственный, который работает
    /// против ТРАНЗИТИВНЫХ зависимостей: адаптер тянет чужую сетку сам, в Dependencies.xml
    /// проекта её нет, и до Gradle о ней никто не знает. Guard такую зависимость поймает
    /// только если EDM4U уже разложил её в Assets/Plugins/Android; exclude убирает её из
    /// графа независимо от этого.
    /// </para>
    /// <para>
    /// Правится сгенерированный проект, а не шаблон: включать Custom Main Gradle Template
    /// ради этого не нужно, и настройки проекта не трогаются.
    /// </para>
    /// </summary>
    public class AppLovinGradleExclusions : IPostGenerateGradleAndroidProject
    {
        // После EDM4U (его порядок — 1000): к нашему проходу зависимости уже объявлены.
        public int callbackOrder => 2000;

        private const string MarkerBegin = "// >>> AMZN GoD SDK: forbidden ad networks — do not edit by hand";
        private const string MarkerEnd = "// <<< AMZN GoD SDK: forbidden ad networks";

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var settings = SdkSettingsManager.LoadSettings();

            // Без медиации адаптеров в проекте нет, а Amplitude/Flurry/Branch игра может
            // подключать самостоятельно — вырезать их из чужой сборки мы не вправе.
            if (settings == null || !settings.Enabled || !settings.AppLovin.Enabled)
                return;

            string buildGradlePath = Path.Combine(path, "build.gradle");

            if (!File.Exists(buildGradlePath))
            {
                Debug.LogWarning($"[AppLovinGradleExclusions] build.gradle не найден: {buildGradlePath}. " +
                                 "Исключения сеток НЕ применены — проверь сборку вручную.");
                return;
            }

            string content = File.ReadAllText(buildGradlePath);

            // Идемпотентность: Unity может перегенерировать проект несколько раз за билд.
            if (content.Contains(MarkerBegin))
                return;

            File.AppendAllText(buildGradlePath, BuildExclusionBlock());
            Debug.Log($"[AppLovinGradleExclusions] Исключения запрещённых сеток дописаны в {buildGradlePath}");
        }

        private static string BuildExclusionBlock()
        {
            var builder = new StringBuilder();

            builder.AppendLine();
            builder.AppendLine(MarkerBegin);
            builder.AppendLine("configurations.all {");

            foreach (var group in ForbiddenAdNetworks.AllMavenGroups())
                builder.AppendLine($"    exclude group: '{group}'");

            // Точечные запреты: группу целиком трогать нельзя — в com.applovin.mediation
            // лежат все разрешённые адаптеры, в com.yandex.android — AppMetrica.
            foreach (var artifact in ForbiddenAdNetworks.AllArtifacts())
                builder.AppendLine($"    exclude group: '{artifact.Group}', module: '{artifact.Module}'");

            builder.AppendLine("}");
            builder.AppendLine(MarkerEnd);

            return builder.ToString();
        }
    }
}
#endif
