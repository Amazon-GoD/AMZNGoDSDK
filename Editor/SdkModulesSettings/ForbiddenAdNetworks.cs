using System;
using System.Collections.Generic;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Пара «группа + артефакт» для случаев, когда группу целиком запрещать нельзя.
    /// <para>
    /// Таких случаев два, и оба неочевидные. Первый: сами адаптеры MAX публикуются в общей
    /// группе <c>com.applovin.mediation</c> — запретив её, снесёшь заодно все разрешённые
    /// адаптеры. Второй: рекламный SDK Яндекса лежит в той же группе <c>com.yandex.android</c>,
    /// что и AppMetrica, которая остаётся в SDK и под запрет НЕ подпадает.
    /// </para>
    /// </summary>
    public sealed class ForbiddenArtifact
    {
        public string Group;
        public string Module;

        public ForbiddenArtifact(string group, string module)
        {
            Group = group;
            Module = module;
        }
    }

    /// <summary>
    /// Рекламные и трекинговые SDK, которых не должно быть в сборке с медиацией AppLovin.
    /// <para>
    /// Единственный источник правды для двух механизмов: <see cref="AppLovinNetworkGuard"/>
    /// (проверка перед билдом) и <see cref="AppLovinGradleExclusions"/> (исключения в Gradle).
    /// </para>
    /// <para>
    /// «Просто не ставить адаптер» как единственная мера не работает: часть списка (Amplitude,
    /// Flurry, Branch) — вообще не адаптеры MAX, а аналитика/атрибуция, и попасть в сборку они
    /// могут только транзитивно, через зависимости чужого адаптера. Ловится это лишь по факту,
    /// в резолвнутых зависимостях.
    /// </para>
    /// </summary>
    public sealed class ForbiddenAdNetwork
    {
        public string DisplayName;

        /// <summary>
        /// Maven group id, который можно запретить целиком: ничего разрешённого в нём нет.
        /// Идёт и в Gradle-исключения, и в проверку имён .aar/.jar и spec'ов Dependencies.xml.
        /// </summary>
        public string[] MavenGroups = Array.Empty<string>();

        /// <summary>Точечные запреты внутри разделяемых групп.</summary>
        public ForbiddenArtifact[] Artifacts = Array.Empty<ForbiddenArtifact>();

        /// <summary>
        /// Фрагменты id UPM-пакетов адаптеров в реестре AppLovin
        /// (com.applovin.mediation.adapters.&lt;сетка&gt;.&lt;платформа&gt;). Начиная с MAX 8.0
        /// адаптеры ставятся через scoped registry, и в манифесте проекта они выглядят
        /// именно так — ни на maven-группу, ни на имя .aar это не похоже, поэтому нужен
        /// отдельный признак. Пусто — адаптера этой сетки в реестре нет.
        /// </summary>
        public string[] UpmPackageTokens = Array.Empty<string>();

        /// <summary>
        /// Имена папок адаптеров MAX (Assets/MaxSdk/Mediation/&lt;Network&gt;). Сравнение
        /// точное, без учёта регистра. Пусто — у сетки нет адаптера MAX.
        /// </summary>
        public string[] AdapterFolderNames = Array.Empty<string>();
    }

    public static class ForbiddenAdNetworks
    {
        private const string MaxMediationGroup = "com.applovin.mediation";

        public static readonly IReadOnlyList<ForbiddenAdNetwork> All = new List<ForbiddenAdNetwork>
        {
            new ForbiddenAdNetwork
            {
                DisplayName = "Tapjoy",
                MavenGroups = new[] { "com.tapjoy" },
                Artifacts = new[] { new ForbiddenArtifact(MaxMediationGroup, "tapjoy-adapter") },
                AdapterFolderNames = new[] { "Tapjoy" },
            },
            new ForbiddenAdNetwork
            {
                // MoPub закрыт в 2022 (поглощён AppLovin), актуального адаптера MAX нет.
                // Держим в списке на случай legacy-зависимости в чужом плагине.
                DisplayName = "MoPub",
                MavenGroups = new[] { "com.mopub" },
                Artifacts = new[] { new ForbiddenArtifact(MaxMediationGroup, "mopub-adapter") },
                AdapterFolderNames = new[] { "MoPub" },
            },
            new ForbiddenAdNetwork
            {
                // Inneractive → Fyber → DT Exchange: одна сетка под тремя именами в разные
                // годы, артефакты встречаются под всеми тремя.
                DisplayName = "Inneractive / Fyber / DT Exchange",
                MavenGroups = new[] { "com.fyber", "com.inneractive", "com.digitalturbine" },
                Artifacts = new[]
                {
                    new ForbiddenArtifact(MaxMediationGroup, "fyber-adapter"),
                    new ForbiddenArtifact(MaxMediationGroup, "inneractive-adapter"),
                    new ForbiddenArtifact(MaxMediationGroup, "dtexchange-adapter"),
                },
                UpmPackageTokens = new[] { "mediation.adapters.fyber." },
                AdapterFolderNames = new[] { "Fyber", "Inneractive", "DTExchange" },
            },
            new ForbiddenAdNetwork
            {
                DisplayName = "Appnext",
                MavenGroups = new[] { "com.appnext" },
                Artifacts = new[] { new ForbiddenArtifact(MaxMediationGroup, "appnext-adapter") },
                AdapterFolderNames = new[] { "Appnext" },
            },
            new ForbiddenAdNetwork
            {
                DisplayName = "Amplitude",
                MavenGroups = new[] { "com.amplitude" },
            },
            new ForbiddenAdNetwork
            {
                DisplayName = "Flurry",
                MavenGroups = new[] { "com.flurry" },
                Artifacts = new[] { new ForbiddenArtifact(MaxMediationGroup, "flurry-adapter") },
                AdapterFolderNames = new[] { "Flurry" },
            },
            new ForbiddenAdNetwork
            {
                DisplayName = "Branch",
                MavenGroups = new[] { "io.branch" },
            },
            new ForbiddenAdNetwork
            {
                // ТОЛЬКО реклама Яндекса. com.yandex.android нельзя запрещать целиком:
                // в этой же группе лежит mobmetricalib — библиотека модуля AppMetrica,
                // который остаётся в SDK.
                DisplayName = "Yandex Ads",
                MavenGroups = new[] { "com.yandex.ads", "com.yandex.mobile.ads" },
                Artifacts = new[]
                {
                    new ForbiddenArtifact("com.yandex.android", "mobileads"),
                    new ForbiddenArtifact(MaxMediationGroup, "yandex-adapter"),
                },
                UpmPackageTokens = new[] { "mediation.adapters.yandex." },
                AdapterFolderNames = new[] { "Yandex" },
            },
            new ForbiddenAdNetwork
            {
                DisplayName = "VK Ads / myTarget",
                MavenGroups = new[] { "com.my.target", "com.vk.ads" },
                Artifacts = new[]
                {
                    new ForbiddenArtifact(MaxMediationGroup, "mytarget-adapter"),
                    new ForbiddenArtifact(MaxMediationGroup, "vkads-adapter"),
                },
                UpmPackageTokens = new[] { "mediation.adapters.mytarget.", "mediation.adapters.vkads." },
                AdapterFolderNames = new[] { "VKAds", "MyTarget" },
            },
            new ForbiddenAdNetwork
            {
                // Причина техническая, а не политическая. play-services-ads регистрирует
                // MobileAdsInitProvider — ContentProvider, который Android поднимает при старте
                // ПРОЦЕССА, до первой Activity. Без meta-data com.google.android.gms.ads.APPLICATION_ID
                // он бросает IllegalStateException, и приложение падает чёрным экраном ещё до
                // загрузки Unity (инцидент 2026-09-03). Сборка идёт под Amazon Appstore, где
                // Google Play Services нет вообще: AdMob там не даёт филла, а крашит гарантированно.
                //
                // ВАЖНО: группу com.google.android.gms целиком запрещать НЕЛЬЗЯ — в ней
                // play-services-base (Firebase) и play-services-ads-identifier (GAID для MAX),
                // которые остаются в SDK. Поэтому только точечные запреты адаптеров MAX.
                DisplayName = "Google AdMob / Ad Manager",
                Artifacts = new[]
                {
                    new ForbiddenArtifact(MaxMediationGroup, "google-adapter"),
                    new ForbiddenArtifact(MaxMediationGroup, "google-ad-manager-adapter"),
                },
                UpmPackageTokens = new[]
                {
                    "mediation.adapters.google.",
                    "mediation.adapters.googleadmanager.",
                },
                AdapterFolderNames = new[] { "Google", "GoogleAdManager" },
            },
            new ForbiddenAdNetwork
            {
                DisplayName = "BidMachine",
                MavenGroups = new[] { "io.bidmachine" },
                Artifacts = new[] { new ForbiddenArtifact(MaxMediationGroup, "bidmachine-adapter") },
                UpmPackageTokens = new[] { "mediation.adapters.bidmachine." },
                AdapterFolderNames = new[] { "BidMachine" },
            },
        };

        /// <summary>Группы, которые запрещаются целиком (Gradle: exclude group).</summary>
        public static IEnumerable<string> AllMavenGroups()
        {
            foreach (var network in All)
            {
                foreach (var group in network.MavenGroups)
                    yield return group;
            }
        }

        /// <summary>Точечные запреты (Gradle: exclude group + module).</summary>
        public static IEnumerable<ForbiddenArtifact> AllArtifacts()
        {
            foreach (var network in All)
            {
                foreach (var artifact in network.Artifacts)
                    yield return artifact;
            }
        }

        /// <summary>
        /// Возвращает сетку, которой принадлежит строка (spec из Dependencies.xml, имя .aar,
        /// путь), либо null. Для точечных запретов требуется совпадение И группы, И артефакта —
        /// иначе com.yandex.android:mobmetricalib (AppMetrica) попал бы под запрет рекламы.
        /// Сравнение без учёта регистра.
        /// </summary>
        public static ForbiddenAdNetwork MatchByGroup(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            foreach (var network in All)
            {
                foreach (var group in network.MavenGroups)
                {
                    if (Contains(value, group))
                        return network;
                }

                foreach (var artifact in network.Artifacts)
                {
                    if (Contains(value, artifact.Group) && Contains(value, artifact.Module))
                        return network;
                }

                foreach (var token in network.UpmPackageTokens)
                {
                    if (Contains(value, token))
                        return network;
                }
            }

            return null;
        }

        /// <summary>Возвращает сетку по имени папки адаптера MAX, либо null.</summary>
        public static ForbiddenAdNetwork MatchByAdapterFolder(string folderName)
        {
            if (string.IsNullOrEmpty(folderName))
                return null;

            foreach (var network in All)
            {
                foreach (var adapter in network.AdapterFolderNames)
                {
                    if (string.Equals(folderName, adapter, StringComparison.OrdinalIgnoreCase))
                        return network;
                }
            }

            return null;
        }

        private static bool Contains(string haystack, string needle)
        {
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
