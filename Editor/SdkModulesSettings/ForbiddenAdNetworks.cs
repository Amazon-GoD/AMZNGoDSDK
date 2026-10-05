using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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

        /// <summary>Разрешённая сама по себе сетка, содержащая ссылки на запрещённые SDK.</summary>
        public bool IsCarrier;

        /// <summary>Префиксы Java-пакетов в DEX descriptors; только семь запрещённых SDK.</summary>
        public string[] DexTypePrefixes = Array.Empty<string>();

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
        public const string PolicyVersion = "amazon-prohibited-sdk-2";
        private const string MaxMediationGroup = "com.applovin.mediation";

        public static readonly IReadOnlyList<ForbiddenAdNetwork> All = new List<ForbiddenAdNetwork>
        {
            new ForbiddenAdNetwork
            {
                DisplayName = "Tapjoy",
                DexTypePrefixes = new[] { "com/tapjoy/" },
                MavenGroups = new[] { "com.tapjoy" },
                Artifacts = new[] { new ForbiddenArtifact(MaxMediationGroup, "tapjoy-adapter") },
                AdapterFolderNames = new[] { "Tapjoy" },
                UpmPackageTokens = new[] { "mediation.adapters.tapjoy." },
            },
            new ForbiddenAdNetwork
            {
                // MoPub закрыт в 2022 (поглощён AppLovin), актуального адаптера MAX нет.
                // Держим в списке на случай legacy-зависимости в чужом плагине.
                DisplayName = "MoPub",
                DexTypePrefixes = new[] { "com/mopub/" },
                MavenGroups = new[] { "com.mopub" },
                Artifacts = new[] { new ForbiddenArtifact(MaxMediationGroup, "mopub-adapter") },
                AdapterFolderNames = new[] { "MoPub" },
                UpmPackageTokens = new[] { "mediation.adapters.mopub." },
            },
            new ForbiddenAdNetwork
            {
                // Inneractive → Fyber → DT Exchange: одна сетка под тремя именами в разные
                // годы, артефакты встречаются под всеми тремя.
                DisplayName = "Inneractive / Fyber / DT Exchange",
                DexTypePrefixes = new[] { "com/fyber/", "com/inneractive/" },
                MavenGroups = new[] { "com.fyber", "com.inneractive", "com.digitalturbine" },
                Artifacts = new[]
                {
                    new ForbiddenArtifact(MaxMediationGroup, "fyber-adapter"),
                    new ForbiddenArtifact(MaxMediationGroup, "inneractive-adapter"),
                    new ForbiddenArtifact(MaxMediationGroup, "dtexchange-adapter"),
                    new ForbiddenArtifact("io.appmetrica.analytics", "analytics-ad-revenue-fyber-v3"),
                },
                UpmPackageTokens = new[] { "mediation.adapters.fyber.", "mediation.adapters.inneractive.", "mediation.adapters.dtexchange." },
                AdapterFolderNames = new[] { "Fyber", "Inneractive", "DTExchange" },
            },
            new ForbiddenAdNetwork
            {
                DisplayName = "Appnext",
                DexTypePrefixes = new[] { "com/appnext/" },
                MavenGroups = new[] { "com.appnext", "com.appnext.sdk", "com.appnext.sdk.adapters" },
                Artifacts = new[] { new ForbiddenArtifact(MaxMediationGroup, "appnext-adapter") },
                AdapterFolderNames = new[] { "Appnext" },
                UpmPackageTokens = new[] { "mediation.adapters.appnext." },
            },
            new ForbiddenAdNetwork
            {
                DisplayName = "Amplitude",
                DexTypePrefixes = new[] { "com/amplitude/" },
                MavenGroups = new[] { "com.amplitude" },
            },
            new ForbiddenAdNetwork
            {
                DisplayName = "Flurry",
                DexTypePrefixes = new[] { "com/flurry/" },
                MavenGroups = new[] { "com.flurry", "com.flurry.android" },
                Artifacts = new[] { new ForbiddenArtifact(MaxMediationGroup, "flurry-adapter") },
                AdapterFolderNames = new[] { "Flurry" },
                UpmPackageTokens = new[] { "mediation.adapters.flurry." },
            },
            new ForbiddenAdNetwork
            {
                DisplayName = "Branch",
                DexTypePrefixes = new[] { "io/branch/" },
                MavenGroups = new[] { "io.branch", "io.branch.sdk.android" },
            },
            new ForbiddenAdNetwork
            {
                // Ad Quality содержит ссылки на Tapjoy/Fyber. Удаляется вся цепочка:
                // исключать только adquality-sdk небезопасно для runtime ironSource.
                DisplayName = "ironSource / LevelPlay (источник ссылок на запрещённые SDK)",
                IsCarrier = true,
                Artifacts = new[]
                {
                    new ForbiddenArtifact(MaxMediationGroup, "ironsource-adapter"),
                    new ForbiddenArtifact("ironsource.sdk", "mediationsdk"),
                    new ForbiddenArtifact("com.ironsource.sdk", "mediationsdk"),
                    new ForbiddenArtifact("com.unity3d.ads-mediation", "mediation-sdk"),
                    new ForbiddenArtifact("com.unity3d.ads-mediation", "adquality-sdk"),
                    new ForbiddenArtifact("com.ironsource", "adquality-sdk"),
                    new ForbiddenArtifact("com.ironsource", "adqualitysdk"),
                },
                UpmPackageTokens = new[] { "mediation.adapters.ironsource." },
                AdapterFolderNames = new[] { "IronSource" },
            },
            new ForbiddenAdNetwork
            {
                DisplayName = "Unity Ads (источник reflection-ссылок на Tapjoy)",
                IsCarrier = true,
                Artifacts = new[]
                {
                    new ForbiddenArtifact(MaxMediationGroup, "unityads-adapter"),
                    new ForbiddenArtifact("com.unity3d.ads", "unity-ads"),
                },
                UpmPackageTokens = new[] { "mediation.adapters.unityads." },
                AdapterFolderNames = new[] { "UnityAds" },
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
                // Google AdMob входит в обязательный комплект MAX; AndroidBuildPreflight
                // проверяет его Android App ID до сборки. Google Ad Manager остаётся исключён.
                // Группу com.google.android.gms целиком запрещать нельзя: она содержит
                // разрешённые зависимости AdMob, Firebase и GAID для MAX.
                DisplayName = "Google Ad Manager",
                Artifacts = new[] { new ForbiddenArtifact(MaxMediationGroup, "google-ad-manager-adapter") },
                UpmPackageTokens = new[] { "mediation.adapters.googleadmanager." },
                AdapterFolderNames = new[] { "GoogleAdManager" },
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

            string[] coordinate = value.Split(':');
            if (coordinate.Length >= 2 && Regex.IsMatch(coordinate[0], @"^[a-zA-Z0-9_.-]+$"))
                return MatchMavenCoordinate(coordinate[0], coordinate[1]);

            foreach (var network in All)
            {
                foreach (var group in network.MavenGroups)
                {
                    if (ContainsIdentity(value, group))
                        return network;
                }

                foreach (var artifact in network.Artifacts)
                {
                    if (ContainsIdentity(value, artifact.Group) && ContainsIdentity(value, artifact.Module))
                        return network;
                }

                foreach (var token in network.UpmPackageTokens)
                {
                    if (value.StartsWith("com.applovin." + token, StringComparison.OrdinalIgnoreCase))
                        return network;
                }
            }

            return null;
        }

        /// <summary>Точное совпадение Maven-координат, без поиска подстрок.</summary>
        public static ForbiddenAdNetwork MatchMavenCoordinate(string group, string module)
        {
            foreach (var network in All)
            {
                foreach (string forbiddenGroup in network.MavenGroups)
                    if (string.Equals(group, forbiddenGroup, StringComparison.OrdinalIgnoreCase)) return network;
                foreach (var artifact in network.Artifacts)
                    if (string.Equals(group, artifact.Group, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(module, artifact.Module, StringComparison.OrdinalIgnoreCase)) return network;
            }
            return null;
        }

        /// <summary>Необязательный reflection-модуль, который безопасно исключается поздно в Gradle.</summary>
        public static bool IsOptionalArtifact(string group, string module)
        {
            return group == "io.appmetrica.analytics" && module == "analytics-ad-revenue-fyber-v3";
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

        private static bool ContainsIdentity(string haystack, string needle)
        {
            return Regex.IsMatch(haystack, @"(^|[^a-zA-Z0-9])" + Regex.Escape(needle) + @"(?=$|[^a-zA-Z0-9])",
                RegexOptions.IgnoreCase);
        }
    }
}
